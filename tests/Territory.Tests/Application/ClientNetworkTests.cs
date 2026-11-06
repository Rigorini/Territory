using System.Net;
using Territory.Application.Security;

namespace Territory.Tests.Application;

public sealed class ClientNetworkTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1234:5678:aaaa:bbbb:cccc:dddd", "2001:db8:1234:5678::/64")]
    [InlineData("2001:db8:1234:5678::1", "2001:db8:1234:5678::/64")]
    [InlineData("::1", "::/64")]
    public void KeyOf_KeepsIPv4AndReducesIPv6ToItsPrefix(string address, string expected) =>
        Assert.Equal(expected, ClientNetwork.KeyOf(IPAddress.Parse(address)));

    [Fact]
    public void KeyOf_SameIPv6Prefix_GivesTheSameKey() =>
        Assert.Equal(
            ClientNetwork.KeyOf(IPAddress.Parse("2001:db8:0:1::a")),
            ClientNetwork.KeyOf(IPAddress.Parse("2001:db8:0:1:ffff:ffff:ffff:ffff")));

    [Fact]
    public void KeyOf_UnknownAddress_IsUnknown() => Assert.Equal(ClientNetwork.Unknown, ClientNetwork.KeyOf(null));

    [Fact]
    public void KeyOf_FitsTheColumn() =>
        Assert.True(ClientNetwork.KeyOf(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")).Length
            <= Territory.Application.Domain.LoginLockout.ClientKeyMaxLength);
}
