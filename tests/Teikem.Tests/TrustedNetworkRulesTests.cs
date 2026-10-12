using System.Net;
using Teikem.Domain.Security;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-12 — IP preferida: lectura y normalización de IPs y rangos CIDR, coincidencia y rangos prohibidos (docs/ip-preferida-mfa-diseno.md §4).</summary>
public class TrustedNetworkRulesTests
{
    private static TrustedCidr Ok(string text)
    {
        var r = TrustedNetworkRules.Parse(text);
        Assert.Null(r.Error);
        return r.Cidr!;
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10/32")]
    [InlineData("  203.0.113.10 ", "203.0.113.10/32")]
    [InlineData("192.168.10.0/24", "192.168.10.0/24")]
    [InlineData("10.20.0.0/16", "10.20.0.0/16")]
    [InlineData("198.51.100.8/29", "198.51.100.8/29")]
    [InlineData("2001:db8::1", "2001:db8::1/128")]
    [InlineData("2001:db8:1234::/48", "2001:db8:1234::/48")]
    public void Valid_ips_and_ranges_are_normalized(string text, string expected)
    {
        Assert.Equal(expected, Ok(text).Text);
    }

    [Fact]
    public void A_single_ip_is_a_full_prefix_and_a_range_is_not()
    {
        Assert.True(Ok("203.0.113.10").IsSingle);
        Assert.True(Ok("2001:db8::1").IsSingle);
        Assert.False(Ok("192.168.10.0/24").IsSingle);
    }

    [Fact]
    public void Host_bits_are_taken_to_the_network_address_and_it_is_flagged()
    {
        var r = TrustedNetworkRules.Parse("192.168.10.5/24");
        Assert.Null(r.Error);
        Assert.Equal("192.168.10.0/24", r.Cidr!.Text);
        Assert.True(r.WasAdjusted);
        Assert.False(TrustedNetworkRules.Parse("192.168.10.0/24").WasAdjusted);
        Assert.False(TrustedNetworkRules.Parse("203.0.113.10").WasAdjusted);
        // un prefijo que no es múltiplo de 8
        Assert.Equal("198.51.100.0/27", TrustedNetworkRules.Parse("198.51.100.30/27").Cidr!.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_is_required(string? text)
    {
        Assert.Equal(TrustedNetworkRules.CidrRequired, TrustedNetworkRules.Parse(text).Error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("127.1")]            // forma suelta: no se acepta
    [InlineData("203.0.113")]
    [InlineData("203.0.113.256")]
    [InlineData("203.0.113.10.5")]
    [InlineData("1")]
    [InlineData("203.0.113.10/")]
    [InlineData("fe80::1%eth0")]
    [InlineData("203.0.113.10 / 24")]
    public void Loose_or_wrong_forms_are_rejected(string text)
    {
        var r = TrustedNetworkRules.Parse(text);
        Assert.NotNull(r.Error);
        Assert.Null(r.Cidr);
    }

    [Theory]
    [InlineData("203.0.113.10/33")]
    [InlineData("203.0.113.10/-1")]
    [InlineData("203.0.113.10/abc")]
    [InlineData("2001:db8::1/129")]
    public void A_bad_prefix_is_rejected_with_its_own_message(string text)
    {
        Assert.Equal(TrustedNetworkRules.CidrPrefixInvalid, TrustedNetworkRules.Parse(text).Error);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.0/8")]
    [InlineData("10.0.0.0/15")]
    [InlineData("203.0.0.0/1")]
    public void Ranges_that_trust_half_the_internet_are_rejected_for_ipv4(string text)
    {
        Assert.Equal(TrustedNetworkRules.CidrTooWide(false), TrustedNetworkRules.Parse(text).Error);
    }

    [Theory]
    [InlineData("::/0")]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:db8::/47")]
    public void Ranges_that_are_too_wide_are_rejected_for_ipv6(string text)
    {
        var r = TrustedNetworkRules.Parse(text);
        Assert.NotNull(r.Error);
        Assert.Null(r.Cidr);
    }

    [Fact]
    public void The_widest_accepted_ranges_are_slash_16_and_slash_48()
    {
        Assert.Equal("172.16.0.0/16", Ok("172.16.5.5/16").Text);
        Assert.Equal("2001:db8:abcd::/48", Ok("2001:db8:abcd:1::/48").Text);
        Assert.Equal("65536", TrustedNetworkRules.AddressCount(Ok("172.16.0.0/16")));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("ff02::1")]
    public void Unspecified_loopback_and_multicast_are_not_networks(string text)
    {
        Assert.Equal(TrustedNetworkRules.CidrNotRoutable, TrustedNetworkRules.Parse(text).Error);
    }

    [Fact]
    public void A_mapped_ipv6_single_ip_becomes_ipv4_but_with_a_prefix_it_is_refused()
    {
        Assert.Equal("203.0.113.10/32", Ok("::ffff:203.0.113.10").Text);
        Assert.Equal(TrustedNetworkRules.CidrMappedWithPrefix, TrustedNetworkRules.Parse("::ffff:203.0.113.0/120").Error);
    }

    [Fact]
    public void Address_counts_are_the_size_of_the_range()
    {
        Assert.Equal("1", TrustedNetworkRules.AddressCount(Ok("203.0.113.10")));
        Assert.Equal("8", TrustedNetworkRules.AddressCount(Ok("198.51.100.8/29")));
        Assert.Equal("256", TrustedNetworkRules.AddressCount(Ok("192.168.10.0/24")));
        Assert.Equal("1208925819614629174706176", TrustedNetworkRules.AddressCount(Ok("2001:db8:abcd::/48"))); // 2^80
    }

    // ---------------------------------------------------------------- coincidencia

    [Theory]
    [InlineData("192.168.10.0/24", "192.168.10.1", true)]
    [InlineData("192.168.10.0/24", "192.168.10.255", true)]
    [InlineData("192.168.10.0/24", "192.168.11.1", false)]
    [InlineData("198.51.100.8/29", "198.51.100.15", true)]
    [InlineData("198.51.100.8/29", "198.51.100.16", false)]
    [InlineData("198.51.100.8/29", "198.51.100.7", false)]
    [InlineData("203.0.113.10", "203.0.113.10", true)]
    [InlineData("203.0.113.10", "203.0.113.11", false)]
    public void An_ip_matches_when_it_falls_inside_the_range(string range, string ip, bool expected)
    {
        Assert.Equal(expected, TrustedNetworkRules.Contains(Ok(range), TrustedNetworkRules.NormalizeIp(ip)));
    }

    [Fact]
    public void Ipv6_matching_and_the_mapped_form_of_an_ipv4_client()
    {
        Assert.True(TrustedNetworkRules.Contains(Ok("2001:db8:abcd::/48"), TrustedNetworkRules.NormalizeIp("2001:db8:abcd:ffff::1")));
        Assert.False(TrustedNetworkRules.Contains(Ok("2001:db8:abcd::/48"), TrustedNetworkRules.NormalizeIp("2001:db8:abce::1")));
        // el servidor puede ver al cliente IPv4 como ::ffff:a.b.c.d: igual coincide con la entrada IPv4
        Assert.True(TrustedNetworkRules.Contains(Ok("203.0.113.10"), IPAddress.Parse("::ffff:203.0.113.10")));
        Assert.True(TrustedNetworkRules.Contains(Ok("192.168.10.0/24"), IPAddress.Parse("::ffff:192.168.10.77")));
    }

    [Fact]
    public void Different_families_and_missing_ips_never_match()
    {
        Assert.False(TrustedNetworkRules.Contains(Ok("203.0.113.10"), IPAddress.Parse("2001:db8::1")));
        Assert.False(TrustedNetworkRules.Contains(Ok("2001:db8:abcd::/48"), IPAddress.Parse("203.0.113.10")));
        Assert.False(TrustedNetworkRules.Contains(Ok("203.0.113.10"), null));
        Assert.Null(TrustedNetworkRules.NormalizeIp(null));
        Assert.Null(TrustedNetworkRules.NormalizeIp("no es una ip"));
    }
}
