namespace Bloxstrap
{
    public static class SettingsMigration
    {
        // Historical names this project was known by before the rename to BoneFish.
        // Only EXACT matches are reset — a user's own custom title (e.g. "Roblox")
        // is never touched. "BoneFish-QA" is included so saves made by QA builds
        // can't leak a "BoneFish-QA" title into production.
        private static readonly string[] LegacyProjectNames = { "Bloxstrap", "Fishstrap", "BoneFish-QA" };

        /// <summary>
        /// One-time migration for legacy values persisted in Settings.json from builds
        /// that predate the rename to BoneFish. Returns true when something changed and
        /// the settings should be saved back to disk; false otherwise. Callers save the
        /// result, which makes the migration self-limiting — after the first save the
        /// persisted values no longer match, so it never rewrites user preferences again.
        /// </summary>
        public static bool MigrateLegacyValues(Settings settings)
        {
            bool changed = false;

            // FIX v7.4.0: BootstrapperTitle stuck on a pre-rename project name for users
            // whose Settings.json was created before the rename and never migrated.
            if (LegacyProjectNames.Contains(settings.BootstrapperTitle, StringComparer.Ordinal))
            {
                settings.BootstrapperTitle = App.ProjectName;
                changed = true;
            }

            // BootstrapperIcon audit (FIX v7.4.0): IconFishstrap is a dead enum value —
            // removed from the UI selections, and GetIcon() already falls through to the
            // Bloxstrap icon. Normalize it once so a stored legacy value can't surprise
            // users with a stale icon mapping if that fallback ever changes.
            if (settings.BootstrapperIcon == BootstrapperIcon.IconFishstrap)
            {
                settings.BootstrapperIcon = BootstrapperIcon.IconBloxstrap;
                changed = true;
            }

            return changed;
        }
    }
}