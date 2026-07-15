using Bloxstrap.AppData;
using Bloxstrap.Integrations;

namespace Bloxstrap
{
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");

        private readonly System.Threading.EventWaitHandle _exitEvent = new(false, System.Threading.EventResetMode.AutoReset, "BoneFish-WatcherExitEvent");

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly WindowManipulation? WindowManipulation;

        public readonly DiscordRichPresence? RichPresence;

        public readonly RobloxNotification? Notification;

        public readonly FpsMonitorService? FpsMonitor;

        public readonly CrosshairService? Crosshair;

        public readonly HotkeyService? Hotkeys;

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

            // Only start activity tracking if we actually have a valid log file.
            // The tray icon below does not depend on this, so it will still appear
            // even when Roblox self-updated and the log file couldn't be identified.
            if (App.Settings.Prop.EnableActivityTracking && !String.IsNullOrEmpty(_watcherData.LogFile) && File.Exists(_watcherData.LogFile))
            {
                ActivityWatcher = new(_watcherData.LogFile);

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence && !App.State.Prop.WatcherRunning)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Running rpc");
                    RichPresence = new(ActivityWatcher);
                }

                if (App.Settings.Prop.EnableRobloxNotifications)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Initializing Roblox notifications");
                    Notification = new(ActivityWatcher);
                }

                if (App.Settings.Prop.EnableFpsMonitor)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Initializing FPS Monitor");
                    FpsMonitor = new(ActivityWatcher, _watcherData.ProcessId);
                }

            }
            else if (App.Settings.Prop.EnableActivityTracking)
            {
                // Activity tracking is enabled but the log file is missing (e.g. Roblox self-updated).
                // The tray icon will still be created below; tracking-dependent features are skipped this session.
                App.Logger.WriteLine(LOG_IDENT, "Activity tracking enabled but log file is unavailable; skipping tracking, tray icon will still appear");
            }

            // Crosshair & Hotkeys adalah fitur INDEPENDEN — tidak butuh ActivityTracking.
            // Inisialisasi di sini (di luar blok EnableActivityTracking) agar tetap jalan
            // meski user matiin tracking atau log file Roblox gak ketemu.
            App.Logger.WriteLine(LOG_IDENT, "Initializing Crosshair Service");
            Crosshair = new CrosshairService();
            Crosshair.Start();

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

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();
            WindowManipulation?.ApplyWindowModifications();

            if (App.Settings.Prop.FakeBorderlessFullscreen)
                WindowManipulation?.FakeBorderless();

            while (Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
                await Task.Delay(1000);

            // Jika system tray mode aktif, jangan langsung exit
            if (App.Settings.Prop.EnableSystemTrayOnClose)
            {
                _notifyIcon?.ShowAlert("BoneFish", "Roblox telah ditutup. BoneFish masih berjalan di system tray.", 5, null);
                // Menunggu sampai user klik Exit dari context menu ATAU ada watcher baru yang menyuruh kita exit
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

                await Task.WhenAny(exitSignalTask, externalExitTask);
            }

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

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
