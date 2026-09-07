namespace Bloxstrap
{
    // https://stackoverflow.com/a/53873141/11852173

    public class Logger
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private FileStream? _filestream;
        private Mutex? _mutex;

        public readonly List<string> History = new();
        public bool Initialized = false;
        public bool NoWriteMode = false;
        public string? FileLocation;

        public string AsDocument => String.Join('\n', History);

        public void Initialize(bool useTempDir = false)
        {
            const string LOG_IDENT = "Logger::Initialize";

            string directory = useTempDir ? Path.Combine(Paths.TempLogs) : Path.Combine(Paths.Base, "Logs");

            // Log filenames use LOCAL system time (DateTime.Now), not UTC, so the date
            // always matches the user's clock. One file per local day: later sessions
            // on the same day append to it instead of creating new files.
            string filename = $"{App.ProjectName}_{DateTime.Now:yyyyMMdd}.log";
            string location = Path.Combine(directory, filename);

            WriteLine(LOG_IDENT, $"Initializing at {location}");

            if (Initialized)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because logger is already initialized");
                return;
            }

            Directory.CreateDirectory(directory);

            try
            {
                // FileMode.Append creates the file on first run and appends on later runs,
                // so every session of the same local day shares one continuous log.
                // FileShare.ReadWrite lets multiple BoneFish processes (e.g. Watcher and
                // Settings running at the same time) write to the same file without
                // sharing violations; writes are serialized via a named mutex below.
                _filestream = File.Open(location, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

                // .NET's append writes are not atomic across processes, so serialize writers
                // of the same file with a named mutex (one per file path)
                string mutexName = $"{App.ProjectName}_log_{location.Replace('\\', '_').Replace(':', '_')}";
                _mutex = new Mutex(false, mutexName);
            }
            catch (IOException)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because the log file could not be opened for writing");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (NoWriteMode)
                    return;

                WriteLine(LOG_IDENT, $"Failed to initialize because Bloxstrap cannot write to {directory}");

                Frontend.ShowMessageBox(
                    String.Format(Strings.Logger_NoWriteMode, directory), 
                    System.Windows.MessageBoxImage.Warning, 
                    System.Windows.MessageBoxButton.OK
                );

                NoWriteMode = true;

                return;
            }
            

            Initialized = true;

            // mark the start of every new process so sessions stay readable within one shared file
            string sessionStart = $"===== New session started (PID {Environment.ProcessId}) =====";
            WriteToLog(sessionStart);

            if (History.Count > 0)
                WriteToLog(string.Join("\r\n", History));

            History.Add(sessionStart);

            WriteLine(LOG_IDENT, "Finished initializing!");

            FileLocation = location;

            // clean up any logs older than a week (dates are in local time, matching the
            // new date-based naming scheme; old per-session files fall back to write time)
            if (Paths.Initialized && Directory.Exists(Paths.Logs))
            {
                foreach (FileInfo log in new DirectoryInfo(Paths.Logs).GetFiles())
                {
                    if (GetLogDate(log).AddDays(7) > DateTime.Now)
                        continue;

                    WriteLine(LOG_IDENT, $"Cleaning up old log file '{log.Name}'");

                    try
                    {
                       log.Delete();
                    }
                    catch (Exception ex)
                    {
                        WriteLine(LOG_IDENT, "Failed to delete log!");
                        WriteException(LOG_IDENT, ex);
                    }
                }
            }
        }

        private static DateTime GetLogDate(FileInfo log)
        {
            string prefix = $"{App.ProjectName}_";

            if (log.Name.StartsWith(prefix) && log.Name.EndsWith(".log"))
            {
                string datePart = log.Name.Substring(prefix.Length, log.Name.Length - prefix.Length - ".log".Length);

                if (DateTime.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    return date;
            }

            return log.LastWriteTime;
        }

        private void WriteLine(string message)
        {
            // Log entries use LOCAL system time (DateTime.Now), not UTC, so timestamps
            // always reflect the user's clock with no offset.
            string timestamp = DateTime.Now.ToString("s");
            string outcon = $"{timestamp} {message}";
            string outlog = outcon.Replace(Paths.UserProfile, "%UserProfile%", StringComparison.InvariantCultureIgnoreCase);

            Debug.WriteLine(outcon);
            WriteToLog(outlog);

            History.Add(outlog);
        }

        public void WriteLine(string identifier, string message) => WriteLine($"[{identifier}] {message}");

        public void WriteException(string identifier, Exception ex)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            string hresult = "0x" + ex.HResult.ToString("X8");

            WriteLine($"[{identifier}] ({hresult}) {ex}");

            Thread.CurrentThread.CurrentUICulture = Locale.CurrentCulture;
        }

        private async void WriteToLog(string message)
        {
            if (!Initialized)
                return;

            try
            {
                await _semaphore.WaitAsync();

                // serialize with other BoneFish processes writing the same file;
                // Mutex has thread affinity, so no awaits between WaitOne and ReleaseMutex
                if (_mutex is not null)
                {
                    try
                    {
                        _mutex.WaitOne();
                    }
                    catch (AbandonedMutexException)
                    {
                        // a previous holder crashed mid-write; this thread now owns the mutex
                    }
                }

                try
                {
                    // FileMode.Append emulates append through a per-handle position that
                    // goes stale once another process writes, so re-sync to the real
                    // end of file before each write (all writers hold the mutex first)
                    _filestream!.Seek(0, SeekOrigin.End);

                    byte[] bytes = Encoding.UTF8.GetBytes($"{message}\r\n");
                    _filestream.Write(bytes, 0, bytes.Length);
                    _filestream.Flush();
                }
                finally
                {
                    _mutex?.ReleaseMutex();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
