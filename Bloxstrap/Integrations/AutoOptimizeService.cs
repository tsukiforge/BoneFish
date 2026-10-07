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
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)]
            public byte IncursSeekPenalty; // BOOLEAN: 0 = no seek penalty (SSD hint), 1 = seek penalty (HDD)
        }

        [StructLayout(LayoutKind.Sequential)]
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

        // ── HardwareProfileEngine accessors (Phase 3) ────────────────────────────
        // Ringan: GlobalMemoryStatusEx = syscall kernel (±µs), bukan WMI. Dipublikasi
        // agar mesin profil hardware bisa membaca total/available RAM tanpa duplikasi
        // struct/P-Invoke, tanpa menambah sumber kebenaran baru.
        public static ulong GetTotalPhysicalMemoryPublic() => GetTotalPhysicalMemory();

        public static ulong GetAvailablePhysicalMemoryPublic()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
                return mem.ullAvailPhys;
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
        //   v5: fallback flash/removable (USB/SD/MMC) untuk disk sistem yang tidak
        //       dilaporkan storage stack (MediaType=0) — fix kasus "Unknown".
        //       + NVMe = bukti SSD KUAT (protokol NVMe tidak ada yang HDD).
        //       + SATA/RAID + MediaType=0 + TRIM aktif + properti seek-penalty tidak
        //         didukung driver → hint SSD lemah (Windows tidak menyalakan TRIM
        //         di disk rotasional). Mengurangi Unknown pada Intel RST.
        // Cache versi lebih lama OTOMATIS di-invalidasi.
        private const int HardwareCacheVersion = 4;
        private const int StorageDetectorVersion = 5;

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
            public string Reason { get; set; } = "";
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
                if (type == StorageMediaType.Unknown)
                    return null;

                _lastDetection = new StorageDetectionResult
                {
                    Type = type,
                    Confidence = entry.Confidence,
                    PhysicalDiskIndex = entry.PhysicalDiskIndex >= 0
                        ? entry.PhysicalDiskIndex.ToString(CultureInfo.InvariantCulture)
                        : "?",
                    DiskModel = entry.DiskModel,
                    BusType = entry.BusType,
                    SystemDrive = entry.SystemDriveRoot,
                    Sources = new List<string>(entry.Sources),
                    Reason = String.IsNullOrWhiteSpace(entry.Reason)
                        ? "Loaded from persistent cache; original reason unavailable"
                        : entry.Reason
                };
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
                    Reason = detection.Reason,
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

            bool seekPenaltyValidated = TrySeekPenaltyOnPhysicalDisk(diskIndex.Value, out bool seekPenalty, out string seekDiag);
            if (seekPenaltyValidated)
            {
                result.Diagnostics.Add($"IOCTL SeekPenalty={seekPenalty} ({(seekPenalty ? "HDD hint (strong)" : "SSD hint (weak)")})");
                votes.Add(("StorageDevice", !seekPenalty, seekPenalty)); // seekPenalty=true → HDD kuat; false → SSD lemah
            }
            else
            {
                result.Diagnostics.Add($"IOCTL SeekPenalty: FAILED — {seekDiag}");
            }

            bool trimValidated = TryTrimOnPhysicalDisk(diskIndex.Value, out bool trimEnabled, out string trimDiag);
            if (trimValidated)
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

                        // ── FIX (audit "storage unknown") ────────────────────────────
                        // Storage stack Windows tidak bisa membedakan SSD/HDD pada banyak
                        // bus flash/removable: SD/eMMC dan banyak USB bridge SELALU
                        // melapor MediaType=0 (Task Manager menampilkan "Unknown").
                        // Untuk DISK SISTEM di bus tersebut, media-nya secara fisis flash
                        // (bukan platter magnetik) → treat sebagai HDD (weak hint) supaya
                        // tweet I/O HDD tetap diterapkan alih-alih jatuh ke Unknown.
                        if (mediaType == 0 && busType is 7 or 12 or 13) // USB, SD, MMC
                        {
                            result.Diagnostics.Add($"BusType={result.BusType} + MediaType unspecified → flash/removable (HDD hint, weak)");
                            votes.Add(("BusType.RemovableFlash", false, false));
                        }
                        break;
                }

                if (busType == 17) // NVMe — selalu solid-state
                {
                    // NVMe adalah protokol khusus solid-state — tidak ada perangkat HDD
                    // NVMe. Ini bukti KUAT (bukan sekadar hint) dan menutup kasus Unknown
                    // pada storage stack yang gagal melapor MediaType.
                    result.Diagnostics.Add("BusType=NVMe → SSD (strong — NVMe is always solid-state)");
                    votes.Add(("BusType.NVMe", true, true));
                }
                else if (mediaType == 0 && (busType is 3 or 8 or 11) && trimValidated && trimEnabled && !seekPenaltyValidated)
                {
                    // ── FIX (audit "storage unknown" #2) ──────────────────────────
                    // Kasus Intel RST / driver RAID: MediaType=0 DAN properti seek-penalty
                    // tidak didukung driver → semua sumber IOCTL diam. Windows tidak
                    // pernah menyalakan TRIM pada disk rotasional, jadi TRIM aktif di
                    // bus SATA/RAID adalah indikator SSD yang dapat diandalkan.
                    result.Diagnostics.Add($"BusType={result.BusType} + MediaType=0 + TrimEnabled + seek-penalty unsupported → SSD hint (weak)");
                    votes.Add(("SataRaid.TrimCorrelation", true, false));
                }
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
                        QueryType = 0, // PropertyStandardQuery
                        AdditionalParameters = 0
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
                        QueryType = 0,
                        AdditionalParameters = 0
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
                if (!App.Settings.Prop.PerformancePresetGraphicsQualityManaged)
                {
                    int? savedPresetQuality = App.Settings.Prop.SelectedPerformancePreset switch
                    {
                        "Balanced" => 5,
                        "AutoOptimize" or "Stable" or "UltraLow" or "ExtremePerformance" => 1,
                        _ => null
                    };

                    if (savedPresetQuality.HasValue)
                        ApplySafeRobloxGraphicsQuality(savedPresetQuality.Value);
                }

                // ── v7.0.5: tier ASLI vs tier EFEKTIF ──────────────────────────────
                // Bug (sebelum fix): DetectSystemTier() punya short-circuit
                //   if (ForceExtremeMode) return ExtremePerformance;
                // sehingga tier asli device (HDD + LowEnd/MidRange) tidak pernah sampai
                // ke pengecekan HDD Balanced, dan cabang OptimizeForLowEnd di bawah
                // return duluan — ApplyHDDBalancedOptimizations() TIDAK PERNAH terpanggil
                // saat ForceExtremeMode aktif → semua tweak I/O HDD hilang, digantikan
                // mode Extreme generik yang buta terhadap bottleneck disk.
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
                    bool bypassManualPresetGuard = App.Settings.Prop.ForceExtremeMode
                        && !String.Equals(App.Settings.Prop.SelectedPerformancePreset, "ExtremePerformance", StringComparison.Ordinal);

                    ApplyAggressiveOptimizations(bypassLowEndGuard: bypassManualPresetGuard);
                    return true;
                }

                // Do not inject storage-specific renderer settings. Only clean stale
                // overrides when a confirmed HDD profile reaches this path.
                if (GetStorageType() == StorageMediaType.Hdd
                    && (tier == SystemTier.LowEnd || tier == SystemTier.MidRange) && !UserHasManualPreset())
                {
                    App.Logger.WriteLine(LOG_IDENT, $"HDD detected + {tier} tier — removing legacy renderer overrides");
                    ApplyHDDBalancedOptimizations();
                    return true;
                }

                if (!UserHasManualPreset() && !App.Settings.Prop.ForceExtremeMode)
                    RestoreSafeRobloxGraphicsQuality();

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

                // ── Legacy FastFlag toggles ───────────────────────────────────────
                // Roblox-rejected values are filtered by FastFlagManager before save.
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
                if (App.Settings.Prop.EnableFastLoadingFlags)
                {
                    try { ApplyFastLoadingFlags(); } catch { }
                    App.Logger.WriteLine(LOG_IDENT, "Fast Loading re-applied after CheckAndApply.");
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

        /// <summary>
        /// Diagnostik performa READ-ONLY (audit FPS Fase 6).
        /// Dipanggil ON-DEMAND dari UI (tombol) — BUKAN background polling, tidak
        /// menulis FastFlag / tidak Save apa pun, dan tidak mengubah konfigurasi user.
        /// Hanya membaca: Settings, FastFlags, GlobalBasicSettings_13.xml, refresh rate,
        /// dan satu query WMI GPU.
        /// CATATAN: bila deteksi storage belum pernah jalan di sesi ini, pemanggilan
        /// GetStorageType() bisa menulis cache hardware milik BoneFish sendiri
        /// (HardwareCache.json) — cache internal, bukan konfigurasi Roblox/user.
        /// </summary>
        public static string GetPerformanceDiagnostics()
        {
            var sb = new StringBuilder();

            void Line(string label, string value) => sb.AppendLine($"{label,-26}: {value}");

            string Flag(string name)
            {
                try { return App.FastFlags.GetValue(name) ?? "(not set)"; }
                catch { return "(error)"; }
            }

            try
            {
                sb.AppendLine("=== BoneFish Performance Diagnostics (READ-ONLY) ===");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                sb.AppendLine("-- Build --");
                Line("BoneFish version", App.Version);
                sb.AppendLine();

                sb.AppendLine("-- Mode / preset --");
                Line("Selected preset", App.Settings.Prop.SelectedPerformancePreset ?? "None");
                Line("ForceExtremeMode", App.Settings.Prop.ForceExtremeMode ? "ON" : "OFF");
                Line("OptimizeForLowEnd", App.Settings.Prop.OptimizeForLowEnd ? "ON" : "OFF");
                Line("UserHasManualPreset", UserHasManualPreset() ? "yes" : "no");
                Line("Guard bypass (intent)", App.Settings.Prop.ForceExtremeMode
                    ? "armed — guard preset manual di-bypass"
                    : "not armed");
                sb.AppendLine();

                sb.AppendLine("-- Hardware --");
                Line("CPU cores", Environment.ProcessorCount.ToString());
                Line("RAM tier (asli)", DetectSystemTier(ignoreForceExtreme: true).ToString());
                Line("Tier efektif", DetectSystemTier().ToString());
                Line("GPU", GetGpuName());
                Line("Refresh rate", $"{FpsUnlockerService.GetPrimaryDisplayRefreshRate()} Hz");
                sb.AppendLine();

                sb.AppendLine("-- Storage (3-state, tidak menebak) --");
                StorageDetectionResult storage = GetStorageDiagnostics();
                Line("Storage type", GetStorageType().ToString());
                Line("Confidence", String.IsNullOrWhiteSpace(storage.Confidence) ? "(none)" : storage.Confidence);
                Line("Physical disk", String.IsNullOrWhiteSpace(storage.PhysicalDiskIndex) ? "?" : storage.PhysicalDiskIndex);
                Line("Disk model", String.IsNullOrWhiteSpace(storage.DiskModel) ? "(unknown)" : storage.DiskModel);
                Line("Bus type", String.IsNullOrWhiteSpace(storage.BusType) ? "(unknown)" : storage.BusType);
                Line("Reason", String.IsNullOrWhiteSpace(storage.Reason) ? "(none)" : storage.Reason);
                Line("HDD-specific renderer FastFlags", "not applied");
                sb.AppendLine();

                sb.AppendLine("-- Local rendering FastFlags (not proof Roblox applied them) --");
                Line("LOD base / L12", $"{Flag("DFIntCSGLevelOfDetailSwitchingDistance")} / {Flag("DFIntCSGLevelOfDetailSwitchingDistanceL12")}");
                Line("LOD L23 / L34", $"{Flag("DFIntCSGLevelOfDetailSwitchingDistanceL23")} / {Flag("DFIntCSGLevelOfDetailSwitchingDistanceL34")}");
                Line("TextureCompositorJobs", Flag("DFIntTextureCompositorActiveJobs"));
                sb.AppendLine();

                sb.AppendLine("-- Toggle performa --");
                Line("Fast Loading", App.Settings.Prop.EnableFastLoadingFlags ? "ON" : "OFF");
                Line("TDR Mitigation", App.Settings.Prop.EnableTdrMitigation ? "ON" : "OFF");
                Line("FPS cap BoneFish", App.Settings.Prop.FpsUnlockerEnabled ? "ON" : "OFF");
                sb.AppendLine();

                sb.AppendLine("-- FPS cap --");
                if (!App.GlobalSettings.Loaded)
                    App.GlobalSettings.Load();

                bool gbsAvailable = App.GlobalSettings.Document is not null;
                string? effectiveCap = gbsAvailable ? App.GlobalSettings.GetPreset("Rendering.FramerateCap") : null;
                string? graphicsLevel = gbsAvailable ? App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") : null;

                Line("Effective FramerateCap", String.IsNullOrWhiteSpace(effectiveCap) ? "(tidak ada / default Roblox)" : effectiveCap);
                Line("Cap dikelola BoneFish", App.Settings.Prop.FpsUnlockerCapManaged ? "yes" : "no");
                Line("Cap user sebelumnya", App.Settings.Prop.FpsUnlockerPreviousCap ?? "(none)");
                Line("Roblox graphics level", String.IsNullOrWhiteSpace(graphicsLevel) ? "(unavailable)" : graphicsLevel);
                sb.AppendLine();

                sb.AppendLine("-- Runtime --");
                Line("Roblox priority", GetRobloxPriority());
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(diagnostics incomplete: {ex.Message})");
            }

            return sb.ToString();
        }

        /// <summary>
        /// GPU name untuk diagnostik — satu query WMI, hanya saat dipanggil.
        /// </summary>
        private static string GetGpuName()
        {
            try
            {
                var names = new List<string>();
                using var searcher = new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT Name FROM Win32_VideoController");

                foreach (ManagementObject controller in searcher.Get().Cast<ManagementObject>())
                {
                    try
                    {
                        string? name = controller["Name"]?.ToString();
                        if (!String.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                    finally
                    {
                        controller.Dispose();
                    }
                }

                return names.Count == 0 ? "(unknown)" : String.Join(" + ", names);
            }
            catch
            {
                return "(unavailable)";
            }
        }

        /// <summary>
        /// Priority proses Roblox yang sedang berjalan (diagnostik, read-only).
        /// Process di-dispose agar tidak menambah handle leak.
        /// </summary>
        private static string GetRobloxPriority()
        {
            try
            {
                Process[] robloxProcesses = Process.GetProcessesByName("RobloxPlayerBeta");

                if (robloxProcesses.Length == 0)
                    return "(Roblox tidak berjalan)";

                string result;
                try
                {
                    result = $"PID {robloxProcesses[0].Id} -> {robloxProcesses[0].PriorityClass}";
                }
                catch
                {
                    result = $"PID {robloxProcesses[0].Id} -> (priority tidak bisa dibaca)";
                }

                foreach (Process process in robloxProcesses)
                {
                    try { process.Dispose(); } catch { }
                }

                return result;
            }
            catch
            {
                return "(unavailable)";
            }
        }

        public static bool ApplySafeRobloxGraphicsQuality(int qualityLevel)
        {
            const string LOG_IDENT = "AutoOptimizeService::ApplySafeRobloxGraphicsQuality";
            if (qualityLevel is < 1 or > 10)
                throw new ArgumentOutOfRangeException(nameof(qualityLevel), qualityLevel, "Graphics quality must be between 1 and 10.");

            if (!App.GlobalSettings.Loaded)
                App.GlobalSettings.Load();

            if (App.GlobalSettings.Document is null
                || App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox SavedQualityLevel is unavailable; graphics quality was not changed.");
                return false;
            }

            string requestedValue = qualityLevel.ToString(CultureInfo.InvariantCulture);
            string? currentValue = App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel");
            bool managed = App.Settings.Prop.PerformancePresetGraphicsQualityManaged;
            string? previousValue = App.Settings.Prop.PerformancePresetPreviousGraphicsQuality;
            string? storedPreviousValue = previousValue;
            string? appliedValue = App.Settings.Prop.PerformancePresetAppliedGraphicsQuality;

            if (!managed || currentValue != App.Settings.Prop.PerformancePresetAppliedGraphicsQuality)
                previousValue = currentValue;

            App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", requestedValue);
            if (App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") != requestedValue)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to set Roblox SavedQualityLevel to {requestedValue}.");
                return false;
            }

            App.GlobalSettings.Save();
            App.Settings.Prop.PerformancePresetGraphicsQualityManaged = true;
            App.Settings.Prop.PerformancePresetPreviousGraphicsQuality = previousValue;
            App.Settings.Prop.PerformancePresetAppliedGraphicsQuality = requestedValue;
            try
            {
                App.Settings.Save();
            }
            catch
            {
                App.Settings.Prop.PerformancePresetGraphicsQualityManaged = managed;
                App.Settings.Prop.PerformancePresetPreviousGraphicsQuality = storedPreviousValue;
                App.Settings.Prop.PerformancePresetAppliedGraphicsQuality = appliedValue;

                if (currentValue is null)
                    App.GlobalSettings.RemovePreset("Rendering.SavedQualityLevel");
                else
                    App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", currentValue);

                App.GlobalSettings.Save();
                throw;
            }
            App.Logger.WriteLine(LOG_IDENT, $"Roblox SavedQualityLevel set to {requestedValue}.");
            return true;
        }

        public static void RestoreSafeRobloxGraphicsQuality()
        {
            const string LOG_IDENT = "AutoOptimizeService::RestoreSafeRobloxGraphicsQuality";
            if (!App.Settings.Prop.PerformancePresetGraphicsQualityManaged)
                return;

            if (!App.GlobalSettings.Loaded)
                App.GlobalSettings.Load();

            if (App.GlobalSettings.Document is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox GlobalBasicSettings is unavailable; keeping the graphics-quality restore point.");
                return;
            }

            string? currentValue = App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel");
            if (currentValue == App.Settings.Prop.PerformancePresetAppliedGraphicsQuality)
            {
                string? previousValue = App.Settings.Prop.PerformancePresetPreviousGraphicsQuality;
                if (previousValue is null)
                    App.GlobalSettings.RemovePreset("Rendering.SavedQualityLevel");
                else
                    App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", previousValue);

                App.GlobalSettings.Save();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Graphics quality changed outside the preset; preserving the newer value.");
            }

            App.Settings.Prop.PerformancePresetGraphicsQualityManaged = false;
            App.Settings.Prop.PerformancePresetPreviousGraphicsQuality = null;
            App.Settings.Prop.PerformancePresetAppliedGraphicsQuality = null;
            App.Settings.Save();
        }

        public static void ApplyAggressiveOptimizations(bool bypassLowEndGuard = false)
        {
            try
            {
                if (!bypassLowEndGuard)
                {
                    if (!App.Settings.Prop.OptimizeForLowEnd)
                        return;

                    if (UserHasManualPreset())
                    {
                        App.Logger.WriteLine(LOG_IDENT, "User has manual preset — skipping automatic renderer changes.");
                        return;
                    }
                }

                PurgeLegacyRendererFlags();
                ApplySafeRobloxGraphicsQuality(1);
                ApplyNetworkOptimizations();
                if (App.Settings.Prop.EnableFastLoadingFlags)
                    ApplyFastLoadingFlags();
                App.Logger.WriteLine(LOG_IDENT,
                    "Removed legacy renderer FastFlags; Roblox graphics quality and frame-rate settings remain in control.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying conservative optimizations: {ex.Message}");
            }
        }

        // Retained as a call-site-compatible cleanup path for confirmed HDD systems.
        public static void ApplyHDDBalancedOptimizations()
        {
            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Cleaning legacy renderer overrides for confirmed HDD system.");
                ApplyAggressiveOptimizations(bypassLowEndGuard: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error cleaning HDD renderer overrides: {ex.Message}");
            }
        }

        // Retired renderer overrides emitted by previous BoneFish presets. All other
        // flags, including user preferences and currently supported settings, survive
        // update cleanup unchanged.
        private static readonly string[] LegacyRendererFlags =
        {
            "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "FIntTextureCompositorLowResFactor", "DFIntTextureCompositorActiveJobs",
            "DFIntDebugFRMQualityLevelOverride", "FIntRomarkStartWithGraphicQualityLevel",
            "FIntRenderShadowIntensity", "DFFlagDebugPauseVoxelizer", "FIntCSGVoxelizerFadeRadius",
            "DFFlagDebugRenderForceTechnologyVoxel", "FFlagNewLightAttenuation", "FFlagFastGPULightCulling3",
            "FFlagDebugSkyGray", "FFlagDisablePostFx", "FFlagDebugSSAOForce", "FIntSSAOMipLevels", "FIntRobloxGuiBlurIntensity", "FIntRenderGrainScale",
            "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntRenderGrassDetailStrands", "FIntRenderGrassHeightScaler", "FFlagGlobalWindActivated",
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34", "DFIntCSGLevelOfDetailSwitchingDistanceStatic", "DFIntDebugRestrictGCDistance",
            "FIntTerrainArraySliceSize",
            "DFIntAnimationLodFacsDistanceMin", "DFIntAnimationLodFacsDistanceMax", "DFIntAnimationLodFacsVisibilityDenominator",
            "FIntRenderLocalLightUpdatesMax", "FIntRenderLocalLightUpdatesMin", "FIntRenderLocalLightFadeInMs",
            "FIntDebugForceMSAASamples",
        };

        private static readonly string[] RendererFlagsToRemove = LegacyRendererFlags
            .Concat(FastFlagManager.FlagsRejectedByRobloxLogs)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static IEnumerable<string> GetRendererFlagsToRemove()
        {
            bool keepOtherBetaFlags = App.Settings.Prop.EnableLegacyFastFlagsBeta;
            if (!keepOtherBetaFlags)
                return RendererFlagsToRemove;

            return RendererFlagsToRemove.Where(flag =>
                !(FastFlagManager.BetaTestableLegacyFlags.Contains(flag)
                    && !FastFlagManager.IsFlagRejectedByRobloxLogs(flag)));
        }

        public static void PurgeLegacyRendererFlags()
        {
            string[] flagsToRemove = GetRendererFlagsToRemove().ToArray();
            foreach (string flag in flagsToRemove)
                App.FastFlags.SetValue(flag, null);
            App.Logger.WriteLine(LOG_IDENT,
                $"Removed {flagsToRemove.Length} retired or Roblox-rejected flags from the active configuration");
        }

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
                foreach (string flag in GetRendererFlagsToRemove())
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
            // ★ HARDENING (Phase 2 — Windows Security investigation): skip list dulu
            // hanya proses inti Windows. Proses keamanan (SecurityHealthService dll.)
            // SECARA TEORI bisa kerja-set-nya di-trim walau bukan protected process —
            // sekarang seluruh set proses keamanan Windows + audio vendor di-skip
            // eksplisit. Defense in depth: this process trim is a separate pathway.
            // MsMpEng sendiri adalah protected process (OpenProcess gagal) tapi tetap
            // didaftarkan agar kebijakannya eksplisit dan tahan terhadap perubahan OS.
            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "smss", "csrss", "lsass", "services",
                "winlogon", "wininit", "svchost", "dwm", "explorer",
                "audiodg", "fontdrvhost", "spoolsv", "SearchIndexer",
                // Windows security stack (Phase 2 rule: selalu dilindungi)
                "MsMpEng", "MsSense", "NisSrv", "MsMpEngCP",
                "SecurityHealthService", "SecurityHealthSystray", "wscsvc",
                "WinDefend", "SenseIR", "SenseCncAgent", "SenseSampleUploader",
                // Realtek/audio vendor companion processes
                "RAVBg64", "RAVCpl64", "RAVCpl", "RtkAudioService64",
                "RtkAudUService64", "RtkAudUService", "RtkAudioService",
                "RtkNGUI64", "RtkNGUI", "RtkBtManServ", "BthAudioAgent", "WsaAudioService"
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
                foreach (string flag in GetRendererFlagsToRemove())
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

        // ── Network preferences (Reusable) ────────────────────────────────────────────
        // Keep BoneFish's matchmaking preferences. Local network flags were reported as
        // denied by the user's Roblox 0.741 client log and do not improve ping.
        public static void ApplyNetworkOptimizations()
        {
            App.Settings.Prop.EnableBetterMatchmaking = true;
            App.Settings.Prop.EnableBetterMatchmakingRandomization = true;
        }

        // ── Fast Loading Flags (Toggle Terpisah) ─────────────────────────────────────
        // On systems with fewer than 8 logical processors, no supported flag remains:
        // the texture-compositor flag was denied by the analyzed Roblox client.
        public static void ApplyFastLoadingFlags()
        {
            int cpuCores = Environment.ProcessorCount;
            if (cpuCores >= 8)
            {
                App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", "6");
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading: FIntRuntimeMaxNumOfThreads=6 (cpuCores={cpuCores} >= 8)");
            }
            else
            {
                App.Settings.Prop.EnableFastLoadingFlags = false;
                App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", null);
                try { App.Settings.Save(); }
                catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
                App.Logger.WriteLine(LOG_IDENT, $"FastLoading disabled: no applicable non-denied flag for cpuCores={cpuCores}");
            }
        }

        public static void RemoveFastLoadingFlags()
        {
            App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", null);
            App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", null);
            App.Logger.WriteLine(LOG_IDENT, "FastLoading flags removed");
        }

        // TDR mitigation uses Roblox's supported saved graphics quality instead of
        // forcing renderer FastFlags. Old MSAA=1 values are removed during migration.
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
                    if (current is not null && current != "1")
                        App.Settings.Prop.TdrMitigationBackup[flag] = current;
                }
                try { App.Settings.Save(); } catch { }
            }

            App.FastFlags.SetValue("FIntDebugForceMSAASamples", null);
            bool qualityApplied = ApplySafeRobloxGraphicsQuality(1);

            App.Logger.WriteLine(LOG_IDENT, qualityApplied
                ? "TDR mitigation uses Roblox's saved graphics quality level 1; no MSAA FastFlag is forced."
                : "TDR mitigation cleared the MSAA FastFlag, but Roblox SavedQualityLevel is unavailable.");
        }

        public static void RemoveTdrMitigationFlags()
        {
            foreach (string flag in TdrMitigationFlags)
            {
                string? previous = App.Settings.Prop.TdrMitigationBackup.TryGetValue(flag, out string? value) ? value : null;
                App.FastFlags.SetValue(flag, previous == "1" ? null : previous);
            }

            App.Settings.Prop.TdrMitigationBackup.Clear();
            try { App.Settings.Save(); } catch { }

            int? selectedPresetQuality = App.Settings.Prop.ForceExtremeMode
                ? 1
                : App.Settings.Prop.SelectedPerformancePreset switch
                {
                    "Balanced" => 5,
                    "AutoOptimize" or "Stable" or "UltraLow" or "ExtremePerformance" => 1,
                    _ => null
                };

            if (selectedPresetQuality.HasValue)
                ApplySafeRobloxGraphicsQuality(selectedPresetQuality.Value);
            else
                RestoreSafeRobloxGraphicsQuality();

            App.Logger.WriteLine(LOG_IDENT, "TDR mitigation removed; preset graphics quality or the original user value was restored.");
        }

        // ── Manual FastFlag Toggles (DisableRobloxAnimations / EnableLowMemoryMode) ─────
        // ★ FIX (v7.2.x, Opsi A — preset-aware purge): Dua toggle manual ini
        // sebelumnya state-nya dibaca langsung dari App.FastFlags. Setiap Play,
        // These retired controls remain as no-op compatibility methods for older UI
        // state. Roblox-rejected values are filtered before saving.
        public static void ApplyDisableRobloxAnimations()
        {
            App.Settings.Prop.DisableRobloxAnimations = false;
            RemoveDisableRobloxAnimations();
        }

        public static void RemoveDisableRobloxAnimations()
        {
            App.FastFlags.SetValue("FFlagRenderUIAnimations", null);
            App.FastFlags.SetValue("FFlagRenderMenuTransitions", null);
            App.FastFlags.SetValue("FFlagRenderInventoryEffects", null);
        }

        public static void ApplyLowMemoryMode()
        {
            App.Settings.Prop.EnableLowMemoryMode = false;
            RemoveLowMemoryMode();
        }

        public static void RemoveLowMemoryMode()
        {
            App.FastFlags.SetValue("FFlagLuaAppEnableLowMemoryMode", null);
        }
    }
}
