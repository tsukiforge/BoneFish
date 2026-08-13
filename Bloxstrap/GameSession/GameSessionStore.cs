using Bloxstrap.GameSession.Models;

namespace Bloxstrap.GameSession
{
    public sealed class GameSessionStore
    {
        private const string LOG_IDENT = "GameSession::Store";
        private const int MaxHistoryEntries = 20;

        private readonly string? _storageRoot;

        public GameSessionStore(string? storageRoot = null)
        {
            _storageRoot = storageRoot;
        }

        private string RootPath => _storageRoot ?? Path.Combine(Paths.Base, "GameSessions");
        private string ActivePath => Path.Combine(RootPath, "active.json");
        private string HistoryPath => Path.Combine(RootPath, "history.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public GameSessionRecord? ReadActive()
        {
            try
            {
                if (!File.Exists(ActivePath))
                    return null;

                return JsonSerializer.Deserialize<GameSessionRecord>(File.ReadAllText(ActivePath));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        public void WriteActive(GameSessionRecord session)
        {
            WriteAtomic(ActivePath, JsonSerializer.Serialize(session, JsonOptions));
        }

        public void ClearActive()
        {
            try
            {
                if (File.Exists(ActivePath))
                    File.Delete(ActivePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public IReadOnlyList<SessionSummary> ReadHistory()
        {
            try
            {
                if (!File.Exists(HistoryPath))
                    return Array.Empty<SessionSummary>();

                return JsonSerializer.Deserialize<List<SessionSummary>>(File.ReadAllText(HistoryPath))
                    ?? new List<SessionSummary>();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return Array.Empty<SessionSummary>();
            }
        }

        public void AppendHistory(SessionSummary summary)
        {
            var history = ReadHistory().ToList();
            history.Add(summary);

            if (history.Count > MaxHistoryEntries)
                history = history.Skip(history.Count - MaxHistoryEntries).ToList();

            WriteAtomic(HistoryPath, JsonSerializer.Serialize(history, JsonOptions));
        }

        private void WriteAtomic(string path, string contents)
        {
            Directory.CreateDirectory(RootPath);
            string temporaryPath = path + ".tmp";

            try
            {
                File.WriteAllText(temporaryPath, contents);
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // The atomic target is already valid; cleanup is best effort.
                }
            }
        }
    }
}
