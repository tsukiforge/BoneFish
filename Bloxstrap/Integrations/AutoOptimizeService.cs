using System;
using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    internal static class AutoOptimizeService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static ulong GetTotalPhysicalMemory()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
                return mem.ullTotalPhys;
            return 0;
        }

        public static bool CheckAndApply()
        {
            try
            {
                int cpu = Environment.ProcessorCount;
                ulong totalMem = GetTotalPhysicalMemory();
                // Thresholds: <=2 logical CPUs or <6GB RAM => low-end
                bool lowEnd = cpu <= 2 || (totalMem > 0 && totalMem < 6UL * 1024 * 1024 * 1024);
                if (lowEnd && !App.Settings.Prop.OptimizeForLowEnd)
                {
                    App.Settings.Prop.OptimizeForLowEnd = true;
                    try { App.Settings.Save(); } catch { }
                    return true;
                }
            }
            catch { /* swallow to avoid crash */ }
            return false;
        }
    }
}
