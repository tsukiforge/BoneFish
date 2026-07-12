using System;
using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk global hotkeys menggunakan Win32 RegisterHotKey API.
    /// Hotkeys bekerja meski BoneFish di-minimize ke system tray.
    ///
    /// Hotkeys yang didaftarkan:
    ///   Ctrl+Shift+C → Toggle Crosshair overlay
    ///   Ctrl+Shift+F → Toggle FPS Monitor
    ///   Ctrl+Shift+N → Toggle Night Vision
    /// </summary>
    public class HotkeyService : IDisposable
    {
        private const string LOG_IDENT = "HotkeyService";

        // Unique IDs untuk setiap hotkey (harus unik per proses)
        private const int HOTKEY_CROSSHAIR = 9001;
        private const int HOTKEY_FPS = 9002;
        private const int HOTKEY_NIGHTVISION = 9003;

        private HwndSource? _hwndSource;
        private bool _registered = false;
        private static HWND _hotkeyHwnd = HWND.Null;

        /// <summary>
        /// Static singleton instance agar bisa diakses dari ViewModel
        /// (sama seperti CrosshairService.Instance).
        /// </summary>
        public static HotkeyService? Instance { get; private set; }

        public void Start(Window? ownerWindow = null)
        {
            const string LOG_IDENT_FULL = "HotkeyService::Start";

            if (_registered)
                return;

            Instance = this;

            try
            {
                // Buat hidden message-only window via HwndSource untuk menerima WM_HOTKEY.
                // Gunakan WS_POPUP (tanpa caption/titlebar) — window akan 0x0 jadi invisible.
                const int WS_POPUP = unchecked((int)0x80000000);

                var sourceParams = new HwndSourceParameters("BoneFishHotkey")
                {
                    WindowStyle = WS_POPUP,
                    PositionX = 0,
                    PositionY = 0,
                    Width = 0,
                    Height = 0,
                    ParentWindow = IntPtr.Zero
                };

                _hwndSource = new HwndSource(sourceParams);
                _hwndSource.AddHook(WndProc);
                _hotkeyHwnd = (HWND)_hwndSource.Handle;

                // Daftarkan semua hotkey
                RegisterHotkey(HOTKEY_CROSSHAIR, "Ctrl+Shift+C");
                RegisterHotkey(HOTKEY_FPS, "Ctrl+Shift+F");
                RegisterHotkey(HOTKEY_NIGHTVISION, "Ctrl+Shift+N");

                _registered = true;
                App.Logger.WriteLine(LOG_IDENT_FULL, "HotkeyService initialized (Ctrl+Shift+C/F/N)");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT_FULL, $"Failed to initialize HotkeyService: {ex.Message}");
            }
        }

        private void RegisterHotkey(int id, string description)
        {
            // MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT
            const uint MOD_NOREPEAT = 0x4000;

            HOT_KEY_MODIFIERS modifiers = HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_SHIFT | (HOT_KEY_MODIFIERS)MOD_NOREPEAT;
            uint virtualKey;

            switch (id)
            {
                case HOTKEY_CROSSHAIR:
                    virtualKey = (uint)'C'; // VK_C
                    break;
                case HOTKEY_FPS:
                    virtualKey = (uint)'F'; // VK_F
                    break;
                case HOTKEY_NIGHTVISION:
                    virtualKey = (uint)'N'; // VK_N
                    break;
                default:
                    return;
            }

            bool success = PInvoke.RegisterHotKey(_hotkeyHwnd, id, modifiers, virtualKey);

            if (success)
                App.Logger.WriteLine(LOG_IDENT, $"Registered hotkey {description} (id={id})");
            else
                App.Logger.WriteLine(LOG_IDENT, $"FAILED to register hotkey {description} (id={id}) — maybe already in use by another app");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                handled = true;

                switch (hotkeyId)
                {
                    case HOTKEY_CROSSHAIR:
                        App.Logger.WriteLine(LOG_IDENT, "Hotkey: Ctrl+Shift+C — Toggle Crosshair");
                        CrosshairService.Instance?.Toggle();
                        break;

                    case HOTKEY_FPS:
                        App.Logger.WriteLine(LOG_IDENT, "Hotkey: Ctrl+Shift+F — Toggle FPS Monitor");
                        ToggleFpsMonitor();
                        break;

                    case HOTKEY_NIGHTVISION:
                        App.Logger.WriteLine(LOG_IDENT, "Hotkey: Ctrl+Shift+N — Toggle Night Vision");
                        ToggleNightVision();
                        break;
                }
            }

            return IntPtr.Zero;
        }

        private static void ToggleFpsMonitor()
        {
            var settings = App.Settings.Prop;
            settings.EnableFpsMonitor = !settings.EnableFpsMonitor;

            try { App.Settings.Save(); } catch { }
        }

        private static void ToggleNightVision()
        {
            var settings = App.Settings.Prop;
            settings.EnableNightVision = !settings.EnableNightVision;

            try { App.Settings.Save(); } catch { }
        }

        public void Stop()
        {
            if (!_registered)
                return;

            Instance = null;

            try
            {
                // Unregister all hotkeys
                PInvoke.UnregisterHotKey(_hotkeyHwnd, HOTKEY_CROSSHAIR);
                PInvoke.UnregisterHotKey(_hotkeyHwnd, HOTKEY_FPS);
                PInvoke.UnregisterHotKey(_hotkeyHwnd, HOTKEY_NIGHTVISION);

                _hwndSource.RemoveHook(WndProc);
                _hwndSource.Dispose();
                _hwndSource = null;
                _hotkeyHwnd = HWND.Null;

                _registered = false;
                App.Logger.WriteLine(LOG_IDENT, "HotkeyService stopped");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error stopping HotkeyService: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
