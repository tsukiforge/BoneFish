using System.Collections.ObjectModel;

namespace Bloxstrap.Models.Persistable
{
    public class Settings
    {
        // uh
        public bool AllowCookieAccess { get; set; } = false;

        // bloxstrap configuration
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FluentAeroDialog;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconBloxstrap;
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public RobloxIcon RobloxIcon { get; set; } = RobloxIcon.IconDefault;
        public string RobloxTitle { get; set; } = "Roblox";
        public string RobloxIconCustomLocation { get; set; } = "";
        public Theme Theme { get; set; } = Theme.Default;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool DeveloperMode { get; set; } = false;
        public bool ForceLocalData { get; set; } = false;
        public bool CheckForUpdates { get; set; } = true;
        public bool MultiInstanceLaunching { get; set; } = false;
        public bool ConfirmLaunches { get; set; } = true;
        public string Locale { get; set; } = "nil";
        public bool ForceRobloxLanguage { get; set; } = false;
        public bool UseFastFlagManager { get; set; } = true;
        public bool WPFSoftwareRender { get; set; } = false;
        public bool EnableAnalytics { get; set; } = false;
        public bool StaticDirectory { get; set; } = false;
        public string Channel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public string RobloxDomain { get; set; } = RobloxInterfaces.Deployment.DefaultRobloxDomain;
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Automatic;
        public string DownloadingStringFormat { get; set; } = Strings.Bootstrapper_Status_Downloading + " {0} - {1}MB / {2}MB";
        public string? SelectedCustomTheme { get; set; } = null;
        public bool BackgroundUpdatesEnabled { get; set; } = false;
        public bool DebugDisableVersionPackageCleanup { get; set; } = false;
        public bool EnableBetterMatchmaking { get; set; } = false;
        public bool EnableBetterMatchmakingRandomization { get; set; } = false;
        public WebEnvironment WebEnvironment { get; set; } = WebEnvironment.Production;

        // integration configuration
        public CleanerOptions CleanerOptions { get; set; } = CleanerOptions.Never;
        public List<string> CleanerDirectories { get; set; } = new List<string>();
        public bool FakeBorderlessFullscreen { get; set; } = false;
        public bool EnableActivityTracking { get; set; } = true;
        public bool UseDiscordRichPresence { get; set; } = true;
        public bool HideRPCButtons { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = false;
        public bool ShowServerDetails { get; set; } = false;
        public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = new();

        // mod preset configuration
        public bool UseDisableAppPatch { get; set; } = false;

        // experimental features
        public bool EnableSystemTrayOnClose { get; set; } = false;
        public bool EnableRobloxNotifications { get; set; } = false;
        public bool EnableFriendOnlineNotifications { get; set; } = false;
        public bool EnableNotificationSound { get; set; } = true;
        public bool EnableFpsMonitor { get; set; } = false;
        public double FpsMonitorX { get; set; } = 0;
        public double FpsMonitorY { get; set; } = 0;
        public bool OptimizeForLowEnd { get; set; } = false; // when true, reduce timers and visual updates to save CPU on older devices

        // Turbo Mode — temporary performance boost, resets on restart
        public bool EnableTurboMode { get; set; } = false;

        // performance preset
        public string SelectedPerformancePreset { get; set; } = "None";

        // ExtremePerformance (Potato Mode) — override manual agar user bisa paksa mode ini
        // walau auto-detect tidak mendeteksi perangkat sebagai UltraLow
        public bool ForceExtremeMode { get; set; } = false;

        // Target FPS untuk TaskScheduler pada Extreme/UltraLow mode.
        // Default 30fps; bisa diturunkan ke 24 untuk perangkat paling lemah.
        public int ExtremeModeFpsTarget { get; set; } = 30;

        // Night Vision mode untuk Potato Mode — terangkan area gelap client-side.
        // Client-side only, tidak mempengaruhi gameplay atau pemain lain.
        public bool EnableNightVision { get; set; } = false;

        // Global hotkey settings
        public bool EnableHotkeys { get; set; } = true;

        // Crosshair overlay settings
        public bool EnableCrosshair { get; set; } = false;
        public string CrosshairStyle { get; set; } = "Cross";      // Dot, Cross, Circle, CrossDot
        public string CrosshairColor { get; set; } = "#00FF00";    // Lime green default
        public double CrosshairSize { get; set; } = 40;             // 20-200px
        public double CrosshairOpacity { get; set; } = 0.8;         // 0.1-1.0
        public double CrosshairX { get; set; } = 0;                 // screen position
        public double CrosshairY { get; set; } = 0;

        // wallpaper background
        public bool EnableWallpaperLauncher { get; set; } = false;

        // custom background
        public string CustomBackgroundPath { get; set; } = "";
    }
}