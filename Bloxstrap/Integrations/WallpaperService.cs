using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk mengubah wallpaper Windows desktop
    /// Mendukung 3 pilihan wallpaper dari folder Bloxstrap\Resources\Wallpapers\
    /// </summary>
    public class WallpaperService
    {
        private const string LOG_IDENT = "WallpaperService";

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        public const int SPI_SETDESKWALLPAPER = 20;
        public const int SPIF_UPDATEINIFILE = 0x01;
        public const int SPIF_SENDCHANGE = 0x02;

        public enum WallpaperType
        {
            Default = 0,
            Cool = 1,
            Quality = 2
        }

        /// <summary>
        /// Get wallpaper path berdasarkan tipe
        /// </summary>
        private static string GetWallpaperPath(WallpaperType type)
        {
            string wallpaperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Wallpapers");

            return type switch
            {
                WallpaperType.Default => Path.Combine(wallpaperPath, "wallpapers.jpg"),
                WallpaperType.Cool => Path.Combine(wallpaperPath, "wallpapersC.jpg"),
                WallpaperType.Quality => Path.Combine(wallpaperPath, "wallpapersQ.jpg"),
                WallpaperType.E => Path.Combine(wallpaperPath, "wallpapersE.jpg"),
                _ => Path.Combine(wallpaperPath, "wallpapers.jpg")
            };
        }

        /// <summary>
        /// Set wallpaper Windows
        /// </summary>
        public static bool SetWallpaper(WallpaperType type)
        {
            try
            {
                string wallpaperPath = GetWallpaperPath(type);

                // Verify file exists
                if (!File.Exists(wallpaperPath))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Wallpaper file not found: {wallpaperPath}");
                    return false;
                }

                // Get full path
                string fullPath = Path.GetFullPath(wallpaperPath);

                // Set wallpaper using Windows API
                int result = SystemParametersInfo(
                    SPI_SETDESKWALLPAPER,
                    0,
                    fullPath,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
                );

                if (result != 0)
                {
                    // Save to registry
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                            @"Control Panel\Desktop", true))
                        {
                            if (key != null)
                            {
                                key.SetValue("Wallpaper", fullPath);
                                key.SetValue("WallpaperStyle", "6"); // Fit to screen
                                key.SetValue("TileWallpaper", "0");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Error saving to registry: {ex.Message}");
                    }

                    App.Logger.WriteLine(LOG_IDENT, $"Wallpaper set successfully: {type} ({fullPath})");
                    return true;
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to set wallpaper: API call returned 0");
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error setting wallpaper: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set random wallpaper dari yang tersedia
        /// </summary>
        public static bool SetRandomWallpaper()
        {
            try
            {
                var types = Enum.GetValues(typeof(WallpaperType)).Cast<WallpaperType>().ToList();
                var randomType = types[new Random().Next(types.Count)];

                App.Logger.WriteLine(LOG_IDENT, $"Setting random wallpaper: {randomType}");
                return SetWallpaper(randomType);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error setting random wallpaper: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get wallpaper yang sedang digunakan
        /// </summary>
        public static WallpaperType GetCurrentWallpaper()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        string? wallpaper = key.GetValue("Wallpaper") as string;
                        if (!string.IsNullOrEmpty(wallpaper))
                        {
                            if (wallpaper.Contains("wallpapersC"))
                                return WallpaperType.Cool;
                            else if (wallpaper.Contains("wallpapersQ"))
                                return WallpaperType.Quality;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error getting current wallpaper: {ex.Message}");
            }

            return WallpaperType.Default;
        }

        /// <summary>
        /// Check if wallpaper files exist
        /// </summary>
        public static bool ValidateWallpaperFiles()
        {
            try
            {
                bool allExist = true;

                foreach (WallpaperType type in Enum.GetValues(typeof(WallpaperType)))
                {
                    string path = GetWallpaperPath(type);
                    if (!File.Exists(path))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Missing wallpaper: {type} at {path}");
                        allExist = false;
                    }
                }

                if (allExist)
                    App.Logger.WriteLine(LOG_IDENT, "All wallpaper files validated successfully");

                return allExist;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error validating wallpaper files: {ex.Message}");
                return false;
            }
        }
    }
}
