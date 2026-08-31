using System.IO;
using ClientAvalonia.GlobalState.Environment;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.GlobalState;

public sealed class MockGameEnvironmentTests
{
    [Fact]
    public void Derived_Paths_Combine_GamePath()
    {
        var env = new MockGameEnvironment
        {
            GamePathValue = @"D:\Games\MG",
        };

        env.ResourcesPath.Should().Be(Path.Combine(@"D:\Games\MG", "Resources"));
        env.BaseResourcesPath.Should().Be(env.ResourcesPath);
    }

    [Fact]
    public void LocalGame_Is_Isolated_Per_Instance()
    {
        var a = new MockGameEnvironment { LocalGameValue = "lnod" };
        var b = new MockGameEnvironment { LocalGameValue = "qec" };

        a.LocalGame.Should().Be("lnod");
        b.LocalGame.Should().Be("qec");
    }
}
