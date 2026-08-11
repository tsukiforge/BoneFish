using Bloxstrap.Sandbox.Interfaces;

namespace Bloxstrap.Tests;

/// <summary>
/// In-memory IFastFlagStore used by tests. Mirrors FastFlagManager semantics:
/// flags are strings, null deletes, Save() persists to a dictionary.
/// </summary>
public class FakeFastFlagStore : IFastFlagStore
{
    private readonly Dictionary<string, string> _persisted = new();
    private readonly Dictionary<string, string> _memory = new();

    /// <summary>When set, the next Save() throws — used to simulate partial/failed writes.</summary>
    public Action? OnSaveFailure { get; set; }

    public string RawContent { get; set; } = "{}";

    public string? GetValue(string key) => _memory.TryGetValue(key, out string? v) ? v : null;

    public void SetValue(string key, string? value)
    {
        if (value is null)
            _memory.Remove(key);
        else
            _memory[key] = value;
    }

    public Dictionary<string, string> GetAll() => new(_memory);

    public string? ReadRawFileContent() => RawContent;

    public void Save()
    {
        OnSaveFailure?.Invoke();
        _persisted.Clear();
        foreach (var pair in _memory)
            _persisted[pair.Key] = pair.Value;
    }

    public Dictionary<string, string> Persisted => new(_persisted);
}
