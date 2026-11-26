using System.Net;

using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity;

[Trait("Category", "Unit")]
public sealed class MaskedIpAddressTests
{
    [Theory]
    [InlineData("192.168.1.25", "192.168.*.*")]
    [InlineData("10.0.0.1", "10.0.*.*")]
    [InlineData("203.0.113.7", "203.0.*.*")]
    public void From_IPv4_KeepsFirstTwoOctets(string address, string expected)
    {
        Assert.Equal(expected, MaskedIpAddress.From(IPAddress.Parse(address)).Value);
    }

    [Theory]
    [InlineData("2001:db8:abcd:12:3456:789a:bcde:f012", "2001:db8:abcd::/48")]
    [InlineData("2001:0db8:0000:0001::1", "2001:db8:0::/48")]
    [InlineData("::1", "0:0:0::/48")]
    public void From_IPv6_KeepsFirstFortyEightBits(string address, string expected)
    {
        Assert.Equal(expected, MaskedIpAddress.From(IPAddress.Parse(address)).Value);
    }

    [Theory]
    [InlineData("::ffff:192.168.1.25", "192.168.*.*")]
    [InlineData("::ffff:10.0.0.1", "10.0.*.*")]
    public void From_IPv4MappedIPv6_MasksAsIPv4(string address, string expected)
    {
        Assert.Equal(expected, MaskedIpAddress.From(IPAddress.Parse(address)).Value);
    }

    [Fact]
    public void From_Null_ReturnsUnknown()
    {
        Assert.Equal("unknown", MaskedIpAddress.From(null).Value);
    }
}