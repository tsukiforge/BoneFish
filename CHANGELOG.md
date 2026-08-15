# BoneFish Changelog

## v7.2.5 - Game Session: Opt-in Zero Overhead, Manual Flags Persisten, Escape Hatch Tray

Release date: 2026-08-15

### 🔌 Game Session Manager kini opt-in (zero overhead default)

- Master toggle `GameSessionEnabled` (default **OFF**) di paling atas halaman Game Session; seluruh halaman greyed-out saat mati.
- Saat OFF, `BeginSessionAsync()` tidak pernah dipanggil — tidak ada WMI query, tidak ada process scan, tidak ada file write. Launch path identik dengan sebelum v7.2.0 (benchmark: gate boolean ≈ 64 ns/call).
- Rules lama tetap tersimpan dan tidak dieksekusi sampai toggle dinyalakan kembali.

### 🔧 Fix: toggle manual FastFlags hilang setiap kali Play

- `DisableRobloxAnimations` dan `EnableLowMemoryMode` kini berbasis Settings (persisten) dan **re-applied di `finally` `CheckAndApply()`** setelah purge `PurgeAllKnownFlags()` — tidak lagi terhapus tiap klik Play.
- Preset (AutoOptimize/Stable/UltraLow/Balanced/Extreme) menulis flag langsung via AutoOptimizeService, tidak membalik state manual user; purge tetap membersihkan stale flag antar preset (tanpa regresi fix v4.4.0).

### 🛟 Escape hatch manual: tombol "Pulihkan Sekarang" di tray + di dalam app

- Bug yang ditemukan: jika RobloxPlayerBeta tetap hidup di system tray setelah keluar game, proses Roblox tidak pernah mati → `EndSession()` tidak pernah dipanggil → aplikasi yang disuspend beku selamanya.
- Tombol tray "Pulihkan Aplikasi yang Disuspend" (selalu tampil) + tombol "Pulihkan Sekarang" di halaman Game Session → restore manual kapan pun.
- **Rescue scan**: jika catatan sesi (`active.json`) hilang tapi proses masih beku (kasus record hilang), tombol memindai seluruh sistem, probe setiap thread, dan me-resume yang tersuspend.
- Restore kini terikat pada **deteksi keluar-masuk game** (log aktivitas Roblox), bukan hanya kematian proses: keluar game → restore langsung meski proses di tray; masuk game baru dalam proses yang sama → re-suspend otomatis. Proses-mati tetap fallback untuk crash/force-close.
- Resources baru EN + ID; test rescue scan (19 test, semua hijau).

## v7.2.0 - Game Session Manager: Pengganti Optimization Sandbox yang Fail-Safe

Release date: 2026-08-13

### 🗑️ Optimization Sandbox dihapus total

Konsep auto-experiment 5 langkah (Snapshot → Experiment → Classifier → Result →
Apply) dihapus seluruhnya — tidak pernah dirilis karena alur optimasi otomatis
yang menyuspend proses nyata berisiko tinggi untuk perangkat pengguna, dan
hasilnya tidak bisa dipertanggungjawabkan tanpa persetujuan.

### 🎮 Game Session Manager (penggantinya)

- Menyediakan approval per aplikasi untuk proses background selama sesi Roblox.
- Semua rule baru default tidak aktif; tidak ada auto-suspend tanpa persetujuan user.
- Windows, Roblox, BoneFish, dan proses security selalu dilindungi di level kode.
- Detector security yang unavailable/degraded mengaktifkan safe mode dan men-suspend 0 proses.
- Snapshot session menyimpan PID, path, waktu mulai, thread yang diubah, dan rule.
- Restore memvalidasi identitas proses, memeriksa thread state, menyimpan ringkasan, dan melaporkan kegagalan dengan nama aplikasi.
- Sweep suspend memiliki batas keras 5 pass dan timeout 2 detik per proses.
- Tests: classifier, fail-safe detector, suspend cap, restore verification, service, dan atomic store (18 test, semuanya hijau).

### 🧹 Kebersihan repo & website

- README showcase diperbaiki — gambar yang direferensikan (`showcaseDefault.png`, `showcase2/3/4.png`) tidak pernah ada; kini memakai file screenshot asli di `showcase/`.
- Screenshot nyasar `Screenshot_TDR_Mitigation_Toggle.png` di root repo dihapus dari git.

## v7.1.2 - Fix Toggle Hilang Saat Reload + Rekomendasi FastFlag Otomatis

Release date: 2026-08-12

### 🐛 Fix: toggle FastFlag non-aktif setelah reload/restart

Beberapa toggle manual di halaman FastFlags hanya hidup di memory — setter-nya
tidak pernah menulis ke disk, dan penutupan window hanya menyimpan Settings.
Akibatnya setelah BoneFish di-restart, toggle tampak mati padahal tadi aktif.

- **Setter toggle manual kini menyimpan seketika** — MSAA, Rendering Mode,
  Display Scaling, Texture Quality, FRM Quality, Mesh LOD, Animations, Low
  Memory Mode, dan UseFastFlagManager memanggil `App.FastFlags.Save()`
  (atau `App.Settings.Save()`) langsung saat diubah, dibungkus try/catch agar
  tetap aman walau gagal tulis.
- **Jaring pengaman saat window ditutup** — `WpfUiWindow_Closing` kini juga
  menyimpan `App.FastFlags.Save()`, jadi tidak ada perubahan yang hilang lagi.

## v7.0.6 - TDR Mitigation Mode: Kurangi Freeze/Layar Putih (iGPU Legacy)

Release date: 2026-08-10

### 🔬 Latar belakang — TDR terkonfirmasi, jalur update driver buntu

Investigasi log BoneFish + Event Viewer (Event ID 4101 cocok ±1 detik dengan
timestamp stall, `15:10:54Z` vs `15:10:55Z`) mengonfirmasi: freeze "layar putih"
= **Intel iGPU Driver TDR** di Intel HD 4400 (Haswell 2013-2014). Intel tidak
merilis update lagi untuk chip ini (legacy sejak 2018; driver terpasang 2020 =
versi terakhir yang akan pernah ada). Karena jalur update buntu, satu-satunya
mitigasi yang jujur = **menurunkan beban kerja GPU** agar TDR lebih jarang
terpicu.

### 🎛️ Fitur baru — toggle "TDR Mitigation Mode"

Toggle **terpisah** (bukan preset) di halaman Fast Flags, stack dengan preset
visual apa pun, mengikuti pola Fast Loading. Flag yang ditulis (SEMUA diverifikasi
ada di **Fast Flag Allowlist resmi Roblox**, sumber: devforum 3966569 +
repo LeventGameing/allowlist):

| Flag | Nilai | Efek |
|---|---|---|
| `FIntDebugForceMSAASamples` | `1` | MSAA off — beban render per-pixel GPU turun drastis (audit: preset lain sudah set x1; toggle ini menguncinya walau user pilih x2/x4) |
| `DFIntDebugFRMQualityLevelOverride` | `3` | Render quality terendah yang AMAN (FRM=1 pernah bikin game gelap saat dipakai bareng shadow/voxelizer — audit Extreme v7.x) |
| `DFFlagTextureQualityOverrideEnabled` + `DFIntTextureQualityOverride` | `True` + `0` | Texture terendah — bandwidth VRAM & RAM bersama iGPU turun |
| `DFIntTaskSchedulerTargetFps` | `30` | Cap konsisten, di-re-apply PALING AKHIR di semua alur preset (tidak ada gap/race saat transisi) |

**Flag yang TIDAK dipakai setelah riset (sengaja, bukan lupa):**
- `DFIntDebugDynamicRenderKiloPixels` — satu-satunya flag penurun resolusi render
  internal; ❌ **tidak ada di allowlist & di-veto Roblox** ("It was vetoed
  internally, as the aim is to remove FFlags, not add more" — Bitdancer,
  devforum 3966569, 2026-04-17). Menulisnya = dead code.
- `DFIntTaskSchedulerTargetFps` ❌ tidak ada di allowlist sejak 2025-09-29 —
  client modern mengabaikannya (pengganti: `GlobalBasicSettings_13.xml`
  `FramerateCap`). Tetap ditulis demi konsistensi nilai preset yang ada.

### ✅ Jujur soal batasan

- Toggle ini **MENGURANGI frekuensi** TDR, **bukan menghilangkan total** — akar
  masalah di driver GPU legacy, di luar kendali FastFlag apa pun.
- Tanpa resolusi-render-scaling (diblokir Roblox), load reduction maksimum yang
  bisa dicapai = kombinasi MSAA off + FRM rendah + texture rendah + cap konsisten.

### 📊 Validasi oleh user (WAJIB manual — kode tidak bisa klaim sendiri)

Bandingkan frekuensi baris **`RENDER STALL TERDETEKSI`** di log BoneFish
(Logs/bonefish.log) sebelum vs sesudah toggle ini aktif, selama beberapa sesi
main. Baris log stall kini menyertakan `TdrMitigation=true/false` agar
perbandingannya mudah.

### 🛡️ Perbaikan pendukung

- **Re-entrancy guard** di semua alur apply preset — dua klik preset dalam waktu
  singkat tidak lagi bisa berjalan bersamaan (race condition yang bisa membuat
  flag/FPS cap dari flow lama "resurrect" saat transisi antar preset).
- Nilai flag sebelum toggle aktif di-snapshot & direstorasi saat toggle dimatikan
  (tidak merusak pilihan user/preset sebelumnya; persisten melewati restart).

---

Release date: 2026-08-09

### 🧩 Perbaikan utama - preset tidak saling menimpa lagi

- Saat **Force Extreme Mode** aktif di perangkat HDD/low-end, kedua mode sekarang **bergabung**: nilai LOD & compositor pekerjaan dari mode HDD tetap dipertahankan, tidak lagi ditimpa total oleh mode Extreme generik yang buta terhadap bottleneck disk. Hasilnya FPS pada kombinasi Extreme+HDD lebih berdasarkan.
- Panel System Info kini menampilkan **dua keterangan terpisah**: "Tier Asli" (kondisi perangkat sebenarnya) dan "Tier Efektif" (mode yang sedang aktif karena Force Extreme Mode) — tidak ada lagi kebingungan apa yang sedang diterapkan.

### ✨ Pembersihan & konsistensi

- Flag yang tidak lagi didukung client (berdasar daftar allowlist resmi Roblox) dibersihkan secara otomatis saat boot — konfigurasi lebih bersih, perilaku kaku dan terprediksi, dan aplikasi tetap kompatibel dengan update Roblox terbaru.

---

## v7.0.2 - Kombinasi Preset Aman: LOD Sadar-HDD + Fast Loading Prioritas + Pembersih Flag Non-Allowlist

Release date: 2026-08-08

### What's new

- **Extreme + HDD/Low-End sekarang HDD-aware** — jarak LOD dan jumlah compositor menyesuaikan jenis penyimpanan (HDD), sehingga kombinasi Force Extreme tidak menurunkan FPS pada perangkat kentang.
- **Fast Loading Toggle tidak lagi hilang diam-diam** saat relaunch — penambahan prioritas tertinggi di setiap boot, apa pun preset aktif.
- **Pembersihan flag non-allowlist** — flag yang diabaikan oleh client modern (di luar daftar allowlist resmi yang aktif sejak 2025) dihapus dari preset; nilai lama tetap dibersihkan secara otomatis.

---

## v7.0.1 — Audit White-Screen: Memory Trim Aman di HDD + Render-Stall Detector 🛡️

Release date: 2026-08-07

### 🔍 Konteks — Investigasi "Freeze → Layar Putih → Pulih Sendiri"

Audit mendalam terhadap gejala: **Roblox jalan normal → freeze → window jadi putih total → pulih setelah beberapa detik → game lanjut**. Target device: i3 Gen 4, Intel HD 4400, 8GB RAM, HDD, Windows 10.

**Kesimpulan audit (diurutkan dari yang paling mungkin):**

| # | Penyebab | Status |
|---|----------|--------|
| 1 | **Intel iGPU Driver TDR** (Timeout Detection & Recovery) — GPU tidak merespons >2 detik, driver di-reset oleh Windows, render context dibuat ulang. Roblox TETAP hidup (bukan crash). Konfirmasi: Event Viewer → System → **Event ID 4101** | 🥇 LIKELY |
| 2 | **Texture streaming di HDD + RAM 8GB dipakai bareng iGPU** (shared memory) — render thread menunggu aset dari HDD → stall/putih sampai aset selesai | 🥈 LIKELY |
| 3 | **BoneFish sebagai amplifier** — flag frame-pacing bisa membuat stall lebih terasa, tapi tidak memicu TDR | 🥉 POSSIBLE |
| 4 | Memory trimming & CPU affinity — **TIDAK aktif di device 8GB/4-core** (gated <5GB dan <=2 core) | ❌ UNLIKELY |

**Kesimpulan penting:** Tidak ada satu pun FastFlag BoneFish yang terbukti memicu white screen. Malah sebaliknya — flag low-end (texture quality 0, SSAO off, FPS cap 30) menurunkan beban GPU/RAM dan cenderung **mengurangi** frekuensi TDR.

### 🔧 FIX 1 — Memory Trim Kini Hanya di SSD

**Sebelum:** `EmptyWorkingSet()` dipanggil ke SEMUA proses background >20MB (kecuali Roblox & sistem) saat RAM < 5GB — **termasuk di HDD**. Di HDD, proses yang di-trim harus page-in BALIK dari disk tepat saat game baru launch (fase loading aset paling kritis) → disk storm yang bisa memperparah stall render.

**Sesudah:** `AutoOptimizeService.OptimizeRobloxProcess()` kini gate `if (totalMemGB < 5 && IsSSD())` — trimming hanya jalan di SSD (page-in hampir instan, aman). Device HDD + RAM rendah terlindungi.

### 🛡️ FIX 2 — Render-Stall Detector (Diagnostik, Tanpa Auto-Restart)

Detektor ringan baru di `Watcher.cs` yang berjalan di loop tunggu Roblox exit (1 cek/detik):

- **Deteksi hang vs crash:** proses Roblox MASIH HIDUP + window tidak merespons pesan Windows (`IsHungAppWindow`, baru true setelah ~5 detik tidak responsif)
- Setelah **3 tick berturut-turut** (~5-7 detik hang nyata, menekan false positive) → tulis 1 baris diagnostik ke log: PID, window handle, working set, preset aktif, OptimizeForLowEnd, FakeBorderless, FpsMonitor, Crosshair, + info sistem (CPU/RAM/Storage/Tier via `GetSystemInfo()`)
- Saat pulih → dicatat durasi stall-nya
- **HANYA mencatat — TIDAK me-restart Roblox, TIDAK mengubah FastFlag saat stall** (prinsip: kumpulkan bukti dulu)
- Jika window handle = 0 (edge case) → log sekali bahwa detector inactive
- P/Invoke baru `IsHungAppWindow` ditambahkan ke `NativeMethods.txt` (pola CsWin32 project)

**Cara pakai diagnostik:** main seperti biasa → saat white screen terjadi → buka log (`%LocalAppData%\BoneFish\Logs\` atau folder install `Logs/`) → cari baris **`RENDER STALL TERDETEKSI`**. Bandingkan dengan Event ID 4101 di Event Viewer untuk konfirmasi TDR.

### ✅ Verifikasi

- Build **0 error, 0 warning** ✅
- Code review: loop semantics tidak berubah, thread-safe (user32 via SendMessageTimeout), reuse `GetSystemInfo()` ✅
- Tidak ada dead code ✅
- Semua fungsionalitas existing dipertahankan (preset, FastFlags, launcher, cleanup) ✅

### Files Changed (3 files)

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Integrations/AutoOptimizeService.cs` | Memory trim gated: `totalMemGB < 5` → `totalMemGB < 5 && IsSSD()` |
| `Bloxstrap/NativeMethods.txt` | +`IsHungAppWindow` |
| `Bloxstrap/Watcher.cs` | +Render-Stall Detector (`WaitForRobloxTick()`, `LogRenderStallDiagnostic()`), kedua loop tunggu Roblox memakainya |

---

## v7.0.0 — Fix Krusial: Freeze "Not Responding" saat Hapus + Jaringan Low-End "Sekelas NASA" 🚀

Release date: 2026-08-06

### 🐛 Bug Krusial — Klik "Hapus" → Klik "Yes" → BoneFish Langsung "Not Responding"

**Gejala:** Setelah user menekan tombol Hapus/Bersihkan (mis. Clear ClientAppSettings, Bersihkan Roblox Player/Studio, Import Mod, Hapus Custom Theme) lalu mengkonfirmasi dengan "Yes", aplikasi langsung membeku dan Windows menampilkan judul window "Not Responding".

**Akar masalah:** Semua operasi penghapusan dijalankan **synchronous di UI thread**:

| Operasi | Biang Kerok Freeze |
|---------|-------------------|
| `RobloxCleanupService.Cleanup()` | `CalculateDirectorySize()` + `Directory.Delete()` folder Roblox **GB-an** + `process.Kill()/WaitForExit(3000)` per proses |
| `ClearClientAppSettings()` (FastFlags) | `CleanupLegacyRobloxFlags()` scan SEMUA folder `Roblox/Versions/version-*` + baca/tulis JSON tiap folder |
| `ImportMod()` (Mods) | Ekstrak zip + copy ribuan file ke folder Modifications |
| `DeleteCustomTheme()` (Appearance) | `Directory.Delete(dir, true)` recursive |
| 5 tombol preset performa (FastFlags) | `CleanupLegacyRobloxFlags()` + `PurgeAllKnownFlags()` + `Save()` — pola freeze yang SAMA |

Di HDD dengan banyak versi Roblox, scan + delete ini butuh beberapa detik — cukup lama sampai Windows menandai window sebagai "Not Responding".

**Perbaikan — semua operasi disk dipindah ke background thread (`Task.Run`):**

| File | Perubahan |
|------|-----------|
| `RobloxCleanupService.cs` | `CleanPlayer()`/`CleanStudio()` → `CleanPlayerAsync()`/`CleanStudioAsync()`. Process-kill (`Kill` + `WaitForExit`) DAN `Directory.Delete` GB-an dipindah ke `Task.Run`. Nilai `RobloxState` di-snapshot di UI thread sebelum background work; mutasi state + `Save()` dikembalikan ke UI thread setelah `await`. |
| `ChannelViewModel.cs` | Command `CleanPlayer`/`CleanStudio` → `AsyncRelayCommand` |
| `FastFlagsViewModel.cs` | `ClearClientAppSettings()` → async, disk scan di `Task.Run`. **Bonus:** 5 preset method (`ApplyRecommendedFastFlags`, `ApplyRecommendedStabilityPreset`, `ApplyUltraLowSpecPreset`, `ApplyBalancedPreset`, `ApplyExtremePerformancePreset`) juga di-async-kan — pola freeze yang sama ikut diperbaiki. |
| `ModsViewModel.cs` | `ImportMod()` → async. Ekstrak zip, cek overwrite, dan copy file semua di `Task.Run`; dialog tetap di UI thread. |
| `AppearanceViewModel.cs` | `DeleteCustomTheme()` → async, `Directory.Delete` di `Task.Run` |

Dialog konfirmasi & notifikasi (`Frontend.ShowMessageBox`) **tetap di UI thread** — hanya operasi disk yang dipindah, jadi UX tidak berubah.

### 🚀 Improve — Jaringan "Sekelas NASA" di Path Low-End

**Gap yang ditemukan:** `ApplyAggressiveOptimizations()` (path auto-detection low-end) memanggil `PurgeAllKnownFlags()` di awal — yang **menghapus flag network** (`FIntRakNetPacketRateLimit`, `DFIntMaxReceivePPS`, `DFIntMaxSendPPS`, `DFIntConnectionMTUSize`, `DFIntOptimizeSendQueue`) — tapi **tidak pernah meng-apply ulang**. Akibatnya user LowEnd/UltraLow yang TIDAK pilih preset manual justru **kehilangan** optimasi jaringan, padahal preset manual (UltraLow, Balanced, dst) selalu memakainya.

**Fix:** `ApplyNetworkOptimizations()` sekarang dipanggil di akhir `ApplyAggressiveOptimizations()` — semua path low-end (auto-detection, HDD Balanced, Turbo Mode) mendapat network boost yang sama:

```
FIntRakNetPacketRateLimit = 50000   (rate limit paket dinaikkan)
DFIntMaxReceivePPS        = 50000   (paket terima per detik maksimal)
DFIntMaxSendPPS           = 50000   (paket kirim per detik maksimal)
DFIntConnectionMTUSize    = 1500    (MTU maksimum ethernet standar)
DFIntOptimizeSendQueue    = 1       (antrian kirim dioptimalkan)
```

**Catatan verifikasi:** Nilai-nilai ini adalah set standar komunitas yang sudah terverifikasi (dipakai semua preset sejak v6.0.0). `MTU=1500` adalah maksimum ethernet standar; `50000 PPS` adalah rekomendasi tertinggi komunitas. Flag tambahan lain (`DFIntRakNetResendBufferSize`, `DFIntMaxIncomingDataSize`, dll) TIDAK ditambahkan karena belum terverifikasi di Allowlist Roblox resmi — konsisten dengan constraint project untuk tidak menebak nama flag.

### ✅ Verifikasi

- Build 0 error, 0 warning ✅
- Semua flow Hapus → Yes sekarang async — UI tetap responsif ✅
- Dialogs tetap di UI thread (tidak ada perubahan UX) ✅
- Tidak ada dead code / caller lama (`CleanPlayer()`/`CleanStudio()` sync dihapus) ✅

### Files Changed (6 files)

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Integrations/RobloxCleanupService.cs` | Async + `Task.Run` untuk kill proses & delete GB-an |
| `Bloxstrap/UI/ViewModels/Settings/ChannelViewModel.cs` | `AsyncRelayCommand` untuk Clean Player/Studio |
| `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs` | `ClearClientAppSettings` + 5 preset → async |
| `Bloxstrap/UI/ViewModels/Settings/ModsViewModel.cs` | `ImportMod` → async |
| `Bloxstrap/UI/ViewModels/Settings/AppearanceViewModel.cs` | `DeleteCustomTheme` → async |
| `Bloxstrap/Integrations/AutoOptimizeService.cs` | +`ApplyNetworkOptimizations()` di path low-end |

---

## v6.3.1 — Fix: Icon Sidebar Tofu (Global Font Override) 🔤

Release date: 2026-07-22

### 🐛 Bug — Global TextBlock Style Merusak SymbolIcon

Global `Style TargetType="TextBlock"` di App.xaml meng-override `FontFamily` untuk **semua** TextBlock,
termasuk yang dipakai internal WPF UI's `SymbolIcon` untuk render icon glyph (Segoe Fluent Icons).
Karena JetBrains Mono tidak punya glyph icon, semua ikon sidebar muncul sebagai kotak kosong (tofu).

### 🔧 Fix — Ganti dari Implicit Style ke Attached Property Inheritance

**Akar masalah:**
- Implicit Style (`Style TargetType="TextBlock"`) → precedence **lebih tinggi** dari TemplatedParent template properties
- SymbolIcon internal template set `FontFamily="Segoe Fluent Icons"` via TemplatedParent — tapi kalah sama implicit style

**Perbaikan:**
| Sebelum (❌) | Sesudah (✅) |
|---|---|
| `App.xaml`: `Style TargetType="TextBlock"` (blanket override) | **Dihapus** |
| `MainWindow.xaml` root Grid | +`TextElement.FontFamily="{StaticResource JetBrainsMonoRegular}"` |
| 12 file halaman Pages/*.xaml (root `ui:UiPage`) | +`TextElement.FontFamily="{StaticResource JetBrainsMonoRegular}"` |

**Mengapa ini bekerja:**
`TextElement.FontFamily` adalah inherited attached property — prioritinya **lebih rendah** dari eksplisit
`FontFamily="Segoe Fluent Icons"` yang di-set di template `SymbolIcon`. Regular TextBlocks tanpa FontFamily
eksplisit mewarisi JetBrains Mono, sementara SymbolIcon tetap pakai Segoe Fluent Icons.

### ✅ Verifikasi

- Semua 10 ikon sidebar muncul normal (bukan kotak kosong) ✅
- Font JetBrains Mono di teks label/heading tetap utuh di semua halaman ✅
- Build 0 error, 0 warning ✅

### Files Changed (14 files)

| File | Perubahan |
|------|-----------|
| `Bloxstrap/App.xaml` | Hapus `Style TargetType="TextBlock"` global |
| `Bloxstrap/UI/Elements/Settings/MainWindow.xaml` | +`TextElement.FontFamily` di root Grid |
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/ExperimentalPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/IntegrationsPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/BootstrapperPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/ChannelPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/AppearancePage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/ShortcutsPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/ModsPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagEditorPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagEditorWarningPage.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsDisabled.xaml` | +`TextElement.FontFamily` |
| `Bloxstrap/UI/Elements/Settings/Pages/GlobalSettingsPage.xaml` | +`TextElement.FontFamily` |

---

## v6.3.0 — Visual Identity: JetBrains Mono Global + Lucide-Style Icons 🎨

Release date: 2026-07-22

### 🎨 Font Global — JetBrains Mono di Seluruh UI

Sebelumnya font JetBrains Mono cuma dipasang di 4 tempat spesifik (FPS Monitor, dialog error, hotkey display).
Sekarang **default global** via `Style TargetType="TextBlock"` di App.xaml — seluruh teks di semua halaman:
- Sidebar navigasi
- Tombol & label
- Halaman Settings (FastFlags, Integrations, Behaviour, dll)
- Dialog, snackbar, breadcrumb

Font Inter (Regular, Medium, SemiBold, Light) dihapus dari folder Resources/Fonts/.

### 🎯 Sidebar Navigasi — Icons Diperbarui

Semua 10 ikon sidebar navigasi di-update dengan nama `SymbolRegular` enum yang benar
setelah WPF UI submodule di-bump (Fluent System Icons v1.1.181):

| Section | Icon |
|---------|------|
| Integrations | `GlobeDesktop20` |
| Behaviour | `ControlButton24` |
| Deployment | `Cloud28` |
| Mods | `BoxToolbox24` |
| Appearance | `PaintBrushArrowDown24` |
| Shortcuts | `AppsListDetail24` |
| FastFlags | `Flag28` |
| GlobalSettings | `DesktopToolbox20` |
| Experimental | `Beaker24` |
| About | `Info28` |

### 📖 README — Atribusi Font

- **Key Features**: "Inter font" → "JetBrains Mono (monospace) font"
- **Special Thanks**: rsms/Inter → JetBrains Mono (SIL OFL)
- **Technology Stack**: SharpVectors note → mention Lucide Icons

### 🎨 Color Scheme — Reduced Accent Dominance

Style `NavigationItem` ditambahkan di Dark.xaml dengan `Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"`
— mengurangi dominasi warna aksen di sidebar navigasi.

### 🧹 Pembersihan

- Font Inter (4 file .ttf) dihapus dari `Resources/Fonts/`
- 6 SVG broken/empty files dihapus dari `Resources/Icons/`
- Dead entries di csproj dibersihkan

### Files Changed

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Bloxstrap.csproj` | Version 6.1.0 → **6.3.0**, font refs, SVG resources |
| `Bloxstrap/App.xaml` | +`Style TargetType="TextBlock"` → JetBrains Mono global |
| `Bloxstrap/UI/Elements/Settings/MainWindow.xaml` | Ikon sidebar: nama SymbolRegular di-update (WPF UI submodule bump) |
| `Bloxstrap/UI/Style/Dark.xaml` | +`xmlns:ui`, NavigationItem style |
| `Bloxstrap/UI/Elements/FpsMonitorOverlay.xaml` | Courier New → JetBrainsMono |
| `Bloxstrap/UI/Elements/Dialogs/ExceptionDialog.xaml` | Courier New → JetBrainsMono |
| `Bloxstrap/UI/Elements/Dialogs/ConnectivityDialog.xaml` | Courier New → JetBrainsMono |
| `Bloxstrap/UI/Elements/Settings/Pages/ExperimentalPage.xaml` | Courier New → JetBrainsMono (hotkey) |
| `README.md` | Key Features, Special Thanks, Tech Stack updated |
| `Resources/Fonts/` | Inter .ttf dihapus, JetBrainsMono .ttf ditambahkan |
| `Resources/Icons/` | 38 Lucide SVG icons |

---

## v6.1.0 — Auto-Reconnect + Documentation Polish 📝

Release date: 2026-07-22

### 🚀 Fitur Baru — Auto-Reconnect Setelah Crash

Saat Roblox crash (exit code != 0 dan negatif / NTSTATUS error), BoneFish sekarang mendeteksi dan menawarkan tombol "Sambung Ulang":

- **Deteksi crash via exit code** — Exit code 0 = normal (user klik Leave/X). Negatif (e.g. -1073741819 / 0xC0000005) = crash.
- **Session duration guard** — Hanya trigger jika sesi > 2 menit (hindari false trigger pas Roblox gagal buka).
- **Notifikasi system tray** — Balloon notification + klik untuk sambung ulang ke server yang sama (PlaceId + JobId).
- **Fallback server baru** — Jika JobId sudah tidak valid, join server baru di PlaceId yang sama.
- **Manual only** — User harus klik notifikasi / tombol Yes. Tidak auto-rejoin.
- **Setting toggle** — `EnableAutoReconnectPrompt` (bool, default true) di Settings.

### 📖 Documentation — v7.0.0 Preparation

#### 📜 License Fix

**Kontradiksi diperbaiki:**
- `LICENSE`: Copyright "returnrqt" → **BoneFishStudio** (+ atribusi Bloxstrap)
- `LICENSE.Bloxstrap`: **NEW** — file lisensi terpisah untuk original Bloxstrap (MIT, pizzaboxer)
- `README.md` TL;DR: ❌ "Commercial use not allowed" → ✅ "Free for any use (personal & commercial)" — **MIT-compliant**

#### 🔗 Badge & Link Updates

Semua link `faizinuha/BoneFish` → **BoneFishStudio/BoneFish** di README.md dan YOUTUBE-CONTENT-SCRIPT.md.

**Badge statis → dinamis (shields.io):**
| Badge | Sebelum | Sesudah |
|-------|---------|--------|
| Version | `v3.5.0` hardcoded | `github/v/release` — auto dari tag |
| Downloads | `10k+` hardcoded | `github/downloads/total` — auto dari API |
| Build | `build-passing` statis | `github/actions/workflow/ci-release.yml` — real-time |
| License | `BoneFish` custom | `MIT` standar |
| Stars | `faizinuha` | `BoneFishStudio` ✅ |

#### 🌐 Website Link

Tombol "Visit Website" → [https://bonefishstudio.vercel.app](https://bonefishstudio.vercel.app) di header README.

#### ✨ Key Features — Sinkron dengan Kode Terkini

Rewrite total bagian fitur untuk mencakup semua fitur yang sudah ditambahkan:
- 🎯 Crosshair Overlay (4 style, drag-to-move, color/size/opacity)
- ⌨️ Global Hotkeys (Ctrl+Shift+C/F)
- 🚀 Turbo Mode
- 💾 HDD/SSD Auto-Detection + HDD Balanced preset
- 🛡️ Anti Not-Responding System (3-layer)
- 🔄 Auto-Reconnect After Crash
- 🗑️ Auto-Cache Cleaner
- 🔧 Quick Repair Shortcuts
- 🔋 Battery Saver Mode
- 🚀 Fast Loading Toggle
- 💻 System Info Panel
- 🎨 Custom Wallpaper (upload sendiri)
- 📐 2-column responsive layout

### Files Changed

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Bloxstrap.csproj` | Version 6.0.0 → **6.1.0** |
| `Bloxstrap/Models/WatcherData.cs` | +`PlaceId`, +`JobId` fields |
| `Bloxstrap/Bootstrapper.cs` | Pass `_joinData.PlaceId/JobId` ke Watcher |
| `Bloxstrap/Models/Persistable/Settings.cs` | +`EnableAutoReconnectPrompt` |
| `Bloxstrap/Watcher.cs` | +`IsCrashExit()`, crash detection, `RejoinRoblox()` |
| `Bloxstrap/Resources/Strings.resx` | +4 entries (balloon, message, title, desc) |
| `Bloxstrap/Resources/Strings.id.resx` | +4 entries |
| `Bloxstrap/Resources/Strings.Designer.cs` | +4 properties |
| `LICENSE` | Copyright returnrqt → BoneFishStudio |
| `LICENSE.Bloxstrap` | **NEW** — original Bloxstrap MIT license |
| `README.md` | Badges dinamis, link BoneFishStudio, +website, License fix, Key Features rewrite |
| `YOUTUBE-CONTENT-SCRIPT.md` | `faizinuha` → `BoneFishStudio` |

---

## v6.0.0 — Network Refactor, Fast Loading Toggle, Anti Not-Responding Audit + Night Vision Removal 🧹

Release date: 2026-07-20

### ♻️ Refactor — Network Flags Ekstrak ke Method Reusable

5 lokasi duplikasi `App.FastFlags.SetValue("...")` untuk flag network (FIntRakNetPacketRateLimit, DFIntMaxReceivePPS, DFIntMaxSendPPS, DFIntConnectionMTUSize, DFIntOptimizeSendQueue) diekstrak ke `AutoOptimizeService.ApplyNetworkOptimizations()`. Semua 5 preset (AutoOptimize, Stable, UltraLow, Balanced, ExtremePerformance) panggil method ini — **100% identik, zero drift.**

| Sebelum | Sesudah |
|---------|--------|
| 5× copy-paste 7 baris | 1× method reusable |
| Risiko: update nilai di 1 tempat lupa 4 lainnya | Update di 1 tempat, semua preset ikut |

### 🚀 Fitur Baru — Fast Loading Toggle

Toggle independen baru **"Fast Loading (Percepat Muncul Aset)"** di halaman FastFlags (kolom kanan, setelah Force Extreme Mode):

- **DFIntTextureCompositorActiveJobs=2** — jika cpuCores >= 4
- **FIntRuntimeMaxNumOfThreads=6** — jika cpuCores >= 8
- **Stack dengan preset apa pun** — bukan preset berdiri sendiri, tapi toggle tambahan
- **Persisten** — state disimpan di `Settings.EnableFastLoadingFlags`
- **Re-apply otomatis** — setiap kali preset diterapkan, Fast Loading flags di-set ulang jika toggle ON

**Flag yang TIDAK dipakai (ditemukan melalui riset Allowlist):**
- `FFlagEnableAsyncResourceLoading` ❌ Tidak di Allowlist
- `FIntRenderChunkLODThreshold` ❌ Tidak di Allowlist
- `FFlagEnableTextureStreamingFix` ❌ Tidak di Allowlist
- `FIntPartSizeBoostThreshold` ❌ Tidak di Allowlist

### 📋 Audit — Anti Not-Responding (Long Session) — Laporan Lengkap

Dokumentasi lengkap di XML comment `ApplyExtremePerformancePreset()`:

| Aspek | Temuan |
|-------|--------|
| **Status ACTIVE** | Manual only (button click). Tidak auto-activate. |
| **Visual flags** | `FFlagDebugSSAOForce=False`, `FIntSSAOMipLevels=0`, `FIntRobloxGuiBlurIntensity=0`, `FIntRenderGrainScale=0` — efek kosmetik, tidak gameplay-breaking |
| **Anti-freeze flags** | `DFIntMaxActiveAnimationTracks=32`, 7 telemetry flags, `FIntRenderLocalLightFadeInMs=0` |
| **Keputusan** | Tidak dibuat toggle terpisah. Preset ini SATU PAKET agresif untuk target low-end extreme (dual-core, <4GB RAM). |

### 🧹 Pembersihan — Night Vision Dihapus Total

`FFlagFastGPULightCulling3` dan `FFlagNewLightAttenuation` sudah **deprecated** sejak September 2025 (Roblox Allowlist). Client Roblox abaikan flag ini. Semua kode dihapus:
- `Settings.EnableNightVision` → dihapus
- `NightVisionEnabled` property → dihapus
- `ToggleNightVision()` + `ToggleNightVisionCommand` → dihapus
- `Ctrl+Shift+N` dari HotkeyService → dihapus
- `NightVisionEnabled = false` dari semua preset method → dihapus

### 🎯 Crosshair — Preview Panel Real-Time

Panel preview crosshair di halaman FastFlag New menampilkan perubahan style/size/opacity/color secara real-time via binding ke `CrosshairColor` property baru.

### 📐 Layout — Redesign 2 Kolom (Responsive)

3 halaman Settings di-redesign dari 1 kolom panjang jadi 2 kolom kiri-kanan:
- **AppearancePage** (232→2 kolom): Theme+Language kiri, Bootstrapper+Custom kanan
- **ChannelPage** (191→2 kolom): Fishstrap kiri, Channel info kanan
- **IntegrationsPage** (162→2 kolom): Activity Tracking kiri, Discord RPC kanan

**Tidak diubah** (konten pendek/cukup 1 kolom): Mods(114), Bootstrapper(131), FastFlagEditor(100), Shortcuts(89), GlobalSettings(90). Responsive fallback otomatis ke 1 kolom jika lebar window < 700px.

### 🔧 GAP Fixes Lainnya

| GAP | Fix |
|-----|-----|
| **GAP 1** — HotkeyService leak | Dikonfirmasi pakai HwndSource (bukan Thread). `IsWatcherRunning()` + `CleanupServices()` sudah terpasang. |
| **GAP 2** — Custom Loading Screen | Flow diverifikasi: Pilih gambar → `NewState` → `Execute()` → copy file ✅. Tidak ada bug. |
| **GAP 5** — Crosshair drag-to-move | Kode `Window_MouseDown/Move/Up` + `SavePosition()` diverifikasi ✅. Hanya 1 instance setelah FIX 1. |

### Files Changed

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Bloxstrap.csproj` | Version 5.5.3 → **6.0.0** |
| `Bloxstrap/Integrations/AutoOptimizeService.cs` | +3 methods: `ApplyNetworkOptimizations()`, `ApplyFastLoadingFlags()`, `RemoveFastLoadingFlags()` |
| `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs` | Refactor 5→1 network call. +`EnableFastLoadingFlags`. +Audit doc di `ApplyExtremePerformancePreset()`. Night Vision dihapus. |
| `Bloxstrap/Models/Persistable/Settings.cs` | +`EnableFastLoadingFlags` (bool, default false) |
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml` | +Toggle Fast Loading |
| `Bloxstrap/UI/Elements/Settings/Pages/AppearancePage.xaml` | Redesign 2 kolom |
| `Bloxstrap/UI/Elements/Settings/Pages/ChannelPage.xaml` | Redesign 2 kolom |
| `Bloxstrap/UI/Elements/Settings/Pages/IntegrationsPage.xaml` | Redesign 2 kolom |
| `Bloxstrap/Resources/Strings.resx` | +2 entries Fast Loading |
| `Bloxstrap/Resources/Strings.id.resx` | +2 entries Fast Loading |

---

## v5.5.3 — Fix: FastFlags ComboBox Reset + ProductVersion Build Config

Release date: 2026-07-18

### 🐛 FastFlags Enum ComboBox — Nilai Tidak Bertahan

**Bug:** Saat FastFlags MSAA / RenderingMode / TextureQuality telah diaktifkan, membuka halaman FastFlags kembali membuat kontrol terlihat nonaktif/ter-reset ke posisi default.

**Root Cause:** Binding `Text` pada `ComboBox` untuk tipe enum dapat menyebabkan nilai ter-parse ulang dan mengubah state yang sudah disimpan saat halaman dibuka ulang.

**Fix:** Ganti `Text="{Binding ...}"` → `SelectedItem="{Binding ...}"` pada 3 ComboBox:

| ComboBox | Properti Binding |
|----------|-----------------|
| MSAA Level | `SelectedMSAALevel` |
| Rendering Mode | `SelectedRenderingMode` |
| Texture Quality | `SelectedTextureQuality` |

Ketiganya kini konsisten menggunakan `SelectedItem`, menjaga nilai enum tetap stabil saat halaman dibuka ulang.

### 🔧 ProductVersion — Selalu Terisi di Semua Build Config

**Bug:** `ProductVersion` pada binary kadang kosong di Debug build, menyebabkan logic `HandleUpgrade()` di `Installer.cs` salah mendeteksi versi dan memunculkan dialog version mismatch.

**Fix:** Tambahkan `AssemblyVersion` dan `AssemblyInformationalVersion` eksplisit di csproj:

```xml
<AssemblyVersion>$(Version)</AssemblyVersion>
<AssemblyInformationalVersion>$(Version)</AssemblyInformationalVersion>
```

Dengan `$(Version)`, cukup update `<Version>` di satu tempat — `AssemblyVersion` dan `AssemblyInformationalVersion` ikut otomatis. Ini memastikan `FileVersionInfo.GetVersionInfo().ProductVersion` selalu mengembalikan nilai yang benar di semua build config (Debug, Release, CI/CD).

### Files Changed

| File | Perubahan |
|------|-----------|
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml` | 3 ComboBox: `Text` → `SelectedItem` |
| `Bloxstrap/Bloxstrap.csproj` | +`<AssemblyVersion>`, +`<AssemblyInformationalVersion>` |

---

## v5.5.0 — Plan 1 Fixes + Plan 2: 4 Fitur Baru 🚀

Release date: 2026-07-17

### 🔧 Plan 1 — FastFlag New Bug Fixes

#### 1. 🔑 Global Hotkey Tidak Berfungsi
- **🐛 Isu:** `HotkeyService` cuma berjalan di Watcher process. Toggle ON/OFF di Settings cuma nyimpen setting, gak benar-benar start/stop service.
- **🔧 Fix:** `ExperimentalViewModel.EnableHotkeys` sekarang start/stop `HotkeyService` langsung dari Settings UI — gak perlu nunggu Watcher process.

#### 2. 🎯 Crosshair Tidak Muncul
- **🐛 Isu:** `CrosshairService.Instance` null di Settings process karena Watcher belum jalan.
- **🔧 Fix:** `ExperimentalViewModel.EnableCrosshair` sekarang create & start `CrosshairService` secara lokal jika Instance null.

#### 3. 🖼️ Wallpaper Tidak Terlihat di UI
- **🐛 Isu:** `BackgroundImage` property ada di ViewModel tapi **tidak di-bind ke XAML** — wallpaper gak pernah tampak.
- **🔧 Fix:** Ditambahkan `ImageBrush` binding + `ProgressBar` loading indicator. Background sekarang tampil dengan opacity 0.15.
- **🐛 Plus: Wallpaper Tidak Full Screen** — background cuma selebar konten karena di-daleman ScrollViewer.
- **🔧 Fix:** Restructure page layout: manual `Grid` + `ScrollViewer` — background sekarang full viewport! ✅

#### 4. 🧹 Wallpaper Tidak Hapus Saat Dinonaktifkan
- **🔧 Fix:** `AppBackgroundService.ClearCache()` dipanggil saat disable. Random mode mati → restore saved background.

#### 5. 📂 Custom Background Fallback `images/img/`
- **🔧 Fix:** Folder `images/img/` otomatis discan sebagai fallback custom background. Browse default ke folder ini.

---

### 🚀 Plan 2 — 4 Fitur Baru

#### Fitur A 🔧 — Repair Shortcut Otomatis
- **Method baru** `Installer.RepairShortcuts()` — verifikasi target shortcut Desktop & Start Menu, recreate jika rusak/salah path
- **UI:** Tombol "🔧 Perbaiki Sekarang" di halaman **Shortcuts** + snackbar notifikasi hasil
- **Manual action** — tidak otomatis di background, user klik sendiri saat butuh

#### Fitur B 🗑️ — Auto-Cleaner Cache Roblox
- **Service baru** `CacheCleanerService.cs` — two-phase cleanup:
  1. Hapus file lebih tua dari `maxAgeDays` (default 14 hari)
  2. Jika total ukuran > 500MB, hapus file terlama sampai di bawah limit
- **Safety:** Cek dulu apakah Roblox sedang berjalan — skip cleanup jika ada proses Roblox aktif
- **Startup:** Non-blocking `Task.Run` di `App.xaml.cs.OnStartup()` — jalan sekali per sesi
- **Settings baru:** `EnableAutoCacheCleanup` (bool, default true), `CacheCleanupMaxAgeDays` (int, default 14)
- **UI:** Toggle "Bersihkan cache otomatis" di halaman **FastFlag New** (Cache & Storage section)

#### Fitur C 💻 — Panel System Info di UI
- **Card baru** di halaman **FastFlags** (sebelum InfoBar warning) menampilkan:
  - CPU Cores, RAM terdeteksi, Storage type (HDD/SSD), System Tier
- **Data** dari `AutoOptimizeService.GetSystemInfo()` (reuse method existing)
- **Auto-refresh** setiap kali `RequestPageReloadEvent` terpicu melalui `OnRequestPageReload()`

#### Fitur D 🔋 — Mode Hemat Baterai untuk Auto-Wallpaper
- **Helper baru** `BatteryHelper.cs` — deteksi status baterai via `System.Windows.Forms.SystemInformation.PowerStatus`
- **Logic:** Jika `EnableBatterySaverForWallpaper` ON + laptop tidak dicharge + ada baterai → skip auto-refresh wallpaper
- **Fallback:** PC desktop (tanpa baterai) — toggle **otomatis tersembunyi** dari UI
- **UI:** Toggle "Hemat Baterai (Auto-Wallpaper)" di halaman **FastFlag New** — hanya muncul jika `HasBattery() == true`

---

### Files Changed (15 files)

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Bloxstrap.csproj` | Version 5.3.0 → **5.5.0** |
| `Bloxstrap/Models/Persistable/Settings.cs` | +3 settings: `EnableAutoCacheCleanup`, `CacheCleanupMaxAgeDays`, `EnableBatterySaverForWallpaper` |
| `Bloxstrap/Integrations/CacheCleanerService.cs` | **NEW** — auto cache cleanup service |
| `Bloxstrap/Utility/BatteryHelper.cs` | **NEW** — battery detection utility |
| `Bloxstrap/Installer.cs` | +`RepairShortcuts()` method |
| `Bloxstrap/App.xaml.cs` | +Auto cache cleanup di startup |
| `Bloxstrap/Integrations/AppBackgroundService.cs` | +Fallback `images/img/` scanning |
| `Bloxstrap/Paths.cs` | +`CustomImagesDir`, +`GetCustomUserImages()` |
| `Bloxstrap/UI/ViewModels/Settings/ExperimentalViewModel.cs` | Fix hotkey/crosshair/wallpaper + battery saver + cache cleanup toggle |
| `Bloxstrap/UI/Elements/Settings/Pages/ExperimentalPage.xaml` | Restructure full-screen background + battery/cache toggles |
| `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs` | +`SystemInfoText`, +`OnRequestPageReload()` |
| `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml` | +System Info card |
| `Bloxstrap/UI/ViewModels/Settings/ShortcutsViewModel.cs` | +`RepairShortcutsCommand`, +`RequestNotificationEvent` |
| `Bloxstrap/UI/Elements/Settings/Pages/ShortcutsPage.xaml` | +Repair button + snackbar |
| `Bloxstrap/UI/Elements/Settings/Pages/ShortcutsPage.xaml.cs` | +Notification handler |

---

## v5.3.1 — HDD-Path Dedup + Hilangkan Tombol 'Pilih' Redundan 🧹

Release date: 2026-07-16

Hotfix v5.3.0 — review paska-push nemu 2 isu, dua-duanya di-address di sini: refactor kecil di AutoOptimizeService untuk hapus duplikasi ~37 baris flag, plus bug UX minor di halaman Wallpaper Custom yang bisa bikin user ngira fitur rusak.

### 🧹 PART 1 — HDD-Path Deduplication (Refactor)

#### 🐛 Isu yang Ditemukan Saat Review

Pas review v5.3.0, ketauan: `ApplyHDDBalancedOptimizations()` itu **copy-paste** dari `ApplyAggressiveOptimizations()` — ~37 baris `App.FastFlags.SetValue(...)` di-duplikasi manual, cuma beda dikit di FPS cap & branch telemetry. Kalau besok ada yang update satu FastFlag (mis. ganti nilai `DFIntDebugFRMQualityLevelOverride`), harus inget update di 2 tempat — **kalau lupa satu**, dua preset jadi nggak sinkron diam-diam. Persis jenis dead-code-adjacent risk yang harusnya kita hindarin dari awal.

#### 🔧 Fix: HDD Method Jadi Delegasi Penuh

Bukan duplikasi lagi — `ApplyHDDBalancedOptimizations()` sekarang cuma manggil `ApplyAggressiveOptimizations(tier: SystemTier.LowEnd, hddIoTweaks: true, bypassLowEndGuard: true)` sebagai delegasi penuh:

```csharp
public static void ApplyHDDBalancedOptimizations()
{
    try
    {
        ApplyAggressiveOptimizations(tier: SystemTier.LowEnd,
                                     hddIoTweaks: true,
                                     bypassLowEndGuard: true);
    }
    catch (Exception ex)
    {
        App.Logger.WriteLine(LOG_IDENT, $"Error applying HDD balanced optimizations: {ex.Message}");
    }
}
```

Dua parameter opsional baru di `ApplyAggressiveOptimizations(...)`:

- **`hddIoTweaks = false`** (default) — branch baru `else if (hddIoTweaks)` di tier picker, setelah base flag diterapkan, set 3 HDD-specific flag:
  - `DFIntTextureCompositorActiveJobs=2` — komposisi atlas 2 thread (vs UltraLow=1, base LowEnd=tidak di-set)
  - `FIntRenderLocalLightUpdatesMax=4` — light update max tetap ringan (seperti UltraOrExtreme)
  - `FIntRenderLocalLightUpdatesMin=2` — light update min (seperti UltraOrExtreme)
- **`bypassLowEndGuard = false`** (default) — wraps 2 early-return guards (`OptimizeForLowEnd` check + `UserHasManualPreset()` check) jadi `if (!bypassLowEndGuard) { ... }`. HDD path real precondition-nya: HDD detected + tier LowEnd/MidRange, tapi **bukan** `OptimizeForLowEnd=true` — jadi butuh bypass biar jalan sebelum user toggle "Optimalkan untuk perangkat low-end" ON.

#### 📋 Verifikasi: 33 FastFlag Identik

Side-by-side comparison OLD vs NEW `ApplyHDDBalancedOptimizations()` memastikan output **100% identik** — semua 33 flag yang di-set di OLD method tetap di-set dengan **nilai yang sama persis**:

| # | Flag | OLD | NEW | Path di kode baru |
|---|------|-----|-----|-------------------|
| 1 | DFFlagTextureQualityOverrideEnabled | True | True | base |
| 2 | DFIntTextureQualityOverride | 0 | 0 | base |
| 3 | FIntTextureCompositorLowResFactor | 1 | 1 | base |
| 4 | DFIntDebugFRMQualityLevelOverride | 3 | 3 | base |
| 5 | FIntRomarkStartWithGraphicQualityLevel | 1 | 1 | base |
| 6 | FFlagDebugSSAOForce | False | False | base |
| 7 | FIntSSAOMipLevels | 0 | 0 | base |
| 8 | FIntRobloxGuiBlurIntensity | 0 | 0 | base |
| 9 | FIntRenderGrainScale | 0 | 0 | base |
| 10 | DFIntCSGLevelOfDetailSwitchingDistance | 250 | 250 | base |
| 11 | DFIntCSGLevelOfDetailSwitchingDistanceL12 | 250 | 250 | base |
| 12 | DFIntCSGLevelOfDetailSwitchingDistanceL23 | 250 | 250 | base (not extreme → 250) |
| 13 | DFIntCSGLevelOfDetailSwitchingDistanceL34 | 250 | 250 | base (not extreme → 250) |
| 14 | DFIntCSGLevelOfDetailSwitchingDistanceStatic | 0 | 0 | base |
| 15 | DFIntCSGv2LodsToGenerate | 0 | 0 | base |
| 16 | FIntTerrainArraySliceSize | 0 | 0 | base |
| 17 | FIntMaxBatchesPerFlush | 5000 | 5000 | base |
| 18 | DFIntMaxFrameBufferSize | 4 | 4 | base |
| 19 | FIntRuntimeMaxNumOfThreads | 4 | 4 | base |
| 20 | DFFlagEnableRequestAsyncCompression | True | True | base |
| 21 | **DFIntTextureCompositorActiveJobs** | **2** | **2** | **new `else if (hddIoTweaks)` branch** |
| 22 | DFIntTaskSchedulerTargetFps | 30 | 30 | base (LowEnd → "30") |
| 23 | DFIntMaxActiveAnimationTracks | 32 | 32 | base |
| 24 | FIntRenderLocalLightFadeInMs | 0 | 0 | base |
| 25 | **FIntRenderLocalLightUpdatesMax** | **4** | **4** | **new `else if (hddIoTweaks)` branch** |
| 26 | **FIntRenderLocalLightUpdatesMin** | **2** | **2** | **new `else if (hddIoTweaks)` branch** |
| 27 | FFlagDebugDisableTelemetryEphemeralCounter | True | True | base |
| 28 | FFlagDebugDisableTelemetryEphemeralStat | True | True | base |
| 29 | FFlagDebugDisableTelemetryEventIngest | True | True | base |
| 30 | FFlagDebugDisableTelemetryPoint | True | True | base |
| 31 | FFlagDebugDisableTelemetryV2Counter | True | True | base |
| 32 | FFlagDebugDisableTelemetryV2Event | True | True | base |
| 33 | FFlagDebugDisableTelemetryV2Stat | True | True | base |

Zero drift, zero flag yang hilang, zero behavioural change — lo bisa update satu FastFlag di `ApplyAggressiveOptimizations()` sekarang dengan yakin HDD path ikut konsisten otomatis. ✅

### 🎨 PART 2 — UX Fix: Tombol 'Pilih' Redundan di Custom Wallpaper

#### 🐛 Bug UX yang Bikin User Ngira Fitur Rusak

Di `ExperimentalPage.xaml` baris "Custom": ada **dua tombol** — "Pilih" 🆚 "Browse...". Tombol "Pilih" manggil `SelectBackground(BackgroundType.Custom)` langsung **tanpa buka file picker**.

**Skenario yang bikin user bingung:**

1. User klik "Pilih" **dulu** sebelum pernah klik "Browse..."
2. `CustomBackgroundPath` masih kosong
3. `AppBackgroundService.GetCustomBackground()` diam-diam fallback ke Default wallpaper 🫥
4. **Nggak ada snackbar, nggak ada error log** — user cuma lihat gambar Default
5. User ngira fitur Custom background **rusak** → complain di GitHub issue

#### 🔧 Fix Opsi A — Hapus Tombol 'Pilih'

Paling konsisten dengan pola command lain di ViewModel (semua command laen butuh file picker / toggle UI):

- ✅ **Hapus** tombol "Pilih" di baris Custom → cukup **satu tombol "Browse..."** saja
- ✅ **Hapus** `SelectWallpaperCustomCommand` ICommand property
- ✅ **Hapus** `OnSelectWallpaperCustom` method
- ✅ **Hapus** constructor init: `SelectWallpaperCustomCommand = new RelayCommand(OnSelectWallpaperCustom)`
- ✅ Tambah `RowDefinition` ke-5 di wallpaper Grid (Default/Cool/Quality/Extra/Custom)

Lo yang sebelumnya udah pernah set Custom background **gak kehilangan apa-apa** — `LoadSavedBackgroundAsync()` di `ExperimentalViewModel.OnNavigatedTo()` tetap load `CustomBackgroundPath` dari disk dan panggil `SelectBackground(Custom)` di belakang layar, jadi Custom wallpaper otomatis muncul lagi tiap buka halaman FastFlag New.

### Files Changed (3 files)

| File | Perubahan |
|------|-----------|
| `Bloxstrap/Integrations/AutoOptimizeService.cs` | +2 params (`hddIoTweaks`, `bypassLowEndGuard`), refactor HDD wrapper jadi delegasi penuh, −53 net lines |
| `Bloxstrap/UI/Elements/Settings/Pages/ExperimentalPage.xaml` | −1 tombol "Pilih" di baris Custom, +1 `RowDefinition` untuk row baru Custom |
| `Bloxstrap/UI/ViewModels/Settings/ExperimentalViewModel.cs` | −`SelectWallpaperCustomCommand`, −`OnSelectWallpaperCustom`, −constructor init |

---

## v5.3.0 — Optimasi Total: Rounding Fix, HDD Detection, Custom Wallpaper, Memory Leak Fix 🚀

Release date: 2026-07-12

### 🎯 PART 1 — Optimasi Performa Laptop Low-End

#### 🔧 Rounding Trap Fix — RAM 8GB Gak Kesiplah!

Dulu: `totalMemGB = bytes / (1024^3)` → integer division truncate ke bawah. Laptop 8GB yang lapor 7.6GB (karena reserved BIOS/iGPU) kebaca jadi **7GB** → salah klasifikasi ke LowEnd! 😤

**Sekarang:** Pake **MB comparison** dengan toleransi:
- 8GB class → `totalMemMB >= 7800` (toleransi ~200MB reserved)
- 16GB class → `totalMemMB >= 15600`
- 4GB class → `totalMemMB < 3800`

Laptop 8GB lo sekarang bener-bener kebaca sebagai **MidRange**! ✅

#### 💾 HDD Detection — Bukan Tebak-Tebakan Lagi!

Dulu: Tebak vendor name via WMI ("Samsung", "WDC", "Kingston" dll) — **sering salah**, apalagi kalo vendor bikin HDD + SSD.

**Sekarang:** `DeviceIoControl` + `IOCTL_STORAGE_QUERY_PROPERTY` + `StorageDeviceSeekPenaltyProperty`:
- Buka `\\.\C:` via `CreateFile` (volume handle, **gak perlu admin**)
- Tanya driver storage: "lo punya seek penalty?" → `IncursSeekPenalty = 0` = **SSD**, `1` = **HDD**
- **100% presisi**, zero dependencies (`System.Management` dihapus ✅)
- Cache di static field — query cuma sekali seumur proses

#### 🚀 HDD Balanced Preset — Otomatis Aktif!

Kalo storage lo **HDD** + tier **LowEnd/MidRange** + belom pilih preset manual → **HDD Balanced otomatis ON**!

**Basis visual:** SAMA dengan ExtremePerformance (FRM=3, texture rendah, SSAO off, LOD 250) — **sudah terbukti gak bikin game gelap**

**+ I/O khusus HDD:**
- Thread dibatasi (`FIntRuntimeMaxNumOfThreads=4`) — head HDD gak oleng
- Async compression (`DFFlagEnableRequestAsyncCompression=True`) — aset cepet di-download
- Texture compositor 2 thread (`DFIntTextureCompositorActiveJobs=2`) — komposisi lancar
- FPS cap 30 — kurangi render frame = kurangi I/O pipeline
- Telemetry mati total — kurangi write ke disk

#### 💾 Persistence Fix — Setting Lo Gak Ilang Lagi!

- **ForceExtremeMode** + **ExtremeModeFpsTarget**: Sekarang auto `Save()` tiap kali diubah 🛡️
- **MainWindow closing**: `App.Settings.Save()` dipanggil **sebelum** window beneran nutup — gak ada lagi setting ilang 🎯

### 🎨 PART 2 — Wallpaper System Overhaul

#### 🖼️ WallpaperService.cs — **DIHAPUS!**

Service yang ganti wallpaper **DESKTOP WINDOWS** lo (bukan background app) udah dihapus total. 0 references, 0 sisa.

#### 🎨 Custom Background — Pake Gambar Lo Sendiri!

Sekarang lo bisa pilih **gambar kustom** dari komputer lo sebagai wallpaper background BoneFish!

**Cara pake:**
1. Buka Settings → FastFlag New → Wallpaper Background
2. Klik **Browse...** → pilih file `.jpg` / `.jpeg` / `.png`
3. Langsung muncul! Background random mode otomatis mati

**Validasi otomatis:** Kalo file ilang / rusak → fallback ke Default + log error

#### 💾 Background Persistence — Gak Lupa Lagi!

- `SelectedBackgroundType` + `BackgroundRandomMode` = background lo keinget terus
- **Mode Random ON:** Ganti background tiap buka app
- **Mode Random OFF:** Background tetap sesuai pilihan terakhir (Default/Cool/Quality/Extra/Custom)
- **Startup auto-load:** Kalo random mode ON → random. Kalo ada saved type → load saved
- **Exit pre-select:** Pas app ditutup, kalo random mode ON → pilih random untuk sesi berikutnya

### 🧹 Memory Leak Fix — CrosshairService Lazy Start

- **Dulu:** Thread + dispatcher CrosshairService jalan TERUS meski EnableCrosshair = false (boros ~2-5 MB)
- **Sekarang:** `Instance` di-set di constructor, `Start()` cuma dipanggil kalo crosshair beneran ON
- Hotkey `Ctrl+Shift+C` tetep bisa lazy-start via `Toggle()` 🎯

### Files Changed (13 files)

| File | Perubahan |
|------|-----------|
| `AutoOptimizeService.cs` | Rounding trap fix, DeviceIoControl HDD detection, HDD Balanced preset, `UserHasManualPreset()` |
| `Bloxstrap.csproj` | Version 5.1.0 → 5.3.0, hapus `System.Management` NuGet |
| `Settings.cs` | +`SelectedBackgroundType`, +`BackgroundRandomMode` |
| `FastFlagsViewModel.cs` | +`try/catch Save()` di ForceExtremeMode + ExtremeModeFpsTarget |
| `MainWindow.xaml.cs` | +`App.Settings.Save()` di Closing |
| `AppBackgroundService.cs` | +Custom type, +GetCustomBackgroundAsync(), +`_customCache` |
| `ExperimentalViewModel.cs` | +Custom/Browse commands, +LoadSavedBackgroundAsync(), +BackgroundRandomMode |
| `ExperimentalPage.xaml` | +Custom row, +Browse button, +Mode Random toggle |
| `App.xaml.cs` | +Startup pre-select, +exit pre-select di SoftTerminate() |
| `WallpaperService.cs` | **DELETED** |

---

## v5.1.0 — Crosshair, Hotkey, Turbo Mode, Wallpaper Restore + Memory Fix 🧹

Release date: 2026-07-12

### 🎯 Crosshair Overlay — Bikin Sendiri Crosshair Lo!

Bosan sama crosshair game yang jelek? Sekarang lo bisa bikin **crosshair kustom** sendiri! Overlay transparan di tengah layar yang bisa lo:

- **Ganti style** — ada 4: Cross, Dot, Circle, sama CrossDot (campuran Cross + Dot)
- **Ganti warna** — tinggal pencet salah satu dari 8 warna preset, dari ijo lime sampe putih
- **Atur ukuran** — dari kecil (20px) sampe gede (200px)
- **Atur transparansi** — dari 10% sampe 100%
- **Drag & drop** — tinggal klik & geser ke posisi mana aja, posisinya bakal keinget terus
- **Toggle pake hotkey** — pencet `Ctrl+Shift+C` buat munculin/sembunyiin

**Dimana?** Settings → FastFlag New → Crosshair. Tinggal enable, langsung muncul!

### ⌨️ Global Hotkeys — Pake Keyboard Buat Semua!

Bosan harus alt-tab ke settings buat matiin/ngewaktifin fitur? Sekarang lo bisa pake **keyboard shortcut**! Works meski BoneFish di-minimize ke system tray:

| Tombol | Fungsi |
|--------|--------|
| `Ctrl+Shift+C` | Tampilin/Sembunyiin Crosshair |
| `Ctrl+Shift+F` | Tampilin/Sembunyiin FPS Monitor |
| `Ctrl+Shift+N` | Aktifin/Nonaktifin Night Vision |

Bisa lo matiin kalo ganggu di **Settings → FastFlag New → Enable Hotkeys**.

### 🚀 Turbo Mode — Satu Klik, PC Lo Ngebut!

**Fitur paling gacor buat laptop kentang!** Tinggal toggle ON, dan BoneFish langsung:

- **Paksa Extreme Performance mode** — sama kayak Potato Mode paling agresif
- **Terapin FastFlags agresif** — turunin render quality, tekstur low-res, shadow dikurangin
- **Auto-reset pas restart** — pas lo buka BoneFish lagi, Turbo Mode balik ke OFF otomatis. Gak bakal ngehek settingan permanen lo!

**Pas di-click OFF** — semua balik normal + FastFlags dihapus. Aman cuy!

### 🖼️ Wallpaper Background — Restored & Fixed!

Fitur wallpaper background di halaman FastFlag New balik lagi! Sekarang pake **async loading** jadi **gak bikin UI lemot**. Bisa pilih 4 wallpaper: Default, Cool, Quality, Extra.

**Yang di-fix:**
- ✅ Loading pake async + MemoryStream — gak blocking UI thread kayak dulu
- ✅ Auto-load pas buka halaman (sebelumnya cuma muncul kalo toggle manual)
- ✅ Cache 4 gambar — gak loading ulang tiap ganti halaman

### 🐛 Bug Fixes — Crosshair & Hotkeys Gak Jalan

**Masalah:** CrosshairService & HotkeyService di-init di DALEM blok `EnableActivityTracking`. Artinya:
- Kalo user matiin Activity Tracking → Crosshair & Hotkeys **GAK PERNAH JALAN**
- Kalo file log Roblox ilang (self-update) → Crosshair & Hotkeys **GAK PERNAH JALAN**

**Fix:** Pindahin inisialisasi ke LUAR blok ActivityTracking. Sekarang Crosshair & Hotkeys jalan **INDEPENDEN** — gak peduli tracking hidup/mati.

### 🏷️ Rename: "Experimental" → "FastFlag New"

Halaman Experimental sekarang bernama **FastFlag New** biar lebih jelas isinya.

### 🧠 Optimasi Memory — Lazy Start & Anti Leak

**1. CrosshairService — Lazy Start 🎯**
- Dulu: thread + dispatcher jalan TERUS meski crosshair mati (boros ~2-5 MB)
- Sekarang: thread cuma dibuat kalo EnableCrosshair ON. Kalo user pencet hotkey `Ctrl+Shift+C`, auto lazy-start.

**2. ActivityWatcher History — Auto-Prune 📜**
- History game yang pernah lo mainin sekarang auto di-prune ke max 50 entries. Gak bakal numpuk sampe ribuan.

**3. DNS Cache — Auto Cleanup 🌐**
- Cache DNS yang expired (>5 menit) otomatis dibersihin tiap kali lookup. Gak numpuk.

**4. HotkeyService — Constructor Pattern 🔧**
- Instance sekarang di-set di constructor (konsisten sama CrosshairService).

### 🔧 Fix Lainnya

- **HandleUpgrade Auto-Redirect** — pas lo jalanin BoneFish versi lama dari folder download, sekarang otomatis ngarahin ke yang udah ke-install. Gak bakal muncul dialog aneh-aneh lagi.
- **🐛 Wallpaper auto-load di startup** — sebelumnya wallpaper cuma muncul kalo user toggle ON/OFF manual. Sekarang otomatis loading pas buka halaman FastFlag New! (fix: pindahin loading dari MainWindow yang salah DataContext ke ExperimentalViewModel).

---

## v5.0.0 - Apply & Restart, Dark Game Fix, Flag Verification & Auto-Optimize Guard

Release date: 2026-07-05

Major release — menambahkan Apply & Restart Roblox, memperbaiki root cause game gelap, verifikasi flag otomatis, dan proteksi AutoOptimizeService terhadap preset user.

### New Features — 🚀 Apply & Restart Roblox

Tombol baru di halaman Fast Flags yang memungkinkan user menerapkan semua flag dan restart Roblox dalam satu klik.

**Cara kerja:**
1. User pilih preset / atur flag sesuai keinginan
2. Klik **🚀 Apply & Restart Roblox**
3. Konfirmasi dialog muncul
4. Otomatis: Save FastFlag + Settings → Kill Roblox + system tray → Launch BoneFish bootstrapper
5. Settings window tertutup, bootstrapper mengambil alih

**Yang dilakukan:**
- Save semua `App.FastFlags` dan `App.Settings` ke disk
- Kill proses `RobloxPlayerBeta` dan `RobloxCrashHandler` (async, UI tidak freeze)
- Signal `BoneFish-WatcherExitEvent` untuk clean up system tray
- Launch `BoneFish.exe -player` untuk restart dengan flag baru
- Tutup settings window agar tidak ada 2 window aktif

### New Features — ✅ FastFlag Verification (VerifyAndNotify)

Setiap kali user mengapply preset, sistem sekarang membaca kembali `ClientAppSettings.json` dari disk dan menghitung jumlah flag yang berhasil ditulis.

**Contoh notifikasi:**
- `✅ Ultra Low aktif — 32 FastFlag berhasil ditulis ke disk.`
- `✅ Balanced aktif — 28 FastFlag berhasil ditulis ke disk.`
- `✅ Potato Mode aktif — 41 FastFlag berhasil ditulis ke disk.`
- `✅ Stable aktif — 25 FastFlag berhasil ditulis ke disk.`

Tujuan: memastikan user yakin flag sudah benar-benar tersimpan sebelum restart.

### New Features — ⏳ Loading Indicator

ProgressRing + teks "Menerapkan preset..." muncul saat user mengklik preset, menggantikan tombol sementara. Snackbar timeout dinaikkan dari 3 detik ke 5 detik agar user sempat membaca verifikasi.

### Bug Fixes — 🌑 Game Gelap (Root Cause Found & Fixed)

**Penyebab sesungguhnya** ditemukan dengan membaca file `ClientAppSettings.json` yang BENAR-BENAR ada di disk (bukan hanya di kode):

| Flag di Disk | Nilai Sebelumnya | Dampak |
|---|---|---|
| `DFIntDebugFRMQualityLevelOverride` | **1** | FRM minimum → lighting pipeline rusak total |
| `FIntRenderShadowIntensity` | **0** | Semua bayangan dimatikan → area gelap |
| `DFFlagDebugPauseVoxelizer` | **True** | Voxelizer dimatikan → baked lighting mati |
| `FIntCSGVoxelizerFadeRadius` | **0** | Tidak ada transisi lighting |

**3 flag terakhir** (shadow=0 + voxelizer pause + fade=0) bersama FRM=1 **MEMATIKAN seluruh pipeline lighting Roblox** — bukan hanya mengurangi, tapi benar-benar mematikan. Ini menyebabkan game (terutama horror/RPG seperti Phasmophobia) menjadi hitam di area yang seharusnya terang.

**Fix yang diterapkan:**

1. **ExtremePerformance preset** (`FastFlagsViewModel.cs`):
   - FRM: `1` → `3` (masih sangat ringan, tapi preserve lighting pipeline)
   - **HAPUS** `FIntRenderShadowIntensity = 0` (shadow biarkan default Roblox)
   - **HAPUS** `DFFlagDebugPauseVoxelizer = True` (voxelizer biarkan jalan)
   - **HAPUS** `FIntCSGVoxelizerFadeRadius = 0` (fade biarkan default)

2. **AutoOptimizeService** (`AutoOptimizeService.cs`):
   - FRM: `1` → `3` (sama, untuk auto-detected low-end)
   - **HAPUS** 3 flag shadow/voxelizer yang sama

3. **UltraLow preset** (`FastFlagsViewModel.cs`):
   - FRM: `1` → `5` (sedikit lebih tinggi dari Extreme, preserve lighting)

### Bug Fixes — 💾 Save Order Bug (Preset Tidak Tersimpan ke Disk)

**Root cause:** Di `ApplyUltraLowSpecPreset()` dan `ApplyExtremePerformancePreset()`, `SelectedPreset` di-set **SETELAH** `App.Settings.Save()`. Jadi nama preset tidak pernah tertulis ke disk.

**Akibat:** Saat bootstrapper restart, `AutoOptimizeService.CheckAndApply()` baca `SelectedPerformancePreset` dari disk → preset tidak ditemukan → **OVERWRITE semua flag** user dengan FRM=1 + shadow=0 + voxelizer=True → game gelap.

**Fix:** Pindahkan `SelectedPreset = "..."` **SEBELUM** `App.Settings.Save()` di kedua method. Sekarang preset tertulis ke disk SEBELUM save, sehingga bootstrapper membaca nilai yang benar.

### Bug Fixes — 🛡️ AutoOptimizeService Overwrite Guard

**Root cause:** `ApplyAggressiveOptimizations()` di `AutoOptimizeService.cs` berjalan **setiap kali** BoneFish launch dan **OVERWRITE** preset pilihan user — memaksa FRM=1, shadow=0, voxelizer=True meskipun user sudah memilih preset lain.

**Fix:** Early-return check di `ApplyAggressiveOptimizations()`: jika user sudah memilih preset manual (UltraLow/Balanced/Stable/ExtremePerformance), **skip semua aggressive overrides** dan return. Preset user dihormati.

### Bug Fixes — 🔁 Auto-Upgrade Downgrade Loop pada HandleUpgrade()

**Gejala:** Dialog `InstallChecker_VersionLessThanInstalled` ("Versi BoneFish yang kamu jalankan adalah versi lama…") muncul **tiap kali launch** untuk user yang menjalankan BoneFish dari staging path auto-updater (`%localappdata%\Temp\BoneFish\Updates\BoneFish.exe`). Bukan cuma spam dialog — kalau user klik **Yes**, kode `HandleUpgrade()` di `Installer.cs` tetap lanjut ke `File.Copy(Paths.Process, Paths.Application, true)`, yaitu **staging binary di-copy ke lokasi install canonical**. Kalau staging binary lebih lama dari installed, **install canonical di-downgrade**. Launch berikutnya: dialog fire lagi → user klik Yes → downgrade lagi. Loop tak berhingga sampai user secara manual hapus staging binary atau reinstall penuh.

**Akar masalah:** Di `HandleUpgrade()`, logika lama memblok `File.Copy` hanya kalau MD5 paths cocok atau kalau user klik No pada dialog. Untuk auto-upgrade session, dialog tetap ditampilkan (tidak di-skip meskipun `isAutoUpgrade=true`), dan setelah Yes, tidak ada cek apakah `currentVer < existingVer` (running < installed) — yang justru adalah kondisi downgrade yang harus di-block.

**Fix yang diterapkan di `Bloxstrap/Installer.cs` `HandleUpgrade()`:**

1. **Pre-dialog early-return khusus auto-upgrade.** Jika `isAutoUpgrade=true` AND `currentVer < existingVer`, langsung `return` sebelum dialog pertama sempat fire. Tidak ada `File.Copy`, tidak ada downgrade, tidak ada yes/no dialog.
2. **Log informatif 4 baris** menggantikan dialog — user bisa langsung baca dari `BoneFish_*.log` apa yang terjadi dan bagaimana cara recover:
   - Versi running vs installed
   - Path staging vs path install
   - Penyebab umum (stale staging binary dari versi sebelumnya)
   - Solusi: hapus staging binary atau reinstall untuk membuat install canonical jadi build aktif
3. **Manual downgrade flow tetap utuh.** Untuk run non-auto-upgrade (user klik kanan exe dari folder atau dari shortcut ke lokasi lama), dialog "older version" tetap ditampilkan dengan perilaku existing — supaya user tetap aware kalau mereka sengaja menjalankan binary lama.

**Skenario yang sekarang ter-handle benar:**

| Skenario | Sebelum fix | Sesudah fix |
|---|---|---|
| Auto-upgrade dengan staging lebih lama (running < installed) | Dialog Yes/No → kalau Yes, install canonical DOWNGRADE → loop | Silent abort + log informatif + no copy |
| Auto-upgrade dengan staging lebih baru (running > installed) | Quiet upgrade — sudah benar sebelumnya | Quiet upgrade — tetap |
| Auto-upgrade dengan staging = installed (MD5 match) | Early-return no-op — sudah benar sebelumnya | Early-return no-op — tetap |
| Manual run dengan binary lebih lama dari install | Dialog Yes/No — sudah benar sebelumnya | Dialog Yes/No — tetap (perilaku existing dipertahankan) |

**Cara recover user yang sudah terkena downgrade loop:**
1. Delete `%localappdata%\Temp\BoneFish\Updates\BoneFish.exe`
2. Run installer BoneFish dari canonical install location (`%localappdata%\Programs\BoneFish\BoneFish.exe`) — sekarang akan rebuild staging fresh dengan versi sama dengan install
3. Atau reinstall penuh via `unins000.exe` + install dari distribusi terbaru

### Performance Presets — Deskripsi Lengkap

BoneFish menyediakan 5 preset performa untuk menyesuaikan Roblox dengan spesifikasi perangkat:

#### 🤖 Auto-Optimize (Default)
- **Target:** Semua spesifikasi
- **Filosofi:** Deteksi hardware otomatis, apply optimasi yang sesuai
- **Yang diatur:** FRM=21, Texture=Level0, MSAA=x1, Mesh=LOD0, D3D11, Disable Animations, Low Memory Mode
- **Cocok untuk:** User yang tidak ingin repot atur manual

#### 🛡️ Stable
- **Target:** Semua spesifikasi, prioritaskan stabilitas
- **Filosofi:** Auto-Optimize base + Network optimization + Nonaktifkan background updates
- **Yang diatur:** Sama seperti Auto-Optimize + Network flags (No Delay) + BackgroundUpdates=False + FakeBorderlessFullscreen=False
- **Cocok untuk:** User yang sering crash/freeze, ingin pengalaman paling stabil

#### 🐌 Ultra Low-Spec
- **Target:** PC kentang (2 core, 2-4GB RAM, Intel HD)
- **Filosofi:** Aggressif tapi TIDAK mematikan lighting
- **Yang diatur:** FRM=5, Texture=Level0, MSAA=x1, LOD=250, FPS=30, LightUpdates=4, Compositor=1, Animations Off, Low Memory Mode
- **Cocok untuk:** Laptop lawas, PC integrated graphics, ingin main Roblox di spek minimum

#### 🥔 Extreme Performance (Potato Mode)
- **Target:** PC paling lemah (2 core, <4GB RAM, no GPU dedicated)
- **Filosofi:** Maksimal performa, pertahankan VISIBILITAS game
- **Yang diatur:** FRM=3, Texture=Level0, MSAA=x1, LOD=250/500/750, FPS=24-60 (configurable), LightUpdates=4, Compositor=1, Telemetry Off, Animations Off, Low Memory Mode
- **Yang TIDAK dilakukan (perubahan dari v4.4.0):**
  - ❌ TIDAK mematikan shadow (sebelumnya `FIntRenderShadowIntensity=0` → game gelap)
  - ❌ TIDAK pause voxelizer (sebelumnya `DFFlagDebugPauseVoxelizer=True` → lighting baked mati)
  - ❌ TIDAK paksa Voxel lighting (merusak game ShadowMap/Future)
  - ❌ TIDAK disable PostFx (visual game rusak)
  - ❌ TIDAK ubah SkyGray (atmosphere berubah)
  - ❌ TIDAK ubah LightAttenuation (model lighting berubah)
- **Cocok untuk:** Device paling lemah yang ingin tetap bisa MELIHAT game dengan jelas

#### ⚖️ Balanced
- **Target:** Mid-range PC (4 core, 8GB RAM)
- **Filosofi:** Keseimbangan antara performa dan visual
- **Yang diatur:** FRM=15, Texture=Level1, MSAA=x2, Mesh=LOD1, Lighting=Default, Network flags
- **Cocok untuk:** PC menengah yang ingin performa lebih tanpa mengorbankan visual terlalu banyak

### Night Vision Mode 🌙

Mode opsional yang menerangkan area gelap di game secara client-side.

**Cara kerja:**
- `FFlagFastGPULightCulling3=True` — GPU light culling efisien, area gelap jadi lebih terang
- `FFlagNewLightAttenuation=True` — model attenuation "lembut", cahaya menyebar lebih jauh
- `FIntRenderLocalLightUpdatesMax=8` — senter/torch update lebih sering

**Efek:** Area gelap / senter game terasa lebih terang. Pemain lain dan server TIDAK melihat perubahan apapun.

**Toggle:** Settings → Fast Flags → Night Vision card → klik untuk aktif/nonaktifkan

### FPS Monitor Overlay 📊

Overlay transparan yang menampilkan FPS real-time, frame time, dan status game.

**Fitur:**
- FPS counter berbasis ETW (Event Tracing for Windows) — akurat, bukan WPF render frames
- Color coding: Hijau (≥60 FPS), Kuning (30-59 FPS), Merah (<30 FPS)
- Draggable — geser ke posisi manapun, posisi tersimpan
- Persistent mode — tetap aktif meski keluar game
- Vulkan detection — informasi jika game pakai Vulkan (ETW tidak bisa baca)
- Low-end throttle — update interval naik ke 2 detik untuk hemat CPU

**Toggle:** Settings → Experimental → Enable FPS Monitor

### Anti Not-Responding System

Sistem 3-layer proteksi terhadap freeze/crash di perangkat low-end:

**Layer 1 — FastFlags (sebelum Roblox launch):**
- `DFIntMaxActiveAnimationTracks=32` — potong Lua GC pressure (default 200+)
- `FIntRenderLocalLightFadeInMs=0` — hapus light fade work di main thread
- 7× `FFlagDebugDisableTelemetry*` — eliminasi background telemetry wakeups

**Layer 2 — Process Priority (800ms setelah Roblox start):**
- Roblox process → `AboveNormal` priority
- Windows scheduler beri lebih banyak CPU time

**Layer 3 — RAM Trim (device <5GB RAM):**
- `EmptyWorkingSet()` pada semua non-critical background processes
- Bebaskan RAM fisik untuk Roblox
- System-critical processes (svchost, dwm, explorer) di-skip
- Bonus: BoneFish pin ke core 0 di dual-core untuk kurangi L2 cache contention

### Changelog (Technical)

**Files changed:**
- `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs`:
  - Added `ApplyAndRestartRobloxCommand` + `ApplyAndRestartRoblox()` (async void, Task.Run for process kill)
  - Added `RequestCloseWindowEvent` for settings window close
  - Added `IsApplying`/`IsNotApplying` loading state
  - Added `VerifyAndNotify()` method — reads back JSON, shows flag count
  - Fixed save order: `SelectedPreset` before `App.Settings.Save()` in UltraLow + ExtremePerformance
  - ExtremePerformance: FRM 1→3, removed shadow/voxelizer flags
  - UltraLow: FRM 1→5
  - All preset methods: `Notify()` → `VerifyAndNotify()` for verification

- `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml`:
  - Added 🚀 Apply & Restart Roblox CardAction button with "SAVE + RESTART" badge
  - Added loading indicator (ProgressRing + text) bound to `IsApplying`
  - Increased snackbar timeout from 3000 to 5000ms

- `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml.cs`:
  - Added `RequestCloseWindowEvent` handler → `Window.Close()`

- `Bloxstrap/Integrations/AutoOptimizeService.cs`:
  - Added early-return in `ApplyAggressiveOptimizations()`: skip if user has manual preset
  - FRM: 1→3 for aggressive optimizations
  - Removed: `FIntRenderShadowIntensity=0`, `DFFlagDebugPauseVoxelizer=True`, `FIntCSGVoxelizerFadeRadius=0`

- `Bloxstrap/Bloxstrap.csproj`:
  - Version bump: 4.4.0 → 5.0.0

---

## v4.5.0 - Fix "Old Version" Warning, Gabung Potato+UltraLow jadi Anti Not-Responding

> **ℹ️ SUPERSEDED by v5.0.0:** Preset UI consolidation yang digambarkan di section ini ("Anti Not-Responding (Long Session)") sudah di-reverse oleh v5.0.0. v5.0.0 merestrukturisasi UI kembali ke 2 preset terpisah (UltraLow + ExtremePerformance) dengan pendekatan berbeda — lihat section v5.0.0 di bawah untuk arsitektur saat ini. Section ini disimpan sebagai historical record dari pendekatan eksperimen yang ternyata perlu di-revert setelah sync dengan conflict game-dark-fix dari remote.
>
> Fitur v4.5.0 yang di-preserve di v5.0.0:
> - `Bootstrapper. forceSyncUpgrade` — bypass BackgroundUpdater untuk Player launch saat VersionGuid mismatch (mencegah warning "you're using an old version of Roblox")
> - Migrasi preset `UltraLow → ExtremePerformance` di `App.OnStartup` — **dihapus di f423397** karena bentrok dengan ApplyUltraLowSpecPreset yang di-restore v5.0.0

Release date: 2026-07-10

Minor release — memperbaiki notifikasi "You're using an old version of Roblox" yang muncul saat launch, dan menggabungkan preset **Ultra Low-Spec** dan **Extreme Performance (Potato Mode)** menjadi satu mode tunggal bernama 🥔 **Anti Not-Responding (Long Session)** yang fokus mencegah freeze/exit saat main lama di device low-end.

### Bug Fixes — Notifikasi "Versi Lama" Roblox Saat Launch

Akar masalah: `Bootstrapper.cs` punya jalur `Background Updater` yang bisa launch Roblox dengan versi lama sambil download versi baru di background. Pada Player launch (Roblox strict server-side handshake), Roblox server akan **tolak** client lama dengan banner **"You're using an old version of Roblox"** karena binary di disk != binary yang server expects.

- **Fiks**: Untuk **Player launch** ketika `VersionGuid` lokal tidak match dengan server latest, **always force synchronous `UpgradeRoblox()`**. Background updater (kalau jalan) di-kill dulu; jalur async di-bypass. Studio launch tetap boleh pakai background updater (server handshake lebih permissive).
- **Implementasi**: Di `Bootstrapper.cs`, tambah variabel `forceSyncUpgrade = !IsStudioLaunch && AppData.State.VersionGuid != _latestVersionGuid`. Logika: kill background updater kalau `forceSyncUpgrade || _mustUpgrade`, lalu jatuh ke `await UpgradeRoblox()`.
- **Edge cases ter-handle**: Studio launch tidak terpengaruh, path Studio tetap viable untuk background updates; cancel flow tetap fungsional via existing mutex; first-time install path `UpgradeRoblox()` selalu dipanggil karena `_mustUpgrade = true` saat `String.IsNullOrEmpty(VersionGuid)` atau `!File.Exists(ExecutablePath)`.

### Feature — 🥔 Anti Not-Responding (Long Session) Konsolidasi

Preset UI **"Ultra low-spec + Anti Crash" (UltraLow)** dan **"Extreme Performance (Potato Mode) (ExtremePerformance)"** digabung menjadi satu button: **🥔 Anti Not-Responding (Long Session)**. Tujuan utama mode baru: mencegah Roblox freeze/not-responding setelah main 30+ menit di device dual-core / RAM <4GB.

- **Apa yang ditambahkan ke preset Extreme**:
  - `DFIntTextureCompositorActiveJobs=1` — batasi atlas composite ke 1 worker (sebelumnya khusus UltraLow, sekarang ikut Extreme). Race condition pada atlas composite di main thread setelah beberapa puluh menit main.
  - `DFIntDebugFRMQualityLevelOverride=1` — kunci FRM quality di minimum agar engine auto-promote tidak trigger re-shader compile saat session panjang.
- **Apa yang sudah ada di Extreme (dari versi sebelumnya)**: 7 telemetry flags, `DFIntMaxActiveAnimationTracks=32`, shadow off, SSAO off, LOD 250/500/750, FPS cap mengikuti slider (default 30, range 24-60).
- **UX**: Tombol Potato Mode tetap ada di UI dengan badge **"PALING AGRESIF"**:

  > 🥔 Anti Not-Responding (Long Session)
  > Untuk laptop kentang: dual-core, RAM <4GB, tanpa GPU dedicated. Tujuan utama: mencegah Roblox freeze/not-responding setelah main lama. Matikan shadow, post-FX, SSAO, telemetry, anim track, dynamic faces. Gabungan flag Potato + Ultra Low.

- **Toggle & slider yang tetap**: Force Extreme Mode override, Target FPS slider (24–60), Night Vision toggle. Semua referensi ke preset lama di-update.

### Migration Otomatis — "UltraLow" → "ExtremePerformance"

User yang sebelumnya memilih `SelectedPerformancePreset = "UltraLow"` di-launch berikutnya otomatis di-migrate ke `"ExtremePerformance"` (mode gabungan baru). Migrasi idempotent — kalau preset sudah `"ExtremePerformance"` atau preset lain, tidak diubah.

- **Implementasi**: Di `App.OnStartup()`, setelah `Settings.Load()`:
  ```csharp
  if (Settings.Prop.SelectedPerformancePreset == "UltraLow") {
      Settings.Prop.SelectedPerformancePreset = "ExtremePerformance";
      Settings.Save();
  }
  ```
- **Logging**: Migrasi log ke `BoneFish_*.log` dengan identifier `App::OnStartup` agar debugging mudah.

### Changelog (Technical)

- `Bloxstrap/Bloxstrap.csproj`: `Version` & `FileVersion` 4.4.0 → **4.5.0**
- `Bloxstrap/Bootstrapper.cs`: tambah logic `forceSyncUpgrade` di sekitar blok upgrade detection; kill background updater kalau flag aktif; force `await UpgradeRoblox()` di Player launch saat `VersionGuid` mismatch
- `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs`:
  - Hapus `ApplyUltraLowSpecPresetCommand` dan method `ApplyUltraLowSpecPreset()`
  - Hapus property `IsUltraLowActive` (update `SelectedPreset` setter untuk tidak reference lagi)
  - Tambah 2 flag anti-not-responding di `ApplyExtremePerformancePreset()`: `DFIntTextureCompositorActiveJobs=1` dan `DFIntDebugFRMQualityLevelOverride=1`
  - Update Notify message: dari "Potato Mode" jadi "Anti Not-Responding (Long Session)"
- `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml`:
  - Hapus CardAction untuk "Ultra low-spec" button
  - Rename Potato button: judul jadi "🥔 Anti Not-Responding (Long Session)", deskripsi update
- `Bloxstrap/App.xaml.cs`: tambah migrasi preset `UltraLow → ExtremePerformance` setelah `Settings.Load()` di `OnStartup()`

### Edge Cases Handled

- **Studio launch** — tidak terpengaruh forceSyncUpgrade, tetap pakai background updater.
- **Multiple preset application (race)** — urutan di tiap preset tetap: Cleanup → Purge → set flags → Save → Reload.
- **User dengan preset lama `"UltraLow"`** — auto-migrate di launch berikutnya tanpa intervensi user.
- **Auto-detect AutoOptimizeService** — `SystemTier.UltraLow` (enum) tetap dipakai untuk auto-detect hardware; jalur `isUltraOrExtreme` di `ApplyAggressiveOptimizations()` masih aktif jadi device UltraLow otomatis mendapat flag yang sama.
- **First-time install** — `UpgradeRoblox()` selalu dipanggil karena `_mustUpgrade = !File.Exists(ExecutablePath)`.

### Audit "Black Flag" — Tidak Ada Flag Game-Breaking di v4.5.0

Cross-check 7 flag yang pernah menyebabkan masalah di v3.9.0 (layar hitam, atmosphere rusak, post-FX game mati, not-responding additional, mic rusak). Semuanya **diverifikasi tidak ada di consolidated preset**:

| Flag Berbahaya | Efek | Status v4.5.0 |
|---|---|---|
| `DFFlagDebugRenderForceTechnologyVoxel` | Layar hitam total (ShadowMap/Future) | ❌ TIDAK di-set (ada di AllKnownManagedFlags untuk purge saja) |
| `FFlagDebugSkyGray` | Sky abu-abu datar | ❌ TIDAK di-set |
| `FFlagDisablePostFx` | Post-FX game (bloom milik game) mati | ❌ TIDAK di-set |
| `FFlagNewLightAttenuation=False` | Harsh black lighting model | ❌ TIDAK di-set ke False. (=True HANYA di Night Vision toggle dengan dialog konfirmasi) |
| `DFIntDebugRestrictGCDistance=1` | GC aggressive → not-responding tambahan | ❌ TIDAK di-set |
| `DFIntAnimationLodFacsDistanceMin/Max/VisibilityDenominator=0` | Pipeline FACS mati → mic rusak | ❌ TIDAK di-set |
| Bayangan/shadow off tanpa lighting model rusak | Aman jika **tanpa** Voxel | ✅ Konsisten — pakai `FIntRenderShadowIntensity=0` PAIRED dengan `DFFlagDebugPauseVoxelizer=True`, tidak paksa Voxel |

**Verdict**: Konsolidasi preset v4.5.0 AMAN. Semua flag yang aktif di `ApplyExtremePerformancePreset()` dan `AutoOptimizeService.ApplyAggressiveOptimizations()` adalah flag **proven-aman** yang hanya menurunkan beban render/CPU/RAM tanpa mengubah model lighting atau visual game.

---

## v4.4.1 - Fix Custom Loading Screen: Registrasi Pertama, Persistensi & Preview

Release date: 2026-07-05

Hotfix — memperbaiki bug kritis pada fitur Custom Loading Screen (Mods → Kustomisasi Gambar): file tidak pernah di-copy saat pertama kali dipilih, state tidak persist setelah restart, dan preview tidak muncul.

### Bug Fixes — Custom Loading Screen Tidak Berfungsi

- **`Changed` override broken** — `LoadingScreenModPresetTask.Changed` membandingkan `MD5Hash.FromStream()` dengan `OriginalState` (sebuah PATH string, bukan hash). Akibatnya: **tidak pernah match** → `Changed` selalu `false` saat `OriginalState` terisi. Tapi masalah lebih parah: `Changed` override cek `!File.Exists(Paths.CustomLoadingScreen)` dulu — file tujuan **belum ada** saat user pertama kali pilih gambar → return `false` → task **tidak pernah masuk `PendingSettingTasks`** → `Execute()` tidak pernah jalan → file tidak pernah di-copy. Loop: file tidak ada karena tidak di-copy, tidak di-copy karena Changed return false.
- **Fix:** Hapus `Changed` override sepenuhnya. Sekarang pakai base `StringBaseTask.Changed` (string comparison `_newState != OriginalState`). First-time selection: `NewState="C:/img.png"` vs `OriginalState=""` → `true` ✅

### Bug Fixes — State Hilang Setelah Restart

- `ModsViewModel` constructor hanya membuat instance task — `NewState` dan `OriginalState` selalu `""`. UI selalu menampilkan tombol "Choose" meski custom loading screen aktif.
- **Fix:** Constructor sekarang cek `File.Exists(Paths.CustomLoadingScreen)` dan set `OriginalState = Paths.CustomLoadingScreen` (setter otomatis sync ke `NewState`). UI menampilkan tombol "Remove" + preview setelah restart.

### Bug Fixes — Preview Tidak Muncul di Startup

- `LoadingScreenPreview` selalu `null` saat ViewModel dibuat. Tidak ada logic untuk load gambar dari disk.
- **Fix:** Setelah restore `OriginalState`, constructor juga load `BitmapImage` dari `Paths.CustomLoadingScreen` ke `LoadingScreenPreview`. Jika file corrupted, silent fail — user lihat tombol Remove tanpa preview, tinggal re-select.

### Additional Fix — BackgroundModPresetTask (Bug Sama)

- `BackgroundModPresetTask` punya `Changed` override identik dengan bug yang sama. DIhapus, pakai base class sekarang.

### Changelog (Technical)

- `Bloxstrap/Models/SettingTasks/LoadingScreenModPresetTask.cs`: hapus `Changed` override — base `StringBaseTask.Changed` handles all cases
- `Bloxstrap/Models/SettingTasks/BackgroundModPresetTask.cs`: hapus `Changed` override — fix identik
- `Bloxstrap/UI/ViewModels/Settings/ModsViewModel.cs`: constructor restore `OriginalState` + preview dari `Paths.CustomLoadingScreen`

---

## v4.4.0 - Fix Gelap, Flag Cleanup, Notifikasi & Triple-Reload Stable Preset

Release date: 2026-07-05

Hotfix — memperbaiki bug kritis yang dilaporkan user: game tetap gelap walau sudah ganti preset, fast flag tidak terhapus saat pindah mode, notifikasi preset tidak muncul, dan Stable preset melakukan 3x reload halaman.

### Bug Fixes — Game Gelap (Darkness)

- **10 flag UI hilang dari daftar purge** — `FFlagRenderUIAnimations`, `FFlagRenderMenuTransitions`, `FFlagRenderInventoryEffects`, `FFlagLuaAppEnableLowMemoryMode`, dan 5 network flags (`FIntRakNetPacketRateLimit`, `DFIntMaxReceivePPS`, `DFIntMaxSendPPS`, `DFIntConnectionMTUSize`, `DFIntOptimizeSendQueue`) serta `FFlagDebugDisplayFPS` **tidak ada di `AllKnownManagedFlags`**. Akibatnya, saat user switch preset, flag-flag ini tidak terhapus dan bertahan dari preset sebelumnya — termasuk flag yang bikin lighting gelap. Semua sudah ditambahkan ke master purge list.
- **`ManagedFlags[]` disinkronkan** — `RemoveOptimizations()` sekarang juga membersihkan flag UI saat user mematikan mode low-end, tidak hanya flag dari AutoOptimizeService.

### Bug Fixes — Flag Tidak Terhapus Saat Pindah Preset

- **`CleanupLegacyRobloxFlags()` dipanggil di SETIAP preset method** — sebelumnya hanya dipanggil sekali di `Bootstrapper.Run()`. Sekarang setiap kali user klik preset (AutoOptimize, UltraLow, Balanced, ExtremePerformance), file `ClientAppSettings.json` di ketiga path dibersihkan:
  - `%localappdata%\Roblox\Versions\xxx\ClientSettings` (path Roblox default — di sinilah flag legacy paling bahaya tertinggal)
  - `%localappdata%\BoneFish\Modifications\ClientSettings`
  - `%localappdata%\BoneFish\Versions\WindowsPlayer\ClientSettings`
- **Order cleanup yang benar** — CleanupLegacyRobloxFlags (DISK) → PurgeAllKnownFlags (MEMORY) → set flag baru → Save. Urutan ini memastikan flag baru tidak ditimpa oleh stale disk write.

### Bug Fixes — Notifikasi Tidak Muncul

- **`Notify()` dipindah SEBELUM `RequestPageReloadEvent`** di 7 method: `ApplyRecommendedFastFlags`, `ApplyRecommendedNetworkSettings`, `ApplyRecommendedStabilityPreset`, `ApplyUltraLowSpecPreset`, `ApplyBalancedPreset`, `ApplyExtremePerformancePreset`, `ClearClientAppSettings`.
- Root cause: `RequestPageReloadEvent` memicu `SetupViewModel()` yang membuat ViewModel baru. Notify pada ViewModel lama event-nya sudah tidak punya subscriber yang terhubung ke UI snackbar. Dengan Notify sebelum reload, snackbar muncul di halaman yang masih aktif.

### Bug Fixes — Night Vision State Inconsistency

- **`NightVisionEnabled = false`** ditambahkan setelah `PurgeAllKnownFlags()` di setiap preset method. Sebelumnya, flag Night Vision (`FFlagFastGPULightCulling3`, `FFlagNewLightAttenuation`) terhapus dari `Prop` tapi setting `EnableNightVision` tetap `true` — UI menunjukkan Night Vision aktif padahal flag sudah hilang. Sekarang state sinkron.

### Refactor — Stable Preset Triple-Reload Dihilangkan

- `ApplyRecommendedStabilityPreset` sebelumnya memanggil `ApplyRecommendedFastFlags()` (1 reload) → `ApplyRecommendedNetworkSettings()` (1 reload) → setup sendiri (1 reload) = **3x reload halaman & 3 snackbar**.
- Sekarang semua logic di-inline ke 1 method: cleanup sekali, set semua flag (FastFlags + Network + Stability), save sekali, notify sekali, reload sekali.

### Light Rendering Tuning

- `FIntRenderLocalLightUpdatesMax`: 6 → **4** — nilai 6 terlalu konservatif, 4 cukup untuk senter/torch bergerak smooth tanpa mengorbankan performance.
- `FIntRenderLocalLightUpdatesMin`: 3 → **2** — minimum yang lebih aman, tetap menjaga lighting responsif di game horror/RPG.

### Changelog (Technical)

- `Bloxstrap/Integrations/AutoOptimizeService.cs`:
  - `AllKnownManagedFlags[]`: +10 flag (FFlagRenderUIAnimations, FFlagRenderMenuTransitions, FFlagRenderInventoryEffects, FFlagLuaAppEnableLowMemoryMode, FIntRakNetPacketRateLimit, DFIntMaxReceivePPS, DFIntMaxSendPPS, DFIntConnectionMTUSize, DFIntOptimizeSendQueue, FFlagDebugDisplayFPS)
  - `ManagedFlags[]`: disinkronkan dengan AllKnownManagedFlags, +12 flag (termasuk FFlagFastGPULightCulling3, FFlagNewLightAttenuation, dan semua UI flags)
  - `CleanupLegacyRobloxFlags()`: diperluas scan ke 3 path (sebelumnya hanya Roblox default, sekarang +2 path BoneFish)
  - `FIntRenderLocalLightUpdatesMax` 6→4, `FIntRenderLocalLightUpdatesMin` 3→2
- `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs`:
  - Semua 7 preset method: `Notify()` sebelum `RequestPageReloadEvent`
  - 4 preset method (`ApplyRecommendedFastFlags`, `ApplyUltraLowSpecPreset`, `ApplyBalancedPreset`, `ApplyExtremePerformancePreset`): + `CleanupLegacyRobloxFlags()`, + `NightVisionEnabled = false`
  - `ApplyRecommendedStabilityPreset`: di-refactor — inline semua logic, 1 save/notify/reload
- `Bloxstrap/Integrations/AutoOptimizeService.cs`: nilai light updates disesuaikan (Max 6→4, Min 3→2) di `ApplyAggressiveOptimizations`

---

## v3.9.0 - Extreme Performance (Potato Mode), Night Vision & Anti Not-Responding

Release date: 2026-07-03

Highlights
- Added **Extreme Performance preset** ("🥔 Potato Mode") — the most aggressive optimization tier, targeting dual-core laptops with <4GB RAM and no dedicated GPU
- Added **Night Vision** toggle (client-side only) — brightens dark areas in-game without affecting other players or the server
- Added **Anti Not-Responding** system — 3-layer protection against freeze/crash on low-end hardware using FastFlags + process-level optimizations
- Added **ForceExtremeMode** override — force Potato Mode regardless of auto-detected hardware tier
- Added **configurable FPS cap** for Extreme/UltraLow mode (24–60 fps slider, default 30)

Quick links
- Potato Mode & Night Vision: Settings → Fast Flags
- Force Extreme Mode toggle: Settings → Fast Flags → below Potato Mode card
- FPS cap slider: Settings → Fast Flags → "Target FPS — Extreme / Potato Mode"

Extreme Performance (Potato Mode) — what's disabled
- All shadow rendering (`FIntRenderShadowIntensity=0`, `DFFlagDebugPauseVoxelizer`, voxelizer fade radius)
- All post-processing (`FFlagDisablePostFx` — bloom, color correction, sun rays, depth of field)
- SSAO / ambient occlusion (`FFlagDebugSSAOForce=False`, `FIntSSAOMipLevels=0`)
- GUI blur on ESC menu (`FIntRobloxGuiBlurIntensity=0`)
- Grass, foliage & global wind simulation
- Terrain texture detail (`FIntTerrainArraySliceSize=0`)
- Water reflections & PBR (forced Voxel lighting via `DFFlagDebugRenderForceTechnologyVoxel`)
- CSGv2 LOD generation on load (`DFIntCSGv2LodsToGenerate=0`)
- Texture compositor low-res factor + reduced compositor jobs
- Light attenuation fallback to legacy model
- Texture quality forced to level 0, FRM quality level 1, MSAA x1, mesh LOD minimum

Anti Not-Responding — 3-layer system
1. **FastFlags** (applied before Roblox launches):
   - `DFIntMaxActiveAnimationTracks=32` — cuts Lua GC pressure (default is 200+)
   - `FIntRenderLocalLightFadeInMs=0` — removes per-frame light fade work on main thread
   - 7× `FFlagDebugDisableTelemetry*` — eliminates background telemetry thread wakeups
2. **Process priority** (800ms after Roblox starts): Roblox process set to `AboveNormal` — Windows scheduler gives it more CPU time, reduces preemption from background apps
3. **RAM trim** (only on devices <5GB RAM): `EmptyWorkingSet()` called on all non-critical background processes before Roblox finishes loading — frees physical RAM pages back to the OS. System-critical processes (svchost, dwm, explorer, lsass, etc.) are skipped. Amount freed is logged.
   - Bonus: on dual-core devices, BoneFish pins itself to core 0 to reduce L2 cache contention with Roblox

Night Vision — how it works (client-side only)
- `FFlagFastGPULightCulling3=True` — more ambient light sources processed per frame, dark areas appear brighter as a side effect
- `FFlagNewLightAttenuation=True` — softer light falloff, gradual dark-to-bright transition instead of hard black walls
- `FIntRenderLocalLightUpdatesMax=8` — torches/flashlights update more often, felt radius is wider
- Confirmation dialog shown on first activation: "Are you sure — aktifkan Night Vision?"
- State is persisted across sessions via `Settings.EnableNightVision`
- Toggling off restores Potato Mode values (attenuation False, Max=4)

Known issues fixed from v3.3.x
- **Mic not showing in Roblox voice chat**: FACS pipeline flags (`DFIntAnimationLodFacsDistanceMin/Max/Denominator`) intentionally NOT used — setting these to 0 also kills the voice activity indicator since Roblox uses the same pipeline for facial capture and voice input
- **Flashlight/torch bug (permanent black area)**: `FIntRenderLocalLightUpdatesMax` raised from 1 → 4 (Potato Mode) / 8 (Night Vision) — value of 1 caused light positions to not update fast enough when moving
- **Not responding more frequent**: `DFIntDebugRestrictGCDistance=1` removed — too aggressive, caused RAM spike when moving between areas as engine had to reload all discarded assets simultaneously

Developer notes
- New settings: `ForceExtremeMode` (bool), `ExtremeModeFpsTarget` (int, default 30), `EnableNightVision` (bool)
- New public method: `AutoOptimizeService.OptimizeRobloxProcess(int pid)` — called from `Bootstrapper.StartRoblox()` 800ms post-launch when `OptimizeForLowEnd=true` and `LaunchMode=Player`
- New private method: `AutoOptimizeService.TrimBackgroundProcesses(int robloxPid)` — P/Invoke `EmptyWorkingSet` via `psapi.dll`, only runs on systems with <5GB RAM
- P/Invoke additions in `AutoOptimizeService.cs`: `OpenProcess`, `EmptyWorkingSet`, `CloseHandle` (kernel32 + psapi)
- `ManagedFlags[]` updated to include all new flags — `RemoveOptimizations()` cleans up correctly when mode is disabled
- Night Vision flags managed separately from ManagedFlags (toggled independently via `ToggleNightVision()`)

Changelog (technical)
- `Bloxstrap/Integrations/AutoOptimizeService.cs`: added Extreme tier, 20+ new FastFlags, `OptimizeRobloxProcess()`, `TrimBackgroundProcesses()`, P/Invoke declarations, updated `ManagedFlags[]`
- `Bloxstrap/Bootstrapper.cs`: added `OptimizeRobloxProcess` hook after Roblox start with 800ms delay
- `Bloxstrap/UI/ViewModels/Settings/FastFlagsViewModel.cs`: added `ApplyExtremePerformancePreset()`, `ToggleNightVision()`, `NightVisionEnabled`, `ForceExtremeMode`, `ExtremeModeFpsTarget` properties, `ToggleNightVisionCommand`
- `Bloxstrap/UI/Elements/Settings/Pages/FastFlagsPage.xaml`: added Potato Mode card (with "PALING AGRESIF" badge), Force Extreme Mode toggle, FPS cap slider, Night Vision card (with "CLIENT-SIDE ONLY" badge)
- `Bloxstrap/Models/Persistable/Settings.cs`: added `ForceExtremeMode`, `ExtremeModeFpsTarget`, `EnableNightVision`

Notes for CI-release
- This file (RELEASE_NOTES.md) is CI-friendly and can be displayed as release notes in the release pipeline.
- It includes user-facing highlights and developer technical changes.

---

## v3.3.3 - Experimental features: Notifications & FPS Monitor (optimized for low-end)

Release date: 2026-06-04

Highlights
- Added Windows-native notifications for Roblox (friend online, general notifications)
- Added FPS Monitor Overlay (real-time FPS + frame time)
- Added "Optimize for low-end devices" setting to reduce CPU usage on older hardware

Quick links
- Experimental settings: Settings > Experimental
- Notifications docs: NOTIFICATION_FEATURE.md
- FPS Monitor docs: FPS_MONITOR_FEATURE.md

Low-end optimizations (what changed)
- FPS Monitor UI updates are throttled when "Optimize for low-end devices" is enabled (update interval increases from 1s → 2s)
- Frame counting uses CompositionTarget.Rendering (lightweight) instead of a 1ms timer — reduces CPU wakeups
- Preallocated brushes used for color coding to avoid allocations every update
- Notification polling interval increases from 5s → 15s when low-end optimization is enabled
- Less frequent updates and lighter rendering to keep gameplay smooth on older laptops

How to enable low-end optimizations
1. Open BoneFish
2. Go to Settings → Experimental
3. Enable "Optimalkan untuk perangkat low-end (Rendah)"

Developer notes
- Settings added: `OptimizeForLowEnd` (bool), `EnableFpsMonitor` (bool), `FpsMonitorX` (double), `FpsMonitorY` (double)
- Notification polling delay now respects `OptimizeForLowEnd`
- FPS Monitor update interval respects `OptimizeForLowEnd`

Known limitations & further work
- Overlay measures WPF render frames; it may not perfectly reflect in-game GPU-rendered FPS
- Future improvements: memory/GPU usage stats, performance graph, adaptive quality changes

Changelog (technical)
- Bloxstrap/Integrations/RobloxNotification.cs: notification polling interval adjusted for low-end mode
- Bloxstrap/UI/Elements/FpsMonitorOverlay.xaml.cs: switched to CompositionTarget.Rendering for frame counting, throttled UI updates, reduced allocations
- Bloxstrap/Integrations/FpsMonitorService.cs: initialized based on setting
- Bloxstrap/Models/Persistable/Settings.cs: new settings added
- Bloxstrap/UI/Elements/Settings/Pages/ExperimentalPage.xaml: new toggles added
- Bloxstrap/UI/ViewModels/Settings/ExperimentalViewModel.cs: new properties for binding

Notes for CI-release
- This file (CHANGELOG.md) is CI-friendly and is automatically included in GitHub release notes by the CI/CD pipeline.

---

Enjoy — and let us know feedback for further optimization on ultra-low-end hardware.
