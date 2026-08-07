using System;
using System.Diagnostics;
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

        // ── P/Invoke untuk memory management ─────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

        // ── P/Invoke untuk HDD/SSD detection via DeviceIoControl ─────────────────────────
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // IOCTL_STORAGE_QUERY_PROPERTY = CTL_CODE(IOCTL_STORAGE_BASE, 0x0500, METHOD_BUFFERED, FILE_ANY_ACCESS)
        // = (0x2d << 16) | (0 << 14) | (0x0500 << 2) | 0 = 0x002D1400
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint StorageDeviceSeekPenaltyProperty = 8; // STORAGE_PROPERTY_ID

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;
            public uint QueryType;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct STORAGE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)]
            public byte IncursSeekPenalty; // BOOLEAN: 0 = SSD, 1 = HDD
        }

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
            HighEnd,         // 4+ cores, 16GB+ RAM
            MidRange,        // 4 cores, 8GB RAM
            LowEnd,          // 2 cores, 4-8GB RAM
            UltraLow,        // 2 cores, <4GB RAM
            ExtremePerformance  // override manual — "Potato Mode" paksa oleh user
        }

        private static ulong GetTotalPhysicalMemory()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
                return mem.ullTotalPhys;
            return 0;
        }

        // ── HDD/SSD Detection via DeviceIoControl Seek Penalty ───────────────────────────
        // Cara kerja: Kirim IOCTL_STORAGE_QUERY_PROPERTY ke volume drive sistem ("\\.\C:").
        // StorageDeviceSeekPenaltyProperty = 8 akan mengembalikan STORAGE_SEEK_PENALTY_DESCRIPTOR
        // dengan field IncursSeekPenalty: false (0) = SSD, true (1) = HDD.
        //
        // Ini LEBIH RELIABLE daripada vendor-name heuristic karena langsung nanya ke driver
        // storage — HDD selalu punya seek penalty, SSD tidak pernah.
        // Tidak perlu admin rights karena pake volume handle (bukan physical drive handle).
        private static bool? _isSSDCached = null;
        private static readonly object _storageLock = new();

        private static bool IsSSD()
        {
            if (_isSSDCached.HasValue)
                return _isSSDCached.Value;

            lock (_storageLock)
            {
                if (_isSSDCached.HasValue)
                    return _isSSDCached.Value;

                try
                {
                    // Buka handle ke system volume ("\\.\C:")
                    string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                    string volumePath = @"\\.\" + systemDrive.TrimEnd('\\');

                    IntPtr hVolume = CreateFile(
                        volumePath,
                        GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (hVolume == INVALID_HANDLE_VALUE)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not open volume {volumePath} for seek penalty query. Assuming HDD.");
                        _isSSDCached = false;
                        return false;
                    }

                    try
                    {
                        // Prepare query struct
                        var query = new STORAGE_PROPERTY_QUERY
                        {
                            PropertyId = StorageDeviceSeekPenaltyProperty,
                            QueryType = 0 // PropertyStandardQuery
                        };

                        int querySize = Marshal.SizeOf(typeof(STORAGE_PROPERTY_QUERY));
                        int descSize = Marshal.SizeOf(typeof(STORAGE_SEEK_PENALTY_DESCRIPTOR));

                        IntPtr queryPtr = Marshal.AllocHGlobal(querySize);
                        IntPtr descPtr = Marshal.AllocHGlobal(descSize);

                        try
                        {
                            Marshal.StructureToPtr(query, queryPtr, false);
                            // Zero-initialize descriptor memory
                        var zeroBytes = new byte[descSize];
                        Marshal.Copy(zeroBytes, 0, descPtr, descSize);

                            bool success = DeviceIoControl(
                                hVolume,
                                IOCTL_STORAGE_QUERY_PROPERTY,
                                queryPtr,
                                (uint)querySize,
                                descPtr,
                                (uint)descSize,
                                out uint bytesReturned,
                                IntPtr.Zero);

                            if (success && bytesReturned >= (uint)descSize)
                            {
                                var descriptor = Marshal.PtrToStructure<STORAGE_SEEK_PENALTY_DESCRIPTOR>(descPtr);
                                bool isSSD = descriptor.IncursSeekPenalty == 0; // false = no seek penalty = SSD
                                _isSSDCached = isSSD;

                                App.Logger.WriteLine(LOG_IDENT,
                                    $"Storage detected via DeviceIoControl: {(isSSD ? "SSD" : "HDD")} " +
                                    $"(IncursSeekPenalty={descriptor.IncursSeekPenalty})");
                                return isSSD;
                            }
                            else
                            {
                                int lastError = Marshal.GetLastWin32Error();
                                App.Logger.WriteLine(LOG_IDENT,
                                    $"DeviceIoControl seek penalty query failed (error={lastError}). Assuming HDD.");
                                _isSSDCached = false;
                                return false;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(queryPtr);
                            Marshal.FreeHGlobal(descPtr);
                        }
                    }
                    finally
                    {
                        CloseHandle(hVolume);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Storage detection via DeviceIoControl failed: {ex.Message}. Assuming HDD.");
                    _isSSDCached = false;
                    return false;
                }
            }
        }

        private static SystemTier DetectSystemTier()
        {
            try
            {
                if (App.Settings.Prop.ForceExtremeMode)
                    return SystemTier.ExtremePerformance;

                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemMB = totalMemBytes / (1024UL * 1024);

                if (cpuCores >= 4 && totalMemMB >= 15600)
                    return SystemTier.HighEnd;
                if (cpuCores >= 4 && totalMemMB >= 7800)
                    return SystemTier.MidRange;
                if (totalMemMB < 3800)
                    return SystemTier.UltraLow;
                return SystemTier.LowEnd;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error detecting system tier: {ex.Message}");
                return SystemTier.MidRange;
            }
        }

        private static bool UserHasManualPreset()
        {
            string preset = App.Settings.Prop.SelectedPerformancePreset ?? "None";
            return preset is "UltraLow" or "Balanced" or "Stable" or "ExtremePerformance";
        }

        public static bool CheckAndApply()
        {
            try
            {
                SystemTier tier = DetectSystemTier();
                bool isExtreme = tier == SystemTier.ExtremePerformance;
                bool shouldOptimize = isExtreme || tier == SystemTier.LowEnd || tier == SystemTier.UltraLow;

                if (shouldOptimize && !App.Settings.Prop.OptimizeForLowEnd)
                {
                    App.Settings.Prop.OptimizeForLowEnd = true;
                    try { App.Settings.Save(); } catch { }

                    string tierName = tier switch
                    {
                        SystemTier.ExtremePerformance => "Extreme Performance / Potato Mode (Override Manual)",
                        SystemTier.UltraLow           => "Ultra Low-End (Sangat Lambat)",
                        SystemTier.LowEnd             => "Low-End (Lambat)",
                        _                             => "Unknown"
                    };

                    App.Logger.WriteLine(LOG_IDENT, $"System tier detected: {tierName}. OptimizeForLowEnd enabled.");
                }

                if (App.Settings.Prop.OptimizeForLowEnd)
                {
                    ApplyAggressiveOptimizations(tier);
                    return true;
                }

                if (!IsSSD() && (tier == SystemTier.LowEnd || tier == SystemTier.MidRange) && !UserHasManualPreset())
                {
                    App.Logger.WriteLine(LOG_IDENT, $"HDD detected + {tier} tier — applying HDD Balanced optimizations");
                    ApplyHDDBalancedOptimizations();
                    return true;
                }

                return RemoveOptimizations();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error in CheckAndApply: {ex.Message}");
                return false;
            }
        }

        public static string GetSystemInfo()
        {
            try
            {
                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemMB = totalMemBytes / (1024UL * 1024);
                SystemTier tier = DetectSystemTier();
                string storageType = IsSSD() ? "SSD" : "HDD";

                return $"CPU Cores: {cpuCores}, RAM: {totalMemMB}MB ({totalMemMB/1024}GB), Storage: {storageType}, Tier: {tier}";
            }
            catch
            {
                return "System info unavailable";
            }
        }

        public static void ApplyAggressiveOptimizations(
            SystemTier? tier = null,
            bool hddIoTweaks = false,
            bool bypassLowEndGuard = false)
        {
            try
            {
                if (!bypassLowEndGuard)
                {
                    if (!App.Settings.Prop.OptimizeForLowEnd)
                        return;

                    if (UserHasManualPreset())
                    {
                        App.Logger.WriteLine(LOG_IDENT, "User has manual preset — skipping aggressive overrides to respect user choice.");
                        return;
                    }
                }

                tier ??= DetectSystemTier();
                bool isExtreme = tier == SystemTier.ExtremePerformance;
                bool isUltraOrExtreme = tier == SystemTier.UltraLow || isExtreme;

                PurgeAllKnownFlags();

                App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", "True");
                App.FastFlags.SetValue("DFIntTextureQualityOverride", "0");
                App.FastFlags.SetValue("FIntTextureCompositorLowResFactor", "1");

                App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "3");
                App.FastFlags.SetValue("FIntRomarkStartWithGraphicQualityLevel", "1");

                App.FastFlags.SetValue("FIntRobloxGuiBlurIntensity", "0");

                App.FastFlags.SetValue("FFlagDebugSSAOForce", "False");
                App.FastFlags.SetValue("FIntSSAOMipLevels", "0");

                App.FastFlags.SetValue("FIntRenderGrainScale", "0");

                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance",       "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL12",    "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL23",    isExtreme ? "500" : "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34",    isExtreme ? "750" : "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceStatic", "0");
                App.FastFlags.SetValue("DFIntCSGv2LodsToGenerate", "0");

                App.FastFlags.SetValue("FIntTerrainArraySliceSize", "0");

                App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");

                App.FastFlags.SetValue("DFIntMaxFrameBufferSize", "4");
                App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", "4");
                App.FastFlags.SetValue("DFFlagEnableRequestAsyncCompression", "True");

                if (isExtreme)
                {
                    int fpsCap = Math.Clamp(App.Settings.Prop.ExtremeModeFpsTarget, 24, 60);
                    App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", fpsCap.ToString());
                }
                else
                {
                    App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", "30");
                }

                App.FastFlags.SetValue("DFIntMaxActiveAnimationTracks", "32");
                App.FastFlags.SetValue("FIntRenderLocalLightFadeInMs", "0");

                App.FastFlags.SetValue("FFlagDebugDisableTelemetryEphemeralCounter", "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryEphemeralStat",    "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryEventIngest",      "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryPoint",            "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Counter",        "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Event",          "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Stat",           "True");

                if (isUltraOrExtreme)
                {
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "4");
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");
                    App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "1");

                    App.Settings.Prop.EnableFpsMonitor = false;
                    App.Settings.Prop.EnableRobloxNotifications = false;
                    try { App.Settings.Save(); } catch { }

                    string label = isExtreme ? "ExtremePerformance (Potato Mode)" : "UltraLow";
                    App.Logger.WriteLine(LOG_IDENT, $"Aggressive optimizations applied for {label}");
                }
                else if (hddIoTweaks)
                {
                    // HDD-specific I/O tweaks (only applied on top of LowEnd base, never Ultra/Extreme)
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "4");
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");
                    App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "2");

                    App.Logger.WriteLine(LOG_IDENT, "Rendering optimizations applied for low-end (with HDD I/O tweaks)");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "Rendering optimizations applied for low-end");
                }

                // ── Network Optimizations ("sekelas NASA") ─────────────────────────
                // ★ FIX: PurgeAllKnownFlags() di awal method ini MENGHAPUS flag network
                // (FIntRakNetPacketRateLimit, DFIntMaxReceivePPS, DFIntMaxSendPPS,
                // DFIntConnectionMTUSize, DFIntOptimizeSendQueue), tapi sebelumnya TIDAK
                // pernah di-apply ulang di path low-end auto. Akibatnya user LowEnd/UltraLow
                // yang tidak pilih preset manual justru KEHILANGAN optimasi jaringan —
                // padahal preset manual (UltraLow, Balanced, dst) selalu memakainya.
                // Sekarang semua path low-end (auto, HDD Balanced, Turbo Mode) juga
                // mendapat network boost yang sama.
                ApplyNetworkOptimizations();
                App.Logger.WriteLine(LOG_IDENT, "Network optimizations applied (low-end path)");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying aggressive optimizations: {ex.Message}");
            }
        }

        // HDD-path delegates ke ApplyAggressiveOptimizations supaya tidak ada duplikasi flag.
        // bypassLowEndGuard=true karena caller (CheckAndApply) memanggil ini justru saat OptimizeForLowEnd masih FALSE
        // (HDD + LowEnd/MidRange tier, !OptimizeForLowEnd, !UserHasManualPreset()).
        // hddIoTweaks=true menyebabkan tambahan 3 flag HDD-specific di akhir apply base:
        //   DFIntTextureCompositorActiveJobs=2 (vs UltraLow=1 vs LowEnd=unset)
        //   FIntRenderLocalLightUpdatesMax=4, FIntRenderLocalLightUpdatesMin=2 (seperti UltraOrExtreme tapi tanpa side-effect FPS/Notifications)
        public static void ApplyHDDBalancedOptimizations()
        {
            try
            {
                App.Logger.WriteLine(LOG_IDENT, "HDD Balanced optimizations applied (LowEnd base + HDD I/O tweaks)");
                ApplyAggressiveOptimizations(
                    tier: SystemTier.LowEnd,
                    hddIoTweaks: true,
                    bypassLowEndGuard: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying HDD Balanced optimizations: {ex.Message}");
            }
        }

        private static readonly string[] AllKnownManagedFlags =
        {
            "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "FIntTextureCompositorLowResFactor", "DFIntTextureCompositorActiveJobs",
            "DFIntDebugFRMQualityLevelOverride", "FIntRomarkStartWithGraphicQualityLevel",
            "FIntRenderShadowIntensity", "DFFlagDebugPauseVoxelizer", "FIntCSGVoxelizerFadeRadius",
            "DFFlagDebugRenderForceTechnologyVoxel", "FFlagNewLightAttenuation", "FFlagFastGPULightCulling3",
            "FFlagDebugSkyGray", "FFlagDisablePostFx", "FFlagDebugSSAOForce", "FIntSSAOMipLevels", "FIntRobloxGuiBlurIntensity", "FIntRenderGrainScale",
            "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntRenderGrassDetailStrands", "FIntRenderGrassHeightScaler", "FFlagGlobalWindActivated",
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34", "DFIntCSGLevelOfDetailSwitchingDistanceStatic", "DFIntCSGv2LodsToGenerate", "DFIntDebugRestrictGCDistance",
            "FIntTerrainArraySliceSize",
            "DFIntAnimationLodFacsDistanceMin", "DFIntAnimationLodFacsDistanceMax", "DFIntAnimationLodFacsVisibilityDenominator",
            "FIntMaxBatchesPerFlush", "DFIntMaxFrameBufferSize", "FIntRuntimeMaxNumOfThreads", "DFFlagEnableRequestAsyncCompression",
            "DFIntTaskSchedulerTargetFps",
            "FIntRenderLocalLightUpdatesMax", "FIntRenderLocalLightUpdatesMin", "FIntRenderLocalLightFadeInMs",
            "DFIntMaxActiveAnimationTracks",
            "FFlagDebugDisableTelemetryEphemeralCounter", "FFlagDebugDisableTelemetryEphemeralStat", "FFlagDebugDisableTelemetryEventIngest",
            "FFlagDebugDisableTelemetryPoint", "FFlagDebugDisableTelemetryV2Counter", "FFlagDebugDisableTelemetryV2Event", "FFlagDebugDisableTelemetryV2Stat",
            "FFlagRenderUIAnimations", "FFlagRenderMenuTransitions", "FFlagRenderInventoryEffects",
            "FFlagLuaAppEnableLowMemoryMode",
            "FIntRakNetPacketRateLimit", "DFIntMaxReceivePPS", "DFIntMaxSendPPS", "DFIntConnectionMTUSize", "DFIntOptimizeSendQueue",
            "FFlagDebugDisplayFPS",
        };

        public static void PurgeAllKnownFlags()
        {
            foreach (string flag in AllKnownManagedFlags)
                App.FastFlags.SetValue(flag, null);
            App.Logger.WriteLine(LOG_IDENT, $"Purged {AllKnownManagedFlags.Length} known managed flags");
        }

        private static readonly string[] ManagedFlags =
        {
            "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "FIntTextureCompositorLowResFactor",
            "DFIntDebugFRMQualityLevelOverride", "FIntRomarkStartWithGraphicQualityLevel",
            "FIntRenderShadowIntensity", "DFFlagDebugPauseVoxelizer", "FIntCSGVoxelizerFadeRadius",
            "FFlagFastGPULightCulling3", "FFlagNewLightAttenuation",
            "FFlagDebugSSAOForce", "FIntSSAOMipLevels", "FIntRobloxGuiBlurIntensity", "FIntRenderGrainScale",
            "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntRenderGrassDetailStrands", "FIntRenderGrassHeightScaler", "FFlagGlobalWindActivated",
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34", "DFIntCSGv2LodsToGenerate",
            "FIntTerrainArraySliceSize",
            "FIntMaxBatchesPerFlush",
            "DFIntTaskSchedulerTargetFps",
            "FIntRenderLocalLightUpdatesMax", "FIntRenderLocalLightUpdatesMin",
            "DFIntTextureCompositorActiveJobs",
            "DFIntMaxActiveAnimationTracks", "FIntRenderLocalLightFadeInMs",
            "FFlagDebugDisableTelemetryEphemeralCounter", "FFlagDebugDisableTelemetryEphemeralStat", "FFlagDebugDisableTelemetryEventIngest",
            "FFlagDebugDisableTelemetryPoint", "FFlagDebugDisableTelemetryV2Counter", "FFlagDebugDisableTelemetryV2Event", "FFlagDebugDisableTelemetryV2Stat",
            "FFlagRenderUIAnimations", "FFlagRenderMenuTransitions", "FFlagRenderInventoryEffects",
            "FFlagLuaAppEnableLowMemoryMode",
            "FIntRakNetPacketRateLimit", "DFIntMaxReceivePPS", "DFIntMaxSendPPS", "DFIntConnectionMTUSize", "DFIntOptimizeSendQueue",
            "FFlagDebugDisplayFPS",
        };

        public static void CleanupLegacyRobloxFlags()
        {
            try
            {
                int totalCleanedFiles = 0;

                string robloxVersionsDir = Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
                if (Directory.Exists(robloxVersionsDir))
                {
                    foreach (string versionDir in Directory.GetDirectories(robloxVersionsDir, "version-*"))
                    {
                        string clientSettingsPath = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                        if (CleanupClientAppSettings(clientSettingsPath))
                            totalCleanedFiles++;
                    }
                }

                string bonefishModPath = Path.Combine(Paths.Modifications, "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(bonefishModPath) && CleanupClientAppSettings(bonefishModPath))
                    totalCleanedFiles++;

                string bonefishVersionPath = Path.Combine(Paths.Base, "Versions", "WindowsPlayer", "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(bonefishVersionPath) && CleanupClientAppSettings(bonefishVersionPath))
                    totalCleanedFiles++;

                if (totalCleanedFiles > 0)
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Legacy flag cleanup done: {totalCleanedFiles} file(s) cleaned from all paths");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"CleanupLegacyRobloxFlags failed (non-fatal): {ex.Message}");
            }
        }

        private static bool CleanupClientAppSettings(string clientSettingsPath)
        {
            try
            {
                if (!File.Exists(clientSettingsPath))
                    return false;

                string content = File.ReadAllText(clientSettingsPath).Trim();

                if (content == "{}" || content == "{ }" || string.IsNullOrWhiteSpace(content))
                    return false;

                var flags = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, object>>(content);

                if (flags == null || flags.Count == 0)
                    return false;

                bool modified = false;
                foreach (string flag in AllKnownManagedFlags)
                {
                    if (flags.Remove(flag))
                        modified = true;
                }

                if (modified)
                {
                    string cleaned = System.Text.Json.JsonSerializer
                        .Serialize(flags, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(clientSettingsPath, cleaned);
                    App.Logger.WriteLine(LOG_IDENT, $"Cleaned legacy flags from: {clientSettingsPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not clean {clientSettingsPath}: {ex.Message}");
            }

            return false;
        }

        public static void OptimizeRobloxProcess(int robloxPid)
        {
            if (!App.Settings.Prop.OptimizeForLowEnd)
                return;

            try
            {
                using var robloxProc = Process.GetProcessById(robloxPid);
                robloxProc.PriorityClass = ProcessPriorityClass.AboveNormal;
                App.Logger.WriteLine(LOG_IDENT, $"Set Roblox PID {robloxPid} priority → AboveNormal");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Priority set failed (non-fatal): {ex.Message}");
            }

            try
            {
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB = totalMemBytes / (1024UL * 1024 * 1024);

                // ★ FIX (audit white-screen v7.x): Memory trim HANYA di SSD.
                // EmptyWorkingSet memaksa proses yang di-trim untuk page-in BALIK dari
                // disk saat mereka butuh memorinya lagi. Di HDD, ini terjadi tepat saat
                // game baru launch (fase loading aset paling kritis) → disk storm yang
                // bisa memperparah stall render / white screen. Di SSD page-in hampir
                // instan, jadi trimming tetap aman di sana.
                if (totalMemGB < 5 && IsSSD())
                    TrimBackgroundProcesses(robloxPid);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Memory trim failed (non-fatal): {ex.Message}");
            }

            try
            {
                if (Environment.ProcessorCount <= 2)
                {
                    using var self = Process.GetCurrentProcess();
                    self.ProcessorAffinity = (IntPtr)0x1;
                    App.Logger.WriteLine(LOG_IDENT, "Dual-core detected: BoneFish pinned to core 0");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Affinity set failed (non-fatal): {ex.Message}");
            }
        }

        private static void TrimBackgroundProcesses(int robloxPid)
        {
            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "smss", "csrss", "lsass", "services",
                "winlogon", "wininit", "svchost", "dwm", "explorer",
                "audiodg", "fontdrvhost", "spoolsv", "SearchIndexer"
            };

            string selfName = Process.GetCurrentProcess().ProcessName;
            int trimmedCount = 0;
            long totalFreedKB = 0;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == robloxPid) continue;
                    if (proc.ProcessName == selfName) continue;
                    if (skipNames.Contains(proc.ProcessName)) continue;

                    long workingSetKB = proc.WorkingSet64 / 1024;
                    if (workingSetKB < 20 * 1024) continue;

                    IntPtr handle = OpenProcess(PROCESS_ALL_ACCESS, false, (uint)proc.Id);
                    if (handle == IntPtr.Zero) continue;

                    try
                    {
                        bool trimmed = EmptyWorkingSet(handle);
                        if (trimmed)
                        {
                            totalFreedKB += workingSetKB;
                            trimmedCount++;
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch { }
            }

            if (trimmedCount > 0)
            {
                App.Logger.WriteLine(LOG_IDENT,
                    $"RAM trim: {trimmedCount} background processes trimmed, " +
                    $"~{totalFreedKB / 1024}MB working set released");
            }
        }

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

                // CATATAN: EnableBetterMatchmaking / EnableBetterMatchmakingRandomization
                // TIDAK di-reset di sini — dua setting ini juga bisa di-set manual oleh
                // user di halaman Behaviour (bukan hanya oleh preset). Meresetnya di sini
                // akan menimpa pilihan manual user. Ini pola yang sudah ada sejak v6.0.0.
                return removedAny;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error removing optimizations: {ex.Message}");
                return false;
            }
        }

        // ── Network Optimizations (Reusable) ─────────────────────────────────────────
        // ★ REFACTOR: Ekstrak dari FastFlagsViewModel untuk menghilangkan duplikasi
        // di 5 preset method. Method ini SETARA dengan blok yang sebelumnya inline:
        //   App.Settings.Prop.EnableBetterMatchmaking = true;
        //   App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
        //   App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
        //   App.FastFlags.SetValue("DFIntMaxReceivePPS",        "50000");
        //   App.FastFlags.SetValue("DFIntMaxSendPPS",           "50000");
        //   App.FastFlags.SetValue("DFIntConnectionMTUSize",    "1500");
        //   App.FastFlags.SetValue("DFIntOptimizeSendQueue",    "1");
        //
        // TIDAK memanggil Save()/Notify()/Reload() — itu tanggung jawab caller.
        // Caller: ApplyRecommendedNetworkSettings(), ApplyRecommendedStabilityPreset(),
        //         ApplyUltraLowSpecPreset(), ApplyBalancedPreset(),
        //         ApplyExtremePerformancePreset().
        //
        // Verifikasi: Kelima preset sebelumnya menulis flag yang SAMA PERSIS —
        // method ini adalah 1-to-1 replacement, tidak ada perubahan nilai.
        public static void ApplyNetworkOptimizations()
        {
            App.Settings.Prop.EnableBetterMatchmaking = true;
            App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
            App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
            App.FastFlags.SetValue("DFIntMaxReceivePPS",        "50000");
            App.FastFlags.SetValue("DFIntMaxSendPPS",           "50000");
            App.FastFlags.SetValue("DFIntConnectionMTUSize",    "1500");
            App.FastFlags.SetValue("DFIntOptimizeSendQueue",    "1");
        }

        // ── Fast Loading Flags (Toggle Terpisah) ─────────────────────────────────────
        // ★ Fast Loading toggle: EnableFastLoadingFlags — mempercepat loading aset
        // (texture, mesh) dengan meningkatkan paralelisme komposisi texture dan
        // thread scheduler. Stack dengan preset visual apa pun.
        //
        // Flag yang dipakai (semua SUDAH ADA di AllKnownManagedFlags):
        //   DFIntTextureCompositorActiveJobs=2  (jika cpuCores >= 4)
        //     Naikkan dari 1 (UltraLow/Extreme) ke 2 agar texture compositor
        //     lebih paralel — aset texture muncul lebih cepat.
        //   FIntRuntimeMaxNumOfThreads=6        (jika cpuCores >= 8)
        //     Naikkan dari 4 (default semua preset) ke 6 agar task scheduler
        //     punya lebih banyak thread untuk loading aset.
        //
        // Flag yang TIDAK dipakai (riset menemukan kemungkinan diblokir Allowlist):
        //   FFlagEnableAsyncResourceLoading     — ❌ Tidak di Allowlist
        //   FIntRenderChunkLODThreshold         — ❌ Tidak di Allowlist
        //   FFlagEnableTextureStreamingFix      — ❌ Tidak di Allowlist
        //   FIntPartSizeBoostThreshold          — ❌ Tidak di Allowlist
        //
        // Flag yang JANGAN dipakai (visual, bukan loading):
        //   FIntRenderShadowIntensity           — Flag visual
        //   DFFlagDisablePostProcessing         — Flag visual
        //
        // DFIntTaskSchedulerTargetFps: SUDAH ADA di ApplyAggressiveOptimizations()
        // sebagai base flag kondisional — tidak ditambahkan di sini untuk
        // menghindari duplikasi/konflik nilai.
        public static void ApplyFastLoadingFlags()
        {
            int cpuCores = Environment.ProcessorCount;
            
            // DFIntTextureCompositorActiveJobs: naikkan ke 2 KHUSUS cpuCores >= 4
            if (cpuCores >= 4)
            {
                App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "2");
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: DFIntTextureCompositorActiveJobs=2 (cpuCores={cpuCores} >= 4)");
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: DFIntTextureCompositorActiveJobs SKIPPED (cpuCores={cpuCores} < 4)");
            }

            // FIntRuntimeMaxNumOfThreads: naikkan ke 6 KHUSUS cpuCores >= 8
            if (cpuCores >= 8)
            {
                App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", "6");
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: FIntRuntimeMaxNumOfThreads=6 (cpuCores={cpuCores} >= 8)");
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: FIntRuntimeMaxNumOfThreads SKIPPED (cpuCores={cpuCores} < 8)");
            }
        }

        public static void RemoveFastLoadingFlags()
        {
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", null);
            App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", null);
            App.Logger.WriteLine(LOG_IDENT, "FastLoading flags removed");
        }
    }
}
