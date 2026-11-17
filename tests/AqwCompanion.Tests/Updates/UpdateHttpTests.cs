using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class UpdateHttpTests
{
    [Fact]
    public void Both_clients_leave_the_deadline_to_each_call()
    {
        using var api = UpdateHttp.CreateApi();
        using var download = UpdateHttp.CreateDownload();

        Assert.Equal(Timeout.InfiniteTimeSpan, api.Timeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, download.Timeout);
    }

    [Fact]
    public void The_handler_settings_are_the_strict_ones()
    {
        using var api = UpdateHttp.CreateApiHandler();
        using var download = UpdateHttp.CreateDownloadHandler();

        foreach (var handler in new[] { api, download })
        {
            Assert.False(handler.AllowAutoRedirect);
            Assert.False(handler.UseCookies);
            Assert.Null(handler.Credentials);
            Assert.False(handler.PreAuthenticate);
        }

        Assert.Equal(System.Net.DecompressionMethods.All, api.AutomaticDecompression);
        Assert.Equal(System.Net.DecompressionMethods.None, download.AutomaticDecompression);
    }
}
