using Bloxstrap.AppData;
using Bloxstrap.GameSession;
using Bloxstrap.GameSession.Models;
using Bloxstrap.Integrations;
using System.Web;
using System.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Bloxstrap
{
    public class Watcher : IDisposable
    {
        public const string ExitEventName = "BoneFish-WatcherExitEvent";

        private readonly InterProcessLock _lock = new("Watcher");

        private readonly System.Threading.EventWaitHandle _exitEvent = new(false, System.Threading.EventResetMode.AutoReset, ExitEventName);

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        // Game join data untuk auto-reconnect setelah crash
        private readonly long? _joinPlaceId;
        private readonly string? _joinJobId;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly WindowManipulation? WindowManipulation;

        public readonly DiscordRichPresence? RichPresence;

        public readonly RobloxNotification? Notification;

        public readonly FpsMonitorService? FpsMonitor;

        public readonly CrosshairService? Crosshair;

        public readonly HotkeyService? Hotkeys;

        // ── Render-Stall Detector (diagnostik, BUKAN auto-restart) ──────────────────
        // Hasil audit white-screen v7.x: pola "freeze → layar putih → pulih" di iGPU
        // Intel tua hampir selalu TDR driver (Event ID 4101) atau texture streaming HDD,
        // bukan crash Roblox. Detektor ini membedakan: proses hidup + window tidak
        // merespons = RENDER STALL (dictat), vs proses mati = crash (diproses terpisah
        // via exit code). HANYA mencatat ke log — tidak me-restart Roblox, tidak
        // mengubah FastFlag saat stall berlangsung.
        private int _stallCheckCount = 0;
        private DateTime? _stallStartUtc = null;
        private bool _stallLogged = false;

        // IsHungAppWindow baru true setelah window tidak merespons ~5 detik
        // (SendMessageTimeout internal default OS). 3 tick berturut-turut menekan
        // false positive dari window yang cuma sibuk sesaat.
        private const int StallCheckThreshold = 3;

        // Keep external Roblox activity available to tray features while BoneFish
        // remains in the system tray.
        private int? _externalGamePid;
        private ActivityWatcher? _externalActivityWatcher;
        private Task? _externalMonitorTask;
        private readonly Dictionary<int, DateTime> _externalLogRetryAfterUtc = new();
        private const string RobloxPlayerProcessName = "RobloxPlayerBeta";

        private void EnsureExternalGameMonitor()
        {
            if (!App.Settings.Prop.EnableSystemTrayOnClose
                || _externalMonitorTask is { IsCompleted: false })
                return;

            _externalMonitorTask = Task.Run(MonitorExternalGameSessions);
        }

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";


            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists, signaling it to exit...");

                // Wake up any waiting watcher zombies so they exit gracefully. The exit event
                // is AutoReset, so each Set() only releases a single waiter; loop to handle
                // more than one stale instance, retrying the lock after each signal.
                for (int i = 0; i < 5 && !_lock.IsAcquired; i++)
                {
                    try
                    {
                        _exitEvent.Set();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }

                    _lock.RetryAcquire(TimeSpan.FromMilliseconds(400));
                }

                if (_lock.IsAcquired)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Successfully took over Watcher lock.");
                }
                else
                {
                    // The previous watcher didn't exit gracefully. Force-kill ONLY the stuck
                    // watcher process (identified by its PID file), so we never take down a
                    // legitimate bootstrapper/settings/menu process running from the same path.
                    App.Logger.WriteLine(LOG_IDENT, "Watcher did not exit gracefully. Force-killing the stuck watcher process...");
                    KillStuckWatcher();
                    _lock.RetryAcquire(TimeSpan.FromSeconds(1));
                }
            }

            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance still exists, aborting watcher startup.");
                return;
            }

            // We hold the lock now. Clear any leftover exit-event signal (an AutoReset event
            // can retain a pending Set() if no waiter consumed it) so we don't exit the system
            // tray prematurely later, then record our PID so a future watcher can target us precisely.
            try
            {
                _exitEvent.Reset();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            WriteWatcherPidFile();

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                while (gameClientProcess.MainWindowHandle == IntPtr.Zero)
                    Thread.Sleep(100);

                _watcherData = new() { ProcessId = gameClientProcess.Id, Handle = gameClientProcess.MainWindowHandle.ToInt64() };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            // Simpan game join data dari Bootstrapper untuk auto-reconnect
            _joinPlaceId = _watcherData.PlaceId;
            _joinJobId = _watcherData.JobId;
            if (_joinPlaceId.HasValue)
                App.Logger.WriteLine(LOG_IDENT, $"Game join data tersimpan: PlaceId={_joinPlaceId}, JobId={_joinJobId ?? "(none)"}");

            // Render-stall detector butuh window handle yang valid. Kalau handle 0
            // (edge case di sebagian jalur launch), IsHungAppWindow selalu false dan
            // deteksi diam-diam tidak jalan — catat sekali supaya user tahu dari log.
            if (_watcherData.Handle == 0)
                App.Logger.WriteLine(LOG_IDENT, "Render-stall detector INACTIVE: window handle = 0 (stall tidak akan terdeteksi)");

            WindowManipulation = new(_watcherData.Handle, _watcherData.ProcessId);

            // Initialize DNS resilience for network stability.
            // (Performance FastFlags are now applied by the Bootstrapper before launch.)
            try
            {
                var dnsTask = DnsResilienceService.TestDnsConnectivityAsync();
                dnsTask.Wait(TimeSpan.FromSeconds(3)); // Wait max 3 seconds

                if (dnsTask.IsCompletedSuccessfully)
                {
                    App.Logger.WriteLine(LOG_IDENT, "DNS resilience service initialized");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            bool activityTrackingEnabled = App.Settings.Prop.EnableActivityTracking;
            if (activityTrackingEnabled
                && !String.IsNullOrEmpty(_watcherData.LogFile)
                && File.Exists(_watcherData.LogFile))
            {
                ActivityWatcher = new(_watcherData.LogFile);

                if (activityTrackingEnabled && App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                if (activityTrackingEnabled && App.Settings.Prop.UseDiscordRichPresence && !App.State.Prop.WatcherRunning)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Running rpc");
                    RichPresence = new(ActivityWatcher);
                }

                if (activityTrackingEnabled && App.Settings.Prop.EnableRobloxNotifications)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Initializing Roblox notifications");
                    Notification = new(ActivityWatcher);
                }

                if (activityTrackingEnabled && App.Settings.Prop.EnableFpsMonitor)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Initializing FPS Monitor");
                    FpsMonitor = new(ActivityWatcher, _watcherData.ProcessId);
                }

            }
            else if (activityTrackingEnabled)
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox log file is unavailable; skipping activity tracking, tray icon will still appear");
            }

            // Crosshair & Hotkeys adalah fitur INDEPENDEN — tidak butuh ActivityTracking.
            // Inisialisasi di sini (di luar blok EnableActivityTracking) agar tetap jalan
            // meski user matiin tracking atau log file Roblox gak ketemu.
            //
            // ★ LAZY START: CrosshairService constructor cuma set Instance (biar hotkey bisa akses).
            // Thread + dispatcher cuma dibuat kalo Start() dipanggil — yaitu kalo EnableCrosshair = true.
            // Kalo crosshair mati, user tinggal pencet Ctrl+Shift+C → Toggle() → lazy-start otomatis.
            Crosshair = new CrosshairService();
            if (App.Settings.Prop.EnableCrosshair)
            {
                App.Logger.WriteLine(LOG_IDENT, "Starting Crosshair Service");
                Crosshair.Start();
            }

            // Global hotkey service — register Ctrl+Shift+C/F/N
            if (App.Settings.Prop.EnableHotkeys)
            {
                App.Logger.WriteLine(LOG_IDENT, "Initializing Hotkey Service");
                Hotkeys = new HotkeyService();
                Hotkeys.Start();
            }

            // Always initialize the tray icon last; guard it so a failure here
            // never leaves the user without a system tray icon.
            try
            {
                _notifyIcon = new(this);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to initialize system tray icon");
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            RestoreLegacyGameSession();
        }

        /// <summary>Restore applications left suspended by an older BoneFish version.</summary>
        private void RestoreLegacyGameSession()
        {
            const string LOG_IDENT = "Watcher::RestoreLegacyGameSession";

            try
            {
                if (App.GameSession.Store.ReadActive() is not { } session
                    || session.SuspendedProcesses.Count == 0)
                    return;

                App.Logger.WriteLine(LOG_IDENT,
                    $"Restoring {session.SuspendedProcesses.Count} application(s) left suspended by a previous version.");
                SessionSummary summary = App.GameSession.EndSession();
                if (summary.TotalSuspended > 0)
                    _notifyIcon?.ShowAlert("BoneFish", App.GameSession.FormatSummary(summary), 10, null);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Legacy session restore failed (non-fatal): {ex.Message}");
            }
        }

        #region Watcher PID tracking
        // Path to a small file recording the PID of the watcher that currently holds the lock.
        // This lets a new watcher precisely target a stuck (zombie) watcher for cleanup without
        // killing unrelated BoneFish processes (bootstrapper/settings/menu) from the same path.
        private static string WatcherPidFile => Path.Combine(Paths.Base, "Watcher.pid");

        private static void WriteWatcherPidFile()
        {
            const string LOG_IDENT = "Watcher::WriteWatcherPidFile";

            try
            {
                File.WriteAllText(WatcherPidFile, Environment.ProcessId.ToString());
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static void TryDeleteWatcherPidFile()
        {
            try
            {
                if (File.Exists(WatcherPidFile))
                    File.Delete(WatcherPidFile);
            }
            catch (Exception)
            {
                // best-effort cleanup
            }
        }

        private static void KillStuckWatcher()
        {
            const string LOG_IDENT = "Watcher::KillStuckWatcher";

            if (!File.Exists(WatcherPidFile))
            {
                App.Logger.WriteLine(LOG_IDENT, "No watcher PID file found; nothing to clean up.");
                return;
            }

            if (!int.TryParse(File.ReadAllText(WatcherPidFile).Trim(), out int pid))
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher PID file is malformed; deleting it.");
                TryDeleteWatcherPidFile();
                return;
            }

            // never kill ourselves
            if (pid == Environment.ProcessId)
                return;

            try
            {
                using var process = Process.GetProcessById(pid);

                // Verify this is actually a BoneFish process running from our executable before
                // killing it, so we never take down an unrelated process that happens to have
                // reused the PID.
                string currentName = Path.GetFileNameWithoutExtension(Paths.Process);
                string? processPath = process.MainModule?.FileName;

                bool isBoneFish =
                    string.Equals(process.ProcessName, currentName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(processPath)
                    && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(Paths.Process), StringComparison.OrdinalIgnoreCase);

                if (!isBoneFish)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} is not a BoneFish process from our path; skipping kill.");
                    TryDeleteWatcherPidFile();
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Killing stuck watcher process (pid={pid})");
                process.Kill();
                process.WaitForExit(2000);
            }
            catch (ArgumentException)
            {
                // process already exited
                App.Logger.WriteLine(LOG_IDENT, $"Stuck watcher process (pid={pid}) has already exited.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            TryDeleteWatcherPidFile();
        }
        #endregion

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        /// <summary>
        /// Escape hatch manual dari system tray / halaman settings: pulihkan semua
        /// proses yang sedang disuspend SEKARANG, tanpa menunggu proses Roblox mati.
        ///
        /// Dua jalur, keduanya dijalankan:
        /// 1. Record sesi aktif (active.json) — EndSession normal.
        /// 2. Rescue scan — kalau ada proses beku yang TIDAK tercatat (record hilang,
        ///    restore lama "berhasil" tapi thread tertinggal), cari via probe
        ///    per-thread di seluruh sistem dan resume.
        /// </summary>
        public void RestoreGameSessionNow()
        {
            const string LOG_IDENT = "Watcher::RestoreGameSessionNow";

            try
            {
                SessionSummary? summary = null;
                if (App.GameSession.Store.ReadActive() is { } session)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Manually restoring session (suspended={session.SuspendedProcesses.Count})");
                    summary = App.GameSession.EndSession();
                }

                IReadOnlyList<RescuedProcess> rescued = App.GameSession.RescueSuspendedProcesses();
                if (rescued.Count > 0)
                {
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Rescue scan: {rescued.Count} proses beku di-resume: " +
                        String.Join(", ", rescued.Select(process => $"{process.ProcessName}({process.ProcessId}) x{process.ThreadCount}")));
                }

                string? message = null;
                if (summary is { TotalSuspended: > 0 })
                    message = App.GameSession.FormatSummary(summary);

                if (rescued.Count > 0)
                {
                    string rescueMessage = String.Format(Strings.GameSession_RestoreNow_Rescued, rescued.Count);
                    message = message is null ? rescueMessage : $"{message}\n{rescueMessage}";
                }

                _notifyIcon?.ShowAlert("BoneFish", message ?? Strings.GameSession_RestoreNow_None, 10, null);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Manual restore failed: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>
        /// Sambung ulang ke game Roblox terakhir menggunakan PlaceId + JobId yang tersimpan.
        /// Kalau JobId sudah tidak valid (server penuh/tutup), fallback ke PlaceId saja
        /// (server mana pun yang tersedia) — Bootstrapper akan menangani ini secara otomatis.
        /// </summary>
        public void RejoinRoblox()
        {
            const string LOG_IDENT = "Watcher::RejoinRoblox";

            if (_joinPlaceId == null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Tidak ada PlaceId — tidak bisa sambung ulang");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Menyambung ulang ke PlaceId={_joinPlaceId}, JobId={_joinJobId ?? "(new server)"}");

            try
            {
                // Bangun PlaceLauncher URL dengan atau tanpa JobId
                string placeLauncherUrl = Utility.UrlBuilder.BuildPlacelauncherUrl(
                    _joinPlaceId.Value,
                    _joinJobId  // null → RequestGameJob tanpa gameId → fallback ke server baru
                );

                // Bungkus dalam format roblox-player:1+placelauncherurl:{encoded_url}+
                string launchUrl = $"roblox-player:1+placelauncherurl:{HttpUtility.UrlEncode(placeLauncherUrl)}+";

                App.Logger.WriteLine(LOG_IDENT, $"Launch args: {launchUrl}");

                Process.Start(Paths.Process, $"-player \"{launchUrl}\"");

                App.Logger.WriteLine(LOG_IDENT, "BoneFish Bootstrapper diluncurkan untuk sambung ulang");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Gagal meluncurkan sambung ulang: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public readonly TaskCompletionSource<bool> SystemTrayExitSignal = new();

        /// <summary>
        /// Roblox crash exit codes (Windows NTSTATUS). Exit code 0 = normal/graceful shutdown
        /// (user klik Leave/X). Nilai negatif menandakan exception/kill paksa oleh OS.
        /// Sumber: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e1262b83fc
        /// </summary>
        private static bool IsCrashExit(int exitCode)
        {
            return exitCode != 0 && exitCode < 0;
        }

        public async Task Run()
        {
            const string LOG_IDENT = "Watcher::Run";

            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();
            WindowManipulation?.ApplyWindowModifications();

            // Start monitoring immediately so Roblox launched from the official
            // tray or website is handled even before this tracked session ends.
            EnsureExternalGameMonitor();

            if (App.Settings.Prop.FakeBorderlessFullscreen)
                WindowManipulation?.FakeBorderless();

            // Capture process reference + session start time SEBELUM Roblox exit,
            // supaya kita bisa baca ExitCode setelah process mati.
            int exitCode = 0;
            bool couldReadExitCode = false;
            DateTime sessionStart = DateTime.UtcNow;

            try
            {
                using var robloxProcess = Process.GetProcessById(_watcherData.ProcessId);
                try { sessionStart = robloxProcess.StartTime.ToUniversalTime(); } catch { }

                while (WaitForRobloxTick())
                    await Task.Delay(1000);

                // Process sudah exit — baca exit code dari Process object yang masih valid
                robloxProcess.WaitForExit(500);
                if (robloxProcess.HasExited)
                {
                    exitCode = robloxProcess.ExitCode;
                    couldReadExitCode = true;
                }
            }
            catch (Exception ex)
            {
                // Fallback: Process.GetProcessById gagal (mis. process sudah mati sebelum kita dapat handle)
                // Gunakan polling loop tanpa tracking exit code
                App.Logger.WriteLine(LOG_IDENT, $"Could not attach to process for exit code tracking: {ex.Message}");

                while (WaitForRobloxTick())
                    await Task.Delay(1000);
            }

            // Restore a matching legacy session immediately after Roblox exits.
            try
            {
                if (App.GameSession.Store.ReadActive() is { } session
                    && (session.GameProcessId == 0 || session.GameProcessId == _watcherData.ProcessId))
                {
                    var summary = App.GameSession.EndSession(_watcherData.ProcessId);
                    if (summary.TotalSuspended > 0)
                        _notifyIcon?.ShowAlert("BoneFish", App.GameSession.FormatSummary(summary), 10, null);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Legacy session restore failed (non-fatal): {ex.Message}");
            }

            // Retry a partial legacy restore before this watcher exits.
            try
            {
                if (App.GameSession.Store.ReadActive() is { RestoreState: SessionRestoreState.Pending, SuspendedProcesses.Count: > 0 }
                    && IsWatcherActiveSession())
                {
                    App.Logger.WriteLine(LOG_IDENT,
                        "Legacy session restore is still pending — retrying before watcher exit");
                    SessionSummary pendingSummary = App.GameSession.EndSession();
                    if (pendingSummary.TotalSuspended > 0)
                        _notifyIcon?.ShowAlert("BoneFish", App.GameSession.FormatSummary(pendingSummary), 10, null);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Sweep session Pending gagal (non-fatal): {ex.Message}");
            }

            // ── Auto-Reconnect: Deteksi Crash ────────────────────────────────────
            bool isCrash = couldReadExitCode && IsCrashExit(exitCode);
            bool longEnoughSession = (DateTime.UtcNow - sessionStart).TotalMinutes > 2;

            if (isCrash && longEnoughSession && _joinPlaceId != null && App.Settings.Prop.EnableAutoReconnectPrompt)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Roblox crash terdeteksi! ExitCode={exitCode}, session={(DateTime.UtcNow - sessionStart).TotalMinutes:F1} menit");

                if (App.Settings.Prop.EnableSystemTrayOnClose)
                {
                    // System tray mode: tampilkan balloon notification dengan click handler
                    _notifyIcon?.ShowAlert(
                        "BoneFish",
                        Strings.Watcher_CrashDetected_Balloon,
                        15,
                        (_, _) => RejoinRoblox()
                    );

                    // Tetap jalan di system tray — user bisa klik notif untuk sambung ulang,
                    // atau klik Exit dari context menu untuk tutup. Sambil menunggu, pantau
                    // game Roblox yang diluncurkan di luar BoneFish (v7.2.7).
                    var exitSignalTask = SystemTrayExitSignal.Task;
                    var externalExitTask = Task.Run(() => {
                        try { return _exitEvent.WaitOne(); } catch { return false; }
                    });
                    EnsureExternalGameMonitor();
                    await Task.WhenAny(exitSignalTask, externalExitTask);
                }
                else
                {
                    // Non-system-tray mode: tampilkan MessageBox
                    var result = Frontend.ShowMessageBox(
                        Strings.Watcher_CrashDetected_Message,
                        MessageBoxImage.Question,
                        MessageBoxButton.YesNo
                    );

                    if (result == MessageBoxResult.Yes)
                        RejoinRoblox();
                }
            }
            else
            {
                // Tidak crash — lanjut ke flow normal
                // Jika system tray mode aktif, jangan langsung exit
                if (App.Settings.Prop.EnableSystemTrayOnClose)
                {
                    _notifyIcon?.ShowAlert("BoneFish", "Roblox telah ditutup. BoneFish masih berjalan di system tray.", 5, null);
                    // Menunggu sampai user klik Exit dari context menu ATAU ada watcher baru yang menyuruh kita exit.
                    // Sambil menunggu, pantau game Roblox yang diluncurkan di luar BoneFish (v7.2.7).
                    var exitSignalTask = SystemTrayExitSignal.Task;
                    var externalExitTask = Task.Run(() => {
                        try
                        {
                            _exitEvent.WaitOne();
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    EnsureExternalGameMonitor();
                    await Task.WhenAny(exitSignalTask, externalExitTask);

                    // FIX (audit): user memilih Exit — pulihkan sesi yang masih dipegang
                    // watcher ini SEBELUM proses mati. Tanpa ini, record Pending sisa
                    // end sebagian tidak pernah di-restore lagi → proses beku permanen.
                    try { App.GameSession.EndSession(); } catch { }
                }
            }

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        /// <summary>
        /// Satu tick dari loop tunggu Roblox exit. Mengembalikan false jika Roblox sudah
        /// mati (loop berhenti), true jika masih hidup (lanjut tick berikutnya).
        /// Sambil menunggu, deteksi render stall: proses hidup tapi window tidak
        /// merespons pesan Windows — dicatat ke log sebagai diagnostik ringan.
        /// </summary>
        private bool WaitForRobloxTick()
        {
            const string LOG_IDENT = "Watcher::StallDetector";

            // FIX (audit RAM): dulu Process.GetProcesses() dipanggil per detik dan hasilnya
            // tidak pernah di-dispose — ratusan handle OS bocor per menit, RAM merambat naik.
            bool processAlive = false;
            try
            {
                using var trackedProcess = Process.GetProcessById(_watcherData!.ProcessId);
                processAlive = !trackedProcess.HasExited;
            }
            catch
            {
                processAlive = false; // proses sudah mati / PID didaur ulang
            }

            if (!processAlive)
                return false;

            bool hung = false;
            try
            {
                hung = PInvoke.IsHungAppWindow((HWND)(IntPtr)_watcherData!.Handle);
            }
            catch (Exception ex)
            {
                // Handle stale/hilang (mis. Roblox recreate window setelah TDR) — non-fatal.
                App.Logger.WriteLine(LOG_IDENT, $"IsHungAppWindow gagal (non-fatal): {ex.Message}");
            }

            if (hung)
            {
                _stallCheckCount++;
                _stallStartUtc ??= DateTime.UtcNow;

                if (_stallCheckCount >= StallCheckThreshold && !_stallLogged)
                {
                    _stallLogged = true;
                    LogRenderStallDiagnostic();
                }
            }
            else
            {
                if (_stallLogged)
                {
                    double stallSeconds = (DateTime.UtcNow - (_stallStartUtc ?? DateTime.UtcNow)).TotalSeconds;
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Roblox pulih dari render stall setelah ~{stallSeconds:F1} detik (proses tetap hidup)");
                }
                _stallCheckCount = 0;
                _stallStartUtc = null;
                _stallLogged = false;
            }

            return true;
        }

        /// <summary>
        /// Tulis satu baris diagnostik saat render stall terdeteksi. Data ringan, tanpa
        /// informasi pribadi. Membantu membedakan penyebab: TDR GPU (Event Viewer 4101)
        /// vs texture streaming HDD vs hang permanen.
        /// </summary>
        private void LogRenderStallDiagnostic()
        {
            const string LOG_IDENT = "Watcher::StallDetector";

            try
            {
                long workingSetMB = 0;
                try
                {
                    using var proc = Process.GetProcessById(_watcherData!.ProcessId);
                    workingSetMB = proc.WorkingSet64 / 1024 / 1024;
                }
                catch { /* proses mati di antara cek — pakai data yang ada */ }

                // Reuse AutoOptimizeService.GetSystemInfo() — sudah memberi CPU/RAM/
                // Storage(HDD/SSD)/Tier yang relevan dengan teori HDD-paging, tanpa
                // menambah dependency baru (deteksi storage sudah di-cache).
                string sysInfo = "";
                try { sysInfo = Integrations.AutoOptimizeService.GetSystemInfo(); } catch { }

                App.Logger.WriteLine(LOG_IDENT,
                    "RENDER STALL TERDETEKSI (Roblox hang, proses MASIH HIDUP — bukan crash) | " +
                    $"PID={_watcherData!.ProcessId} | windowHandle={_watcherData.Handle} | " +
                    $"workingSet={workingSetMB}MB | preset={App.Settings.Prop.SelectedPerformancePreset ?? "None"} | " +
                    $"OptimizeForLowEnd={App.Settings.Prop.OptimizeForLowEnd} | " +
                    $"FakeBorderless={App.Settings.Prop.FakeBorderlessFullscreen} | " +
                    $"FpsMonitor={App.Settings.Prop.EnableFpsMonitor} | Crosshair={App.Settings.Prop.EnableCrosshair} | " +
                    $"TdrMitigation={App.Settings.Prop.EnableTdrMitigation} | " +
                    $"System={sysInfo}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Gagal menulis diagnostik stall: {ex.Message}");
            }
        }

        // A pending record means a previous restore only partially succeeded. Only
        // restore it here if it belongs to this watcher or no game PID was recorded.
        private bool IsWatcherActiveSession()
        {
            if (App.GameSession.Store.ReadActive() is not { } session)
                return false;

            // Record Pending yang proses gamenya tidak diketahui/tidak tercantum
            // aman di-restore oleh watcher mana pun: game-nya sudah tidak memegang
            // sesi (state Pending berarti restore sudah pernah dicoba).
            int watcherGamePid = _watcherData?.ProcessId ?? 0;
            return session.GameProcessId == 0
                || (watcherGamePid != 0 && session.GameProcessId == watcherGamePid);
        }

        /// <summary>
        /// Cari ActivityWatcher (internal maupun eksternal) yang sedang memantau
        /// game aktif — dipakai tray menu/notifikasi untuk menampilkan data server.
        /// </summary>
        public ActivityWatcher? FindActiveGameWatcher()
        {
            // FIX (audit tray #3): jangan tuntut InGame — Log Tracer & Game History
            // harus tetap hidup saat user di server browser/desktop app. Prefer watcher
            // eksternal yang masih menempel; jika tidak ada, pakai watcher internal.
            if (_externalActivityWatcher is { IsDisposed: false })
                return _externalActivityWatcher;

            if (ActivityWatcher is { IsDisposed: false })
                return ActivityWatcher;

            return null;
        }

        // ── Wiring game eksternal ke tray menu (FIX audit tray) ────────────────
        // ActivityWatcher eksternal tidak bisa langsung dipakai MenuContainer karena
        // dibuat-bongkar dinamis saat game eksternal attach/detach. Watcher jadi
        // jembatan: MenuContainer & NotifyIconWrapper bertanya "siapa game yang
        // sedang aktif" lewat event/properti ini.
        public ActivityWatcher? ExternalActivityWatcher => _externalActivityWatcher;

        public sealed record ActiveGameWatcherChangedEventArgs(ActivityWatcher? Watcher, bool IsExternal);

        public event EventHandler<ActiveGameWatcherChangedEventArgs>? ActiveGameWatcherChanged;

        private void SetExternalActivityWatcher(ActivityWatcher? watcher)
        {
            _externalActivityWatcher = watcher;

            try
            {
                ActiveGameWatcherChanged?.Invoke(this, new(watcher, watcher is not null));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::SetActiveGameWatcher", $"Gagal memberi tahu tray menu (non-fatal): {ex.Message}");
            }
        }

        /// <summary>
        /// Background tray watcher for Roblox clients launched outside BoneFish.
        /// This keeps activity details available to tray features; it does not suspend
        /// processes. Any leftover legacy session is restored on exit or takeover.
        /// </summary>
        private void MonitorExternalGameSessions()
        {
            const string LOG_IDENT = "Watcher::ExternalMonitor";

            App.Logger.WriteLine(LOG_IDENT, "Tray watcher active — monitoring external Roblox clients for activity details");

            while (true)
            {
                try
                {
                    // A newer watcher will restore any remaining legacy session.
                    if (_exitEvent.WaitOne(2000))
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Watcher baru mengambil alih — menutup activity watcher eksternal");
                        CleanupExternalGame(endSession: false);
                        return;
                    }
                }
                catch
                {
                    return;
                }

                // User clicked Exit: restore any remaining legacy session before shutdown.
                if (SystemTrayExitSignal.Task.IsCompleted)
                {
                    CleanupExternalGame(endSession: true);
                    return;
                }

                if (_externalGamePid is int trackedPid)
                {
                    // FIX (audit RAM): dulu GetProcessesSafe().Any() menginstansiasi seluruh
                    // proses sistem tiap 2 detik tanpa dispose → handle leak di mode tray.
                    // Cukup satu lookup per-PID yang di-dispose.
                    bool alive;
                    try
                    {
                        using var tracked = Process.GetProcessById(trackedPid);
                        alive = !tracked.HasExited;
                    }
                    catch
                    {
                        alive = false;
                    }

                    if (alive)
                        continue;

                    App.Logger.WriteLine(LOG_IDENT, $"External Roblox client (pid={trackedPid}) exited");
                    CleanupExternalGame(endSession: true);
                    continue;
                }

                // FIX (audit RAM): dulu seluruh daftar proses diambil tiap iterasi (tiap
                // 2 detik) tanpa dispose. Cukup daftar PID (int) — jauh lebih ringan.
                HashSet<int> livePids = Utilities.GetProcessesSafe()
                    .Select(process => { try { return process.Id; } finally { process.Dispose(); } })
                    .ToHashSet();
                foreach (int retryProcessId in _externalLogRetryAfterUtc.Keys.Where(processId => !livePids.Contains(processId)).ToList())
                    _externalLogRetryAfterUtc.Remove(retryProcessId);

                int? candidate = FindUntrackedRobloxProcess();
                if (candidate is int pid)
                    AttachExternalGame(pid);
            }
        }

        /// <summary>
        /// Cari proses RobloxPlayerBeta yang TIDAK di-track: bukan game yang
        /// diluncurkan watcher ini, bukan game eksternal yang sedang dipantau,
        /// dan tidak masuk daftar ignore. Kembalikan null jika tidak ada.
        /// </summary>
        private int? FindUntrackedRobloxProcess()
        {
            const string LOG_IDENT = "Watcher::ExternalMonitor::FindProcess";

            try
            {
                foreach (var process in Utilities.GetProcessesSafe())
                {
                    // FIX (audit RAM): dispose objek Process — loop ini jalan tiap 2 detik
                    // di mode tray; tanpa dispose, handle OS menumpuk lewat finalizer queue.
                    int pid;
                    try
                    {
                        if (!String.Equals(process.ProcessName, RobloxPlayerProcessName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        pid = process.Id;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                    if (_watcherData is not null && pid == _watcherData.ProcessId)
                        continue;
                    if (_externalGamePid == pid
                        || (_externalLogRetryAfterUtc.TryGetValue(pid, out DateTime retryAfter)
                            && retryAfter > DateTime.UtcNow))
                        continue;

                    return pid;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Scan proses Roblox gagal (non-fatal): {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Temukan file log sesi yang sesuai untuk proses eksternal: file "*Player*"
        /// di %LocalAppData%\Roblox\logs yang DITULIS SETELAH proses start. Filter
        /// waktu mencegah attach ke log sesi lama (replay join entry lama → suspend
        /// tanpa alasan). Null jika tidak ada file yang cocok.
        /// </summary>
        private static string? FindRobloxLogFileForProcess(int pid)
        {
            const string LOG_IDENT = "Watcher::ExternalMonitor::FindLog";

            string logDirectory = Path.Combine(Paths.LocalAppData, "Roblox", "logs");
            if (!Directory.Exists(logDirectory))
                return null;

            DateTime? processStartUtc = null;
            try
            {
                using var process = Process.GetProcessById(pid);
                processStartUtc = process.StartTime.ToUniversalTime();
            }
            catch
            {
                // proses mati antara scan dan lookup — filter waktu tidak diterapkan
            }

            FileInfo? best = null;
            try
            {
                foreach (FileInfo file in new DirectoryInfo(logDirectory).GetFiles())
                {
                    if (!file.Name.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (processStartUtc is DateTime start && file.LastWriteTimeUtc < start.AddSeconds(-30))
                        continue;

                    if (best is null || file.LastWriteTimeUtc > best.LastWriteTimeUtc)
                        best = file;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Scan log gagal (non-fatal): {ex.Message}");
            }

            return best?.FullName;
        }

        /// <summary>
        /// Attach an ActivityWatcher to an external Roblox client's log file so tray
        /// activity features can show the current server information.
        /// </summary>
        private void AttachExternalGame(int pid)
        {
            const string LOG_IDENT = "Watcher::ExternalMonitor::Attach";

            try
            {
                string? logFile = FindRobloxLogFileForProcess(pid);
                if (logFile is null)
                {
                    // The log may appear several seconds after the client starts.
                    _externalLogRetryAfterUtc[pid] = DateTime.UtcNow.AddSeconds(5);
                    return;
                }

                _externalLogRetryAfterUtc.Remove(pid);
                _externalGamePid = pid;
                var externalWatcher = new ActivityWatcher(logFile, attachExisting: true);
                SetExternalActivityWatcher(externalWatcher);
                // Reconstruct the current game state before notifying tray consumers.
                externalWatcher.ScanExistingGameState();

                externalWatcher.Start();

                // FIX (audit tray): wire activity watcher game eksternal ke tray menu &
                // notifikasi — tanpa ini, Server Details / Invite Deeplink / riwayat game
                // / notif lokasi server tidak muncul untuk game yang diluncurkan di
                // luar BoneFish. Data sebelumnya dikumpulkan watcher eksternal tapi
                // tidak pernah sampai ke UI tray.
                _notifyIcon?.SetupExternalActivityWatcher(externalWatcher);

                App.Logger.WriteLine(LOG_IDENT, $"Game eksternal terdeteksi (pid={pid}) — activity tracking dimulai: {Path.GetFileName(logFile)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Attach game eksternal gagal (non-fatal): {ex.Message}");
                CleanupExternalGame(endSession: false);
            }
        }

        /// <summary>
        /// Lepas semua tracking game eksternal. endSession=true → sesi aktif milik
        /// proses eksternal di-End (restore) dulu; false → sesi diserahkan (takeover
        /// watcher baru) atau sudah tidak ada sesi.
        /// </summary>
        private void CleanupExternalGame(bool endSession)
        {
            const string LOG_IDENT = "Watcher::ExternalMonitor::Cleanup";

            if (endSession)
            {
                try
                {
                    if (_externalGamePid is int pid
                        && App.GameSession.Store.ReadActive() is { } session
                        && (session.GameProcessId == 0 || session.GameProcessId == pid))
                    {
                        var summary = App.GameSession.EndSession(pid);
                        if (summary.TotalSuspended > 0)
                            _notifyIcon?.ShowAlert("BoneFish", App.GameSession.FormatSummary(summary), 10, null);

                        App.Logger.WriteLine(LOG_IDENT, $"Sesi eksternal pid={pid} diakhiri ({summary.TotalSuspended} proses di-restore)");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"End session eksternal gagal (non-fatal): {ex.Message}");
                }
            }

            if (_externalActivityWatcher is not null)
            {
                _externalActivityWatcher.Dispose();
                SetExternalActivityWatcher(null);
            }

            // Lepaskan wiring tray menu dari watcher eksternal + tutup window yang
            // menampilkan datanya, sehingga UI tidak menampilkan server sesi lama.
            _notifyIcon?.TeardownExternalActivityWatcher(endSession);

            _externalGamePid = null;
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            // Last-resort sweep for a legacy session whose partial restore is still pending.
            // Do not touch a record owned by a different live Roblox watcher.
            try
            {
                if (App.GameSession.Store.ReadActive() is { RestoreState: SessionRestoreState.Pending, SuspendedProcesses.Count: > 0 }
                    && IsWatcherActiveSession())
                {
                    App.Logger.WriteLine("Watcher::Dispose", "Record Pending tersisa — merestore sebelum watcher dibuang");
                    App.GameSession.EndSession();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::Dispose", $"Sweep Pending on dispose gagal (non-fatal): {ex.Message}");
            }

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();

            Notification?.Dispose();

            FpsMonitor?.Dispose();

            Crosshair?.Dispose();

            Hotkeys?.Dispose();

            App.State.Prop.WatcherRunning = false;

            // Only remove the PID file if it still belongs to us, so we don't clobber a newer
            // watcher that has since taken over.
            try
            {
                if (File.Exists(WatcherPidFile)
                    && int.TryParse(File.ReadAllText(WatcherPidFile).Trim(), out int pid)
                    && pid == Environment.ProcessId)
                {
                    File.Delete(WatcherPidFile);
                }
            }
            catch (Exception)
            {
                // best-effort cleanup
            }

            try
            {
                _exitEvent.Dispose();
            }
            catch (Exception)
            {
                // Ignore disposal exceptions
            }

            GC.SuppressFinalize(this);
        }
    }
}
