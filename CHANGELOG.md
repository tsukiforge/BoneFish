# BoneFish Changelog

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
