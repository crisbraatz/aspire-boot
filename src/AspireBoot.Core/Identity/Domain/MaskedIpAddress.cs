using System.Globalization;
using System.Net;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>
/// An IP address reduced to a network prefix so sessions can be shown without storing the full address:
/// IPv4 keeps the first two octets (<c>a.b.*.*</c>), IPv6 keeps the first 48 bits (<c>xxxx:xxxx:xxxx::/48</c>).
/// </summary>
public sealed record MaskedIpAddress
{
    private const string UnknownValue = "unknown";

    private MaskedIpAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static MaskedIpAddress From(IPAddress? address)
    {
        if (address is null)
        {
            return new MaskedIpAddress(UnknownValue);
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? MaskIPv4(address)
            : MaskIPv6(address);
    }

    private static MaskedIpAddress MaskIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{bytes[0]}.{bytes[1]}.*.*");

        return new MaskedIpAddress(value);
    }

    private static MaskedIpAddress MaskIPv6(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        var first = Group(bytes, 0);
        var second = Group(bytes, 2);
        var third = Group(bytes, 4);

        return new MaskedIpAddress($"{first}:{second}:{third}::/48");
    }

    private static string Group(byte[] bytes, int offset) =>
        ((bytes[offset] << 8) | bytes[offset + 1]).ToString("x", CultureInfo.InvariantCulture);
}