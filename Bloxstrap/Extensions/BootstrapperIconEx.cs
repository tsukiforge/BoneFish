using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Media;

namespace Bloxstrap.Extensions
{
    static class BootstrapperIconEx
    {
        private static readonly ConcurrentDictionary<BootstrapperIcon, ImageSource> ImageSourceCache = new();

        public static void ClearCache(BootstrapperIcon icon)
        {
            ImageSourceCache.TryRemove(icon, out _);
        }

        public static ImageSource GetImageSource(this BootstrapperIcon icon)
        {
            return ImageSourceCache.GetOrAdd(icon, key =>
            {
                ImageSource image = key.GetIcon().GetImageSource();
                if (image.CanFreeze)
                    image.Freeze();
                return image;
            });
        }
        public static IReadOnlyCollection<BootstrapperIcon> Selections => new BootstrapperIcon[]
        {
            //BootstrapperIcon.IconFishstrap,
            BootstrapperIcon.IconBloxstrap,
            BootstrapperIcon.Icon2022,
            BootstrapperIcon.Icon2019,
            BootstrapperIcon.Icon2017,
            BootstrapperIcon.IconLate2015,
            BootstrapperIcon.IconEarly2015,
            BootstrapperIcon.Icon2011,
            BootstrapperIcon.Icon2008,
            BootstrapperIcon.IconBloxstrapClassic,
            BootstrapperIcon.IconCustom
        };

        // small note on handling icon sizes
        // i'm using multisize icon packs here with sizes 16, 24, 32, 48, 64 and 128
        // use this for generating multisize packs: https://www.aconvert.com/icon/

        public static Icon GetIcon(this BootstrapperIcon icon)
        {
            const string LOG_IDENT = "BootstrapperIconEx::GetIcon";

            // load the custom icon file
            if (icon == BootstrapperIcon.IconCustom)
            {
                Icon? customIcon = null;
                string location = App.Settings.Prop.BootstrapperIconCustomLocation;

                if (String.IsNullOrEmpty(location)) 
                {
                    App.Logger.WriteLine(LOG_IDENT, "Warning: custom icon is not set.");
                }
                else
                {
                    try
                    {
                        customIcon = new Icon(location);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to load custom icon!");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }

                return customIcon ?? Properties.Resources.IconBloxstrap;
            }

            return icon switch
            {
                //BootstrapperIcon.IconFishstrap => Properties.Resources.IconFishstrap,
                BootstrapperIcon.IconBloxstrap => Properties.Resources.IconBloxstrap,
                BootstrapperIcon.Icon2008 => Properties.Resources.Icon2008,
                BootstrapperIcon.Icon2011 => Properties.Resources.Icon2011,
                BootstrapperIcon.IconEarly2015 => Properties.Resources.IconEarly2015,
                BootstrapperIcon.IconLate2015 => Properties.Resources.IconLate2015,
                BootstrapperIcon.Icon2017 => Properties.Resources.Icon2017,
                BootstrapperIcon.Icon2019 => Properties.Resources.Icon2019,
                BootstrapperIcon.Icon2022 => Properties.Resources.Icon2022,
                BootstrapperIcon.IconBloxstrapClassic => Properties.Resources.IconBloxstrapClassic,
                _ => Properties.Resources.IconBloxstrap
            };
        }
    }
}
