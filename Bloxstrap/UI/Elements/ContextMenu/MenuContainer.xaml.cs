using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

using Bloxstrap.Integrations;
using Bloxstrap.UI.Elements.Settings.Pages;

namespace Bloxstrap.UI.Elements.ContextMenu
{
    /// <summary>
    /// Interaction logic for NotifyIconMenu.xaml
    /// </summary>
    public partial class MenuContainer
    {
        // i wouldve gladly done this as mvvm but turns out that data binding just does not work with menuitems for some reason so idk this sucks

        private readonly Watcher _watcher;

        // FIX (audit tray): game eksternal (diluncurkan di luar BoneFish) membawa
        // ActivityWatcher-nya sendiri yang dibuat-bongkar dinamis. Menu tidak lagi
        // memegang referensi tetap — ia selalu bertanya ke Watcher siapa game yang
        // sedang aktif (internal maupun eksternal) melalui FindActiveGameWatcher().
        private ActivityWatcher? _activityWatcher => _watcher.FindActiveGameWatcher();

        private ServerInformation? _serverInformationWindow;

        private ServerHistory? _gameHistoryWindow;

        public MenuContainer(Watcher watcher)
        {
            InitializeComponent();

            _watcher = watcher;

            // ActivityWatcher bisa belum ada saat menu dibuat (log belum ketemu) dan
            // watcher eksternal bisa muncul belakangan — daftarkan handler untuk
            // keduanya; handler mengecek ulang _activityWatcher saat event terpicu.
            _watcher.ActiveGameWatcherChanged += ActivityWatcherChangedHandler;

            if (_activityWatcher is not null)
            {
                _activityWatcher.OnLogOpen += ActivityWatcher_OnLogOpen;
                _activityWatcher.OnGameJoin += ActivityWatcher_OnGameJoin;
                _activityWatcher.OnGameLeave += ActivityWatcher_OnGameLeave;

                if (!App.Settings.Prop.UseDisableAppPatch)
                    GameHistoryMenuItem.Visibility = Visibility.Visible;
            }

            if (_watcher.RichPresence is not null)
                RichPresenceMenuItem.Visibility = Visibility.Visible;

            VersionTextBlock.Text = $"{App.ProjectName} v{App.Version}";

            // FIX (audit tray #4): status sesi pada item Restore diperbarui setiap kali
            // menu dibuka — user langsung tahu ada berapa aplikasi yang sedang ditahan.
            ContextMenu.Opened += (_, _) => RefreshSessionStatus();
            RefreshSessionStatus();
        }

        private void RefreshSessionStatus()
        {
            int suspended = App.GameSession.Store.ReadActive()?.SuspendedProcesses.Count ?? 0;
            GameSessionRestoreMenuItem.Header = BuildRestoreMenuHeader(suspended);

            // ── Phase 8: status-oriented tray ─────────────────────────────────────
            // Empat baris status di header menu. Di-refresh HANYA saat menu dibuka
            // (ContextMenu.Opened) — tanpa timer, tanpa polling. Semua builder
            // guarded try/catch: kegagalan deteksi tidak boleh mematikan menu.

            // Roblox: PID proses (1 syscall GetProcessesByName, di-dispose langsung)
            bool robloxRunning;
            int robloxPid = 0;
            try
            {
                Process[] procs = Process.GetProcessesByName("RobloxPlayerBeta");
                robloxRunning = procs.Length > 0;
                if (robloxRunning)
                    robloxPid = procs[0].Id;
                foreach (Process p in procs) { try { p.Dispose(); } catch { } }
            }
            catch
            {
                robloxRunning = false;
            }

            StatusRobloxText.Text = robloxRunning
                ? $"Roblox: Running (PID {robloxPid})"
                : "Roblox: Not running";

            // Item kontekstual: Launch hanya saat Roblox tidak berjalan, Close hanya
            // saat berjalan — tidak ada rebuild menu, hanya toggle Visibility.
            LaunchRobloxMenuItem.Visibility = robloxRunning ? Visibility.Collapsed : Visibility.Visible;
            CloseRobloxMenuItem.Visibility = robloxRunning ? Visibility.Visible : Visibility.Collapsed;

            StatusPerformanceText.Text = BuildPerformanceStatusText();
            StatusStorageText.Text = BuildStorageStatusText();
            StatusSecurityText.Text = BuildSecurityStatusText();
        }

        private static object BuildRestoreMenuHeader(int suspendedCount)
        {
            return new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Text = suspendedCount > 0
                    ? $"{Strings.ContextMenu_RestoreGameSession} ({suspendedCount})"
                    : Strings.ContextMenu_RestoreGameSession
            };
        }

        // ── Phase 8: builder teks status (semua murah, on-demand, fail-soft) ──────

        /// <summary>
        /// Preset aktif + catatan ForceExtremeMode. Tidak ada query hardware di sini —
        /// hanya baca Settings (in-memory).
        /// </summary>
        private static string BuildPerformanceStatusText()
        {
            try
            {
                string preset = App.Settings.Prop.SelectedPerformancePreset ?? "None";
                return App.Settings.Prop.ForceExtremeMode
                    ? $"Performance: {preset} (Extreme forced)"
                    : $"Performance: {preset}";
            }
            catch
            {
                return "Performance: Unknown";
            }
        }

        /// <summary>
        /// Storage 3-state dari HardwareProfile (cache-once; GetProfile() tanpa refresh
        /// statis = murah setelah panggilan pertama, tanpa WMI berulang).
        /// Unknown TIDAK PERNAH ditebak menjadi SSD/HDD (fase 15 kriteria 4-5).
        /// </summary>
        private static string BuildStorageStatusText()
        {
            try
            {
                var profile = HardwareProfileEngine.GetProfile();
                return $"Storage: {HardwareProfile.StorageDisplay(profile.StorageType)}";
            }
            catch
            {
                return "Storage: Unknown";
            }
        }

        /// <summary>
        /// State deteksi Windows Security (Phase 11): TIDAK PERNAH disembunyikan.
        /// Degraded/Unknown tetap tampil apa adanya — fail-safe, bukan fail-silent.
        /// </summary>
        private static string BuildSecurityStatusText()
        {
            try
            {
                var detector = App.GameSession.Detector;
                string state = detector.State switch
                {
                    GameSession.Models.SecurityDetectionState.Ok => "Protected",
                    GameSession.Models.SecurityDetectionState.Degraded => "Degraded",
                    _ => "Unknown"
                };
                return $"Security: {state}";
            }
            catch
            {
                // Kegagalan baca detektor → jangan pernah tampil "Protected".
                return "Security: Unknown";
            }
        }

        /// <summary>Cek proses Roblox murah (satu panggilan, langsung di-dispose).</summary>
        private static bool ProcessIsRunning(string name)
        {
            try
            {
                Process[] procs = Process.GetProcessesByName(name);
                bool running = procs.Length > 0;
                foreach (Process p in procs) { try { p.Dispose(); } catch { } }
                return running;
            }
            catch
            {
                return false;
            }
        }

        // Saat watcher game aktif berganti (eksternal attach/detach, log internal
        // terbuka), pindahkan subscription ke watcher yang baru.
        private void ActivityWatcherChangedHandler(object? sender, Watcher.ActiveGameWatcherChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Watcher is null)
                {
                    // Game eksternal lepas: sembunyikan menu terkait sesi (jangan panggil
                    // ActivityWatcher_OnGameLeave — guard sender-nya menuntut watcher).
                    InviteDeeplinkMenuItem.Visibility = Visibility.Collapsed;
                    ServerDetailsMenuItem.Visibility = Visibility.Collapsed;
                    _serverInformationWindow?.Close();

                    if (!HasInternalActivityWatcher())
                        GameHistoryMenuItem.Visibility = Visibility.Collapsed;

                    return;
                }

                e.Watcher.OnLogOpen += ActivityWatcher_OnLogOpen;
                e.Watcher.OnGameJoin += ActivityWatcher_OnGameJoin;
                e.Watcher.OnGameLeave += ActivityWatcher_OnGameLeave;

                if (!App.Settings.Prop.UseDisableAppPatch)
                    GameHistoryMenuItem.Visibility = Visibility.Visible;

                // Game eksternal bisa sudah in-game saat menempel (attachExisting melewati
                // histori log → event join tidak akan terpicu lagi) — tampilkan menunya
                // langsung.
                if (e.Watcher.InGame)
                {
                    if (e.Watcher.Data.ServerType == ServerType.Public)
                        InviteDeeplinkMenuItem.Visibility = Visibility.Visible;

                    ServerDetailsMenuItem.Visibility = Visibility.Visible;
                }
            });
        }

        private bool HasInternalActivityWatcher() => _watcher.ActivityWatcher is not null;

        /// <summary>
        /// Tutup window yang menampilkan data sesi game (server info & riwayat) —
        /// dipanggil saat game eksternal berakhir agar UI tidak menampilkan data lama.
        /// </summary>
        public void CloseServerDependentWindows()
        {
            _serverInformationWindow?.Close();
            _gameHistoryWindow?.Close();
        }

        public void ShowServerInformationWindow()
        {
            if (_serverInformationWindow is null)
            {
                if (_activityWatcher is null)
                    return;

                _serverInformationWindow = new(_activityWatcher);
                _serverInformationWindow.Closed += (_, _) => _serverInformationWindow = null;
            }

            if (!_serverInformationWindow.IsVisible)
                _serverInformationWindow.ShowDialog();
            else
                _serverInformationWindow.Activate();
        }

        public void ActivityWatcher_OnLogOpen(object? sender, EventArgs e) => 
            Dispatcher.Invoke(() => LogTracerMenuItem.Visibility = Visibility.Visible);

        public void ActivityWatcher_OnGameJoin(object? sender, EventArgs e)
        {
            // Handler bisa terpicu watcher mana pun (internal/eksternal); abaikan jika
            // pemicunya bukan watcher game yang sedang aktif.
            if (sender is not ActivityWatcher watcher || watcher != _activityWatcher)
                return;

            Dispatcher.Invoke(() => {
                if (_activityWatcher is null)
                    return;

                if (_activityWatcher.Data.ServerType == ServerType.Public)
                    InviteDeeplinkMenuItem.Visibility = Visibility.Visible;

                ServerDetailsMenuItem.Visibility = Visibility.Visible;
            });
        }

        public void ActivityWatcher_OnGameLeave(object? sender, EventArgs e)
        {
            if (sender is not ActivityWatcher watcher || watcher != _activityWatcher)
                return;

            Dispatcher.Invoke(() => {
                InviteDeeplinkMenuItem.Visibility = Visibility.Collapsed;
                ServerDetailsMenuItem.Visibility = Visibility.Collapsed;

                _serverInformationWindow?.Close();
            });
        }

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            // this is an awful hack lmao im so sorry to anyone who reads this
            // this is done to register the context menu wrapper as a tool window so it doesnt appear in the alt+tab switcher
            // https://stackoverflow.com/a/551847/11852173

            HWND hWnd = (HWND)new WindowInteropHelper(this).Handle;

            int exStyle = PInvoke.GetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            exStyle |= 0x00000080; //NativeMethods.WS_EX_TOOLWINDOW;
            PInvoke.SetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
        }

        private void Window_Closed(object sender, EventArgs e) => App.Logger.WriteLine("MenuContainer::Window_Closed", "Context menu container closed");

        private void RichPresenceMenuItem_Click(object sender, RoutedEventArgs e) => _watcher.RichPresence?.SetVisibility(((MenuItem)sender).IsChecked);

        private void InviteDeeplinkMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_activityWatcher is null)
                return;

            Clipboard.SetDataObject(_activityWatcher.Data.GetInviteDeeplink());
        }

        private void ServerDetailsMenuItem_Click(object sender, RoutedEventArgs e) => ShowServerInformationWindow();

        private void LogTracerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string? location = _activityWatcher?.LogLocation;

            if (location is not null)
                Utilities.ShellExecute(location);
        }

        private void CloseRobloxMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = Frontend.ShowMessageBox(
                Strings.ContextMenu_CloseRobloxMessage,
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo
            );

            if (result != MessageBoxResult.Yes)
                return;

            _watcher.KillRobloxProcess();
        }

        private void GameSessionRestoreMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _watcher.RestoreGameSessionNow();
        }

        private void GameSessionSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Arahkan settings window langsung ke halaman Game Session pada
            // peluncuran berikutnya, lalu buka settings window-nya.
            App.State.Prop.LastPage = typeof(GameSessionPage).FullName!;
            try { App.State.Save(); } catch { }

            Process.Start(Paths.Process, "-settings");
        }

        private void LaunchRobloxMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Jalur peluncuran yang sama dengan menu utama (LaunchHandler.LaunchRoblox)
            // — BUKAN protokol roblox-player mentah, agar semua patch/channel handling
            // BoneFish tetap berlaku.
            LaunchHandler.LaunchRoblox(LaunchMode.Player);
        }

        private void DiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Phase 6/8: tray membuka Diagnostic Center yang sama dengan halaman
            // Fast Flags — sumber data tunggal, tanpa MessageBox diagnostik lagi.
            new DiagnosticCenterWindow().ShowDialog();
        }

        private void JoinLastServerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_activityWatcher is null)
                return;

            if (_gameHistoryWindow is null)
            {
                _gameHistoryWindow = new(_activityWatcher);
                _gameHistoryWindow.Closed += (_, _) => _gameHistoryWindow = null;
            }

            if (!_gameHistoryWindow.IsVisible)
                _gameHistoryWindow.ShowDialog();
            else
                _gameHistoryWindow.Activate();
        }

        private void ExitBoneFishMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Pulihkan sesi Game Session apa pun yang masih aktif SEBELUM BoneFish
            // mati — termasuk sesi game eksternal (diluncurkan di luar BoneFish)
            // yang dipantau watcher dari system tray (v7.2.7). Tanpa ini, proses
            // yang disuspend tetap beku setelah app ditutup.
            try
            {
                var summary = App.GameSession.EndSession();
                if (summary.TotalSuspended > 0)
                    App.Logger.WriteLine("Menu::Exit", $"{summary.TotalSuspended} proses di-restore sebelum exit");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Menu::Exit", $"Restore sebelum exit gagal (non-fatal): {ex.Message}");
            }

            _watcher.SystemTrayExitSignal.TrySetResult(true);
            App.Terminate();
        }
    }
}
