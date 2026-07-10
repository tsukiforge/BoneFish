# BoneFish Release Notes

## v4.5.0 - Fix "Old Version" Warning, Gabung Potato+UltraLow jadi Anti Not-Responding

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

## Mods Folder Analysis

Lokasi: `Bloxstrap/Resources/Mods/`

- **OldAvatarBackground.rbxl** — Roblox place file untuk old avatar background. Direferensi oleh `ModPresetTask` di `ModsViewModel.cs` dengan target `ExtraContent\places\Mobile.rbxl`. Status: **OK, file ada.**
- **Cursor/From2006/** — Direktori kosong. Sepertinya direncanakan untuk cursor style 2006 tapi file cursornya belum ditambahkan. Status: **Direktori kosong — perlu diisi atau dihapus.**
- **Cursor/From2013/** — Direktori kosong. Sama seperti From2006, direncanakan untuk cursor style 2013. Status: **Direktori kosong — perlu diisi atau dihapus.**
- **Sounds/OldJump.mp3** — Sound effect lompat jadul Roblox. Status: **OK.**
- **Sounds/OldWalk.mp3** — Sound effect jalan jadul Roblox. Status: **OK.**
- **Sounds/OldGetUp.mp3** — Sound effect bangun jadul Roblox. Status: **OK.**
- **Sounds/Empty.mp3** — File suara kosong (silence), kemungkinan untuk mute sound tertentu. Status: **OK.**

Rekomendasi: Direktori `Cursor/From2006` dan `Cursor/From2013` kosong — jika fitur cursor mod belum diimplementasi, hapus direktori kosong ini. Jika direncanakan, tambahkan file `.cur` atau `.ani` yang sesuai.

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
- This file (RELEASE_NOTES.md) is CI-friendly and can be displayed as release notes in the release pipeline.
- It includes user-facing highlights and developer technical changes.

---

Enjoy — and let us know feedback for further optimization on ultra-low-end hardware.