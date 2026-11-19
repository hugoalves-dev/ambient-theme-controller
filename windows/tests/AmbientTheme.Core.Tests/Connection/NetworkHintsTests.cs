using System.Net;
using AmbientTheme.Core.Connection;

namespace AmbientTheme.Core.Tests.Connection;

public class NetworkHintsTests
{
    private static readonly LocalNetwork[] Networks =
    [
        new(IPAddress.Parse("192.168.1.20"), 24),
        new(IPAddress.Parse("192.168.137.1"), 24),
    ];

    [Theory]
    [InlineData("192.168.1.236")]
    [InlineData("192.168.137.175")]
    [InlineData("pixel.local")]
    public void Reachable_looking_address_has_no_hint(string host)
    {
        Assert.Null(NetworkHints.Describe(new Endpoint(host, 8766), Networks));
    }

    [Fact]
    public void Carrier_or_vpn_range_is_called_out_as_such()
    {
        var hint = NetworkHints.Describe(new Endpoint("100.66.106.147", 8766), Networks);

        Assert.NotNull(hint);
        Assert.Contains("operadora ou VPN", hint);
        Assert.Contains("192.168.1.0/24", hint);
    }

    [Fact]
    public void Address_outside_every_local_network_lists_the_local_ones()
    {
        var hint = NetworkHints.Describe(new Endpoint("10.0.0.5", 8766), Networks);

        Assert.NotNull(hint);
        Assert.Contains("192.168.137.0/24", hint);
    }

    [Fact]
    public void Without_local_networks_nothing_is_claimed()
    {
        Assert.Null(NetworkHints.Describe(new Endpoint("10.0.0.5", 8766), []));
    }
}
