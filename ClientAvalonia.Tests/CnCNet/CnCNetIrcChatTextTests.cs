using Avalonia.Media;
using ClientAvalonia.CnCNet;
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
}
