using Bloxstrap.GameSession;
using Bloxstrap.GameSession.Models;

namespace Bloxstrap.Tests.GameSession;

public class GameSessionStoreTests
{
    [Fact]
    public void Active_and_history_records_round_trip_and_clear()
    {
        string root = Path.Combine(Path.GetTempPath(), "BoneFishGameSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameSessionStore(root);
            var session = new GameSessionRecord
            {
                GameProcessId = 123,
                AppliedRules = new List<string> { "Chrome" }
            };

            store.WriteActive(session);

            Assert.Equal(123, store.ReadActive()!.GameProcessId);

            var summary = new SessionSummary
            {
                SessionId = session.SessionId,
                TotalSuspended = 1,
                RestoredCount = 1
            };
            store.AppendHistory(summary);
            Assert.Single(store.ReadHistory());

            store.ClearActive();
            Assert.Null(store.ReadActive());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Corrupt_active_file_is_non_fatal()
    {
        string root = Path.Combine(Path.GetTempPath(), "BoneFishGameSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameSessionStore(root);
            store.WriteActive(new GameSessionRecord());
            File.WriteAllText(Path.Combine(root, "active.json"), "not json");

            Assert.Null(store.ReadActive());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
