using Bloxstrap.GameSession;
using Bloxstrap.GameSession.Models;

namespace Bloxstrap.Tests.GameSession;

public class ProcessClassifierTests
{
    private static readonly SecuritySoftwareDetector Detector = new();

    private static ProcessSnapshot Snapshot(int pid, string name, string? path = null) => new()
    {
        ProcessId = pid,
        ProcessName = name,
        ExecutablePath = path ?? Path.Combine(@"C:\Program Files\SomeVendor", name + ".exe"),
        StartTimeUtc = DateTime.UtcNow.AddMinutes(-5),
        SessionId = 1
    };

    [Theory]
    [InlineData("RAVBg64")]          // Realtek Audio Background
    [InlineData("RAVCpl64")]         // Realtek HD Audio Control Panel
    [InlineData("RAVCpl")]
    [InlineData("RtkAudioService64")]// Realtek Audio Service
    [InlineData("RtkAudUService64")]
    [InlineData("RtkAudUService")]
    [InlineData("RtkAudioService")]
    [InlineData("cxaudsvc")]         // Conexant / Synaptics
    [InlineData("NahimicSvc32")]     // Nahimic
    [InlineData("NahimicSvc64")]
    [InlineData("NahimicSvc")]
    [InlineData("GameInputRedistService")] // per-user service (controller input)
    [InlineData("OneDrive.Sync.Service")]  // per-user service (OneDrive sync)
    public void Audio_vendor_processes_are_always_protected(string processName)
    {
        var snapshot = Snapshot(1000, processName);

        Assert.True(ProcessClassifier.IsAlwaysProtected(snapshot, Detector, selfProcessId: 1, gameProcessId: 2));
    }

    [Fact]
    public void Windows_service_process_id_is_protected()
    {
        // A service running in the user's session whose name is NOT in the static
        // list must still be protected purely because its PID is registered in
        // Win32_Service. (OneDrive.Sync.Service was the real-world case; here we use
        // a synthetic vendor name so the test exercises the PID signal, not the
        // static name list.)
        var snapshot = Snapshot(7001, "CustomVendorSyncSvc");
        IReadOnlySet<int> serviceProcessIds = new HashSet<int> { 7001, 7002 };

        Assert.True(ProcessClassifier.IsAlwaysProtected(snapshot, Detector, 1, 2, serviceProcessIds));
    }

    [Fact]
    public void Non_service_approved_application_is_not_protected()
    {
        var snapshot = Snapshot(3000, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");

        Assert.False(ProcessClassifier.IsAlwaysProtected(snapshot, Detector, 1, 2, new HashSet<int> { 999 }));
    }

    [Fact]
    public void Unknown_identity_fails_closed_even_with_service_pids()
    {
        var snapshot = new ProcessSnapshot
        {
            ProcessId = 4000,
            ProcessName = "mystery",
            ExecutablePath = null,
            StartTimeUtc = null,
            SessionId = -1
        };

        Assert.True(ProcessClassifier.IsCritical(snapshot, Detector, 1, 2, new HashSet<int>()));
    }
}