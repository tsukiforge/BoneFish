using Bloxstrap.GameSession.Models;

namespace Bloxstrap.GameSession
{
    public sealed class ProcessSuspendResult
    {
        public List<int> SuspendedThreadIds { get; init; } = new();
        public int TotalThreadCount { get; init; }
        public int FailedThreadCount { get; init; }
        public bool PartiallySuspended { get; init; }
        public int SweepPasses { get; init; }
    }

    public sealed class ProcessSuspensionService
    {
        public const int MaxSweepPasses = 5;
        public static readonly TimeSpan SweepTimeoutPerProcess = TimeSpan.FromSeconds(2);

        private readonly Func<int, IProcessAccessor> _accessorFactory;

        public ProcessSuspensionService(Func<int, IProcessAccessor>? accessorFactory = null)
        {
            _accessorFactory = accessorFactory ?? (processId => new Win32ProcessAccessor(processId));
        }

        public ProcessSuspendResult SuspendProcess(int processId, CancellationToken cancellationToken = default)
        {
            const string LOG_IDENT = "GameSession::SuspendProcess";
            var result = new ProcessSuspendResultBuilder();
            var stopwatch = Stopwatch.StartNew();
            var suspendedThreadIds = new HashSet<int>();
            var failedThreadIds = new HashSet<int>();
            int pass = 0;
            bool reachedPassLimit = false;

            try
            {
                using IProcessAccessor accessor = _accessorFactory(processId);

                for (pass = 1; pass <= MaxSweepPasses; pass++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (stopwatch.Elapsed >= SweepTimeoutPerProcess)
                    {
                        reachedPassLimit = true;
                        break;
                    }

                    IReadOnlyCollection<int> currentThreadIds = accessor.GetThreadIds();
                    var pendingThreadIds = currentThreadIds
                        .Where(threadId => !suspendedThreadIds.Contains(threadId) && !failedThreadIds.Contains(threadId))
                        .ToArray();

                    if (pendingThreadIds.Length == 0)
                        break;

                    foreach (int threadId in pendingThreadIds)
                    {
                        if (stopwatch.Elapsed >= SweepTimeoutPerProcess)
                        {
                            reachedPassLimit = true;
                            break;
                        }

                        if (accessor.TrySuspendThread(threadId))
                            suspendedThreadIds.Add(threadId);
                        else
                            failedThreadIds.Add(threadId);
                    }

                    if (pass == MaxSweepPasses)
                        reachedPassLimit = true;

                    // Give a process a scheduling opportunity to finish creating threads.
                    Thread.Yield();
                }

                IReadOnlyCollection<int> finalThreadIds = accessor.GetThreadIds();
                bool unresolvedThreads = finalThreadIds.Any(threadId =>
                    !suspendedThreadIds.Contains(threadId) && !failedThreadIds.Contains(threadId));

                int totalThreadCount = Math.Max(
                    finalThreadIds.Count,
                    suspendedThreadIds.Count + failedThreadIds.Count);

                result.SuspendedThreadIds.AddRange(suspendedThreadIds);
                result.TotalThreadCount = totalThreadCount;
                result.FailedThreadCount = failedThreadIds.Count;
                result.PartiallySuspended = failedThreadIds.Count > 0 || (reachedPassLimit && unresolvedThreads);
                result.SweepPasses = Math.Min(pass, MaxSweepPasses);

                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"PID={processId}: {result.SuspendedThreadIds.Count}/{totalThreadCount} threads suspended; " +
                    $"failed={result.FailedThreadCount}; passes={result.SweepPasses}; " +
                    $"partial={result.PartiallySuspended}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID={processId}: suspend skipped: {ex.Message}");

                // A thread may already have been suspended before enumeration failed. Return
                // those IDs so GameSessionService can persist and restore them.
                result.SuspendedThreadIds.AddRange(suspendedThreadIds);
                result.TotalThreadCount = suspendedThreadIds.Count + failedThreadIds.Count;
                result.FailedThreadCount = failedThreadIds.Count;
                result.PartiallySuspended = suspendedThreadIds.Count > 0;
                result.SweepPasses = Math.Min(Math.Max(pass, 1), MaxSweepPasses);
            }

            return result.Build();
        }

        public RestoreResult RestoreProcess(SuspendedProcessRecord record)
        {
            const string LOG_IDENT = "GameSession::RestoreProcess";

            try
            {
                using IProcessAccessor accessor = _accessorFactory(record.ProcessId);

                if (record.StartTimeUtc.HasValue)
                {
                    DateTime? currentStart = accessor.GetStartTimeUtc();
                    if (!currentStart.HasValue || Math.Abs((currentStart.Value - record.StartTimeUtc.Value).TotalSeconds) > 1)
                    {
                        return Failed(record, RestoreStatus.IdentityMismatch,
                            "PID sekarang milik proses lain; resume dibatalkan.");
                    }
                }

                int resumeFailures = 0;
                foreach (int threadId in record.ThreadIds.Distinct())
                {
                    if (!accessor.IsAlive)
                        return Failed(record, RestoreStatus.NotFound,
                            "Proses sudah ditutup manual sebelum restore.");

                    if (!accessor.TryResumeThread(threadId))
                    {
                        // A thread can disappear during a normal process shutdown. Do not turn
                        // that into a hard failure if the process no longer reports that thread.
                        if (!accessor.GetThreadIds().Contains(threadId))
                            continue;

                        resumeFailures++;
                    }
                }

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!accessor.IsAlive)
                    {
                        return Failed(record, RestoreStatus.NotFound,
                            "Proses sudah ditutup manual saat verifikasi restore.");
                    }

                    Thread.Sleep(100);

                    bool stillSuspended = record.ThreadIds
                        .Distinct()
                        .Any(threadId => accessor.GetThreadIds().Contains(threadId) && accessor.IsThreadSuspended(threadId));

                    if (!stillSuspended && resumeFailures == 0)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"PID={record.ProcessId} ({record.ProcessName}) restored and verified.");
                        return new RestoreResult
                        {
                            ProcessName = record.ProcessName,
                            Status = RestoreStatus.Restored,
                            Message = "Proses kembali berjalan dan terverifikasi."
                        };
                    }

                    if (stillSuspended)
                    {
                        foreach (int threadId in record.ThreadIds.Distinct())
                            accessor.TryResumeThread(threadId);
                    }
                }

                return Failed(record, resumeFailures > 0 ? RestoreStatus.ResumeFailed : RestoreStatus.VerificationFailed,
                    resumeFailures > 0
                        ? $"{resumeFailures} thread gagal di-resume."
                        : "Thread masih suspended setelah verifikasi ulang.");
            }
            catch (ArgumentException)
            {
                return Failed(record, RestoreStatus.NotFound,
                    "Proses tidak ditemukan (mungkin sudah ditutup manual).");
            }
            catch (InvalidOperationException)
            {
                return Failed(record, RestoreStatus.NotFound,
                    "Proses sudah tidak berjalan saat restore.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return Failed(record, RestoreStatus.ResumeFailed, ex.Message);
            }
        }

        private static RestoreResult Failed(SuspendedProcessRecord record, RestoreStatus status, string message)
        {
            App.Logger.WriteLine("GameSession::RestoreProcess",
                $"PID={record.ProcessId} ({record.ProcessName}) restore status={status}: {message}");

            return new RestoreResult
            {
                ProcessName = record.ProcessName,
                Status = status,
                Message = message
            };
        }

        private sealed class ProcessSuspendResultBuilder
        {
            public List<int> SuspendedThreadIds { get; } = new();
            public int TotalThreadCount { get; set; }
            public int FailedThreadCount { get; set; }
            public bool PartiallySuspended { get; set; }
            public int SweepPasses { get; set; }

            public ProcessSuspendResult Build() => new()
            {
                SuspendedThreadIds = SuspendedThreadIds,
                TotalThreadCount = TotalThreadCount,
                FailedThreadCount = FailedThreadCount,
                PartiallySuspended = PartiallySuspended,
                SweepPasses = SweepPasses
            };
        }
    }
}
