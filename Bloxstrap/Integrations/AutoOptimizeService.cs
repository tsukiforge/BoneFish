using System;
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

                tier ??= DetectSystemTier();
                bool isExtreme = tier == SystemTier.ExtremePerformance;
                bool isUltraOrExtreme = tier == SystemTier.UltraLow || isExtreme;

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
                // DFIntDebugFRMQualityLevelOverride=1: kualitas render paling rendah —
                // resolusi internal diturunkan, LOD semua objek ke minimum, kurangi beban GPU.
                // Dipertahankan di level 1 (sudah minimum), tidak diturunkan lebih jauh.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "1");

                // FIntRomarkStartWithGraphicQualityLevel=1: paksa slider kualitas grafis
                // di in-game menu mulai dari level 1, mencegah Roblox auto-detect ke level tinggi.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntRomarkStartWithGraphicQualityLevel", "1");

                // ── SHADOW RENDERING ─────────────────────────────────────────────────────
                // FIntRenderShadowIntensity=0: set intensitas shadow ke 0, bayangan objek
                // tidak ter-render sama sekali — penghematan GPU draw-call paling besar.
                // Sumber: catb0x/Roblox-Potato-FFlags, Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntRenderShadowIntensity", "0");

                // DFFlagDebugPauseVoxelizer=True: hentikan voxelizer sehingga baked/voxel
                // shadows tidak dihitung. Bekerja bersama FIntRenderShadowIntensity=0.
                // Sumber: catb0x/Roblox-Potato-FFlags, Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", "True");

                // FIntCSGVoxelizerFadeRadius=0: matikan fade radius voxelizer sehingga
                // tidak ada komputasi transisi shadow yang sia-sia.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntCSGVoxelizerFadeRadius", "0");

                // FFlagNewLightAttenuation=False: nonaktifkan attenuation pencahayaan baru
                // yang lebih mahal, fallback ke model attenuation lama yang lebih ringan.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                App.FastFlags.SetValue("FFlagNewLightAttenuation", "False");

                // ── SKY, ATMOSPHERE & POST-PROCESSING ────────────────────────────────────
                // FFlagDebugSkyGray=True: ganti skybox default dengan warna abu-abu datar.
                // Menghilangkan render skybox + atmosphere yang mahal (gradient, scatter, dll).
                // Hanya efektif pada game dengan default Roblox skybox.
                // Sumber: catb0x/Roblox-Potato-FFlags, Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FFlagDebugSkyGray", "True");

                // FFlagDisablePostFx=True: matikan semua efek post-processing sekaligus —
                // mencakup bloom, color correction, sun rays, depth of field, dan blur.
                // Ini salah satu flag paling impactful untuk GPU budget rendah.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                App.FastFlags.SetValue("FFlagDisablePostFx", "True");

                // FFlagDebugSSAOForce=False + FIntSSAOMipLevels=0: matikan SSAO
                // (Screen Space Ambient Occlusion) — efek bayangan kontak yang mahal.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FFlagDebugSSAOForce", "False");
                App.FastFlags.SetValue("FIntSSAOMipLevels", "0");

                // FIntRobloxGuiBlurIntensity=0: matikan blur background saat menu ESC dibuka.
                // Blur menu adalah post-process tambahan yang tidak perlu di device kentang.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntRobloxGuiBlurIntensity", "0");

                // ── GRASS, FOLIAGE & ANGIN ────────────────────────────────────────────────
                // FIntFRMMinGrassDistance/FIntFRMMaxGrassDistance=0: set jarak render rumput
                // ke 0 sehingga tidak ada rumput yang digambar sama sekali.
                // FIntRenderGrassDetailStrands=0: hapus helai detail rumput (strand rendering).
                // FIntRenderGrassHeightScaler=0: set skala tinggi rumput ke 0, matikan sepenuhnya.
                // Sumber: catb0x/Roblox-Potato-FFlags, Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntFRMMinGrassDistance", "0");
                App.FastFlags.SetValue("FIntFRMMaxGrassDistance", "0");
                App.FastFlags.SetValue("FIntRenderGrassDetailStrands", "0");
                App.FastFlags.SetValue("FIntRenderGrassHeightScaler", "0");

                // FFlagGlobalWindActivated=False: matikan simulasi angin global —
                // angin menggerakkan foliage dan kain, menambah beban CPU/GPU simulasi.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FFlagGlobalWindActivated", "False");

                // ── MESH LOD / RENDER DISTANCE ───────────────────────────────────────────
                // DFIntCSGLevelOfDetailSwitchingDistance + L12/L23/L34=0: paksa semua mesh
                // ke LOD paling rendah mulai dari jarak 0 — semua objek langsung low-poly
                // tanpa transisi jarak jauh/dekat.
                // Sumber: Firebladedoge229 gist, catb0x/Roblox-Potato-FFlags (confirmed 2026).
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance", "0");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL12", "0");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL23", "0");
                App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34", "0");

                // DFIntCSGv2LodsToGenerate=0: matikan generasi LOD mesh CSGv2 sama sekali —
                // mengurangi waktu load awal dan CPU overhead saat masuk game.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("DFIntCSGv2LodsToGenerate", "0");

                // DFIntDebugRestrictGCDistance=1: paksa GC (Garbage Collector) lebih agresif
                // terhadap aset jauh, efektif menurunkan streaming distance dan tekanan RAM.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("DFIntDebugRestrictGCDistance", "1");

                // ── TERRAIN DETAIL ────────────────────────────────────────────────────────
                // FIntTerrainArraySliceSize=0: kurangi jumlah slice array terrain texture —
                // langsung berdampak pada kualitas terrain (lebih flat/blocky) tapi hemat VRAM.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("FIntTerrainArraySliceSize", "0");

                // ── WATER REFLECTION & EFEK PERMUKAAN ────────────────────────────────────
                // DFFlagDebugRenderForceTechnologyVoxel=True: paksa engine ke mode Voxel
                // Lighting alih-alih ShadowMap/Future. Voxel jauh lebih ringan — tidak ada
                // real-time shadow map, tidak ada screen-space reflections, tidak ada PBR
                // environment specular. Water reflections otomatis hilang di Voxel mode.
                // Sumber: catb0x/Roblox-Potato-FFlags, Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("DFFlagDebugRenderForceTechnologyVoxel", "True");

                // ── GRAIN SCALE & BATCH FLUSH ────────────────────────────────────────────
                // FIntRenderGrainScale=0: matikan efek grain/film noise di post-process pass.
                // Grain adalah efek kosmetik murni, tidak ada gunanya di device kentang.
                App.FastFlags.SetValue("FIntRenderGrainScale", "0");

                // FIntMaxBatchesPerFlush=5000: perbesar ukuran batch render agar flush
                // lebih jarang — mengurangi overhead draw-call dan state-change per frame.
                App.FastFlags.SetValue("FIntMaxBatchesPerFlush", "5000");

                // ── DYNAMIC FACES (AVATAR FACIAL ANIMATION) ──────────────────────────────
                // DFIntAnimationLodFacsDistanceMin/Max=0 + Denominator=0: matikan LOD
                // facial animation (FACS) sepenuhnya — avatar tidak punya ekspresi wajah
                // tapi menghilangkan overhead animasi wajah yang mahal di CPU.
                // Sumber: Firebladedoge229 gist (confirmed 2026).
                App.FastFlags.SetValue("DFIntAnimationLodFacsDistanceMin", "0");
                App.FastFlags.SetValue("DFIntAnimationLodFacsDistanceMax", "0");
                App.FastFlags.SetValue("DFIntAnimationLodFacsVisibilityDenominator", "0");

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

                // ── ULTRALLOW + EXTREME: dynamic lighting + texture compositor ────────────
                if (isUltraOrExtreme)
                {
                    // FIntRenderLocalLightUpdatesMax/Min=1: batasi update dynamic light per frame
                    // ke 1 saja. Scene dengan banyak lampu tetap bisa render tapi CPU overhead
                    // turun drastis — tidak ada burst update saat lampu hidup/mati serentak.
                    // Sumber: catb0x/Roblox-Potato-FFlags (confirmed 2026).
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMax", "1");
                    App.FastFlags.SetValue("FIntRenderLocalLightUpdatesMin", "1");

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

        // FastFlags yang dikelola service ini. Satu tempat agar RemoveOptimizations()
        // bisa membersihkan semua flag ketika low-end mode dimatikan.
        // PENTING: setiap flag baru yang ditambah di ApplyAggressiveOptimizations()
        // HARUS ditambahkan di sini juga, atau tidak akan dibersihkan saat mode dinonaktifkan.
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
            "FFlagNewLightAttenuation",

            // ── Sky, atmosphere & post-processing ────────────────────────────────────────
            "FFlagDebugSkyGray",
            "FFlagDisablePostFx",
            "FFlagDebugSSAOForce",
            "FIntSSAOMipLevels",
            "FIntRobloxGuiBlurIntensity",

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
            "DFIntDebugRestrictGCDistance",

            // ── Terrain detail ────────────────────────────────────────────────────────────
            "FIntTerrainArraySliceSize",

            // ── Water reflection / lighting technology ────────────────────────────────────
            "DFFlagDebugRenderForceTechnologyVoxel",

            // ── Grain scale & batch flush ─────────────────────────────────────────────────
            "FIntRenderGrainScale",
            "FIntMaxBatchesPerFlush",

            // ── Dynamic faces (FACS avatar animation) ────────────────────────────────────
            "DFIntAnimationLodFacsDistanceMin",
            "DFIntAnimationLodFacsDistanceMax",
            "DFIntAnimationLodFacsVisibilityDenominator",

            // ── Task scheduler / FPS cap ──────────────────────────────────────────────────
            "DFIntTaskSchedulerTargetFps",

            // ── Dynamic lighting (UltraLow + Extreme only) ────────────────────────────────
            "FIntRenderLocalLightUpdatesMax",
            "FIntRenderLocalLightUpdatesMin",

            // ── Texture compositor concurrency (UltraLow + Extreme only) ─────────────────
            "DFIntTextureCompositorActiveJobs",
        };

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
