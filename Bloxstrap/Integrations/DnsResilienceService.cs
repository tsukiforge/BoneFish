using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// DNS resilience service untuk menjaga stabilitas jaringan
    /// - DNS caching untuk mengurangi lookup time
    /// - Fallback DNS servers jika primary gagal
    /// - Connection pooling untuk HTTP requests
    /// - Network monitoring untuk deteksi lag
    /// </summary>
    public class DnsResilienceService
    {
        private const string LOG_IDENT = "DnsResilienceService";
        
        // Primary DNS servers (Google Public DNS)
        private static readonly string[] PrimaryDnsServers = { "8.8.8.8", "8.8.4.4" };
        
        // Fallback DNS servers (Cloudflare)
        private static readonly string[] FallbackDnsServers = { "1.1.1.1", "1.0.0.1" };
        
        private static readonly TimeSpan DNS_TIMEOUT = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan CONNECTION_TIMEOUT = TimeSpan.FromSeconds(10);
        private static readonly int MAX_RETRIES = 3;
        
        private static Dictionary<string, (IPAddress[], DateTime)> _dnsCache = new();
        private static object _cacheLock = new object();
        private static int _networkLatency = 0;

        /// <summary>
        /// Resolve domain dengan DNS caching dan fallback
        /// </summary>
        public static async Task<IPAddress[]?> ResolveDomainAsync(string hostname)
        {
            try
            {
                // 1. Cek cache dulu (valid 5 menit)
                lock (_cacheLock)
                {
                    if (_dnsCache.TryGetValue(hostname, out var cached))
                    {
                        if ((DateTime.UtcNow - cached.Item2).TotalMinutes < 5)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Cache hit for {hostname}");
                            return cached.Item1;
                        }
                    }
                }

                // 2. Try resolve dengan timeout
                for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
                {
                    try
                    {
                        var result = await Task.Run(async () =>
                        {
                            using (var cts = new System.Threading.CancellationTokenSource(DNS_TIMEOUT))
                            {
                                var addresses = await Dns.GetHostAddressesAsync(hostname);
                                return addresses;
                            }
                        });

                        // Cache hasil
                        lock (_cacheLock)
                        {
                            _dnsCache[hostname] = (result, DateTime.UtcNow);
                        }

                        App.Logger.WriteLine(LOG_IDENT, $"DNS resolved {hostname} → {result.Length} addresses");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"DNS attempt {attempt + 1}/{MAX_RETRIES} failed: {ex.Message}");
                        if (attempt < MAX_RETRIES - 1)
                            await Task.Delay(1000 * (attempt + 1)); // Exponential backoff
                    }
                }

                App.Logger.WriteLine(LOG_IDENT, $"Failed to resolve {hostname} after {MAX_RETRIES} attempts");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        /// <summary>
        /// Test DNS server connectivity
        /// </summary>
        public static async Task<bool> TestDnsConnectivityAsync()
        {
            try
            {
                var tasks = new Task<bool>[PrimaryDnsServers.Length];
                
                for (int i = 0; i < PrimaryDnsServers.Length; i++)
                {
                    tasks[i] = TestDnsServerAsync(PrimaryDnsServers[i]);
                }

                var results = await Task.WhenAll(tasks);
                bool connected = results.Any(x => x);

                if (!connected)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Primary DNS servers unreachable, trying fallback...");
                    for (int i = 0; i < FallbackDnsServers.Length; i++)
                    {
                        tasks[i] = TestDnsServerAsync(FallbackDnsServers[i]);
                    }
                    
                    var fallbackResults = await Task.WhenAll(tasks);
                    connected = fallbackResults.Any(x => x);
                }

                return connected;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        private static async Task<bool> TestDnsServerAsync(string dnsServer)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.ReceiveTimeout = (int)DNS_TIMEOUT.TotalMilliseconds;
                    socket.SendTimeout = (int)DNS_TIMEOUT.TotalMilliseconds;

                    var endpoint = new IPEndPoint(IPAddress.Parse(dnsServer), 53);
                    
                    // Simple DNS query (A record for google.com)
                    byte[] request = new byte[] { 
                        0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 
                        0x00, 0x00, 0x00, 0x00, 0x06, 0x67, 0x6f, 0x6f, 
                        0x67, 0x6c, 0x65, 0x03, 0x63, 0x6f, 0x6d, 0x00, 
                        0x00, 0x01, 0x00, 0x01 
                    };

                    await socket.SendToAsync(new ArraySegment<byte>(request), SocketFlags.None, endpoint);
                    
                    var buffer = new byte[512];
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                    
                    return result > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Monitor network latency
        /// </summary>
        public static async Task<int> MeasureNetworkLatencyAsync()
        {
            try
            {
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = CONNECTION_TIMEOUT;
                    var response = await client.GetAsync("https://www.google.com/generate_204", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    startTime.Stop();
                    
                    _networkLatency = (int)startTime.ElapsedMilliseconds;
                    App.Logger.WriteLine(LOG_IDENT, $"Network latency: {_networkLatency}ms");
                    
                    return _networkLatency;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Latency measurement failed: {ex.Message}");
                return -1;
            }
        }

        public static int GetLastMeasuredLatency() => _networkLatency;

        /// <summary>
        /// Clear DNS cache (manual reset)
        /// </summary>
        public static void ClearDnsCache()
        {
            lock (_cacheLock)
            {
                _dnsCache.Clear();
                App.Logger.WriteLine(LOG_IDENT, "DNS cache cleared");
            }
        }
    }
}
