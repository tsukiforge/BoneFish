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

    [Fact]
    public void Rescue_scan_resumes_orphaned_suspended_threads_across_processes()
    {
        var chrome = new FakeAccessor(new[] { new[] { 1, 2, 3 } });
        chrome.SuspendedThreads.UnionWith(new[] { 1, 2 });

        var explorer = new FakeAccessor(new[] { new[] { 4, 5 } });
        explorer.SuspendedThreads.UnionWith(new[] { 5 });

        var healthy = new FakeAccessor(new[] { new[] { 6 } });
        var accessors = new Dictionary<int, FakeAccessor>
        {
            [1] = chrome,
            [2] = explorer,
            [3] = healthy
        };

        var service = new ProcessSuspensionService(
            accessorFactory: processId => accessors[processId],
            processSource: () => new[]
            {
                new ProcessSnapshot { ProcessId = 1, ProcessName = "chrome" },
                new ProcessSnapshot { ProcessId = 2, ProcessName = "explorer" },
                new ProcessSnapshot { ProcessId = 3, ProcessName = "healthy" }
            });

        IReadOnlyList<RescuedProcess> rescued = service.RescueSuspendedProcesses();

        Assert.Equal(2, rescued.Count);
        Assert.Contains(rescued, item => item.ProcessId == 1 && item.ThreadCount == 2);
        Assert.Contains(rescued, item => item.ProcessId == 2 && item.ThreadCount == 1);
        Assert.Empty(chrome.SuspendedThreads);
        Assert.Empty(explorer.SuspendedThreads);
        Assert.Empty(healthy.SuspendedThreads);
        Assert.False(rescued.Any(item => item.ProcessId == 3));
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
