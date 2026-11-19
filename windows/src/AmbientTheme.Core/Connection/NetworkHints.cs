using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AmbientTheme.Core.Connection;

public sealed record LocalNetwork(IPAddress Address, int PrefixLength);

/// <summary>
/// Explains the most common manual-endpoint mistake: an address that is not on any network this computer is
/// attached to (for example a carrier/VPN address). It only states what is known; it never blames the network.
/// </summary>
public static class NetworkHints
{
    public static IReadOnlyList<LocalNetwork> LocalNetworks() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(unicast => new LocalNetwork(unicast.Address, unicast.PrefixLength))
            .ToArray();

    public static string? Describe(Endpoint endpoint, IReadOnlyList<LocalNetwork> networks)
    {
        if (networks.Count == 0 || !IPAddress.TryParse(endpoint.Host, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        if (networks.Any(network => SameSubnet(address, network)))
        {
            return null;
        }

        var local = string.Join(", ", networks.Select(Format));
        var bytes = address.GetAddressBytes();
        var carrier = bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
        var what = carrier
            ? "É uma faixa usada por operadora ou VPN (por exemplo dados móveis ou Tailscale), não pela rede local."
            : "Não pertence a nenhuma rede deste computador.";
        return $"{what} Redes deste computador: {local}. Use o endereço que o celular mostra quando está na mesma rede.";
    }

    private static string Format(LocalNetwork network)
    {
        var bytes = network.Address.GetAddressBytes();
        var mask = Mask(network.PrefixLength);
        var net = new IPAddress(bytes.Select((value, index) => (byte)(value & mask[index])).ToArray());
        return $"{net}/{network.PrefixLength}";
    }

    private static bool SameSubnet(IPAddress address, LocalNetwork network)
    {
        var mask = Mask(network.PrefixLength);
        var a = address.GetAddressBytes();
        var b = network.Address.GetAddressBytes();
        return a.Select((value, index) => value & mask[index]).SequenceEqual(b.Select((value, index) => value & mask[index]));
    }

    private static byte[] Mask(int prefix) =>
        Enumerable.Range(0, 4).Select(i => (byte)(prefix >= (i + 1) * 8 ? 0xFF : prefix <= i * 8 ? 0 : 0xFF << (8 - (prefix - i * 8)))).ToArray();
}
