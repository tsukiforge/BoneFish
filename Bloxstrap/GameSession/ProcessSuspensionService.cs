using System.Diagnostics;
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

        /// <summary>
        /// v7.6.3 — true bila proses di-suspend secara atomik via NtSuspendProcess.
        /// ThreadIds yang tercatat saat itu hanya snapshot untuk verifikasi; resume
        /// memakai jalur level proses, bukan daftar thread ini.
        /// </summary>
        public bool ProcessLevelSuspend { get; init; }
    }

    public sealed class RescuedProcess
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = "";
        public int ThreadCount { get; init; }
    }

    public sealed class ProcessSuspensionService
    {
        public const int MaxSweepPasses = 5;
        public static readonly TimeSpan SweepTimeoutPerProcess = TimeSpan.FromSeconds(2);

        private readonly Func<int, IProcessAccessor> _accessorFactory;
        private readonly Func<IEnumerable<ProcessSnapshot>> _processSource;

        public ProcessSuspensionService(
            Func<int, IProcessAccessor>? accessorFactory = null,
            Func<IEnumerable<ProcessSnapshot>>? processSource = null)
        {
            _accessorFactory = accessorFactory ?? (processId => new Win32ProcessAccessor(processId));
            _processSource = processSource ?? DefaultProcessSource;
        }

        private static IEnumerable<ProcessSnapshot> DefaultProcessSource()
        {
            var snapshots = new List<ProcessSnapshot>();

            foreach (Process process in Utilities.GetProcessesSafe())
            {
                try
                {
                    snapshots.Add(new ProcessSnapshot
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName
                    });
                }
                catch
                {
                    // Proses berubah di antara enumerasi — skip.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return snapshots;
        }

        /// <summary>
        /// v7.6.3 FIX — jalur utama sekarang NtSuspendProcess (atomik). Versi lama
        /// men-suspend per-thread dengan maksimal 5 sweep pass; aplikasi yang terus
        /// menambah thread (browser, launcher, Discord) selalu punya thread baru yang
        /// lolos antar sweep, sehingga proses "tetap berjalan seperti biasa" padahal
        /// statusnya katanya suspended. NtSuspendProcess membuat kernel menahan semua
        /// thread — termasuk yang lahir selama operasi — tanpa race.
        /// </summary>
        public ProcessSuspendResult SuspendProcess(int processId, CancellationToken cancellationToken = default)
        {
            const string LOG_IDENT = "GameSession::SuspendProcess";
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using IProcessAccessor accessor = _accessorFactory(processId);
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyCollection<int> threadSnapshot = accessor.GetThreadIds();

                if (accessor.TrySuspendProcess())
                {
                    // Verifikasi: probe beberapa thread untuk memastikan benar-benar
                    // tersuspend. Bila NtSuspendProcess sukses, semua thread pasti
                    // suspend count >= 1.
                    int verified = 0;
                    int probeBudget = Math.Min(threadSnapshot.Count, 8);
                    foreach (int threadId in threadSnapshot.Take(probeBudget))
                    {
                        if (accessor.IsThreadSuspended(threadId))
                            verified++;
                    }

                    App.Logger.WriteLine(
                        LOG_IDENT,
                        $"PID={processId}: process-level suspend OK; threads~{threadSnapshot.Count}; verified={verified}/{probeBudget}");

                    return new ProcessSuspendResult
                    {
                        SuspendedThreadIds = threadSnapshot.ToList(),
                        TotalThreadCount = threadSnapshot.Count,
                        FailedThreadCount = 0,
                        PartiallySuspended = false,
                        SweepPasses = 1,
                        ProcessLevelSuspend = true
                    };
                }

                // Process-level gagal (mis. ntdll tidak tersedia) — jalur lama per-thread.
                App.Logger.WriteLine(LOG_IDENT, $"PID={processId}: process-level suspend unavailable — falling back to per-thread sweep");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID={processId}: process-level suspend failed: {ex.Message} — falling back to per-thread sweep");
            }

            return SuspendProcessPerThread(processId, cancellationToken);
        }

        /// <summary>
        /// Jalur lama (v7.6.2 dan sebelumnya): suspend per-thread dengan sweep.
        /// Dipertahankan sebagai fallback dan untuk test/diagnostik.
        /// </summary>
        public ProcessSuspendResult SuspendProcessPerThread(int processId, CancellationToken cancellationToken = default)
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

        /// <summary>
        /// Rescue scan: pindai SEMUA proses untuk thread yang masih tersuspend dan
        /// resume yang ketemu. Dipakai sebagai jaring pengaman manual saat catatan
        /// sesi (active.json) sudah hilang — misalnya restore lama menganggap sukses
        /// tapi sebagian thread tertinggal tersuspend, atau session di-end tanpa
        /// pernah me-restore. Hanya dipanggil atas inisiatif user (tombol tray/page),
        /// bukan otomatis, karena probe per-thread memakan waktu.
        /// </summary>
        public IReadOnlyList<RescuedProcess> RescueSuspendedProcesses()
        {
            const string LOG_IDENT = "GameSession::RescueSuspendedProcesses";

            var rescued = new List<RescuedProcess>();

            foreach (ProcessSnapshot process in _processSource())
            {
                if (process.ProcessId == Environment.ProcessId)
                    continue;

                try
                {
                    using IProcessAccessor accessor = _accessorFactory(process.ProcessId);
                    int threadCount = 0;

                    foreach (int threadId in accessor.GetThreadIds())
                    {
                        // IsThreadSuspended mem-probe lalu mengembalikan state;
                        // kalau thread benar-benar tersuspend, resume untuk selamanya.
                        if (accessor.IsThreadSuspended(threadId) && accessor.TryResumeThread(threadId))
                            threadCount++;
                    }

                    if (threadCount > 0)
                    {
                        rescued.Add(new RescuedProcess
                        {
                            ProcessId = process.ProcessId,
                            ProcessName = process.ProcessName,
                            ThreadCount = threadCount
                        });
                        App.Logger.WriteLine(LOG_IDENT,
                            $"PID={process.ProcessId} ({process.ProcessName}): {threadCount} thread di-resume (rescue scan)");
                    }
                }
                catch
                {
                    // Proses protected/berubah di antara enumerasi — skip, bukan error.
                }
            }

            return rescued;
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

                if (!accessor.IsAlive)
                    return Failed(record, RestoreStatus.NotFound,
                        "Proses sudah ditutup manual sebelum restore.");

                // ── v7.6.3 FIX — resume level proses dulu ───────────────────────────
                // NtResumeProcess membalikkan NtSuspendProcess secara atomik: kernel
                // menurunkan suspend count SETIAP thread, termasuk thread yang lahir
                // setelah suspend. Ini menutup celah lama di mana thread baru (spawn
                // saat proses terlanjur disuspend per-thread, atau app yang restart
                // subprocess-nya sendiri) tidak pernah masuk daftar ThreadIds dan
                // tertinggal beku — serta pola "VerificationFailed" pada app yang
                // spawn thread baru saat direstore.
                if (accessor.SupportsProcessLevelControl && accessor.TryResumeProcess())
                {
                    // Verifikasi ringkas: thread snapshot lama tidak boleh ada yang
                    // masih tersuspend. Thread yang sudah mati dihitung sukses.
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        if (!accessor.IsAlive)
                            return Failed(record, RestoreStatus.NotFound,
                                "Proses sudah ditutup manual saat verifikasi restore.");

                        Thread.Sleep(100);

                        IReadOnlyCollection<int> currentThreadIds = accessor.GetThreadIds();
                        bool stillSuspended = record.ThreadIds
                            .Distinct()
                            .Any(threadId => currentThreadIds.Contains(threadId) && accessor.IsThreadSuspended(threadId));

                        if (!stillSuspended)
                        {
                            App.Logger.WriteLine(LOG_IDENT,
                                $"PID={record.ProcessId} ({record.ProcessName}) restored via process-level resume and verified.");
                            return new RestoreResult
                            {
                                ProcessName = record.ProcessName,
                                Status = RestoreStatus.Restored,
                                Message = "Proses kembali berjalan dan terverifikasi."
                            };
                        }

                        // Thread yang masih tersuspend punya suspend count > 1 (pernah
                        // di-suspend dua kali) — resume sekali lagi.
                        foreach (int threadId in record.ThreadIds.Distinct())
                            accessor.TryResumeThread(threadId);
                    }

                    return Failed(record, RestoreStatus.VerificationFailed,
                        "Process-level resume dijalankan namun sebagian thread masih tersuspend.");
                }

                // ── Fallback jalur lama: resume per-thread berdasarkan catatan ──────
                int resumeFailures = 0;
                int initialThreadCount = record.ThreadIds.Distinct().Count();

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

                // Snapshot current thread count for logging (new threads spawned between
                // suspend and restore would explain SynTPEnh's VerificationFailed pattern).
                IReadOnlyCollection<int> currentThreadIdsFallback = accessor.GetThreadIds();
                int currentThreadCount = currentThreadIdsFallback.Count;
                int newThreadCount = currentThreadCount - initialThreadCount;

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!accessor.IsAlive)
                    {
                        return Failed(record, RestoreStatus.NotFound,
                            "Proses sudah ditutup manual saat verifikasi restore.");
                    }

                    Thread.Sleep(100);

                    // Re-read thread list — threads may have appeared/disappeared.
                    currentThreadIdsFallback = accessor.GetThreadIds();
                    bool stillSuspended = record.ThreadIds
                        .Distinct()
                        .Any(threadId => currentThreadIdsFallback.Contains(threadId) && accessor.IsThreadSuspended(threadId));

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

                // ── Detailed diagnostic logging for VerificationFailed ──────────
                // Catat kondisi thread saat verifikasi gagal — membantu debug pola
                // spesifik seperti SynTPEnh yang berulang di sesi terpisah.
                IReadOnlyCollection<int> finalThreadIds = accessor.GetThreadIds();
                int stillSuspendedCount = record.ThreadIds.Distinct()
                    .Count(threadId => finalThreadIds.Contains(threadId) && accessor.IsThreadSuspended(threadId));
                int disappearedThreads = record.ThreadIds.Distinct()
                    .Count(threadId => !finalThreadIds.Contains(threadId));
                int extraThreads = finalThreadIds.Count - record.ThreadIds.Distinct().Count();

                App.Logger.WriteLine(LOG_IDENT,
                    $"PID={record.ProcessId} ({record.ProcessName}) verification detail: " +
                    $"initialThreads={initialThreadCount}, currentThreads={currentThreadCount}, " +
                    $"newThreadsSpawned={newThreadCount}, stillSuspended={stillSuspendedCount}, " +
                    $"disappeared={disappearedThreads}, extraThreads={extraThreads}, " +
                    $"resumeFailures={resumeFailures}");

                if (stillSuspendedCount > 0)
                {
                    // Log specific thread IDs that remain suspended for diagnosability.
                    var stuckThreadIds = record.ThreadIds.Distinct()
                        .Where(threadId => finalThreadIds.Contains(threadId) && accessor.IsThreadSuspended(threadId))
                        .ToList();
                    App.Logger.WriteLine(LOG_IDENT,
                        $"PID={record.ProcessId} ({record.ProcessName}) stuck thread IDs: [{String.Join(", ", stuckThreadIds)}]");
                }

                return Failed(record, resumeFailures > 0 ? RestoreStatus.ResumeFailed : RestoreStatus.VerificationFailed,
                    resumeFailures > 0
                        ? $"{resumeFailures} thread gagal di-resume."
                        : $"Thread masih suspended setelah verifikasi ulang (initial={initialThreadCount}, current={currentThreadCount}, newSpawned={newThreadCount}, stuck={stillSuspendedCount}).");
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
