using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloxstrap.UI.Elements
{
    public enum CrosshairStyle
    {
        Dot,
        Cross,
        Circle,
        CrossDot
    }

    public partial class CrosshairOverlay : Window
    {
        private const string LOG_IDENT = "CrosshairOverlay";

        private Point _lastMousePos;
        private bool _isDragging = false;

        // Cached shapes for dynamic redraw
        private readonly Ellipse _dotShape = new();
        private readonly Line _hLine = new();
        private readonly Line _vLine = new();
        private readonly Ellipse _ringShape = new();

        public CrosshairOverlay()
        {
            InitializeComponent();
            LoadPosition();
            ApplyCurrentSettings();
            UpdateVisibility();
        }

        public void ApplyCurrentSettings()
        {
            var settings = App.Settings.Prop;

            // Parse color
            Color color = Colors.Lime;
            try
            {
                if (!string.IsNullOrEmpty(settings.CrosshairColor))
                    color = (Color)ColorConverter.ConvertFromString(settings.CrosshairColor);
            }
            catch { }

            double size = Math.Clamp(settings.CrosshairSize, 20, 200);
            double thickness = Math.Max(2, size / 20);
            double gap = size * 0.25;
            double opacity = Math.Clamp(settings.CrosshairOpacity, 0.1, 1.0);

            Width = size + 40;
            Height = size + 40;
            CrosshairCanvas.Width = Width;
            CrosshairCanvas.Height = Height;
            this.Opacity = opacity;

            CrosshairCanvas.Children.Clear();

            var brush = new SolidColorBrush(color);
            double centerX = Width / 2;
            double centerY = Height / 2;

            switch (settings.CrosshairStyle)
            {
                case "Cross":
                default:
                    // Horizontal line (left segment)
                    _hLine.X1 = centerX - size / 2;
                    _hLine.Y1 = centerY;
                    _hLine.X2 = centerX - gap;
                    _hLine.Y2 = centerY;
                    _hLine.Stroke = brush;
                    _hLine.StrokeThickness = thickness;
                    _hLine.StrokeStartLineCap = PenLineCap.Round;
                    _hLine.StrokeEndLineCap = PenLineCap.Round;
                    CrosshairCanvas.Children.Add(_hLine);

                    // Horizontal line (right segment)
                    var hLineRight = new Line
                    {
                        X1 = centerX + gap,
                        Y1 = centerY,
                        X2 = centerX + size / 2,
                        Y2 = centerY,
                        Stroke = brush,
                        StrokeThickness = thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    CrosshairCanvas.Children.Add(hLineRight);

                    // Vertical line (top segment)
                    _vLine.X1 = centerX;
                    _vLine.Y1 = centerY - size / 2;
                    _vLine.X2 = centerX;
                    _vLine.Y2 = centerY - gap;
                    _vLine.Stroke = brush;
                    _vLine.StrokeThickness = thickness;
                    _vLine.StrokeStartLineCap = PenLineCap.Round;
                    _vLine.StrokeEndLineCap = PenLineCap.Round;
                    CrosshairCanvas.Children.Add(_vLine);

                    // Vertical line (bottom segment)
                    var vLineBottom = new Line
                    {
                        X1 = centerX,
                        Y1 = centerY + gap,
                        X2 = centerX,
                        Y2 = centerY + size / 2,
                        Stroke = brush,
                        StrokeThickness = thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    CrosshairCanvas.Children.Add(vLineBottom);
                    break;

                case "Dot":
                    _dotShape.Width = size * 0.3;
                    _dotShape.Height = size * 0.3;
                    _dotShape.Fill = brush;
                    CrosshairCanvas.Children.Add(_dotShape);
                    Canvas.SetLeft(_dotShape, centerX - _dotShape.Width / 2);
                    Canvas.SetTop(_dotShape, centerY - _dotShape.Height / 2);
                    break;

                case "Circle":
                    _ringShape.Width = size * 0.8;
                    _ringShape.Height = size * 0.8;
                    _ringShape.Stroke = brush;
                    _ringShape.StrokeThickness = thickness;
                    CrosshairCanvas.Children.Add(_ringShape);
                    Canvas.SetLeft(_ringShape, centerX - _ringShape.Width / 2);
                    Canvas.SetTop(_ringShape, centerY - _ringShape.Height / 2);
                    break;

                case "CrossDot":
                    // Cross lines
                    var cdHLeft = new Line { X1 = centerX - size / 2, Y1 = centerY, X2 = centerX - gap, Y2 = centerY, Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    var cdHRight = new Line { X1 = centerX + gap, Y1 = centerY, X2 = centerX + size / 2, Y2 = centerY, Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    var cdVTop = new Line { X1 = centerX, Y1 = centerY - size / 2, X2 = centerX, Y2 = centerY - gap, Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    var cdVBottom = new Line { X1 = centerX, Y1 = centerY + gap, X2 = centerX, Y2 = centerY + size / 2, Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    CrosshairCanvas.Children.Add(cdHLeft);
                    CrosshairCanvas.Children.Add(cdHRight);
                    CrosshairCanvas.Children.Add(cdVTop);
                    CrosshairCanvas.Children.Add(cdVBottom);

                    // Center dot
                    _dotShape.Width = size * 0.15;
                    _dotShape.Height = size * 0.15;
                    _dotShape.Fill = brush;
                    CrosshairCanvas.Children.Add(_dotShape);
                    Canvas.SetLeft(_dotShape, centerX - _dotShape.Width / 2);
                    Canvas.SetTop(_dotShape, centerY - _dotShape.Height / 2);
                    break;
            }
        }

        private void UpdateVisibility()
        {
            if (App.Settings.Prop.EnableCrosshair)
            {
                if (!IsLoaded)
                    Show();
                else if (!IsVisible)
                    Visibility = Visibility.Visible;
            }
            else
            {
                if (IsLoaded && IsVisible)
                    Visibility = Visibility.Collapsed;
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
                Left += currentPos.X - _lastMousePos.X;
                Top += currentPos.Y - _lastMousePos.Y;
                _lastMousePos = currentPos;
                SavePosition();
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void SavePosition()
        {
            try
            {
                App.Settings.Prop.CrosshairX = Left;
                App.Settings.Prop.CrosshairY = Top;
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
                // Default: center of screen
                if (App.Settings.Prop.CrosshairX == 0 && App.Settings.Prop.CrosshairY == 0)
                {
                    Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                    Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
                }
                else
                {
                    Left = App.Settings.Prop.CrosshairX;
                    Top = App.Settings.Prop.CrosshairY;
                }
            }
            catch
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
            }
        }

        public void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
                Visibility = Visibility.Collapsed;
            else
                Visibility = Visibility.Visible;
        }

        protected override void OnClosed(EventArgs e)
        {
            App.Logger.WriteLine(LOG_IDENT, "Crosshair overlay closed");
            base.OnClosed(e);
        }
    }
}
