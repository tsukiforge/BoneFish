using System;
using System.Threading;
using System.Windows.Threading;
using Bloxstrap.UI.Elements;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk menampilkan Crosshair overlay di layar.
    /// Berjalan di dispatcher thread terpisah (seperti FpsMonitorService)
    /// sehingga tidak memblokir UI utama.
    ///
    /// Crosshair bersifat independen dari ActivityWatcher — tidak perlu
    /// menunggu game join untuk muncul. Overlay bisa di-toggle kapan saja
    /// dari Settings atau hotkey.
    /// </summary>
    public class CrosshairService : IDisposable
    {
        private const string LOG_IDENT = "CrosshairService";

        /// <summary>
        /// Static singleton instance agar bisa diakses dari ViewModel
        /// tanpa perlu referensi ke Watcher.
        /// </summary>
        public static CrosshairService? Instance { get; private set; }

        private CrosshairOverlay? _overlay;
        private Dispatcher? _dispatcher;
        private Thread? _thread;
        private bool _isRunning = false;

        public CrosshairService()
        {
            // Set Instance di constructor, jadi hotkey bisa akses meski Start() belum dipanggil.
            // Thread + dispatcher cuma dibuat pas Start() — lazy initialization.
            Instance = this;
        }

        public void Start()
        {
            if (_isRunning)
                return;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Starting Crosshair Service");
                _isRunning = true;

                _thread = new Thread(() =>
                {
                    try
                    {
                        _dispatcher = Dispatcher.CurrentDispatcher;
                        _overlay = new CrosshairOverlay();

                        if (App.Settings.Prop.EnableCrosshair)
                        {
                            _overlay.Show();
                            App.Logger.WriteLine(LOG_IDENT, "Crosshair overlay shown");
                        }

                        Dispatcher.Run();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Error in Crosshair thread: {ex.Message}");
                    }
                })
                {
                    IsBackground = false,
                    Name = "CrosshairThread"
                };

                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error starting Crosshair: {ex.Message}");
                _isRunning = false;
            }
        }

        public void Toggle()
        {
            if (_dispatcher != null && _overlay != null)
            {
                _dispatcher.Invoke(() =>
                {
                    _overlay.ToggleVisibility();

                    bool visible = _overlay.Visibility == System.Windows.Visibility.Visible;
                    App.Settings.Prop.EnableCrosshair = visible;

                    try { App.Settings.Save(); } catch { }

                    App.Logger.WriteLine(LOG_IDENT, $"Crosshair toggled: {(visible ? "ON" : "OFF")}");
                });
            }
            else if (_overlay == null && !_isRunning)
            {
                // Service not started yet — start with crosshair enabled
                App.Settings.Prop.EnableCrosshair = true;
                try { App.Settings.Save(); } catch { }
                Start();
            }
        }

        public void ApplySettings()
        {
            if (_dispatcher != null && _overlay != null)
            {
                _dispatcher.Invoke(() =>
                {
                    if (App.Settings.Prop.EnableCrosshair)
                    {
                        if (!_overlay.IsLoaded)
                            _overlay.Show();
                        else if (_overlay.Visibility != System.Windows.Visibility.Visible)
                            _overlay.Visibility = System.Windows.Visibility.Visible;

                        _overlay.ApplyCurrentSettings();
                    }
                    else
                    {
                        if (_overlay.IsLoaded && _overlay.IsVisible)
                            _overlay.Hide();
                    }
                });
            }
            else if (App.Settings.Prop.EnableCrosshair && !_isRunning)
            {
                Start();
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            Instance = null;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Stopping Crosshair Service");

                _isRunning = false;

                if (_dispatcher != null && _overlay != null)
                {
                    _dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (_overlay.IsLoaded)
                                _overlay.Close();
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error stopping Crosshair: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (_dispatcher != null)
                    _dispatcher.InvokeShutdown();

                if (_thread != null && _thread.IsAlive)
                    _thread.Join(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error disposing: {ex.Message}");
            }

            _overlay = null;
            _dispatcher = null;
            _thread = null;

            App.Logger.WriteLine(LOG_IDENT, "Crosshair Service disposed");
        }
    }
}
