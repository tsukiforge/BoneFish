# Storage Detection (v7.7.0) — Decision Tree, Cache & Test Matrix

Hard requirement: **SSD → SSD, HDD → HDD, dan bila bukti tidak cukup / kontradiktif → `Unknown`.**
Tidak ada jalur kode yang menebak `Unknown → HDD` atau `Unknown → SSD`. *A wrong detection is worse than `Unknown`.*

Implementasi: `Bloxstrap/Integrations/AutoOptimizeService.cs` (`DetectStorageType()` + source methods).

---

## 1. Decision tree

```text
System volume (mis. C:)
 ↓
Win32_LogicalDiskToPartition  →  nomor PHYSICAL DISK tempat Windows berada
                                 (bukan disk pertama, bukan sekadar "C:")
 ↓
Sumber deteksi (semua ke physical disk yang sama, dengan validasi ketat):
  S1  IOCTL DEVICE_SEEK_PENALTY_DESCRIPTOR   (handle \\.\PhysicalDriveN, akses-0)
        • validasi Version >= 1, Size >= sizeof(struct), IncursSeekPenalty ∈ {0,1}
        • IncursSeekPenalty = 1 → HDD  [BUKTI KUAT]
        • IncursSeekPenalty = 0 → SSD  [HINT LEMAH — driver tanpa dukungan
          properti ini kerap mengembalikan descriptor kosong; tanpa validasi,
          inilah akar bug HDD→SSD pada engine v2/v3]
  S2  IOCTL DEVICE_TRIM_DESCRIPTOR
        • TrimEnabled = 1 → SSD  [HINT LEMAH]
        • TrimEnabled = 0 → tidak ada informasi (bukan bukti HDD)
  S3  WMI MSFT_PhysicalDisk.MediaType  (disk index hasil mapping, bukan disk pertama)
        • 3 → SSD  [BUKTI KUAT]   (sumber data yang dipakai Task Manager)
        • 4 → HDD  [BUKTI KUAT]
        • 0/lain  → tidak ada informasi
  S4  BusType = NVMe (17) → SSD  [HINT LEMAH]
 ↓
CONSISTENCY CHECK (DecideStorageType):
  • tidak ada sumber yang valid .................. → Unknown (None)
  • bukti kuat BERLAWANAN (mis. S3=SSD vs S1=HDD) → Unknown (None, reason: conflicting)
  • ≥1 bukti kuat HDD, tanpa sinyal SSD lawan .... → HDD   (Medium; High bila ≥2 sumber)
  • ≥1 bukti kuat SSD, tanpa sinyal HDD lawan .... → SSD   (High)
  • hanya hint lemah SSD, ≥2 sumber sepakat ...... → SSD   (Medium)
  • hanya 1 hint lemah / kombinasi tak pasti ..... → Unknown (Low)
 ↓
SSD / HDD / Unknown
```

Contoh diagnostik konflik (log, bukan UI):

```text
Mapping: C: → partition "Disk #0, Partition #2" → PhysicalDrive0
IOCTL SeekPenalty=False (SSD hint, weak)
WMI MSFT_PhysicalDisk: Model="WDC WD10EZEX", BusType=SATA, MediaType=4
Storage decision: Unknown (confidence=None) — Conflicting strong storage media indicators:
  MSFT_PhysicalDisk → SSD vs StorageDevice → HDD
```

## 2. Cache

`%LocalAppData%\BoneFish\HardwareCache.json`:

| Field | Fungsi validasi |
|---|---|
| `Version` + `DetectorVersion` | v4 sekarang. Beda versi → cache dibuang, deteksi ulang. |
| `StorageType` | `"SSD"` / `"HDD"`. **Unknown tidak pernah ditulis.** |
| `SystemDriveRoot` + `VolumeSerial` | Drive/ volume berubah → invalid. |
| `PhysicalDiskIndex`, `DiskModel`, `BusType` | Identitas disk untuk audit. |
| `Confidence`, `Sources` | Bukti saat cache ditulis. |
| `DetectedAtUtc` | Maks. 30 hari. |

"Deteksi Ulang Hardware" (`ForceRefreshHardwareCache`) = hapus file cache + reset memori + deteksi penuh. Sudah demikian perilakunya dan kini juga menghapus cache saat hasil `Unknown` (tidak ada hasil basi yang tertinggal).

## 3. Test matrix (manual)

| # | Hardware | Expected |
|---|---|---|
| 1 | SATA HDD (mode AHCI) | HDD |
| 2 | SATA SSD | SSD |
| 3 | NVMe SSD | SSD |
| 4 | Disk 0 = HDD + Disk 1 = SSD, Windows di HDD | **HDD** |
| 5 | Disk 0 = SSD + Disk 1 = HDD, Windows di SSD | **SSD** |
| 6 | HDD via USB bridge | HDD atau Unknown (bridge tanpa metadata → Unknown sah) |
| 7 | SSD via USB bridge | SSD atau Unknown |
| 8 | Virtual disk (VHD/Hyper-V) | Unknown |
| 9 | RAID/Intel RST tanpa driver AHCI | Unknown jika sumber kontradiksi |
| 10 | Akses IOCTL ditolak / WMI mati | Unknown (bukan HDD/SSD) |

Cara cek per mesin: log `Logs/` cari `Storage decision:` + baris `Mapping:`/`IOCTL`/`WMI`.
Panel System Info menampilkan `Storage: SSD|HDD|Unknown`.

## 4. Pengaruh `Unknown` ke optimasi

- Preset HDD Balanced **tidak** diterapkan saat Unknown (tidak ada bukti) — tidak berbahaya, hanya tanpa tuning I/O.
- Memory trim (RAM<5GB, SSD) hanya jalan saat **Ssd** terkonfirmasi.
- Tidak ada jalur yang memperlakukan Unknown sebagai HDD maupun SSD.
