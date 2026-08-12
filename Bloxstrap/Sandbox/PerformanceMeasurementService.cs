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
        /// Measure FPS, 1% low, RAM and CPU over <paramref name="duration"/> by sampling the
        /// Roblox process once per second. FPS comes from ETW (<see cref="RealFpsCounter"/>, the
        /// same source the FPS Monitor overlay uses); RAM/CPU come from the process itself, so they
        /// work even without administrator privileges. Returns a reliable sample only when ETW
        /// telemetry was available and enough FPS samples were collected.
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
            var ramSamples = new List<double>();
            var cpuSamples = new List<double>();
            RealFpsCounter? counter = null;
            bool counterUsable = false;

            await Task.Run(() =>
            {
                counter = new RealFpsCounter(processId.Value);

                if (!counter.Start())
                {
                    // FPS needs ETW (admin), but RAM/CPU are still measured from the process.
                    App.Logger.WriteLine(LOG_IDENT, "FPS telemetry unavailable: ETW requires administrator privileges (RAM/CPU still sampled)");
                }
                else
                {
                    counterUsable = true;
                }

                DateTime started = DateTime.UtcNow;
                TimeSpan? lastCpuTime = null;
                DateTime lastCpuTick = started;

                while (DateTime.UtcNow - started < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Process? proc = null;
                    try
                    {
                        proc = Process.GetProcessById(processId.Value);
                        if (proc.HasExited)
                        {
                            proc.Dispose();
                            proc = null;
                        }
                    }
                    catch
                    {
                        proc?.Dispose();
                        proc = null;
                    }

                    if (proc is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Roblox process exited during measurement");
                        break;
                    }

                    try
                    {
                        if (counterUsable)
                        {
                            double fps = counter.SampleFps();
                            if (fps > 0)
                                values.Add(fps);
                        }

                        // RAM: same working-set metric BoneFish already reports for Roblox elsewhere.
                        ramSamples.Add(proc.WorkingSet64 / 1024.0 / 1024.0);

                        // CPU: wall-clock-normalized delta of process CPU time, as % of all cores.
                        TimeSpan cpuTime = proc.TotalProcessorTime;
                        DateTime now = DateTime.UtcNow;
                        if (lastCpuTime is not null)
                        {
                            cpuSamples.Add(ComputeCpuPercent(
                                cpuTime - lastCpuTime.Value,
                                now - lastCpuTick,
                                Environment.ProcessorCount));
                        }
                        lastCpuTime = cpuTime;
                        lastCpuTick = now;
                    }
                    catch
                    {
                        // process exited mid-tick; non-fatal
                    }
                    finally
                    {
                        proc.Dispose();
                    }

                    Thread.Sleep(1000);
                }
            }, cancellationToken);

            try
            {
                // RAM/CPU are recorded whenever the process could be sampled, independent of ETW.
                if (ramSamples.Count > 0)
                    sample.AverageRamMB = Math.Round(ramSamples.Average(), 1);
                if (cpuSamples.Count > 0)
                    sample.AverageCpuPercent = Math.Round(cpuSamples.Average(), 1);
                sample.ProcessMetricsSampled = ramSamples.Count > 0 || cpuSamples.Count > 0;

                if (counterUsable && values.Count >= MinReliableSamples && counter!.HasObservedFrames)
                {
                    (double median, double p1Low) = ComputeFpsPercentiles(values);

                    sample.MedianFps = median;
                    sample.P1LowFps = p1Low;
                    sample.SampleCount = values.Count;
                    sample.Reliable = true;

                    App.Logger.WriteLine(LOG_IDENT,
                        $"Measurement complete: median {median:F1} FPS, 1% low {p1Low:F1}, " +
                        $"RAM {sample.AverageRamMB:F0} MB, CPU {sample.AverageCpuPercent:F0}% ({values.Count} samples)");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Measurement insufficient: {values.Count} FPS samples, ETW usable: {counterUsable}");
                }
            }
            finally
            {
                counter?.Dispose();
            }

            return sample;
        }

        /// <summary>
        /// Median and PresentMon-style 1% low from per-second FPS samples: the 1% low is the FPS
        /// value at the 1st percentile (equivalently the FPS of the 99th percentile frame time).
        /// </summary>
        public static (double Median, double P1Low) ComputeFpsPercentiles(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
                return (0, 0);

            var sorted = values.OrderBy(v => v).ToList();

            double median = sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;

            int p1Index = Math.Clamp((int)(sorted.Count * 0.01), 0, sorted.Count - 1);
            return (median, sorted[p1Index]);
        }

        /// <summary>Per-process CPU usage as a percentage of all logical processors.</summary>
        public static double ComputeCpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
        {
            if (wallDelta.TotalSeconds <= 0 || processorCount <= 0)
                return 0;
            return cpuDelta.TotalSeconds / wallDelta.TotalSeconds * 100.0 / processorCount;
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
