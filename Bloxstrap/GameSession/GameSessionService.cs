using Bloxstrap.GameSession.Models;

namespace Bloxstrap.GameSession
{
    public sealed class GameSessionService
    {
        private const string LOG_IDENT = "GameSession";

        // Serialize recovery attempts so the same saved session cannot be restored twice.
        private readonly SemaphoreSlim _sessionLock = new(1, 1);
        private static readonly TimeSpan SessionLockTimeout = TimeSpan.FromSeconds(30);

        private readonly Func<int, bool> _isProcessAlive;

        public SecuritySoftwareDetector Detector { get; }
        private ProcessSuspensionService Suspension { get; }
        public GameSessionStore Store { get; }

        public GameSessionService(
            SecuritySoftwareDetector? detector = null,
            ProcessSuspensionService? suspension = null,
            GameSessionStore? store = null,
            Func<int, bool>? isProcessAlive = null)
        {
            Detector = detector ?? new SecuritySoftwareDetector();
            Suspension = suspension ?? new ProcessSuspensionService();
            Store = store ?? new GameSessionStore();
            _isProcessAlive = isProcessAlive ?? IsProcessAlive;
        }

        public IReadOnlyList<RescuedProcess> RescueSuspendedProcesses()
        {
            return Suspension.RescueSuspendedProcesses();
        }


        public SessionSummary EndSession(int? expectedGameProcessId = null)
        {
            if (!_sessionLock.Wait(SessionLockTimeout))
            {
                App.Logger.WriteLine(LOG_IDENT, "EndSession timeout — active session tetap tersimpan untuk recovery berikutnya.");
                GameSessionRecord? session = Store.ReadActive();
                return session is null
                    ? new SessionSummary { EndedAtUtc = DateTime.UtcNow }
                    : new SessionSummary
                    {
                        SessionId = session.SessionId,
                        GameProcessId = session.GameProcessId,
                        StartedAtUtc = session.StartedAtUtc,
                        EndedAtUtc = DateTime.UtcNow,
                        TotalSuspended = session.SuspendedProcesses.Count
                    };
            }

            try
            {
                return EndSessionCore(expectedGameProcessId);
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private SessionSummary EndSessionCore(int? expectedGameProcessId)
        {
            const string LOG_IDENT_LOCAL = "GameSession::EndSession";
            GameSessionRecord? session = Store.ReadActive();

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

    }
}
