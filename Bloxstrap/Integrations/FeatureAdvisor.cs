namespace Bloxstrap.Integrations
{
    /// <summary>
    /// ── Adaptive Feature Availability (Phase 4) + Why-Recommendations (Phase 7) ──
    /// Classifies every BoneFish feature against the detected hardware profile.
    /// Features are NEVER hard-disabled by tier — the classification only drives
    /// guidance in the UI (Recommended / Optional / Advanced / NotRecommended /
    /// Unavailable). Unavailable is reserved for objectively impossible states
    /// (e.g. FPS Unlocker with no readable refresh rate).
    /// </summary>
    public enum FeatureAvailability
    {
        Recommended,
        Optional,
        Advanced,
        NotRecommended,
        Unavailable
    }

    public sealed class FeatureAdvice
    {
        public required string Feature { get; init; }
        public required FeatureAvailability Availability { get; init; }
        public required string Reason { get; init; }        // WHY — hardware facts, not slogans
        public required string Recommendation { get; init; } // "Recommended: ON/OFF/…"
    }

    public static class FeatureAdvisor
    {
        public static FeatureAdvice FastLoading(HardwareProfile profile, bool currentlyEnabled)
        {
            bool hdd = profile.StorageType == "HDD";
            bool ssd = profile.StorageType == "SSD";
            bool lowCores = profile.LogicalProcessors < 4;

            string why = (profile.StorageType, lowCores) switch
            {
                ("HDD", false) => $"Fast Loading tersedia. CPU Anda punya {profile.LogicalProcessors} logical processor. Karena system drive Anda HDD, menaikkan paralelisme aset (texture compositor jobs) dapat meningkatkan kontensi disk dan membuat loading justru terasa patah-patah.",
                ("HDD", true) => $"CPU hanya {profile.LogicalProcessors} logical processor dan system drive HDD — paralelisme tambahan tidak punya core untuk bekerja dan menambah kontensi disk.",
                ("SSD", false) => $"System drive Anda SSD. Fast Loading tersedia dan seharusnya memiliki kontensi storage rendah — paralelisme aset dapat dipakai dengan aman.",
                ("SSD", true) => $"System drive SSD, tetapi CPU hanya {profile.LogicalProcessors} logical processor. Manfaat paralelisme kecil; flag utama (runtime threads) tetap dilewati di bawah 8 core.",
                (_, true) => "Tipe storage tidak bisa diverifikasi. BoneFish menghindari tuning HDD-specific, dan paralelisme aset tetap terbatas di bawah 4 logical CPU.",
                _ => "Tipe storage tidak bisa diverifikasi. BoneFish akan menghindari tuning HDD-specific; paralelisme tetap aman dicoba namun manfaatnya belum terukur."
            };

            FeatureAvailability availability =
                (hdd && lowCores) ? FeatureAvailability.NotRecommended :
                hdd ? FeatureAvailability.Optional :
                lowCores ? FeatureAvailability.Optional :
                FeatureAvailability.Recommended;

            string recommendation = availability switch
            {
                FeatureAvailability.NotRecommended => hdd && lowCores
                    ? "Recommended: OFF — matikan jika disk usage tetap mendekati 100% saat Roblox loading."
                    : "Recommended: OFF jika loading terasa patah-patah.",
                FeatureAvailability.Optional => "Recommended: OFF bila HDD; boleh dicoba ON dan dimatikan kembali bila loading memburuk.",
                _ => currentlyEnabled ? "Status: ON — sesuai untuk hardware ini." : "Recommended: ON boleh dicoba."
            };

            return new FeatureAdvice
            {
                Feature = "Fast Loading",
                Availability = availability,
                Reason = why,
                Recommendation = recommendation
            };
        }

        public static FeatureAdvice FpsUnlocker(HardwareProfile profile, bool currentlyEnabled, bool refreshRateKnown)
        {
            if (!refreshRateKnown || profile.DisplayRefreshRate <= 0)
            {
                return new FeatureAdvice
                {
                    Feature = "FPS Unlocker",
                    Availability = FeatureAvailability.Unavailable,
                    Reason = "Refresh rate monitor tidak bisa dibaca, sehingga cap FPS otomatis tidak bisa dihitung dengan aman.",
                    Recommendation = "Recommended: tidak tersedia di konfigurasi display ini."
                };
            }

            bool integrated = profile.GpuDetectionComplete && !profile.HasDedicatedGpu;
            bool lowEnd = profile.Tier is HardwareProfile.HardwareTier.UltraLow or HardwareProfile.HardwareTier.Low;
            int cap = FpsUnlockerService.GetRecommendedFramerateCap(profile);

            string why = lowEnd || (profile.PhysicalCores is > 0 and <= 2 && profile.TotalRamMb <= 8192 && integrated)
                ? $"Hardware tier {profile.TierDisplay}{(integrated ? " dengan GPU integrated" : "")}. Cap otomatis {cap} FPS membatasi beban agar lebih sesuai dengan kemampuan hardware dan refresh rate monitor."
                : integrated
                    ? $"GPU integrated ({profile.GpuName}), tier {profile.TierDisplay}. Cap otomatis {cap} FPS mengikuti tier dan refresh rate monitor ({profile.DisplayRefreshRate} Hz)."
                    : $"GPU dedicated terdeteksi ({profile.GpuName}), tier {profile.TierDisplay}. Cap otomatis {cap} FPS mengikuti tier dan refresh rate monitor ({profile.DisplayRefreshRate} Hz).";

            return new FeatureAdvice
            {
                Feature = "FPS Unlocker",
                Availability = lowEnd || integrated ? FeatureAvailability.Optional : FeatureAvailability.Recommended,
                Reason = why,
                Recommendation = lowEnd || integrated ? "Recommended: optional — biarkan OFF jika FPS sudah stabil." : "Status: cocok untuk hardware ini."
            };
        }

        public static FeatureAdvice TdrMitigation(HardwareProfile profile, bool currentlyEnabled)
        {
            bool integrated = profile.GpuDetectionComplete && !profile.HasDedicatedGpu;

            return new FeatureAdvice
            {
                Feature = "TDR Mitigation",
                Availability = integrated ? FeatureAvailability.Recommended : FeatureAvailability.Optional,
                Reason = integrated
                    ? $"GPU integrated terdeteksi ({profile.GpuName}). TDR (layar putih/freeze sesaat, Event Viewer 4101) paling umum pada iGPU legacy; mode ini menurunkan MSAA untuk mengurangi beban per-pixel."
                    : "TDR terutama menyerang iGPU legacy; dengan GPU dedicated/sistem sehat, mode ini biasanya tidak diperlukan namun tidak berbahaya.",
                Recommendation = integrated
                    ? (currentlyEnabled ? "Status: ON — sesuai." : "Recommended: ON bila pernah mengalami freeze/layar putih.")
                    : "Recommended: OFF kecuali pernah melihat Event ID 4101."
            };
        }

        public static FeatureAdvice MemoryOptimization(HardwareProfile profile)
        {
            bool ssd = profile.StorageType == "SSD";
            bool lowRam = profile.TotalRamMb < 6200;

            return new FeatureAdvice
            {
                Feature = "Memory Optimization (working-set trim)",
                Availability = (lowRam && ssd) ? FeatureAvailability.Recommended :
                               lowRam ? FeatureAvailability.Optional :
                               FeatureAvailability.NotRecommended,
                Reason = lowRam && ssd
                    ? $"RAM {profile.TotalRamMb / 1024}GB + SSD: trim working-set aman (page-in cepat) dan melegakan tekanan memori."
                    : lowRam
                        ? $"RAM {profile.TotalRamMb / 1024}GB tetapi storage {(profile.StorageType == "HDD" ? "HDD" : "Unknown")}: EmptyWorkingSet memaksa page-in balik dari disk saat proses butuh memorinya lagi — di HDD ini memicu disk storm. Trim HANYA dijalankan otomatis pada SSD terkonfirmasi."
                        : $"RAM {profile.TotalRamMb / 1024}GB cukup; trim justru membuang cache yang masih dipakai.",
                Recommendation = lowRam && ssd ? "Status: otomatis (hanya SSD + RAM <5GB, perilaku existing dipertahankan)." : "Recommended: biarkan otomatis — tidak diaktifkan untuk hardware ini."
            };
        }

        public static FeatureAdvice ManualFastFlags(HardwareProfile profile)
        {
            bool lowEnd = profile.Tier is HardwareProfile.HardwareTier.UltraLow or HardwareProfile.HardwareTier.Low;

            return new FeatureAdvice
            {
                Feature = "Manual FastFlag Editor",
                Availability = FeatureAvailability.Advanced,
                Reason = "Editor manual selalu tersedia. Flag tidak berada di allowlist Roblox akan diabaikan client; kombinasi override render yang salah terbukti menyebabkan layar putih di iGPU tua.",
                Recommendation = lowEnd
                    ? "Advanced: untuk low-end, mulai dari preset dulu — edit manual hanya bila paham flag-nya."
                    : "Advanced: aman digunakan; kembalikan ke preset bila visual rusak."
            };
        }

        public static FeatureAdvice NetworkOptimization(HardwareProfile profile)
        {
            return new FeatureAdvice
            {
                Feature = "Network Optimization",
                Availability = FeatureAvailability.Optional,
                Reason = "Flag jaringan (packet limit/MTU) tidak bergantung hardware; efeknya tergantung koneksi, bukan CPU/GPU/storage.",
                Recommendation = "Recommended: boleh ON di semua tier."
            };
        }

        public static FeatureAdvice BackgroundMonitoring(HardwareProfile profile)
        {
            bool lowEnd = profile.Tier is HardwareProfile.HardwareTier.UltraLow or HardwareProfile.HardwareTier.Low
                || profile.StorageType == "HDD";

            return new FeatureAdvice
            {
                Feature = "FPS Monitor / Rich Presence / Notifikasi Roblox",
                Availability = lowEnd ? FeatureAvailability.NotRecommended : FeatureAvailability.Optional,
                Reason = lowEnd
                    ? $"Di tier {profile.TierDisplay} dengan storage {HardwareProfile.StorageDisplay(profile.StorageType)}, overlay/monitor tambahan menambah kerja UI thread dan I/O kecil yang terasa di sesi panjang."
                    : "Overlay/monitor berjalan hanya saat Roblox aktif; dampak idle nol.",
                Recommendation = lowEnd ? "Recommended: OFF kecuali dibutuhkan." : "Recommended: sesuai selera."
            };
        }
    }
}
