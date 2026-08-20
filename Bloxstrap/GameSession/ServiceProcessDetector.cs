using System.Management;

namespace Bloxstrap.GameSession
{
    /// <summary>
    /// Resolves the ProcessIds of all Windows services currently managed by the
    /// Service Control Manager (SCM). A process that runs as a service is a
    /// system/vendor component (audio stack, driver companion, sync service) —
    /// NOT an application that is safe to suspend, even when it runs in the
    /// user's session (e.g. OneDrive.Sync.Service).
    ///
    /// This is an ADDITIONAL signal for the classifier on top of the static
    /// name list: it protects any service regardless of vendor, so the fix
    /// does not go stale when vendors rename their executables. Fail-safe:
    /// when the WMI query fails it returns an empty set (the static
    /// CriticalProcessNames list still applies — no process is put at risk by
    /// a detector outage).
    /// </summary>
    public static class ServiceProcessDetector
    {
        private const string LOG_IDENT = "GameSession::ServiceProcessDetector";
        private const int WmiTimeoutSeconds = 3;

        /// <summary>
        /// Returns the ProcessId of every running Windows service.
        /// Never throws — failures degrade to an empty set (non-fatal).
        /// </summary>
        public static IReadOnlySet<int> GetServiceProcessIds(CancellationToken cancellationToken = default)
        {
            var processIds = new HashSet<int>();

            try
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery("SELECT ProcessId FROM Win32_Service"),
                    new System.Management.EnumerationOptions
                    {
                        ReturnImmediately = false,
                        Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds)
                    });

                foreach (ManagementBaseObject item in searcher.Get())
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        object? rawPid = item["ProcessId"];
                        if (rawPid is null || Convert.ToInt32(rawPid) == 0)
                            continue;

                        processIds.Add(Convert.ToInt32(rawPid));
                    }
                    finally
                    {
                        item.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Non-fatal: static audio/vendor name list still protects known processes.
                App.Logger.WriteLine(LOG_IDENT, $"Win32_Service query failed (non-fatal): {ex.Message}");
            }

            return processIds;
        }
    }
}