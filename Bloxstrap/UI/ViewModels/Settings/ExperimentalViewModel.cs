using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Integrations;
using Bloxstrap.UI.Elements.Settings;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ExperimentalViewModel : NotifyPropertyChangedViewModel
    {
        private string _wallpaperStatus = "Background akan berubah secara acak saat aplikasi dibuka.";

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

        public bool EnableWallpaperLauncher
        {
            get => App.Settings.Prop.EnableWallpaperLauncher;
            set
            {
                App.Settings.Prop.EnableWallpaperLauncher = value;
                OnPropertyChanged(nameof(EnableWallpaperLauncher));
            }
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
        public IAsyncRelayCommand SelectWallpaper4Command { get; }

        public ExperimentalViewModel()
        {
            SelectWallpaper1Command = new AsyncRelayCommand(SelectWallpaper1);
            SelectWallpaper2Command = new AsyncRelayCommand(SelectWallpaper2);
            SelectWallpaper3Command = new AsyncRelayCommand(SelectWallpaper3);
            SelectWallpaper4Command = new AsyncRelayCommand(SelectWallpaper4);

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

        private async Task SelectWallpaper4()
        {
            await SelectBackground(AppBackgroundService.BackgroundType.Extra);
        }

        private async Task SelectBackground(AppBackgroundService.BackgroundType type)
        {
            try
            {
                App.Logger.WriteLine("ExperimentalViewModel", $"SelectBackground called with type: {type}");
                
                var backgroundImage = AppBackgroundService.GetBackground(type);
                
                App.Logger.WriteLine("ExperimentalViewModel", $"Background image loaded: {backgroundImage != null}");
                
                if (backgroundImage != null)
                {
                    // Apply to current window immediately
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                        if (mainWindow != null)
                        {
                            App.Logger.WriteLine("ExperimentalViewModel", "Applying background to MainWindow");
                            mainWindow.Background = new System.Windows.Media.ImageBrush(backgroundImage)
                            {
                                Stretch = System.Windows.Media.Stretch.UniformToFill
                            };
                            App.Logger.WriteLine("ExperimentalViewModel", "Background applied successfully");
                        }
                        else
                        {
                            App.Logger.WriteLine("ExperimentalViewModel", "MainWindow not found!");
                        }
                    });

                    WallpaperStatus = $"✓ Background diubah ke: {type}";
                }
                else
                {
                    WallpaperStatus = $"✗ Gagal mengubah background ke: {type}";
                }
            }
            catch (Exception ex)
            {
                WallpaperStatus = $"✗ Error: {ex.Message}";
                App.Logger.WriteLine("ExperimentalViewModel", $"Error selecting background: {ex.Message}");
                App.Logger.WriteLine("ExperimentalViewModel", $"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateWallpaperStatus()
        {
            WallpaperStatus = "Background akan berubah secara acak saat aplikasi dibuka.";
        }
    }
}
