using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;
using Xunit;

namespace Bloxstrap.Tests;

public class TestSandboxHarness : IDisposable
{
    public FakeFastFlagStore Store { get; } = new();

    public string StorageRoot { get; }

    public OptimizationSandboxService Service { get; }

    public List<string> PresetWrites { get; } = new();

    public TestSandboxHarness()
    {
        StorageRoot = Path.Combine(Path.GetTempPath(), "BoneFishTests", Guid.NewGuid().ToString("N"));
        Service = new OptimizationSandboxService(
            Store,
            presetWriter: name => PresetWrites.Add(name),
            robloxRunningCheck: () => false,
            storageRoot: StorageRoot);
    }

    public SandboxExperiment CreateExperiment(params SandboxChange[] changes) =>
        Service.Manager.CreateExperiment("Balanced", changes);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(StorageRoot))
                Directory.Delete(StorageRoot, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}

public static class SandboxTestHelpers
{
    public static SandboxChange Set(string name, string value) => new() { FlagName = name, NewValue = value };

    public static SandboxChange Remove(string name) => new() { FlagName = name, NewValue = null };
}
