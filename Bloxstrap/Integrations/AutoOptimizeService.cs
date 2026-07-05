using System;
using System.Diagnostics;
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
        // OpenProcess: buka handle ke process dengan PID — diperlukan untuk EmptyWorkingSet.
        // PROCESS_SET_INFORMATION (0x0200) cukup untuk set priority/affinity.
        // PROCESS_ALL_ACCESS lebih luas tapi lebih kompatibel di berbagai Windows version.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        // EmptyWorkingSet: paksa Windows trim physical RAM yang dipakai proses lain
        // ke minimum sebelum Roblox start — membebaskan RAM fisik untuk Roblox.
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // CloseHandle: wajib dipanggil setelah OpenProcess agar tidak leak handle.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // PROCESS_ALL_ACCESS — dipakai untuk OpenProcess agar bisa query dan set info.
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

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

        private static SystemTier DetectSystemTier()
        {
            try
            {
                // Jika user menyalakan ForceExtremeMode, abaikan deteksi hardware
                // dan langsung paksa tier ExtremePerformance (Potato Mode).
                if (App.Settings.Prop.ForceExtremeMode)
                    return SystemTier.ExtremePerformance;

                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB = totalMemBytes / (1024UL * 1024 * 1024);

                if (cpuCores >= 4 && totalMemGB >= 16)
                    return SystemTier.HighEnd;
                
                if (cpuCores >= 4 && totalMemGB >= 8)
                    return SystemTier.MidRange;
                
                if (totalMemGB < 4)
                    return SystemTier.UltraLow;
                
                return SystemTier.LowEnd;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error detecting system tier: {ex.Message}");
                return SystemTier.MidRange; // Default fallback
            }
        }

        /// <summary>
        /// Cek dan apply optimasi otomatis berdasarkan spek sistem.
        /// Mengaktifkan OptimizeForLowEnd untuk perangkat low-end dan menerapkan
        /// FastFlag rendering ringan agar Roblox benar-benar lebih lancar.
        /// ForceExtremeMode = true akan memaksa preset ExtremePerformance (Potato Mode)
        /// tanpa memandang hasil deteksi hardware.
        /// </summary>
        public static bool CheckAndApply()
        {
            try
            {
                SystemTier tier = DetectSystemTier();

                // ExtremePerformance bisa dipaksa oleh user via ForceExtremeMode,
                // atau terdeteksi otomatis (saat ini hanya via ForceExtremeMode).
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
                }

                // Apply rendering optimizations whenever low-end mode is active.
                // This must run before Roblox launches so the FastFlags take effect.
                if (App.Settings.Prop.OptimizeForLowEnd)
                {
                    ApplyAggressiveOptimizations(tier);
                    return true;
                }

                // Low-end mode is off: make sure our previously-applied optimization flags are removed
                // so the user isn't permanently stuck on degraded quality.
                return RemoveOptimizations();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error in CheckAndApply: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get detailed system info untuk debugging
        /// </summary>
        public static string GetSystemInfo()
        {
            try
            {
                int cpuCores = Environment.ProcessorCount;
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB = totalMemBytes / (1024UL * 1024 * 1024);
                SystemTier tier = DetectSystemTier();

                return $"CPU Cores: {cpuCores}, RAM: {totalMemGB}GB, Tier: {tier}";
            }
            catch
            {
                return "System info unavailable";
            }
        }

        /// <summary>
        /// Apply rendering optimizations untuk perangkat low-end / ultra-low-end / extreme (Potato Mode).
        /// FastFlag yang diterapkan akan disimpan oleh pemanggil (Bootstrapper) sebelum Roblox berjalan.
        ///
        /// Semua flag di bawah HANYA menggunakan nama yang dikonfirmasi di 2+ sumber komunitas
        /// aktif (Firebladedoge229 gist — last updated Juli 2026; catb0x/Roblox-Potato-FFlags).
        /// Flag di luar allowlist Roblox (sejak late 2025) di-ignore secara silent oleh client —
        /// jadi kami tidak memakai nama yang belum terverifikasi agar optimasi benar-benar efektif.
        /// Sumber referensi:
        ///   - https://gist.github.com/Firebladedoge229/917827fdd94bbec986b503fafb7fb8ba
        ///   - https://github.com/catb0x/Roblox-Potato-FFlags
        ///   - https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569
        /// </summary>
        public static void ApplyAggressiveOptimizations(SystemTier? tier = null)
        {
            try
            {
                if (!App.Settings.Prop.OptimizeForLowEnd)
                    return;

                // Jika user sudah memilih preset manual dari UI (bukan AutoOptimize/None),
                // JANGAN override — biarkan preset user yang menentukan semua flag.
                // Ini mencegah ApplyAggressiveOptimizations OVERWRITE FRM/shadow/etc
                // yang sudah dipilih user di settings (penyebab utama game gelap).
                string preset = App.Settings.Prop.SelectedPerformancePreset ?? "None";
                bool userHasManualPreset = preset is "UltraLow" or "Balanced" or "Stable" or "ExtremePerformance";

                if (userHasManualPreset)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"User has manual preset '{preset}' — skipping aggressive overrides to respect user choice.");
                    return;
                }

                tier ??= DetectSystemTier();
                bool isExtreme = tier == SystemTier.ExtremePerformance;
                bool isUltraOrExtreme = tier == SystemTier.UltraLow || isExtreme;

                // Bersihkan SEMUA flag lama sebelum menulis yang baru —
                // mencegah akumulasi flag antar versi / antar preset.
                PurgeAllKnownFlags();

                // ── TEXTURE QUALITY ──────────────────────────────────────────────────────
                // Paksa kualitas tekstur ke level 0 (terendah) untuk kurangi tekanan VRAM/RAM.
                // DFFlagTextureQualityOverrideEnabled: aktifkan override kualitas tekstur.
                // DFIntTextureQualityOverride=0: set level tekstur ke minimum global.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", "True");
                App.FastFlags.SetValue("DFIntTextureQualityOverride", "0");

                // FIntTextureCompositorLowResFactor=1: paksa compositor tekstur pakai resolusi
                // paling rendah saat generate atlas — kurangi penggunaan VRAM secara signifikan.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntTextureCompositorLowResFactor", "1");

                // ── FRM QUALITY OVERRIDE ──────────────────────────────────────────────────
                // DFIntDebugFRMQualityLevelOverride=3: kualitas render rendah tapi MASIH
                // menjaga lighting pipeline. Nilai 1 (sebelumnya) MEMATIKAN lighting baked
                // dan menyebabkan game jadi HITAM di area yang seharusnya terang.
                // Nilai 3 adalah sweet spot: masih sangat ringan tapi game tetap bisa dilihat.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "3");

                // FIntRomarkStartWithGraphicQualityLevel=1: paksa slider kualitas grafis
                // di in-game menu mulai dari level 1, mencegah Roblox auto-detect ke level tinggi.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntRomarkStartWithGraphicQualityLevel", "1");                // ── SHADOW & VOXELIZER: TIDAK DIMATIKAN ─────────────────────────────────
                // CATATAN KRITIS (PENYEBAB GAME GELAP):
                // FIntRenderShadowIntensity=0 + DFFlagDebugPauseVoxelizer=True sebelumnya
                // digunakan bersama FRM=1. Kombinasi ini MEMATIKAN seluruh pipeline lighting:
                //   - Shadow=0: tidak ada bayangan sama sekali → area yang seharusnya terang
                //     jadi gelap karena tidak ada ambient occlusion / shadow mapping.
                //   - Voxelizer paused: baked voxel lighting tidak dihitung → game horror/RPG
                //     yang bergantung pada dynamic light dari senter/torch jadi hitam.
                //
                // FIX: Kedua flag DIHAPUS. FRM=3 sudah cukup ringan untuk mengurangi
                // shadow quality tanpa mematikannya. Biarkan Roblox handle shadow
                // dan voxelizer dengan default behavior (scaled down by FRM).
                // ── SKY, ATMOSPHERE & POST-PROCESSING ────────────────────────────────────
                // CATATAN: FFlagDebugSkyGray dan FFlagDisablePostFx SENGAJA TIDAK DIPAKAI.
                //
                // FFlagDebugSkyGray: mengganti skybox dengan abu-abu datar — terlihat sangat
                // aneh di game apapun yang punya custom sky/atmosphere. Merusak visual.
                //
                // FFlagDisablePostFx: mematikan post-FX GLOBAL termasuk bloom, color correction,
                // sun rays, dan depth of field milik GAME — bukan hanya Roblox default.
                // Efeknya: visual game berubah total, bisa jadi terlalu gelap/flat/rusak
                // tergantung desain game tersebut. Tidak acceptable untuk preset umum.
                //
                // Yang aman dan tetap dipakai:
                // FIntRobloxGuiBlurIntensity=0: hanya matikan blur menu ESC — tidak menyentuh
                // efek post-processing in-game sama sekali.
                App.FastFlags.SetValue("FIntRobloxGuiBlurIntensity", "0");

                // FFlagDebugSSAOForce=False + FIntSSAOMipLevels=0: matikan SSAO
                // (Screen Space Ambient Occlusion) — efek bayangan kontak yang mahal di GPU.
                // Penghematan nyata, perubahan visual minimal (hanya shadow contact yang halus).
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FFlagDebugSSAOForce", "False");
                App.FastFlags.SetValue("FIntSSAOMipLevels", "0");

                // FIntRenderGrainScale=0: matikan efek grain/film noise.
                // Grain murni kosmetik, tidak ada game yang bergantung pada efek ini.
                App.FastFlags.SetValue("FIntRenderGrainScale", "0");

                // Grass & wind dibiarkan default — tidak memengaruhi rendering speed.

                // ── MESH LOD / RENDER DISTANCE ───────────────────────────────────────────
                // LOD 250/500/750 — objek dekat tetap high-poly, tidak tembus/pop-in.
                // Nilai 0 menyebabkan semua objek jadi low-poly dari jarak 0 — bug aset tembus.
                // Potato Mode dan UltraLow pakai 250 sebagai minimum switch distance.
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance",       "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL12",    "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL23",    isExtreme ? "500" : "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34",    isExtreme ? "750" : "250");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceStatic", "0");
                App.FastFlags.SetValue("DFIntCSGv2LodsToGenerate", "0");

                // DFIntDebugRestrictGCDistance=1: flag ini TERLALU agresif untuk Extreme mode —
                // GC membuang aset jauh terlalu cepat, lalu saat player gerak engine harus
                // re-load semuanya sekaligus → spike RAM → not responding / freeze.
                // Dinonaktifkan di sini. Tekanan RAM sudah cukup ditangani oleh LOD=0 + FRM=1.

                // ── TERRAIN DETAIL ────────────────────────────────────────────────────────
                // FIntTerrainArraySliceSize=0: kurangi slice array terrain texture — hemat VRAM.
                // Terrain jadi sedikit lebih flat tapi tidak mengubah lighting/visual game.
                App.FastFlags.SetValue("FIntTerrainArraySliceSize", "0");

                // DFFlagDebugRenderForceTechnologyVoxel: SENGAJA TIDAK DISET.
                // Memaksa Voxel lighting merusak SEMUA game yang memakai ShadowMap/Future —
                // hasilnya layar hitam total seperti screenshot v3.9.0. Tidak acceptable.

                // ── GRAIN SCALE & BATCH FLUSH ────────────────────────────────────────────
                // FIntRenderGrainScale=0: matikan efek grain/film noise.
                App.FastFlags.SetValue("FIntRenderGrainScale", "0");

                // FIntMaxBatchesPerFlush=5000: perbesar ukuran batch render.
                App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");

                // ── RENDERING SPEED ───────────────────────────────────────────────────────
                // DFIntMaxFrameBufferSize=4: kurangi frame buffer queue → frame lebih cepat
                // ditampilkan, input lag berkurang. Nilai 4 paling stabil (0-3 bikin laggy).
                // Sumber: Dantezz025/Roblox-Fast-Flags (confirmed 2026).
                App.FastFlags.SetValue("DFIntMaxFrameBufferSize", "4");

                // FIntRuntimeMaxNumOfThreads=4: batasi max thread Roblox di dual-core.
                // Context switching overhead berkurang → tiap thread lebih banyak waktu CPU.
                // Sumber: Dantezz025/Roblox-Fast-Flags (confirmed 2026).
                App.FastFlags.SetValue("FIntRuntimeMaxNumOfThreads", "4");

                // DFFlagEnableRequestAsyncCompression=True: kompresi async request aset.
                // Aset lebih cepat di-download saat join → kurangi pop-in/tembus.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("DFFlagEnableRequestAsyncCompression", "True");

                // ── DYNAMIC FACES (AVATAR FACIAL ANIMATION) ──────────────────────────────
                // CATATAN PENTING: flag FACS (DFIntAnimationLodFacsDistanceMin/Max/Denominator)
                // SENGAJA TIDAK DIPAKAI di sini.
                // Mematikan FACS pipeline sepenuhnya (nilai 0) juga mematikan voice activity
                // indicator Roblox — mic user tidak akan naik meski input Discord aktif,
                // karena Roblox memakai pipeline FACS yang sama untuk facial + voice capture.
                // Trade-off tidak sepadan: CPU saving kecil vs fitur mic rusak total.

                // ── TARGET FPS / TASK SCHEDULER ───────────────────────────────────────────
                // DFIntTaskSchedulerTargetFps: batasi target FPS internal Roblox.
                // ExtremePerformance: ambil dari setting user (default 30, minimum 24) —
                //   user bisa turunkan ke 24 untuk device paling lemah via slider di UI.
                // UltraLow + LowEnd: hardcode 30fps — cukup playable, kurangi beban CPU.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                if (isExtreme)
                {
                    // Clamp ke range 24-60fps; nilai user disimpan di Settings.ExtremeModeFpsTarget
                    int fpsCap = Math.Clamp(App.Settings.Prop.ExtremeModeFpsTarget, 24, 60);
                    App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", fpsCap.ToString());
                }
                else
                {
                    App.FastFlags.SetValue("DFIntTaskSchedulerTargetFps", "30");
                }

                // ── ANTI NOT-RESPONDING: FastFlag level ───────────────────────────────────
                // Flag-flag ini mengurangi penyebab main thread stall (freeze/not responding)
                // yang paling umum di device RAM <4GB.

                // DFIntMaxActiveAnimationTracks=32: default Roblox sekitar 200+ track animasi
                // aktif per scene. Di device RAM kecil, Lua GC harus collect lebih banyak objek
                // animasi → GC spike → stall main thread → not responding.
                // 32 adalah nilai minimum yang masih membuat gameplay terasa normal.
                // Sumber: Firebladedoge229 gist, terverifikasi 2026.
                App.FastFlags.SetValue("DFIntMaxActiveAnimationTracks", "32");

                // FIntRenderLocalLightFadeInMs=0: matikan animasi fade-in saat light update.
                // Tanpa flag ini, setiap dynamic light yang berubah punya fade transition
                // yang dijalankan di main thread — pada scene yang lampu-nya sering berubah
                // (misalnya game horror dengan efek flicker), ini adalah source stutter rutin.
                // Sumber: Dantezz025/Roblox-Fast-Flags (confirmed 2026).
                App.FastFlags.SetValue("FIntRenderLocalLightFadeInMs", "0");

                // Telemetry: Roblox kirim event ke server setiap beberapa detik.
                // Di dual-core, thread telemetry wakeup berkompetisi dengan main thread.
                // Matikan semua 7 endpoint telemetry = kurangi background CPU wakeup.
                // Sumber: Firebladedoge229 gist, Dantezz025 (confirmed 2026).
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryEphemeralCounter", "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryEphemeralStat",    "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryEventIngest",      "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryPoint",            "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Counter",        "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Event",          "True");
                App.FastFlags.SetValue("FFlagDebugDisableTelemetryV2Stat",           "True");

                // ── ULTRALLOW + EXTREME: dynamic lighting + texture compositor ────────────
                if (isUltraOrExtreme)
                {
                    // FIntRenderLocalLightUpdatesMax/Min: batasi update dynamic light per frame.
                    // NILAI DIPILIH HATI-HATI:
                    //   - Nilai 1 (sebelumnya): terlalu rendah — senter/torch yang bergerak
                    //     tidak bisa update posisi cahaya cukup cepat, area sekitar jadi hitam
                    //     permanen / bug gelap. Ini terutama parah di game horror/RPG.
                    //   - Nilai 2: minimum yang lebih aman untuk menjaga lighting responsif
                    //     tanpa mengorbankan performance terlalu banyak.
                    //   - Nilai 4: cukup untuk senter bergerak smooth, tetap hemat vs default.
                    //   - Default Roblox: ~8-16 tergantung scene.
                    // Sumber: catb0x/Roblox-Potato-FFlags (confirmed 2026).
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "4");
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "2");

                    // DFIntTextureCompositorActiveJobs=1: batasi worker background yang
                    // meng-composite atlas tekstur ke 1 thread — mencegah tekstur compositor
                    // mencuri semua core di dual-core device.
                    // Catatan: nilai 0 = disable player textures sepenuhnya; 1 = tetap render
                    // tapi dengan concurrency minimal. Kami pilih 1 agar avatar masih terlihat.
                    // Sumber: catb0x/Roblox-Potato-FFlags (confirmed 2026).
                    App.FastFlags.SetValue("DFIntTextureCompositorActiveJobs", "1");

                    // Nonaktifkan overlay BoneFish pada perangkat paling lemah —
                    // FpsMonitor (ETW sampling) dan notifikasi menambah CPU usage di main thread.
                    App.Settings.Prop.EnableFpsMonitor = false;
                    App.Settings.Prop.EnableRobloxNotifications = false;
                    try { App.Settings.Save(); } catch { }

                    string label = isExtreme ? "ExtremePerformance (Potato Mode)" : "UltraLow";
                    App.Logger.WriteLine(LOG_IDENT, $"Aggressive optimizations applied for {label}");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "Rendering optimizations applied for low-end");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error applying aggressive optimizations: {ex.Message}");
            }
        }

        // ── MASTER LIST: semua flag yang PERNAH dikelola BoneFish ────────────────────────
        // Termasuk flag yang sudah dihapus dari versi lama (Voxel, PostFx, SkyGray, dll).
        // Tujuan: PurgeAllKnownFlags() bisa membersihkan ClientAppSettings.json sepenuhnya
        // dari SEMUA sisa flag lama, terlepas dari versi BoneFish yang sebelumnya dipakai.
        // ATURAN: flag apapun yang PERNAH dipakai SetValue() di codebase ini HARUS ada di sini.
        private static readonly string[] AllKnownManagedFlags =
        {
            // Texture quality
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntTextureCompositorLowResFactor",
            "DFIntTextureCompositorActiveJobs",

            // FRM / graphics quality
            "DFIntDebugFRMQualityLevelOverride",
            "FIntRomarkStartWithGraphicQualityLevel",

            // Shadow rendering
            "FIntRenderShadowIntensity",
            "DFFlagDebugPauseVoxelizer",
            "FIntCSGVoxelizerFadeRadius",

            // Lighting technology — termasuk yang sudah DIHAPUS dari preset aktif
            "DFFlagDebugRenderForceTechnologyVoxel",   // DIHAPUS — layar hitam
            "FFlagNewLightAttenuation",                // DIHAPUS — merusak lighting game
            "FFlagFastGPULightCulling3",               // night vision flag

            // Sky, atmosphere & post-processing — termasuk yang sudah DIHAPUS
            "FFlagDebugSkyGray",                       // DIHAPUS — sky abu-abu
            "FFlagDisablePostFx",                      // DIHAPUS — post-FX game mati
            "FFlagDebugSSAOForce",
            "FIntSSAOMipLevels",
            "FIntRobloxGuiBlurIntensity",
            "FIntRenderGrainScale",

            // Grass, foliage & wind
            "FIntFRMMinGrassDistance",
            "FIntFRMMaxGrassDistance",
            "FIntRenderGrassDetailStrands",
            "FIntRenderGrassHeightScaler",
            "FFlagGlobalWindActivated",

            // Mesh LOD / render distance
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",
            "DFIntCSGLevelOfDetailSwitchingDistanceStatic",
            "DFIntCSGv2LodsToGenerate",
            "DFIntDebugRestrictGCDistance",            // DIHAPUS — not responding

            // Terrain detail
            "FIntTerrainArraySliceSize",

            // Dynamic faces (FACS) — termasuk yang sudah DIHAPUS
            "DFIntAnimationLodFacsDistanceMin",        // DIHAPUS — mic rusak
            "DFIntAnimationLodFacsDistanceMax",        // DIHAPUS — mic rusak
            "DFIntAnimationLodFacsVisibilityDenominator", // DIHAPUS — mic rusak

            // Batch flush & render
            "FIntMaxBatchesPerFlush",
            "DFIntMaxFrameBufferSize",
            "FIntRuntimeMaxNumOfThreads",
            "DFFlagEnableRequestAsyncCompression",

            // Task scheduler / FPS cap
            "DFIntTaskSchedulerTargetFps",

            // Dynamic lighting
            "FIntRenderLocalLightUpdatesMax",
            "FIntRenderLocalLightUpdatesMin",
            "FIntRenderLocalLightFadeInMs",

            // Anti not-responding
            "DFIntMaxActiveAnimationTracks",
            "FFlagDebugDisableTelemetryEphemeralCounter",
            "FFlagDebugDisableTelemetryEphemeralStat",
            "FFlagDebugDisableTelemetryEventIngest",
            "FFlagDebugDisableTelemetryPoint",
            "FFlagDebugDisableTelemetryV2Counter",
            "FFlagDebugDisableTelemetryV2Event",
            "FFlagDebugDisableTelemetryV2Stat",

            // ── UI-managed flags (FastFlagsViewModel properties) ────────────────────────
            // DisableRobloxAnimations property
            "FFlagRenderUIAnimations",
            "FFlagRenderMenuTransitions",
            "FFlagRenderInventoryEffects",

            // EnableLowMemoryMode property
            "FFlagLuaAppEnableLowMemoryMode",

            // Network flags (ApplyRecommendedNetworkSettings & all presets)
            "FIntRakNetPacketRateLimit",
            "DFIntMaxReceivePPS",
            "DFIntMaxSendPPS",
            "DFIntConnectionMTUSize",
            "DFIntOptimizeSendQueue",

            // FPS monitor flag (set by Bootstrapper)
            "FFlagDebugDisplayFPS",
        };

        /// <summary>
        /// Hapus SEMUA flag yang pernah dikelola BoneFish dari ClientAppSettings.json —
        /// termasuk flag dari versi lama yang sudah tidak dipakai lagi.
        /// Dipanggil di awal setiap ApplyAggressiveOptimizations() agar tidak ada
        /// sisa flag lama yang terakumulasi antar versi / antar preset.
        /// </summary>
        public static void PurgeAllKnownFlags()
        {
            foreach (string flag in AllKnownManagedFlags)
                App.FastFlags.SetValue(flag, null);

            App.Logger.WriteLine(LOG_IDENT, $"Purged {AllKnownManagedFlags.Length} known managed flags");
        }

        // FastFlags yang aktif dikelola oleh preset saat ini.
        // Dipakai oleh RemoveOptimizations() untuk cleanup saat mode dimatikan.
        // SINKRONISASI: setiap flag di sini HARUS juga ada di AllKnownManagedFlags.
        private static readonly string[] ManagedFlags =
        {
            // ── Texture quality ──────────────────────────────────────────────────────────
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntTextureCompositorLowResFactor",

            // ── FRM / graphics quality ────────────────────────────────────────────────────
            "DFIntDebugFRMQualityLevelOverride",
            "FIntRomarkStartWithGraphicQualityLevel",

            // ── Shadow rendering ─────────────────────────────────────────────────────────
            "FIntRenderShadowIntensity",
            "DFFlagDebugPauseVoxelizer",
            "FIntCSGVoxelizerFadeRadius",

            // ── Lighting technology ──────────────────────────────────────────────────────
            "FFlagFastGPULightCulling3",
            "FFlagNewLightAttenuation",

            // ── Sky, atmosphere & post-processing ────────────────────────────────────────
            "FFlagDebugSSAOForce",
            "FIntSSAOMipLevels",
            "FIntRobloxGuiBlurIntensity",
            "FIntRenderGrainScale",

            // ── Grass, foliage & wind ─────────────────────────────────────────────────────
            "FIntFRMMinGrassDistance",
            "FIntFRMMaxGrassDistance",
            "FIntRenderGrassDetailStrands",
            "FIntRenderGrassHeightScaler",
            "FFlagGlobalWindActivated",

            // ── Mesh LOD / render distance ────────────────────────────────────────────────
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",
            "DFIntCSGv2LodsToGenerate",

            // ── Terrain detail ────────────────────────────────────────────────────────────
            "FIntTerrainArraySliceSize",

            // ── Grain scale & batch flush ─────────────────────────────────────────────────
            "FIntMaxBatchesPerFlush",

            // ── Task scheduler / FPS cap ──────────────────────────────────────────────────
            "DFIntTaskSchedulerTargetFps",

            // ── Dynamic lighting (UltraLow + Extreme only) ────────────────────────────────
            "FIntRenderLocalLightUpdatesMax",
            "FIntRenderLocalLightUpdatesMin",

            // ── Texture compositor concurrency (UltraLow + Extreme only) ─────────────────
            "DFIntTextureCompositorActiveJobs",

            // ── Anti not-responding ───────────────────────────────────────────────────────
            "DFIntMaxActiveAnimationTracks",
            "FIntRenderLocalLightFadeInMs",
            "FFlagDebugDisableTelemetryEphemeralCounter",
            "FFlagDebugDisableTelemetryEphemeralStat",
            "FFlagDebugDisableTelemetryEventIngest",
            "FFlagDebugDisableTelemetryPoint",
            "FFlagDebugDisableTelemetryV2Counter",
            "FFlagDebugDisableTelemetryV2Event",
            "FFlagDebugDisableTelemetryV2Stat",

            // ── UI-managed flags (FastFlagsViewModel) ────────────────────────────────────
            // DisableRobloxAnimations
            "FFlagRenderUIAnimations",
            "FFlagRenderMenuTransitions",
            "FFlagRenderInventoryEffects",

            // EnableLowMemoryMode
            "FFlagLuaAppEnableLowMemoryMode",

            // Network flags
            "FIntRakNetPacketRateLimit",
            "DFIntMaxReceivePPS",
            "DFIntMaxSendPPS",
            "DFIntConnectionMTUSize",
            "DFIntOptimizeSendQueue",

            // FPS monitor
            "FFlagDebugDisplayFPS",
        };

        /// <summary>
        /// Bersihkan ClientAppSettings.json di path Roblox default DAN path BoneFish
        /// dari flag-flag yang pernah kita inject.
        ///
        /// Masalah: BoneFish menulis flags ke path-nya sendiri, tapi jika user
        /// pernah pakai versi lama atau preset lama, sisa flags bisa tertinggal
        /// di multiple path:
        ///   - %localappdata%\Roblox\Versions\xxx\ClientSettings (Roblox default)
        ///   - %localappdata%\BoneFish\Modifications\ClientSettings
        ///   - %localappdata%\BoneFish\Versions\WindowsPlayer\ClientSettings
        /// dan terus dibaca Roblox meski BoneFish sudah update.
        ///
        /// Dipanggil sekali saat BoneFish launch (dari Bootstrapper.Run) untuk
        /// memastikan tidak ada kontaminasi flags lama di semua path.
        /// </summary>
        public static void CleanupLegacyRobloxFlags()
        {
            try
            {
                int totalCleanedFiles = 0;

                // ── Path 1: Roblox default ───────────────────────────────────────────
                string robloxVersionsDir = Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
                if (Directory.Exists(robloxVersionsDir))
                {
                    // Scan semua folder version-xxx
                    foreach (string versionDir in Directory.GetDirectories(robloxVersionsDir, "version-*"))
                    {
                        string clientSettingsPath = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                        if (CleanupClientAppSettings(clientSettingsPath))
                            totalCleanedFiles++;
                    }
                }

                // ── Path 2: BoneFish Modifications ───────────────────────────────────────
                string bonefishModPath = Path.Combine(Paths.Modifications, "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(bonefishModPath) && CleanupClientAppSettings(bonefishModPath))
                    totalCleanedFiles++;

                // ── Path 3: BoneFish Versions\WindowsPlayer ──────────────────────────────
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

        /// <summary>
        /// Bersihkan satu file ClientAppSettings.json dari flag-flag yang kita kelola.
        /// Mengembalikan true jika file dimodifikasi, false jika tidak.
        /// </summary>
        private static bool CleanupClientAppSettings(string clientSettingsPath)
        {
            try
            {
                if (!File.Exists(clientSettingsPath))
                    return false;

                string content = File.ReadAllText(clientSettingsPath).Trim();

                // Skip kalau sudah kosong
                if (content == "{}" || content == "{ }" || string.IsNullOrWhiteSpace(content))
                    return false;

                // Parse dan hapus semua flag yang kita kelola
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
                        .Serialize(flags, new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                    File.WriteAllText(clientSettingsPath, cleaned);
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Cleaned legacy flags from: {clientSettingsPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT,
                    $"Could not clean {clientSettingsPath}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Dipanggil dari Bootstrapper.StartRoblox() setelah Roblox process berhasil start.
        /// Melakukan 3 hal untuk mencegah not-responding pada device RAM terbatas:
        ///
        /// 1. Set Roblox process priority ke AboveNormal — Windows scheduler kasih CPU time
        ///    lebih banyak ke Roblox, mengurangi preemption dari background process Windows.
        ///    Tidak pakai RealTime/High karena bisa deadlock system di dual-core.
        ///
        /// 2. EmptyWorkingSet semua proses non-sistem — paksa Windows swap RAM proses lain
        ///    ke page file, membebaskan RAM fisik untuk Roblox sebelum loading screen selesai.
        ///    Khusus device <4GB di mana margin RAM hampir nol.
        ///
        /// 3. Affinity hint untuk dual-core — hanya aktif jika CPU 2 core. Pin Roblox ke
        ///    kedua core, BoneFish sendiri ke core 0 saja, agar tidak berebut L2 cache.
        ///
        /// Semua operasi wrapped dalam try-catch individual agar satu kegagalan tidak
        /// menghentikan optimasi lainnya.
        /// </summary>
        public static void OptimizeRobloxProcess(int robloxPid)
        {
            if (!App.Settings.Prop.OptimizeForLowEnd)
                return;

            // ── 1. SET ROBLOX PROCESS PRIORITY ───────────────────────────────────────────
            // AboveNormal: satu tingkat di atas Normal, satu di bawah High.
            // High bisa menyebabkan Windows audio/input thread tidak kebagian CPU di dual-core.
            // AboveNormal adalah sweet spot: Roblox dapat lebih banyak CPU tanpa destabilisasi.
            try
            {
                // Buka process by PID — handle process sudah di-dispose di Bootstrapper
                // (sengaja oleh tim Bloxstrap untuk menghindari Byfron trip), jadi kita
                // buka ulang dengan PID yang sudah kita simpan.
                using var robloxProc = Process.GetProcessById(robloxPid);
                robloxProc.PriorityClass = ProcessPriorityClass.AboveNormal;
                App.Logger.WriteLine(LOG_IDENT, $"Set Roblox PID {robloxPid} priority → AboveNormal");
            }
            catch (Exception ex)
            {
                // Bisa gagal jika Roblox sudah exit atau tidak ada permission
                App.Logger.WriteLine(LOG_IDENT, $"Priority set failed (non-fatal): {ex.Message}");
            }

            // ── 2. TRIM WORKING SET PROSES LAIN ──────────────────────────────────────────
            // Hanya dilakukan jika RAM total < 5GB — tidak perlu di device yang cukup RAM.
            try
            {
                ulong totalMemBytes = GetTotalPhysicalMemory();
                ulong totalMemGB    = totalMemBytes / (1024UL * 1024 * 1024);

                if (totalMemGB < 5)
                {
                    TrimBackgroundProcesses(robloxPid);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Memory trim failed (non-fatal): {ex.Message}");
            }

            // ── 3. PROCESSOR AFFINITY HINT (dual-core only) ──────────────────────────────
            // Pada dual-core (2 logical processors), pin BoneFish ke core 0 saja agar
            // core 1 lebih tersedia untuk Roblox main thread tanpa context switch berebut
            // L1/L2 cache yang sama.
            // Roblox sendiri tidak di-pin — biarkan OS scheduler yang manage, karena
            // Roblox punya thread pool sendiri yang perlu fleksibel.
            try
            {
                if (Environment.ProcessorCount <= 2)
                {
                    // Pin BoneFish ke core 0 saja (bit mask: 0b01 = core 0)
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

        /// <summary>
        /// Trim RAM usage semua proses background (bukan Roblox, bukan system critical).
        /// EmptyWorkingSet memaksa Windows memindahkan page-page RAM proses ke page file,
        /// membebaskan RAM fisik untuk Roblox tanpa mematikan proses tersebut.
        ///
        /// Target: proses non-Roblox, non-sistem, non-BoneFish dengan working set > 20MB.
        /// Proses yang di-skip: System, smss, csrss, lsass, svchost, winlogon, explorer
        /// (mematikan atau trim terlalu agresif pada ini bisa destabilisasi Windows).
        /// </summary>
        private static void TrimBackgroundProcesses(int robloxPid)
        {
            // Daftar nama proses sistem kritis yang tidak boleh di-trim
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
                    // Skip Roblox sendiri dan BoneFish
                    if (proc.Id == robloxPid)       continue;
                    if (proc.ProcessName == selfName) continue;

                    // Skip proses sistem kritis
                    if (skipNames.Contains(proc.ProcessName)) continue;

                    // Hanya trim proses dengan working set > 20MB — tidak worth trim yang kecil
                    long workingSetKB = proc.WorkingSet64 / 1024;
                    if (workingSetKB < 20 * 1024) continue;

                    // Buka process handle dengan akses minimum yang diperlukan
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
                catch
                {
                    // Skip proses yang tidak bisa diakses (elevated, protected, dll)
                    // — ini normal, tidak perlu log tiap proses
                }
            }

            if (trimmedCount > 0)
            {
                App.Logger.WriteLine(LOG_IDENT,
                    $"RAM trim: {trimmedCount} background processes trimmed, " +
                    $"~{totalFreedKB / 1024}MB working set released");
            }
        }

        /// <summary>
        /// Remove any optimization FastFlags this service previously applied.
        /// Returns true if at least one flag was removed.
        /// </summary>
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

                return removedAny;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error removing optimizations: {ex.Message}");
                return false;
            }
        }
    }
}
