using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk mengubah background UI aplikasi BoneFish
    /// Mendukung 3 pilihan wallpaper sebagai background dari folder Bloxstrap\Resources\Wallpapers\
    /// Wallpaper berubah random setiap kali aplikasi dibuka
    /// </summary>
    public class AppBackgroundService
    {
        private const string LOG_IDENT = "AppBackgroundService";

        public enum BackgroundType
        {
            Default = 0,
            Cool = 1,
            Quality = 2,
            Extra = 3
        }

        /// <summary>
        /// Get wallpaper image source berdasarkan tipe
        /// </summary>
        private static BitmapImage? GetBackgroundImage(BackgroundType type)
        {
            try
            {
                string wallpaperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Wallpapers");

                string imagePath = type switch
                {
                    BackgroundType.Default => Path.Combine(wallpaperPath, "wallpapers.jpg"),
                    BackgroundType.Cool => Path.Combine(wallpaperPath, "wallpapersC.jpg"),
                    BackgroundType.Quality => Path.Combine(wallpaperPath, "wallpapersQ.jpg"),
                    BackgroundType.Extra => Path.Combine(wallpaperPath, "wallpapersE.jpg"),
                    _ => Path.Combine(wallpaperPath, "wallpapers.jpg")
                };

                if (!File.Exists(imagePath))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Background image not found: {imagePath}");
                    return null;
                }

                BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error loading background image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get random background image
        /// </summary>
        public static BitmapImage? GetRandomBackground()
        {
            try
            {
                var backgroundTypes = Enum.GetValues(typeof(BackgroundType)).Cast<BackgroundType>().ToList();
                var randomType = backgroundTypes[Random.Shared.Next(backgroundTypes.Count)];

                App.Logger.WriteLine(LOG_IDENT, $"Selected random background: {randomType}");
                return GetBackgroundImage(randomType);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error getting random background: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get specific background image
        /// </summary>
        public static BitmapImage? GetBackground(BackgroundType type)
        {
            return GetBackgroundImage(type);
        }

        /// <summary>
        /// Check if background files exist
        /// </summary>
        public static bool ValidateBackgroundFiles()
        {
            try
            {
                bool allExist = true;

                foreach (BackgroundType type in Enum.GetValues(typeof(BackgroundType)))
                {
                    string wallpaperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Wallpapers");
                    string imagePath = type switch
                    {
                        BackgroundType.Default => Path.Combine(wallpaperPath, "wallpapers.jpg"),
                        BackgroundType.Cool => Path.Combine(wallpaperPath, "wallpapersC.jpg"),
                        BackgroundType.Quality => Path.Combine(wallpaperPath, "wallpapersQ.jpg"),
                        BackgroundType.Extra => Path.Combine(wallpaperPath, "wallpapersE.jpg"),
                        _ => Path.Combine(wallpaperPath, "wallpapers.jpg")
                    };

                    if (!File.Exists(imagePath))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Missing background image: {type} at {imagePath}");
                        allExist = false;
                    }
                }

                if (allExist)
                    App.Logger.WriteLine(LOG_IDENT, "All background images validated successfully");

                return allExist;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error validating background files: {ex.Message}");
                return false;
            }
        }
    }
}
