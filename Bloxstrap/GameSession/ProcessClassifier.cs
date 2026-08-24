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
            "RobloxPlayerBeta", "RobloxStudioBeta", "RobloxCrashHandler",

            // ── Audio/hardware vendor service (v7.2.8) ─────────────────────────────
            // Bug nyata 8/19: headset mati suara selama Game Session aktif karena
            // RAVBg64/RAVCpl64 (Realtek Audio Background/Control Panel) ikut
            // ter-suspend — keduanya jalan di session user dan bukan Windows path,
            // jadi lolos guard lama (audiodg dilindungi, vendor service tidak).
            // Service sejati (mis. RtkAudioService64) memang berjalan di session 0
            // dan sudah aman via guard SessionId==0 — daftar ini menutup PROSES
            // PENDAMPING audio vendor yang hidup di session user:
            "RAVBg64", "RAVCpl64", "RAVCpl",                    // Realtek Audio Background / Control Panel
            "RtkAudioService64", "RtkAudUService64", "RtkAudUService", "RtkAudioService", // Realtek Audio Service
            "RtkNGUI64", "RtkNGUI",                             // Realtek HD Audio Manager (mic settings: noise cancel, echo cancel, boost)
            "RtkBtManServ",                                      // Realtek Bluetooth Manager Service
            "cxaudsvc",                                          // Conexant / Synaptics audio
            "NahimicSvc32", "NahimicSvc64", "NahimicSvc",         // Nahimic (MSI / HP / gaming laptop audio)

            // ── Bluetooth / USB audio agent (v7.3.0) ────────────────────────────────
            // Bug 8/23: mic tidak berfungsi di Roblox voice chat SETELAH sesi.
            // RtkNGUI64 (Realtek HD Audio Manager) jalan di session user dan handle
            // mic settings (noise cancellation, echo cancellation, boost). Ketika
            // di-suspend, audio driver timeout connection; ketika di-resume, proses
            // lanjut jalan tapi connection udah putus → mic pipeline broken.
            // Bluetooth/USB audio agent juga perlu dilindungi untuk headset wireless:
            "BthAudioAgent",                                     // Bluetooth Audio Agent
            "WsaAudioService",                                   // Windows Sonic / spatial audio

            // ── Per-user service umum (v7.2.8) ─────────────────────────────────
            // Windows 10+ service yang jalan DI SESSION USER (bukan session 0)
            // sehingga tidak tertangkap guard SessionId==0, dan TIDAK terdaftar di
            // SCM klasik (Win32_Service) sehingga lolos service-PID check. Keduanya
            // terbukti ter-suspend di semua sesi nyata dan berdampak buruk:
            //   - GameInputRedistService → controller game mati saat dimainkan
            //   - OneDrive.Sync.Service  → sinkronisasi OneDrive terganggu
            "GameInputRedistService", "OneDrive.Sync.Service"
        };

        public static ProcessClassification Classify(
            ProcessSnapshot snapshot,
            SecuritySoftwareDetector detector,
            int selfProcessId,
            int gameProcessId,
            GameSessionRule? rule,
            IReadOnlySet<int>? serviceProcessIds = null)
        {
            if (IsCritical(snapshot, detector, selfProcessId, gameProcessId, serviceProcessIds))
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
            int gameProcessId,
            IReadOnlySet<int>? serviceProcessIds = null)
        {
            if (IsAlwaysProtected(snapshot, detector, selfProcessId, gameProcessId, serviceProcessIds))
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
            int gameProcessId,
            IReadOnlySet<int>? serviceProcessIds = null)
        {
            if (snapshot.ProcessId == selfProcessId || snapshot.ProcessId == gameProcessId)
                return true;

            if (CriticalProcessNames.Contains(snapshot.ProcessName))
                return true;

            // Windows service (managed by SCM): system/vendor component, never safe
            // to suspend — even when the service runs in the user's session
            // (e.g. OneDrive.Sync.Service). Detected once per BeginSession via
            // ServiceProcessDetector (WMI Win32_Service) so any vendor is covered
            // without a static name list.
            if (serviceProcessIds is not null && serviceProcessIds.Contains(snapshot.ProcessId))
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
            int gameProcessId,
            IReadOnlySet<int>? serviceProcessIds = null)
        {
            if (IsCritical(snapshot, detector, selfProcessId, gameProcessId, serviceProcessIds))
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
