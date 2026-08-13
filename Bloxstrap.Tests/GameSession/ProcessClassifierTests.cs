using Bloxstrap.GameSession;
using Bloxstrap.GameSession.Models;
using System.Diagnostics;

namespace Bloxstrap.Tests.GameSession;

public class ProcessClassifierTests
{
    [Fact]
    public void System_process_is_always_critical()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var snapshot = Snapshot("csrss", 10);
        var rule = new GameSessionRule { ProcessName = "csrss", SuspendDuringGame = true };

        Assert.Equal(ProcessClassification.Critical, ProcessClassifier.Classify(snapshot, detector, 1, 2, rule));
    }

    [Fact]
    public void Security_process_is_critical_even_when_user_approves_it()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        detector.KnownSecurityProcessNames.Add("vendorav");
        var snapshot = Snapshot("vendorav", 10);
        var rule = new GameSessionRule { ProcessName = "vendorav", SuspendDuringGame = true };

        Assert.Equal(ProcessClassification.Critical, ProcessClassifier.Classify(snapshot, detector, 1, 2, rule));
    }

    [Fact]
    public void Detector_unavailable_is_fail_safe_critical()
    {
        var detector = new FixedDetector(SecurityDetectionState.Unavailable);
        var snapshot = Snapshot("Chrome", 10);
        var rule = new GameSessionRule { ProcessName = "Chrome", SuspendDuringGame = true };

        Assert.Equal(ProcessClassification.Critical, ProcessClassifier.Classify(snapshot, detector, 1, 2, rule));
    }

    [Fact]
    public void Normal_approved_process_is_safe_when_detector_is_healthy()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var snapshot = Snapshot("Chrome", 10);
        var rule = new GameSessionRule { ProcessName = "Chrome", SuspendDuringGame = true };

        Assert.Equal(ProcessClassification.Safe, ProcessClassifier.Classify(snapshot, detector, 1, 2, rule));
    }

    [Fact]
    public void Unapproved_process_is_keep_not_auto_suspended()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var snapshot = Snapshot("Spotify", 10);

        Assert.Equal(ProcessClassification.Keep, ProcessClassifier.Classify(snapshot, detector, 1, 2, null));
    }

    [Fact]
    public void Missing_identity_is_critical()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var snapshot = new ProcessSnapshot { ProcessId = 10, ProcessName = "Chrome" };
        var rule = new GameSessionRule { ProcessName = "Chrome", SuspendDuringGame = true };

        Assert.Equal(ProcessClassification.Critical, ProcessClassifier.Classify(snapshot, detector, 1, 2, rule));
    }

    [Fact]
    public void Windows_service_process_is_not_an_automatic_candidate()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var snapshot = Snapshot("svchost", 10);
        snapshot.SessionId = 0;

        Assert.False(ProcessClassifier.IsAutomaticCandidate(snapshot, detector, 1, 2));
    }

    [Fact]
    public void Approved_browser_can_be_an_automatic_candidate_only_when_safe()
    {
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var snapshot = Snapshot("chrome", 10);
        snapshot.SessionId = Process.GetCurrentProcess().SessionId;

        Assert.True(ProcessClassifier.IsAutomaticCandidate(snapshot, detector, 1, 2));
    }

    private static ProcessSnapshot Snapshot(string name, int id) => new()
    {
        ProcessId = id,
        SessionId = Process.GetCurrentProcess().SessionId,
        ProcessName = name,
        ExecutablePath = $@"C:\Apps\{name}.exe",
        StartTimeUtc = DateTime.UtcNow
    };

    private sealed class FixedDetector : SecuritySoftwareDetector
    {
        public FixedDetector(SecurityDetectionState state)
        {
            State = state;
            Message = state.ToString();
        }

        public override Task<SecurityDetectionState> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(State);
    }
}
