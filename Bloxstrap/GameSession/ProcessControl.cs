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
    }

    internal sealed class Win32ProcessAccessor : IProcessAccessor
    {
        private const uint THREAD_SUSPEND_RESUME = 0x0002;

        private readonly Process _process;

        public Win32ProcessAccessor(int processId)
        {
            _process = Process.GetProcessById(processId);
        }

        public int ProcessId => _process.Id;

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
        private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
