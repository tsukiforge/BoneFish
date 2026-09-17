using System.Runtime.InteropServices;

namespace Bloxstrap.GameSession
{
    public interface IProcessAccessor : IDisposable
    {
        int ProcessId { get; }
        bool IsAlive { get; }
        IReadOnlyCollection<int> GetThreadIds();
        bool TrySuspendThread(int threadId);
        bool TryResumeThread(int threadId);
        bool IsThreadSuspended(int threadId);
        DateTime? GetStartTimeUtc();
        long GetProcessorTimeTicks();

        // v7.6.3 — suspend/resume level proses (atomik, mencakup thread yang lahir
        // di tengah operasi). Implementasi boleh tidak mendukung (returns false).
        bool TrySuspendProcess();
        bool TryResumeProcess();
        bool SupportsProcessLevelControl { get; }
    }

    /// <summary>
    /// v7.6.3 FIX — feedback "aplikasi yang dipilih untuk di-suspend tidak benar-benar
    /// ter-suspend dan masih berjalan seperti biasa".
    ///
    /// Dulu suspend hanya per-thread via SuspendThread dengan maksimal 5 sweep pass
    /// dalam 2 detik. Aplikasi seperti browser/launcher terus menambah thread baru,
    /// sehingga sweep tidak pernah menangkap semuanya — thread yang lolos membuat
    /// proses tetap hidup. Selain itu OpenThread(THREAD_SUSPEND_RESUME) bisa gagal
    /// untuk thread yang baru dibuat, dan proses modern yang diproteksi
    /// (PROCESS_SUSPEND_RESUME butuh akses eksplisit) menolak handle Process.GetProcessById.
    ///
    /// Sekarang: NtSuspendProcess/NtResumeProcess (syscall ntdll yang sama dipakai
    /// Process Explorer / pssuspend) men-suspend SELURUH proses secara atomik —
    /// kernel menahan setiap thread termasuk yang baru lahir. Per-thread API tetap
    /// ada sebagai fallback dan untuk verifikasi.
    /// </summary>
    internal sealed class Win32ProcessAccessor : IProcessAccessor
    {
        private const uint THREAD_SUSPEND_RESUME = 0x0002;

        // PROCESS_SUSPEND_RESUME (0x0800) — hak minimal untuk NtSuspendProcess/NtResumeProcess.
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        private const string LOG_IDENT = "GameSession::Win32ProcessAccessor";

        private readonly Process _process;
        private readonly int _processId;

        public Win32ProcessAccessor(int processId)
        {
            _processId = processId;
            _process = Process.GetProcessById(processId);
        }

        public int ProcessId => _processId;

        public bool SupportsProcessLevelControl => true;

        public bool IsAlive
        {
            get
            {
                try { return !_process.HasExited; }
                catch { return false; }
            }
        }

        public IReadOnlyCollection<int> GetThreadIds()
        {
            try
            {
                return _process.Threads
                    .Cast<ProcessThread>()
                    .Select(thread => thread.Id)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        public bool TrySuspendProcess()
        {
            if (TryProcessLevelSuspend())
                return true;

            // Fallback: sweep semua thread saat ini. Tidak sempurna (thread baru
            // bisa lolos), tapi tetap lebih baik daripada gagal total.
            int attempted = 0, succeeded = 0;
            foreach (int threadId in GetThreadIds())
            {
                attempted++;
                if (TrySuspendThread(threadId))
                    succeeded++;
            }

            App.Logger.WriteLine(LOG_IDENT,
                $"PID={_processId}: NtSuspendProcess unavailable — per-thread fallback {succeeded}/{attempted} threads");
            return attempted > 0 && succeeded == attempted;
        }

        public bool TryResumeProcess()
        {
            if (TryProcessLevelResume())
                return true;

            int attempted = 0, succeeded = 0;
            foreach (int threadId in GetThreadIds())
            {
                attempted++;
                if (TryResumeThread(threadId))
                    succeeded++;
            }

            App.Logger.WriteLine(LOG_IDENT,
                $"PID={_processId}: NtResumeProcess unavailable — per-thread fallback {succeeded}/{attempted} threads");
            return attempted > 0 && succeeded == attempted;
        }

        private bool TryProcessLevelSuspend()
        {
            IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)_processId);
            if (handle == IntPtr.Zero)
            {
                App.Logger.WriteLine(LOG_IDENT,
                    $"PID={_processId}: OpenProcess(PROCESS_SUSPEND_RESUME) failed (error={Marshal.GetLastWin32Error()})");
                return false;
            }

            try
            {
                NTSTATUS status = NtSuspendProcess(handle);
                if (status != NTSTATUS.Success)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID={_processId}: NtSuspendProcess returned {status}");
                    return false;
                }

                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private bool TryProcessLevelResume()
        {
            IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)_processId);
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                NTSTATUS status = NtResumeProcess(handle);
                if (status != NTSTATUS.Success)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID={_processId}: NtResumeProcess returned {status}");
                    return false;
                }

                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public bool TrySuspendThread(int threadId)
        {
            IntPtr handle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)threadId);
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                return SuspendThread(handle) != uint.MaxValue;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public bool TryResumeThread(int threadId)
        {
            IntPtr handle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)threadId);
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                return ResumeThread(handle) != uint.MaxValue;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public bool IsThreadSuspended(int threadId)
        {
            IntPtr handle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)threadId);
            if (handle == IntPtr.Zero)
                return true;

            try
            {
                // ProcessThread.WaitReason can remain "Suspended" after a native resume.
                // Probe the actual suspend count instead, restoring it immediately when
                // the probe observes a suspended thread.
                uint previousSuspendCount = ResumeThread(handle);
                if (previousSuspendCount == uint.MaxValue)
                    return true;

                if (previousSuspendCount == 0)
                    return false;

                // Restore the count after probing. The thread was suspended when the
                // probe returned a non-zero previous count, regardless of probe result.
                SuspendThread(handle);
                return true;
            }
            catch
            {
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public DateTime? GetStartTimeUtc()
        {
            try { return _process.StartTime.ToUniversalTime(); }
            catch { return null; }
        }

        public long GetProcessorTimeTicks()
        {
            try { return _process.TotalProcessorTime.Ticks; }
            catch { return -1; }
        }

        public void Dispose() => _process.Dispose();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private enum NTSTATUS : uint
        {
            Success = 0x00000000
        }

        [DllImport("ntdll.dll")]
        private static extern NTSTATUS NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern NTSTATUS NtResumeProcess(IntPtr processHandle);
    }
}
