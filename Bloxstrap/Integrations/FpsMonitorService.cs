using System;
using System.Windows;
using System.Threading;
using System.Windows.Threading;
using Bloxstrap.UI.Elements;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk menampilkan FPS Monitor overlay saat game berjalan
    /// FPS Monitor tetap aktif bahkan setelah user keluar dari game atau menutup BoneFish
    /// Menggunakan separate dispatcher untuk persistence
    /// </summary>
    public class FpsMonitorService : IDisposable
    {
        private const string LOG_IDENT = "FpsMonitorService";

        private readonly ActivityWatcher _activityWatcher;
        private FpsMonitorOverlay? _fpsOverlay;
        private bool _isRunning = false;
        private bool _persistentMode = false;
        private Dispatcher? _fpsDispatcher;
        private Thread? _fpsThread;

        public FpsMonitorService(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;

            _activityWatcher.OnGameJoin += (_, _) => StartMonitoring();
            _activityWatcher.OnGameLeave += (_, _) => ContinueMonitoring();
        }

        public void StartMonitoring()
        {
            if (App.Settings.Prop.EnableFpsMonitor == false || _isRunning)
                return;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Starting FPS Monitor");

                _isRunning = true;
                _persistentMode = true;

                if (_fpsOverlay == null || _fpsDispatcher == null)
                {
                    CreateFpsWindowOnSeparateThread();
                }
                else if (_fpsDispatcher != null)
                {
                    _fpsDispatcher.Invoke(() =>
                    {
                        if (_fpsOverlay != null && !_fpsOverlay.IsLoaded)
                        {
                            _fpsOverlay.Show();
                            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor overlay shown");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error starting FPS Monitor: {ex.Message}");
                _isRunning = false;
            }
        }

        private void CreateFpsWindowOnSeparateThread()
        {
            _fpsThread = new Thread(() =>
            {
                try
                {
                    _fpsDispatcher = Dispatcher.CurrentDispatcher;
                    _fpsOverlay = new FpsMonitorOverlay();
                    _fpsOverlay.Show();
                    App.Logger.WriteLine(LOG_IDENT, "FPS Monitor overlay created on separate dispatcher");
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Error in FPS thread: {ex.Message}");
                }
            })
            {
                IsBackground = false,
                Name = "FpsMonitorThread"
            };

            _fpsThread.SetApartmentState(ApartmentState.STA);
            _fpsThread.Start();
        }

        public void ContinueMonitoring()
        {
            // FPS Monitor tetap aktif - jangan di-stop
            if (_persistentMode && _isRunning && _fpsDispatcher != null)
            {
                try
                {
                    App.Logger.WriteLine(LOG_IDENT, "Game left but FPS Monitor continues (persistent mode enabled)");
                    
                    _fpsDispatcher.Invoke(() =>
                    {
                        if (_fpsOverlay != null && _fpsOverlay.IsLoaded)
                        {
                            _fpsOverlay.SetPersistentMode(true);
                            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor switched to persistent mode");
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Error continuing FPS Monitor: {ex.Message}");
                }
            }
        }

        public void StopMonitoring()
        {
            if (!_isRunning)
                return;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Stopping FPS Monitor");

                _isRunning = false;
                _persistentMode = false;

                if (_fpsDispatcher != null && _fpsOverlay != null)
                {
                    _fpsDispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (_fpsOverlay.IsLoaded)
                            {
                                _fpsOverlay.Close();
                            }
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error stopping FPS Monitor: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (_fpsDispatcher != null)
                {
                    _fpsDispatcher.InvokeShutdown();
                }

                if (_fpsThread != null && _fpsThread.IsAlive)
                {
                    _fpsThread.Join(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error disposing: {ex.Message}");
            }

            _fpsOverlay = null;
            _fpsDispatcher = null;
            _fpsThread = null;

            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor service disposed");
        }
    }
}
