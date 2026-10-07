using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    // FPS Unlocker — independen dari preset visual (bisa stack dengan apa pun):
    // deteksi refresh rate monitor lalu tulis FramerateCap di GlobalBasicSettings_13.xml.
    // DFIntTaskSchedulerTargetFps TIDAK dipakai: tidak ada di allowlist sejak 2025-09-29.

    public static class FpsUnlockerService
    {
        private const string LOG_IDENT = "FpsUnlocker";
        private const int MaximumRobloxFramerateCap = 120;
        private const int LegacyRobloxFramerateCap = 240;

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

        public static int GetRecommendedFramerateCap(HardwareProfile.HardwareTier tier, int refreshRate)
        {
            if (refreshRate <= 0)
                return 0;

            int tierCap = tier switch
            {
                HardwareProfile.HardwareTier.UltraLow => 30,
                HardwareProfile.HardwareTier.Low => 45,
                HardwareProfile.HardwareTier.Balanced => 60,
                HardwareProfile.HardwareTier.Mid => 90,
                HardwareProfile.HardwareTier.High => MaximumRobloxFramerateCap,
                _ => 60
            };

            return Math.Min(tierCap, refreshRate);
        }

        public static int GetRecommendedFramerateCap(HardwareProfile profile)
        {
            if (profile.DisplayRefreshRate <= 0)
                return 0;

            bool constrainedIntegratedSystem =
                profile.PhysicalCores is > 0 and <= 2
                && profile.TotalRamBytes > 0
                && profile.TotalRamMb <= 8192
                && profile.GpuDetectionComplete
                && !profile.HasDedicatedGpu;

            if (constrainedIntegratedSystem)
                return Math.Min(30, profile.DisplayRefreshRate);

            return GetRecommendedFramerateCap(profile.Tier, profile.DisplayRefreshRate);
        }

        public static FpsUnlockerResult Apply()
        {
            HardwareProfile profile = HardwareProfileEngine.GetProfile();
            int refreshRate = GetPrimaryDisplayRefreshRate();
            if (refreshRate <= 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Gagal mendeteksi refresh rate monitor");
                return new FpsUnlockerResult(false, false, 0);
            }

            profile = profile with { DisplayRefreshRate = refreshRate };
            int cap = GetRecommendedFramerateCap(profile);

            App.Logger.WriteLine(LOG_IDENT,
                $"Menerapkan FramerateCap={cap} (tier={profile.TierDisplay}, refresh rate monitor={refreshRate} Hz)");

            if (!App.GlobalSettings.Loaded)
                App.GlobalSettings.Load();

            if (App.GlobalSettings.Document is null)
            {
                // file dibuat Roblox setelah pertama kali jalan; Bootstrapper re-apply tiap launch
                App.Logger.WriteLine(LOG_IDENT, "GlobalBasicSettings_13.xml belum ada — deferred ke launch berikutnya");
                return new FpsUnlockerResult(true, true, cap);
            }

            // ★ HARDENING (audit FPS Fase 5): jangan hancurkan cap manual user tanpa jejak.
            // Catat nilai FramerateCap yang ada sebelum BoneFish menimpanya — hanya sekali.
            // Nilai ini dipakai Revert() untuk memulihkan konfigurasi user saat toggle OFF.
            if (!App.Settings.Prop.FpsUnlockerCapManaged)
            {
                string? existing = App.GlobalSettings.GetPreset("Rendering.FramerateCap");

                if (!String.IsNullOrWhiteSpace(existing))
                {
                    App.Settings.Prop.FpsUnlockerPreviousCap = existing;
                    App.Logger.WriteLine(LOG_IDENT, $"FramerateCap user terdeteksi ({existing}) — disimpan untuk dipulihkan saat toggle OFF");
                }
                else
                {
                    App.Settings.Prop.FpsUnlockerPreviousCap = null;
                }

                App.Settings.Prop.FpsUnlockerCapManaged = true;
                try { App.Settings.Save(); } catch { }
            }

            App.GlobalSettings.SetPreset("Rendering.FramerateCap", cap);

            bool applied = App.GlobalSettings.GetPreset("Rendering.FramerateCap") == cap.ToString();
            if (applied)
            {
                App.GlobalSettings.Save();
                App.Settings.Prop.FpsUnlockerAppliedCap = cap;
                try { App.Settings.Save(); } catch { }
            }
            else
                App.Logger.WriteLine(LOG_IDENT, "Elemen FramerateCap tidak ditemukan di GlobalBasicSettings_13.xml — deferred ke launch berikutnya");

            return new FpsUnlockerResult(true, !applied, cap);
        }

        public static void Revert()
        {
            if (!App.GlobalSettings.Loaded)
                App.GlobalSettings.Load();

            if (App.GlobalSettings.Document is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Mematikan FPS Unlocker — GlobalBasicSettings_13.xml tidak ada, tidak ada yang diubah");
                ResetManagedState();
                return;
            }

            string? previous = App.Settings.Prop.FpsUnlockerPreviousCap;
            bool managed = App.Settings.Prop.FpsUnlockerCapManaged;

            if (managed && !String.IsNullOrWhiteSpace(previous))
            {
                // Pulihkan cap milik user — JANGAN hapus konfigurasi yang bukan milik BoneFish.
                App.GlobalSettings.SetPreset("Rendering.FramerateCap", previous);
                App.GlobalSettings.Save();
                App.Logger.WriteLine(LOG_IDENT, $"Mematikan FPS Unlocker — FramerateCap dipulihkan ke nilai sebelumnya ({previous})");
            }
            else
            {
                // Tidak ada nilai user yang tercatat. Hapus HANYA bila nilainya memang
                // nilai tulisan BoneFish; selain itu jangan sentuh.
                string? current = App.GlobalSettings.GetPreset("Rendering.FramerateCap");
                int? appliedCap = App.Settings.Prop.FpsUnlockerAppliedCap;

                bool isManagedValue = managed && (
                    (appliedCap.HasValue && current == appliedCap.Value.ToString())
                    || (!appliedCap.HasValue && current is "120" or "240"));

                if (isManagedValue)
                {
                    App.GlobalSettings.RemovePreset("Rendering.FramerateCap");
                    App.GlobalSettings.Save();
                    App.Logger.WriteLine(LOG_IDENT, $"Mematikan FPS Unlocker — FramerateCap tulisan BoneFish ({current}) dihapus (kembali ke default Roblox)");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Mematikan FPS Unlocker — FramerateCap ({current ?? "<tidak ada>"}) bukan nilai tulisan BoneFish, tidak diubah");
                }
            }

            ResetManagedState();
        }

        private static void ResetManagedState()
        {
            App.Settings.Prop.FpsUnlockerCapManaged = false;
            App.Settings.Prop.FpsUnlockerPreviousCap = null;
            App.Settings.Prop.FpsUnlockerAppliedCap = null;
            try { App.Settings.Save(); } catch { }
        }
    }
}
