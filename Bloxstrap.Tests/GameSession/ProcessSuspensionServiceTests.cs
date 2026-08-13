using Bloxstrap.GameSession;
using Bloxstrap.GameSession.Models;

namespace Bloxstrap.Tests.GameSession;

public class ProcessSuspensionServiceTests
{
    [Fact]
    public void Sweep_has_hard_five_pass_limit_and_reports_partial_counts()
    {
        var accessor = new FakeAccessor(
            new[]
            {
                new[] { 1 },
                new[] { 1, 2 },
                new[] { 1, 2, 3 },
                new[] { 1, 2, 3, 4 },
                new[] { 1, 2, 3, 4, 5 },
                new[] { 1, 2, 3, 4, 5, 6 }
            });
        var service = new ProcessSuspensionService(_ => accessor);

        ProcessSuspendResult result = service.SuspendProcess(42);

        Assert.Equal(ProcessSuspensionService.MaxSweepPasses, result.SweepPasses);
        Assert.Equal(5, result.SuspendedThreadIds.Count);
        Assert.Equal(6, result.TotalThreadCount);
        Assert.True(result.PartiallySuspended);
    }

    [Fact]
    public void Failed_thread_is_reported_with_specific_numbers()
    {
        var accessor = new FakeAccessor(new[] { new[] { 1, 2 }, new[] { 1, 2 } })
        {
            FailedSuspendThreadId = 2
        };
        var service = new ProcessSuspensionService(_ => accessor);

        ProcessSuspendResult result = service.SuspendProcess(42);

        Assert.Single(result.SuspendedThreadIds);
        Assert.Equal(2, result.TotalThreadCount);
        Assert.Equal(1, result.FailedThreadCount);
        Assert.True(result.PartiallySuspended);
    }

    [Fact]
    public void Restore_resumes_recorded_threads_and_verifies_state()
    {
        var accessor = new FakeAccessor(new[] { new[] { 1, 2 } });
        accessor.SuspendedThreads.UnionWith(new[] { 1, 2 });
        var service = new ProcessSuspensionService(_ => accessor);
        var record = Record(1, 2);

        RestoreResult result = service.RestoreProcess(record);

        Assert.Equal(RestoreStatus.Restored, result.Status);
        Assert.Empty(accessor.SuspendedThreads);
    }

    [Fact]
    public void Restore_rejects_pid_reuse_by_start_time()
    {
        var accessor = new FakeAccessor(new[] { new[] { 1 } })
        {
            StartTimeUtc = DateTime.UtcNow.AddMinutes(10)
        };
        accessor.SuspendedThreads.Add(1);
        var service = new ProcessSuspensionService(_ => accessor);
        var record = Record(1, 1);

        RestoreResult result = service.RestoreProcess(record);

        Assert.Equal(RestoreStatus.IdentityMismatch, result.Status);
        Assert.NotEmpty(accessor.SuspendedThreads);
    }

    [Fact]
    public void Restore_reports_missing_process_without_throwing()
    {
        var service = new ProcessSuspensionService(_ => throw new ArgumentException("gone"));

        RestoreResult result = service.RestoreProcess(Record(1, 1));

        Assert.Equal(RestoreStatus.NotFound, result.Status);
    }

    private static SuspendedProcessRecord Record(params int[] threads) => new()
    {
        ProcessId = 42,
        ProcessName = "Chrome",
        ExecutablePath = @"C:\Apps\Chrome.exe",
        StartTimeUtc = DateTime.UtcNow,
        ThreadIds = threads.ToList(),
        TotalThreadCount = threads.Length,
        SuspendedThreadCount = threads.Length
    };

    private sealed class FakeAccessor : IProcessAccessor
    {
        private readonly IReadOnlyList<int[]> _threadSequences;
        private int _getThreadIdsCalls;

        public HashSet<int> SuspendedThreads { get; } = new();
        public int? FailedSuspendThreadId { get; set; }
        public DateTime? StartTimeUtc { get; set; } = DateTime.UtcNow;
        public int ProcessId => 42;
        public bool IsAlive { get; set; } = true;

        public FakeAccessor(IEnumerable<int[]> threadSequences)
        {
            _threadSequences = threadSequences.ToList();
        }

        public IReadOnlyCollection<int> GetThreadIds()
        {
            int index = Math.Min(_getThreadIdsCalls++, _threadSequences.Count - 1);
            return _threadSequences[index];
        }

        public bool TrySuspendThread(int threadId)
        {
            if (threadId == FailedSuspendThreadId)
                return false;

            SuspendedThreads.Add(threadId);
            return true;
        }

        public bool TryResumeThread(int threadId)
        {
            SuspendedThreads.Remove(threadId);
            return true;
        }

        public bool IsThreadSuspended(int threadId) => SuspendedThreads.Contains(threadId);
        public DateTime? GetStartTimeUtc() => StartTimeUtc;
        public long GetProcessorTimeTicks() => 1;
        public void Dispose() { }
    }
}
