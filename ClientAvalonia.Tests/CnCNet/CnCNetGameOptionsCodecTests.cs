using System;
using System.Collections.Generic;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// GO CTCP body encode/decode (DX <c>CnCNetGameLobby</c> game options message).
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetGameOptionsCodecTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public CnCNetGameOptionsCodecTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void BuildAndParse_Roundtrips_CheckBoxesAndDropDowns()
    {
        var original = new CnCNetGameOptionsState
        {
            CheckBoxValues = [true, false, true, true],
            DropDownIndices = [0, 2, 1],
            MapOfficial = true,
            MapSha1 = "ABCDEF0123456789",
            GameModeName = "Standard",
            FrameSendRate = 7,
            MaxAhead = 11,
            ProtocolVersion = 2,
            RandomSeed = 424242,
            RemoveStartingLocations = true,
            MapUntranslatedName = "Cool Map",
        };

        string body = CnCNetGameOptionsCodec.BuildBody(original, checkBoxCount: 4, dropDownCount: 3);

        bool ok = CnCNetGameOptionsCodec.TryParseBody(
            body, 4, 3, out CnCNetGameOptionsState? parsed, out string? error);

        ok.Should().BeTrue(error);
        parsed.Should().NotBeNull();
        parsed!.CheckBoxValues.Should().Equal(true, false, true, true);
        parsed.DropDownIndices.Should().Equal(0, 2, 1);
        parsed.MapOfficial.Should().BeTrue();
        parsed.MapSha1.Should().Be("ABCDEF0123456789");
        parsed.GameModeName.Should().Be("Standard");
        parsed.FrameSendRate.Should().Be(7);
        parsed.MaxAhead.Should().Be(11);
        parsed.ProtocolVersion.Should().Be(2);
        parsed.RandomSeed.Should().Be(424242);
        parsed.RemoveStartingLocations.Should().BeTrue();
        parsed.MapUntranslatedName.Should().Be("Cool Map");
    }

    [Fact]
    public void BuildBody_PadsMissingControlValues_WithZeros()
    {
        var sparse = new CnCNetGameOptionsState
        {
            CheckBoxValues = [true],
            DropDownIndices = [2],
            MapOfficial = false,
            MapSha1 = "HASH",
            GameModeName = "Mode",
            FrameSendRate = 3,
            MaxAhead = 5,
            ProtocolVersion = 1,
            RandomSeed = 9,
            RemoveStartingLocations = false,
            MapUntranslatedName = "Map",
        };

        string body = CnCNetGameOptionsCodec.BuildBody(sparse, checkBoxCount: 3, dropDownCount: 2);

        CnCNetGameOptionsCodec.TryParseBody(body, 3, 2, out CnCNetGameOptionsState? parsed, out _)
            .Should().BeTrue();
        parsed!.CheckBoxValues.Should().Equal(true, false, false);
        parsed.DropDownIndices.Should().Equal(2, 0);
    }

    [Fact]
    public void BuildBody_Overload_UsesStateListLengths()
    {
        var state = SampleState(checkBoxes: [false, true], dropDowns: [1]);
        string a = CnCNetGameOptionsCodec.BuildBody(state);
        string b = CnCNetGameOptionsCodec.BuildBody(state, 2, 1);
        a.Should().Be(b);
    }

    [Fact]
    public void TryParseBody_Rejects_TooShortMessage()
    {
        bool ok = CnCNetGameOptionsCodec.TryParseBody(
            "1;0;1",
            checkBoxCount: 2,
            dropDownCount: 1,
            out CnCNetGameOptionsState? state,
            out string? error);

        ok.Should().BeFalse();
        state.Should().BeNull();
        error.Should().Contain("invalid game options message length");
    }

    [Fact]
    public void TryParseBody_Rejects_NonNumericDropDown()
    {
        var state = SampleState(checkBoxes: [true, false], dropDowns: [0]);
        string body = CnCNetGameOptionsCodec.BuildBody(state, 2, 1);
        // Corrupt the dropdown token (first ';' after checkbox int(s)).
        string[] parts = body.Split(';');
        parts[1] = "xx";
        string corrupt = string.Join(';', parts);

        bool ok = CnCNetGameOptionsCodec.TryParseBody(corrupt, 2, 1, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("dropdown");
    }

    [Fact]
    public void TryParseBody_ManyCheckBoxes_UsesMultiplePackedInts()
    {
        // 33 checkboxes → 2 packed ints (checkBoxCount/32 + 1).
        var values = new bool[33];
        values[0] = true;
        values[31] = true;
        values[32] = true;

        var state = SampleState(values, [4]);
        string body = CnCNetGameOptionsCodec.BuildBody(state, 33, 1);

        CnCNetGameOptionsCodec.TryParseBody(body, 33, 1, out CnCNetGameOptionsState? parsed, out string? error)
            .Should().BeTrue(error);
        parsed!.CheckBoxValues.Should().HaveCount(33);
        parsed.CheckBoxValues[0].Should().BeTrue();
        parsed.CheckBoxValues[31].Should().BeTrue();
        parsed.CheckBoxValues[32].Should().BeTrue();
        parsed.DropDownIndices.Should().Equal(4);
    }

    private static CnCNetGameOptionsState SampleState(
        IReadOnlyList<bool>? checkBoxes = null,
        IReadOnlyList<int>? dropDowns = null)
        => new()
        {
            CheckBoxValues = checkBoxes ?? [true, false],
            DropDownIndices = dropDowns ?? [1],
            MapOfficial = true,
            MapSha1 = "SHA1",
            GameModeName = "Standard",
            FrameSendRate = ClientConfiguration.Instance.DefaultFrameSendRate,
            MaxAhead = ClientConfiguration.Instance.DefaultMaxAhead,
            ProtocolVersion = ClientConfiguration.Instance.DefaultProtocolVersion,
            RandomSeed = 12345,
            RemoveStartingLocations = false,
            MapUntranslatedName = "Sample",
        };
}
