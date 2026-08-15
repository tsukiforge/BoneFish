using Bloxstrap.GameSession.Models;

namespace Bloxstrap.GameSession
{
    public sealed class GameSessionService
    {
        private const string LOG_IDENT = "GameSession";

        private readonly Func<IEnumerable<ProcessSnapshot>> _processSource;
        private readonly Func<int, bool> _isProcessAlive;
        private readonly Func<ICollection<GameSessionRule>> _rulesSource;

        public SecuritySoftwareDetector Detector { get; }
        public ProcessSuspensionService Suspension { get; }
        public GameSessionStore Store { get; }
        public GameSessionRecord? ActiveSession { get; private set; }

        public GameSessionService(
            SecuritySoftwareDetector? detector = null,
            ProcessSuspensionService? suspension = null,
            GameSessionStore? store = null,
            Func<IEnumerable<ProcessSnapshot>>? processSource = null,
            Func<int, bool>? isProcessAlive = null,
            Func<ICollection<GameSessionRule>>? rulesSource = null)
        {
            Detector = detector ?? new SecuritySoftwareDetector();
            Suspension = suspension ?? new ProcessSuspensionService();
            Store = store ?? new GameSessionStore();
            _processSource = processSource ?? ScanProcesses;
            _isProcessAlive = isProcessAlive ?? IsProcessAlive;
            _rulesSource = rulesSource ?? (() => App.Settings.Prop.GameSessionRules);
        }

        public async Task<GameSessionRecord> BeginSessionAsync(CancellationToken cancellationToken = default)
        {
            const string LOG_IDENT_LOCAL = "GameSession::BeginSession";

            if (Store.ReadActive() is { } existing)
            {
                if (ShouldRestoreStale(existing))
                    EndSession();

                if (Store.ReadActive() is not null)
                    throw new InvalidOperationException("A previous Game Session still has processes pending restore.");
            }

            SecurityDetectionState detectorState = await Detector.RefreshAsync(cancellationToken);
            List<ProcessSnapshot> processes = _processSource().ToList();
            var session = new GameSessionRecord
            {
                CoordinatorProcessId = Environment.ProcessId,
                DetectorState = detectorState,
                DetectorMessage = Detector.Message
            };

            // Persist before the first mutation so an interruption still leaves a recovery record.
            ActiveSession = session;
            Store.WriteActive(session);

            ICollection<GameSessionRule> storedRules = _rulesSource();
            bool rulesChanged = false;

            if (App.Settings.Prop.GameSessionAutoSelectSafeApps && detectorState == SecurityDetectionState.Ok)
            {
                foreach (ProcessSnapshot process in processes)
                {
                    if (!ProcessClassifier.IsAutomaticCandidate(process, Detector, Environment.ProcessId, 0))
                        continue;

                    GameSessionRule? existingRule = FindRule(process, storedRules
                        .Where(rule => true)
                        .GroupBy(RuleKey)
                        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase));

                    if (existingRule is null)
                    {
                        storedRules.Add(new GameSessionRule
                        {
                            ProcessName = process.ProcessName,
                            ExecutablePath = process.ExecutablePath,
                            SuspendDuringGame = true
                        });
                        rulesChanged = true;
                    }
                    else if (!existingRule.AutoSelectionDisabled && !existingRule.SuspendDuringGame)
                    {
                        existingRule.SuspendDuringGame = true;
                        rulesChanged = true;
                    }
                }
            }

            if (rulesChanged)
            {
                try { App.Settings.Save(); } catch { }
            }

            Dictionary<string, GameSessionRule> rules = storedRules
                .Where(rule => rule.SuspendDuringGame)
                .GroupBy(rule => RuleKey(rule))
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (ProcessSnapshot process in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    GameSessionRule? rule = FindRule(process, rules);
                    if (rule is null || !rule.SuspendDuringGame)
                        continue;

                    ProcessClassification classification = ProcessClassifier.Classify(
                        process,
                        Detector,
                        Environment.ProcessId,
                        0,
                        rule);

                    if (classification != ProcessClassification.Safe)
                    {
                        App.Logger.WriteLine(LOG_IDENT_LOCAL,
                            $"Skip PID={process.ProcessId} ({process.ProcessName}): classified {classification}; detector={detectorState}");
                        continue;
                    }

                    ProcessSuspendResult result = Suspension.SuspendProcess(process.ProcessId, cancellationToken);
                    if (result.SuspendedThreadIds.Count == 0)
                        continue;

                    session.AppliedRules.Add(RuleKey(rule));
                    session.SuspendedProcesses.Add(new SuspendedProcessRecord
                    {
                        ProcessId = process.ProcessId,
                        SessionId = process.SessionId,
                        ProcessName = process.ProcessName,
                        ExecutablePath = process.ExecutablePath,
                        StartTimeUtc = process.StartTimeUtc,
                        AppliedRule = RuleKey(rule),
                        ThreadIds = result.SuspendedThreadIds,
                        TotalThreadCount = result.TotalThreadCount,
                        SuspendedThreadCount = result.SuspendedThreadIds.Count,
                        FailedThreadCount = result.FailedThreadCount,
                        PartiallySuspended = result.PartiallySuspended
                    });

                    Store.WriteActive(session);
                    App.Logger.WriteLine(
                        LOG_IDENT_LOCAL,
                        $"{process.ProcessName} ter-suspend {result.SuspendedThreadIds.Count}/{result.TotalThreadCount} thread" +
                        (result.PartiallySuspended ? " (PartiallySuspended)" : ""));
                }
            }
            catch
            {
                EndSession();
                throw;
            }

            if (session.SuspendedProcesses.Count > 0)
                Store.WriteActive(session);
            else
            {
                Store.ClearActive();
                ActiveSession = null;
            }

            App.Logger.WriteLine(
                LOG_IDENT_LOCAL,
                $"detector={session.DetectorState}; suspended={session.SuspendedProcesses.Count}; " +
                "unapproved processes were not touched");

            return session;
        }

        public IReadOnlyList<RescuedProcess> RescueSuspendedProcesses()
        {
            return Suspension.RescueSuspendedProcesses();
        }

        public void AttachGameProcess(int processId)
        {
            if (ActiveSession is null)
                return;

            ActiveSession.GameProcessId = processId;
            if (ActiveSession.SuspendedProcesses.Count > 0)
                Store.WriteActive(ActiveSession);
        }

        public void MarkHandedOffToWatcher()
        {
            if (ActiveSession is null || ActiveSession.SuspendedProcesses.Count == 0)
                return;

            ActiveSession.HandedOffToWatcher = true;
            Store.WriteActive(ActiveSession);
        }

        public SessionSummary EndSession(int? expectedGameProcessId = null)
        {
            const string LOG_IDENT_LOCAL = "GameSession::EndSession";
            GameSessionRecord? session = ActiveSession ?? Store.ReadActive();

            if (session is null)
                return new SessionSummary { EndedAtUtc = DateTime.UtcNow };

            if (expectedGameProcessId.HasValue
                && session.GameProcessId != 0
                && session.GameProcessId != expectedGameProcessId.Value)
            {
                App.Logger.WriteLine(LOG_IDENT_LOCAL,
                    $"Refusing restore for mismatched game PID. expected={expectedGameProcessId}, stored={session.GameProcessId}");
                return new SessionSummary
                {
                    SessionId = session.SessionId,
                    GameProcessId = session.GameProcessId,
                    StartedAtUtc = session.StartedAtUtc,
                    EndedAtUtc = DateTime.UtcNow,
                    TotalSuspended = session.SuspendedProcesses.Count
                };
            }

            var summary = new SessionSummary
            {
                SessionId = session.SessionId,
                GameProcessId = session.GameProcessId,
                StartedAtUtc = session.StartedAtUtc,
                EndedAtUtc = DateTime.UtcNow,
                TotalSuspended = session.SuspendedProcesses.Count
            };

            session.RestoreState = SessionRestoreState.Restoring;

            foreach (SuspendedProcessRecord process in session.SuspendedProcesses)
            {
                RestoreResult result = Suspension.RestoreProcess(process);
                summary.Results.Add(result);

                if (result.Succeeded)
                    summary.RestoredCount++;
            }

            Store.AppendHistory(summary);
            var pending = new List<SuspendedProcessRecord>();
            for (int index = 0; index < session.SuspendedProcesses.Count; index++)
            {
                if (summary.Results[index].Status is RestoreStatus.ResumeFailed or RestoreStatus.VerificationFailed)
                    pending.Add(session.SuspendedProcesses[index]);
            }

            if (pending.Count > 0)
            {
                session.SuspendedProcesses = pending;
                session.GameProcessId = 0;
                session.HandedOffToWatcher = false;
                session.RestoreState = SessionRestoreState.Pending;
                Store.WriteActive(session);
            }
            else
            {
                session.RestoreState = SessionRestoreState.Restored;
                Store.ClearActive();
            }

            ActiveSession = null;

            App.Logger.WriteLine(LOG_IDENT_LOCAL, FormatSummary(summary));
            return summary;
        }

        public bool ShouldRestoreStale(GameSessionRecord session)
        {
            if (session.GameProcessId == 0)
                return session.CoordinatorProcessId == 0 || !_isProcessAlive(session.CoordinatorProcessId);

            if (!_isProcessAlive(session.GameProcessId))
                return true;

            if (!session.HandedOffToWatcher)
                return true;

            string watcherPidPath = Path.Combine(Paths.Base, "Watcher.pid");
            if (!File.Exists(watcherPidPath)
                || !Int32.TryParse(File.ReadAllText(watcherPidPath).Trim(), out int watcherPid))
            {
                return true;
            }

            return !_isProcessAlive(watcherPid);
        }

        public string FormatSummary(SessionSummary summary)
        {
            if (summary.TotalSuspended == 0)
                return "Tidak ada proses yang disuspend.";

            string restoredNames = String.Join(", ", summary.Results.Where(result => result.Succeeded).Select(result => result.ProcessName));
            string failed = String.Join(" ", summary.Results
                .Where(result => !result.Succeeded)
                .Select(result => $"{result.ProcessName}: {result.Message}"));

            if (summary.RestoredCount == summary.TotalSuspended)
                return String.Format(Strings.GameSession_RestoredAll, summary.RestoredCount, restoredNames);

            return String.Format(Strings.GameSession_RestoredPartial, summary.RestoredCount, summary.TotalSuspended, failed).Trim();
        }

        public IReadOnlyList<ProcessSnapshot> ScanForUi()
        {
            return _processSource()
                .Where(process => process.ProcessId != Environment.ProcessId)
                .Where(process => !String.IsNullOrWhiteSpace(process.ProcessName))
                .Where(process => !ProcessClassifier.IsCritical(process, Detector, Environment.ProcessId, 0))
                .GroupBy(process => RuleKey(process), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static IEnumerable<ProcessSnapshot> ScanProcesses()
        {
            var snapshots = new List<ProcessSnapshot>();

            foreach (Process process in Utilities.GetProcessesSafe())
            {
                try
                {
                    string? executablePath = null;
                    DateTime? startTime = null;

                    try { executablePath = process.MainModule?.FileName; } catch { }
                    try { startTime = process.StartTime.ToUniversalTime(); } catch { }

                    snapshots.Add(new ProcessSnapshot
                    {
                        ProcessId = process.Id,
                        SessionId = TryGetSessionId(process),
                        ProcessName = process.ProcessName,
                        ExecutablePath = executablePath,
                        StartTimeUtc = startTime
                    });
                }
                catch
                {
                    // Process exited or is protected between enumeration and inspection.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return snapshots;
        }

        private static GameSessionRule? FindRule(ProcessSnapshot process, IReadOnlyDictionary<string, GameSessionRule> rules)
        {
            if (!String.IsNullOrWhiteSpace(process.ExecutablePath)
                && rules.TryGetValue(process.ExecutablePath, out GameSessionRule? pathRule))
            {
                return pathRule;
            }

            rules.TryGetValue(process.ProcessName, out GameSessionRule? nameRule);
            return nameRule;
        }

        private static string RuleKey(GameSessionRule rule)
        {
            return !String.IsNullOrWhiteSpace(rule.ExecutablePath)
                ? rule.ExecutablePath
                : rule.ProcessName;
        }

        private static string RuleKey(ProcessSnapshot process)
        {
            return !String.IsNullOrWhiteSpace(process.ExecutablePath)
                ? process.ExecutablePath
                : process.ProcessName;
        }

        private static bool IsProcessAlive(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static int TryGetSessionId(Process process)
        {
            try { return process.SessionId; }
            catch { return -1; }
        }
    }
}
