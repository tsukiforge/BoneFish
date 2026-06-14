using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Membaca FPS Roblox yang ASLI menggunakan ETW (Event Tracing for Windows),
    /// dengan menghitung jumlah panggilan DXGI Present() per detik untuk proses Roblox.
    ///
    /// Ini adalah pendekatan yang sama yang dipakai PresentMon / Windows Game Bar:
    /// event berasal dari OS Windows, bukan dari injeksi ke proses Roblox, sehingga
    /// AMAN dari anti-cheat (Hyperion/Byfron). ETW real-time session memerlukan hak
    /// administrator; bila tidak tersedia, <see cref="Start"/> mengembalikan false.
    /// </summary>
    public class RealFpsCounter : IDisposable
    {
        private const string LOG_IDENT = "RealFpsCounter";
        private const string SessionName = "BoneFish-FpsCounter";
        private const string DxgiProvider = "Microsoft-Windows-DXGI";

        private readonly int _processId;
        private readonly Stopwatch _stopwatch = new();

        private TraceEventSession? _session;
        private Thread? _processingThread;
        private long _frameCount = 0;
        private long _totalFramesObserved = 0;
        private bool _disposed = false;

        /// <summary>
        /// True jika ETW berhasil diinisialisasi (proses berjalan dengan hak admin).
        /// </summary>
        public bool IsSupported { get; private set; }

        public RealFpsCounter(int processId)
        {
            _processId = processId;
        }

        /// <summary>
        /// Mulai sesi ETW. Mengembalikan true jika berhasil (butuh admin).
        /// </summary>
        public bool Start()
        {
            if (TraceEventSession.IsElevated() != true)
            {
                App.Logger.WriteLine(LOG_IDENT, "Process is not elevated; real ETW FPS counter is unavailable");
                IsSupported = false;
                return false;
            }

            try
            {
                // Clean up any stale session left over from a previous crash
                // (a real-time session name can only be used once at a time).
                try
                {
                    TraceEventSession.GetActiveSession(SessionName)?.Stop();
                }
                catch
                {
                    // no existing session, ignore
                }

                _session = new TraceEventSession(SessionName) { StopOnDispose = true };
                _session.EnableProvider(DxgiProvider);
                _session.Source.Dynamic.All += OnEvent;

                _stopwatch.Start();

                _processingThread = new Thread(() =>
                {
                    try
                    {
                        _session.Source.Process();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"ETW processing stopped: {ex.Message}");
                    }
                })
                {
                    IsBackground = true,
                    Name = "FpsEtwThread"
                };

                _processingThread.Start();

                IsSupported = true;
                App.Logger.WriteLine(LOG_IDENT, $"Real ETW FPS counter started for PID {_processId}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to start ETW session: {ex.Message}");
                IsSupported = false;
                Dispose();
                return false;
            }
        }

        private void OnEvent(TraceEvent data)
        {
            if (data.ProcessID != _processId)
                return;

            // Count exactly one event per presented frame. The DXGI provider emits
            // "PresentStart" once per Present() call; matching the exact name (rather
            // than any "Present*" event) avoids double-counting overlay/multiplane events.
            if (data.Opcode == TraceEventOpcode.Start &&
                data.EventName.Equals("PresentStart", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _frameCount);
                Interlocked.Increment(ref _totalFramesObserved);
            }
        }

        /// <summary>
        /// True once at least one frame has been observed via ETW. If this stays false
        /// while in-game, the client is likely rendering through Vulkan, which does not
        /// emit DXGI Present events (the built-in Roblox HUD should be used instead).
        /// </summary>
        public bool HasObservedFrames => Interlocked.Read(ref _totalFramesObserved) > 0;

        /// <summary>
        /// Mengembalikan FPS sejak panggilan terakhir dan mereset penghitung.
        /// </summary>
        public double SampleFps()
        {
            double elapsed = _stopwatch.Elapsed.TotalSeconds;
            if (elapsed <= 0)
                return 0;

            long frames = Interlocked.Exchange(ref _frameCount, 0);
            _stopwatch.Restart();

            return frames / elapsed;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _session?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error disposing ETW session: {ex.Message}");
            }

            _session = null;
            GC.SuppressFinalize(this);
        }
    }
}
