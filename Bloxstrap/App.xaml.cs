using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;

using Bloxstrap.Integrations;
using Microsoft.Win32;

namespace Bloxstrap
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
#if QA_BUILD
        public const string ProjectName = "BoneFish-QA";
#else
        public const string ProjectName = "BoneFish";
#endif
        public const string ProjectOwner = "faizinuha";
        public const string ProjectRepository = "faizinuha/BoneFish";
        public const string ProjectDownloadLink = "https://github.com/faizinuha/BoneFish/releases";
        public const string ProjectHelpLink = "https://github.com/bloxstraplabs/bloxstrap/wiki";
        public const string ProjectSupportLink = "https://github.com/faizinuha/BoneFish/issues/new";
        public const string ProjectRemoteDataLink = "https://config.fishstrap.app/v1/Data.json";

        public const string RobloxPlayerAppName = "RobloxPlayerBeta.exe";
        public const string RobloxStudioAppName = "RobloxStudioBeta.exe";

        // simple shorthand for extremely frequently used and long string - this goes under HKCU
        public const string UninstallKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProjectName}";

        public const string ApisKey = $"Software\\{ProjectName}";

        public static LaunchSettings LaunchSettings { get; private set; } = null!;

        public static BuildMetadataAttribute BuildMetadata = Assembly.GetExecutingAssembly().GetCustomAttribute<BuildMetadataAttribute>()!;

        public static string Version = Assembly.GetExecutingAssembly().GetName().Version!.ToString()[..^2];

        public static Bootstrapper? Bootstrapper { get; set; } = null!;

        public static bool IsActionBuild => !String.IsNullOrEmpty(BuildMetadata.CommitRef);

        public static bool IsProductionBuild => IsActionBuild && BuildMetadata.CommitRef.StartsWith("tag", StringComparison.Ordinal);

        public static bool IsStudioVisible => !String.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);

        public static readonly MD5 MD5Provider = MD5.Create();

        public static readonly Logger Logger = new();

        public static readonly Dictionary<string, BaseTask> PendingSettingTasks = new();

        public static readonly JsonManager<Settings> Settings = new();

        public static readonly JsonManager<State> State = new();

        public static readonly JsonManager<RobloxState> RobloxState = new();

        public static readonly RemoteDataManager RemoteData = new();

        public static readonly FastFlagManager FastFlags = new();

        public static readonly GlobalSettingsManager GlobalSettings = new();

        public static readonly CookiesManager Cookies = new();

        public static readonly GameSession.GameSessionService GameSession = new();

        public static readonly HttpClient HttpClient = new(
            new HttpClientLoggingHandler(
                new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }
            )
        );

        private static bool _showingExceptionDialog = false;

        public static void Terminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;

            Logger.WriteLine("App::Terminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");

            // ★ FIX 1: Bersihkan service sebelum exit — cegah proses zombie
            CleanupServices();

            Environment.Exit(exitCodeNum);
        }

        public static void SoftTerminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;

            Logger.WriteLine("App::SoftTerminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");

            // ★ FIX 1: Bersihkan service sebelum exit — cegah proses zombie
            CleanupServices();

            // Pre-select background untuk sesi berikutnya kalo random mode aktif
            if (App.Settings?.Prop is not null && App.Settings.Prop.BackgroundRandomMode)
            {
                try
                {
                    var types = (AppBackgroundService.BackgroundType[])Enum.GetValues(typeof(AppBackgroundService.BackgroundType));
                    var nonCustomTypes = types.Where(t => t != AppBackgroundService.BackgroundType.Custom).ToList();
                    if (nonCustomTypes.Count > 0)
                    {
                        var randomType = nonCustomTypes[Random.Shared.Next(nonCustomTypes.Count)];
                        App.Settings.Prop.SelectedBackgroundType = randomType.ToString();
                        App.Settings.Save();
                        Logger.WriteLine("App::SoftTerminate", $"Pre-selected next background: {randomType}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("App::SoftTerminate", $"Failed to pre-select background: {ex.Message}");
                }
            }

            Current.Dispatcher.Invoke(() => Current.Shutdown(exitCodeNum));
        }

        void GlobalExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            Logger.WriteLine("App::GlobalExceptionHandler", "An exception occurred");

            FinalizeExceptionHandling(e.Exception);
        }

        public static void FinalizeExceptionHandling(AggregateException ex)
        {
            foreach (var innerEx in ex.InnerExceptions)
                Logger.WriteException("App::FinalizeExceptionHandling", innerEx);

            FinalizeExceptionHandling(ex.GetBaseException(), false);
        }

        public static void FinalizeExceptionHandling(Exception ex, bool log = true)
        {
            if (log)
                Logger.WriteException("App::FinalizeExceptionHandling", ex);

            if (_showingExceptionDialog)
                return;

            _showingExceptionDialog = true;

            SendLog();

            if (Bootstrapper?.Dialog != null)
            {
                if (Bootstrapper.Dialog.TaskbarProgressValue == 0)
                    Bootstrapper.Dialog.TaskbarProgressValue = 1; // make sure it's visible

                Bootstrapper.Dialog.TaskbarProgressState = TaskbarItemProgressState.Error;
            }

            Frontend.ShowExceptionDialog(ex);

            Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
        }

        public static async Task<GithubRelease?> GetLatestRelease()
        {
            const string LOG_IDENT = "App::GetLatestRelease";

            try
            {
                Uri githubReleasesUrl = new($"https://api.github.com/repos/{ProjectRepository}/releases/latest");
                var releaseInfo = await Http.GetJson<GithubRelease>(githubReleasesUrl);

                if (releaseInfo is null || releaseInfo.Assets is null)
                {
                    Logger.WriteLine(LOG_IDENT, "Encountered invalid data");
                    return null;
                }

                return releaseInfo;
            }
            catch (Exception ex)
            {
                Logger.WriteException(LOG_IDENT, ex);
            }

            return null;
        }

        public static void SendLog()
        {

        }

        private static bool IsPortableInstallFolder(string processDir)
        {
            bool hasSettings = false;
            bool hasState = false;
            int count = 0;

            foreach (var file in Directory.EnumerateFiles(processDir))
            {
                count++;

                if (count > 3)
                    return false;

                switch (Path.GetFileName(file))
                {
                    case "Settings.json":
                        hasSettings = true;
                        break;
                    case "State.json":
                        hasState = true;
                        break;
                }
            }

            return count <= 3 && hasSettings && hasState;
        }

        public static void AssertWindowsOSVersion()
        {
            const string LOG_IDENT = "App::AssertWindowsOSVersion";

            int major = Environment.OSVersion.Version.Major;
            if (major < 10) // Windows 10 and newer only
            {
                Logger.WriteLine(LOG_IDENT, $"Detected unsupported Windows version ({Environment.OSVersion.Version}).");

                if (!LaunchSettings.QuietFlag.Active)
                    Frontend.ShowMessageBox(Strings.App_OSDeprecation_Win7_81, MessageBoxImage.Error);

                Terminate(ErrorCode.ERROR_INVALID_FUNCTION);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string LOG_IDENT = "App::OnStartup";

            Locale.Initialize();

            base.OnStartup(e);

            Logger.WriteLine(LOG_IDENT, $"Starting {ProjectName} v{Version}");

            string userAgent = $"{ProjectName}/{Version}";

            if (IsActionBuild)
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from commit {BuildMetadata.CommitHash} ({BuildMetadata.CommitRef})");

                if (IsProductionBuild)
                    userAgent += $" (Production)";
                else
                    userAgent += $" (Artifact {BuildMetadata.CommitHash}, {BuildMetadata.CommitRef})";
            }
            else
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from {BuildMetadata.Machine}");

#if QA_BUILD
                userAgent += " (QA)";
#else
                userAgent += $" (Build {Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildMetadata.Machine))})";
#endif
            }

            Logger.WriteLine(LOG_IDENT, $"OSVersion: {Environment.OSVersion}");
            Logger.WriteLine(LOG_IDENT, $"Loaded from {Paths.Process}");
            Logger.WriteLine(LOG_IDENT, $"Temp path is {Paths.Temp}");
            Logger.WriteLine(LOG_IDENT, $"WindowsStartMenu path is {Paths.WindowsStartMenu}");

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            HttpClient.Timeout = TimeSpan.FromSeconds(30);
            HttpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

            LaunchSettings = new LaunchSettings(e.Args);

            // installation check begins here
            using var uninstallKey = Registry.CurrentUser.OpenSubKey(UninstallKey);
            string? installLocation = null;
            bool fixInstallLocation = false;

            if (uninstallKey?.GetValue("InstallLocation") is string value)
            {
                if (Directory.Exists(value))
                {
                    installLocation = value;
                }
                else
                {
                    // check if user profile folder has been renamed
                    var match = Regex.Match(value, @"^[a-zA-Z]:\\Users\\([^\\]+)", RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        string newLocation = value.Replace(match.Value, Paths.UserProfile, StringComparison.InvariantCultureIgnoreCase);

                        if (Directory.Exists(newLocation))
                        {
                            installLocation = newLocation;
                            fixInstallLocation = true;
                        }
                    }
                }
            }

            // silently change install location if we detect a portable run
            if (installLocation is null && Directory.GetParent(Paths.Process)?.FullName is string processDir)
            {
                if (IsPortableInstallFolder(processDir))
                {
                    installLocation = processDir;
                    fixInstallLocation = true;
                }
            }

            if (fixInstallLocation && installLocation is not null)
            {
                var installer = new Installer
                {
                    InstallLocation = installLocation,
                    IsImplicitInstall = true
                };

                if (installer.CheckInstallLocation())
                {
                    Logger.WriteLine(LOG_IDENT, $"Changing install location to '{installLocation}'");
                    installer.DoInstall();
                }
                else
                {
                    // force reinstall
                    installLocation = null;
                }
            }

            if (installLocation is null)
            {
                Logger.Initialize(true);
                AssertWindowsOSVersion();
                Logger.WriteLine(LOG_IDENT, "Not installed, launching the installer");
                AssertWindowsOSVersion(); // prevent new installs from unsupported operating systems
                LaunchHandler.LaunchInstaller();
            }
            else
            {
                Paths.Initialize(installLocation);

                // ensure executable is in the install directory
                if (Paths.Process != Paths.Application && !File.Exists(Paths.Application))
                    File.Copy(Paths.Process, Paths.Application);

                Logger.Initialize(LaunchSettings.UninstallFlag.Active);

                if (!Logger.Initialized && !Logger.NoWriteMode)
                {
                    Logger.WriteLine(LOG_IDENT, "Possible duplicate launch detected, terminating.");
                    Terminate();
                }

                Settings.Load();

                // ── Legacy settings migration (FIX v7.4.0) ───────────────────────────
                // Users with a Settings.json from before the rename to BoneFish may still
                // have an old project name ("Fishstrap"/"Bloxstrap") persisted as their
                // bootstrapper title. Reset only exact matches of historical project
                // names — custom user titles are never touched. The Save() below persists
                // the fix, so the migration is self-limiting and only ever rewrites the
                // file once.
                if (SettingsMigration.MigrateLegacyValues(Settings.Prop))
                    Settings.Save();

                State.Load();
                RobloxState.Load();

                // ── Preset migration REMOVED in v5.0.0 ───────────────────────────────────────
                // v4.5.0 punya blok migrasi yang auto-overwrite Settings.SelectedPerformancePreset
                // dari "UltraLow" ke "ExtremePerformance" karena di v4.5.0 kedua preset tersebut
                // digabung jadi satu. v5.0.0 merestrukturisasi UI kembali ke 2 preset terpisah
                // (UltraLow + ExtremePerformance) — lihat CHANGELOG.md v5.0.0 dan
                // FastFlagsViewModel.ApplyUltraLowSpecPreset().
                //
                // Blok migrasi lama akan jadi FOOTGUN di v5.0.0: setiap user yang klik tombol
                // UltraLow dari UI (pilihan valid post-sync) akan di-overwrite ke
                // "ExtremePerformance" tiap launch — membuat UltraLow button praktis tidak
                // bisa dipakai. Karena itu blok dihapus seluruhnya di v5.0.0.
                //
                // Catatan untuk kontributor masa depan: JANGAN reintroduce migrasi UltraLow →
                // ExtremePerformance tanpa re-check architecture preset saat ini.
                // Daftar preset valid di v5.0.0: UltraLow, Balanced, Stable,
                // ExtremePerformance, AutoOptimize, None — semuanya tidak di-overwrite
                // oleh block mana pun di OnStartup().
                FastFlags.Load();
                GlobalSettings.Load();

                // ── Retroactive .bak cleanup (FIX v7.3.1) ─────────────────────
                // Bersihkan backup .bak yang menumpuk (>2 versi) di semua direktori
                // BoneFish saat startup. Dilakukan SETELAH Load() supaya logging
                // sudah aktif dan Paths.Base sudah valid.
                try
                {
                    JsonManager<Settings>.CleanupAllBackupsOnStartup();
                }
                catch (Exception ex)
                {
                    Logger.WriteException(LOG_IDENT, ex);
                }

                try
                {
                    // Watcher mode = launcher baru saja hand-off sesi aktif (proses tetap
                    // ter-suspend). JANGAN restore di sini — Watcher.Run yang mengadopsi
                    // sesi itu. Restore di titik ini justru membunuh proteksi: watcher lama
                    // yang mati meninggalkan Watcher.pid, dan ShouldRestoreStale akan
                    // menganggap sesi valid sebagai stale (bug v7.2.5).
                    if (!App.LaunchSettings.WatcherFlag.Active
                        && App.GameSession.Store.ReadActive() is { } stale
                        && App.GameSession.ShouldRestoreStale(stale))
                    {
                        Logger.WriteLine(LOG_IDENT, "Recovering a stale Game Session before continuing startup.");
                        App.GameSession.EndSession();
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Game Session stale recovery failed (non-fatal): {ex.Message}");
                }

                if (Settings.Prop.AllowCookieAccess)
                    Task.Run(Cookies.LoadCookies);

                if (!Locale.SupportedLocales.ContainsKey(Settings.Prop.Locale))
                {
                    Settings.Prop.Locale = "nil";
                    Settings.Save();
                }

                Locale.Set(Settings.Prop.Locale);

                if (!LaunchSettings.BypassUpdateCheck)
                    Installer.HandleUpgrade();

                Task.Run(App.RemoteData.LoadData); // ok

                // ★ FIX 3: Background SELALU aktif — validasi background files saat startup
                // (EnableWallpaperLauncher dihapus)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Logger.WriteLine(LOG_IDENT, "Validating app background files...");
                        bool isValid = await AppBackgroundService.ValidateBackgroundFilesAsync();
                        if (isValid)
                            Logger.WriteLine(LOG_IDENT, "App background files validated");

                        // Pre-load background untuk startup berikutnya
                        // Kalo random mode ON, pilih random untuk sesi berikutnya
                        if (App.Settings.Prop.BackgroundRandomMode)
                        {
                            var types = (AppBackgroundService.BackgroundType[])Enum.GetValues(typeof(AppBackgroundService.BackgroundType));
                            var nonCustomTypes = types.Where(t => t != AppBackgroundService.BackgroundType.Custom).ToList();
                            if (nonCustomTypes.Count > 0)
                            {
                                var randomType = nonCustomTypes[Random.Shared.Next(nonCustomTypes.Count)];
                                App.Settings.Prop.SelectedBackgroundType = randomType.ToString();
                                try { App.Settings.Save(); } catch { }
                                Logger.WriteLine(LOG_IDENT, $"Pre-selected next background: {randomType} (random mode)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine(LOG_IDENT, $"App background initialization failed: {ex.Message}");
                    }
                });

                // ★ AUTO CACHE CLEANUP (Fitur B) — non-blocking, jalan sekali per startup
                if (App.Settings.Prop.EnableAutoCacheCleanup)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            Logger.WriteLine(LOG_IDENT, "Running auto cache cleanup...");
                            int deleted = await CacheCleanerService.CleanupOldCache(
                                maxAgeDays: App.Settings.Prop.CacheCleanupMaxAgeDays);
                            if (deleted > 0)
                                Logger.WriteLine(LOG_IDENT, $"Auto cache cleanup removed {deleted} file(s)");
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLine(LOG_IDENT, $"Auto cache cleanup failed (non-fatal): {ex.Message}");
                        }
                    });
                }

                WindowsRegistry.RegisterApis(); // we want to register those early on
                                                // so we wont have any issues with bloxshade

                LaunchHandler.ProcessLaunchArgs();
            }

            // you must *explicitly* call terminate when everything is done, it won't be called implicitly
        }

        /// <summary>
        /// ★ FIX 1: Bersihkan CrosshairService &amp; HotkeyService sebelum process exit.
        /// Dipanggil dari Terminate() dan SoftTerminate() — mencakup semua jalur shutdown.
        /// Mencegah proses zombie yang tetap hidup karena thread IsBackground=false (sekarang true)
        /// tapi tetap baik untuk cleanup yang rapi.
        /// </summary>
        private static void CleanupServices()
        {
            try
            {
                if (App.GameSession.ActiveSession is { HandedOffToWatcher: false })
                {
                    Logger.WriteLine("App::CleanupServices", "Restoring Game Session before process exit");
                    App.GameSession.EndSession();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("App::CleanupServices", $"Game Session cleanup: {ex.Message}");
            }

            try
            {
                if (CrosshairService.Instance != null)
                {
                    Logger.WriteLine("App::CleanupServices", "Disposing CrosshairService");
                    CrosshairService.Instance.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("App::CleanupServices", $"CrosshairService cleanup: {ex.Message}");
            }

            try
            {
                if (HotkeyService.Instance != null)
                {
                    Logger.WriteLine("App::CleanupServices", "Stopping HotkeyService");
                    HotkeyService.Instance.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("App::CleanupServices", $"HotkeyService cleanup: {ex.Message}");
            }
        }
    }
}
