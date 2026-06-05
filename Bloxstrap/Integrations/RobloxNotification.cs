using System.Windows;
using System.Xml;
using System.Net.Http;
using Bloxstrap.Models.RobloxApi;

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

            const string LOG_IDENT = "RobloxNotification::StartNotificationMonitoring";
            App.Logger.WriteLine(LOG_IDENT, "Starting notification monitoring");

            StopNotificationMonitoring();

            _notificationCheckToken = new CancellationTokenSource();
            _notifiedUsers.Clear();

            _notificationCheckTask = Task.Run(async () =>
            {
                int consecutiveFailures = 0;
                const int MAX_CONSECUTIVE_FAILURES = 3;

                while (!_notificationCheckToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Adaptive delay based on low-end optimization dan network failures
                        int baseDelayMs = App.Settings.Prop.OptimizeForLowEnd ? 15000 : 5000;
                        int delayMs = baseDelayMs + (consecutiveFailures * 2000); // Backoff on failures
                        
                        await Task.Delay(Math.Min(delayMs, 30000), _notificationCheckToken.Token);
                        
                        // Check DNS connectivity sebelum API call
                        if (!await DnsResilienceService.TestDnsConnectivityAsync())
                        {
                            App.Logger.WriteLine(LOG_IDENT, "DNS unavailable, skipping notification check");
                            consecutiveFailures++;
                            continue;
                        }

                        consecutiveFailures = 0; // Reset on success

                        if (App.Settings.Prop.EnableFriendOnlineNotifications)
                        {
                            await CheckFriendsStatus();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Network error in notification monitoring: {ex.Message}");
                        consecutiveFailures++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Error in notification monitoring: {ex.Message}");
                        consecutiveFailures++;
                    }

                    // Stop monitoring jika too many failures
                    if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Too many failures ({MAX_CONSECUTIVE_FAILURES}), pausing notifications");
                        break;
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

                // Fallback to MessageBox since Windows Runtime APIs are not available
                ShowGeneralNotification($"{username} ada online!", "Apa mau main bareng?");

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
                // Use MessageBox as the primary notification method
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

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
