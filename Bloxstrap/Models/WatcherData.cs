namespace Bloxstrap.Models
{
    internal class WatcherData
    {
        public int ProcessId { get; set; }

        public string? LogFile { get; set; }

        public List<int>? AutoclosePids { get; set; }

        public long Handle { get; set; }

        // Game join data untuk auto-reconnect setelah Roblox crash
        public long? PlaceId { get; set; }
        public string? JobId { get; set; }
    }
}
