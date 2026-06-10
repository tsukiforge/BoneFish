using System;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ExperimentalViewModel : NotifyPropertyChangedViewModel
    {
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
    }
}
