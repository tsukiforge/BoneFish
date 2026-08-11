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
}
