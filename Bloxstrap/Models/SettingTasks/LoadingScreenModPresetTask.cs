using Bloxstrap.Models.SettingTasks.Base;

namespace Bloxstrap.Models.SettingTasks
{
    public class LoadingScreenModPresetTask : StringBaseTask
    {
        public LoadingScreenModPresetTask() : base("Mods", "CustomLoadingScreen") { }

        public override void Execute()
        {
            if (String.IsNullOrEmpty(OriginalState))
            {
                if (File.Exists(Paths.CustomLoadingScreen))
                    OriginalState = Paths.CustomLoadingScreen;
            }

            if (String.IsNullOrEmpty(NewState))
            {
                if (File.Exists(Paths.CustomLoadingScreen))
                    File.Delete(Paths.CustomLoadingScreen);
            }
            else
            {
                if (String.Compare(NewState, Paths.CustomLoadingScreen, StringComparison.InvariantCultureIgnoreCase) != 0 && File.Exists(NewState))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Paths.CustomLoadingScreen)!);
                    File.Copy(NewState, Paths.CustomLoadingScreen, true);
                }
            }
        }
    }
}
