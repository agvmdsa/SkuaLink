using System.Text.RegularExpressions;
using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Architecture;

/// <summary>The page's list of push kinds and the PC's list must be the same names in the same order.</summary>
public class PushKindOrderTests
{
    [Fact]
    public void The_page_PUSH_KINDS_equal_PushKinds_All_in_order()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "web", "src", "push.ts"));
        var list = Regex.Match(source, @"PUSH_KINDS\s*=\s*\[(?<names>[^\]]*)\]");
        Assert.True(list.Success, "web/src/push.ts has no PUSH_KINDS list");
        var names = Regex.Matches(list.Groups["names"].Value, "'([A-Za-z]+)'").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(13, names.Count);
        Assert.Equal(PushKinds.All, names);
    }

    [Fact]
    public void The_page_has_a_label_for_every_kind()
    {
        var labels = File.ReadAllText(Path.Combine(RepositoryRoot(), "web", "src", "i18n", "pushEn.ts"));
        foreach (var kind in PushKinds.All)
        {
            Assert.Contains($"'push.kind.{kind}':", labels);
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
