using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

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

        // ── Posisi disimpan sebagai TITIK TENGAH overlay (fix v7.6.3) ─────────────
        // Ganti ukuran → window membesar/mengecil SIMETRIS di sekitar titik tengah;
        // titik tengah tidak pernah bergeser (keluhan "crosshair geser saat size
        // diperbesar"). Constructor: ApplyCurrentSettings() dulu (ukuran final),
        // baru LoadPosition() (centering dihitung dari ukuran asli).
        private double _savedCenterX;
        private double _savedCenterY;
        private bool _userMoved = false;

        // ── FIX v7.7.0: Lock to Roblox client-area center ──────────────────────────
        // Crosshair FPS harus jatuh di TENGAH CLIENT AREA Roblox (bukan tengah
        // monitor utama — salah saat Roblox windowed/multi-monitor/DPI beda).
        // Alur: RobloxWindow → GetClientRect + ClientToScreen (koordinat fisik px)
        // → pusat → konversi px→DIP pakai skala monitor overlay → Left/Top.
        // DispatcherTimer 150ms menangani: Roblox resize, maximize, pindah monitor.
        // Drag manual = user intent → lock dimatikan otomatis; toggle settings
        // untuk mengunci lagi. Posisi custom lama tidak pernah ditimpa saat locked
        // (SavePosition tidak dipanggil dalam mode lock).
        private DispatcherTimer? _lockTimer;

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

            // Urutan penting: ukuran window final dulu, baru posisi dihitung.
            ApplyCurrentSettings();
            LoadPosition();
            UpdateVisibility();
            UpdateLockTimer();
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

            // Anchor TITIK TENGAH saat ukuran berubah — pusat tidak pernah bergeser.
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

            UpdateLockTimer();
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

        // ── Lock to Roblox center ─────────────────────────────────────────────────

        private void UpdateLockTimer()
        {
            bool shouldRun = App.Settings.Prop.EnableCrosshair && App.Settings.Prop.CrosshairLockToRoblox;

            if (shouldRun && _lockTimer is null)
            {
                _lockTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                _lockTimer.Tick += (_, _) => LockToRobloxCenter();
                _lockTimer.Start();
                App.Logger.WriteLine(LOG_IDENT, "Lock-to-Roblox timer started");
            }
            else if (!shouldRun && _lockTimer is not null)
            {
                _lockTimer.Stop();
                _lockTimer = null;
                App.Logger.WriteLine(LOG_IDENT, "Lock-to-Roblox timer stopped");
            }
        }

        private void LockToRobloxCenter()
        {
            if (Visibility != Visibility.Visible)
                return;

            IntPtr hwnd = FindVisibleRobloxWindow();
            if (hwnd == IntPtr.Zero)
                return; // Roblox tidak jalan — crosshair diam di posisi terakhir

            if (!GetClientRect(hwnd, out RECT clientRect))
                return;

            var clientOrigin = new POINT(0, 0);
            if (!ClientToScreen(hwnd, ref clientOrigin))
                return;

            // Pusat client area dalam KOORDINAT FISIK (px layar).
            double physCenterX = clientOrigin.X + clientRect.Right / 2.0;
            double physCenterY = clientOrigin.Y + clientRect.Bottom / 2.0;

            // Konversi px fisik → DIP WPF pakai skala monitor tempat overlay berada.
            // Overlay selalu dipindah mengikuti Roblox, jadi skala monitor-nya
            // = skala monitor Roblox (menangani DPI 100/125/150% + multi-monitor).
            double dpiX = 1.0, dpiY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is not null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            if (dpiX <= 0) dpiX = 1.0;
            if (dpiY <= 0) dpiY = 1.0;

            double centerDipX = physCenterX / dpiX;
            double centerDipY = physCenterY / dpiY;

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;

            // Pindah TANPA SavePosition — posisi custom user tidak pernah ditimpa
            // selama lock aktif; mematikan lock mengembalikan posisi tersimpan.
            Left = centerDipX - width / 2;
            Top = centerDipY - height / 2;
        }

        private static IntPtr FindVisibleRobloxWindow()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    using Process p = process;
                    try
                    {
                        IntPtr hwnd = p.MainWindowHandle;
                        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                            return hwnd;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"FindVisibleRobloxWindow failed: {ex.Message}");
            }
            return IntPtr.Zero;
        }

        private static bool IsLockEnabled => App.Settings.Prop.CrosshairLockToRoblox;

        private void DisableLock()
        {
            if (!IsLockEnabled)
                return;

            App.Settings.Prop.CrosshairLockToRoblox = false;
            try { App.Settings.Save(); } catch { }
            UpdateLockTimer();
            App.Logger.WriteLine(LOG_IDENT, "Lock-to-Roblox disabled (user dragged the crosshair)");
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

            UpdateLockTimer();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // Drag = posisi manual menang: matikan lock supaya timer tidak
                // bertarung dengan drag. Toggle settings untuk mengunci lagi.
                DisableLock();

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
                // Simpan TITIK TENGAH, bukan pojok kiri atas.
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

                // CrosshairX/Y menyimpan TITIK TENGAH overlay.
                // Nilai (0,0) berarti belum pernah diposisikan → tengah layar.
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

            UpdateLockTimer();
        }

        protected override void OnClosed(EventArgs e)
        {
            _lockTimer?.Stop();
            _lockTimer = null;
            App.Logger.WriteLine(LOG_IDENT, "Crosshair overlay closed");
            base.OnClosed(e);
        }

        // ── P/Invoke: Roblox client rect ──────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);
    }
}
