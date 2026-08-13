using Bloxstrap.GameSession;
using Bloxstrap.GameSession.Models;
using System.Diagnostics;

namespace Bloxstrap.Tests.GameSession;

public class GameSessionServiceTests
{
    [Fact]
    public async Task Begin_applies_only_approved_noncritical_processes()
    {
        string root = TempRoot();
        var detector = new FixedDetector(SecurityDetectionState.Ok);
        var accessor = new FakeAccessor();
        var suspension = new ProcessSuspensionService(_ => accessor);
        var chrome = Snapshot("Chrome", 100);
        var defender = Snapshot("MsMpEng", 101);
        var rules = new List<GameSessionRule>
        {
            new() { ProcessName = "Chrome", ExecutablePath = chrome.ExecutablePath, SuspendDuringGame = true },
            new() { ProcessName = "MsMpEng", ExecutablePath = defender.ExecutablePath, SuspendDuringGame = true }
        };
        var service = new GameSessionService(
            detector,
            suspension,
            new GameSessionStore(root),
            () => new[] { chrome, defender },
            _ => true,
            () => rules);

        try
        {
            GameSessionRecord session = await service.BeginSessionAsync();

            Assert.Single(session.SuspendedProcesses);
            Assert.Equal("Chrome", session.SuspendedProcesses[0].ProcessName);
            Assert.DoesNotContain(accessor.SuspendedPids, pid => pid == defender.ProcessId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Detector_unavailable_suspends_zero_processes()
    {
        string root = TempRoot();
        var detector = new FixedDetector(SecurityDetectionState.Unavailable);
        var accessor = new FakeAccessor();
        var process = Snapshot("Chrome", 100);
        var rules = new List<GameSessionRule>
        {
            new() { ProcessName = "Chrome", ExecutablePath = process.ExecutablePath, SuspendDuringGame = true }
        };
        var service = new GameSessionService(
            detector,
            new ProcessSuspensionService(_ => accessor),
            new GameSessionStore(root),
            () => new[] { process },
            _ => true,
            () => rules);

        try
        {
            GameSessionRecord session = await service.BeginSessionAsync();

            Assert.Empty(session.SuspendedProcesses);
            Assert.Empty(accessor.SuspendedPids);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task End_restores_and_is_idempotent()
    {
        string root = TempRoot();
        var accessor = new FakeAccessor();
        var process = Snapshot("Spotify", 100);
        var rules = new List<GameSessionRule>
        {
            new() { ProcessName = "Spotify", ExecutablePath = process.ExecutablePath, SuspendDuringGame = true }
        };
        var service = new GameSessionService(
            new FixedDetector(SecurityDetectionState.Ok),
            new ProcessSuspensionService(_ => accessor),
            new GameSessionStore(root),
            () => new[] { process },
            _ => true,
            () => rules);

        try
        {
            GameSessionRecord session = await service.BeginSessionAsync();
            service.AttachGameProcess(999);

            SessionSummary summary = service.EndSession(999);
            SessionSummary second = service.EndSession(999);

            Assert.Equal(1, summary.TotalSuspended);
            Assert.Equal(1, summary.RestoredCount);
            Assert.Empty(second.Results);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "BoneFishGameSessionTests", Guid.NewGuid().ToString("N"));

    private static ProcessSnapshot Snapshot(string name, int id) => new()
    {
        ProcessId = id,
        SessionId = Environment.ProcessId == 0 ? -1 : Process.GetCurrentProcess().SessionId,
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

    private sealed class FakeAccessor : IProcessAccessor
    {
        public HashSet<int> SuspendedPids { get; } = new();
        public HashSet<int> SuspendedThreads { get; } = new();
        public int ProcessId => 100;
        public bool IsAlive => true;

        public IReadOnlyCollection<int> GetThreadIds() => new[] { 1, 2 };
        public bool TrySuspendThread(int threadId)
        {
            SuspendedPids.Add(ProcessId);
            SuspendedThreads.Add(threadId);
            return true;
        }

        public bool TryResumeThread(int threadId)
        {
            SuspendedThreads.Remove(threadId);
            return true;
        }

        public bool IsThreadSuspended(int threadId) => SuspendedThreads.Contains(threadId);
        public DateTime? GetStartTimeUtc() => DateTime.UtcNow;
        public long GetProcessorTimeTicks() => 1;
        public void Dispose() { }
    }
}
