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

        // ── FIX v7.6.3: posisi disimpan sebagai TITIK TENGAH overlay ─────────────
        // Dulu posisi disimpan sebagai Left/Top (pojok kiri atas) dan centering
        // dihitung dari ukuran window. Akibatnya:
        //   1. Constructor memanggil LoadPosition() SEBELUM ApplyCurrentSettings(),
        //      jadi centering awal dihitung dari ukuran default XAML (80px), bukan
        //      ukuran asli — window langsung tidak pas tengah.
        //   2. Saat user memperbesar ukuran crosshair, pojok kiri atas tidak bergerak
        //      sehingga TITIK TENGAH crosshair bergeser — persis keluhan "crosshair
        //      tidak jatuh di tengah layar saat ukuran diperbesar".
        // Sekarang: seluruh posisi (settings + drag) dihitung dari titik tengah, dan
        // ApplyCurrentSettings() men-anchor center pada setiap perubahan ukuran,
        // sehingga crosshair selalu tepat di tengah layar pada ukuran APA PUN.
        private double _savedCenterX;
        private double _savedCenterY;
        private bool _userMoved = false;

        // Cached shapes for dynamic redraw
        private readonly Ellipse _dotShape = new();
        private readonly Line _hLine = new();
        private readonly Line _hLineRight = new();
        private readonly Line _vLine = new();
        private readonly Line _vLineBottom = new();
        private readonly Ellipse _ringShape = new();

        public CrosshairOverlay()
        {
            InitializeComponent();

            // ★ FIX v7.6.3: urutan diperbaiki — ukuran window harus final SEBELUM
            // posisi tengah layar dihitung, bukan sebaliknya.
            ApplyCurrentSettings();
            LoadPosition();
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

            // ── FIX v7.6.3: anchor TITIK TENGAH saat ukuran berubah ─────────────
            // Center lama: posisi tengah window saat ini (kalau sudah pernah diposisikan).
            // Center lama dipertahankan → window membesar/mengecil SIMETRIS di sekitar
            // titik tengah, dan titik tengah itu sendiri tidak pernah bergeser.
            double oldCenterX = Left + Width / 2;
            double oldCenterY = Top + Height / 2;
            bool hasValidCenter = IsLoaded || _userMoved || _savedCenterX != 0 || _savedCenterY != 0;

            Width = size + 40;
            Height = size + 40;

            if (hasValidCenter)
            {
                Left = oldCenterX - Width / 2;
                Top = oldCenterY - Height / 2;
            }

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
                    ConfigureLine(_hLine, brush, thickness, centerX - size / 2, centerY, centerX - gap, centerY);
                    ConfigureLine(_hLineRight, brush, thickness, centerX + gap, centerY, centerX + size / 2, centerY);
                    ConfigureLine(_vLine, brush, thickness, centerX, centerY - size / 2, centerX, centerY - gap);
                    ConfigureLine(_vLineBottom, brush, thickness, centerX, centerY + gap, centerX, centerY + size / 2);
                    CrosshairCanvas.Children.Add(_hLine);
                    CrosshairCanvas.Children.Add(_hLineRight);
                    CrosshairCanvas.Children.Add(_vLine);
                    CrosshairCanvas.Children.Add(_vLineBottom);
                    break;

                case "Dot":
                    ConfigureEllipse(_dotShape, brush, null, 0, size * 0.3, centerX, centerY);
                    CrosshairCanvas.Children.Add(_dotShape);
                    break;

                case "Circle":
                    ConfigureEllipse(_ringShape, null, brush, thickness, size * 0.8, centerX, centerY);
                    CrosshairCanvas.Children.Add(_ringShape);
                    break;

                case "CrossDot":
                    ConfigureLine(_hLine, brush, thickness, centerX - size / 2, centerY, centerX - gap, centerY);
                    ConfigureLine(_hLineRight, brush, thickness, centerX + gap, centerY, centerX + size / 2, centerY);
                    ConfigureLine(_vLine, brush, thickness, centerX, centerY - size / 2, centerX, centerY - gap);
                    ConfigureLine(_vLineBottom, brush, thickness, centerX, centerY + gap, centerX, centerY + size / 2);
                    CrosshairCanvas.Children.Add(_hLine);
                    CrosshairCanvas.Children.Add(_hLineRight);
                    CrosshairCanvas.Children.Add(_vLine);
                    CrosshairCanvas.Children.Add(_vLineBottom);

                    ConfigureEllipse(_dotShape, brush, null, 0, size * 0.15, centerX, centerY);
                    CrosshairCanvas.Children.Add(_dotShape);
                    break;
            }
        }

        private static void ConfigureLine(Line line, Brush brush, double thickness, double x1, double y1, double x2, double y2)
        {
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
            line.Stroke = brush;
            line.StrokeThickness = thickness;
            line.StrokeStartLineCap = PenLineCap.Round;
            line.StrokeEndLineCap = PenLineCap.Round;
        }

        private static void ConfigureEllipse(Ellipse ellipse, Brush? fill, Brush? stroke, double strokeThickness, double diameter, double centerX, double centerY)
        {
            ellipse.Width = diameter;
            ellipse.Height = diameter;
            ellipse.Fill = fill;
            ellipse.Stroke = stroke;
            ellipse.StrokeThickness = strokeThickness;
            Canvas.SetLeft(ellipse, centerX - diameter / 2);
            Canvas.SetTop(ellipse, centerY - diameter / 2);
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
                // ★ FIX v7.6.3: simpan TITIK TENGAH, bukan pojok kiri atas.
                _userMoved = true;
                _savedCenterX = Left + Width / 2;
                _savedCenterY = Top + Height / 2;

                App.Settings.Prop.CrosshairX = _savedCenterX;
                App.Settings.Prop.CrosshairY = _savedCenterY;
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
                double screenCenterX = SystemParameters.PrimaryScreenWidth / 2;
                double screenCenterY = SystemParameters.PrimaryScreenHeight / 2;

                // ★ FIX v7.6.3: CrosshairX/Y sekarang menyimpan TITIK TENGAH overlay.
                // Nilai (0,0) berarti belum pernah diposisikan → tengah layar.
                // (Data lama dari versi sebelumnya menyimpan pojok window; untuk
                // posisi default (0,0) hasilnya identik — tepat di tengah layar.)
                _savedCenterX = App.Settings.Prop.CrosshairX;
                _savedCenterY = App.Settings.Prop.CrosshairY;
                _userMoved = _savedCenterX != 0 || _savedCenterY != 0;

                if (!_userMoved)
                {
                    _savedCenterX = screenCenterX;
                    _savedCenterY = screenCenterY;
                }

                Left = _savedCenterX - Width / 2;
                Top = _savedCenterY - Height / 2;
            }
            catch
            {
                _savedCenterX = SystemParameters.PrimaryScreenWidth / 2;
                _savedCenterY = SystemParameters.PrimaryScreenHeight / 2;
                _userMoved = false;

                Left = _savedCenterX - Width / 2;
                Top = _savedCenterY - Height / 2;
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
