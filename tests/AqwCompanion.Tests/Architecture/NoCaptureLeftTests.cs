namespace AqwCompanion.Tests.Architecture;

/// <summary>Since 0.6.0 Skua is the only data source: no capture project, library or code may come back.</summary>
public class NoCaptureLeftTests
{
    private static readonly string[] CaptureNames = ["SharpPcap", "PacketDotNet", "AqwCompanion.Capture", "Npcap"];

    [Fact]
    public void The_solution_and_the_central_packages_name_no_capture_project_or_library()
    {
        var root = RepositoryRoot();
        foreach (var file in new[] { "AqwCompanion.slnx", "Directory.Packages.props" })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain(CaptureNames, n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void No_project_references_a_capture_project_or_library()
    {
        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
                     .Append(Path.Combine(RepositoryRoot(), "tests", "AqwCompanion.Tests", "AqwCompanion.Tests.csproj")))
        {
            var text = File.ReadAllText(csproj);
            Assert.DoesNotContain(CaptureNames, n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_capture_folders_are_gone()
    {
        var root = RepositoryRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "src", "AqwCompanion.Capture")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "AqwCompanion.Core", "Capture")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "AqwCompanion.App", "GameData")));
    }

    [Fact]
    public void The_app_assembly_references_no_capture_library()
    {
        var referenced = typeof(AqwCompanion.App.AppPaths).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
        Assert.DoesNotContain(referenced, n => CaptureNames.Any(c => n.Contains(c, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_readme_the_docs_and_the_installer_never_mention_npcap_or_the_capture()
    {
        var root = RepositoryRoot();
        var files = new[] { "README.md", Path.Combine("installer", "SkuaLink.iss"), Path.Combine("plugin", "SkuaLink.SkuaBridge", "README.md") }
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md").Select(f => Path.GetRelativePath(root, f)));
        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.False(text.Contains("npcap", StringComparison.OrdinalIgnoreCase), file + " mentions Npcap");
            Assert.False(text.Contains("administrator rights each time", StringComparison.OrdinalIgnoreCase), file + " still asks for administrator rights");
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AqwCompanion.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("AqwCompanion.slnx not found above " + AppContext.BaseDirectory);
    }
}
