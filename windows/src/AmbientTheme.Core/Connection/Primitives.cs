using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AmbientTheme.Core.Connection;

/// <summary>A place to try. Never an identity: the same device may show up at any address.</summary>
public sealed record Endpoint(string Host, int Port)
{
    public static bool TryParse(string? text, out Endpoint endpoint)
    {
        endpoint = new Endpoint(string.Empty, 0);
        var trimmed = text?.Trim() ?? string.Empty;
        var separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || trimmed.Contains('/'))
        {
            return false;
        }

        var host = trimmed[..separator].Trim('[', ']');
        if (host.Length == 0
            || !int.TryParse(trimmed[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        endpoint = new Endpoint(host, port);
        return true;
    }

    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

/// <summary>Reconnection schedule 1, 2, 4, 8, 15, 30 s with ±20 % jitter (spec §6).</summary>
public sealed class BackoffPolicy(Func<double>? random = null)
{
    private static readonly int[] Seconds = [1, 2, 4, 8, 15, 30];
    private readonly Func<double> _random = random ?? Random.Shared.NextDouble;

    public TimeSpan Delay(int attempt)
    {
        var seconds = Seconds[Math.Clamp(attempt, 0, Seconds.Length - 1)];
        return TimeSpan.FromSeconds(seconds * (0.8 + (0.4 * _random())));
    }
}

public static class EndpointResolver
{
    /// <summary>
    /// Candidate order: last authenticated endpoint, fresh discovery, previously seen endpoints, manual entry.
    /// All of them are hints; only the TLS identity and credential decide who is on the other side.
    /// </summary>
    public static IReadOnlyList<Endpoint> Order(Endpoint? lastActive, IEnumerable<Endpoint> discovered,
        IEnumerable<Endpoint> known, Endpoint? manual)
    {
        IEnumerable<Endpoint?> all = [lastActive, .. discovered, .. known, manual];
        return all.OfType<Endpoint>().Distinct().ToArray();
    }
}

/// <summary>Source of mDNS/DNS-SD candidates. The Windows implementation depends on the discovery PoC (issue #5).</summary>
public interface IDiscovery
{
    IReadOnlyList<Endpoint> Current { get; }
}

/// <summary>Identity of the Android: SHA-256 of the certificate's SubjectPublicKeyInfo, pinned at pairing.</summary>
public static class CertificatePin
{
    public static string Compute(X509Certificate certificate)
    {
        using var parsed = new X509Certificate2(certificate);
        return Convert.ToHexStringLower(SHA256.HashData(parsed.PublicKey.ExportSubjectPublicKeyInfo()));
    }

    public static bool Matches(X509Certificate certificate, string expectedPin) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Compute(certificate)),
            System.Text.Encoding.ASCII.GetBytes(expectedPin.ToLowerInvariant()));

    /// <summary>
    /// Short code both screens show at first pairing (first 64 bits of the pin). It is a usability
    /// compromise that still needs a threat review before release (spec §5).
    /// </summary>
    public static string DisplayCode(string pin) =>
        string.Join('-', pin[..16].ToUpperInvariant().Chunk(4).Select(group => new string(group)));
}

/// <summary>Temporary, single-use invitation shown by Windows as a QR code. It carries no endpoint.</summary>
public sealed record PairingInvite(string ClientId, string ClientName, string Token, DateTimeOffset ExpiresAt)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private const string Scheme = "ambienttheme";

    public static PairingInvite Create(string clientId, string clientName, DateTimeOffset now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()) + Lifetime;
        return new PairingInvite(clientId, clientName, token, expires);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public string ToUri() =>
        $"{Scheme}://pair?v=1&c={Uri.EscapeDataString(ClientId)}&n={Uri.EscapeDataString(ClientName)}" +
        $"&t={Token}&e={ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string text, out PairingInvite invite)
    {
        invite = new PairingInvite(string.Empty, string.Empty, string.Empty, default);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Scheme || uri.Host != "pair")
        {
            return false;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

        if (query.GetValueOrDefault("v") != "1"
            || !query.TryGetValue("c", out var clientId)
            || !query.TryGetValue("n", out var name)
            || !query.TryGetValue("t", out var token)
            || !long.TryParse(query.GetValueOrDefault("e"), NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
        {
            return false;
        }

        invite = new PairingInvite(clientId, name, token, DateTimeOffset.FromUnixTimeSeconds(expires));
        return true;
    }
}

/// <summary>Why an attempt or session ended. Wording for each is in the spec's error language (§10).</summary>
public enum ConnectionFailure
{
    Refused,
    Timeout,
    Unreachable,
    TlsIdentityMismatch,
    AuthFailed,
    DeviceMismatch,
    ProtocolViolation,
    Closed,
}

public static class ConnectionErrors
{
    /// <summary>Maps a connect exception to a diagnostic without claiming more than the system told us.</summary>
    public static ConnectionFailure Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AuthenticationException:
                    return ConnectionFailure.TlsIdentityMismatch;
                case OperationCanceledException:
                    return ConnectionFailure.Timeout;
                case SocketException socket:
                    return socket.SocketErrorCode switch
                    {
                        SocketError.ConnectionRefused => ConnectionFailure.Refused,
                        SocketError.TimedOut => ConnectionFailure.Timeout,
                        _ => ConnectionFailure.Unreachable,
                    };
            }
        }

        return ConnectionFailure.Unreachable;
    }
}
