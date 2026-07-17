using System;
using System.Windows.Forms;

namespace Bloxstrap.Utility
{
    /// <summary>
    /// Utility untuk mendeteksi status baterai device.
    /// Digunakan oleh FastFlagsViewModel (System Info) dan ExperimentalViewModel (Battery Saver).
    /// </summary>
    internal static class BatteryHelper
    {
        /// <summary>
        /// Cek apakah device sedang berjalan dengan daya baterai (tidak dicharge).
        /// </summary>
        public static bool IsOnBatteryPower()
        {
            try
            {
                var powerStatus = SystemInformation.PowerStatus;
                if (powerStatus == null)
                    return false;

                // Jika tidak ada baterai sama sekali (PC desktop)
                if (powerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
                    return false;

                return powerStatus.PowerLineStatus == PowerLineStatus.Offline;
            }
            catch
            {
                return false; // default safe: jangan skip wallpaper
            }
        }

        /// <summary>
        /// Cek apakah device punya baterai (laptop/tablet) atau tidak (PC desktop).
        /// </summary>
        public static bool HasBattery()
        {
            try
            {
                var powerStatus = SystemInformation.PowerStatus;
                if (powerStatus == null)
                    return false;

                return !powerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery);
            }
            catch
            {
                return false;
            }
        }
    }
}
