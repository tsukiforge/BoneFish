using Bloxstrap.Enums;
using Bloxstrap.Models.Persistable;

namespace Bloxstrap.Tests;

public class SettingsMigrationTests
{
    [Fact]
    public void Legacy_title_Bloxstrap_is_reset_to_project_name()
    {
        var settings = new Settings { BootstrapperTitle = "Bloxstrap" };

        Assert.True(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal(App.ProjectName, settings.BootstrapperTitle);
    }

    [Fact]
    public void Legacy_title_Fishstrap_is_reset_to_project_name()
    {
        var settings = new Settings { BootstrapperTitle = "Fishstrap" };

        Assert.True(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal(App.ProjectName, settings.BootstrapperTitle);
    }

    [Fact]
    public void Legacy_title_BoneFish_QA_is_reset_to_project_name()
    {
        var settings = new Settings { BootstrapperTitle = "BoneFish-QA" };

        Assert.True(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal(App.ProjectName, settings.BootstrapperTitle);
    }

    [Fact]
    public void Custom_title_is_never_touched()
    {
        var settings = new Settings { BootstrapperTitle = "Roblox" };

        Assert.False(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal("Roblox", settings.BootstrapperTitle);
    }

    [Fact]
    public void Already_migrated_default_title_is_a_noop()
    {
        var settings = new Settings { BootstrapperTitle = App.ProjectName };

        Assert.False(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal(App.ProjectName, settings.BootstrapperTitle);
    }

    [Fact]
    public void Dead_IconFishstrap_value_is_normalized_to_IconBloxstrap()
    {
        var settings = new Settings { BootstrapperIcon = BootstrapperIcon.IconFishstrap };

        Assert.True(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal(BootstrapperIcon.IconBloxstrap, settings.BootstrapperIcon);
    }

    [Fact]
    public void Valid_icon_is_never_changed()
    {
        var settings = new Settings { BootstrapperIcon = BootstrapperIcon.Icon2011 };

        Assert.False(SettingsMigration.MigrateLegacyValues(settings));
        Assert.Equal(BootstrapperIcon.Icon2011, settings.BootstrapperIcon);
    }
}