using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Models;
using Xunit;

namespace Bloxstrap.Tests;

public class ResultClassifierTests
{
    private static SandboxFpsSample Reliable(double medianFps, int count = 30) => new()
    {
        MedianFps = medianFps,
        SampleCount = count,
        Reliable = true
    };

    [Fact]
    public void Missing_Measurement_Is_Insufficient_Data()
    {
        Assert.Equal(SandboxTestResult.InsufficientData, PerformanceMeasurementService.Classify(null, null));
        Assert.Equal(SandboxTestResult.InsufficientData, PerformanceMeasurementService.Classify(null, Reliable(60)));
        Assert.Equal(SandboxTestResult.InsufficientData, PerformanceMeasurementService.Classify(Reliable(60), null));
    }

    [Fact]
    public void Unreliable_Telemetry_Is_Inconclusive()
    {
        var unreliable = new SandboxFpsSample { MedianFps = 60, SampleCount = 30, Reliable = false };

        Assert.Equal(SandboxTestResult.Inconclusive, PerformanceMeasurementService.Classify(unreliable, Reliable(70)));
        Assert.Equal(SandboxTestResult.Inconclusive, PerformanceMeasurementService.Classify(Reliable(60), unreliable));
    }

    [Fact]
    public void Too_Few_Samples_Is_Inconclusive()
    {
        var few = new SandboxFpsSample { MedianFps = 60, SampleCount = 10, Reliable = true };

        Assert.Equal(SandboxTestResult.Inconclusive, PerformanceMeasurementService.Classify(few, Reliable(70)));
    }

    [Fact]
    public void Zero_Baseline_Is_Inconclusive()
    {
        Assert.Equal(SandboxTestResult.Inconclusive, PerformanceMeasurementService.Classify(Reliable(0), Reliable(70)));
    }

    [Theory]
    [InlineData(100, 104, SandboxTestResult.Similar)]  // +4% — inside the 5% noise threshold
    [InlineData(100, 96, SandboxTestResult.Similar)]   // −4%
    [InlineData(100, 100, SandboxTestResult.Similar)]
    [InlineData(100, 110, SandboxTestResult.Improved)] // +10%
    [InlineData(100, 90, SandboxTestResult.Degraded)]  // −10%
    [InlineData(120, 150, SandboxTestResult.Improved)] // +25%
    [InlineData(60, 54, SandboxTestResult.Degraded)]   // −10%
    public void Classify_Applies_Relative_Threshold(double before, double after, SandboxTestResult expected)
    {
        var result = PerformanceMeasurementService.Classify(Reliable(before), Reliable(after));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SandboxTestResult.Improved, "Improved")]
    [InlineData(SandboxTestResult.Similar, "Similar")]
    [InlineData(SandboxTestResult.Degraded, "Degraded")]
    [InlineData(SandboxTestResult.Inconclusive, "Inconclusive")]
    [InlineData(SandboxTestResult.InsufficientData, "Insufficient data")]
    public void Result_To_Label_Mapping(SandboxTestResult result, string expected) =>
        Assert.Equal(expected, PerformanceMeasurementService.ResultToLabel(result));

    // ── 1% low / percentile helpers ────────────────────────────────────────────────────

    [Fact]
    public void Empty_Fps_List_Yields_Zero_Percentiles()
    {
        var (median, p1Low) = PerformanceMeasurementService.ComputeFpsPercentiles(Array.Empty<double>());
        Assert.Equal(0, median);
        Assert.Equal(0, p1Low);
    }

    [Fact]
    public void Median_Of_Odd_Count_Is_Middle_Value()
    {
        var (median, _) = PerformanceMeasurementService.ComputeFpsPercentiles(new[] { 10.0, 20.0, 30.0, 40.0, 50.0 });
        Assert.Equal(30, median);
    }

    [Fact]
    public void Median_Of_Even_Count_Is_Average_Of_Middles()
    {
        var (median, _) = PerformanceMeasurementService.ComputeFpsPercentiles(new[] { 10.0, 20.0, 30.0, 40.0 });
        Assert.Equal(25, median);
    }

    [Fact]
    public void P1_Low_Is_Bottom_Percentile_Of_Sorted_Fps()
    {
        // 100 samples: FPS 5..104 — the 1st percentile sits at index 1 of the ascending sort.
        // (1% of 100 = 1 sample, so the percentile value is the second-smallest entry.)
        var values = Enumerable.Range(5, 100).Select(v => (double)v).ToList();
        var (_, p1Low) = PerformanceMeasurementService.ComputeFpsPercentiles(values);
        Assert.Equal(6, p1Low);
    }

    [Fact]
    public void P1_Low_For_Small_Counts_Falls_Back_To_Minimum()
    {
        var (_, p1Low) = PerformanceMeasurementService.ComputeFpsPercentiles(new[] { 30.0, 45.0, 60.0 });
        Assert.Equal(30, p1Low);
    }

    [Theory]
    [InlineData(2.0, 1.0, 4, 50.0)]   // 2s CPU over 1s wall on 4 cores = 50%
    [InlineData(1.0, 2.0, 4, 12.5)]   // 1s CPU over 2s wall on 4 cores = 12.5%
    [InlineData(3.0, 1.0, 1, 300.0)]  // single core, 3x cpu time = 300%
    [InlineData(1.0, 0.0, 4, 0.0)]    // zero wall time must not divide by zero
    [InlineData(1.0, 1.0, 0, 0.0)]    // zero cores must not divide by zero
    public void Cpu_Percent_Normalizes_By_Wall_Time_And_Cores(double cpuSec, double wallSec, int cores, double expected)
    {
        Assert.Equal(expected, PerformanceMeasurementService.ComputeCpuPercent(TimeSpan.FromSeconds(cpuSec), TimeSpan.FromSeconds(wallSec), cores));
    }

    // ── Sample serialization (journal backward compatibility) ─────────────────────────

    [Fact]
    public void Sample_Json_Round_Trips_New_Fields()
    {
        var sample = new SandboxFpsSample
        {
            MedianFps = 120.5,
            P1LowFps = 95.0,
            SampleCount = 30,
            AverageRamMB = 2048.0,
            AverageCpuPercent = 42.0,
            Reliable = true
        };

        string json = System.Text.Json.JsonSerializer.Serialize(sample);
        var restored = System.Text.Json.JsonSerializer.Deserialize<SandboxFpsSample>(json);

        Assert.NotNull(restored);
        Assert.Equal(sample.MedianFps, restored!.MedianFps);
        Assert.Equal(sample.P1LowFps, restored.P1LowFps);
        Assert.Equal(sample.SampleCount, restored.SampleCount);
        Assert.Equal(sample.AverageRamMB, restored.AverageRamMB);
        Assert.Equal(sample.AverageCpuPercent, restored.AverageCpuPercent);
        Assert.Equal(sample.Reliable, restored.Reliable);
    }

    [Fact]
    public void Old_Journals_Without_New_Fields_Deserialize_With_Defaults()
    {
        // A journal written before the RAM/CPU/1% low fields existed must still load.
        string oldJson = "{\"MedianFps\":60,\"SampleCount\":30,\"Reliable\":true}";
        var restored = System.Text.Json.JsonSerializer.Deserialize<SandboxFpsSample>(oldJson);

        Assert.NotNull(restored);
        Assert.Equal(60, restored!.MedianFps);
        Assert.Equal(0, restored.P1LowFps);
        Assert.Equal(0, restored.AverageRamMB);
        Assert.Equal(0, restored.AverageCpuPercent);
        Assert.False(restored.ProcessMetricsSampled);
    }

    [Fact]
    public void Sampled_Zero_Cpu_Is_Distinct_From_Not_Sampled()
    {
        // A genuinely measured 0% CPU (e.g. Roblox minimized) must not be confused with
        // "never sampled" — display logic gates on the flag, not the value.
        var sampledZero = new SandboxFpsSample { AverageRamMB = 2048.0, AverageCpuPercent = 0.0, ProcessMetricsSampled = true };
        Assert.True(sampledZero.ProcessMetricsSampled);

        var neverSampled = new SandboxFpsSample();
        Assert.False(neverSampled.ProcessMetricsSampled);
    }
}
