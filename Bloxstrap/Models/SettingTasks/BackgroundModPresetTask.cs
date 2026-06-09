using Bloxstrap.Models.SettingTasks.Base;

namespace Bloxstrap.Models.SettingTasks
{
    public class BackgroundModPresetTask : StringBaseTask
    {
        public BackgroundModPresetTask() : base("Mods", "CustomBackground") { }

        public override bool Changed
        {
            get
            {
                if (!File.Exists(Paths.CustomBackground))
                    return false;

                using var fileStream = File.OpenRead(Paths.CustomBackground);
                return MD5Hash.FromStream(fileStream) != OriginalState;
            }
        }

        public override void Execute()
        {
            if (String.IsNullOrEmpty(OriginalState))
            {
                if (File.Exists(Paths.CustomBackground))
                    OriginalState = Paths.CustomBackground;
            }

            if (String.IsNullOrEmpty(NewState))
            {
                if (File.Exists(Paths.CustomBackground))
                    File.Delete(Paths.CustomBackground);
            }
            else
            {
                if (String.Compare(NewState, Paths.CustomBackground, StringComparison.InvariantCultureIgnoreCase) != 0 && File.Exists(NewState))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Paths.CustomBackground)!);
                    File.Copy(NewState, Paths.CustomBackground, true);
                }
            }
        }
    }
}
