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
            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x1;
            SelectedTextureQuality = TextureQuality.Level0;
            MeshQualityEnabled = true;
            MeshQuality = 0;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 1;  // Minimum rendering quality
            
            // Ultra low lighting
            App.FastFlags.SetPreset("Rendering.LightingMode", "Default");
            App.FastFlags.SetPreset("Rendering.EnvironmentSpecMap", "False");
            App.FastFlags.SetPreset("Rendering.DisableLighting", "False");
            App.FastFlags.SetPreset("Terrain.GridV2", "False");

            // Anti-crash memory optimizations
            App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", "30");
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "1");
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "1");
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "1");

            DisableRobloxAnimations = true;
            EnableLowMemoryMode = true;

            ApplyRecommendedNetworkSettings();
            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            SelectedPreset = "UltraLow";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Ultra low-spec preset untuk PC spek kentang telah diterapkan.");
        }

        private void ApplyBalancedPreset()        {
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
        /// Preset "ExtremePerformance" (Potato Mode) — lebih agresif dari UltraLow.
        /// Ditujukan untuk laptop dual-core, RAM kurang dari 4GB, tanpa GPU dedicated.
        ///
        /// Perbedaan utama vs UltraLow:
        ///  - Matikan semua post-FX (bloom, SSAO, blur, color correction, sun rays)
        ///  - Matikan shadow intensitas + SSAO (bukan hanya voxelizer)
        ///  - Paksa Voxel lighting (water reflection otomatis hilang)
        ///  - Matikan foliage wind + dynamic faces (FACS)
        ///  - Matikan terrain detail (FIntTerrainArraySliceSize=0)
        ///  - Target FPS dapat dikonfigurasi user (default 30, min 24)
        ///  - ForceExtremeMode di-set true agar AutoOptimizeService juga apply tier ini
        /// </summary>
        private void ApplyExtremePerformancePreset()
        {
            UseFastFlagManager = true;
            FixDisplayScaling = true;

            // D3D11 adalah pilihan paling stabil untuk device lama.
            // Hindari Vulkan (bisa crash) dan D3D10 (kompatibilitas game modern terbatas).
            SelectedRenderingMode = RenderingMode.D3D11;

            // MSAA x1 = matikan anti-aliasing, hemat GPU fill-rate signifikan.
            SelectedMSAALevel = MSAAMode.x1;

            // Tekstur level 0 = terendah, kurangi tekanan VRAM/RAM.
            SelectedTextureQuality = TextureQuality.Level0;

            // Mesh LOD minimum (0) = semua objek langsung low-poly dari jarak berapa pun.
            MeshQualityEnabled = true;
            MeshQuality = 0;

            // FRM Quality level 1 = resolusi render internal paling rendah.
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 1;

            // ── Shadow & Lighting ─────────────────────────────────────────────────────────
            // Matikan intensitas shadow sama sekali — tidak ada bayangan yang digambar.
            App.FastFlags.SetValue("FIntRenderShadowIntensity", "0");
            // Pause voxelizer + reset fade radius voxelizer.
            App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", "True");
            App.FastFlags.SetValue("FIntCSGVoxelizerFadeRadius", "0");
            // Paksa Voxel lighting (paling ringan) — otomatis hilangkan water reflection/PBR.
            App.FastFlags.SetValue("DFFlagDebugRenderForceTechnologyVoxel", "True");
            // Nonaktifkan light attenuation baru, fallback ke model lama yang lebih murah.
            App.FastFlags.SetValue("FFlagNewLightAttenuation", "False");
            // Batasi update dynamic light per frame ke 1.
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "1");
            App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "1");

            // ── Post-Processing ───────────────────────────────────────────────────────────
            // Matikan semua post-FX: bloom, color correction, sun rays, depth of field.
            App.FastFlags.SetValue("FFlagDisablePostFx", "True");
            // Matikan SSAO (ambient occlusion) — efek bayangan kontak yang mahal di CPU/GPU.
            App.FastFlags.SetValue("FFlagDebugSSAOForce", "False");
            App.FastFlags.SetValue("FIntSSAOMipLevels", "0");
            // Matikan blur menu ESC — post-process tambahan yang tidak diperlukan.
            App.FastFlags.SetValue("FIntRobloxGuiBlurIntensity", "0");
            // Matikan grain/film noise pass.
            App.FastFlags.SetValue("FIntRenderGrainScale", "0");

            // ── Sky & Atmosphere ──────────────────────────────────────────────────────────
            // Ganti skybox dengan warna abu-abu datar — hapus render atmosfer gradient.
            App.FastFlags.SetValue("FFlagDebugSkyGray", "True");

            // ── Grass, Foliage & Wind ─────────────────────────────────────────────────────
            App.FastFlags.SetValue("FIntFRMMinGrassDistance", "0");
            App.FastFlags.SetValue("FIntFRMMaxGrassDistance", "0");
            App.FastFlags.SetValue("FIntRenderGrassDetailStrands", "0");
            App.FastFlags.SetValue("FIntRenderGrassHeightScaler", "0");
            // Matikan simulasi angin global — foliage dan kain tidak bergerak.
            App.FastFlags.SetValue("FFlagGlobalWindActivated", "False");

            // ── Terrain Detail ────────────────────────────────────────────────────────────
            // Kurangi slice array terrain texture — terrain jadi lebih flat tapi hemat VRAM.
            App.FastFlags.SetValue("FIntTerrainArraySliceSize", "0");

            // ── Texture ───────────────────────────────────────────────────────────────────
            // Paksa compositor tekstur ke resolusi paling rendah.
            App.FastFlags.SetValue("FIntTextureCompositorLowResFactor", "1");
            // Batasi concurrent texture compositor jobs ke 1 thread.
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "1");

            // ── Mesh LOD Extended ─────────────────────────────────────────────────────────
            // Matikan generasi LOD CSGv2 — kurangi CPU load saat load game awal.
            App.FastFlags.SetValue("DFIntCSGv2LodsToGenerate", "0");
            // GC agresif terhadap aset jauh — turunkan streaming distance dan tekanan RAM.
            App.FastFlags.SetValue("DFIntDebugRestrictGCDistance", "1");
            // Perbesar ukuran batch render untuk kurangi draw-call overhead per frame.
            App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");
            // Paksa kualitas grafis mulai dari level 1 di menu in-game.
            App.FastFlags.SetValue("FIntRomarkStartWithGraphicQualityLevel", "1");

            // ── Dynamic Faces (FACS) ──────────────────────────────────────────────────────
            // Matikan LOD facial animation — hilangkan ekspresi wajah avatar untuk hemat CPU.
            App.FastFlags.SetValue("DFIntAnimationLodFacsDistanceMin", "0");
            App.FastFlags.SetValue("DFIntAnimationLodFacsDistanceMax", "0");
            App.FastFlags.SetValue("DFIntAnimationLodFacsVisibilityDenominator", "0");

            // ── Task Scheduler FPS Cap ────────────────────────────────────────────────────
            // Ambil target FPS dari setting user (default 30, minimum 24).
            // User bisa atur via slider "Target FPS (Extreme Mode)" di UI.
            int fpsCap = Math.Clamp(App.Settings.Prop.ExtremeModeFpsTarget, 24, 60);
            App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", fpsCap.ToString());

            // ── BoneFish Settings ─────────────────────────────────────────────────────────
            // Aktifkan ForceExtremeMode agar AutoOptimizeService juga apply tier Extreme
            // saat Roblox launch berikutnya (CheckAndApply() terpanggil dari Bootstrapper).
            App.Settings.Prop.ForceExtremeMode = true;
            OnPropertyChanged(nameof(ForceExtremeMode));

            // Nonaktifkan animasi UI dan low memory mode untuk efisiensi tambahan.
            DisableRobloxAnimations = true;
            EnableLowMemoryMode = true;

            // Matikan background updates — tidak perlu cek update saat game berjalan.
            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            // Terapkan juga network preset untuk latency minimum.
            ApplyRecommendedNetworkSettings();

            SelectedPreset = "ExtremePerformance";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("🥔 Potato Mode aktif — preset ekstrem untuk PC paling lemah diterapkan.");
        }
    }
}
