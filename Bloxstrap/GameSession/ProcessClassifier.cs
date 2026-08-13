using Bloxstrap.GameSession.Models;

namespace Bloxstrap.GameSession
{
    public static class ProcessClassifier
    {
        private static readonly HashSet<string> AutomaticUserApplicationNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
            "spotify", "discord", "steam", "steamwebhelper", "epicgameslauncher",
            "battle.net", "riotclientservices", "slack", "teams", "telegram",
            "whatsapp", "vlc", "zoom", "notion", "obs64"
        };

        private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "MemCompression", "Registry", "smss", "csrss", "lsass",
            "services", "winlogon", "wininit", "svchost", "dwm", "explorer", "audiodg",
            "fontdrvhost", "spoolsv", "SearchIndexer", "SearchHost", "WmiPrvSE", "WmiApSrv",
            "RuntimeBroker", "sihost", "ctfmon", "conhost", "dllhost", "sppsvc", "MsMpEng",
            "MsSense", "NisSrv", "SecurityHealthService", "SecurityHealthSystray",
            "RobloxPlayerBeta", "RobloxStudioBeta", "RobloxCrashHandler"
        };

        public static ProcessClassification Classify(
            ProcessSnapshot snapshot,
            SecuritySoftwareDetector detector,
            int selfProcessId,
            int gameProcessId,
            GameSessionRule? rule)
        {
            if (IsCritical(snapshot, detector, selfProcessId, gameProcessId))
                return ProcessClassification.Critical;

            // Unapproved applications are visible to the UI but must remain untouched.
            if (rule is null || !rule.SuspendDuringGame)
                return ProcessClassification.Keep;

            // Any uncertainty in security detection is a hard safety stop. This deliberately
            // disables suspension for the session rather than risking an unknown security tool.
            if (detector.State != SecurityDetectionState.Ok)
                return ProcessClassification.Critical;

            // A readable identity is required before a rule can mutate another process.
            if (String.IsNullOrWhiteSpace(snapshot.ProcessName)
                || String.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                || !snapshot.StartTimeUtc.HasValue)
            {
                return ProcessClassification.Critical;
            }

            return ProcessClassification.Safe;
        }

        public static bool IsCritical(
            ProcessSnapshot snapshot,
            SecuritySoftwareDetector detector,
            int selfProcessId,
            int gameProcessId)
        {
            if (IsAlwaysProtected(snapshot, detector, selfProcessId, gameProcessId))
                return true;

            // Unknown identity is never safe to touch.
            return String.IsNullOrWhiteSpace(snapshot.ProcessName)
                || String.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                || !snapshot.StartTimeUtc.HasValue;
        }

        public static bool IsAlwaysProtected(
            ProcessSnapshot snapshot,
            SecuritySoftwareDetector detector,
            int selfProcessId,
            int gameProcessId)
        {
            if (snapshot.ProcessId == selfProcessId || snapshot.ProcessId == gameProcessId)
                return true;

            if (CriticalProcessNames.Contains(snapshot.ProcessName))
                return true;

            // Session 0 is reserved for services. An unavailable session ID is not
            // treated as safe here; the caller will fail closed on missing identity.
            if (snapshot.SessionId >= 0 && snapshot.SessionId == 0)
                return true;

            if (snapshot.SessionId < 0)
                return true;

            if (IsWindowsPath(snapshot.ExecutablePath))
                return true;

            if (detector.KnownSecurityProcessNames.Contains(snapshot.ProcessName))
                return true;

            if (!String.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                && detector.KnownSecurityExecutablePaths.Contains(snapshot.ExecutablePath))
            {
                return true;
            }

            try
            {
                if (String.Equals(snapshot.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            return false;
        }

        public static bool IsAutomaticCandidate(
            ProcessSnapshot snapshot,
            SecuritySoftwareDetector detector,
            int selfProcessId,
            int gameProcessId)
        {
            if (IsCritical(snapshot, detector, selfProcessId, gameProcessId))
                return false;

            if (snapshot.SessionId < 0 || snapshot.SessionId != Process.GetCurrentProcess().SessionId)
                return false;

            return AutomaticUserApplicationNames.Contains(snapshot.ProcessName);
        }

        private static bool IsWindowsPath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string windowsPath = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                string executablePath = Path.GetFullPath(path);
                return executablePath.StartsWith(windowsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(executablePath, windowsPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}
