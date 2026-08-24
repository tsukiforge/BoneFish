using System.Management;
using Bloxstrap.GameSession.Models;

namespace Bloxstrap.GameSession
{
    /// <summary>
    /// Detects security software before any user-approved process can be touched.
    /// Any infrastructure failure is conservative: the caller must not suspend
    /// unverified processes while the detector is degraded or unavailable.
    /// </summary>
    public class SecuritySoftwareDetector
    {
        private const string LOG_IDENT = "GameSession::SecuritySoftwareDetector";
        private const int WmiTimeoutSeconds = 3;

        private static readonly Dictionary<string, string[]> ProductProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["defender"] = new[] { "MsMpEng", "MsSense", "NisSrv", "MsMpEngCP", "SecurityHealthService", "SecurityHealthSystray" },
            ["windows firewall"] = new[] { "svchost" },
            ["defender firewall"] = new[] { "svchost" },
            ["microsoft firewall"] = new[] { "svchost" },
            ["kaspersky"] = new[] { "avp", "kavsvc", "avpui" },
            ["avast"] = new[] { "avast", "aswidsagent", "aswSP" },
            ["avg"] = new[] { "avgnt", "AVGSvc", "avgsvca" },
            ["eset"] = new[] { "ekrn", "egui" },
            ["bitdefender"] = new[] { "bdredline", "vsserv", "bdagent" },
            ["norton"] = new[] { "ccSvcHst", "NortonSecurity" },
            ["symantec"] = new[] { "ccSvcHst", "rtvscan" },
            ["mcafee"] = new[] { "MfeSvc", "mfemms", "McAPExe" },
            ["malwarebytes"] = new[] { "MBAMService", "mbam", "MbamTray" },
            ["comodo"] = new[] { "cavwp", "cmdagent", "cis" },
            ["sophos"] = new[] { "SophosUI", "swi_service", "SSP" },
            ["trend micro"] = new[] { "PccNTMon", "TMBMSRV", "UmxAgent" },
            ["panda"] = new[] { "PSANHost", "PavPrsrv" },
            ["f-secure"] = new[] { "fsavg", "fsgk32st", "fssm32" },
            ["webroot"] = new[] { "WRSA", "WRCoreService" }
        };

        public SecurityDetectionState State { get; protected set; } = SecurityDetectionState.Unavailable;
        public string Message { get; protected set; } = "Security detection has not run.";
        public HashSet<string> KnownSecurityProcessNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KnownSecurityExecutablePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> DetectedProducts => _detectedProducts;

        private readonly List<string> _detectedProducts = new();
        private bool _hasUnmappedProduct;

        public virtual Task<SecurityDetectionState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Refresh(cancellationToken), cancellationToken);
        }

        public SecurityDetectionState Refresh(CancellationToken cancellationToken = default)
        {
            KnownSecurityProcessNames.Clear();
            KnownSecurityExecutablePaths.Clear();
            _detectedProducts.Clear();
            _hasUnmappedProduct = false;

            // These are known security components even if Security Center itself is unavailable.
            AddKnownNames(ProductProcessNames["defender"]);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                ServiceProbe securityCenter = QueryService("wscsvc");
                if (!securityCenter.Exists)
                    return SetUnavailable("Security Center service (wscsvc) was not found.");

                if (!securityCenter.IsRunning || securityCenter.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                    return SetUnavailable("Security Center service is stopped or disabled.");

                ServiceProbe defender = QueryService("WinDefend");
                bool defenderRunning = defender.Exists && defender.IsRunning;

                int successfulQueries = 0;
                int failedQueries = 0;
                bool requiredQueryFailed = false;
                QueryProducts("AntiVirusProduct", ref successfulQueries, ref failedQueries, cancellationToken, required: true, requiredQueryFailed: ref requiredQueryFailed);
                QueryProducts("FirewallProduct", ref successfulQueries, ref failedQueries, cancellationToken, required: false, requiredQueryFailed: ref requiredQueryFailed);
                // This class is absent on some Windows 11 builds; it is best effort.
                QueryProducts("AntiSpywareProduct", ref successfulQueries, ref failedQueries, cancellationToken, required: false, requiredQueryFailed: ref requiredQueryFailed);

                if (defenderRunning)
                    AddKnownNames(ProductProcessNames["defender"]);

                if (successfulQueries == 0)
                    return SetUnavailable("Security Center product queries were unavailable.");

                if (requiredQueryFailed)
                    return SetUnavailable("The required antivirus product query was unavailable.");

                if (_detectedProducts.Count == 0 && !defenderRunning)
                    return SetDegraded("Security Center returned no registered security product.");

                if (_hasUnmappedProduct)
                    return SetDegraded("A registered security product could not be mapped safely to its processes.");

                if (failedQueries > 0)
                    return SetDegraded("The required security product class could not be queried.");

                return SetState(SecurityDetectionState.Ok, "Security software detection completed.");
            }
            catch (OperationCanceledException)
            {
                return SetUnavailable("Security detection was cancelled before completion.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return SetUnavailable($"Security detection failed: {ex.Message}");
            }
        }

        private void QueryProducts(
            string className,
            ref int successfulQueries,
            ref int failedQueries,
            CancellationToken cancellationToken,
            bool required,
            ref bool requiredQueryFailed)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var scope = new ManagementScope(@"\\.\root\SecurityCenter2");
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery($"SELECT displayName, pathToSignedProductExe, pathToSignedReportingExe FROM {className}"),
                    new System.Management.EnumerationOptions
                    {
                        ReturnImmediately = false,
                        Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds)
                    });

                foreach (ManagementBaseObject item in searcher.Get())
                {
                    try
                    {
                        string productName = item["displayName"]?.ToString() ?? "";
                        if (String.IsNullOrWhiteSpace(productName))
                            continue;

                        _detectedProducts.Add(productName);
                        bool mappedByPath = AddExecutablePath(item["pathToSignedProductExe"]?.ToString())
                            | AddExecutablePath(item["pathToSignedReportingExe"]?.ToString());
                        if (!mappedByPath && !MapProductToProcesses(productName))
                            _hasUnmappedProduct = true;
                    }
                    finally
                    {
                        item.Dispose();
                    }
                }

                successfulQueries++;
            }
            catch (Exception ex)
            {
                if (required)
                {
                    failedQueries++;
                    requiredQueryFailed = true;
                }
                App.Logger.WriteLine(LOG_IDENT, $"{className} query failed: {ex.Message}");
            }
        }

        private bool MapProductToProcesses(string productName)
        {
            bool mapped = false;
            foreach ((string key, string[] names) in ProductProcessNames)
            {
                if (productName.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    AddKnownNames(names);
                    mapped = true;
                }
            }

            return mapped;
        }

        private bool AddExecutablePath(string? executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                return false;

            try
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(executablePath.Trim('"'));
                if (expandedPath.Contains("://", StringComparison.OrdinalIgnoreCase)
                    || !Path.IsPathFullyQualified(expandedPath))
                {
                    return false;
                }

                string fullPath = Path.GetFullPath(expandedPath);
                string processName = Path.GetFileNameWithoutExtension(fullPath);
                if (String.IsNullOrWhiteSpace(processName))
                    return false;

                KnownSecurityExecutablePaths.Add(fullPath);
                KnownSecurityProcessNames.Add(processName);
                return true;
            }
            catch
            {
                return false;
            }
        }


        private void AddKnownNames(IEnumerable<string> names)
        {
            foreach (string name in names)
                KnownSecurityProcessNames.Add(name);
        }

        private static ServiceProbe QueryService(string serviceName)
        {
            var scope = new ManagementScope(@"\\.\root\cimv2");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT Name, State, StartMode FROM Win32_Service"),
                new System.Management.EnumerationOptions
                {
                    ReturnImmediately = false,
                    Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds)
                });

            foreach (ManagementBaseObject item in searcher.Get())
            {
                try
                {
                    if (!String.Equals(item["Name"]?.ToString(), serviceName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return new ServiceProbe(
                        true,
                        String.Equals(item["State"]?.ToString(), "Running", StringComparison.OrdinalIgnoreCase),
                        item["StartMode"]?.ToString() ?? "");
                }
                finally
                {
                    item.Dispose();
                }
            }

            return new ServiceProbe(false, false, "");
        }

        private SecurityDetectionState SetUnavailable(string message)
        {
            return SetState(SecurityDetectionState.Unavailable, message);
        }

        private SecurityDetectionState SetDegraded(string message)
        {
            return SetState(SecurityDetectionState.Degraded, message);
        }

        private SecurityDetectionState SetState(SecurityDetectionState state, string message)
        {
            State = state;
            Message = message;
            App.Logger.WriteLine(LOG_IDENT, $"State={state}: {message}");
            return state;
        }

        private readonly record struct ServiceProbe(bool Exists, bool IsRunning, string StartMode);
    }
}
