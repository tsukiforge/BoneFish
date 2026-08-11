using Bloxstrap.Integrations;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox
{
    /// <summary>
    /// Samples real FPS telemetry for the sandbox by reusing the existing ETW-based
    /// <see cref="RealFpsCounter"/> (the same source the FPS Monitor overlay uses).
    /// When ETW is unavailable (no admin), sampling reports an unreliable result and the
    /// sandbox honestly reports "insufficient data" instead of pretending.
    /// </summary>
    public class PerformanceMeasurementService
    {
        private const string LOG_IDENT = "OptimizationSandbox::PerformanceMeasurement";
        private const string RobloxProcessName = "RobloxPlayerBeta";

        /// <summary>Minimum number of one-second samples for a measurement to count as reliable.</summary>
        public const int MinReliableSamples = 15;

        /// <summary>Median FPS change beyond this relative threshold counts as an actual improvement/degradation.</summary>
        public const double ChangeThreshold = 0.05;

        public static int? FindRobloxProcessId()
        {
            try
            {
                var processes = Process.GetProcessesByName(RobloxProcessName);
                return processes.Length > 0 ? processes[0].Id : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Measure median FPS over <paramref name="duration"/>. Returns a reliable sample only
        /// when ETW telemetry was available and enough samples were collected.
        /// </summary>
        public async Task<SandboxFpsSample> MeasureAsync(
            TimeSpan duration,
            int? processId = null,
            CancellationToken cancellationToken = default)
        {
            var sample = new SandboxFpsSample
            {
                SampledAt = DateTime.UtcNow,
                Reliable = false
            };

            processId ??= FindRobloxProcessId();

            if (processId is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Measurement skipped: Roblox is not running");
                return sample;
            }

            var values = new List<double>();
            RealFpsCounter? counter = null;
            bool counterUsable = false;

            await Task.Run(() =>
            {
                counter = new RealFpsCounter(processId.Value);

                if (!counter.Start())
                {
                    App.Logger.WriteLine(LOG_IDENT, "Measurement unavailable: ETW telemetry requires administrator privileges");
                    return;
                }

                counterUsable = true;

                DateTime started = DateTime.UtcNow;

                while (DateTime.UtcNow - started < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsRobloxProcessAlive(processId.Value))
                    {
                        double fps = counter.SampleFps();
                        if (fps > 0)
                            values.Add(fps);
                    }
                    else
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Roblox process exited during measurement");
                        break;
                    }

                    Thread.Sleep(1000);
                }
            }, cancellationToken);

            try
            {
                if (counterUsable && values.Count >= MinReliableSamples && counter!.HasObservedFrames)
                {
                    values.Sort();
                    double median = values.Count % 2 == 1
                        ? values[values.Count / 2]
                        : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2.0;

                    sample.MedianFps = median;
                    sample.SampleCount = values.Count;
                    sample.Reliable = true;

                    App.Logger.WriteLine(LOG_IDENT, $"Measurement complete: median {median:F1} FPS ({values.Count} samples)");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Measurement insufficient: {values.Count} samples, ETW usable: {counterUsable}");
                }
            }
            finally
            {
                counter?.Dispose();
            }

            return sample;
        }

        private static bool IsRobloxProcessAlive(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Classify a before/after measurement pair. Performance is noisy, so a small FPS delta
        /// is never declared a success — it must exceed the relative threshold.
        /// </summary>
        public static SandboxTestResult Classify(SandboxFpsSample? before, SandboxFpsSample? after)
        {
            if (before is null || after is null)
                return SandboxTestResult.InsufficientData;

            if (!before.Reliable || !after.Reliable)
                return SandboxTestResult.Inconclusive;

            if (before.SampleCount < MinReliableSamples || after.SampleCount < MinReliableSamples)
                return SandboxTestResult.Inconclusive;

            if (before.MedianFps <= 0)
                return SandboxTestResult.Inconclusive;

            double delta = (after.MedianFps - before.MedianFps) / before.MedianFps;

            if (Math.Abs(delta) <= ChangeThreshold)
                return SandboxTestResult.Similar;

            return delta > 0 ? SandboxTestResult.Improved : SandboxTestResult.Degraded;
        }

        public static string ResultToLabel(SandboxTestResult result) => result switch
        {
            SandboxTestResult.Improved => "Improved",
            SandboxTestResult.Similar => "Similar",
            SandboxTestResult.Degraded => "Degraded",
            SandboxTestResult.Inconclusive => "Inconclusive",
            _ => "Insufficient data"
        };
    }
}
