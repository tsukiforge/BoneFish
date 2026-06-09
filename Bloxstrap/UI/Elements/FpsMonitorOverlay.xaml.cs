using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Media;
using System.Threading;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.Elements
{
    public partial class FpsMonitorOverlay : Window
    {
        private const string LOG_IDENT = "FpsMonitorOverlay";
        
        private Stopwatch _frameTimer = new();
        private int _frameCount = 0;
        private double _currentFps = 0;
        private double _frameTime = 0;
        
        private System.Windows.Threading.DispatcherTimer? _fpsUpdateTimer;
        private System.Windows.Threading.DispatcherTimer? _frameCounterTimer;
        
        private Point _lastMousePos;
        private bool _isDragging = false;
        private bool _persistentMode = false;

        private readonly SolidColorBrush _greenBrush = new(Color.FromRgb(0, 255, 0));
        private readonly SolidColorBrush _goldBrush = new(Color.FromRgb(255, 215, 0));
        private readonly SolidColorBrush _redBrush = new(Color.FromRgb(255, 0, 0));
        private readonly SolidColorBrush _purpleBrush = new(Color.FromRgb(200, 100, 255));

        public FpsMonitorOverlay()
        {
            InitializeComponent();
            InitializeFpsMonitoring();
            LoadPosition();
        }

        private void InitializeFpsMonitoring()
        {
            // Determine update interval based on system tier
            int updateMs = DetermineFpsUpdateInterval();

            // FPS Update Timer - update UI at configurable interval
            _fpsUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(updateMs)
            };
            _fpsUpdateTimer.Tick += (_, _) => UpdateFpsDisplay();
            _fpsUpdateTimer.Start();

            // Frame counter timer - increment frame count every 16ms (~60fps)
            _frameCounterTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _frameCounterTimer.Tick += (_, _) => _frameCount++;
            _frameCounterTimer.Start();

            // Reduce overlay visibility for very low-end systems
            if (App.Settings.Prop.OptimizeForLowEnd)
            {
                this.Opacity = 0.7; // Reduce opacity to save rendering
            }

            _frameTimer.Start();

            App.Logger.WriteLine(LOG_IDENT, $"FPS Monitor initialized (update interval={updateMs}ms, tier={GetSystemTier()})");
        }

        private int DetermineFpsUpdateInterval()
        {
            // Ultra-aggressive for ultra-low-end: only update every 3 seconds
            if (App.Settings.Prop.OptimizeForLowEnd)
            {
                var systemInfo = AutoOptimizeService.GetSystemInfo();
                if (systemInfo.Contains("1") || systemInfo.Contains("2GB")) // 1-2GB RAM
                    return 3000; // Update every 3 seconds
                
                return 2000; // Update every 2 seconds for regular low-end
            }

            return 1000; // Update every 1 second for normal systems
        }

        private string GetSystemTier()
        {
            // Call static system info method
            return AutoOptimizeService.GetSystemInfo();
        }

        private void UpdateFpsDisplay()
        {
            if (_frameTimer.Elapsed.TotalSeconds > 0)
            {
                _currentFps = _frameCount / _frameTimer.Elapsed.TotalSeconds;
                if (_currentFps <= 0) _currentFps = 0.0001; // prevent div by zero
                _frameTime = 1000.0 / _currentFps;

                // Update UI (on UI thread)
                FpsValueText.Text = $"{(int)_currentFps} FPS";
                FrameTimeText.Text = $"{_frameTime:F1} ms";

                // Color coding for FPS using preallocated brushes
                if (_currentFps >= 60)
                    FpsValueText.Foreground = _greenBrush;
                else if (_currentFps >= 30)
                    FpsValueText.Foreground = _goldBrush;
                else if (_persistentMode)
                    FpsValueText.Foreground = _purpleBrush; // Purple untuk persistent mode
                else
                    FpsValueText.Foreground = _redBrush;

                _frameCount = 0;
                _frameTimer.Restart();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _lastMousePos = PointToScreen(e.GetPosition(this));
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPos = PointToScreen(e.GetPosition(this));
                double offsetX = currentPos.X - _lastMousePos.X;
                double offsetY = currentPos.Y - _lastMousePos.Y;

                Left += offsetX;
                Top += offsetY;

                _lastMousePos = currentPos;
                SavePosition();
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            StatusText.Text = _persistentMode ? "Persistent | Drag to move" : "Drag to move";
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            StatusText.Text = _persistentMode ? "Persistent" : "Online";
        }

        private void SavePosition()
        {
            try
            {
                App.Settings.Prop.FpsMonitorX = Left;
                App.Settings.Prop.FpsMonitorY = Top;
                App.Settings.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error saving position: {ex.Message}");
            }
        }

        private void LoadPosition()
        {
            try
            {
                // If saved position is 0 (default), place top-right
                if (App.Settings.Prop.FpsMonitorX == 0 && App.Settings.Prop.FpsMonitorY == 0)
                {
                    Left = SystemParameters.PrimaryScreenWidth - 200;
                    Top = 20;
                }
                else
                {
                    Left = App.Settings.Prop.FpsMonitorX;
                    Top = App.Settings.Prop.FpsMonitorY;
                }
            }
            catch
            {
                // Default position - top right corner
                Left = SystemParameters.PrimaryScreenWidth - 200;
                Top = 20;
            }
        }

        public void SetPersistentMode(bool persistent)
        {
            _persistentMode = persistent;
            if (persistent)
            {
                App.Logger.WriteLine(LOG_IDENT, "FPS Monitor set to persistent mode - will continue running after game exit");
                StatusText.Text = "Persistent";
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "FPS Monitor set to normal mode");
                StatusText.Text = "Online";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _fpsUpdateTimer?.Stop();
            _frameCounterTimer?.Stop();
            _frameTimer?.Stop();
            
            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor closed");
            base.OnClosed(e);
        }
    }
}
