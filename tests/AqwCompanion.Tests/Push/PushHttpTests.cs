using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Push;

public class PushHttpTests
{
    [Fact]
    public void The_handler_follows_no_redirects_keeps_no_cookies_and_sends_no_credentials()
    {
        using var handler = PushHttp.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
    }

    [Fact]
    public void Pooled_connections_are_renewed_every_5_minutes_so_a_changed_dns_answer_is_picked_up()
    {
        using var handler = PushHttp.CreateHandler();

        Assert.Equal(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);
    }

    [Fact]
    public void The_client_has_a_deadline_longer_than_one_request_and_finite()
    {
        using var client = PushHttp.Create();

        Assert.True(client.Timeout > PushSender.RequestTimeout);
        Assert.True(client.Timeout < TimeSpan.FromMinutes(1));
    }
}
