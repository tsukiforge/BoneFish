using System.Management;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// ── Hardware Profile Engine (Phase 3) ────────────────────────────────────────
    /// Read-only, cached, on-demand hardware snapshot. All expensive queries (WMI GPU,
    /// OS build) run AT MOST ONCE per process lifetime and only when something asks
    /// for the profile — no timers, no polling, no background work.
    ///
    /// Tier is derived from MULTIPLE characteristics (CPU count, physical memory,
    /// dedicated-GPU presence, storage class) — never RAM alone, never cores alone.
    /// Storage detection itself stays delegated to AutoOptimizeService's 3-state
    /// engine (SSD/HDD/Unknown — Unknown is never guessed away).
    /// </summary>
    public sealed record HardwareProfile
    {
        public enum HardwareTier { UltraLow, Low, Balanced, Mid, High }

        public enum MemoryPressureLevel { Low, Moderate, High }

        public string CpuName { get; init; } = "";
        public int LogicalProcessors { get; init; }
        public int PhysicalCores { get; init; }          // 0 = unavailable
        public ulong TotalRamBytes { get; init; }
        public ulong AvailableRamBytes { get; init; }
        public MemoryPressureLevel MemoryPressure { get; init; } = MemoryPressureLevel.Low;
        public string GpuName { get; init; } = "";
        public bool HasDedicatedGpu { get; init; }
        public bool GpuDetectionComplete { get; init; }  // false → integrated/dedicated unknown
        public string StorageType { get; init; } = "Unknown";  // "SSD" / "HDD" / "Unknown" (3-state preserved)
        public string StorageConfidence { get; init; } = "None";
        public string SystemDrive { get; init; } = "";
        public string DisplayResolution { get; init; } = "";
        public int DisplayRefreshRate { get; init; }
        public string OsVersion { get; init; } = "";
        public HardwareTier Tier { get; init; } = HardwareTier.Balanced;
        public string TierReason { get; init; } = "";
        public DateTime CollectedAtUtc { get; init; } = DateTime.UtcNow;

        public ulong TotalRamMb => TotalRamBytes / (1024UL * 1024);
        public ulong AvailableRamMb => AvailableRamBytes / (1024UL * 1024);

        public string TierDisplay => Tier switch
        {
            HardwareTier.UltraLow => "Ultra Low",
            HardwareTier.Low => "Low",
            HardwareTier.Balanced => "Balanced",
            HardwareTier.Mid => "Mid",
            HardwareTier.High => "High",
            _ => "Unknown"
        };

        public static string StorageDisplay(string storageType) => storageType switch
        {
            "SSD" => "SSD",
            "HDD" => "HDD",
            _ => "Unknown"
        };
    }

    public static class HardwareProfileEngine
    {
        private const string LOG_IDENT = "HardwareProfileEngine";

        private static readonly object _lock = new();
        private static HardwareProfile? _cached;

        /// <summary>
        /// Returns the cached profile, or builds one. Static hardware facts (CPU model,
        /// core counts, GPU, OS build, display) are detected at most once per process.
        /// Available-RAM / memory pressure are refreshed per call — they are two cheap
        /// GlobalMemoryStatusEx syscalls (microseconds), not WMI.
        /// </summary>
        public static HardwareProfile GetProfile(bool refreshDynamicOnly = false)
        {
            lock (_lock)
            {
                if (_cached is { } cached && !refreshDynamicOnly)
                    return RefreshMemoryPressure(cached);

                if (_cached is { } existing)
                {
                    // refreshDynamicOnly: keep static facts, refresh RAM pressure.
                    var refreshed = RefreshMemoryPressure(existing);
                    return refreshed with { CollectedAtUtc = DateTime.UtcNow };
                }

                _cached = DetectFullProfile();
                return _cached;
            }
        }

        /// <summary>
        /// Drops the cached profile entirely (used by "Deteksi Ulang Hardware").
        /// Storage cache invalidation itself stays in AutoOptimizeService.
        /// </summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _cached = null;
            }
        }

        private static HardwareProfile RefreshMemoryPressure(HardwareProfile profile)
        {
            try
            {
                ulong total = AutoOptimizeService.GetTotalPhysicalMemoryPublic();
                ulong available = AutoOptimizeService.GetAvailablePhysicalMemoryPublic();

                if (total == 0)
                    return profile;

                HardwareProfile.MemoryPressureLevel pressure =
                    available < total / 8 ? HardwareProfile.MemoryPressureLevel.High :
                    available < total / 4 ? HardwareProfile.MemoryPressureLevel.Moderate :
                    HardwareProfile.MemoryPressureLevel.Low;

                if (pressure == profile.MemoryPressure && available == profile.AvailableRamBytes)
                    return profile;

                return profile with
                {
                    TotalRamBytes = total,
                    AvailableRamBytes = available,
                    MemoryPressure = pressure,
                    CollectedAtUtc = DateTime.UtcNow
                };
            }
            catch
            {
                return profile;
            }
        }

        private static HardwareProfile DetectFullProfile()
        {
            int logical = Environment.ProcessorCount;
            string cpuName = "";
            int physicalCores = 0;
            string gpuName = "";
            bool hasDedicatedGpu = false;
            bool gpuComplete = false;
            string osVersion = "";

            // ── WMI batch 1: CPU model + physical cores (one query) ──────────────
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (ManagementObject cpu in searcher.Get().Cast<ManagementObject>())
                {
                    try
                    {
                        string? name = cpu["Name"]?.ToString();
                        if (!String.IsNullOrWhiteSpace(name))
                            cpuName = String.IsNullOrEmpty(cpuName) ? name.Trim() : cpuName + " + " + name.Trim();

                        if (ushort.TryParse(cpu["NumberOfCores"]?.ToString(), out ushort cores))
                            physicalCores += cores;

                        if (ushort.TryParse(cpu["NumberOfLogicalProcessors"]?.ToString(), out ushort logicalFromWmi))
                            logical = Math.Max(logical, logicalFromWmi);
                    }
                    finally
                    {
                        cpu.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"CPU query failed (non-fatal): {ex.Message}");
            }

            // ── WMI batch 2: GPU — name, dedicated VRAM when available (one query) ──
            try
            {
                var gpuNames = new List<string>();
                using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (ManagementObject controller in searcher.Get().Cast<ManagementObject>())
                {
                    try
                    {
                        string? name = controller["Name"]?.ToString();
                        if (String.IsNullOrWhiteSpace(name))
                            continue;

                        gpuNames.Add(name);

                        // AdapterRAM is 32-bit and caps at 4GB; treat any adapter reporting
                        // >= 1GB VRAM as dedicated-capable. Name heuristics fill the gaps
                        // (Intel/AMD iGPU naming is well established; NVIDIA is always
                        // dedicated). This is a capability signal for tiering, not a spec sheet.
                        bool looksIntegrated =
                            name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                            && (name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("Iris", StringComparison.OrdinalIgnoreCase));

                        bool looksDedicated =
                            name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Arc ", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Quadro", StringComparison.OrdinalIgnoreCase);

                        if (!looksIntegrated && !looksDedicated)
                        {
                            // Unknown vendor — fall back to VRAM size when readable.
                            try
                            {
                                object? raw = controller["AdapterRAM"];
                                if (raw is not null)
                                {
                                    ulong vram = Convert.ToUInt64(raw);
                                    looksDedicated = vram >= 1024UL * 1024 * 1024 * 4; // >= 4GB
                                }
                            }
                            catch { }
                        }

                        hasDedicatedGpu |= looksDedicated;
                    }
                    finally
                    {
                        controller.Dispose();
                    }
                }

                gpuName = String.Join(" + ", gpuNames);
                gpuComplete = gpuNames.Count > 0;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GPU query failed (non-fatal): {ex.Message}");
            }

            // ── OS version/build (registry read, no WMI) ─────────────────────────
            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string display = key?.GetValue("DisplayVersion")?.ToString() ?? "";
                string build = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
                osVersion = $"Windows {(String.IsNullOrWhiteSpace(display) ? "" : display + " ")}(build {build})".Trim();
            }
            catch
            {
                osVersion = $"Windows (build {Environment.OSVersion.Version.Build})";
            }

            // ── Display: reuse FpsUnlockerService's lightweight EnumDisplaySettings ──
            int refreshRate = 0;
            try { refreshRate = FpsUnlockerService.GetPrimaryDisplayRefreshRate(); } catch { }
            string resolution = GetPrimaryDisplayResolution();

            // ── Storage: delegate to the 3-state engine (SSD/HDD/Unknown, never guesses) ──
            string storageType = "Unknown";
            string storageConfidence = "None";
            string systemDrive = "";
            try
            {
                var storage = AutoOptimizeService.GetStorageDiagnostics();
                storageType = storage.Type switch
                {
                    AutoOptimizeService.StorageMediaType.Ssd => "SSD",
                    AutoOptimizeService.StorageMediaType.Hdd => "HDD",
                    _ => "Unknown"
                };
                storageConfidence = String.IsNullOrWhiteSpace(storage.Confidence) ? "None" : storage.Confidence;
                systemDrive = storage.SystemDrive;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Storage diagnostics unavailable (non-fatal): {ex.Message}");
            }

            ulong totalRam = 0;
            ulong availableRam = 0;
            try
            {
                totalRam = AutoOptimizeService.GetTotalPhysicalMemoryPublic();
                availableRam = AutoOptimizeService.GetAvailablePhysicalMemoryPublic();
            }
            catch { }

            var profile = new HardwareProfile
            {
                CpuName = String.IsNullOrWhiteSpace(cpuName) ? $"Unknown CPU ({logical} logical)" : cpuName,
                LogicalProcessors = logical,
                PhysicalCores = physicalCores,
                TotalRamBytes = totalRam,
                AvailableRamBytes = availableRam,
                MemoryPressure = ComputePressure(totalRam, availableRam),
                GpuName = String.IsNullOrWhiteSpace(gpuName) ? "(unknown)" : gpuName,
                HasDedicatedGpu = hasDedicatedGpu,
                GpuDetectionComplete = gpuComplete,
                StorageType = storageType,
                StorageConfidence = storageConfidence,
                SystemDrive = systemDrive,
                DisplayResolution = resolution,
                DisplayRefreshRate = refreshRate,
                OsVersion = osVersion
            };

            var (tier, reason) = ComputeTier(profile);
            App.Logger.WriteLine(LOG_IDENT,
                $"Hardware profile: tier={tier} — {reason} | CPU={profile.LogicalProcessors}L/{physicalCores}P, RAM={profile.TotalRamMb}MB, GPU={(hasDedicatedGpu ? "dGPU" : gpuComplete ? "iGPU" : "?")}, storage={storageType}");

            return profile with { Tier = tier, TierReason = reason };
        }

        private static HardwareProfile.MemoryPressureLevel ComputePressure(ulong total, ulong available)
        {
            if (total == 0)
                return HardwareProfile.MemoryPressureLevel.Low;

            return
                available < total / 8 ? HardwareProfile.MemoryPressureLevel.High :
                available < total / 4 ? HardwareProfile.MemoryPressureLevel.Moderate :
                HardwareProfile.MemoryPressureLevel.Low;
        }

        private static string GetPrimaryDisplayResolution()
        {
            try
            {
                // EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS) already exists in
                // FpsUnlockerService; replicate the width/height read without new P/Invoke.
                int refresh = FpsUnlockerService.GetPrimaryDisplayRefreshRate();
                if (refresh <= 0)
                    return "";
                // Resolution via System.Windows(SystemParameters) is only accurate for the
                // primary monitor at 96 DPI; instead read via GDI the same way refresh is read.
                return GetResolutionViaGdi();
            }
            catch
            {
                return "";
            }
        }

        private static string GetResolutionViaGdi()
        {
            try
            {
                int width = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 0;
                int height = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 0;
                return width > 0 && height > 0 ? $"{width}x{height}" : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Multi-signal tier decision (spec Phase 3: NOT RAM alone, NOT cores alone;
        /// storage must influence the result). Signals, in weight order:
        ///   1. logical CPU count
        ///   2. total physical RAM
        ///   3. dedicated GPU presence
        ///   4. storage class (HDD caps the tier; NVMe/SSD lets strong CPU+RAM qualify)
        /// A score is accumulated and mapped to the 5-tier model.
        /// </summary>
        public static (HardwareProfile.HardwareTier Tier, string Reason) ComputeTier(HardwareProfile profile)
        {
            int score = 0;
            var parts = new List<string>();

            // CPU signal
            if (profile.LogicalProcessors <= 2) { score -= 2; parts.Add($"{profile.LogicalProcessors} logical CPU (sangat terbatas)"); }
            else if (profile.LogicalProcessors == 3) { score -= 1; parts.Add($"{profile.LogicalProcessors} logical CPU"); }
            else if (profile.LogicalProcessors <= 4) { score += 1; parts.Add($"{profile.LogicalProcessors} logical CPU"); }
            else if (profile.LogicalProcessors <= 8) { score += 2; parts.Add($"{profile.LogicalProcessors} logical CPU"); }
            else { score += 3; parts.Add($"{profile.LogicalProcessors} logical CPU"); }

            ulong ramGb = profile.TotalRamBytes / (1024UL * 1024 * 1024);
            if (profile.TotalRamMb < 3800) { score -= 2; parts.Add($"RAM {profile.TotalRamMb / 1024}GB (sangat terbatas)"); }
            else if (profile.TotalRamMb < 6200) { score -= 1; parts.Add($"RAM {ramGb}GB"); }
            else if (profile.TotalRamMb < 12000) { score += 1; parts.Add($"RAM {ramGb}GB"); }
            else if (profile.TotalRamMb < 25000) { score += 2; parts.Add($"RAM {ramGb}GB"); }
            else { score += 3; parts.Add($"RAM {ramGb}GB"); }

            // GPU signal — only a tiebreaker, never the sole reason for a downgrade
            if (!profile.GpuDetectionComplete)
                parts.Add("GPU tidak terdeteksi (abaikan sinyal GPU)");
            else if (profile.HasDedicatedGpu) { score += 1; parts.Add("GPU dedicated"); }
            else
            {
                score -= 1;
                parts.Add("GPU integrated");
            }

            // Storage signal — HDD caps the achievable tier (the classic bottleneck);
            // Unknown does not guess: it only blocks the storage bonus.
            if (profile.StorageType == "HDD") { score -= 1; parts.Add("storage HDD (bottleneck I/O)"); }
            else if (profile.StorageType == "SSD") { score += 1; parts.Add("storage SSD"); }
            else { parts.Add("storage Unknown (tidak dinilai)"); }

            var tier = score switch
            {
                <= -3 => HardwareProfile.HardwareTier.UltraLow,
                <= -1 => HardwareProfile.HardwareTier.Low,
                <= 2 => HardwareProfile.HardwareTier.Balanced,
                <= 4 => HardwareProfile.HardwareTier.Mid,
                _ => HardwareProfile.HardwareTier.High
            };

            // HDD cap: 2 cores + 16GB + HDD is not "High" for game loading behavior.
            if (profile.StorageType == "HDD" && tier > HardwareProfile.HardwareTier.Balanced)
            {
                tier = HardwareProfile.HardwareTier.Balanced;
                parts.Add("tier dibatasi Balanced karena storage HDD");
            }

            return (tier, String.Join(", ", parts));
        }
    }
}
