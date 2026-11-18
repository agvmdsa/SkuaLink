using System.Security.Cryptography;
using System.Text;
using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.TestDoubles;

public static class TestReleases
{
    private const string Host = "https://github.com/agvmdsa/SkuaLink/releases/download/";

    /// <summary>A release with address-valid assets and nothing behind them (for logic that never downloads).</summary>
    public static ReleaseInfo Info(string version)
    {
        ReleaseVersion.TryParseTag("v" + version, out var v);
        Uri Url(string name) => new($"{Host}{v.Tag}/{name}");
        return new ReleaseInfo(
            v,
            new ReleaseAsset(ReleaseParser.InstallerFileName(v), Url(ReleaseParser.InstallerFileName(v)), 1000),
            new ReleaseAsset(ReleaseParser.PluginFileName, Url(ReleaseParser.PluginFileName), 100),
            new ReleaseAsset(ReleaseParser.SumsFileName, Url(ReleaseParser.SumsFileName), 200));
    }

    public static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// A release whose assets a stub handler serves. <paramref name="sums"/> null = the correct sums; otherwise the given text is served
    /// (use <see cref="SumsFor"/> to build variants).
    /// </summary>
    public static (ReleaseInfo Release, StubHttpHandler Handler) Served(string version, byte[] installer, byte[] plugin, string? sums = null)
    {
        ReleaseVersion.TryParseTag("v" + version, out var v);
        var installerName = ReleaseParser.InstallerFileName(v);
        var sumsBytes = Encoding.UTF8.GetBytes(sums ?? SumsFor(installerName, installer, ReleaseParser.PluginFileName, plugin));
        ReleaseAsset Asset(string name, byte[] data) => new(name, new Uri($"{Host}{v.Tag}/{name}"), data.Length);
        var release = new ReleaseInfo(v, Asset(installerName, installer), Asset(ReleaseParser.PluginFileName, plugin), Asset(ReleaseParser.SumsFileName, sumsBytes));
        var handler = new StubHttpHandler(request => Path.GetFileName(request.RequestUri!.AbsolutePath) switch
        {
            var n when n == installerName => StubHttpHandler.Bytes(installer),
            ReleaseParser.PluginFileName => StubHttpHandler.Bytes(plugin),
            ReleaseParser.SumsFileName => StubHttpHandler.Bytes(sumsBytes),
            _ => StubHttpHandler.Status(System.Net.HttpStatusCode.NotFound),
        });
        return (release, handler);
    }

    public static string SumsFor(string installerName, byte[] installer, string pluginName, byte[] plugin) =>
        $"{Hex(installer)}  {installerName}\n{Hex(plugin)}  {pluginName}\n";
}
