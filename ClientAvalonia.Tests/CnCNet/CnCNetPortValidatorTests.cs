using System.Collections.Generic;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// DX bare int.Parse vs MG explicit range. Tests are tagged so the divergence is documented.
/// DX CnCNetTunnel.Parse uses Convert.ToInt32/int.Parse with NO range guard;
/// Avalonia CnCNetPortValidator adds explicit 1..65535 (MG-Extension).
/// </summary>
public sealed class CnCNetPortValidatorTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("80", 80)]
    [InlineData("443", 443)]
    [InlineData("65535", 65535)]
    [Trait("DXContract", "DX-PORT-RANGE")]
    public void TryParse_Accepts_DxStandardRange_1To65535(string text, int expected)
    {
        CnCNetPortValidator.TryParse(text, out ushort port).Should().BeTrue();
        port.Should().Be((ushort)expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [Trait("Baseline", "MG-Extension")]
    public void TryParse_Rejects_OutsideRange(string text)
    {
        // DX would accept these via bare int.Parse; Avalonia rejects as MG hardening.
        // Note: "-1" is NOT in this list — it gets recovered to ushort 65535 via the
        // signed-int16 tunnel-port path (see TryParseTunnelPortToken_RecoversSignedInt16_*).
        CnCNetPortValidator.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Baseline", "MG-Extension")]
    public void TryParse_NegativeSmallInt_RecoversToUnsigned_IntoAcceptedRange()
    {
        // -1 → ushort 65535 via the signed-int16 recovery path; treated as a valid port.
        // This mirrors what some CnCNet tunnel servers return for high NAT ports.
        bool ok = CnCNetPortValidator.TryParse("-1", out ushort port);
        ok.Should().BeTrue();
        port.Should().Be(65535);
    }

    [Fact]
    public void TryParse_Rejects_GarbageText()
    {
        CnCNetPortValidator.TryParse("", out _).Should().BeFalse();
        CnCNetPortValidator.TryParse("   ", out _).Should().BeFalse();
        CnCNetPortValidator.TryParse("abc", out _).Should().BeFalse();
        CnCNetPortValidator.TryParse("65536", out _).Should().BeFalse(); // over ushort range
    }

    [Fact]
    [Trait("Baseline", "MG-Extension")]
    public void TryParseTunnelPortToken_RecoversSignedInt16_Negative26016_Becomes39520()
    {
        // Some tunnel servers emit ports > 32767 as signed-int16 text (e.g. NAT 39520 → "-26016").
        // DX passes through verbatim; Avalonia recovers via ushort cast.
        bool ok = CnCNetPortValidator.TryParseTunnelPortToken("-26016", out ushort port, out string? note);
        ok.Should().BeTrue();
        port.Should().Be(39520);
        note.Should().NotBeNull("recovered-from-signed path should produce a diagnostic note");
    }

    [Fact]
    public void TryParseTunnelPortToken_PlainPort_NoRecoveryNote()
    {
        bool ok = CnCNetPortValidator.TryParseTunnelPortToken("12345", out ushort port, out string? note);
        ok.Should().BeTrue();
        port.Should().Be(12345);
        note.Should().BeNull();
    }

    [Fact]
    [Trait("DXContract", "DX-PORT-RANGE")]
    public void TryParseEndpoint_SplitsLastColon_Ipv6Friendly()
    {
        bool ok = CnCNetPortValidator.TryParseEndpoint("tunnel.example.com:50000", out string host, out ushort port);
        ok.Should().BeTrue();
        host.Should().Be("tunnel.example.com");
        port.Should().Be(50000);
    }

    [Fact]
    public void TryParseEndpoint_UsesLastColon_ForMultiColonAddresses()
    {
        // IPv6-ish: last colon wins. DX/Avalonia both use LastIndexOf(':').
        bool ok = CnCNetPortValidator.TryParseEndpoint("[::1]:1234", out string host, out ushort port);
        ok.Should().BeTrue();
        host.Should().Be("[::1]");
        port.Should().Be(1234);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-colon")]
    [InlineData(":1234")]      // empty host
    [InlineData("host:")]      // empty port
    [InlineData("host:abc")]
    public void TryParseEndpoint_Rejects_MalformedEndpoints(string endpoint)
    {
        CnCNetPortValidator.TryParseEndpoint(endpoint, out _, out _).Should().BeFalse();
    }

    [Fact]
    [Trait("DXContract", "DX-PORT-RANGE")]
    public void TryValidatePlayerPorts_ReturnsTunnelError_WhenCountMismatch()
    {
        // DX error string (GetPlayerPortInfo) when tunnel returns fewer ports than player count.
        var ports = new List<ushort> { 1000, 2000 };
        bool ok = CnCNetPortValidator.TryValidatePlayerPorts(ports, expectedCount: 4, out string? error);
        ok.Should().BeFalse();
        error.Should().Be(DxAliases.TunnelPortCountError);
    }

    [Fact]
    public void TryValidatePlayerPorts_ReturnsOk_WhenAllInRange()
    {
        var ports = new List<ushort> { 1000, 2000, 3000, 4000 };
        CnCNetPortValidator.TryValidatePlayerPorts(ports, expectedCount: 4, out string? error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Baseline", "MG-Extension")]
    public void TryValidatePlayerPorts_RejectsZeroPort_AfterCountMatches()
    {
        // Port 0 fails IsValid (MG explicit range). DX bare int.Parse would accept it.
        var ports = new List<ushort> { 1000, 0, 3000, 4000 };
        bool ok = CnCNetPortValidator.TryValidatePlayerPorts(ports, expectedCount: 4, out string? error);
        ok.Should().BeFalse();
        error.Should().Contain("invalid player port");
    }

    [Fact]
    public void IsValid_BoundaryPorts()
    {
        CnCNetPortValidator.IsValid(0).Should().BeFalse();
        CnCNetPortValidator.IsValid(1).Should().BeTrue();
        CnCNetPortValidator.IsValid(65535).Should().BeTrue();
    }
}
