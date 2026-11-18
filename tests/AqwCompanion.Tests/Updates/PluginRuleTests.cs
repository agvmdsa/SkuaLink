using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class PluginRuleTests
{
    private static ReleaseVersion V(int a, int b, int c) => new(a, b, c);

    [Fact]
    public void An_update_with_no_connected_plugin_requires_the_new_dll_without_an_installed_version()
    {
        var need = PluginRule.Evaluate("0.7.0", V(0, 8, 0), []);

        Assert.Equal(new PluginRequirement(PluginRequirementReason.UpdateAvailable, V(0, 8, 0), null), need);
    }

    [Fact]
    public void An_update_names_the_plugin_version_a_connected_skua_reports()
    {
        var need = PluginRule.Evaluate("0.7.0", V(0, 8, 0), ["0.7.0"]);

        Assert.Equal(new PluginRequirement(PluginRequirementReason.UpdateAvailable, V(0, 8, 0), "0.7.0"), need);
    }

    [Fact]
    public void An_update_needs_nothing_when_every_connected_plugin_already_is_that_version()
    {
        Assert.Null(PluginRule.Evaluate("0.7.0", V(0, 8, 0), ["0.8.0", "0.8.0"]));
    }

    [Fact]
    public void An_update_picks_the_first_connected_plugin_that_differs()
    {
        var need = PluginRule.Evaluate("0.7.0", V(0, 8, 0), ["0.8.0", "0.7.0", "0.6.0"]);

        Assert.Equal("0.7.0", need!.Installed);
    }

    [Theory]
    [InlineData("0.7.0", "0.6.0")]
    [InlineData("0.7.0", "0.8.0")]
    [InlineData("0.7.0", "0.7.1")]
    public void Without_an_update_a_connected_plugin_that_differs_from_the_app_requires_the_app_version(string app, string plugin)
    {
        var need = PluginRule.Evaluate(app, null, [plugin]);

        Assert.Equal(new PluginRequirement(PluginRequirementReason.VersionMismatch, V(0, 7, 0), plugin), need);
    }

    [Fact]
    public void Without_an_update_a_matching_plugin_or_no_plugin_needs_nothing()
    {
        Assert.Null(PluginRule.Evaluate("0.7.0", null, ["0.7.0"]));
        Assert.Null(PluginRule.Evaluate("0.7.0", null, ["0.7.0", "0.7.0"]));
        Assert.Null(PluginRule.Evaluate("0.7.0", null, []));
    }

    [Fact]
    public void Build_metadata_is_ignored_on_both_sides()
    {
        Assert.Null(PluginRule.Evaluate("0.7.0+abc123", null, ["0.7.0+def456"]));
        Assert.Null(PluginRule.Evaluate("0.7.0", V(0, 8, 0), ["0.8.0+x"]));
    }

    [Theory]
    [InlineData("0.8.0-dev.3+abc")]
    [InlineData("not a version")]
    [InlineData("")]
    public void A_development_or_unreadable_app_version_has_no_release_to_match_so_the_mismatch_rule_is_skipped(string app)
    {
        Assert.Null(PluginRule.Evaluate(app, null, ["0.1.0"]));
    }

    [Fact]
    public void An_update_wins_over_a_mismatch_with_the_app_version()
    {
        var need = PluginRule.Evaluate("0.7.0", V(0, 8, 0), ["0.6.0"]);

        Assert.Equal(PluginRequirementReason.UpdateAvailable, need!.Reason);
        Assert.Equal(V(0, 8, 0), need.Required);
        Assert.Equal("0.6.0", need.Installed);
    }

    [Fact]
    public void Blank_plugin_versions_are_ignored()
    {
        Assert.Null(PluginRule.Evaluate("0.7.0", null, ["", "  "]));
    }
}
