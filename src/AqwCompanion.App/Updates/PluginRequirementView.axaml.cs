using Avalonia.Controls;

namespace AqwCompanion.App.Updates;

/// <summary>The "New DLL required" part with its "Download plugin" button, shared by the banner, the Updates block and the Skua block. DataContext: <see cref="UpdatesViewModel"/>.</summary>
public partial class PluginRequirementView : UserControl
{
    public PluginRequirementView() => InitializeComponent();
}
