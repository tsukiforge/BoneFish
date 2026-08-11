using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox.Interfaces
{
    /// <summary>
    /// Narrow abstraction over the FastFlag configuration the sandbox is allowed to touch.
    /// In production this wraps <c>App.FastFlags</c> (FastFlagManager); tests use a fake.
    /// </summary>
    public interface IFastFlagStore
    {
        string? GetValue(string key);

        void SetValue(string key, string? value);

        /// <summary>All currently known flags (name → value).</summary>
        Dictionary<string, string> GetAll();

        /// <summary>Raw content of the configuration file on disk, or null if it does not exist yet.</summary>
        string? ReadRawFileContent();

        /// <summary>Persist the current in-memory configuration to disk.</summary>
        void Save();
    }

    /// <summary>
    /// Thrown when a sandbox operation fails in a way that must be surfaced explicitly
    /// (snapshot failure, write failure, verification failure, corrupted data).
    /// </summary>
    public class SandboxException : Exception
    {
        public SandboxException(string message) : base(message) { }

        public SandboxException(string message, Exception inner) : base(message, inner) { }
    }
}

namespace Bloxstrap.Sandbox
{
    /// <summary>
    /// Production implementation backed by the existing BoneFish FastFlagManager.
    /// The sandbox deliberately does NOT implement its own FastFlag writing.
    /// </summary>
    public sealed class AppFastFlagStore : Interfaces.IFastFlagStore
    {
        private const string LOG_IDENT = "OptimizationSandbox::AppFastFlagStore";

        public string? GetValue(string key)
        {
            try
            {
                return App.FastFlags.GetValue(key);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        public void SetValue(string key, string? value)
        {
            try
            {
                App.FastFlags.SetValue(key, value);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                throw;
            }
        }

        public Dictionary<string, string> GetAll()
        {
            var result = new Dictionary<string, string>();
            try
            {
                foreach (var pair in App.FastFlags.Prop)
                {
                    if (pair.Value is not null)
                        result[pair.Key] = pair.Value.ToString()!;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            return result;
        }

        public string? ReadRawFileContent()
        {
            try
            {
                string path = App.FastFlags.FileLocation;
                if (!File.Exists(path))
                    return null;

                return SandboxStorage.ReadAllText(path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to read raw file content: {ex.Message}");
                return null;
            }
        }

        public void Save()
        {
            try
            {
                App.FastFlags.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                throw;
            }
        }
    }
}
