using Bloxstrap.GameSession.Models;

namespace Bloxstrap.Integrations
{
    public enum DiagnosticStatus { Ok, Attention, Problem, Unknown }

    /// <summary>One line of the diagnostic report: status glyph + text + optional detail.</summary>
    public sealed class DiagnosticEntry
    {
        public required DiagnosticStatus Status { get; init; }
        public required string Text { get; init; }
        public string Detail { get; init; } = "";

        public string Glyph => Status switch
        {
            DiagnosticStatus.Ok => "✓",
            DiagnosticStatus.Attention => "⚠",
            DiagnosticStatus.Problem => "✕",
            _ => "?"
        };
    }

    public sealed class DiagnosticSection
    {
        public required string Title { get; init; }
        public required List<DiagnosticEntry> Entries { get; init; }
    }

    /// <summary>
    /// ── Diagnostic Center report builder (Phase 6) ───────────────────────────────
    /// Replaces the old MessageBox text dump. READ-ONLY, ON-DEMAND: called once when
    /// the user opens the Diagnostic Center. Reuses the already-cached storage engine
    /// and hardware profile; the only potentially-new WMI queries are the security
    /// detector's (cached for the diagnostic refresh, with a 3s WMI timeout) — none of
    /// this runs on a timer.
    ///
    /// Sections: Hardware, Storage, Roblox, Performance, Security, FastFlags, FPS,
    /// Warnings, Recommendations.
    /// </summary>
    public static class DiagnosticReportBuilder
    {
        private const string LOG_IDENT = "DiagnosticReportBuilder";

        public static List<DiagnosticSection> Build()
        {
            var sections = new List<DiagnosticSection>();
            var warnings = new List<DiagnosticEntry>();
            var recommendations = new List<DiagnosticEntry>();

            HardwareProfile profile;
            try { profile = HardwareProfileEngine.GetProfile(); }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Hardware profile failed: {ex.Message}");
                profile = new HardwareProfile { CpuName = "(unavailable)" };
            }

            sections.Add(BuildHardware(profile));
            sections.Add(BuildStorage(profile, warnings, recommendations));
            sections.Add(BuildRoblox(warnings, recommendations));
            sections.Add(BuildPerformance(profile, warnings, recommendations));
            sections.Add(BuildSecurity(warnings, recommendations));
            sections.Add(BuildFastFlags(warnings, recommendations));
            sections.Add(BuildFps(profile, warnings, recommendations));

            // Warnings & Recommendations aggregate everything flagged above.
            if (warnings.Count == 0)
                warnings.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = "Tidak ada peringatan aktif." });
            if (recommendations.Count == 0)
                recommendations.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = "Tidak ada rekomendasi baru — konfigurasi sesuai hardware." });

            sections.Add(new DiagnosticSection { Title = "Warnings", Entries = warnings });
            sections.Add(new DiagnosticSection { Title = "Recommendations", Entries = recommendations });

            return sections;
        }

        private static DiagnosticSection BuildHardware(HardwareProfile profile)
        {
            var entries = new List<DiagnosticEntry>
            {
                new() { Status = DiagnosticStatus.Ok, Text = $"CPU: {profile.CpuName} — {profile.LogicalProcessors} logical{(profile.PhysicalCores > 0 ? $" / {profile.PhysicalCores} physical" : "")}" },
                new() { Status = StatusForPressure(profile.MemoryPressure), Text = $"RAM: total {profile.TotalRamMb / 1024} GB, tersedia {profile.AvailableRamMb / 1024} GB — tekanan {PressureText(profile.MemoryPressure)}" },
                new() { Status = DiagnosticStatus.Ok, Text = $"GPU: {profile.GpuName} ({GpuClass(profile)})" },
                new()
                {
                    Status = String.IsNullOrEmpty(profile.DisplayResolution) ? DiagnosticStatus.Unknown : DiagnosticStatus.Ok,
                    Text = String.IsNullOrEmpty(profile.DisplayResolution)
                        ? "Display: resolusi/refresh rate tidak terbaca"
                        : $"Display: {profile.DisplayResolution} @ {profile.DisplayRefreshRate} Hz"
                },
                new() { Status = DiagnosticStatus.Ok, Text = $"OS: {profile.OsVersion}" },
                new()
                {
                    Status = DiagnosticStatus.Ok,
                    Text = $"Hardware tier: {profile.TierDisplay}",
                    Detail = profile.TierReason
                }
            };

            return new DiagnosticSection { Title = "Hardware", Entries = entries };
        }

        private static DiagnosticSection BuildStorage(HardwareProfile profile, List<DiagnosticEntry> warnings, List<DiagnosticEntry> recommendations)
        {
            var entries = new List<DiagnosticEntry>();
            AutoOptimizeService.StorageDetectionResult storage;
            try { storage = AutoOptimizeService.GetStorageDiagnostics(); }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Storage diagnostics failed: {ex.Message}");
                storage = new AutoOptimizeService.StorageDetectionResult
                {
                    Reason = $"Storage diagnostics failed: {ex.Message}"
                };
            }

            switch (profile.StorageType)
            {
                case "HDD":
                    entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = "HDD terdeteksi (bukti kuat, multi-sumber)" });
                    break;
                case "SSD":
                    entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = "SSD terdeteksi (bukti kuat, multi-sumber)" });
                    break;
                default:
                    entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Unknown, Text = "Tipe storage tidak bisa dipastikan (SSD/HDD sama-sama mungkin)" });
                    entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Unknown, Text = "BoneFish TIDAK menebak — tuning HDD-specific tidak diterapkan" });
                    recommendations.Add(new DiagnosticEntry
                    {
                        Status = DiagnosticStatus.Unknown,
                        Text = "Storage Unknown: bila Anda tahu system drive adalah SSD, jalankan \"Deteksi Ulang Hardware\"; bila tetap Unknown, detektor memang tidak menemukan bukti cukup (perilaku benar)."
                    });
                    break;
            }

            entries.Add(new DiagnosticEntry
            {
                Status = String.IsNullOrWhiteSpace(storage.PhysicalDiskIndex) || storage.PhysicalDiskIndex == "?" ? DiagnosticStatus.Unknown : DiagnosticStatus.Ok,
                Text = $"Physical disk: {storage.PhysicalDiskIndex}",
                Detail = String.IsNullOrWhiteSpace(storage.DiskModel) ? "" : $"Model: {storage.DiskModel} ({storage.BusType})"
            });
            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"System volume: {storage.SystemDrive} → disk fisik yang benar (bukan disk pertama)"
            });
            entries.Add(new DiagnosticEntry
            {
                Status = String.IsNullOrWhiteSpace(storage.Confidence) || storage.Confidence == "None" ? DiagnosticStatus.Unknown : DiagnosticStatus.Ok,
                Text = $"Confidence: {(String.IsNullOrWhiteSpace(storage.Confidence) ? "None" : storage.Confidence)}"
            });
            if (!String.IsNullOrWhiteSpace(storage.Reason))
                entries.Add(new DiagnosticEntry
                {
                    Status = DiagnosticStatus.Unknown,
                    Text = $"Alasan deteksi: {storage.Reason}",
                    Detail = String.Join("; ", storage.Diagnostics)
                });

            if (profile.StorageType == "HDD")
                warnings.Add(new DiagnosticEntry
                {
                    Status = DiagnosticStatus.Attention,
                    Text = "System drive HDD — Fast Loading dan trim memori otomatis berperilaku konservatif untuk menghindari kontensi disk."
                });

            return new DiagnosticSection { Title = "Storage", Entries = entries };
        }

        private static DiagnosticSection BuildRoblox(List<DiagnosticEntry> warnings, List<DiagnosticEntry> recommendations)
        {
            var entries = new List<DiagnosticEntry>();

            try
            {
                Process[] procs = Process.GetProcessesByName("RobloxPlayerBeta");
                if (procs.Length == 0)
                {
                    entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = "Roblox tidak sedang berjalan (normal — hanya relevan saat main)" });
                    recommendations.Add(new DiagnosticEntry
                    {
                        Status = DiagnosticStatus.Ok,
                        Text = "Roblox tidak berjalan: diagnostik runtime (priority, FPS) akan lengkap setelah game diluncurkan."
                    });
                }
                else
                {
                    using var proc = procs[0];
                    string priority;
                    long wsMb = 0;
                    try { priority = proc.PriorityClass.ToString(); } catch { priority = "(tidak terbaca)"; }
                    try { wsMb = proc.WorkingSet64 / 1024 / 1024; } catch { }

                    entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = $"Roblox berjalan: PID {proc.Id}, priority {priority}, working set ~{wsMb} MB" });
                }
                foreach (Process p in procs) { try { p.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Unknown, Text = $"Status Roblox tidak terbaca: {ex.Message}" });
            }

            return new DiagnosticSection { Title = "Roblox", Entries = entries };
        }

        private static DiagnosticSection BuildPerformance(HardwareProfile profile, List<DiagnosticEntry> warnings, List<DiagnosticEntry> recommendations)
        {
            var entries = new List<DiagnosticEntry>();
            string preset = App.Settings.Prop.SelectedPerformancePreset ?? "None";

            entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = $"Preset aktif: {preset}" });
            entries.Add(new DiagnosticEntry
            {
                Status = App.Settings.Prop.ForceExtremeMode ? DiagnosticStatus.Attention : DiagnosticStatus.Ok,
                Text = App.Settings.Prop.ForceExtremeMode
                    ? "ForceExtremeMode aktif — tier efektif dipaksa ExtremePerformance mengabaikan hardware asli (intent eksplisit user)"
                    : "ForceExtremeMode OFF — tier mengikuti hardware asli"
            });
            entries.Add(new DiagnosticEntry
            {
                Status = App.Settings.Prop.OptimizeForLowEnd ? DiagnosticStatus.Attention : DiagnosticStatus.Ok,
                Text = App.Settings.Prop.OptimizeForLowEnd
                    ? "OptimizeForLowEnd aktif (auto via tier detection atau pilihan user)"
                    : "OptimizeForLowEnd OFF"
            });

            bool fastLoading = App.Settings.Prop.EnableFastLoadingFlags;
            entries.Add(new DiagnosticEntry
            {
                Status = fastLoading && profile.StorageType == "HDD" ? DiagnosticStatus.Attention : DiagnosticStatus.Ok,
                Text = $"Fast Loading: {(fastLoading ? "ON" : "OFF")}" + (fastLoading && profile.StorageType == "HDD" ? " (HDD — paralelisme aset dapat menaikkan kontensi disk)" : "")
            });
            if (fastLoading && profile.StorageType == "HDD")
            {
                warnings.Add(new DiagnosticEntry { Status = DiagnosticStatus.Attention, Text = "Fast Loading aktif di atas HDD — bila loading Roblox membuat disk usage menempel 100%, pertimbangkan mematikan toggle ini." });
                recommendations.Add(new DiagnosticEntry
                {
                    Status = DiagnosticStatus.Attention,
                    Text = "Matikan Fast Loading jika Roblox loading membuat disk usage tetap mendekati 100%; manfaat flag thread lokal belum terverifikasi."
                });
            }

            entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = $"TDR Mitigation: {(App.Settings.Prop.EnableTdrMitigation ? "ON (Roblox graphics quality set low)" : "OFF")}" });
            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"Memory mode: trim otomatis {(profile.StorageType == "SSD" && profile.TotalRamMb < 5120 ? "aktif (SSD + RAM <5GB)" : "tidak aktif (butuh SSD terkonfirmasi + RAM <5GB)")}"
            });

            return new DiagnosticSection { Title = "Performance", Entries = entries };
        }

        private static DiagnosticSection BuildSecurity(List<DiagnosticEntry> warnings, List<DiagnosticEntry> recommendations)
        {
            var entries = new List<DiagnosticEntry>();

            SecurityDetectionState state;
            string message;
            int knownNames;
            int knownPaths;
            int productCount;
            try
            {
                App.GameSession.Detector.Refresh();
                state = App.GameSession.Detector.State;
                message = App.GameSession.Detector.Message;
                knownNames = App.GameSession.Detector.KnownSecurityProcessNames.Count;
                knownPaths = App.GameSession.Detector.KnownSecurityExecutablePaths.Count;
                productCount = App.GameSession.Detector.DetectedProducts.Count;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Security refresh failed: {ex.Message}");
                state = SecurityDetectionState.Unavailable;
                message = ex.Message;
                knownNames = knownPaths = productCount = 0;
            }

            entries.Add(new DiagnosticEntry
            {
                Status = state == SecurityDetectionState.Ok ? DiagnosticStatus.Ok : DiagnosticStatus.Attention,
                Text = state switch
                {
                    SecurityDetectionState.Ok => "Windows Security: terdeteksi dan berjalan",
                    SecurityDetectionState.Degraded => "Windows Security: TERDETEKSI TIDAK LENGKAP (degraded)",
                    _ => "Windows Security: tidak bisa dideteksi"
                },
                Detail = message
            });

            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"Defender: proses inti (MsMpEng, MsSense, NisSrv, SecurityHealth*) selalu dilindungi terlepas dari status deteksi — {knownNames} nama + {knownPaths} path terdaftar di guard"
            });

            if (productCount > 0)
            {
                string products = String.Join(", ", App.GameSession.Detector.DetectedProducts);
                entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = $"Produk keamanan terdaftar: {products}" });
            }
            else
            {
                entries.Add(new DiagnosticEntry
                {
                    Status = DiagnosticStatus.Unknown,
                    Text = "Tidak ada produk keamanan terbaca dari Security Center (wscsvc mati/disable atau WMI diblokir)"
                });
                recommendations.Add(new DiagnosticEntry
                {
                    Status = DiagnosticStatus.Attention,
                    Text = "Security Center tidak terbaca: Windows mungkin dalam keadaan debloated/keamanan nonaktif. BoneFish TIDAK mengubah Windows Security apa pun — periksa Windows Security secara manual (Virus & threat protection)."
                });
            }

            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = "BoneFish TIDAK pernah menonaktifkan / memodifikasi Windows Security, Defender, atau Firewall"
            });

            if (state != SecurityDetectionState.Ok)
                warnings.Add(new DiagnosticEntry
                {
                    Status = DiagnosticStatus.Attention,
                    Text = $"Windows Security reports an issue (deteksi {state}). BoneFish will NOT automatically suppress or bypass this warning. Detail: {message}"
                });

            return new DiagnosticSection { Title = "Security", Entries = entries };
        }

        private static DiagnosticSection BuildFastFlags(List<DiagnosticEntry> warnings, List<DiagnosticEntry> recommendations)
        {
            var entries = new List<DiagnosticEntry>();

            string Flag(string name)
            {
                try { return App.FastFlags.GetValue(name) ?? "(not set)"; }
                catch { return "(error)"; }
            }

            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"LOD dalam konfigurasi lokal: CSG {Flag("DFIntCSGLevelOfDetailSwitchingDistance")} / L12 {Flag("DFIntCSGLevelOfDetailSwitchingDistanceL12")} / L23 {Flag("DFIntCSGLevelOfDetailSwitchingDistanceL23")} / L34 {Flag("DFIntCSGLevelOfDetailSwitchingDistanceL34")}"
            });
            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"TextureCompositorJobs: {Flag("DFIntTextureCompositorActiveJobs")}"
            });

            string[] rejectedFlags = FastFlagManager.FlagsRejectedByRobloxLogs.ToArray();
            string[] stillConfiguredRejectedFlags = rejectedFlags
                .Where(flag => App.FastFlags.GetValue(flag) is not null)
                .ToArray();
            entries.Add(new DiagnosticEntry
            {
                Status = stillConfiguredRejectedFlags.Length == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Attention,
                Text = stillConfiguredRejectedFlags.Length == 0
                    ? $"BoneFish memblokir {rejectedFlags.Length} flag yang tercatat ditolak pada log Roblox 0.741; penerimaan flag lain hanya dapat dipastikan dari log Roblox."
                    : $"Flag yang tercatat ditolak masih tersimpan: {string.Join(", ", stillConfiguredRejectedFlags)}"
            });

            string msaa = Flag("FIntDebugForceMSAASamples");
            bool tdrMitigationEnabled = App.Settings.Prop.EnableTdrMitigation;
            entries.Add(new DiagnosticEntry
            {
                Status = tdrMitigationEnabled && msaa != "1" ? DiagnosticStatus.Attention : DiagnosticStatus.Ok,
                Text = $"MSAA configuration: {(msaa == "(not set)" ? "Roblox default" : $"force {msaa}x")} (TDR mitigation {(tdrMitigationEnabled ? "ON" : "OFF")}; Roblox acceptance must be confirmed from its log)"
            });

            bool deprecatedPresent = false;
            foreach (string deprecated in new[] { "DFIntTaskSchedulerTargetFps", "DFIntDebugFRMQualityLevelOverride", "FIntTextureCompositorLowResFactor", "FFlagFastGPULightCulling3", "FFlagNewLightAttenuation", "FIntRenderShadowIntensity", "DFFlagDebugPauseVoxelizer" })
            {
                string value = Flag(deprecated);
                if (value != "(not set)")
                {
                    deprecatedPresent = true;
                    entries.Add(new DiagnosticEntry
                    {
                        Status = DiagnosticStatus.Attention,
                        Text = $"Flag lama {deprecated}={value} masih tertulis (di luar allowlist — client mengabaikannya; dibersihkan otomatis saat Play berikutnya)"
                    });
                }
            }
            if (!deprecatedPresent)
                entries.Add(new DiagnosticEntry { Status = DiagnosticStatus.Ok, Text = "Tidak ada deprecated flag (DFIntTaskSchedulerTargetFps, FRM override, texture override, Night Vision) tertulis ✓" });

            return new DiagnosticSection { Title = "FastFlags", Entries = entries };
        }

        private static DiagnosticSection BuildFps(HardwareProfile profile, List<DiagnosticEntry> warnings, List<DiagnosticEntry> recommendations)
        {
            var entries = new List<DiagnosticEntry>();

            entries.Add(new DiagnosticEntry
            {
                Status = profile.DisplayRefreshRate > 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Unknown,
                Text = profile.DisplayRefreshRate > 0
                    ? $"Refresh rate monitor utama: {profile.DisplayRefreshRate} Hz"
                    : "Refresh rate monitor tidak terbaca"
            });

            string? effectiveCap = null;
            try
            {
                if (!App.GlobalSettings.Loaded)
                    App.GlobalSettings.Load();
                effectiveCap = App.GlobalSettings.GetPreset("Rendering.FramerateCap");
            }
            catch { }

            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"FramerateCap efektif: {(String.IsNullOrWhiteSpace(effectiveCap) ? "(default Roblox / 60)" : effectiveCap)}"
            });
            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"Cap dikelola BoneFish: {(App.Settings.Prop.FpsUnlockerCapManaged ? $"ya ({(App.Settings.Prop.FpsUnlockerEnabled ? "ON" : "OFF")})" : "tidak")}"
                    + (App.Settings.Prop.FpsUnlockerPreviousCap is { } prev ? $", cap user sebelumnya: {prev}" : "")
            });
            int configuredCap = FpsUnlockerService.GetConfiguredFramerateCap();
            entries.Add(new DiagnosticEntry
            {
                Status = DiagnosticStatus.Ok,
                Text = $"FPS cap BoneFish: {(App.Settings.Prop.FpsUnlockerEnabled
                    ? $"ON — cap {configuredCap} FPS"
                    : "OFF")}"
            });

            return new DiagnosticSection { Title = "FPS", Entries = entries };
        }

        private static DiagnosticStatus StatusForPressure(HardwareProfile.MemoryPressureLevel pressure) => pressure switch
        {
            HardwareProfile.MemoryPressureLevel.Low => DiagnosticStatus.Ok,
            HardwareProfile.MemoryPressureLevel.Moderate => DiagnosticStatus.Attention,
            _ => DiagnosticStatus.Problem
        };

        private static string PressureText(HardwareProfile.MemoryPressureLevel pressure) => pressure switch
        {
            HardwareProfile.MemoryPressureLevel.Low => "rendah",
            HardwareProfile.MemoryPressureLevel.Moderate => "moderate",
            _ => "tinggi — tutup aplikasi besar bila Roblox patah-patah"
        };

        private static string GpuClass(HardwareProfile profile) =>
            !profile.GpuDetectionComplete ? "tipe tidak pasti" :
            profile.HasDedicatedGpu ? "dedicated" : "integrated";
    }
}
