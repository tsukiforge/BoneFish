using Bloxstrap.Enums.FlagPresets;
using System.Security.Policy;
using System.Windows;

namespace Bloxstrap
{
    public class FastFlagManager : JsonManager<Dictionary<string, object>>
    {
        public const string BetaMSAAFlag = "FIntDebugForceMSAASamples";
        public const string BetaFRMQualityFlag = "DFIntDebugFRMQualityLevelOverride";
        public const string BetaTextureQualityEnabledFlag = "DFFlagTextureQualityOverrideEnabled";
        public const string BetaTextureQualityFlag = "DFIntTextureQualityOverride";

        public static IReadOnlySet<string> BetaTestableLegacyFlags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BetaMSAAFlag,
            BetaFRMQualityFlag,
            BetaTextureQualityEnabledFlag,
            BetaTextureQualityFlag
        };

        private static readonly HashSet<string> ObservedRejectedFlags = new(StringComparer.OrdinalIgnoreCase)
        {
            "DFIntConnectionMTUSize",
            "DFIntCSGLevelOfDetailSwitchingDistanceStatic",
            "DFIntCSGv2LodsToGenerate",
            "DFIntMaxReceivePPS",
            "DFIntMaxSendPPS",
            "DFIntOptimizeSendQueue",
            "DFIntTextureCompositorActiveJobs",
            "DFFlagEnableRequestAsyncCompression",
            "FFlagDebugSSAOForce",
            "FFlagLuaAppEnableLowMemoryMode",
            "FFlagRenderInventoryEffects",
            "FFlagRenderMenuTransitions",
            "FFlagRenderUIAnimations",
            "FIntMaxBatchesPerFlush",
            "FIntRakNetPacketRateLimit",
            "FIntRobloxGuiBlurIntensity",
            "FIntRomarkStartWithGraphicQualityLevel",
            "FIntSSAOMipLevels",
            "FIntTerrainArraySliceSize"
        };

        private static readonly HashSet<string> ReportedRejectedFlags = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyDictionary<string, string> RejectedLegacyFlagValues { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DFIntConnectionMTUSize"] = "1500",
                ["DFIntCSGLevelOfDetailSwitchingDistanceStatic"] = "0",
                ["DFIntCSGv2LodsToGenerate"] = "0",
                ["DFIntMaxReceivePPS"] = "50000",
                ["DFIntMaxSendPPS"] = "50000",
                ["DFIntOptimizeSendQueue"] = "1",
                ["DFIntTextureCompositorActiveJobs"] = "1",
                ["DFFlagEnableRequestAsyncCompression"] = "True",
                ["FFlagDebugSSAOForce"] = "False",
                ["FFlagLuaAppEnableLowMemoryMode"] = "True",
                ["FFlagRenderInventoryEffects"] = "False",
                ["FFlagRenderMenuTransitions"] = "False",
                ["FFlagRenderUIAnimations"] = "False",
                ["FIntMaxBatchesPerFlush"] = "5000",
                ["FIntRakNetPacketRateLimit"] = "50000",
                ["FIntRobloxGuiBlurIntensity"] = "0",
                ["FIntRomarkStartWithGraphicQualityLevel"] = "1",
                ["FIntSSAOMipLevels"] = "0",
                ["FIntTerrainArraySliceSize"] = "0"
            };

        public static IReadOnlySet<string> FlagsRejectedByRobloxLogs => ObservedRejectedFlags;

        public static bool IsFlagRejectedByRobloxLogs(string name) => ObservedRejectedFlags.Contains(name);

        public override string ClassName => nameof(FastFlagManager);

        public override string LOG_IDENT_CLASS => ClassName;

        public override string ProfilesLocation => Path.Combine(Paths.Base, "Profiles");

        public override string FileLocation => Path.Combine(Paths.Modifications, "ClientSettings\\ClientAppSettings.json");

        public bool Changed => !OriginalProp.SequenceEqual(Prop);

        public static IReadOnlyDictionary<string, string> PresetFlags = new Dictionary<string, string>
        {

            // Presets and stuff
            { "Rendering.ManualFullscreen", "FFlagHandleAltEnterFullscreenManually" },
            { "Rendering.DisableScaling", "DFFlagDisableDPIScale" },

            // Rendering engines
            { "Rendering.Mode.D3D11", "FFlagDebugGraphicsPreferD3D11" },
            { "Rendering.Mode.Vulkan", "FFlagDebugGraphicsPreferVulkan" },
        };

        public static IReadOnlyDictionary<RenderingMode, string> RenderingModes => new Dictionary<RenderingMode, string>
        {
            { RenderingMode.Default, "None" },
            { RenderingMode.Vulkan, "Vulkan" },
            { RenderingMode.D3D11, "D3D11" },
        };

        // all fflags are stored as strings
        // to delete a flag, set the value as null
        public void SetValue(string key, object? value)
        {
            const string LOG_IDENT = "FastFlagManager::SetValue";

            if (value is null)
            {
                if (Prop.ContainsKey(key))
                    App.Logger.WriteLine(LOG_IDENT, $"Deletion of '{key}' is pending");

                Prop.Remove(key);
            }
            else
            {
                if (IsFlagRejectedByRobloxLogs(key) && !App.Settings.Prop.EnableRejectedLegacyFastFlags)
                {
                    Prop.Remove(key);
                    if (ReportedRejectedFlags.Add(key))
                    {
                        App.Logger.WriteLine(LOG_IDENT,
                            $"Blocked '{key}' because the user's Roblox 0.741 client log reported it as denied local configuration.");
                    }
                    return;
                }

                if (Prop.ContainsKey(key))
                {
                    if (value.ToString() == Prop[key].ToString())
                        return;

                    App.Logger.WriteLine(LOG_IDENT, $"Changing of '{key}' from '{Prop[key]}' to '{value}' is pending");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Setting of '{key}' to '{value}' is pending");
                }

                Prop[key] = value.ToString()!;
            }
        }

        // this returns null if the fflag doesn't exist
        public string? GetValue(string key)
        {
            // check if we have an updated change for it pushed first
            if (Prop.TryGetValue(key, out object? value) && value is not null)
                return value.ToString();

            return null;
        }

        public void SetPreset(string prefix, object? value)
        {
            foreach (var pair in PresetFlags.Where(x => x.Key.StartsWith(prefix)))
                SetValue(pair.Value, value);
        }

        public void SetPresetEnum(string prefix, string target, object? value)
        {
            foreach (var pair in PresetFlags.Where(x => x.Key.StartsWith(prefix)))
            {
                if (pair.Key.StartsWith($"{prefix}.{target}"))
                    SetValue(pair.Value, value);
                else
                    SetValue(pair.Value, null);
            }
        }

        public string? GetPreset(string name)
        {
            if (!PresetFlags.ContainsKey(name))
            {
                App.Logger.WriteLine("FastFlagManager::GetPreset", $"Could not find preset {name}");
                Debug.Assert(false, $"Could not find preset {name}");
                return null;
            }

            return GetValue(PresetFlags[name]);
        }

        public T GetPresetEnum<T>(IReadOnlyDictionary<T, string> mapping, string prefix, string value) where T : Enum
        {
            foreach (var pair in mapping)
            {
                if (pair.Value == "None")
                    continue;

                if (GetPreset($"{prefix}.{pair.Value}") == value)
                    return pair.Key;
            }

            return mapping.First().Key;
        }

        public bool IsPreset(string Flag) => PresetFlags.Values.Any(v => v.ToLower() == Flag.ToLower());

        public override void Save()
        {
            foreach (string flag in Prop.Keys.Where(IsFlagRejectedByRobloxLogs)
                         .Where(_ => !App.Settings.Prop.EnableRejectedLegacyFastFlags)
                         .ToArray())
            {
                Prop.Remove(flag);
                if (ReportedRejectedFlags.Add(flag))
                {
                    App.Logger.WriteLine(LOG_IDENT_CLASS,
                        $"Removed Roblox-rejected flag '{flag}' before saving ClientAppSettings.");
                }
            }

            // convert all flag values to strings before saving

            foreach (var pair in Prop)
                Prop[pair.Key] = pair.Value.ToString()!;

            base.Save();

            // clone the dictionary
            OriginalProp = new(Prop);
        }

        public override void Load(bool alertFailure = true)
        {
            base.Load(alertFailure);

            bool fastFlagsChanged = false;
            bool settingsChanged = false;

            if (App.Settings.Prop.EnableRejectedLegacyFastFlags)
            {
                foreach ((string flag, string flagValue) in RejectedLegacyFlagValues)
                {
                    if (GetValue(flag) == flagValue)
                        continue;

                    SetValue(flag, flagValue);
                    fastFlagsChanged = true;
                }
            }

            if (App.Settings.Prop.DisableRobloxAnimations || App.Settings.Prop.EnableLowMemoryMode)
            {
                App.Settings.Prop.DisableRobloxAnimations = false;
                App.Settings.Prop.EnableLowMemoryMode = false;
                settingsChanged = true;
                App.Logger.WriteLine(LOG_IDENT_CLASS,
                    "Disabled animation and low-memory toggles because their flags were denied by the user's Roblox 0.741 client log.");
            }

            if (App.Settings.Prop.EnableFastLoadingFlags && Environment.ProcessorCount < 8)
            {
                App.Settings.Prop.EnableFastLoadingFlags = false;
                settingsChanged = true;
                App.Logger.WriteLine(LOG_IDENT_CLASS,
                    "Disabled Fast Loading because this CPU has fewer than 8 logical processors and its compositor flag was denied by the user's Roblox 0.741 client log.");
            }

            foreach (string flag in Prop.Keys.Where(IsFlagRejectedByRobloxLogs)
                         .Where(_ => !App.Settings.Prop.EnableRejectedLegacyFastFlags)
                         .ToArray())
            {
                Prop.Remove(flag);
                fastFlagsChanged = true;
                App.Logger.WriteLine(LOG_IDENT_CLASS,
                    $"Removed '{flag}' from existing ClientAppSettings because the user's Roblox 0.741 client log reported it as denied.");
            }

            if (settingsChanged)
            {
                try { App.Settings.Save(); }
                catch (Exception ex) { App.Logger.WriteException(LOG_IDENT_CLASS, ex); }
            }

            if (GetPreset("Rendering.ManualFullscreen") != "False")
            {
                SetPreset("Rendering.ManualFullscreen", "False");
                fastFlagsChanged = true;
            }

            if (fastFlagsChanged)
                Save();
            else
                OriginalProp = new(Prop);
        }
    }
}
