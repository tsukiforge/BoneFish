using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    // FPS cap diterapkan independen dari preset visual:
    // tulis FramerateCap di GlobalBasicSettings_13.xml.
    // DFIntTaskSchedulerTargetFps TIDAK dipakai: tidak ada di allowlist sejak 2025-09-29.
    public static class FpsUnlockerService
    {
        private const string LOG_IDENT = "FpsUnlocker";
        public const int MinimumRobloxFramerateCap = 50;
        public const int MaximumRobloxFramerateCap = 144;
        private const int AdaptiveBaseCap = 50;
        private const int AdaptiveUpperCap = 60;
        private static readonly TimeSpan AdaptiveStableWindow = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan AdaptiveChangeCooldown = TimeSpan.FromSeconds(30);
        private static readonly object AdaptiveSync = new();
        private static DateTime? _adaptiveHealthySinceUtc;
        private static DateTime _adaptiveLastChangeUtc = DateTime.MinValue;
        private static bool _adaptiveApplyInProgress;
        private static int? _adaptiveAppliedCap;
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

        public static int GetConfiguredFramerateCap() =>
            Math.Clamp(App.Settings.Prop.FpsUnlockerCap, MinimumRobloxFramerateCap, MaximumRobloxFramerateCap);

        public static bool IsAdaptiveMode =>
            App.Settings.Prop.FpsUnlockerEnabled && App.Settings.Prop.FpsUnlockerAdaptiveMode;

        // On low-end/forced-extreme profiles, changing GlobalBasicSettings between
        // 50 and 60 FPS while Roblox is under render pressure adds disk writes and
        // can amplify a stall on HDD systems. Keep the conservative base cap until
        // the user explicitly selects a fixed cap or disables the adaptive mode.
        private static bool IsConservativeHardwareProfile =>
            App.Settings.Prop.OptimizeForLowEnd || App.Settings.Prop.ForceExtremeMode;

        public static int GetCurrentAdaptiveCap()
        {
            lock (AdaptiveSync)
                return _adaptiveAppliedCap ?? AdaptiveBaseCap;
        }

        public static FpsUnlockerResult Apply()
        {
            int cap = App.Settings.Prop.FpsUnlockerAdaptiveMode ? AdaptiveBaseCap : GetConfiguredFramerateCap();
            lock (AdaptiveSync)
            {
                _adaptiveHealthySinceUtc = null;
                _adaptiveLastChangeUtc = DateTime.UtcNow;
                _adaptiveAppliedCap = cap;
            }
            return ApplyCap(cap, "manual");
        }

        /// <summary>
        /// Called by the watcher only on a healthy, responsive tick. It is intentionally
        /// cheap: no IO occurs until the stable window and cooldown have both elapsed.
        /// </summary>
        public static void NotifyHealthyTick()
        {
            if (!IsAdaptiveMode)
                return;

            // FPS cap is a launch-time setting. Rewriting GlobalBasicSettings while
            // Roblox is rendering or recovering from a stall makes the game reload
            // its user settings at the worst possible time. Adaptive mode remains
            // accepted for backwards compatibility, but never raises the cap live.
            if (IsConservativeHardwareProfile || IsRobloxRunning())
                return;

            bool shouldRaise = false;
            lock (AdaptiveSync)
            {
                DateTime now = DateTime.UtcNow;
                _adaptiveHealthySinceUtc ??= now;
                shouldRaise = _adaptiveAppliedCap == AdaptiveBaseCap
                    && !_adaptiveApplyInProgress
                    && now - _adaptiveHealthySinceUtc.Value >= AdaptiveStableWindow
                    && now - _adaptiveLastChangeUtc >= AdaptiveChangeCooldown;
                if (shouldRaise)
                    _adaptiveApplyInProgress = true;
            }

            if (!shouldRaise)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    if (IsAdaptiveMode)
                        ApplyCap(AdaptiveUpperCap, "adaptive-stable");
                }
                catch (Exception ex)
                {
                    try { App.Logger.WriteException(LOG_IDENT, ex); } catch { }
                }
                finally
                {
                    lock (AdaptiveSync)
                    {
                        _adaptiveApplyInProgress = false;
                        _adaptiveLastChangeUtc = DateTime.UtcNow;
                    }
                }
            });
        }

        /// <summary>
        /// Called when the watcher confirms a render stall. It immediately schedules a
        /// single safe fallback; repeated stall ticks do not cause repeated disk writes.
        /// </summary>
        public static void NotifyRenderStall()
        {
            if (!IsAdaptiveMode)
                return;

            // Do not write GlobalBasicSettings during a stall. The configured base
            // cap is already applied on the next launch and is safer than a live
            // 50/60 FPS oscillation while Roblox is unresponsive.
            if (IsRobloxRunning())
                return;

            bool shouldLower = false;
            lock (AdaptiveSync)
            {
                _adaptiveHealthySinceUtc = null;
                shouldLower = _adaptiveAppliedCap != AdaptiveBaseCap && !_adaptiveApplyInProgress;
                if (shouldLower)
                    _adaptiveApplyInProgress = true;
            }

            if (!shouldLower)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    if (IsAdaptiveMode)
                        ApplyCap(AdaptiveBaseCap, "adaptive-stall");
                }
                catch (Exception ex)
                {
                    try { App.Logger.WriteException(LOG_IDENT, ex); } catch { }
                }
                finally
                {
                    lock (AdaptiveSync)
                    {
                        _adaptiveApplyInProgress = false;
                        _adaptiveLastChangeUtc = DateTime.UtcNow;
                    }
                }
            });
        }

        private static FpsUnlockerResult ApplyCap(int cap, string reason)
        {
            cap = Math.Clamp(cap, MinimumRobloxFramerateCap, MaximumRobloxFramerateCap);
            int refreshRate = GetPrimaryDisplayRefreshRate();
            App.Logger.WriteLine(LOG_IDENT,
                $"Menerapkan FramerateCap={cap} reason={reason}" +
                (refreshRate > 0 ? $" (refresh rate monitor={refreshRate} Hz)" : " (refresh rate monitor tidak terbaca)"));

            if (IsRobloxRunning())
            {
                App.Logger.WriteLine(LOG_IDENT,
                    "Roblox masih berjalan — cap FPS ditunda ke launch berikutnya; GlobalBasicSettings tidak ditulis saat sesi aktif.");
                return new FpsUnlockerResult(true, true, cap);
            }

            App.GlobalSettings.Load();
            if (App.GlobalSettings.Document is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "GlobalBasicSettings_13.xml belum ada — deferred ke launch berikutnya");
                return new FpsUnlockerResult(true, true, cap);
            }

            string? current = App.GlobalSettings.GetPreset("Rendering.FramerateCap");
            if (!App.Settings.Prop.FpsUnlockerCapManaged)
            {
                App.Settings.Prop.FpsUnlockerPreviousCap = String.IsNullOrWhiteSpace(current) ? null : current;
                App.Settings.Prop.FpsUnlockerCapManaged = true;
                try { App.Settings.Save(); } catch { }
                if (!String.IsNullOrWhiteSpace(current))
                    App.Logger.WriteLine(LOG_IDENT, $"FramerateCap user terdeteksi ({current}) — disimpan untuk dipulihkan saat toggle OFF");
            }

            if (current == cap.ToString())
            {
                lock (AdaptiveSync)
                    _adaptiveAppliedCap = cap;
                App.Settings.Prop.FpsUnlockerAppliedCap = cap;
                try { App.Settings.Save(); } catch { }
                App.Logger.WriteLine(LOG_IDENT, $"FramerateCap sudah {cap}; penulisan GlobalBasicSettings dilewati");
                return new FpsUnlockerResult(true, false, cap);
            }

            App.GlobalSettings.SetPreset("Rendering.FramerateCap", cap);
            bool applied = App.GlobalSettings.GetPreset("Rendering.FramerateCap") == cap.ToString();
            if (!applied)
            {
                App.Logger.WriteLine(LOG_IDENT, "Elemen FramerateCap tidak ditemukan di GlobalBasicSettings_13.xml — deferred ke launch berikutnya");
                return new FpsUnlockerResult(true, true, cap);
            }

            App.GlobalSettings.Save();
            App.Settings.Prop.FpsUnlockerAppliedCap = cap;
            try { App.Settings.Save(); } catch { }
            lock (AdaptiveSync)
                _adaptiveAppliedCap = cap;
            return new FpsUnlockerResult(true, false, cap);
        }

        private static bool IsRobloxRunning()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("RobloxPlayerBeta");
                bool running = false;
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                            running = true;
                    }
                    catch
                    {
                        // An inaccessible process may still be the active client.
                        running = true;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
                return running;
            }
            catch
            {
                // If process inspection is unavailable, prefer deferring a write
                // over changing Roblox settings while a session may be active.
                return true;
            }
        }

        public static void Revert()
        {
            lock (AdaptiveSync)
            {
                _adaptiveHealthySinceUtc = null;
                _adaptiveAppliedCap = null;
                _adaptiveApplyInProgress = false;
            }

            App.GlobalSettings.Load();
            if (App.GlobalSettings.Document is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Mematikan FPS Unlocker — GlobalBasicSettings_13.xml tidak ada, tidak ada yang diubah");
                ResetManagedState();
                return;
            }

            string? previous = App.Settings.Prop.FpsUnlockerPreviousCap;
            bool managed = App.Settings.Prop.FpsUnlockerCapManaged;
            string? current = App.GlobalSettings.GetPreset("Rendering.FramerateCap");
            int? appliedCap = App.Settings.Prop.FpsUnlockerAppliedCap;
            if (managed && !String.IsNullOrWhiteSpace(previous))
            {
                App.GlobalSettings.SetPreset("Rendering.FramerateCap", previous);
                if (current != previous)
                    App.GlobalSettings.Save();
                App.Logger.WriteLine(LOG_IDENT, $"Mematikan FPS Unlocker — FramerateCap dipulihkan ke nilai sebelumnya ({previous})");
            }
            else if (managed && appliedCap.HasValue && current == appliedCap.Value.ToString())
            {
                App.GlobalSettings.RemovePreset("Rendering.FramerateCap");
                App.GlobalSettings.Save();
                App.Logger.WriteLine(LOG_IDENT, $"Mematikan FPS Unlocker — FramerateCap tulisan BoneFish ({current}) dihapus (kembali ke default Roblox)");
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Mematikan FPS Unlocker — FramerateCap ({current ?? "<tidak ada>"}) bukan nilai tulisan BoneFish, tidak diubah");
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
