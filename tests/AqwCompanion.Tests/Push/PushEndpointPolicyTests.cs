using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Push;

public class PushEndpointPolicyTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com/wp/abc")]
    [InlineData("https://web.push.apple.com/QAbc123")]
    [InlineData("https://a.b.push.apple.com/x")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://db5p.notify.windows.com/?token=abc")]
    [InlineData("https://FCM.googleapis.com:443/fcm/send/abc")]
    public void An_https_endpoint_of_a_known_push_service_is_allowed(string endpoint) => Assert.True(PushEndpointPolicy.IsAllowed(endpoint));

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com.evil.com/x")]
    [InlineData("https://evilfcm.googleapis.com/x")]
    [InlineData("https://fcm.googleapis.com@evil.com/x")]
    [InlineData("https://user:pw@fcm.googleapis.com/x")]
    [InlineData("https://fcm.googleapis.com:8443/x")]
    [InlineData("https://push.apple.com/x")]
    [InlineData("https://evilpush.apple.com/x")]
    [InlineData("https://web.push.apple.com.evil.com/x")]
    [InlineData("https://notify.windows.com/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://fcm.googleapis.com./x")]
    [InlineData("https://fcm.googleapis.com/x#frag")]
    [InlineData("//fcm.googleapis.com/x")]
    [InlineData("fcm.googleapis.com/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? endpoint) => Assert.False(PushEndpointPolicy.IsAllowed(endpoint));

    [Fact]
    public void An_endpoint_over_1024_characters_is_refused()
    {
        Assert.False(PushEndpointPolicy.IsAllowed("https://fcm.googleapis.com/" + new string('a', 1024)));
    }

    [Fact]
    public void TryParse_returns_the_uri_of_an_allowed_endpoint()
    {
        Assert.True(PushEndpointPolicy.TryParse("https://fcm.googleapis.com/fcm/send/abc", out var uri));
        Assert.Equal("fcm.googleapis.com", uri.Host);
    }
}
