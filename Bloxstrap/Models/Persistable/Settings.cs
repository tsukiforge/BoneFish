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

        // Game Session Manager rules. New applications are persisted disabled.
        public ObservableCollection<GameSessionRule> GameSessionRules { get; set; } = new();
        public bool GameSessionAutoSelectSafeApps { get; set; } = false;

        // Master toggle Game Session Manager — default OFF (opt-in).
        // Saat false, BeginSessionAsync() tidak pernah dipanggil di bootstrapper,
        // jadi nol overhead WMI/process-scan/file-write untuk user yang tidak memakai
        // fitur ini. Rules yang sudah dicentang TETAP tersimpan, hanya tidak dieksekusi.
        public bool GameSessionEnabled { get; set; } = false;

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

        // ★ GAP 4: Night Vision DIHAPUS (FFlagFastGPULightCulling3 + FFlagNewLightAttenuation
        // sudah deprecated sejak September 2025 karena Roblox Allowlist system — keduanya
        // tidak ada di allowlist, jadi client Roblox abaikan flag ini. Tidak berefek apa-apa.)

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

        // wallpaper background (EnableWallpaperLauncher dihapus — FIX 3: background selalu aktif)

        // custom background
        public string CustomBackgroundPath { get; set; } = "";

        // persistence untuk selected background type (Default/Cool/Quality/Extra/Custom)
        public string SelectedBackgroundType { get; set; } = "";

        // kalo true, background ganti random tiap buka app
        public bool BackgroundRandomMode { get; set; } = true;

        // Auto cache cleanup settings
        public bool EnableAutoCacheCleanup { get; set; } = true;
        public int CacheCleanupMaxAgeDays { get; set; } = 14;

        // Battery saver untuk auto-wallpaper
        public bool EnableBatterySaverForWallpaper { get; set; } = true;

        // Fast Loading — toggle independen untuk percepat loading aset
        public bool EnableFastLoadingFlags { get; set; } = false;

        // TDR Mitigation — toggle independen untuk KURANGI freeze/layar putih
        // (Intel iGPU Driver TDR, Event ID 4101) dengan menurunkan beban GPU.
        // Bukan menghilangkan total — akar masalah di driver, bukan software.
        public bool EnableTdrMitigation { get; set; } = false;

        // Backup nilai Flag sebelum ditimpa toggle TDR — dipakai saat toggle
        // dimatikan agar nilai user/preset sebelum toggle bisa dikembalikan.
        public Dictionary<string, string> TdrMitigationBackup { get; set; } = new();

        // Manual FastFlag toggles — disimpan sebagai preferensi TERPISAH agar state-nya
        // survive PurgeAllKnownFlags()/RemoveOptimizations() di setiap Play.
        // Sebelumnya state dibaca langsung dari FastFlags yang di-purge tiap launch
        // → toggle "hilang" setiap kali main walau sudah Save. Pola: sama seperti
        // TDR Mitigation (Settings bool + re-apply di akhir CheckAndApply()).
        public bool DisableRobloxAnimations { get; set; } = false;
        public bool EnableLowMemoryMode { get; set; } = false;

        // Auto-Reconnect — tawarkan sambung ulang setelah Roblox crash
        public bool EnableAutoReconnectPrompt { get; set; } = true;
    }
}
