using System;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ExperimentalViewModel : NotifyPropertyChangedViewModel
    {
        public bool EnableSystemTrayOnClose
        {
            get => App.Settings.Prop.EnableSystemTrayOnClose;
            set
            {
                App.Settings.Prop.EnableSystemTrayOnClose = value;
                OnPropertyChanged(nameof(EnableSystemTrayOnClose));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableRobloxNotifications
        {
            get => App.Settings.Prop.EnableRobloxNotifications;
            set
            {
                App.Settings.Prop.EnableRobloxNotifications = value;
                OnPropertyChanged(nameof(EnableRobloxNotifications));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableFriendOnlineNotifications
        {
            get => App.Settings.Prop.EnableFriendOnlineNotifications;
            set
            {
                App.Settings.Prop.EnableFriendOnlineNotifications = value;
                OnPropertyChanged(nameof(EnableFriendOnlineNotifications));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableNotificationSound
        {
            get => App.Settings.Prop.EnableNotificationSound;
            set
            {
                App.Settings.Prop.EnableNotificationSound = value;
                OnPropertyChanged(nameof(EnableNotificationSound));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool IsRunningAsAdmin => Utilities.IsRunningAsAdmin();

        public Visibility FpsAdminInfoVisibility =>
            EnableFpsMonitor && !IsRunningAsAdmin ? Visibility.Visible : Visibility.Collapsed;

        public bool EnableFpsMonitor
        {
            get => App.Settings.Prop.EnableFpsMonitor;
            set
            {
                App.Settings.Prop.EnableFpsMonitor = value;
                OnPropertyChanged(nameof(EnableFpsMonitor));
                OnPropertyChanged(nameof(FpsAdminInfoVisibility));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool OptimizeForLowEnd
        {
            get => App.Settings.Prop.OptimizeForLowEnd;
            set
            {
                App.Settings.Prop.OptimizeForLowEnd = value;
                OnPropertyChanged(nameof(OptimizeForLowEnd));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableHotkeys
        {
            get => App.Settings.Prop.EnableHotkeys;
            set
            {
                App.Settings.Prop.EnableHotkeys = value;
                OnPropertyChanged(nameof(EnableHotkeys));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableCrosshair
        {
            get => App.Settings.Prop.EnableCrosshair;
            set
            {
                App.Settings.Prop.EnableCrosshair = value;
                OnPropertyChanged(nameof(EnableCrosshair));
                try { App.Settings.Save(); } catch { }
                
                // Notify CrosshairService to apply/remove overlay
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public string CrosshairStyle
        {
            get => App.Settings.Prop.CrosshairStyle;
            set
            {
                App.Settings.Prop.CrosshairStyle = value;
                OnPropertyChanged(nameof(CrosshairStyle));
                try { App.Settings.Save(); } catch { }
                
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public double CrosshairSize
        {
            get => App.Settings.Prop.CrosshairSize;
            set
            {
                App.Settings.Prop.CrosshairSize = value;
                OnPropertyChanged(nameof(CrosshairSize));
                try { App.Settings.Save(); } catch { }
                
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public double CrosshairOpacity
        {
            get => App.Settings.Prop.CrosshairOpacity;
            set
            {
                App.Settings.Prop.CrosshairOpacity = value;
                OnPropertyChanged(nameof(CrosshairOpacity));
                try { App.Settings.Save(); } catch { }
                
                CrosshairService.Instance?.ApplySettings();
            }
        }

        // Available crosshair styles for the ComboBox
        public string[] CrosshairStyles { get; } = new[] { "Cross", "Dot", "Circle", "CrossDot" };

        public ICommand SelectCrosshairColorCommand { get; }

        private void SelectCrosshairColor(object? param)
        {
            if (param is string color && !string.IsNullOrEmpty(color))
            {
                App.Settings.Prop.CrosshairColor = color;
                try { App.Settings.Save(); } catch { }
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public bool EnableTurboMode
        {
            get => App.Settings.Prop.EnableTurboMode;
            set
            {
                App.Settings.Prop.EnableTurboMode = value;
                OnPropertyChanged(nameof(EnableTurboMode));

                if (value)
                {
                    // Turbo Mode ON: force extreme optimizations
                    App.Settings.Prop.OptimizeForLowEnd = true;
                    App.Settings.Prop.ForceExtremeMode = true;
                    OnPropertyChanged(nameof(OptimizeForLowEnd));

                    AutoOptimizeService.ApplyAggressiveOptimizations(AutoOptimizeService.SystemTier.ExtremePerformance);
                }
                else
                {
                    // Turbo Mode OFF: restore normal settings
                    App.Settings.Prop.OptimizeForLowEnd = false;
                    App.Settings.Prop.ForceExtremeMode = false;
                    OnPropertyChanged(nameof(OptimizeForLowEnd));

                    AutoOptimizeService.RemoveOptimizations();
                }

                try { App.Settings.Save(); } catch { }

                if (App.FastFlags.Changed)
                    App.FastFlags.Save();
            }
        }

        public ExperimentalViewModel()
        {
            SelectCrosshairColorCommand = new RelayCommand<string?>(SelectCrosshairColor);
        }
    }
}
