using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    // FPS Unlocker — independen dari preset visual (bisa stack dengan apa pun):
    // deteksi refresh rate monitor lalu tulis FramerateCap di GlobalBasicSettings_13.xml.
    // DFIntTaskSchedulerTargetFps TIDAK dipakai: tidak ada di allowlist sejak 2025-09-29.

    public static class FpsUnlockerService
    {
        private const string LOG_IDENT = "FpsUnlocker";

        private const int ENUM_CURRENT_SETTINGS = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        public readonly record struct FpsUnlockerResult(bool Ok, bool Deferred, int Cap);

        public static int GetPrimaryDisplayRefreshRate()
        {
            DEVMODE devMode = new();
            devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode))
                return 0;

            return devMode.dmDisplayFrequency;
        }

        public static FpsUnlockerResult Apply()
        {
            int refreshRate = GetPrimaryDisplayRefreshRate();

            if (refreshRate <= 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Gagal mendeteksi refresh rate monitor");
                return new FpsUnlockerResult(false, false, 0);
            }

            // Roblox menerima FramerateCap di rentang 30-240
            int cap = Math.Clamp(refreshRate, 30, 240);

            App.Logger.WriteLine(LOG_IDENT, $"Menerapkan FramerateCap={cap} (refresh rate monitor: {refreshRate} Hz)");

            if (!App.GlobalSettings.Loaded)
                App.GlobalSettings.Load();

            if (App.GlobalSettings.Document is null)
            {
                // file dibuat Roblox setelah pertama kali jalan; Bootstrapper re-apply tiap launch
                App.Logger.WriteLine(LOG_IDENT, "GlobalBasicSettings_13.xml belum ada — deferred ke launch berikutnya");
                return new FpsUnlockerResult(true, true, cap);
            }

            App.GlobalSettings.SetPreset("Rendering.FramerateCap", cap);

            bool applied = App.GlobalSettings.GetPreset("Rendering.FramerateCap") == cap.ToString();
            if (applied)
                App.GlobalSettings.Save();
            else
                App.Logger.WriteLine(LOG_IDENT, "Elemen FramerateCap tidak ditemukan di GlobalBasicSettings_13.xml — deferred ke launch berikutnya");

            return new FpsUnlockerResult(true, !applied, cap);
        }

        public static void Revert()
        {
            App.Logger.WriteLine(LOG_IDENT, "Mematikan FPS Unlocker — menghapus FramerateCap (kembali ke default Roblox)");

            if (!App.GlobalSettings.Loaded)
                App.GlobalSettings.Load();

            if (App.GlobalSettings.Document is null)
                return;

            App.GlobalSettings.RemovePreset("Rendering.FramerateCap");
            App.GlobalSettings.Save();
        }
    }
}
