using System.Text.RegularExpressions;

namespace AqwCompanion.Tests.Architecture;

/// <summary>0.7.0 adds push notifications with the framework's own crypto: the central package list must not grow.</summary>
public class NoNewPackagesTests
{
    private static readonly string[] Expected =
    [
        "AngleSharp", "Avalonia", "Avalonia.Desktop", "Avalonia.Headless.XUnit", "Avalonia.Themes.Fluent", "CommunityToolkit.Mvvm", "LiteDB",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.NET.Test.Sdk", "MinVer", "QRCoder", "Serilog", "Serilog.Extensions.Logging", "Serilog.Sinks.File", "xunit", "xunit.runner.visualstudio",
    ];

    [Fact]
    public void The_central_package_list_is_the_one_of_0_6_0()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Packages.props"));
        var found = Regex.Matches(text, "Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Expected.Order(StringComparer.Ordinal).ToList(), found);
    }

    [Fact]
    public void The_phone_project_still_has_no_package_reference()
    {
        var csproj = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AqwCompanion.Phone", "AqwCompanion.Phone.csproj"));
        Assert.DoesNotContain("PackageReference", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_page_still_has_only_its_four_dev_dependencies_and_no_runtime_dependency()
    {
        var json = File.ReadAllText(Path.Combine(RepositoryRoot(), "web", "package.json"));
        Assert.DoesNotContain("\"dependencies\"", json, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(json, "\"[@a-z/0-9-]+\": \"\\d").Count);
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
