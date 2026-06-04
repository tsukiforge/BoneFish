using System.Windows;
using Bloxstrap.Models.RobloxApi;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk menampilkan notifikasi Windows native untuk Roblox
    /// </summary>
    public class RobloxNotification : IDisposable
    {
        private const string LOG_IDENT = "RobloxNotification";
        private const string APP_ID = "BoneFish";
        
        private readonly ActivityWatcher _activityWatcher;
        private CancellationTokenSource? _notificationCheckToken;
        private Task? _notificationCheckTask;
        private HashSet<string> _notifiedUsers = new();

        public event EventHandler<FriendNotificationEventArgs>? OnFriendOnline;
        public event EventHandler<NotificationEventArgs>? OnNotification;

        public RobloxNotification(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            
            _activityWatcher.OnGameJoin += (_, _) => StartNotificationMonitoring();
            _activityWatcher.OnGameLeave += (_, _) => StopNotificationMonitoring();
        }

        public void StartNotificationMonitoring()
        {
            if (App.Settings.Prop.EnableRobloxNotifications == false)
                return;

            const string LOG_IDENT = "RobloxNotification";
            App.Logger.WriteLine(LOG_IDENT, "Starting notification monitoring");

            StopNotificationMonitoring();

            _notificationCheckToken = new CancellationTokenSource();
            _notifiedUsers.Clear();

            _notificationCheckTask = Task.Run(async () =>
            {
                while (!_notificationCheckToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        int delayMs = App.Settings.Prop.OptimizeForLowEnd ? 15000 : 5000;
                        await Task.Delay(delayMs, _notificationCheckToken.Token); // Check interval configurable based on low-end optimization
                        
                        if (App.Settings.Prop.EnableFriendOnlineNotifications)
                        {
                            // Placeholder: Dalam implementasi nyata, ini akan mengambil data teman dari Roblox API
                            // Untuk sekarang, ini adalah skeleton yang menunggu integrasi API
                            await CheckFriendsStatus();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Error in notification monitoring: {ex.Message}");
                    }
                }
            }, _notificationCheckToken.Token);
        }

        public void StopNotificationMonitoring()
        {
            if (_notificationCheckToken != null)
            {
                _notificationCheckToken.Cancel();
                _notificationCheckToken.Dispose();
                _notificationCheckToken = null;
            }

            _notifiedUsers.Clear();
        }

        private async Task CheckFriendsStatus()
        {
            try
            {
                // TODO: Implementasi untuk mengambil status teman dari Roblox API
                // Untuk sekarang, ini adalah placeholder
                await Task.Delay(0);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error checking friends status: {ex.Message}");
            }
        }

        public void ShowFriendOnlineNotification(string username)
        {
            try
            {
                if (_notifiedUsers.Contains(username))
                    return;

                _notifiedUsers.Add(username);

                // Buat XML untuk toast notification
                string toastXml = $@"
                    <toast>
                        <visual>
                            <binding template='ToastText02'>
                                <text id='1'>👋 {System.Net.WebUtility.HtmlEncode(username)} ada online!</text>
                                <text id='2'>Apa mau main bareng?</text>
                            </binding>
                        </visual>
                        <audio src='ms-winsoundevent:Notification.Default'/>
                    </toast>";

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(toastXml);

                var toast = new ToastNotification(xmlDoc);
                toast.ExpirationTime = DateTime.Now.AddSeconds(30);
                
                try
                {
                    ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);
                }
                catch
                {
                    // Fallback jika toast notifier tidak tersedia
                    ShowGeneralNotification($"{username} ada online!", "Apa mau main bareng?");
                }

                App.Logger.WriteLine(LOG_IDENT, $"Menampilkan notifikasi untuk teman online: {username}");
                OnFriendOnline?.Invoke(this, new FriendNotificationEventArgs { Username = username });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error showing friend notification: {ex.Message}");
            }
        }

        public void ShowGeneralNotification(string title, string message)
        {
            try
            {
                // Buat XML untuk toast notification umum
                string toastXml = $@"
                    <toast>
                        <visual>
                            <binding template='ToastText02'>
                                <text id='1'>{System.Net.WebUtility.HtmlEncode(title)}</text>
                                <text id='2'>{System.Net.WebUtility.HtmlEncode(message)}</text>
                            </binding>
                        </visual>
                        <audio src='ms-winsoundevent:Notification.Default'/>
                    </toast>";

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(toastXml);

                var toast = new ToastNotification(xmlDoc);
                toast.ExpirationTime = DateTime.Now.AddSeconds(15);

                try
                {
                    ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);
                }
                catch
                {
                    // Fallback jika toast notifier tidak tersedia
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                }

                App.Logger.WriteLine(LOG_IDENT, $"Menampilkan notifikasi: {title}");
                OnNotification?.Invoke(this, new NotificationEventArgs { Title = title, Message = message });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error showing notification: {ex.Message}");
            }
        }

        public void ResetFriendNotification(string username)
        {
            _notifiedUsers.Remove(username);
        }

        public void Dispose()
        {
            StopNotificationMonitoring();
        }
    }

    public class FriendNotificationEventArgs : EventArgs
    {
        public string Username { get; set; } = "";
    }

    public class NotificationEventArgs : EventArgs
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
