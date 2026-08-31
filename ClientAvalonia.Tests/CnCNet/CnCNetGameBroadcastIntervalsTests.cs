using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class CnCNetGameBroadcastIntervalsTests
{
    [Theory]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(7, 5)]
    [InlineData(12, 10)]
    [InlineData(18, 20)]
    [InlineData(0, 5)]
    [InlineData(99, 30)]
    public void Snap_Maps_Onto_Discrete_Sequence(int input, int expected)
        => CnCNetGameBroadcastIntervals.Snap(input).Should().Be(expected);

    [Fact]
    public void Allowed_Is_Arithmetic_5_Through_30()
    {
        CnCNetGameBroadcastIntervals.AllowedSeconds.Should().Equal(5, 10, 15, 20, 25, 30);
        CnCNetGameBroadcastIntervals.ComboItemsCsv.Should().Be("5,10,15,20,25,30");
        CnCNetGameBroadcastIntervals.DefaultComboIndex.Should().Be(5);
    }
}
