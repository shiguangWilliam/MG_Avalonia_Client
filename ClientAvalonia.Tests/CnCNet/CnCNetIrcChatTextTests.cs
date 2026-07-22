using System;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Pins IRC color-prefix parsing to DX <c>CnCNetManager.DoChatMessageReceived</c> behavior.
/// </summary>
public sealed class CnCNetIrcChatTextTests
{
    [Fact]
    public void Parse_StripsColorPrefix_AndResolvesOrange()
    {
        // Orange is catalog index 7 in CnCNetChatColorCatalog (matches settings "Orange").
        string raw = $"\u0003{7:D2}hello world";

        (string text, Color color) = CnCNetIrcChatText.Parse(raw, CnCNetIrcChatText.DefaultChatColor);

        text.Should().Be("hello world");
        color.Should().Be(Colors.Orange);
    }

    [Fact]
    public void Parse_WithoutPrefix_UsesFallback()
    {
        (string text, Color color) = CnCNetIrcChatText.Parse(
            "plain",
            Colors.LimeGreen);

        text.Should().Be("plain");
        color.Should().Be(Colors.LimeGreen);
    }

    [Fact]
    public void Parse_StripsTrailingUnitSeparator()
    {
        string raw = $"\u0003{5:D2}hi\u001f";

        (string text, Color color) = CnCNetIrcChatText.Parse(raw, Colors.White);

        text.Should().Be("hi");
        color.Should().Be(Colors.Red);
    }

    [Fact]
    public void ChatLine_DefaultsTextColorToWhite()
    {
        var line = new CnCNetChatLine { DisplayText = "x" };
        line.TextColor.Should().Be(CnCNetIrcChatText.DefaultChatColor);
    }

    // ---- B2: control / bidi-override character sanitization ----

    [Fact]
    public void SanitizeDisplayText_StripsNullByte()
    {
        CnCNetIrcChatText.SanitizeDisplayText("foo\u0000bar")
            .Should().Be("foobar");
    }

    [Fact]
    public void SanitizeDisplayText_StripsRtlOverride()
    {
        // \u202E is the classic RTL override used for visual deception.
        CnCNetIrcChatText.SanitizeDisplayText("hello\u202Eworld")
            .Should().Be("helloworld");
    }

    [Fact]
    public void SanitizeDisplayText_StripsLtrOverride()
    {
        CnCNetIrcChatText.SanitizeDisplayText("\u202Dhidden")
            .Should().Be("hidden");
    }

    [Fact]
    public void SanitizeDisplayText_StripsZeroWidthCharacters()
    {
        CnCNetIrcChatText.SanitizeDisplayText("a\u200Bb\u200Cc\u200Dd")
            .Should().Be("abcd");
    }

    [Fact]
    public void SanitizeDisplayText_PreservesTabsAndNewlines()
    {
        // Tabs and newlines have legitimate use in chat formatting.
        CnCNetIrcChatText.SanitizeDisplayText("line1\n\tindented")
            .Should().Be("line1\n\tindented");
    }

    [Fact]
    public void SanitizeDisplayText_FastPath_ReturnsOriginalWhenNoControlChars()
    {
        string plain = "Just a normal chat message!";
        // Reference equality verifies the fast path was taken.
        CnCNetIrcChatText.SanitizeDisplayText(plain)
            .Should().BeSameAs(plain);
    }

    [Fact]
    public void SanitizeDisplayText_EmptyInput_ReturnsEmpty()
    {
        CnCNetIrcChatText.SanitizeDisplayText("")
            .Should().BeEmpty();
    }

    [Fact]
    public void SanitizeDisplayText_StripsBellAndEscape()
    {
        // \u0007 (BEL) and \u001B (ESC) are common ANSI / terminal attack vectors.
        CnCNetIrcChatText.SanitizeDisplayText("a\u0007b\u001Bc")
            .Should().Be("abc");
    }

    [Fact]
    public void SanitizeDisplayText_StripsIrcColorControlChar()
    {
        // \u0003 alone (no digit suffix) should be stripped — note this is the
        // raw control char, not part of a valid color prefix.
        CnCNetIrcChatText.SanitizeDisplayText("foo\u0003bar")
            .Should().Be("foobar");
    }

    [Fact]
    public void Parse_StripsEmbeddedControlChars()
    {
        // End-to-end: a malicious message with embedded NUL + RTL override.
        string raw = "hello\u0000\u202Eworld";
        (string text, _) = CnCNetIrcChatText.Parse(raw, Colors.White);
        text.Should().Be("helloworld");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SanitizeDisplayText_StripsSohDelimiters()
    {
        // SOH (\u0001) is the CTCP delimiter — the illegal char on both sides of ACTION.
        CnCNetIrcChatText.SanitizeDisplayText("\u0001ACTION\u0001")
            .Should().Be("ACTION");
    }

    [Theory]
    [InlineData("\u0001ACTION\u0001", "")]                         // DX KABOOOOOOM sample: SOH both sides, empty body
    [InlineData("\u0001ACTION waves\u0001", "waves")]              // normal ACTION with trailing SOH
    [InlineData("\u0001ACTION waves", "waves")]                    // leading SOH only
    [InlineData("ACTION\u0001", "")]                               // trailing SOH only after bare ACTION
    [InlineData("\u0001ACTION  spaced\u0001", " spaced")]          // keep body spacing after the token space
    [Trait("Category", "Security")]
    [Trait("DXContract", "DX-ACTION-SOH")]
    public void TryNormalizeActionCtcp_StripsFlankingIllegalSoh(string raw, string expectedBody)
    {
        // Must never throw — DX crashes on the bare \u0001ACTION\u0001 form.
        bool ok = CnCNetIrcChatText.TryNormalizeActionCtcp(raw, out string body);

        ok.Should().BeTrue();
        body.Should().Be(expectedBody);
        body.Should().NotContain("\u0001");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("DXContract", "DX-ACTION-SOH")]
    public void TryNormalizeActionCtcp_BareActionWithSohBothSides_DoesNotThrow()
    {
        // Exact wire form from the DX client.log crash: PRIVMSG … :\u0001ACTION\u0001
        Action act = () => CnCNetIrcChatText.TryNormalizeActionCtcp("\u0001ACTION\u0001", out _);
        act.Should().NotThrow();
    }

    [Fact]
    public void TryNormalizeActionCtcp_NonActionCtcp_ReturnsFalse()
    {
        CnCNetIrcChatText.TryNormalizeActionCtcp("\u0001GAME R13;...\u0001", out _)
            .Should().BeFalse();
        CnCNetIrcChatText.TryNormalizeActionCtcp("plain chat", out _)
            .Should().BeFalse();
        CnCNetIrcChatText.TryNormalizeActionCtcp("ACTION without soh", out _)
            .Should().BeFalse("plain text must not be treated as CTCP ACTION");
        CnCNetIrcChatText.TryNormalizeActionCtcp(null, out _)
            .Should().BeFalse();
    }

    /// <summary>
    /// Regression: DX Connection.cs uses <c>Contains("ACTION")</c> to decide PRIVMSG→NOTICE
    /// CTCP routing. That false-positives on map/room names containing "FACTION" and would
    /// divert GAME CTCPs off the listing path. Avalonia must use ACTION-prefix matching only.
    /// </summary>
    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Usability")]
    public void TryNormalizeActionCtcp_GameCtcpWithFactionInMapName_IsNotAction()
    {
        string fields = SampleGameMessages.BuildGameMessage(map: "FACTION WAR Arena");
        string wire = "\u0001" + SampleGameMessages.BuildGameCtcp(fields) + "\u0001";

        // The DX Contains("ACTION") bug: "FACTION" contains the substring "ACTION".
        wire.Contains("ACTION", StringComparison.Ordinal).Should().BeTrue(
            "sanity: payload really does contain the FACTION→ACTION substring");

        CnCNetIrcChatText.TryNormalizeActionCtcp(wire, out _)
            .Should().BeFalse(
                "GAME CTCP must not be treated as ACTION just because a field contains FACTION");
    }
}
