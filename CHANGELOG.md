# BoneFish Changelog

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
