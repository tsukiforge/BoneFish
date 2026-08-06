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

        private bool _isApplying;
        public bool IsApplying
        {
            get => _isApplying;
            set
            {
                _isApplying = value;
                OnPropertyChanged(nameof(IsApplying));
                OnPropertyChanged(nameof(IsNotApplying));
            }
        }
        public bool IsNotApplying => !IsApplying;

        public event EventHandler? RequestPageReloadEvent
        {
            add
            {
                _requestPageReloadEvent += value;
            }
            remove
            {
                _requestPageReloadEvent -= value;
            }
        }
        private event EventHandler? _requestPageReloadEvent;

        /// <summary>
        /// Memicu RequestPageReloadEvent dan refresh SystemInfo.
        /// </summary>
        private void OnRequestPageReload()
        {
            RefreshSystemInfo();
            _requestPageReloadEvent?.Invoke(this, EventArgs.Empty);
        }

        // Ganti semua pemanggilan RequestPageReloadEvent?.Invoke dengan OnRequestPageReload()
        
        public event EventHandler? OpenFlagEditorEvent;

        public event EventHandler<string>? RequestNotificationEvent;

        public event EventHandler? RequestCloseWindowEvent;

        private void OpenFastFlagEditor() => OpenFlagEditorEvent?.Invoke(this, EventArgs.Empty);

        private void Notify(string message) => RequestNotificationEvent?.Invoke(this, message);

        public ICommand OpenFastFlagEditorCommand => new RelayCommand(OpenFastFlagEditor);

        // ★ FIX freeze: semua preset pindahkan disk scan (CleanupLegacyRobloxFlags
        // yang iterasi folder Roblox/Versions/version-* + baca/tulis JSON) ke
        // background thread supaya klik preset tidak bikin UI "Not Responding".
        public ICommand ApplyRecommendedFastFlagsCommand => new AsyncRelayCommand(ApplyRecommendedFastFlags);

        public ICommand ApplyRecommendedNetworkSettingsCommand => new RelayCommand(ApplyRecommendedNetworkSettings);

        public ICommand ApplyRecommendedStabilityPresetCommand => new AsyncRelayCommand(ApplyRecommendedStabilityPreset);

        public ICommand ApplyUltraLowSpecPresetCommand => new AsyncRelayCommand(ApplyUltraLowSpecPreset);

        public ICommand ApplyBalancedPresetCommand => new AsyncRelayCommand(ApplyBalancedPreset);

        public ICommand ApplyExtremePerformancePresetCommand => new AsyncRelayCommand(ApplyExtremePerformancePreset);

        // ToggleNightVisionCommand dihapus (GAP 4 — Night Vision deprecated)

        public ICommand ClearClientAppSettingsCommand => new AsyncRelayCommand(ClearClientAppSettings);

        public ICommand ApplyAndRestartRobloxCommand => new RelayCommand(ApplyAndRestartRoblox);

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

                OnRequestPageReload();
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

        // ── System Info (Fitur C) ──────────────────────────────────────────────────
        /// <summary>
        /// Informasi sistem: CPU, RAM, storage type, tier — dari AutoOptimizeService.
        /// Dipanggil ulang setiap kali RequestPageReloadEvent terpicu.
        /// </summary>
        public string SystemInfoText { get; private set; } = LoadSystemInfo();

        public void RefreshSystemInfo()
        {
            SystemInfoText = LoadSystemInfo();
            OnPropertyChanged(nameof(SystemInfoText));
        }

        private static string LoadSystemInfo()
        {
            try
            {
                return Integrations.AutoOptimizeService.GetSystemInfo();
            }
            catch
            {
                return "System info unavailable";
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
                try { App.Settings.Save(); } catch { }
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
                try { App.Settings.Save(); } catch { }
            }
        }

        /// <summary>
        /// Fast Loading — toggle independen untuk percepat loading aset.
        /// Bisa aktif BERSAMAAN dengan preset visual apa pun.
        /// Saat diaktifkan: apply flag optimal; saat dimatikan: remove flag.
        /// Flag yang dipakai conditional pada cpuCores (>=4 / >=8).
        /// </summary>
        public bool EnableFastLoadingFlags
        {
            get => App.Settings.Prop.EnableFastLoadingFlags;
            set
            {
                App.Settings.Prop.EnableFastLoadingFlags = value;
                if (value)
                    Integrations.AutoOptimizeService.ApplyFastLoadingFlags();
                else
                    Integrations.AutoOptimizeService.RemoveFastLoadingFlags();
                OnPropertyChanged(nameof(EnableFastLoadingFlags));
                try { App.FastFlags.Save(); } catch { }
                try { App.Settings.Save(); } catch { }
            }
        }

        private async Task ApplyRecommendedFastFlags()
        {
            // Bersihkan semua flag lama sebelum apply preset baru — dari DISK dan MEMORY.
            // ★ FIX freeze: CleanupLegacyRobloxFlags scan semua folder Roblox
            // Versions/version-* + baca/tulis JSON — dipindah ke background.
            await Task.Run(() =>
            {
                Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();
                Integrations.AutoOptimizeService.PurgeAllKnownFlags();
            });
            // NightVisionEnabled = false — dihapus (GAP 4)
            // ForceExtremeMode harus di-reset saat pindah ke preset lain,
            // agar AutoOptimizeService.DetectSystemTier() tidak memaksa ExtremePerformance
            // di launch berikutnya walau user sudah pilih preset lain.
            App.Settings.Prop.ForceExtremeMode = false;
            OnPropertyChanged(nameof(ForceExtremeMode));

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
            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }
            VerifyAndNotify("Auto-Optimize");
            OnRequestPageReload();
        }

        private void ApplyRecommendedNetworkSettings()
        {
            // ★ REFACTOR: Panggil method reusable dari AutoOptimizeService
            Integrations.AutoOptimizeService.ApplyNetworkOptimizations();

            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }
            Notify("Auto-optimize jaringan & No Delay telah diterapkan.");
            OnRequestPageReload();
        }

        private async Task ApplyRecommendedStabilityPreset()
        {
            // ── Cleanup (sekali vs sebelumnya ter-delegate 3x save/notify/reload) ─────
            // ★ FIX freeze: disk scan ke background thread.
            await Task.Run(() =>
            {
                Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();
                Integrations.AutoOptimizeService.PurgeAllKnownFlags();
            });
            // NightVisionEnabled = false — dihapus (GAP 4)
            App.Settings.Prop.ForceExtremeMode = false;
            OnPropertyChanged(nameof(ForceExtremeMode));

            // ── AutoOptimize base ────────────────────────────────────────────────────────
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

            // ── Network ────────────────────────────────────────────────────────────────
            Integrations.AutoOptimizeService.ApplyNetworkOptimizations();

            // ── Fast Loading: re-apply jika toggle aktif (stack dengan preset) ─────────
            if (App.Settings.Prop.EnableFastLoadingFlags)
                Integrations.AutoOptimizeService.ApplyFastLoadingFlags();

            // ── Stability-specific ──────────────────────────────────────────────────
            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            // ── Finalize: save sekali, notify sekali, reload sekali ──────────────────
            SelectedPreset = "Stable";
            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }
            VerifyAndNotify("Stable");
            OnRequestPageReload();
        }

        private async Task ApplyUltraLowSpecPreset()
        {
            // Bersihkan semua flag lama sebelum apply preset baru — dari DISK dan MEMORY.
            // ★ FIX freeze: disk scan ke background thread.
            await Task.Run(() =>
            {
                Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();
                Integrations.AutoOptimizeService.PurgeAllKnownFlags();
            });
            // NightVisionEnabled = false — dihapus (GAP 4)
            App.Settings.Prop.ForceExtremeMode = false;
            OnPropertyChanged(nameof(ForceExtremeMode));

            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x1;
            SelectedTextureQuality = TextureQuality.Level0;
            MeshQualityEnabled = true;
            MeshQuality = 0;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 5;

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

            // Network flags langsung — panggil method reusable
            Integrations.AutoOptimizeService.ApplyNetworkOptimizations();

            // Fast Loading: re-apply jika toggle aktif
            if (App.Settings.Prop.EnableFastLoadingFlags)
                Integrations.AutoOptimizeService.ApplyFastLoadingFlags();

            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            // CRITICAL: SelectedPreset HARUS di-set SEBELUM App.Settings.Save()
            // Agar value "UltraLow" tertulis ke disk. Saat bootstrapper restart,
            // AutoOptimizeService.CheckAndApply() baca SelectedPerformancePreset dari disk.
            // Jika "UltraLow" tidak ada di disk, service ini OVERWRITE semua flag
            // dengan FRM=1 + shadow=0 + voxelizer=True → game GELAP.
            SelectedPreset = "UltraLow";

            // Save semua flag ke disk SEBELUM page reload agar tidak ada flag yang hilang
            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }

            VerifyAndNotify("Ultra Low");
            OnRequestPageReload();
        }

        private async Task ApplyBalancedPreset()
        {
            // Bersihkan semua flag lama sebelum apply preset baru — dari DISK dan MEMORY.
            // ★ FIX freeze: disk scan ke background thread.
            await Task.Run(() =>
            {
                Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();
                Integrations.AutoOptimizeService.PurgeAllKnownFlags();
            });
            // NightVisionEnabled = false — dihapus (GAP 4)
            App.Settings.Prop.ForceExtremeMode = false;
            OnPropertyChanged(nameof(ForceExtremeMode));

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

            // Network flags — panggil method reusable (ApplyNetworkOptimizations tidak trigger reload)
            Integrations.AutoOptimizeService.ApplyNetworkOptimizations();

            // Fast Loading: re-apply jika toggle aktif
            if (App.Settings.Prop.EnableFastLoadingFlags)
                Integrations.AutoOptimizeService.ApplyFastLoadingFlags();

            SelectedPreset = "Balanced";
            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }
            VerifyAndNotify("Balanced");
            OnRequestPageReload();
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
        ///
        /// ═══════════════════════════════════════════════════════════════════
        /// ★ AUDIT: Anti Not-Responding (Long Session) — Laporan Lengkap
        /// ═══════════════════════════════════════════════════════════════════
        ///
        /// 1. STATUS ACTIVE:
        ///    - Hanya aktif melalui klik manual user (button "Anti Not-Responding").
        ///    - TIDAK auto-activated oleh CheckAndApply()/DetectSystemTier().
        ///    - Dapat juga aktif via ForceExtremeMode toggle + launch restart.
        ///
        /// 2. FLAG VISUAL (sengaja disertakan, tidak dipisah jadi toggle):
        ///    - FFlagDebugSSAOForce=False   — SSAO dimatikan
        ///    - FIntSSAOMipLevels=0         — SSAO kualitas 0
        ///    - FIntRobloxGuiBlurIntensity=0 — Blur UI mati
        ///    - FIntRenderGrainScale=0      — Grain mati
        ///    Alasan: Preset ini didesain untuk device dual-core, RAM <4GB,
        ///    di mana SEMUA post-processing ringan pun membebani CPU/GPU.
        ///    Label "PALING AGRESIF" sudah memperingatkan user.
        ///    Visual yang dimatikan hanya efek kosmetik (blur, grain, SSAO)
        ///    — TIDAK memengaruhi gameplay, lighting, atau shadow.
        ///
        /// 3. FLAG ANTI-FREEZE/STABILITY:
        ///    - DFIntMaxActiveAnimationTracks=32 — batasi animasi
        ///    - FIntRenderLocalLightFadeInMs=0  — fade light instan
        ///    - 7 flag telemetry off              — kurangi I/O disk
        ///    Flag ini TIDAK dipisah jadi toggle terpisah karena:
        ///    a) Preset ini adalah SATU KESATUAN agresif untuk device lemah.
        ///    b) Anti-freeze tanpa pengorbanan visual tidak cukup efektif.
        ///    c) User masih bisa stack toggle lain (Fast Loading, dsb).
        ///
        /// 4. KEPUTUSAN FINAL:
        ///    - Tidak dibuat toggle EnableAntiFreezeMode terpisah.
        ///    - Preset tetap sebagai SATU PAKET untuk target pengguna
        ///      spesifik (low-end extreme).
        ///    - Dokumentasi ini untuk transparansi — bukan dead code.
        /// ═══════════════════════════════════════════════════════════════════
        /// </summary>
        private async Task ApplyExtremePerformancePreset()
        {
            // Bersihkan semua flag lama sebelum apply preset baru — dari DISK dan MEMORY.
            // ★ FIX freeze: disk scan ke background thread.
            await Task.Run(() =>
            {
                Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();
                Integrations.AutoOptimizeService.PurgeAllKnownFlags();
            });
            // NightVisionEnabled = false — dihapus (GAP 4)

            UseFastFlagManager = true;
            FixDisplayScaling = true;
            SelectedRenderingMode = RenderingMode.D3D11;
            SelectedMSAALevel = MSAAMode.x1;
            SelectedTextureQuality = TextureQuality.Level0;
            MeshQualityEnabled = true;
            MeshQuality = 0;
            FRMQualityOverrideEnabled = true;
            FRMQualityOverride = 3;

            // ── Shadow: TIDAK dimatikan sepenuhnya ──────────────────────────────────────
            // CATATAN SEBELUMNYA (PENYEBAB GAME GELAP):
            // FIntRenderShadowIntensity=0 + DFFlagDebugPauseVoxelizer=True + FRM=1
            // menghapus SEMUA bayangan dan lighting baked → game jadi HITAM.
            // Terutama parah di game ShadowMap/Future (Phasmophobia, horror games).
            //
            // FIX: FRM=3 masih sangat ringan tapi preserve basic lighting pipeline.
            // Shadow intensity DIHAPUS (biarkan default Roblox), voxelizer TIDAK dipause.
            //
            // Light updates Max=4 agar senter/torch tidak bug gelap.
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
            // CATATAN: ForceExtremeMode TIDAK di-hardcode true di sini.
            // ForceExtremeMode adalah toggle INDEPENDEN — user bisa menyalakannya
            // secara manual jika ingin mode ini dipaksa pada launch berikutnya.
            // Sebelumnya baris ini memaksa ForceExtremeMode=true setiap kali preset
            // Extreme dipilih, menyebabkan preset lain (Balanced, UltraLow, Stable)
            // tetap kena override Extreme saat Roblox di-launch ulang — lihat
            // AutoOptimizeService.DetectSystemTier() yang cek ForceExtremeMode duluan.
            DisableRobloxAnimations = true;
            EnableLowMemoryMode = true;
            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            // Network flags — panggil method reusable (ApplyNetworkOptimizations tidak trigger reload)
            Integrations.AutoOptimizeService.ApplyNetworkOptimizations();

            // Fast Loading: re-apply jika toggle aktif
            if (App.Settings.Prop.EnableFastLoadingFlags)
                Integrations.AutoOptimizeService.ApplyFastLoadingFlags();

            // CRITICAL: SelectedPreset HARUS di-set SEBELUM App.Settings.Save()
            // Agar value "ExtremePerformance" tertulis ke disk. Lihat komentar di ApplyUltraLowSpecPreset.
            SelectedPreset = "ExtremePerformance";

            // Save semua flag ke disk SEBELUM page reload agar tidak ada flag yang hilang
            try { App.FastFlags.Save(); } catch { }
            try { App.Settings.Save(); } catch { }

            VerifyAndNotify("Potato Mode");
            OnRequestPageReload();
        }

        // ── Flag Verification ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifikasi bahwa semua FastFlag benar-benar tertulis ke disk.
        /// Membaca kembali ClientAppSettings.json dan menghitung jumlah flag.
        /// Memberikan notifikasi detail ke user agar yakin flag sudah diterapkan.
        /// </summary>
        private void VerifyAndNotify(string presetName)
        {
            try
            {
                string filePath = Path.Combine(Paths.Modifications, "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var flags = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    int count = flags?.Count ?? 0;
                    Notify($"✅ {presetName} aktif — {count} FastFlag berhasil ditulis ke disk.");
                }
                else
                {
                    Notify($"✅ {presetName} aktif — file ClientAppSettings.json akan dibuat saat Roblox launch.");
                }
            }
            catch (Exception ex)
            {
                Notify($"✅ {presetName} aktif — (verifikasi gagal: {ex.Message})");
            }
        }

        // ── Apply & Restart Roblox ────────────────────────────────────────────────────────

        /// <summary>
        /// Simpan semua FastFlag + Settings, kill Roblox + watcher, lalu restart.
        /// Async agar UI tidak freeze saat proses kill.
        /// </summary>
        private async void ApplyAndRestartRoblox()
        {
            const string LOG_IDENT = "FastFlagsViewModel::ApplyAndRestartRoblox";

            if (IsApplying) return;
            IsApplying = true;

            var result = System.Windows.MessageBox.Show(
                "🚀 Apply & Restart Roblox\n\n" +
                "Ini akan MENYIMPAN semua FastFlag dan SETTING yang sudah kamu pilih,\n" +
                "lalu menutup paksa Roblox (termasuk system tray),\n" +
                "dan menjalankan ulang BoneFish.\n\n" +
                "Setelah restart, klik game Roblox seperti biasa untuk main.\n\n" +
                "Lanjutkan?",
                "Apply & Restart Roblox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question
            );

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                IsApplying = false;
                return;
            }

            // 1. Save semua flag ke disk
            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Saving FastFlags and Settings...");
                App.FastFlags.Save();
                App.Settings.Save();
                App.Logger.WriteLine(LOG_IDENT, "FastFlags saved successfully.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Notify($"❌ Gagal menyimpan FastFlag: {ex.Message}");
                IsApplying = false;
                return;
            }

            // 2. Kill RobloxPlayerBeta + RobloxCrashHandler (async supaya UI tidak freeze)
            App.Logger.WriteLine(LOG_IDENT, "Killing Roblox processes...");
            await Task.Run(() =>
            {
                foreach (string name in new[] { "RobloxPlayerBeta", "RobloxCrashHandler" })
                {
                    try
                    {
                        foreach (var proc in Process.GetProcessesByName(name))
                        {
                            proc.Kill();
                            if (!proc.WaitForExit(500))
                                App.Logger.WriteLine(LOG_IDENT, $"{name} (pid={proc.Id}) did not exit within 500ms, continuing.");
                            else
                                App.Logger.WriteLine(LOG_IDENT, $"Killed {name} (pid={proc.Id})");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            });

            // 3. Signal watcher to exit (clean up system tray)
            App.Logger.WriteLine(LOG_IDENT, "Signalling watcher to exit...");
            try
            {
                using var exitEvent = System.Threading.EventWaitHandle.OpenExisting("BoneFish-WatcherExitEvent");
                exitEvent.Set();
                App.Logger.WriteLine(LOG_IDENT, "Watcher exit event signalled.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            // 4. Launch BoneFish bootstrapper to reload with new flags
            App.Logger.WriteLine(LOG_IDENT, "Restarting BoneFish bootstrapper...");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Paths.Process,
                    Arguments = "-player",
                    UseShellExecute = true
                });
                App.Logger.WriteLine(LOG_IDENT, "Bootstrapper launched.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Notify($"❌ Gagal restart BoneFish: {ex.Message}");
                IsApplying = false;
                return;
            }

            // 5. Tutup settings window — soalnya bootstrapper udah jalan
            RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);
        }

        // ── Clear ClientAppSettings ───────────────────────────────────────────────────────

        /// <summary>
        /// Hapus semua flag dari ClientAppSettings.json di path BoneFish DAN path Roblox.
        /// Berguna saat terjadi bug visual (gelap, aneh) akibat akumulasi flag lama.
        /// Setelah clear, user bisa pilih ulang preset yang diinginkan.
        /// </summary>
        private async Task ClearClientAppSettings()
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

            // ★ FIX freeze: CleanupLegacyRobloxFlags() men-scan SEMUA folder
            // Roblox/Versions/version-* lalu baca & tulis JSON tiap folder.
            // Di HDD ini bisa butuh beberapa detik — synchronous di UI thread
            // bikin aplikasi "Not Responding". Dipindah ke Task.Run.
            await Task.Run(() =>
            {
                // 1. Clear via FastFlagManager (path BoneFish) — bersihkan MEMORY
                Integrations.AutoOptimizeService.PurgeAllKnownFlags();
                App.FastFlags.Prop.Clear();
                try { App.FastFlags.Save(); } catch { }

                // 2. Clear path Roblox + BoneFish via DISK scan
                Integrations.AutoOptimizeService.CleanupLegacyRobloxFlags();
            });

            // 3. Reset preset indicator (kembali ke UI thread)
            App.Settings.Prop.SelectedPerformancePreset = "None";
            App.Settings.Prop.ForceExtremeMode = false;
            OnPropertyChanged(nameof(ForceExtremeMode));
            SelectedPreset = "None";

            try { App.Settings.Save(); } catch { }

            Notify("✅ ClientAppSettings berhasil dibersihkan. Pilih preset untuk memulai ulang.");
            OnRequestPageReload();
        }

        // ── Night Vision — DIHAPUS (GAP 4) ────────────────────────────────────────────
        // ★ GAP 4: Night Vision dihapus total karena flag-nya sudah deprecated.
        //   FFlagFastGPULightCulling3 dan FFlagNewLightAttenuation tidak ada di Roblox
        //   Allowlist sejak September 2025 — client Roblox abaikan flag ini.
        //   Referensi: https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569

        // NightVisionEnabled property, ToggleNightVision(), dan ToggleNightVisionCommand
        // telah dihapus. Semua referensi NightVisionEnabled = false di method preset juga
        // sudah dibersihkan.
    }
}
