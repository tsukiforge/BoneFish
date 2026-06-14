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
        /// Cek dan apply optimasi otomatis berdasarkan spek sistem.
        /// Mengaktifkan OptimizeForLowEnd untuk perangkat low-end dan menerapkan
        /// FastFlag rendering ringan agar Roblox benar-benar lebih lancar.
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
                }

                // Apply rendering optimizations whenever low-end mode is active.
                // This must run before Roblox launches so the FastFlags take effect.
                if (App.Settings.Prop.OptimizeForLowEnd)
                {
                    ApplyAggressiveOptimizations(tier);
                    return true;
                }

                // Low-end mode is off: make sure our previously-applied optimization flags are removed
                // so the user isn't permanently stuck on degraded quality.
                return RemoveOptimizations();
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
        /// Apply rendering optimizations untuk perangkat low-end / ultra-low-end.
        /// FastFlag yang diterapkan akan disimpan oleh pemanggil (Bootstrapper) sebelum Roblox berjalan.
        /// </summary>
        public static void ApplyAggressiveOptimizations(SystemTier? tier = null)
        {
            try
            {
                if (!App.Settings.Prop.OptimizeForLowEnd)
                    return;

                tier ??= DetectSystemTier();

                // Lower the texture quality to reduce VRAM/memory pressure.
                App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", "True");
                App.FastFlags.SetValue("DFIntTextureQualityOverride", "0");

                // Disable post-processing grain which is comparatively expensive.
                App.FastFlags.SetValue("FIntRenderGrainScale", "0");

                // Increase batch size so the renderer flushes less often.
                App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");

                // Reduce render-distance / level-of-detail switching cost.
                // Use a milder quality floor for low-end and the lowest for ultra-low-end.
                App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", tier == SystemTier.UltraLow ? "1" : "5");

                if (tier == SystemTier.UltraLow)
                {
                    // Disable BoneFish overlays/services that themselves consume CPU on the weakest devices.
                    App.Settings.Prop.EnableFpsMonitor = false;
                    App.Settings.Prop.EnableRobloxNotifications = false;
                    try { App.Settings.Save(); } catch { }

                    App.Logger.WriteLine(LOG_IDENT, "Aggressive optimizations applied for ultra-low-end");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "Rendering optimizations applied for low-end");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying aggressive optimizations: {ex.Message}");
            }
        }

        // FastFlags that this service manages. Kept in one place so we can cleanly remove them
        // when low-end optimization is turned off.
        private static readonly string[] ManagedFlags =
        {
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntRenderGrainScale",
            "FIntMaxBatchesPerFlush",
            "DFIntDebugFRMQualityLevelOverride"
        };

        /// <summary>
        /// Remove any optimization FastFlags this service previously applied.
        /// Returns true if at least one flag was removed.
        /// </summary>
        public static bool RemoveOptimizations()
        {
            try
            {
                bool removedAny = false;

                foreach (string flag in ManagedFlags)
                {
                    if (App.FastFlags.GetValue(flag) is not null)
                    {
                        App.FastFlags.SetValue(flag, null);
                        removedAny = true;
                    }
                }

                if (removedAny)
                    App.Logger.WriteLine(LOG_IDENT, "Removed low-end optimization FastFlags");

                return removedAny;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error removing optimizations: {ex.Message}");
                return false;
            }
        }
    }
}
