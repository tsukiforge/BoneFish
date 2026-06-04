using Bloxstrap.UI.Elements;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk menampilkan FPS Monitor overlay saat game berjalan
    /// </summary>
    public class FpsMonitorService : IDisposable
    {
        private const string LOG_IDENT = "FpsMonitorService";

        private readonly ActivityWatcher _activityWatcher;
        private FpsMonitorOverlay? _fpsOverlay;
        private bool _isRunning = false;

        public FpsMonitorService(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;

            _activityWatcher.OnGameJoin += (_, _) => StartMonitoring();
            _activityWatcher.OnGameLeave += (_, _) => StopMonitoring();
        }

        public void StartMonitoring()
        {
            if (App.Settings.Prop.EnableFpsMonitor == false || _isRunning)
                return;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Starting FPS Monitor");

                _isRunning = true;

                // Create dan show FPS overlay di main thread
                if (Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_fpsOverlay == null || !_fpsOverlay.IsLoaded)
                        {
                            _fpsOverlay = new FpsMonitorOverlay();
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

        public void StopMonitoring()
        {
            if (!_isRunning)
                return;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Stopping FPS Monitor");

                _isRunning = false;

                if (Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_fpsOverlay != null && _fpsOverlay.IsLoaded)
                        {
                            _fpsOverlay.Close();
                            _fpsOverlay = null;
                            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor overlay closed");
                        }
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
            StopMonitoring();
            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor service disposed");
        }
    }
}
