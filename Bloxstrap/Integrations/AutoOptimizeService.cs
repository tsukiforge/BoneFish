using System;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Auto-optimize service untuk perangkat low-end
    /// Deteksi spesifikasi sistem dan apply optimasi otomatis
    /// </summary>
    internal static class AutoOptimizeService
    {
        private const string LOG_IDENT = "AutoOptimizeService";

        // ── P/Invoke untuk memory management ─────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

        // ── P/Invoke untuk HDD/SSD detection via DeviceIoControl ─────────────────────────
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // IOCTL_STORAGE_QUERY_PROPERTY = CTL_CODE(IOCTL_STORAGE_BASE, 0x0500, METHOD_BUFFERED, FILE_ANY_ACCESS)
        // = (0x2d << 16) | (0 << 14) | (0x0500 << 2) | 0 = 0x002D1400
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint StorageDeviceSeekPenaltyProperty = 7; // STORAGE_PROPERTY_ID (terverifikasi: 0=DeviceProperty … 7=SeekPenalty, 8=Trim)
        private const uint StorageDeviceTrimProperty = 8;        // STORAGE_PROPERTY_ID

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;
            public uint QueryType;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)]
            public byte IncursSeekPenalty; // BOOLEAN: 0 = no seek penalty (SSD hint), 1 = seek penalty (HDD)
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DEVICE_TRIM_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)]
            public byte TrimEnabled; // BOOLEAN: 1 = device mendukung TRIM (hint SSD)
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public enum SystemTier
        {
            HighEnd,         // 4+ cores, 16GB+ RAM
            MidRange,        // 4 cores, 8GB RAM
            LowEnd,          // 2 cores, 4-8GB RAM
            UltraLow,        // 2 cores, <4GB RAM
            ExtremePerformance  // override manual — "Potato Mode" paksa oleh user
        }

        private static ulong GetTotalPhysicalMemory()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
                return mem.ullTotalPhys;
            return 0;
        }

        // ── HDD/SSD Detection (v7.7.0) ──────────────────────────────────────────────
        // Detail lengkap di komentar blok DetectStorageType() (multi-source 3-state,
        // physical-disk mapping, validasi descriptor, konsistensi).
        //
        // ── Persistent cache (v7.3.0) ──────────────────────────────────────
        // Hasil + diagnostik disimpan ke %LocalAppData%\BoneFish\HardwareCache.json.
        // GetStorageType() memakai cache bila < 30 hari DAN drive sistem tidak berubah
        // (root path + volume serial) DAN versi detector sama. Unknown TIDAK di-cache.
        // Tombol "Deteksi Ulang Hardware" memanggil ForceRefreshHardwareCache()
        // untuk hapus cache + deteksi ulang penuh.
        //
        // AUDIT CheckAndApply() untuk kandidat cache serupa: DetectSystemTier()
        // (Environment.ProcessorCount + GlobalMemoryStatusEx) dan GetTotalPhysicalMemory()
        // adalah syscall kernel super ringan (±µs) — membaca cache dari disk justru
        // lebih lambat, jadi TIDAK ikut di-persist. Query IOCTL volume handle
        // satu-satunya yang worth di-cache lintas sesi.
        private const int HardwareCacheMaxAgeDays = 30;
        private static readonly string HardwareCachePath = Path.Combine(Paths.LocalAppData, App.ProjectName, "HardwareCache.json");

        // ★ FIX v7.7.0: DetectorVersion=4 — engine 3-state multi-source.
        //   v1: GENERIC_READ (selalu AccessDenied non-admin → fallback "HDD")
        //   v2: IOCTL volume-handle + fallback HDD (2-state, menebak)
        //   v3: IOCTL/WMI 2-state (masih menebak saat gagal)
        //   v4: physical-disk mapping + konsistensi multi-source → SSD/HDD/Unknown.
        // Cache versi lebih lama OTOMATIS di-invalidasi.
        private const int HardwareCacheVersion = 4;
        private const int StorageDetectorVersion = 4;

        public enum StorageMediaType { Ssd, Hdd, Unknown }

        /// <summary>
        /// Hasil deteksi storage + diagnostik lengkap (lihat GetStorageDiagnostics()).
        /// </summary>
        public sealed class StorageDetectionResult
        {
            public StorageMediaType Type { get; set; } = StorageMediaType.Unknown;
            public string Confidence { get; set; } = "None";   // High / Medium / Low / None
            public string PhysicalDiskIndex { get; set; } = "?";
            public string DiskModel { get; set; } = "";
            public string BusType { get; set; } = "";
            public string SystemDrive { get; set; } = "";
            public List<string> Sources { get; set; } = new();
            public List<string> Diagnostics { get; set; } = new();
            public string Reason { get; set; } = "";
        }

        public sealed class HardwareCacheEntry
        {
            public int Version { get; set; } = HardwareCacheVersion;
            public string StorageType { get; set; } = "";      // "SSD" / "HDD" — Unknown TIDAK di-cache
            public int DetectorVersion { get; set; } = StorageDetectorVersion;
            public string SystemDriveRoot { get; set; } = "";
            public uint VolumeSerial { get; set; }
            public int PhysicalDiskIndex { get; set; } = -1;
            public string DiskModel { get; set; } = "";
            public string BusType { get; set; } = "";
            public string Confidence { get; set; } = "";
            public List<string> Sources { get; set; } = new();
            public DateTime DetectedAtUtc { get; set; }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumeInformation(
            string lpRootPathName,
            StringBuilder? lpVolumeNameBuffer,
            int nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder? lpFileSystemNameBuffer,
            int nFileSystemNameSize);

        // Result 3-state: null = Unknown (tidak pernah di-cache sebagai jawaban)
        private static StorageMediaType? _storageTypeCached = null;
        private static StorageDetectionResult? _lastDetection = null;
        private static readonly object _storageLock = new();

        private static string GetSystemDriveRoot() =>
            Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        private static uint GetSystemVolumeSerial()
        {
            try
            {
                var nameBuffer = new StringBuilder(256);
                if (GetVolumeInformation(GetSystemDriveRoot(), nameBuffer, nameBuffer.Capacity,
                        out uint serial, out _, out _, null, 0))
                    return serial;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GetVolumeInformation failed: {ex.Message}");
            }
            return 0;
        }

        private static StorageMediaType? TryLoadStorageTypeFromCache()
        {
            try
            {
                if (!File.Exists(HardwareCachePath))
                    return null;

                var entry = JsonSerializer.Deserialize<HardwareCacheEntry>(File.ReadAllText(HardwareCachePath));
                if (entry is null)
                    return null;

                if (entry.Version != HardwareCacheVersion || entry.DetectorVersion != StorageDetectorVersion)
                {
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Persistent storage cache v{entry.Version}/detector v{entry.DetectorVersion} != v{HardwareCacheVersion}/v{StorageDetectorVersion} — re-detecting");
                    return null;
                }

                if (String.IsNullOrWhiteSpace(entry.StorageType))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Persistent storage cache has no usable result — re-detecting");
                    return null;
                }

                if ((DateTime.UtcNow - entry.DetectedAtUtc).TotalDays > HardwareCacheMaxAgeDays)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Persistent storage cache expired (>{HardwareCacheMaxAgeDays} days) — re-detecting");
                    return null;
                }

                if (!String.Equals(entry.SystemDriveRoot, GetSystemDriveRoot(), StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger.WriteLine(LOG_IDENT, "System drive root changed since cache write — re-detecting");
                    return null;
                }

                uint currentSerial = GetSystemVolumeSerial();
                if (currentSerial != 0 && entry.VolumeSerial != 0 && currentSerial != entry.VolumeSerial)
                {
                    App.Logger.WriteLine(LOG_IDENT, "System volume serial changed since cache write — re-detecting");
                    return null;
                }

                var type = ParseStorageType(entry.StorageType);
                App.Logger.WriteLine(LOG_IDENT,
                    $"Storage type from persistent cache: {entry.StorageType} (disk={entry.PhysicalDiskIndex}, model=\"{entry.DiskModel}\", sources=[{String.Join("+", entry.Sources)}])");
                return type;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read persistent storage cache: {ex.Message}");
                return null;
            }
        }

        private static StorageMediaType ParseStorageType(string value) => value switch
        {
            "SSD" => StorageMediaType.Ssd,
            "HDD" => StorageMediaType.Hdd,
            _ => StorageMediaType.Unknown
        };

        private static void SaveStorageTypeToCache(StorageMediaType type, StorageDetectionResult detection)
        {
            // Hard requirement: Unknown TIDAK pernah di-cache — selalu re-detect.
            if (type == StorageMediaType.Unknown)
                return;

            try
            {
                var entry = new HardwareCacheEntry
                {
                    Version = HardwareCacheVersion,
                    DetectorVersion = StorageDetectorVersion,
                    StorageType = type == StorageMediaType.Ssd ? "SSD" : "HDD",
                    SystemDriveRoot = GetSystemDriveRoot(),
                    VolumeSerial = GetSystemVolumeSerial(),
                    PhysicalDiskIndex = Int32.TryParse(detection.PhysicalDiskIndex, out int idx) ? idx : -1,
                    DiskModel = detection.DiskModel,
                    BusType = detection.BusType,
                    Confidence = detection.Confidence,
                    Sources = detection.Sources,
                    DetectedAtUtc = DateTime.UtcNow
                };

                Directory.CreateDirectory(Path.GetDirectoryName(HardwareCachePath)!);
                File.WriteAllText(HardwareCachePath, JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not save persistent storage cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Sumber kebenaran tunggal tipe storage. 3-state: Ssd / Hdd / Unknown.
        /// Hard requirement: detection gagal ATAU sumber saling kontradiksi
        /// → Unknown, TIDAK PERNAH menebak HDD/SSD.
        /// </summary>
        private static StorageMediaType GetStorageType()
        {
            if (_storageTypeCached.HasValue)
                return _storageTypeCached.Value;

            lock (_storageLock)
            {
                if (_storageTypeCached.HasValue)
                    return _storageTypeCached.Value;

                StorageMediaType? cached = TryLoadStorageTypeFromCache();
                if (cached is { } fromCache && fromCache != StorageMediaType.Unknown)
                {
                    _storageTypeCached = fromCache;
                    return fromCache;
                }

                var stopwatch = Stopwatch.StartNew();
                StorageDetectionResult detection = DetectStorageType();
                stopwatch.Stop();
                _lastDetection = detection;

                App.Logger.WriteLine(LOG_IDENT,
                    $"Storage detection finished in {stopwatch.ElapsedMilliseconds} ms — result={detection.Type}, confidence={detection.Confidence}, sources=[{String.Join("+", detection.Sources)}], reason={detection.Reason}");

                if (detection.Type == StorageMediaType.Unknown)
                {
                    // Unknown tidak di-cache sebagai jawaban; hapus cache lama supaya
                    // tidak ada hasil basi yang ditampilkan di mana pun.
                    try { File.Delete(HardwareCachePath); } catch { }
                    _storageTypeCached = StorageMediaType.Unknown;
                    return StorageMediaType.Unknown;
                }

                _storageTypeCached = detection.Type;
                SaveStorageTypeToCache(detection.Type, detection);
                return detection.Type;
            }
        }

        /// <summary>Diagnostics terakhir (untuk debug/log/panel diagnostik).</summary>
        public static StorageDetectionResult GetStorageDiagnostics()
        {
            if (_lastDetection is null)
                GetStorageType();
            return _lastDetection ?? new StorageDetectionResult();
        }

        public static void ForceRefreshHardwareCache()
        {
            lock (_storageLock)
            {
                _storageTypeCached = null;
                _lastDetection = null;
                try { File.Delete(HardwareCachePath); } catch { }
            }

            var type = GetStorageType();
            App.Logger.WriteLine(LOG_IDENT, $"Hardware detection manually refreshed: {type} (persistent cache re-written)");
        }

        // ── Storage detection engine v4 (FIX v7.7.0) — 3-state multi-source ─────
        // ROOT CAUSE HDD→SSD (kode v7.6.3):
        //   TryDetectViaSeekPenaltyIoctl() memperlakukan "query sukses" = "descriptor valid":
        //     isSsd = descriptor.IncursSeekPenalty == 0;
        //   Driver yang TIDAK support StorageDeviceSeekPenaltyProperty (mode IDE/RAID,
        //   virtual disk, beberapa USB bridge) tetap mengembalikan success dengan
        //   descriptor KOSONG (semua byte 0) → IncursSeekPenalty==0 dibaca
        //   "tidak ada seek penalty" → HDD DILAPORKAN SEBAGAI SSD. Descriptor
        //   Version/Size tidak pernah divalidasi, dan query dikirim ke handle VOLUME
        //   (bukan physical disk) sehingga jawabannya bisa bukan media fisik.
        // ROOT CAUSE SSD→HDD (versi pra-7.6.3): GENERIC_READ pada volume handle
        //   selalu AccessDenied non-admin → fallback "Assuming HDD".
        //
        // ENGINE v4 (hard requirement — tidak menebak):
        //   1. System volume → partition → PHYSICAL DISK yang tepat (bukan disk pertama).
        //   2. Multi-source dengan validasi ketat:
        //      S1 IOCTL DEVICE_SEEK_PENALTY_DESCRIPTOR pada handle PHYSICAL DISK;
        //         validasi Version/Size + IncursSeekPenalty ∈ {0,1}.
        //         IncursSeekPenalty=1 → HDD (bukti kuat); =0 → hint SSD LEMAH
        //         (tidak boleh jadi dasar SSD sendirian — lihat root cause di atas).
        //      S2 IOCTL DEVICE_TRIM_DESCRIPTOR (TrimEnabled=1 → hint SSD lemah).
        //      S3 WMI MSFT_PhysicalDisk.MediaType — 3=SSD (kuat), 4=HDD (kuat),
        //         0/missing = gagal (bukan jawaban). Sumber sama dipakai Task Manager.
        //      S4 BusType NVMe (17) → hint SSD lemah (diagnostik).
        //   3. Keputusan (terdokumentasi):
        //      - Bukti kuat berlawanan (S3 SSD vs S1 HDD, dsb.) → Unknown
        //      - ≥1 bukti kuat + semua sumber valid sepakat → SSD/HDD (High/Medium)
        //      - Hanya hint lemah: ≥2 hint sepakat → SSD (Medium); 1 hint → Unknown
        //      - Semua sumber gagal → Unknown (BUKAN HDD, BUKAN SSD)
        private static StorageDetectionResult DetectStorageType()
        {
            var result = new StorageDetectionResult
            {
                SystemDrive = GetSystemDriveRoot()
            };

            // 1) System volume → physical disk index
            int? diskIndex = MapSystemVolumeToPhysicalDisk(result);
            if (diskIndex is null)
            {
                result.Reason = "System volume could not be mapped to a physical disk";
                App.Logger.WriteLine(LOG_IDENT, $"Storage detection: {result.Reason} → Unknown");
                return result;
            }

            result.PhysicalDiskIndex = diskIndex.Value.ToString(CultureInfo.InvariantCulture);

            // 2) Kumpulkan sumber
            var votes = new List<(string Name, bool IsSsd, bool Strong)>();

            if (TrySeekPenaltyOnPhysicalDisk(diskIndex.Value, out bool seekPenalty, out string seekDiag))
            {
                result.Diagnostics.Add($"IOCTL SeekPenalty={seekPenalty} ({(seekPenalty ? "HDD hint (strong)" : "SSD hint (weak)")})");
                votes.Add(("StorageDevice", !seekPenalty, seekPenalty)); // seekPenalty=true → HDD kuat; false → SSD lemah
            }
            else
            {
                result.Diagnostics.Add($"IOCTL SeekPenalty: FAILED — {seekDiag}");
            }

            if (TryTrimOnPhysicalDisk(diskIndex.Value, out bool trimEnabled, out string trimDiag))
            {
                if (trimEnabled)
                {
                    result.Diagnostics.Add("IOCTL TrimEnabled=True (SSD hint, weak)");
                    votes.Add(("StorageDevice.Trim", true, false));
                }
                else
                {
                    result.Diagnostics.Add("IOCTL TrimEnabled=False (no information)");
                }
            }
            else
            {
                result.Diagnostics.Add($"IOCTL Trim: FAILED — {trimDiag}");
            }

            if (TryQueryWmiPhysicalDisk(diskIndex.Value, out ushort mediaType, out ushort busType, out string model, out string wmiDiag))
            {
                result.DiskModel = model;
                result.BusType = busType switch
                {
                    1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE1394", 5 => "SSA",
                    6 => "FibreChannel", 7 => "USB", 8 => "RAID", 9 => "iSCSI",
                    10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC",
                    14 => "Virtual", 15 => "FileBackedVirtual", 16 => "StorageSpaces", 17 => "NVMe",
                    _ => $"BusType{busType}"
                };
                result.Diagnostics.Add($"WMI MSFT_PhysicalDisk: Model=\"{model}\", BusType={result.BusType}, MediaType={mediaType}");

                switch (mediaType)
                {
                    case 3:
                        votes.Add(("MSFT_PhysicalDisk", true, true));
                        break;
                    case 4:
                        votes.Add(("MSFT_PhysicalDisk", false, true));
                        break;
                    default:
                        result.Diagnostics.Add($"WMI MediaType={mediaType} (unspecified — no information)");
                        break;
                }

                if (busType == 17) // NVMe — selalu solid-state
                    votes.Add(("BusType.NVMe", true, false));
            }
            else
            {
                result.Diagnostics.Add($"WMI MSFT_PhysicalDisk: FAILED — {wmiDiag}");
            }

            // 3) Konsistensi & keputusan
            DecideStorageType(votes, result);
            return result;
        }

        private static void DecideStorageType(List<(string Name, bool IsSsd, bool Strong)> votes, StorageDetectionResult result)
        {
            var strongSsd = votes.Where(v => v.Strong && v.IsSsd).ToList();
            var strongHdd = votes.Where(v => v.Strong && !v.IsSsd).ToList();
            var weakSsd   = votes.Where(v => !v.Strong && v.IsSsd).ToList();
            var weakHdd   = votes.Where(v => !v.Strong && !v.IsSsd).ToList();

            result.Sources = votes.Select(v => v.Name).ToList();

            if (votes.Count == 0)
            {
                result.Type = StorageMediaType.Unknown;
                result.Confidence = "None";
                result.Reason = "Detection failed: no source returned a validated result";
            }
            else if (strongSsd.Count > 0 && strongHdd.Count > 0)
            {
                result.Type = StorageMediaType.Unknown;
                result.Confidence = "None";
                result.Reason = $"Conflicting strong storage media indicators: {String.Join(", ", strongSsd.Select(v => v.Name))} → SSD vs {String.Join(", ", strongHdd.Select(v => v.Name))} → HDD";
            }
            else if (strongHdd.Count > 0 && weakSsd.Count == 0)
            {
                result.Type = StorageMediaType.Hdd;
                result.Confidence = strongHdd.Count >= 2 ? "High" : "Medium";
                result.Reason = $"Strong HDD evidence ({String.Join(", ", strongHdd.Select(v => v.Name))}) with no contradicting indicator";
            }
            else if (strongSsd.Count > 0 && weakHdd.Count == 0)
            {
                result.Type = StorageMediaType.Ssd;
                result.Confidence = "High";
                result.Reason = $"Strong SSD evidence ({String.Join(", ", strongSsd.Select(v => v.Name))}) with no contradicting indicator";
            }
            else if (weakHdd.Count > 0 && strongSsd.Count == 0 && strongHdd.Count == 0 && weakSsd.Count == 0)
            {
                // Hint HDD lemah saja tidak ada di engine ini (trim=0 dianggap no-info);
                // tetap ditangani defensif.
                result.Type = StorageMediaType.Unknown;
                result.Confidence = "Low";
                result.Reason = "Only weak HDD hints available — insufficient evidence";
            }
            else if (weakSsd.Count >= 2 && strongSsd.Count == 0 && strongHdd.Count == 0 && weakHdd.Count == 0)
            {
                result.Type = StorageMediaType.Ssd;
                result.Confidence = "Medium";
                result.Reason = $"Multiple corroborating weak SSD hints ({String.Join(", ", weakSsd.Select(v => v.Name))})";
            }
            else
            {
                result.Type = StorageMediaType.Unknown;
                result.Confidence = "Low";
                result.Reason = "Insufficient or contradictory storage media evidence";
            }

            App.Logger.WriteLine(LOG_IDENT,
                $"Storage decision: {result.Type} (confidence={result.Confidence}) — {result.Reason}");
        }

        /// <summary>
        /// System volume (mis. C:) → Win32_LogicalDiskToPartition → nomor physical disk.
        /// Menjamin yang dideteksi adalah DISK TEMPAT WINDOWS BERADA, bukan disk pertama.
        /// </summary>
        private static int? MapSystemVolumeToPhysicalDisk(StorageDetectionResult result)
        {
            try
            {
                string driveLetter = GetSystemDriveRoot().TrimEnd('\\'); // "C:"

                using var assocSearcher = new ManagementObjectSearcher(
                    "root\\cimv2",
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in assocSearcher.Get().Cast<ManagementObject>())
                {
                    using var partitionObj = partition;
                    string partitionDeviceId = partitionObj["DeviceId"]?.ToString() ?? "";
                    if (String.IsNullOrWhiteSpace(partitionDeviceId))
                        continue;

                    int diskIndex = GetDiskIndexFromPartitionId(partitionDeviceId);
                    if (diskIndex >= 0)
                    {
                        result.Diagnostics.Add($"Mapping: {driveLetter} → partition \"{partitionDeviceId}\" → PhysicalDrive{diskIndex}");
                        return diskIndex;
                    }
                }

                result.Diagnostics.Add($"Mapping: no partition found for {driveLetter}");
                return null;
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add($"Mapping: FAILED — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// S1: DEVICE_SEEK_PENALTY_DESCRIPTOR pada handle PHYSICAL DISK (akses-0).
        /// VALIDASI KETAT: Version >= 1, Size >= sizeof(struct), IncursSeekPenalty
        /// harus 0 atau 1. Driver yang tidak mendukung properti ini sering
        /// mengembalikan success dengan descriptor kosong — TANPA validasi itu,
        /// IncursSeekPenalty==0 membuat HDD terbaca sebagai SSD
        /// (root cause salah klasifikasi HDD→SSD pada engine v2/v3).
        /// Return false = TIDAK ADA INFORMASI (bukan jawaban SSD/HDD).
        /// </summary>
        private static bool TrySeekPenaltyOnPhysicalDisk(int diskIndex, out bool incursSeekPenalty, out string diagnostic)
        {
            incursSeekPenalty = false;
            diagnostic = "";

            try
            {
                IntPtr handle = OpenPhysicalDiskHandle(diskIndex);
                if (handle == INVALID_HANDLE_VALUE)
                {
                    diagnostic = $"cannot open PhysicalDrive{diskIndex.ToString(CultureInfo.InvariantCulture)}";
                    return false;
                }

                try
                {
                    var query = new STORAGE_PROPERTY_QUERY
                    {
                        PropertyId = StorageDeviceSeekPenaltyProperty,
                        QueryType = 0 // PropertyStandardQuery
                    };

                    int querySize = Marshal.SizeOf(typeof(STORAGE_PROPERTY_QUERY));
                    int descSize = Marshal.SizeOf(typeof(DEVICE_SEEK_PENALTY_DESCRIPTOR));

                    IntPtr queryPtr = Marshal.AllocHGlobal(querySize);
                    IntPtr descPtr = Marshal.AllocHGlobal(descSize);
                    try
                    {
                        Marshal.StructureToPtr(query, queryPtr, false);
                        Marshal.Copy(new byte[descSize], 0, descPtr, descSize);

                        bool success = DeviceIoControl(
                            handle,
                            IOCTL_STORAGE_QUERY_PROPERTY,
                            queryPtr, (uint)querySize,
                            descPtr, (uint)descSize,
                            out _,
                            IntPtr.Zero);

                        if (!success)
                        {
                            diagnostic = $"DeviceIoControl failed (error={Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture)})";
                            return false;
                        }

                        var descriptor = Marshal.PtrToStructure<DEVICE_SEEK_PENALTY_DESCRIPTOR>(descPtr);

                        if (descriptor.Version < 1 || descriptor.Size < (uint)descSize)
                        {
                            diagnostic = $"descriptor invalid (Version={descriptor.Version.ToString(CultureInfo.InvariantCulture)}, Size={descriptor.Size.ToString(CultureInfo.InvariantCulture)}) — property not supported";
                            return false;
                        }

                        if (descriptor.IncursSeekPenalty > 1)
                        {
                            diagnostic = $"IncursSeekPenalty={descriptor.IncursSeekPenalty.ToString(CultureInfo.InvariantCulture)} out of range — not supported";
                            return false;
                        }

                        incursSeekPenalty = descriptor.IncursSeekPenalty == 1;
                        diagnostic = "validated";
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(queryPtr);
                        Marshal.FreeHGlobal(descPtr);
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch (Exception ex)
            {
                diagnostic = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// S2: DEVICE_TRIM_DESCRIPTOR pada handle physical disk.
        /// TrimEnabled=true → hint SSD lemah (OS memberi tahu device mendukung TRIM).
        /// TrimEnabled=false = TIDAK ADA INFORMASI (bukan bukti HDD).
        /// Return false = tidak ada informasi.
        /// </summary>
        private static bool TryTrimOnPhysicalDisk(int diskIndex, out bool trimEnabled, out string diagnostic)
        {
            trimEnabled = false;
            diagnostic = "";

            try
            {
                IntPtr handle = OpenPhysicalDiskHandle(diskIndex);
                if (handle == INVALID_HANDLE_VALUE)
                {
                    diagnostic = $"cannot open PhysicalDrive{diskIndex.ToString(CultureInfo.InvariantCulture)}";
                    return false;
                }

                try
                {
                    var query = new STORAGE_PROPERTY_QUERY
                    {
                        PropertyId = StorageDeviceTrimProperty,
                        QueryType = 0
                    };

                    int querySize = Marshal.SizeOf(typeof(STORAGE_PROPERTY_QUERY));
                    int descSize = Marshal.SizeOf(typeof(DEVICE_TRIM_DESCRIPTOR));

                    IntPtr queryPtr = Marshal.AllocHGlobal(querySize);
                    IntPtr descPtr = Marshal.AllocHGlobal(descSize);
                    try
                    {
                        Marshal.StructureToPtr(query, queryPtr, false);
                        Marshal.Copy(new byte[descSize], 0, descPtr, descSize);

                        bool success = DeviceIoControl(
                            handle,
                            IOCTL_STORAGE_QUERY_PROPERTY,
                            queryPtr, (uint)querySize,
                            descPtr, (uint)descSize,
                            out _,
                            IntPtr.Zero);

                        if (!success)
                        {
                            diagnostic = $"DeviceIoControl failed (error={Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture)})";
                            return false;
                        }

                        var descriptor = Marshal.PtrToStructure<DEVICE_TRIM_DESCRIPTOR>(descPtr);

                        if (descriptor.Version < 1 || descriptor.Size < (uint)descSize)
                        {
                            diagnostic = $"descriptor invalid (Version={descriptor.Version.ToString(CultureInfo.InvariantCulture)}, Size={descriptor.Size.ToString(CultureInfo.InvariantCulture)})";
                            return false;
                        }

                        trimEnabled = descriptor.TrimEnabled != 0;
                        diagnostic = "validated";
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(queryPtr);
                        Marshal.FreeHGlobal(descPtr);
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch (Exception ex)
            {
                diagnostic = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// S3: WMI MSFT_PhysicalDisk (root\\Microsoft\\Windows\\Storage, Win8+) untuk
        /// physical disk index TERTENTU — sumber data yang sama dipakai kolom
        /// "Media type" Task Manager. MediaType: 3=SSD, 4=HDD, 0/other=unspecified
        /// (unspecified = tidak ada informasi, BUKAN jawaban).
        /// </summary>
        private static bool TryQueryWmiPhysicalDisk(int diskIndex, out ushort mediaType, out ushort busType, out string model, out string diagnostic)
        {
            mediaType = 0;
            busType = 0;
            model = "";
            diagnostic = "";

            try
            {
                var scope = new ManagementScope("root\\Microsoft\\Windows\\Storage");
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery($"SELECT DeviceId, MediaType, BusType, Model, FriendlyDescription FROM MSFT_PhysicalDisk WHERE DeviceId='{diskIndex.ToString(CultureInfo.InvariantCulture)}'"));

                foreach (ManagementObject disk in searcher.Get().Cast<ManagementObject>())
                {
                    using var diskObj = disk;
                    mediaType = diskObj["MediaType"] is null ? (ushort)0 : Convert.ToUInt16(diskObj["MediaType"]);
                    busType = diskObj["BusType"] is null ? (ushort)0 : Convert.ToUInt16(diskObj["BusType"]);
                    model = diskObj["Model"]?.ToString() ?? diskObj["FriendlyDescription"]?.ToString() ?? "";
                    diagnostic = "ok";
                    return true;
                }

                diagnostic = $"MSFT_PhysicalDisk DeviceId={diskIndex.ToString(CultureInfo.InvariantCulture)} not found";
                return false;
            }
            catch (Exception ex)
            {
                diagnostic = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Buka handle akses-0 ke physical disk (BUKAN volume). Handle volume
        /// menjawab lewat layer partisi/volume dan bisa tidak merepresentasikan
        /// media fisik; physical disk handle menjawab dari disk driver langsung.
        /// Akses-0 cukup untuk IOCTL_STORAGE_QUERY_PROPERTY dan bekerja tanpa admin.
        /// </summary>
        private static IntPtr OpenPhysicalDiskHandle(int diskIndex)
        {
            string devicePath = $"\\\\.\\PhysicalDrive{diskIndex.ToString(CultureInfo.InvariantCulture)}";
            IntPtr handle = CreateFile(
                devicePath,
                0, // akses-0: cukup untuk query properti
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE)
                App.Logger.WriteLine(LOG_IDENT, $"Could not open {devicePath} (error={Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture)})");

            return handle;
        }

        private static int GetDiskIndexFromPartitionId(string partitionDeviceId)
        {
            // Format: "Disk #0, Partition #1" (locale-dependent separator, tapi
            // prefix "Disk #N" stabil di semua locale Windows).
            try
            {
                const string prefix = "Disk #";
                int start = partitionDeviceId.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    return -1;

                start += prefix.Length;
                int end = start;
                while (end < partitionDeviceId.Length && Char.IsDigit(partitionDeviceId[end]))
                    end++;

                if (end == start)
                    return -1;

                return Int32.Parse(partitionDeviceId[start..end], CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }

        private static SystemTier DetectSystemTier(bool ignoreForceExtreme = false)
        {
            try
            {
                // v7.0.5: param ignoreForceExtreme=true dipakai caller yang butuh tier ASLI
                // hardware (tanpa override ForceExtremeMode) — lihat CheckAndApply() dan
                // GetSystemInfo().
                if (!ignoreForceExtreme && App.Settings.Prop.ForceExtremeMode)
                    return SystemTier.ExtremePerformance;

                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemMB = totalMemBytes / (1024UL * 1024);

                if (cpuCores >= 4 && totalMemMB >= 15600)
                    return SystemTier.HighEnd;
                if (cpuCores >= 4 && totalMemMB >= 7800)
                    return SystemTier.MidRange;
                if (totalMemMB < 3800)
                    return SystemTier.UltraLow;
                return SystemTier.LowEnd;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error detecting system tier: {ex.Message}");
                return SystemTier.MidRange;
            }
        }

        private static bool UserHasManualPreset()
        {
            string preset = App.Settings.Prop.SelectedPerformancePreset ?? "None";
            return preset is "UltraLow" or "Balanced" or "Stable" or "ExtremePerformance";
        }

        public static bool CheckAndApply()
        {
            try
            {
                // ── v7.0.5: tier ASLI vs tier EFEKTIF ──────────────────────────────
                // Bug (sebelum fix): DetectSystemTier() punya short-circuit
                //   if (ForceExtremeMode) return ExtremePerformance;
                // sehingga tier asli device (HDD + LowEnd/MidRange) tidak pernah sampai
                // ke pengecekan HDD Balanced, dan cabang OptimizeForLowEnd di bawah
                // return duluan — ApplyHDDBalancedOptimizations() TIDAK PERNAH terpanggil
                // saat ForceExtremeMode aktif → semua tweak I/O HDD hilang, digantikan
                // mode Extreme generik yang buta terhadap bottleneck disk.
                // Sekarang: tier asli dihitung terpisah (ignoreForceExtreme) dan dipakai
                // untuk memutuskan kombinasi Extreme+HDD.
                SystemTier trueTier = DetectSystemTier(ignoreForceExtreme: true);
                SystemTier tier = DetectSystemTier();
                bool isExtreme = tier == SystemTier.ExtremePerformance;
                bool shouldOptimize = isExtreme || tier == SystemTier.LowEnd || tier == SystemTier.UltraLow;

                if (shouldOptimize && !App.Settings.Prop.OptimizeForLowEnd)
                {
                    App.Settings.Prop.OptimizeForLowEnd = true;
                    try { App.Settings.Save(); } catch { }

                    string tierName = tier switch
                    {
                        SystemTier.ExtremePerformance => "Extreme Performance / Potato Mode (Override Manual)",
                        SystemTier.UltraLow           => "Ultra Low-End (Sangat Lambat)",
                        SystemTier.LowEnd             => "Low-End (Lambat)",
                        _                             => "Unknown"
                    };

                    App.Logger.WriteLine(LOG_IDENT, $"System tier detected: {tierName}. OptimizeForLowEnd enabled.");
                    if (trueTier != tier)
                        App.Logger.WriteLine(LOG_IDENT, $"Original hardware tier: {trueTier} (hidden by ForceExtremeMode)");
                }

                if (App.Settings.Prop.OptimizeForLowEnd)
                {
                    // v7.0.5: ForceExtreme + HDD + tier asli LowEnd/MidRange → GABUNGAN
                    // Extreme & HDD tweaks ("Extreme Mode sadar HDD"), bukan saling
                    // menggantikan. hddIoTweaks=true diarahkan ke ApplyAggressiveOptimizations
                    // yang sudah ada (reuse, bukan reimplement) — nilai 250/jobs=2 dll.
                    // bypassLowEndGuard=true hanya untuk kasus combo: bukti dari log device
                    // nyata — user memakai ForceExtremeMode TAPI guard UserHasManualPreset()
                    // di ApplyAggressiveOptimizations membuat auto-optimize di-skip total
                    // saat boot (ClientAppSettings.json hanya berisi 4 flag dasar). Karena
                    // toggle ForceExtremeMode ADALAH ekspresi intent user untuk extreme,
                    // combo tidak boleh diam-diam di-skip oleh guard preset manual.
                    bool hddCombo = App.Settings.Prop.ForceExtremeMode
                        && GetStorageType() == StorageMediaType.Hdd
                        && (trueTier == SystemTier.LowEnd || trueTier == SystemTier.MidRange);

                    if (hddCombo)
                        App.Logger.WriteLine(LOG_IDENT,
                            $"ForceExtreme + HDD combo: tier asli {trueTier}, tier efektif ExtremePerformance — applying Extreme + HDD tweaks (bypass manual-preset guard)");

                    ApplyAggressiveOptimizations(
                        tier: tier,
                        hddIoTweaks: hddCombo,
                        bypassLowEndGuard: hddCombo);
                    return true;
                }

                // v7.7.0: Unknown → optimasi HDD TIDAK diterapkan (tidak ada bukti).
                // Ini keputusan aman: HDD tweaks menurunkan agresivitas I/O; menerapkannya
                // pada SSD tidak merusak, tapi TIDAK menerapkannya pada HDD asli hanya
                // kehilangan sedikit tuning — sedangkan SALAH klasifikasi jauh lebih buruk.
                if (GetStorageType() == StorageMediaType.Hdd
                    && (tier == SystemTier.LowEnd || tier == SystemTier.MidRange) && !UserHasManualPreset())
                {
                    App.Logger.WriteLine(LOG_IDENT, $"HDD detected + {tier} tier — applying HDD Balanced optimizations");
                    ApplyHDDBalancedOptimizations();
                    return true;
                }

                return RemoveOptimizations();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error in CheckAndApply: {ex.Message}");
                return false;
            }
            finally
            {
                // ── TDR Mitigation (toggle) — chokepoint boot ──────────────────────
                // ★ Re-apply PALING AKHIR lewat finally: berlaku untuk SEMUA path
                // (aggressive, HDD Balanced, RemoveOptimizations, early return),
                // jadi apa pun yang dijalankan path lain, nilai TDR menang pada
                // launch berikutnya. Priority: HIGHEST (sama seperti Fast Loading).
                if (App.Settings.Prop.EnableTdrMitigation)
                {
                    try { ApplyTdrMitigationFlags(); } catch { }
                    App.Logger.WriteLine(LOG_IDENT, "TDR Mitigation re-applied after CheckAndApply (priority: HIGHEST)");
                }

                // ── Manual FastFlag toggles — chokepoint boot ─────────────────────
                // ★ FIX: DisableRobloxAnimations/EnableLowMemoryMode disimpan sebagai
                // bool terpisah di Settings (bukan dibaca dari FastFlags saat itu).
                // Setiap Play, PurgeAllKnownFlags()/RemoveOptimizations() menghapus
                // keempat flag-nya dari AllKnownManagedFlags dan tidak ada yang
                // me-re-apply → toggle manual "hilang" tiap main. Re-apply di sini
                // (pola SAMA dengan TDR Mitigation di atas) supaya manual flags
                // survive apa pun path yang dijalankan.
                if (App.Settings.Prop.DisableRobloxAnimations)
                {
                    try { ApplyDisableRobloxAnimations(); } catch { }
                    App.Logger.WriteLine(LOG_IDENT, "DisableRobloxAnimations re-applied after CheckAndApply (priority: HIGHEST)");
                }
                if (App.Settings.Prop.EnableLowMemoryMode)
                {
                    try { ApplyLowMemoryMode(); } catch { }
                    App.Logger.WriteLine(LOG_IDENT, "EnableLowMemoryMode re-applied after CheckAndApply (priority: HIGHEST)");
                }
            }
        }

        public static string GetSystemInfo()
        {
            try
            {
                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemMB = totalMemBytes / (1024UL * 1024);
                // v7.0.5: tampilkan DUA tier — asli (tanpa override ForceExtremeMode) dan
                // efektif (dengan override) — supaya user tidak bingung device sebenarnya
                // saat Force Extreme Mode menyembunyikan tier asli.
                SystemTier trueTier = DetectSystemTier(ignoreForceExtreme: true);
                SystemTier effectiveTier = DetectSystemTier();
                string storageType = GetStorageType() switch
                {
                    StorageMediaType.Ssd => "SSD",
                    StorageMediaType.Hdd => "HDD",
                    _ => "Unknown"
                };

                string tierInfo = trueTier == effectiveTier
                    ? $"Tier: {trueTier}"
                    : $"Tier Asli: {trueTier}, Tier Efektif: {effectiveTier} (ForceExtremeMode aktif)";

                return $"CPU Cores: {cpuCores}, RAM: {totalMemMB}MB ({totalMemMB/1024}GB), Storage: {storageType}, {tierInfo}";
            }
            catch
            {
                return "System info unavailable";
            }
        }

        public static void ApplyAggressiveOptimizations(
            SystemTier? tier = null,
            bool hddIoTweaks = false,
            bool bypassLowEndGuard = false)
        {
            try
            {
                if (!bypassLowEndGuard)
                {
                    if (!App.Settings.Prop.OptimizeForLowEnd)
                        return;

                    if (UserHasManualPreset())
                    {
                        App.Logger.WriteLine(LOG_IDENT, "User has manual preset — skipping aggressive overrides to respect user choice.");
                        return;
                    }
                }

tier ??= DetectSystemTier();
            bool isExtreme = tier == SystemTier.ExtremePerformance;
            bool isUltraOrExtreme = tier == SystemTier.UltraLow || isExtreme;

            // ── Konflik kombinasi (v7.x): HDD + Extreme ─────────────────────────────
            // ★ FIX: Saat ForceExtremeMode aktif, DetectSystemTier() mengembalikan
            // ExtremePerformance SEPENUHNYA terlepas dari hardware — akibatnya
            // ApplyHDDBalancedOptimizations() TIDAK PERNAH dipanggil (CheckAndApply
            // return dulu di cabang OptimizeForLowEnd). Agar preset HDD tetap "hidup"
            // saat dieksekusi: relatif terhadap HDD di sini (hddIoTweaks juga diset
            // dari sina berlaku) sehingga kombinasi Extreme+HDD menghasilkan LOD &
            // compositor yang BENAR untuk disk lambat, bukan nilai "generic Extreme".
            bool isHDD = GetStorageType() == StorageMediaType.Hdd;

            PurgeAllKnownFlags();

                // ── Rombak v7.2.7: flag render paksa DIBUANG ───────────────────────
                // ★ Audit layar-putih 8/18/2026 (Intel HD 1GB, driver 20.19.15.5126):
                //   DFIntDebugFRMQualityLevelOverride=3 + DFIntTextureQualityOverride=0
                //   (dengan FFlagTextureQualityOverrideEnabled) + FIntTextureCompositorLowResFactor=1
                //   terbukti merusak render di iGPU tua (ClientMemStatus Error, white screen).
                //   Selain itu DFIntTaskSchedulerTargetFps TIDAK di allowlist sejak 2025-09-29
                //   (client mengabaikannya) dan FIntRenderGrainScale di-deny client 0.734.
                //   Keempatnya TIDAK PERNAH ditulis lagi; nilai lama tetap dibersihkan
                //   oleh PurgeAllKnownFlags() via AllKnownManagedFlags.
                App.FastFlags.SetValue("FIntRomarkStartWithGraphicQualityLevel", "1");

                App.FastFlags.SetValue("FIntRobloxGuiBlurIntensity", "0");

                App.FastFlags.SetValue("FFlagDebugSSAOForce", "False");
                App.FastFlags.SetValue("FIntSSAOMipLevels", "0");

                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance",       "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL12",    "250");
                // ── Konflik kombinasi Extreme + HDD pada LOD ────────────────────────
                // Nilai Extreme 500/750 membuat geometri TETAP high-poly lebih jauh
                // (switch LOD semakin jauh) = beban render lebih berat = FPS turun di
                // perangkat 'potato' (bukti: LANGKAH 0 user — Extreme ON 36-40 FPS vs
                // OFF 50-60). Saat dieksekusi bersamaan dengan HDD/LowEnd, LOD dipertahankan
                // 250 (paling agresif) karena preset HDD sendiri TIDAK pernah jalan
                // (CheckAndApply return duluan di cabang OptimizeForLowEnd).
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL23",    isExtreme && !isHDD ? "500" : "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34",    isExtreme && !isHDD ? "750" : "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceStatic", "0");
                App.FastFlags.SetValue("DFIntCSGv2LodsToGenerate", "0");

                App.FastFlags.SetValue("FIntTerrainArraySliceSize", "0");

                App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");

                // DFIntMaxFrameBufferSize=4 (frame buffer terlalu kecil → artefak render/
                // layar putih di iGPU tua) dan FIntRuntimeMaxNumOfThreads=4 (thread render
                // dibatasi tanpa manfaat terukur) DIBUANG di rombak v7.2.7 — tidak pernah
                // ditulis lagi. FIntRuntimeMaxNumOfThreads hanya ditulis oleh toggle Fast
                // Loading (6 jika cpuCores >= 8).
                App.FastFlags.SetValue("DFFlagEnableRequestAsyncCompression", "True");

                // DFIntTaskSchedulerTargetFps: TIDAK ditulis lagi sejak rombak v7.2.7 —
                // tidak ada di allowlist sejak 2025-09-29 (client modern mengabaikannya;
                // pengganti: GlobalBasicSettings_13.xml FramerateCap). FPS cap manual
                // tetap bisa di-set user lewat pengaturan FramerateCap di Roblox.

                // ── ANR audit (v7.x) ❌ DIPERTAHANKAN ───────────────────────────────
                // DFIntMaxActiveAnimationTracks, FIntRenderLocalLightFadeInMs, dan 7 flag
                // telemetry (FFlagDebugDisableTelemetry*) TIDAK TERDAFTAR di Roblox Fast
                // Flag Allowlist resmi yang aktif sejak 29 September 2025 (devforum thread
                // 3966569; juga repo LeventGameing/allowlist) — client MODERN MENGABAIKAN
                // flag ini (silent no-op). Tidak ada lagi SetValue untuk flag tersebut;
                // nilai lama tetap dibersihkan lewat AllKnownManagedFlags saat purge.

                if (isUltraOrExtreme)
                {
                    // FIntRenderLocalLightUpdatesMax/Min: DIBUANG di rombak v7.2.7 —
                    // di-deny client 0.734 ("Denied local configuration"), menulisnya
                    // sia-sia. Extreme+HDD → jobs=2 (selaras HDD Balanced); Extreme murni → 1
                    App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", isHDD ? "2" : "1");

                    App.Settings.Prop.EnableFpsMonitor = false;
                    App.Settings.Prop.EnableRobloxNotifications = false;
                    try { App.Settings.Save(); } catch { }

                    string label = isExtreme ? "ExtremePerformance (Potato Mode)" : "UltraLow";
                    if (isExtreme && hddIoTweaks)
                        label += " + HDD tweaks (HDD-aware combo)";
                    App.Logger.WriteLine(LOG_IDENT, $"Aggressive optimizations applied for {label}");
                }
                else if (hddIoTweaks)
                {
                    // HDD-specific I/O tweaks (only applied on top of LowEnd base, never Ultra/Extreme)
                    App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "2");

                    App.Logger.WriteLine(LOG_IDENT, "Rendering optimizations applied for low-end (with HDD I/O tweaks)");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "Rendering optimizations applied for low-end");
                }

// ── Network Optimizations ("sekelas NASA") ─────────────────────────
                // ★ FIX: PurgeAllKnownFlags() di awal method ini MENGHAPUS flag network
                // (FIntRakNetPacketRateLimit, DFIntMaxReceivePPS, DFIntMaxSendPPS,
                // DFIntConnectionMTUSize, DFIntOptimizeSendQueue), tapi sebelumnya TIDAK
                // pernah di-apply ulang di path low-end auto. Akibatnya user LowEnd/UltraLow
                // yang tidak pilih preset manual justru KEHILANGAN optimasi jaringan —
                // padahal preset manual (UltraLow, Balanced, dst) selalu memakainya.
                // Sekarang semua path low-end (auto, HDD Balanced, Turbo Mode) juga
                // mendapat network boost yang sama.
                ApplyNetworkOptimizations();
                App.Logger.WriteLine(LOG_IDENT, "Network optimizations applied (low-end path)");

                // ── Konflik urutan / Fast Loading (toggle) ──────────────────────────
                // ★ FIX: ApplyFastLoadingFlags() hanya dipanggil dari toggle UI, TIDAK
                // pernah dari boot. Di relaunch, PurgeAllKnownFlags() + nilai di atas
                // menghapus/menimpa flag-nya (DFIntTextureCompositorActiveJobs — dan
                // FIntRuntimeMaxNumOfThreads sebelum rombak v7.2.7) — toggle Fast
                // Loading mati diam-diam.
                // Re-apply PALING AKHIR agar kombinasi ini menang apa pun preset lain.
                if (App.Settings.Prop.EnableFastLoadingFlags)
                {
                    try { ApplyFastLoadingFlags(); } catch { }
                    App.Logger.WriteLine(LOG_IDENT, "FastLoading re-applied after aggressive path (priority: HIGHEST)");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying aggressive optimizations: {ex.Message}");
            }
        }

        // HDD-path delegates ke ApplyAggressiveOptimizations supaya tidak ada duplikasi flag.
        // bypassLowEndGuard=true karena caller (CheckAndApply) memanggil ini justru saat OptimizeForLowEnd masih FALSE
        // (HDD + LowEnd/MidRange tier, !OptimizeForLowEnd, !UserHasManualPreset()).
        // hddIoTweaks=true menyebabkan tambahan 3 flag HDD-specific di akhir apply base:
        //   DFIntTextureCompositorActiveJobs=2 (vs UltraLow=1 vs LowEnd=unset)
        //   FIntRenderLocalLightUpdatesMax=4, FIntRenderLocalLightUpdatesMin=2 (seperti UltraOrExtreme tapi tanpa side-effect FPS/Notifications)
        public static void ApplyHDDBalancedOptimizations()
        {
            try
            {
                App.Logger.WriteLine(LOG_IDENT, "HDD Balanced optimizations applied (LowEnd base + HDD I/O tweaks)");
                ApplyAggressiveOptimizations(
                    tier: SystemTier.LowEnd,
                    hddIoTweaks: true,
                    bypassLowEndGuard: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying HDD Balanced optimizations: {ex.Message}");
            }
        }

        private static readonly string[] AllKnownManagedFlags =
        {
            "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "FIntTextureCompositorLowResFactor", "DFIntTextureCompositorActiveJobs",
            "DFIntDebugFRMQualityLevelOverride", "FIntRomarkStartWithGraphicQualityLevel",
            "FIntRenderShadowIntensity", "DFFlagDebugPauseVoxelizer", "FIntCSGVoxelizerFadeRadius",
            "DFFlagDebugRenderForceTechnologyVoxel", "FFlagNewLightAttenuation", "FFlagFastGPULightCulling3",
            "FFlagDebugSkyGray", "FFlagDisablePostFx", "FFlagDebugSSAOForce", "FIntSSAOMipLevels", "FIntRobloxGuiBlurIntensity", "FIntRenderGrainScale",
            "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntRenderGrassDetailStrands", "FIntRenderGrassHeightScaler", "FFlagGlobalWindActivated",
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34", "DFIntCSGLevelOfDetailSwitchingDistanceStatic", "DFIntCSGv2LodsToGenerate", "DFIntDebugRestrictGCDistance",
            "FIntTerrainArraySliceSize",
            "DFIntAnimationLodFacsDistanceMin", "DFIntAnimationLodFacsDistanceMax", "DFIntAnimationLodFacsVisibilityDenominator",
            "FIntMaxBatchesPerFlush", "DFIntMaxFrameBufferSize", "FIntRuntimeMaxNumOfThreads", "DFFlagEnableRequestAsyncCompression",
            "DFIntTaskSchedulerTargetFps",
            "FIntRenderLocalLightUpdatesMax", "FIntRenderLocalLightUpdatesMin", "FIntRenderLocalLightFadeInMs",
            "DFIntMaxActiveAnimationTracks",
            "FFlagDebugDisableTelemetryEphemeralCounter", "FFlagDebugDisableTelemetryEphemeralStat", "FFlagDebugDisableTelemetryEventIngest",
            "FFlagDebugDisableTelemetryPoint", "FFlagDebugDisableTelemetryV2Counter", "FFlagDebugDisableTelemetryV2Event", "FFlagDebugDisableTelemetryV2Stat",
            "FFlagRenderUIAnimations", "FFlagRenderMenuTransitions", "FFlagRenderInventoryEffects",
            "FFlagLuaAppEnableLowMemoryMode",
            "FIntRakNetPacketRateLimit", "DFIntMaxReceivePPS", "DFIntMaxSendPPS", "DFIntConnectionMTUSize", "DFIntOptimizeSendQueue",
            "FFlagDebugDisplayFPS",
            "FIntDebugForceMSAASamples",
        };

        public static void PurgeAllKnownFlags()
        {
            foreach (string flag in AllKnownManagedFlags)
                App.FastFlags.SetValue(flag, null);
            App.Logger.WriteLine(LOG_IDENT, $"Purged {AllKnownManagedFlags.Length} known managed flags");
        }

        private static readonly string[] ManagedFlags =
        {
            "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "FIntTextureCompositorLowResFactor",
            "DFIntDebugFRMQualityLevelOverride", "FIntRomarkStartWithGraphicQualityLevel",
            "FIntRenderShadowIntensity", "DFFlagDebugPauseVoxelizer", "FIntCSGVoxelizerFadeRadius",
            "FFlagFastGPULightCulling3", "FFlagNewLightAttenuation",
            "FFlagDebugSSAOForce", "FIntSSAOMipLevels", "FIntRobloxGuiBlurIntensity", "FIntRenderGrainScale",
            "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntRenderGrassDetailStrands", "FIntRenderGrassHeightScaler", "FFlagGlobalWindActivated",
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34", "DFIntCSGv2LodsToGenerate",
            "FIntTerrainArraySliceSize",
            "FIntMaxBatchesPerFlush",
            "DFIntTaskSchedulerTargetFps",
            "FIntRenderLocalLightUpdatesMax", "FIntRenderLocalLightUpdatesMin",
            "DFIntTextureCompositorActiveJobs",
            "DFIntMaxActiveAnimationTracks", "FIntRenderLocalLightFadeInMs",
            "FFlagDebugDisableTelemetryEphemeralCounter", "FFlagDebugDisableTelemetryEphemeralStat", "FFlagDebugDisableTelemetryEventIngest",
            "FFlagDebugDisableTelemetryPoint", "FFlagDebugDisableTelemetryV2Counter", "FFlagDebugDisableTelemetryV2Event", "FFlagDebugDisableTelemetryV2Stat",
            "FFlagRenderUIAnimations", "FFlagRenderMenuTransitions", "FFlagRenderInventoryEffects",
            "FFlagLuaAppEnableLowMemoryMode",
            "FIntRakNetPacketRateLimit", "DFIntMaxReceivePPS", "DFIntMaxSendPPS", "DFIntConnectionMTUSize", "DFIntOptimizeSendQueue",
            "FFlagDebugDisplayFPS",
            "FIntDebugForceMSAASamples",
        };

        public static void CleanupLegacyRobloxFlags()
        {
            try
            {
                int totalCleanedFiles = 0;

                string robloxVersionsDir = Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
                if (Directory.Exists(robloxVersionsDir))
                {
                    foreach (string versionDir in Directory.GetDirectories(robloxVersionsDir, "version-*"))
                    {
                        string clientSettingsPath = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                        if (CleanupClientAppSettings(clientSettingsPath))
                            totalCleanedFiles++;
                    }
                }

                string bonefishModPath = Path.Combine(Paths.Modifications, "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(bonefishModPath) && CleanupClientAppSettings(bonefishModPath))
                    totalCleanedFiles++;

                string bonefishVersionPath = Path.Combine(Paths.Base, "Versions", "WindowsPlayer", "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(bonefishVersionPath) && CleanupClientAppSettings(bonefishVersionPath))
                    totalCleanedFiles++;

                if (totalCleanedFiles > 0)
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Legacy flag cleanup done: {totalCleanedFiles} file(s) cleaned from all paths");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"CleanupLegacyRobloxFlags failed (non-fatal): {ex.Message}");
            }
        }

        private static bool CleanupClientAppSettings(string clientSettingsPath)
        {
            try
            {
                if (!File.Exists(clientSettingsPath))
                    return false;

                string content = File.ReadAllText(clientSettingsPath).Trim();

                if (content == "{}" || content == "{ }" || string.IsNullOrWhiteSpace(content))
                    return false;

                var flags = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, object>>(content);

                if (flags == null || flags.Count == 0)
                    return false;

                bool modified = false;
                foreach (string flag in AllKnownManagedFlags)
                {
                    if (flags.Remove(flag))
                        modified = true;
                }

                if (modified)
                {
                    string cleaned = System.Text.Json.JsonSerializer
                        .Serialize(flags, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(clientSettingsPath, cleaned);
                    App.Logger.WriteLine(LOG_IDENT, $"Cleaned legacy flags from: {clientSettingsPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not clean {clientSettingsPath}: {ex.Message}");
            }

            return false;
        }

        public static void OptimizeRobloxProcess(int robloxPid)
        {
            if (!App.Settings.Prop.OptimizeForLowEnd)
                return;

            try
            {
                using var robloxProc = Process.GetProcessById(robloxPid);
                robloxProc.PriorityClass = ProcessPriorityClass.AboveNormal;
                App.Logger.WriteLine(LOG_IDENT, $"Set Roblox PID {robloxPid} priority → AboveNormal");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Priority set failed (non-fatal): {ex.Message}");
            }

            try
            {
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB = totalMemBytes / (1024UL * 1024 * 1024);

                // ★ FIX (audit white-screen v7.x): Memory trim HANYA di SSD.
                // EmptyWorkingSet memaksa proses yang di-trim untuk page-in BALIK dari
                // disk saat mereka butuh memorinya lagi. Di HDD, ini terjadi tepat saat
                // game baru launch (fase loading aset paling kritis) → disk storm yang
                // bisa memperparah stall render / white screen. Di SSD page-in hampir
                // instan, jadi trimming tetap aman di sana.
                if (totalMemGB < 5 && GetStorageType() == StorageMediaType.Ssd)
                    TrimBackgroundProcesses(robloxPid);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Memory trim failed (non-fatal): {ex.Message}");
            }

            try
            {
                if (Environment.ProcessorCount <= 2)
                {
                    using var self = Process.GetCurrentProcess();
                    self.ProcessorAffinity = (IntPtr)0x1;
                    App.Logger.WriteLine(LOG_IDENT, "Dual-core detected: BoneFish pinned to core 0");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Affinity set failed (non-fatal): {ex.Message}");
            }
        }

        private static void TrimBackgroundProcesses(int robloxPid)
        {
            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "smss", "csrss", "lsass", "services",
                "winlogon", "wininit", "svchost", "dwm", "explorer",
                "audiodg", "fontdrvhost", "spoolsv", "SearchIndexer"
            };

            string selfName = Process.GetCurrentProcess().ProcessName;
            int trimmedCount = 0;
            long totalFreedKB = 0;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == robloxPid) continue;
                    if (proc.ProcessName == selfName) continue;
                    if (skipNames.Contains(proc.ProcessName)) continue;

                    long workingSetKB = proc.WorkingSet64 / 1024;
                    if (workingSetKB < 20 * 1024) continue;

                    IntPtr handle = OpenProcess(PROCESS_ALL_ACCESS, false, (uint)proc.Id);
                    if (handle == IntPtr.Zero) continue;

                    try
                    {
                        bool trimmed = EmptyWorkingSet(handle);
                        if (trimmed)
                        {
                            totalFreedKB += workingSetKB;
                            trimmedCount++;
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch { }
            }

            if (trimmedCount > 0)
            {
                App.Logger.WriteLine(LOG_IDENT,
                    $"RAM trim: {trimmedCount} background processes trimmed, " +
                    $"~{totalFreedKB / 1024}MB working set released");
            }
        }

        public static bool RemoveOptimizations()
        {
            try
            {
                bool removedAny = false;
                foreach (string flag in ManagedFlags)
                {
                    if (App.FastFlags.GetValue(flag) is not null)
                    {
                        App.FastFlags.SetValue(flag, null);
                        removedAny = true;
                    }
                }
                if (removedAny)
                    App.Logger.WriteLine(LOG_IDENT, "Removed low-end optimization FastFlags");

                // CATATAN: EnableBetterMatchmaking / EnableBetterMatchmakingRandomization
                // TIDAK di-reset di sini — dua setting ini juga bisa di-set manual oleh
                // user di halaman Behaviour (bukan hanya oleh preset). Meresetnya di sini
                // akan menimpa pilihan manual user. Ini pola yang sudah ada sejak v6.0.0.
                return removedAny;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error removing optimizations: {ex.Message}");
                return false;
            }
        }

        // ── Network Optimizations (Reusable) ─────────────────────────────────────────
        // ★ REFACTOR: Ekstrak dari FastFlagsViewModel untuk menghilangkan duplikasi
        // di 5 preset method. Method ini SETARA dengan blok yang sebelumnya inline:
        //   App.Settings.Prop.EnableBetterMatchmaking = true;
        //   App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
        //   App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
        //   App.FastFlags.SetValue("DFIntMaxReceivePPS",        "50000");
        //   App.FastFlags.SetValue("DFIntMaxSendPPS",           "50000");
        //   App.FastFlags.SetValue("DFIntConnectionMTUSize",    "1500");
        //   App.FastFlags.SetValue("DFIntOptimizeSendQueue",    "1");
        //
        // TIDAK memanggil Save()/Notify()/Reload() — itu tanggung jawab caller.
        // Caller: ApplyRecommendedNetworkSettings(), ApplyRecommendedStabilityPreset(),
        //         ApplyUltraLowSpecPreset(), ApplyBalancedPreset(),
        //         ApplyExtremePerformancePreset().
        //
        // Verifikasi: Kelima preset sebelumnya menulis flag yang SAMA PERSIS —
        // method ini adalah 1-to-1 replacement, tidak ada perubahan nilai.
        public static void ApplyNetworkOptimizations()
        {
            App.Settings.Prop.EnableBetterMatchmaking = true;
            App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
            App.FastFlags.SetValue("FIntRakNetPacketRateLimit", "50000");
            App.FastFlags.SetValue("DFIntMaxReceivePPS",        "50000");
            App.FastFlags.SetValue("DFIntMaxSendPPS",           "50000");
            App.FastFlags.SetValue("DFIntConnectionMTUSize",    "1500");
            App.FastFlags.SetValue("DFIntOptimizeSendQueue",    "1");
        }

        // ── Fast Loading Flags (Toggle Terpisah) ─────────────────────────────────────
        // ★ Fast Loading toggle: EnableFastLoadingFlags — mempercepat loading aset
        // (texture, mesh) dengan meningkatkan paralelisme komposisi texture dan
        // thread scheduler. Stack dengan preset visual apa pun.
        //
        // Flag yang dipakai (semua SUDAH ADA di AllKnownManagedFlags):
        //   DFIntTextureCompositorActiveJobs=2  (jika cpuCores >= 4)
        //     Naikkan dari 1 (UltraLow/Extreme) ke 2 agar texture compositor
        //     lebih paralel — aset texture muncul lebih cepat.
        //   FIntRuntimeMaxNumOfThreads=6        (jika cpuCores >= 8)
        //     Naikkan dari 4 (default semua preset) ke 6 agar task scheduler
        //     punya lebih banyak thread untuk loading aset.
        //
        // Flag yang TIDAK dipakai (riset menemukan kemungkinan diblokir Allowlist):
        //   FFlagEnableAsyncResourceLoading     — ❌ Tidak di Allowlist
        //   FIntRenderChunkLODThreshold         — ❌ Tidak di Allowlist
        //   FFlagEnableTextureStreamingFix      — ❌ Tidak di Allowlist
        //   FIntPartSizeBoostThreshold          — ❌ Tidak di Allowlist
        //
        // Flag yang JANGAN dipakai (visual, bukan loading):
        //   FIntRenderShadowIntensity           — Flag visual
        //   DFFlagDisablePostProcessing         — Flag visual
        //
        // Catatan rombak v7.2.7: DFIntTaskSchedulerTargetFps tidak lagi ditulis
        // oleh ApplyAggressiveOptimizations() (dead flag, di luar allowlist sejak
        // 2025-09-29) — tidak ada konflik nilai yang perlu dijaga di sini.
        public static void ApplyFastLoadingFlags()
        {
            int cpuCores = Environment.ProcessorCount;
            
            // DFIntTextureCompositorActiveJobs: naikkan ke 2 KHUSUS cpuCores >= 4
            if (cpuCores >= 4)
            {
                App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "2");
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: DFIntTextureCompositorActiveJobs=2 (cpuCores={cpuCores} >= 4)");
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: DFIntTextureCompositorActiveJobs SKIPPED (cpuCores={cpuCores} < 4)");
            }

            // FIntRuntimeMaxNumOfThreads: naikkan ke 6 KHUSUS cpuCores >= 8
            if (cpuCores >= 8)
            {
                App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", "6");
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: FIntRuntimeMaxNumOfThreads=6 (cpuCores={cpuCores} >= 8)");
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: FIntRuntimeMaxNumOfThreads SKIPPED (cpuCores={cpuCores} < 8)");
            }
        }

        public static void RemoveFastLoadingFlags()
        {
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", null);
            App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", null);
            App.Logger.WriteLine(LOG_IDENT, "FastLoading flags removed");
        }

        // ── TDR Mitigation Mode (Toggle Terpisah) ──────────────────────────────────
        // ★ LATAR BELAKANG (v7.0.5): freeze "layar putih" terkonfirmasi = Intel iGPU
        // Driver TDR (Event ID 4101 di Event Viewer, cocok ±1 detik dengan log stall).
        // Device: Intel HD 4400 (Haswell 2013-2014) — legacy sejak 2018, TIDAK ada
        // update driver lagi dari Intel (driver 2020 = versi terakhir). Karena jalur
        // update buntu, satu-satunya mitigasi software yang jujur adalah MENURUNKAN
        // BEBAN RENDER GPU agar TDR lebih jarang terpicu — BUKAN menghilangkan total
        // (akar masalah di driver, di luar kendali FastFlag apa pun).
        //
        // ★ ROMBAK v7.2.7 (audit layar-putih 8/18/2026, Intel HD 1GB driver
        // 20.19.15.5126): kombinasi lama "MSAA=1 + FRM=3 + Texture=0 + FPS cap=30"
        // TERBUKTI MENYEBABKAN layar putih di iGPU tua (ClientMemStatus Error,
        // artefak render). Rombak: TDR Mitigation sekarang HANYA menurunkan MSAA —
        // satu-satunya komponen yang menurunkan beban per-pixel tanpa merusak render:
        //   FIntDebugForceMSAASamples=1              — MSAA off → beban per-pixel GPU turun
        //                                              drastis (MSAA = beban render per-frame
        //                                              paling signifikan yang bisa diatur).
        //
        // Flag yang DIBUANG dari TDR (tidak pernah ditulis lagi):
        //   DFIntDebugFRMQualityLevelOverride=3      — render quality paksa; kombinasi
        //                                              dengan texture override terbukti
        //                                              merusak render di iGPU tua.
        //   DFFlagTextureQualityOverrideEnabled=True + DFIntTextureQualityOverride=0
        //                                              — texture paksa 0; bagian dari
        //                                              kombinasi layar-putih di atas.
        //   DFIntTaskSchedulerTargetFps=30           — ❌ TIDAK di allowlist sejak 2025-09-29
        //                                              (client modern mengabaikannya; pengganti:
        //                                              GlobalBasicSettings_13.xml FramerateCap).
        //
        // Nilai lama dari versi terpasang tetap dibersihkan oleh PurgeAllKnownFlags()
        // via AllKnownManagedFlags — tidak ada migrasi khusus yang dibutuhkan.
        private static readonly string[] TdrMitigationFlags =
        {
            "FIntDebugForceMSAASamples",
        };

        public static void ApplyTdrMitigationFlags()
        {
            // Snapshot nilai SEBELUM ditimpa — hanya sekali (persisten di Settings,
            // jadi tetap valid walau user restart app lalu mematikan toggle).
            if (App.Settings.Prop.TdrMitigationBackup.Count == 0)
            {
                foreach (string flag in TdrMitigationFlags)
                {
                    string? current = App.FastFlags.GetValue(flag);
                    if (current is not null)
                        App.Settings.Prop.TdrMitigationBackup[flag] = current;
                }
                try { App.Settings.Save(); } catch { }
            }

            App.FastFlags.SetValue("FIntDebugForceMSAASamples", "1");

            App.Logger.WriteLine(LOG_IDENT, "TDR Mitigation applied: MSAA=1 only (FRM/texture/FPS-cap overrides removed in v7.2.7 overhaul)");
        }

        public static void RemoveTdrMitigationFlags()
        {
            foreach (string flag in TdrMitigationFlags)
            {
                string? previous = App.Settings.Prop.TdrMitigationBackup.TryGetValue(flag, out string? value) ? value : null;
                App.FastFlags.SetValue(flag, previous);
            }

            App.Settings.Prop.TdrMitigationBackup.Clear();
            try { App.Settings.Save(); } catch { }

            App.Logger.WriteLine(LOG_IDENT, "TDR Mitigation flags removed (previous values restored)");
        }

        // ── Manual FastFlag Toggles (DisableRobloxAnimations / EnableLowMemoryMode) ─────
        // ★ FIX (v7.2.x, Opsi A — preset-aware purge): Dua toggle manual ini
        // sebelumnya state-nya dibaca langsung dari App.FastFlags. Setiap Play,
        // CheckAndApply() → PurgeAllKnownFlags() menghapus flag-nya (keempat flag
        // ada di AllKnownManagedFlags) dan TIDAK ada yang me-re-apply → toggle
        // "hilang" tiap launch walau sudah disimpan dengan benar.
        // Solusi: state toggle dipindah ke Settings (bool terpisah), lalu keempat
        // flag di-RE-APPLY di akhir CheckAndApply() — pola SAMA dengan TDR Mitigation
        // (priority: HIGHEST). Purge tetap membersihkan stale values dari preset
        // sebelumnya, jadi fix v4.4.0 (flag tidak terhapus saat pindah preset)
        // TIDAK ter-regresi.
        public static void ApplyDisableRobloxAnimations()
        {
            App.FastFlags.SetValue("FFlagRenderUIAnimations", "False");
            App.FastFlags.SetValue("FFlagRenderMenuTransitions", "False");
            App.FastFlags.SetValue("FFlagRenderInventoryEffects", "False");
        }

        public static void RemoveDisableRobloxAnimations()
        {
            App.FastFlags.SetValue("FFlagRenderUIAnimations", null);
            App.FastFlags.SetValue("FFlagRenderMenuTransitions", null);
            App.FastFlags.SetValue("FFlagRenderInventoryEffects", null);
        }

        public static void ApplyLowMemoryMode()
        {
            App.FastFlags.SetValue("FFlagLuaAppEnableLowMemoryMode", "True");
        }

        public static void RemoveLowMemoryMode()
        {
            App.FastFlags.SetValue("FFlagLuaAppEnableLowMemoryMode", null);
        }
    }
}
