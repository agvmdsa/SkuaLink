using System.Xml.Linq;

namespace AqwCompanion.Tests.Architecture;

/// <summary>Since 0.6.0 nothing needs administrator rights (the capture is gone): the app must start as the user who opened it.</summary>
public class AppManifestTests
{
    private static readonly XNamespace V3 = "urn:schemas-microsoft-com:asm.v3";

    [Fact]
    public void The_manifest_runs_the_app_as_the_invoking_user()
    {
        var level = Manifest().Descendants(V3 + "requestedExecutionLevel").Select(e => (string?)e.Attribute("level")).SingleOrDefault();
        Assert.Equal("asInvoker", level);
    }

    [Fact]
    public void The_manifest_declares_windows_10_support_so_the_app_does_not_run_in_legacy_mode()
    {
        var ids = Manifest()
            .Descendants(XName.Get("supportedOS", "urn:schemas-microsoft-com:compatibility.v1"))
            .Select(e => (string?)e.Attribute("Id"))
            .ToList();

        Assert.Contains("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", ids);
    }

    private static XDocument Manifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AqwCompanion.App", "app.manifest");
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("app.manifest not found above " + AppContext.BaseDirectory);
    }
}
