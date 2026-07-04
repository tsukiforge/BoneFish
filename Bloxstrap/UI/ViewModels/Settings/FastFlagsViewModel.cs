using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Enums.FlagPresets;
using System.Windows;
using Bloxstrap.UI.Elements.Settings.Pages;
using Wpf.Ui.Mvvm.Contracts;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class FastFlagsViewModel : NotifyPropertyChangedViewModel
    {
        private Dictionary<string, object>? _preResetFlags;

        public event EventHandler? RequestPageReloadEvent;
        
        public event EventHandler? OpenFlagEditorEvent;

        public event EventHandler<string>? RequestNotificationEvent;

        private void OpenFastFlagEditor() => OpenFlagEditorEvent?.Invoke(this, EventArgs.Empty);

        private void Notify(string message) => RequestNotificationEvent?.Invoke(this, message);

        public ICommand OpenFastFlagEditorCommand => new RelayCommand(OpenFastFlagEditor);

        public ICommand ApplyRecommendedFastFlagsCommand => new RelayCommand(ApplyRecommendedFastFlags);

        public ICommand ApplyRecommendedNetworkSettingsCommand => new RelayCommand(ApplyRecommendedNetworkSettings);

        public ICommand ApplyRecommendedStabilityPresetCommand => new RelayCommand(ApplyRecommendedStabilityPreset);

        public ICommand ApplyUltraLowSpecPresetCommand => new RelayCommand(ApplyUltraLowSpecPreset);

        public ICommand ApplyBalancedPresetCommand => new RelayCommand(ApplyBalancedPreset);

        public ICommand ApplyExtremePerformancePresetCommand => new RelayCommand(ApplyExtremePerformancePreset);

        public ICommand ToggleNightVisionCommand => new RelayCommand(ToggleNightVision);

        public ICommand ClearClientAppSettingsCommand => new RelayCommand(ClearClientAppSettings);
        public bool UseFastFlagManager
        {
            get => App.Settings.Prop.UseFastFlagManager;
            set => App.Settings.Prop.UseFastFlagManager = value;
        }

        public IReadOnlyDictionary<MSAAMode, string?> MSAALevels => FastFlagManager.MSAAModes;

        public MSAAMode SelectedMSAALevel
        {
            get => MSAALevels.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.MSAA")).Key;
            set => App.FastFlags.SetPreset("Rendering.MSAA", MSAALevels[value]);
        }

        public IReadOnlyDictionary<RenderingMode, string> RenderingModes => FastFlagManager.RenderingModes;

        public RenderingMode SelectedRenderingMode
        {
            get => App.FastFlags.GetPresetEnum(RenderingModes, "Rendering.Mode", "True");
            set
            {
                if (value != RenderingMode.Vulkan)
                    App.Settings.Prop.FakeBorderlessFullscreen = false; // vulkan exclusive

                App.FastFlags.SetPresetEnum("Rendering.Mode", value.ToString(), "True");
            }
        }

        public bool FixDisplayScaling
        {
            get => App.FastFlags.GetPreset("Rendering.DisableScaling") == "True";
            set => App.FastFlags.SetPreset("Rendering.DisableScaling", value ? "True" : null);
        }

        public IReadOnlyDictionary<TextureQuality, string?> TextureQualities => FastFlagManager.TextureQualityLevels;

        public TextureQuality SelectedTextureQuality
        {
            get => TextureQualities.Where(x => x.Value == App.FastFlags.GetPreset("Rendering.TextureQuality.Level")).FirstOrDefault().Key;
            set
            {
                if (value == TextureQuality.Default)
                {
                    App.FastFlags.SetPreset("Rendering.TextureQuality", null);
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.TextureQuality.OverrideEnabled", "True");
                    App.FastFlags.SetPreset("Rendering.TextureQuality.Level", TextureQualities[value]);
                }
            }
        }

        private static readonly string[] LODLevels = { "L0", "L12", "L23", "L34" };

        public bool FRMQualityOverrideEnabled
        {
            get => App.FastFlags.GetPreset("Rendering.FRMQualityOverride") != null;
            set
            {
                if (value)
                    FRMQualityOverride = 21;
                else
                    App.FastFlags.SetPreset("Rendering.FRMQualityOverride", null);

                OnPropertyChanged(nameof(FRMQualityOverride));
                OnPropertyChanged(nameof(FRMQualityOverrideEnabled));
            }
        }

        public int FRMQualityOverride
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.FRMQualityOverride"), out var x) ? x : 21;
            set
            {
                App.FastFlags.SetPreset("Rendering.FRMQualityOverride", value);

                OnPropertyChanged(nameof(FRMQualityOverride));
            }
        }

        public bool MeshQualityEnabled
        {
            get => App.FastFlags.GetPreset("Geometry.MeshLOD.Static") != null;
            set
            {
                if (value)
                {
                    // we enable level 3 by default
                    MeshQuality = 3;
                }
                else
                {
                    foreach (string level in LODLevels)
                        App.FastFlags.SetPreset($"Geometry.MeshLOD.{level}", null);

                    App.FastFlags.SetPreset("Geometry.MeshLOD.Static", null);
                }

                OnPropertyChanged(nameof(MeshQualityEnabled));
            }
        }

        public int MeshQuality
        {
            get => int.TryParse(App.FastFlags.GetPreset("Geometry.MeshLOD.Static"), out var x) ? x : 0;
            set
            {
                // holy..
                int clamped = Math.Clamp(value, 0, LODLevels.Length - 1);

                for (int i = 0; i < LODLevels.Length; i++)
                {
                    int lodValue = (Math.Clamp(clamped - i, 0, 3) + 1) * 250;
                    string lodLevel = LODLevels[i];

                    App.FastFlags.SetPreset($"Geometry.MeshLOD.{lodLevel}", lodValue);
                }

                App.FastFlags.SetPreset("Geometry.MeshLOD.Static", clamped);
                OnPropertyChanged(nameof(MeshQuality));
                OnPropertyChanged(nameof(MeshQualityEnabled));
            }
        }

        public bool ResetConfiguration
        {
            get => _preResetFlags is not null;

            set
            {
                if (value)
                {
                    _preResetFlags = new(App.FastFlags.Prop);
                    App.FastFlags.Prop.Clear();
                }
                else
                {
                    App.FastFlags.Prop = _preResetFlags!;
                    _preResetFlags = null;
                }

                RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool DisableRobloxAnimations
        {
            get => App.FastFlags.GetValue("FFlagRenderUIAnimations") == "False";
            set
            {
                if (value)
                {
                    App.FastFlags.SetValue("FFlagRenderUIAnimations", "False");
                    App.FastFlags.SetValue("FFlagRenderMenuTransitions", "False");
                    App.FastFlags.SetValue("FFlagRenderInventoryEffects", "False");
                }
                else
                {
                    App.FastFlags.SetValue("FFlagRenderUIAnimations", null);
                    App.FastFlags.SetValue("FFlagRenderMenuTransitions", null);
                    App.FastFlags.SetValue("FFlagRenderInventoryEffects", null);
                }
                OnPropertyChanged(nameof(DisableRobloxAnimations));
            }
        }

        public bool EnableLowMemoryMode
        {
            get => App.FastFlags.GetValue("FFlagLuaAppEnableLowMemoryMode") == "True";
            set
            {
                if (value)
                {
                    App.FastFlags.SetValue("FFlagLuaAppEnableLowMemoryMode", "True");
                }
                else
                {
                    App.FastFlags.SetValue("FFlagLuaAppEnableLowMemoryMode", null);
                }
                OnPropertyChanged(nameof(EnableLowMemoryMode));
            }
        }

        public string SelectedPreset
        {
            get => App.Settings.Prop.SelectedPerformancePreset;
            set
            {
                App.Settings.Prop.SelectedPerformancePreset = value;
                OnPropertyChanged(nameof(SelectedPreset));
                OnPropertyChanged(nameof(IsUltraLowActive));
                OnPropertyChanged(nameof(IsBalancedActive));
                OnPropertyChanged(nameof(IsStableActive));
                OnPropertyChanged(nameof(IsAutoOptimizeActive));
                OnPropertyChanged(nameof(IsExtremePerformanceActive));
            }
        }

        public bool IsUltraLowActive => SelectedPreset == "UltraLow";
        public bool IsBalancedActive => SelectedPreset == "Balanced";
        public bool IsStableActive => SelectedPreset == "Stable";
        public bool IsAutoOptimizeActive => SelectedPreset == "AutoOptimize";
        public bool IsExtremePerformanceActive => SelectedPreset == "ExtremePerformance";

        /// <summary>
        /// Toggle ForceExtremeMode — memungkinkan user memaksa Potato Mode
        /// tanpa harus punya hardware yang terdeteksi sebagai UltraLow.
        /// </summary>
        public bool ForceExtremeMode
        {
            get => App.Settings.Prop.ForceExtremeMode;
            set
            {
                App.Settings.Prop.ForceExtremeMode = value;
                OnPropertyChanged(nameof(ForceExtremeMode));
            }
        }

        /// <summary>
        /// Target FPS untuk TaskScheduler pada Extreme/UltraLow mode.
        /// Default 30; minimum 24 untuk perangkat paling lemah.
        /// </summary>
        public int ExtremeModeFpsTarget
        {
            get => App.Settings.Prop.ExtremeModeFpsTarget;
            set
            {
                App.Settings.Prop.ExtremeModeFpsTarget = Math.Clamp(value, 24, 60);
                OnPropertyChanged(nameof(ExtremeModeFpsTarget));
            }
        }

        private void ApplyRecommendedFastFlags()
        {
            // Bersihkan semua flag lama sebelum apply preset baru.
            Integrations.AutoOptimizeService.PurgeAllKnownFlags();

            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x1;
            SelectedTextureQuality = TextureQuality.Level0;
            MeshQualityEnabled = true;
            MeshQuality = 0;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 21;
            DisableRobloxAnimations = true;
            EnableLowMemoryMode = true;

            SelectedPreset = "AutoOptimize";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Auto-optimize FastFlags telah diterapkan.");
        }

        private void ApplyRecommendedNetworkSettings()
        {
            App.Settings.Prop.EnableBetterMatchmaking = true;
            App.Settings.Prop.EnableBetterMatchmakingRandomization = true;

            // DNS/No Delay Network Flags
            App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
            App.FastFlags.SetValue("DFIntMaxReceivePPS", "50000");
            App.FastFlags.SetValue("DFIntMaxSendPPS", "50000");
            App.FastFlags.SetValue("DFIntConnectionMTUSize", "1500");
            App.FastFlags.SetValue("DFIntOptimizeSendQueue", "1");

            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Auto-optimize jaringan & No Delay telah diterapkan.");
        }

        private void ApplyRecommendedStabilityPreset()
        {
            ApplyRecommendedFastFlags();
            ApplyRecommendedNetworkSettings();

            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            SelectedPreset = "Stable";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Preset stabil untuk low-spec telah diterapkan.");
        }

        private void ApplyUltraLowSpecPreset()
        {
            // Bersihkan semua flag lama sebelum apply preset baru.
            Integrations.AutoOptimizeService.PurgeAllKnownFlags();

            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x1;
            SelectedTextureQuality = TextureQuality.Level0;
            MeshQualityEnabled = true;
            MeshQuality = 0;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 1;

            // LOD — semua level 250 sesuai ultra low-spec.json
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance",       "250");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL12",    "250");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL23",    "250");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34",    "250");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceStatic", "0");

            // Anti-crash: batasi FPS, light updates (Max=4 bukan 1 — nilai 1 bug gelap),
            // dan texture compositor jobs.
            App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", "30");
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "4");
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "1");

            DisableRobloxAnimations = true;
            EnableLowMemoryMode = true;

            // Network flags langsung
            App.Settings.Prop.EnableBetterMatchmaking = true;
            App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
            App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
            App.FastFlags.SetValue("DFIntMaxReceivePPS",        "50000");
            App.FastFlags.SetValue("DFIntMaxSendPPS",           "50000");
            App.FastFlags.SetValue("DFIntConnectionMTUSize",    "1500");
            App.FastFlags.SetValue("DFIntOptimizeSendQueue",    "1");

            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }

            SelectedPreset = "UltraLow";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Ultra low-spec preset untuk PC spek kentang telah diterapkan.");
        }

        private void ApplyBalancedPreset()        {
            // Bersihkan semua flag lama sebelum apply preset baru.
            Integrations.AutoOptimizeService.PurgeAllKnownFlags();

            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x2;
            SelectedTextureQuality = TextureQuality.Level1;
            MeshQualityEnabled = true;
            MeshQuality = 1;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 15;

            // Balanced lighting
            App.FastFlags.SetPreset("Rendering.LightingMode", "Default");
            App.FastFlags.SetPreset("Terrain.GridV2", "False");

            ApplyRecommendedNetworkSettings();

            SelectedPreset = "Balanced";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Preset seimbang telah diterapkan.");
        }

        /// <summary>
        /// Preset "ExtremePerformance" (Potato Mode).
        ///
        /// Filosofi: SAMA dengan UltraLow sebagai base, ditambah:
        ///  - Anti not-responding (telemetry off, animation tracks, light fade)
        ///  - FPS cap yang bisa dikonfigurasi user (default 30, min 24)
        ///  - Rendering asset lebih cepat muncul (LOD distance diperbesar, bukan 0)
        ///    sehingga objek dekat tidak tembus/pop-in
        ///
        /// Yang TIDAK dilakukan (beda dari versi sebelumnya yang bermasalah):
        ///  - TIDAK paksa Voxel lighting → layar hitam di game ShadowMap/Future
        ///  - TIDAK disable PostFx → visual game rusak
        ///  - TIDAK SkyGray → atmosphere game berubah
        ///  - TIDAK ubah LightAttenuation → model lighting game berubah
        /// </summary>
        private void ApplyExtremePerformancePreset()
        {
            // Bersihkan semua flag lama sebelum apply preset baru.
            Integrations.AutoOptimizeService.PurgeAllKnownFlags();

            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x1;
            SelectedTextureQuality = TextureQuality.Level0;
            MeshQualityEnabled = true;
            MeshQuality = 0;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 1;

            // ── Shadow: matikan intensitas bayangan (aman, tidak ubah lighting mode) ──────
            App.FastFlags.SetValue("FIntRenderShadowIntensity", "0");
            App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", "True");
            App.FastFlags.SetValue("FIntCSGVoxelizerFadeRadius", "0");

            // ── Light updates: Max=4 agar senter/torch tidak bug gelap ────────────────────
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "4");
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");
            App.FastFlags.SetValue("FIntRenderLocalLightFadeInMs", "0");

            // ── Post-processing ringan: hanya yang tidak merusak visual game ────────────
            App.FastFlags.SetValue("FFlagDebugSSAOForce", "False");
            App.FastFlags.SetValue("FIntSSAOMipLevels", "0");
            App.FastFlags.SetValue("FIntRobloxGuiBlurIntensity", "0");
            App.FastFlags.SetValue("FIntRenderGrainScale", "0");

            // ── Grass & wind: dibiarkan default — tidak memengaruhi rendering speed ──────

            // ── LOD / Asset rendering cepat ───────────────────────────────────────────────
            // Nilai 250 (bukan 0!) agar objek dekat tetap high-poly, tidak tembus/pop-in.
            // Nilai 0 menyebabkan semua objek jadi low-poly dari jarak 0 — itulah yang
            // bikin aset "tembus" saat didekati. 250 = switch ke low-poly mulai ~250 studs.
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance",       "250");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL12",    "250");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL23",    "500");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34",    "750");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceStatic", "0");
            App.FastFlags.SetValue("DFIntCSGv2LodsToGenerate", "0");

            // ── Texture & terrain ────────────────────────────────────────────────────────
            App.FastFlags.SetValue("FIntTerrainArraySliceSize", "0");
            App.FastFlags.SetValue("FIntTextureCompositorLowResFactor", "1");
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "1");

            // ── Render batch: kurangi draw-call overhead ─────────────────────────────────
            App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");
            App.FastFlags.SetValue("FIntRomarkStartWithGraphicQualityLevel", "1");

            // ── Rendering speed: percepat render aset ────────────────────────────────────
            // DFIntMaxFrameBufferSize=4: kurangi frame buffer queue dari default (~10) ke 4.
            // Makin kecil buffer = frame lebih cepat ditampilkan, input lag berkurang.
            // Nilai 0-3 bikin gerakan player lain laggy. 4 adalah sweet spot paling stabil.
            // Sumber: Dantezz025/Roblox-Fast-Flags (confirmed 2026).
            App.FastFlags.SetValue("DFIntMaxFrameBufferSize", "4");

            // FIntRuntimeMaxNumOfThreads=4: batasi max thread yang di-spawn Roblox.
            // Di dual-core, default Roblox spawn terlalu banyak thread → context switching
            // overhead → tiap thread dapat lebih sedikit waktu CPU → render jadi lambat.
            // Nilai 4 lebih terkontrol untuk dual/quad core.
            // Sumber: Dantezz025/Roblox-Fast-Flags (confirmed 2026).
            App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", "4");

            // DFFlagEnableRequestAsyncCompression=True: aktifkan kompresi async untuk
            // request aset ke server Roblox — aset lebih cepat di-download saat join game,
            // mengurangi delay "tembus/pop-in" saat aset belum selesai load.
            // Sumber: Firebladedoge229 gist (confirmed 2026).
            App.FastFlags.SetValue("DFFlagEnableRequestAsyncCompression", "True");

            // ── Anti Not-Responding ───────────────────────────────────────────────────────
            App.FastFlags.SetValue("DFIntMaxActiveAnimationTracks", "32");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryEphemeralCounter", "True");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryEphemeralStat",    "True");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryEventIngest",      "True");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryPoint",            "True");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Counter",        "True");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Event",          "True");
            App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Stat",           "True");

            // ── FPS Cap ───────────────────────────────────────────────────────────────────
            int fpsCap = Math.Clamp(App.Settings.Prop.ExtremeModeFpsTarget, 24, 60);
            App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", fpsCap.ToString());

            // ── BoneFish settings ────────────────────────────────────────────────────────
            App.Settings.Prop.ForceExtremeMode = true;
            OnPropertyChanged(nameof(ForceExtremeMode));
            DisableRobloxAnimations = true;
            EnableLowMemoryMode = true;
            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            // Network flags langsung — tidak panggil ApplyRecommendedNetworkSettings()
            // karena method itu punya RequestPageReloadEvent sendiri yang interrupt flow save.
            App.Settings.Prop.EnableBetterMatchmaking = true;
            App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
            App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
            App.FastFlags.SetValue("DFIntMaxReceivePPS",        "50000");
            App.FastFlags.SetValue("DFIntMaxSendPPS",           "50000");
            App.FastFlags.SetValue("DFIntConnectionMTUSize",    "1500");
            App.FastFlags.SetValue("DFIntOptimizeSendQueue",    "1");

            // Save semua flag ke disk SEBELUM page reload agar tidak ada flag yang hilang
            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }

            SelectedPreset = "ExtremePerformance";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("🥔 Potato Mode aktif — ringan, visual normal, aset tidak tembus.");
        }

        // ── Clear ClientAppSettings ───────────────────────────────────────────────────────

        /// <summary>
        /// Hapus semua flag dari ClientAppSettings.json di path BoneFish DAN path Roblox.
        /// Berguna saat terjadi bug visual (gelap, aneh) akibat akumulasi flag lama.
        /// Setelah clear, user bisa pilih ulang preset yang diinginkan.
        /// </summary>
        private void ClearClientAppSettings()
        {
            var result = System.Windows.MessageBox.Show(
                "Ini akan menghapus SEMUA FastFlag dari ClientAppSettings.json\n" +
                "di folder BoneFish dan folder Roblox.\n\n" +
                "Roblox akan berjalan dengan setting default sampai kamu\n" +
                "pilih preset lagi.\n\n" +
                "Lanjutkan?",
                "Clear ClientAppSettings",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning
            );

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // 1. Clear via FastFlagManager (path BoneFish)
            Integrations.AutoOptimizeService.PurgeAllKnownFlags();
            App.FastFlags.Prop.Clear();
            try { App.FastFlags.Save(); } catch { }

            // 2. Clear path Roblox juga
            Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();

            // 3. Reset preset indicator
            App.Settings.Prop.SelectedPerformancePreset = "None";
            App.Settings.Prop.ForceExtremeMode = false;
            OnPropertyChanged(nameof(ForceExtremeMode));
            SelectedPreset = "None";

            try { App.Settings.Save(); } catch { }

            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("✅ ClientAppSettings berhasil dibersihkan. Pilih preset untuk memulai ulang.");
        }

        // ── Night Vision ──────────────────────────────────────────────────────────────────
        /// <summary>
        /// Status Night Vision saat ini — true = aktif, false = nonaktif.
        /// Membaca dari Settings agar persisten antar session.
        /// </summary>
        public bool NightVisionEnabled
        {
            get => App.Settings.Prop.EnableNightVision;
            set
            {
                App.Settings.Prop.EnableNightVision = value;
                OnPropertyChanged(nameof(NightVisionEnabled));
            }
        }

        /// <summary>
        /// Toggle Night Vision dengan dialog konfirmasi.
        /// Menampilkan peringatan "Are you sure?" sebelum mengaktifkan,
        /// karena flag ini mengubah cara lighting Roblox bekerja secara global.
        ///
        /// Cara kerja Night Vision (client-side only, tidak mempengaruhi server/pemain lain):
        ///   FFlagFastGPULightCulling3=True  — aktifkan GPU light culling yang lebih efisien,
        ///       sebagai efek samping membuat area yang seharusnya gelap menjadi lebih terang
        ///       karena lebih banyak sumber cahaya ambient yang diproses.
        ///   FFlagNewLightAttenuation=True   — model attenuation baru yang lebih "lembut",
        ///       cahaya menyebar lebih jauh dari sumbernya, area transisi gelap-terang
        ///       jadi lebih gradual dan tidak se-hitam mode normal.
        ///   FIntRenderLocalLightUpdatesMax=8 — naikkan update dynamic light ke 8/frame
        ///       agar senter/torch game update lebih sering = radius cahayanya terasa lebih luas.
        ///
        /// Catatan: TIDAK menggunakan flag ambient override karena tidak ada di allowlist Roblox.
        /// Efek "night vision" adalah kombinasi light culling + attenuation, bukan cheat.
        /// Sumber: Dantezz025/Roblox-Fast-Flags (FFlagFastGPULightCulling3 + FFlagNewLightAttenuation,
        ///   confirmed 2026); flag ini juga tidak ada di daftar banned/exploit flagnya Roblox.
        /// </summary>
        private void ToggleNightVision()
        {
            if (!NightVisionEnabled)
            {
                // Belum aktif — tampilkan konfirmasi dulu
                var result = System.Windows.MessageBox.Show(
                    "🌙 Are you sure — aktifkan Night Vision?\n\n" +
                    "Mode ini menerangkan area gelap di game secara client-side.\n" +
                    "Pemain lain dan server TIDAK melihat perubahan apapun.\n\n" +
                    "• Area gelap / senter game akan terasa lebih terang\n" +
                    "• Tidak ada keuntungan kompetitif langsung\n" +
                    "• Bisa di-nonaktifkan kapan saja\n\n" +
                    "Lanjutkan?",
                    "Night Vision — Konfirmasi",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question
                );

                if (result != System.Windows.MessageBoxResult.Yes)
                    return;

                // Aktifkan Night Vision flags
                // FFlagFastGPULightCulling3: GPU light culling efisien — sebagai efek samping
                // lebih banyak ambient light yang sampai ke permukaan, area gelap jadi lebih terang.
                App.FastFlags.SetValue("FFlagFastGPULightCulling3", "True");

                // FFlagNewLightAttenuation=True: model attenuation "lembut" — cahaya menyebar
                // lebih jauh dari sumbernya, transisi terang-gelap lebih gradual.
                // Catatan: di AutoOptimizeService kita set ini ke False (untuk hemat CPU),
                // Night Vision OVERRIDE nilai itu ke True saat aktif.
                App.FastFlags.SetValue("FFlagNewLightAttenuation", "True");

                // FIntRenderLocalLightUpdatesMax=8: naikkan dari 4 (potato mode) ke 8 agar
                // senter/torch update lebih sering → radius cahaya terasa lebih luas & responsif.
                App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "8");
                App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");

                NightVisionEnabled = true;
                try { App.FastFlags.Save(); } catch { }
                try { App.Settings.Save(); } catch { }
                Notify("🌙 Night Vision aktif — area gelap akan lebih terang.");
            }
            else
            {
                // Nonaktifkan — hapus semua flag Night Vision (kembalikan ke null = default game)
                // JANGAN set FFlagNewLightAttenuation ke "False" — itu merusak lighting game.
                // Cukup hapus flag ini sehingga Roblox pakai model lighting default-nya sendiri.
                App.FastFlags.SetValue("FFlagFastGPULightCulling3", null);
                App.FastFlags.SetValue("FFlagNewLightAttenuation", null);
                // Kembalikan light updates ke nilai Potato Mode
                App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "4");
                App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");

                NightVisionEnabled = false;
                try { App.FastFlags.Save(); } catch { }
                try { App.Settings.Save(); } catch { }
                Notify("🌙 Night Vision dinonaktifkan.");
            }

            OnPropertyChanged(nameof(NightVisionEnabled));
        }
    }
}
