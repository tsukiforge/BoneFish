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
            }
        }

        public bool IsUltraLowActive => SelectedPreset == "UltraLow";
        public bool IsBalancedActive => SelectedPreset == "Balanced";
        public bool IsStableActive => SelectedPreset == "Stable";
        public bool IsAutoOptimizeActive => SelectedPreset == "AutoOptimize";

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

            ApplyRecommendedNetworkSettings();
            App.Settings.Prop.BackgroundUpdatesEnabled = false;
            App.Settings.Prop.FakeBorderlessFullscreen = false;

            SelectedPreset = "UltraLow";
            RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            Notify("Ultra low-spec preset untuk PC spek kentang telah diterapkan.");
        }

        private void ApplyBalancedPreset()
        {
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
    }
}
