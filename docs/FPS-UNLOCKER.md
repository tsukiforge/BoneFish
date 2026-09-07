# FPS Unlocker (BoneFish v7.5+)

## Apa ini?

Toggle **independen** di Settings → Fast Flags yang mendeteksi refresh rate monitor
lalu menulis cap FPS ke pengaturan Roblox (`GlobalBasicSettings_13.xml` →
`FramerateCap`). Bisa di-stack dengan preset visual apa pun — tidak terikat preset
tertentu.

## Apakah beneran nambah FPS atau cuma pajangan?

**Tergantung monitor kamu.**

| Refresh Rate Monitor | Efek |
|---|---|
| **60 Hz** (laptop standar, ThinkPad) | **Placebo / pajangan.** Roblox default cap-nya 60 — tidak ada kenaikan FPS. |
| **75 Hz** | **Naik ±15 FPS** dari default 60 (Roblox render 75 fps, bukan 60). |
| **120 / 144 / 165 / 240 Hz** | **Nyata.** Roblox di-unlock dari 60 ke refresh rate monitor — FPS bisa jauh lebih tinggi (tergantung GPU & CPU). |

**Kesimpulan**: fitur ini FUNGSIONAL, bukan sekadar hiasan. Tapi manfaatnya
hanya terasa di monitor >60 Hz. Di laptop biasa (60 Hz), toggle ini tidak
mengubah apa pun — FramerateCap tetap 60.

## Cara kerja

1. User mengaktifkan toggle → muncul *loading configuration*.
2. `EnumDisplaySettings` (Win32) mendeteksi refresh rate monitor.
3. `FramerateCap` ditulis ke `GlobalBasicSettings_13.xml` (file settings Roblox).
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
  Cari `<int name="FramerateCap">...</int>` — nilainya harus sesuai refresh rate monitor.
- **Nonaktifkan toggle** — elemen `FramerateCap` dihapus dari file tersebut.
- Di dalam game Roblox, buka Settings → Graphics → *Frame Rate Limit* — nilainya
  akan mengikuti apa yang ditulis BoneFish.

## File terkait

| File | Peran |
|---|---|
| `Integrations/FpsUnlockerService.cs` | Deteksi monitor + apply/revert FramerateCap |
| `GlobalSettingsManager.cs` | `RemovePreset()` — hapus elemen dari XML |
| `FastFlagsViewModel.cs` | Toggle + loading state + status text |
| `FastFlagsPage.xaml` | OptionControl UI toggle |
| `Bootstrapper.cs` | Re-apply FramerateCap tiap launch |
| `Settings.cs` | `FpsUnlockerEnabled` (persist) |