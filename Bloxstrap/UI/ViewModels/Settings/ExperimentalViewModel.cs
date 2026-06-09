using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ExperimentalViewModel : NotifyPropertyChangedViewModel
    {
        private string _wallpaperStatus = "No wallpaper selected";

        public bool EnableSystemTrayOnClose
        {
            get => App.Settings.Prop.EnableSystemTrayOnClose;
            set => App.Settings.Prop.EnableSystemTrayOnClose = value;
        }

        public bool EnableRobloxNotifications
        {
            get => App.Settings.Prop.EnableRobloxNotifications;
            set => App.Settings.Prop.EnableRobloxNotifications = value;
        }

        public bool EnableFriendOnlineNotifications
        {
            get => App.Settings.Prop.EnableFriendOnlineNotifications;
            set => App.Settings.Prop.EnableFriendOnlineNotifications = value;
        }

        public bool EnableNotificationSound
        {
            get => App.Settings.Prop.EnableNotificationSound;
            set => App.Settings.Prop.EnableNotificationSound = value;
        }

        public bool EnableFpsMonitor
        {
            get => App.Settings.Prop.EnableFpsMonitor;
            set => App.Settings.Prop.EnableFpsMonitor = value;
        }

        public bool OptimizeForLowEnd
        {
            get => App.Settings.Prop.OptimizeForLowEnd;
            set => App.Settings.Prop.OptimizeForLowEnd = value;
        }

        public string WallpaperStatus
        {
            get => _wallpaperStatus;
            set
            {
                if (_wallpaperStatus != value)
                {
                    _wallpaperStatus = value;
                    OnPropertyChanged(nameof(WallpaperStatus));
                }
            }
        }

        public IAsyncRelayCommand SelectWallpaper1Command { get; }
        public IAsyncRelayCommand SelectWallpaper2Command { get; }
        public IAsyncRelayCommand SelectWallpaper3Command { get; }

        public ExperimentalViewModel()
        {
            SelectWallpaper1Command = new AsyncRelayCommand(SelectWallpaper1);
            SelectWallpaper2Command = new AsyncRelayCommand(SelectWallpaper2);
            SelectWallpaper3Command = new AsyncRelayCommand(SelectWallpaper3);

            UpdateWallpaperStatus();
        }

        private async Task SelectWallpaper1()
        {
            await SelectBackground(AppBackgroundService.BackgroundType.Default);
        }

        private async Task SelectWallpaper2()
        {
            await SelectBackground(AppBackgroundService.BackgroundType.Cool);
        }

        private async Task SelectWallpaper3()
        {
            await SelectBackground(AppBackgroundService.BackgroundType.Quality);
        }

        private async Task SelectBackground(AppBackgroundService.BackgroundType type)
        {
            try
            {
                await Task.Run(() =>
                {
                    var backgroundImage = AppBackgroundService.GetBackground(type);
                    if (backgroundImage != null)
                    {
                        WallpaperStatus = $"✓ Background changed to: {type}";
                        App.Settings.Save();
                    }
                    else
                    {
                        WallpaperStatus = $"✗ Failed to change background to: {type}";
                    }
                });
            }
            catch (Exception ex)
            {
                WallpaperStatus = $"✗ Error: {ex.Message}";
                App.Logger.WriteLine("ExperimentalViewModel", $"Error selecting background: {ex.Message}");
            }
        }

        private void UpdateWallpaperStatus()
        {
            WallpaperStatus = "Background will change randomly on app launch when enabled.";
        }
    }
}
