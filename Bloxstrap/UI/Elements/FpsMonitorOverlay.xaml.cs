using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.Elements
{
    public partial class FpsMonitorOverlay : Window
    {
        private const string LOG_IDENT = "FpsMonitorOverlay";

        private readonly RealFpsCounter _fpsCounter;

        private System.Windows.Threading.DispatcherTimer? _fpsUpdateTimer;

        private Point _lastMousePos;
        private bool _isDragging = false;
        private bool _persistentMode = false;
        private bool _vulkanWarned = false;
        private int _ticksSinceStart = 0;

        private readonly SolidColorBrush _greenBrush = new(Color.FromRgb(0, 255, 0));
        private readonly SolidColorBrush _goldBrush = new(Color.FromRgb(255, 215, 0));
        private readonly SolidColorBrush _redBrush = new(Color.FromRgb(255, 0, 0));
        private readonly SolidColorBrush _purpleBrush = new(Color.FromRgb(200, 100, 255));

        public FpsMonitorOverlay(RealFpsCounter fpsCounter)
        {
            _fpsCounter = fpsCounter;

            InitializeComponent();
            InitializeFpsMonitoring();
            LoadPosition();

            StatusText.Text = "Online";
        }

        private void InitializeFpsMonitoring()
        {
            // The real FPS source (ETW) is sampled on a fixed interval.
            // Use a slower interval on low-end systems to save CPU.
            int updateMs = App.Settings.Prop.OptimizeForLowEnd ? 2000 : 1000;

            _fpsUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(updateMs)
            };
            _fpsUpdateTimer.Tick += (_, _) => UpdateFpsDisplay();
            _fpsUpdateTimer.Start();

            // Reduce overlay opacity for very low-end systems to save rendering
            if (App.Settings.Prop.OptimizeForLowEnd)
                this.Opacity = 0.7;

            App.Logger.WriteLine(LOG_IDENT, $"FPS Monitor initialized (real ETW source, update interval={updateMs}ms)");
        }

        private void UpdateFpsDisplay()
        {
            _ticksSinceStart++;

            double fps = _fpsCounter.SampleFps();
            double frameTime = fps > 0 ? 1000.0 / fps : 0;

            // If after a few seconds in-game we've never seen a DXGI Present event,
            // the client is rendering through Vulkan (no DXGI events). The custom
            // overlay can't read FPS in that case, so inform the user to use the
            // built-in Roblox HUD (Shift+F5) instead of showing a misleading 0.
            if (!_fpsCounter.HasObservedFrames)
            {
                if (_ticksSinceStart >= 4 && !_vulkanWarned)
                {
                    _vulkanWarned = true;
                    App.Logger.WriteLine(LOG_IDENT, "No DXGI frames observed; likely Vulkan rendering mode");

                    FpsValueText.Text = "N/A";
                    FpsValueText.Foreground = _purpleBrush;
                    FrameTimeText.Text = "Vulkan";
                    StatusText.Text = "Use Shift+F5";
                }

                if (_vulkanWarned)
                    return;
            }
            else if (_vulkanWarned)
            {
                // frames started coming in after all; resume normal display
                _vulkanWarned = false;
                StatusText.Text = _persistentMode ? "Persistent" : "Online";
            }

            FpsValueText.Text = $"{(int)Math.Round(fps)} FPS";
            FrameTimeText.Text = $"{frameTime:F1} ms";

            // Color coding for FPS using preallocated brushes
            if (fps >= 60)
                FpsValueText.Foreground = _greenBrush;
            else if (fps >= 30)
                FpsValueText.Foreground = _goldBrush;
            else if (_persistentMode)
                FpsValueText.Foreground = _purpleBrush; // Purple untuk persistent mode
            else
                FpsValueText.Foreground = _redBrush;
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

            App.Logger.WriteLine(LOG_IDENT, "FPS Monitor closed");
            base.OnClosed(e);
        }
    }
}
