using AqwCompanion.App;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Updates;

public class UpdatesCompositionTests
{
    private static AppPaths Paths() => new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public void The_container_wires_the_update_services_and_attaches_the_view_model_to_the_settings_without_a_request_or_a_file()
    {
        var paths = Paths();
        using var services = CompositionRoot.Build(paths);

        var updates = services.GetRequiredService<UpdatesViewModel>();

        Assert.Same(updates, services.GetRequiredService<SettingsViewModel>().Updates);
        Assert.Same(services.GetRequiredService<UpdateChecker>(), services.GetRequiredService<IUpdateState>());
        Assert.Equal(UpdateStatus.NeverChecked, services.GetRequiredService<UpdateChecker>().Current.Status);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void The_privacy_text_names_github_and_the_unchanged_forget_text_is_kept()
    {
        Assert.Contains("GitHub", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.Contains("Check for updates", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Only three things", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.Contains("the push service of your phone's browser (Apple, Google, Mozilla or Microsoft)", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.Equal(
            "Remove everything the app saved on this computer? Your saved characters, Skua logs, script queues and the Hard Farms list are cleared, and so is the game data saved by versions before 0.6.",
            SettingsViewModel.ForgetPromptText);
    }
}
