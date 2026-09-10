# FPS Unlocker

## Apa ini?

Toggle **independen** di Settings → Fast Flags yang mendeteksi refresh rate monitor
lalu menulis batas maksimum 240 FPS ke pengaturan Roblox
(`GlobalBasicSettings_13.xml` → `FramerateCap`). Bisa di-stack dengan preset
visual apa pun — tidak terikat preset tertentu.

## Apakah beneran nambah FPS atau cuma pajangan?

**Tergantung monitor kamu.**

| Refresh Rate Monitor | Efek |
|---|---|
| **60 Hz** (laptop standar, ThinkPad) | Batas software dapat naik hingga 240 FPS, tetapi layar tetap menampilkan 60 Hz. |
| **75 Hz** | Batas software dapat naik hingga 240 FPS; FPS aktual tetap bergantung pada GPU dan CPU. |
| **120 / 144 / 165 / 240 Hz** | Roblox dapat merender di atas 60 FPS hingga batas 240 FPS, jika GPU dan CPU mampu. |

**Kesimpulan**: fitur ini menaikkan batas software hingga 240 FPS. Itu tidak
menjamin FPS aktual 240 atau membuat FPS tidak terbatas: GPU, CPU, suhu, mode
grafis, dan batas client Roblox tetap menentukan hasil akhirnya.

## Cara kerja

1. User mengaktifkan toggle → muncul *loading configuration*.
2. `EnumDisplaySettings` (Win32) mendeteksi refresh rate monitor untuk logging.
3. `FramerateCap=240` ditulis ke `GlobalBasicSettings_13.xml` (file settings Roblox).
4. Tiap kali BoneFish meluncurkan Roblox, nilai ini di-apply ulang (best-effort).
5. Saat toggle dimatikan → elemen `FramerateCap` dihapus → Roblox kembali ke
   default-nya (60 FPS).

## Kenapa bukan DFIntTaskSchedulerTargetFps?

Flag FastFlag itu sudah **tidak ada di allowlist Roblox sejak 2025-09-29** —
client modern mengabaikannya. FramerateCap di GlobalBasicSettings adalah
pengganti resmi yang diakui Roblox.

## Verifikasi

- **Aktifkan toggle**, lalu cek file:
  `%LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml`
  Cari `<int name="FramerateCap">240</int>`.
- **Nonaktifkan toggle** — elemen `FramerateCap` dihapus dari file tersebut.
- Ukur FPS dengan overlay Roblox atau FPS monitor. Nilai aktual dapat lebih rendah
  dari 240 jika perangkat tidak mampu mempertahankannya.

## File terkait

| File | Peran |
|---|---|
| `Integrations/FpsUnlockerService.cs` | Deteksi monitor + apply/revert FramerateCap |
| `GlobalSettingsManager.cs` | `RemovePreset()` — hapus elemen dari XML |
| `FastFlagsViewModel.cs` | Toggle + loading state + status text |
| `FastFlagsPage.xaml` | OptionControl UI toggle |
| `Bootstrapper.cs` | Re-apply FramerateCap tiap launch |
| `Settings.cs` | `FpsUnlockerEnabled` (persist) |