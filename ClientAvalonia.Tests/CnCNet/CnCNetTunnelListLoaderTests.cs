using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// CnCNetTunnelListLoader.Parse — parses a master-list body, dedupes by host:port,
/// drops tunnels with RequiresPassword or unsupported Version, caches to disk.
/// Tests feed a synthetic list body (via SampleGameMessages.BuildTunnelListBody).
/// </summary>
/// <remarks>
/// <see cref="ProgramConstants.ClientUserFilesPath"/> is used for the cache write —
/// we bind a TempGameRoot so the cache lands in our throwaway tree, not under the real install.
/// Serial because we mutate <c>ProgramConstants._hostedGamePathOverride</c> (process-wide static).
/// </remarks>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetTunnelListLoaderTests : System.IDisposable
{
    private readonly TempGameRoot _root = new();

    public CnCNetTunnelListLoaderTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Parse_Accepts_VersionTwoTunnels()
    {
        string body = SampleGameMessages.BuildTunnelListBody(
            ("tunnel1.example.com", 50000),
            ("tunnel2.example.com", 50001));

        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: false);

        tunnels.Should().HaveCount(2);
        tunnels[0].Address.Should().Be("tunnel1.example.com");
        tunnels[0].Port.Should().Be(50000);
        tunnels[0].Version.Should().Be(2);
        tunnels[1].Address.Should().Be("tunnel2.example.com");
    }

    [Fact]
    public void Parse_Drops_LegacyVersionTunnels()
    {
        // Version 1 tunnels are not supported — only Version 2 (SupportedTunnelVersion).
        // Manually build a body with a v1 tunnel.
        string body =
            "1\r\n" +
            "old.example.com:50000;OldCountry;OC;OldTunnel;0;0;100;2;0.0;0.0;1;0.0"; // version=1

        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: false);
        tunnels.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Drops_PasswordProtectedTunnels()
    {
        string body =
            "1\r\n" +
            "locked.example.com:50000;Country;CC;LockedTunnel;1;0;100;2;0.0;0.0;2;0.0"; // RequiresPassword=1

        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: false);
        tunnels.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Dedupes_ByHostAndPort()
    {
        // Two lines with the same host:port → only one tunnel survives.
        string body =
            "2\r\n" +
            "dup.example.com:50000;CountryA;CA;TunnelA;0;0;100;2;0.0;0.0;2;0.0\r\n" +
            "dup.example.com:50000;CountryB;CB;TunnelB;0;0;100;2;0.0;0.0;2;0.0";

        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: false);
        tunnels.Should().HaveCount(1);
        tunnels[0].Name.Should().Be("TunnelA", "first occurrence wins");
    }

    [Fact]
    public void Parse_Skips_HeaderLine()
    {
        // The first line is a count header; it has no tunnel data and should not produce a tunnel.
        string body =
            "0\r\n" +
            "tunnel.example.com:50000;Country;CC;Tunnel;0;0;100;2;0.0;0.0;2;0.0";

        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: false);
        // Header is skipped, one valid tunnel remains.
        tunnels.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_CachesToDisk_WhenTunnelsReturned()
    {
        string body = SampleGameMessages.BuildTunnelListBody(("cache.example.com", 50000));

        CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: true);

        string cachePath = Path.Combine(ProgramConstants.ClientUserFilesPath, "tunnel_cache");
        File.Exists(cachePath).Should().BeTrue();
        File.ReadAllBytes(cachePath).Should().Equal(Encoding.Default.GetBytes(body));
    }

    [Fact]
    public void Parse_DoesNotCache_WhenNoTunnelsParsed()
    {
        // Empty body → no tunnels → no cache write.
        string body = "0\r\n";
        CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: true);

        string cachePath = Path.Combine(ProgramConstants.ClientUserFilesPath, "tunnel_cache");
        File.Exists(cachePath).Should().BeFalse("cache should only be written when tunnels were found");
    }

    [Fact]
    public void Parse_Handles_EmptyInput()
    {
        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(System.Array.Empty<byte>(), cacheToDisk: false);
        tunnels.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Handles_MalformedLine_Gracefully()
    {
        // A malformed line (not enough fields) should be skipped, not crash the whole parse.
        string body =
            "2\r\n" +
            "this-is-not-a-valid-line\r\n" +
            "good.example.com:50000;Country;CC;GoodTunnel;0;0;100;2;0.0;0.0;2;0.0";

        IReadOnlyList<CnCNetTunnel> tunnels = CnCNetTunnelListLoader.Parse(Encoding.Default.GetBytes(body), cacheToDisk: false);
        tunnels.Should().HaveCount(1);
        tunnels[0].Address.Should().Be("good.example.com");
    }
}
