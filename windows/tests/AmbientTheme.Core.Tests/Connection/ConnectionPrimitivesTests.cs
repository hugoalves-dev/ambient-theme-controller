using System.Net.Sockets;
using System.Security.Authentication;
using AmbientTheme.Core.Connection;

namespace AmbientTheme.Core.Tests.Connection;

public class ConnectionPrimitivesTests
{
    [Theory]
    [InlineData("192.168.137.175:8766", "192.168.137.175", 8766)]
    [InlineData(" pixel.local:9000 ", "pixel.local", 9000)]
    [InlineData("[fe80::1]:8766", "fe80::1", 8766)]
    public void Endpoint_parses_host_and_port(string text, string host, int port)
    {
        Assert.True(Endpoint.TryParse(text, out var endpoint));
        Assert.Equal(new Endpoint(host, port), endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("192.168.0.1")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("host:abc")]
    [InlineData(":8766")]
    [InlineData("http://host:8766/path")]
    public void Endpoint_refuses_invalid_text(string text)
    {
        Assert.False(Endpoint.TryParse(text, out _));
    }

    [Fact]
    public void Endpoint_text_roundtrips_including_ipv6()
    {
        Assert.Equal("[fe80::1]:8766", new Endpoint("fe80::1", 8766).ToString());
        Assert.Equal("pixel.local:9000", new Endpoint("pixel.local", 9000).ToString());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 15)]
    [InlineData(5, 30)]
    [InlineData(50, 30)]
    public void Backoff_follows_the_bounded_schedule(int attempt, double seconds)
    {
        var policy = new BackoffPolicy(() => 0.5);

        Assert.Equal(TimeSpan.FromSeconds(seconds), policy.Delay(attempt));
    }

    [Theory]
    [InlineData(0.0, 6.4)]
    [InlineData(0.999, 9.6)]
    public void Backoff_jitter_stays_within_twenty_percent(double random, double expectedSeconds)
    {
        var delay = new BackoffPolicy(() => random).Delay(3);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 1);
    }

    [Fact]
    public void Resolver_orders_candidates_by_trust_and_removes_duplicates()
    {
        var active = new Endpoint("10.0.0.5", 8766);
        var discovered = new Endpoint("10.0.0.9", 8766);
        var known = new Endpoint("192.168.137.175", 8766);
        var manual = new Endpoint("pixel.local", 8766);

        var ordered = EndpointResolver.Order(active, [discovered, active], [known, discovered], manual);

        Assert.Equal([active, discovered, known, manual], ordered);
    }

    [Fact]
    public void Resolver_works_with_only_a_manual_endpoint()
    {
        var manual = new Endpoint("pixel.local", 8766);

        Assert.Equal([manual], EndpointResolver.Order(null, [], [], manual));
    }

    [Fact]
    public void Invite_has_a_strong_single_use_token_and_expires_in_two_minutes()
    {
        var now = DateTimeOffset.Parse("2026-11-16T10:00:00Z");
        var first = PairingInvite.Create("pc-1", "Desktop", now);
        var second = PairingInvite.Create("pc-1", "Desktop", now);

        Assert.NotEqual(first.Token, second.Token);
        Assert.True(Convert.FromBase64String(first.Token.Replace('-', '+').Replace('_', '/') + "=").Length >= 32);
        Assert.False(first.IsExpired(now.AddSeconds(119)));
        Assert.True(first.IsExpired(now.AddSeconds(120)));
    }

    [Fact]
    public void Invite_roundtrips_through_the_qr_uri()
    {
        var invite = PairingInvite.Create("pc-1", "Meu PC & cia", DateTimeOffset.Parse("2026-11-16T10:00:00Z"));

        Assert.True(PairingInvite.TryParse(invite.ToUri(), out var parsed));
        Assert.Equal(invite, parsed);
    }

    [Theory]
    [InlineData("https://example.com/pair?v=1&c=a&n=b&t=c&e=1")]
    [InlineData("ambienttheme://pair?v=2&c=a&n=b&t=c&e=1")]
    [InlineData("ambienttheme://pair?v=1&c=a&n=b")]
    [InlineData("garbage")]
    public void Foreign_or_incomplete_qr_content_is_refused(string uri)
    {
        Assert.False(PairingInvite.TryParse(uri, out _));
    }

    [Fact]
    public void Fingerprint_is_shown_in_short_comparable_groups()
    {
        Assert.Equal("0123-4567-89AB-CDEF", CertificatePin.DisplayCode("0123456789abcdef0123456789abcdef"));
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, ConnectionFailure.Refused)]
    [InlineData(SocketError.TimedOut, ConnectionFailure.Timeout)]
    [InlineData(SocketError.HostUnreachable, ConnectionFailure.Unreachable)]
    [InlineData(SocketError.NetworkUnreachable, ConnectionFailure.Unreachable)]
    [InlineData(SocketError.HostNotFound, ConnectionFailure.Unreachable)]
    public void Socket_errors_map_to_honest_diagnostics(SocketError error, ConnectionFailure expected)
    {
        var wrapped = new System.Net.WebSockets.WebSocketException("failed",
            new HttpRequestException("failed", new SocketException((int)error)));

        Assert.Equal(expected, ConnectionErrors.Classify(wrapped));
    }

    [Fact]
    public void Tls_failure_maps_to_identity_mismatch()
    {
        var wrapped = new System.Net.WebSockets.WebSocketException("failed",
            new HttpRequestException("failed", new AuthenticationException("cert")));

        Assert.Equal(ConnectionFailure.TlsIdentityMismatch, ConnectionErrors.Classify(wrapped));
    }

    [Fact]
    public void Cancelled_attempt_maps_to_timeout()
    {
        Assert.Equal(ConnectionFailure.Timeout, ConnectionErrors.Classify(new OperationCanceledException()));
    }
}
