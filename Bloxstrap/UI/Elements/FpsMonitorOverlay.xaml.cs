using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Media;

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
        
        private Point _lastMousePos;
        private bool _isDragging = false;

        private readonly SolidColorBrush _greenBrush = new(Color.FromRgb(0, 255, 0));
        private readonly SolidColorBrush _goldBrush = new(Color.FromRgb(255, 215, 0));
        private readonly SolidColorBrush _redBrush = new(Color.FromRgb(255, 0, 0));

        public FpsMonitorOverlay()
        {
            InitializeComponent();
            InitializeFpsMonitoring();
            LoadPosition();
        }

        private void InitializeFpsMonitoring()
        {
            // Determine update interval based on low-end optimization
            int updateMs = App.Settings.Prop.OptimizeForLowEnd ? 2000 : 1000;

            // FPS Update Timer - update UI at configurable interval
            _fpsUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(updateMs)
            };
            _fpsUpdateTimer.Tick += (_, _) => UpdateFpsDisplay();
            _fpsUpdateTimer.Start();

            // Use CompositionTarget.Rendering for accurate frame counting (lightweight)
            CompositionTarget.Rendering += OnRendering;

            _frameTimer.Start();

            App.Logger.WriteLine(LOG_IDENT, $"FPS Monitor initialized (update interval={updateMs}ms)");
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            // Called once per WPF render; cheap to increment
            _frameCount++;
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
            StatusText.Text = "Drag to move";
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            StatusText.Text = "Online";
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

        protected override void OnClosed(EventArgs e)
        {
            _fpsUpdateTimer?.Stop();
            CompositionTarget.Rendering -= OnRendering;
            _frameTimer?.Stop();
            
            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor closed");
            base.OnClosed(e);
        }
    }
}
