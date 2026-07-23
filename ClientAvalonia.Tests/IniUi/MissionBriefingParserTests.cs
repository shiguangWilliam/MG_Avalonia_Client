using ClientAvalonia.IniUi.Binding;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

public sealed class MissionBriefingParserTests
{
    [Fact]
    public void Parse_Extracts_Location_Objectives_And_Body()
    {
        const string raw =
            """
            同盟军战役 1: 重启
            地点：美国，旧金山

            敌军已占领金门大桥附近的设施。

            任务目标：
            1. 夺回盟军基地
            2. 摧毁苏联雷达站
            3. 保护爱因斯坦
            """;

        MissionBriefingParsed parsed = MissionBriefingParser.Parse(raw);

        parsed.IsStructured.Should().BeTrue();
        parsed.Title.Should().Be("同盟军战役 1: 重启");
        parsed.Location.Should().Be("美国，旧金山");
        parsed.Body.Should().Contain("金门大桥");
        parsed.Objectives.Should().Equal(
            "夺回盟军基地",
            "摧毁苏联雷达站",
            "保护爱因斯坦");
    }

    [Fact]
    public void Parse_Supports_English_Keywords()
    {
        const string raw =
            """
            Allied Mission 2
            Location: San Jose

            Hold the line.

            Objectives:
            1. Defend the base
            2. Destroy the Psychic Beacon
            """;

        MissionBriefingParsed parsed = MissionBriefingParser.Parse(raw);

        parsed.Location.Should().Be("San Jose");
        parsed.Objectives.Should().HaveCount(2);
        parsed.Objectives[0].Should().Be("Defend the base");
    }

    [Fact]
    public void Parse_Empty_Returns_Empty_Result()
    {
        MissionBriefingParsed parsed = MissionBriefingParser.Parse("   ");
        parsed.IsStructured.Should().BeFalse();
        parsed.RawFallback.Should().BeEmpty();
    }
}
