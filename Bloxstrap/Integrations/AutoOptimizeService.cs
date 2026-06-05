using System;
using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Auto-optimize service untuk perangkat low-end
    /// Deteksi spesifikasi sistem dan apply optimasi otomatis
    /// </summary>
    internal static class AutoOptimizeService
    {
        private const string LOG_IDENT = "AutoOptimizeService";

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

        public enum SystemTier
        {
            HighEnd,    // 4+ cores, 16GB+ RAM
            MidRange,   // 4 cores, 8GB RAM
            LowEnd,     // 2 cores, 4-8GB RAM
            UltraLow    // 2 cores, <4GB RAM
        }

        private static ulong GetTotalPhysicalMemory()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
                return mem.ullTotalPhys;
            return 0;
        }

        private static SystemTier DetectSystemTier()
        {
            try
            {
                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB = totalMemBytes / (1024UL * 1024 * 1024);

                if (cpuCores >= 4 && totalMemGB >= 16)
                    return SystemTier.HighEnd;
                
                if (cpuCores >= 4 && totalMemGB >= 8)
                    return SystemTier.MidRange;
                
                if (totalMemGB < 4)
                    return SystemTier.UltraLow;
                
                return SystemTier.LowEnd;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error detecting system tier: {ex.Message}");
                return SystemTier.MidRange; // Default fallback
            }
        }

        /// <summary>
        /// Cek dan apply optimasi otomatis berdasarkan spek sistem
        /// </summary>
        public static bool CheckAndApply()
        {
            try
            {
                SystemTier tier = DetectSystemTier();
                bool shouldOptimize = tier == SystemTier.LowEnd || tier == SystemTier.UltraLow;

                if (shouldOptimize && !App.Settings.Prop.OptimizeForLowEnd)
                {
                    App.Settings.Prop.OptimizeForLowEnd = true;
                    try { App.Settings.Save(); } catch { }

                    string tierName = tier switch
                    {
                        SystemTier.UltraLow => "Ultra Low-End (Sangat Lambat)",
                        SystemTier.LowEnd => "Low-End (Lambat)",
                        _ => "Unknown"
                    };

                    App.Logger.WriteLine(LOG_IDENT, $"System tier detected: {tierName}. OptimizeForLowEnd enabled.");
                    return true;
                }

                if (!shouldOptimize && App.Settings.Prop.OptimizeForLowEnd)
                {
                    // Optional: disable optimization jika sistem cukup bagus
                    // App.Settings.Prop.OptimizeForLowEnd = false;
                }

                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error in CheckAndApply: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get detailed system info untuk debugging
        /// </summary>
        public static string GetSystemInfo()
        {
            try
            {
                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB = totalMemBytes / (1024UL * 1024 * 1024);
                SystemTier tier = DetectSystemTier();

                return $"CPU Cores: {cpuCores}, RAM: {totalMemGB}GB, Tier: {tier}";
            }
            catch
            {
                return "System info unavailable";
            }
        }

        /// <summary>
        /// Apply aggressive optimizations untuk ultra-low-end systems
        /// </summary>
        public static void ApplyAggressiveOptimizations()
        {
            try
            {
                if (App.Settings.Prop.OptimizeForLowEnd)
                {
                    // Disable expensive features
                    App.Settings.Prop.EnableFpsMonitor = false;
                    App.Settings.Prop.EnableRobloxNotifications = false;
                    
                    // Reduce visual effects
                    App.FastFlags.SetValue("DFIntTextureQualityOverride", "0");
                    App.FastFlags.SetValue("FIntRenderGrainScale", "0");
                    App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");

                    App.Logger.WriteLine(LOG_IDENT, "Aggressive optimizations applied for ultra-low-end");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying aggressive optimizations: {ex.Message}");
            }
        }
    }
}
