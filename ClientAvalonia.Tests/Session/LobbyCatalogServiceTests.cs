using System;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.Services;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// <see cref="ILobbyCatalogService"/> 单测——见 layered-architecture-progress-report.md §9.5 Slice 2。
/// </summary>
public sealed class LobbyCatalogServiceTests : IDisposable
{
    public LobbyCatalogServiceTests()
    {
        EnvironmentServices.Reset();
        ProgramConstants.AI_PLAYER_NAMES.Clear();
        ProgramConstants.TEAMS.Clear();
    }

    public void Dispose() => EnvironmentServices.Reset();

    [Fact]
    public void Reload_Loads_SideNames_From_LobbySideCatalog()
    {
        var sut = new LobbyCatalogService();
        sut.Reload(includeSpectator: true);

        sut.SideNames.Should().NotBeEmpty();
        sut.SideEntries.Should().NotBeEmpty();
        sut.SideNames.Count.Should().Be(sut.SideEntries.Count);
    }

    [Fact]
    public void Reload_Includes_Spectator_When_Requested()
    {
        var sut = new LobbyCatalogService();
        sut.Reload(includeSpectator: true);
        bool withSpectator = sut.SideEntries.Any(s =>
            s.InternalName == LobbySideCatalog.SpectatorInternalName);

        sut.Reload(includeSpectator: false);
        bool withoutSpectator = sut.SideEntries.Any(s =>
            s.InternalName == LobbySideCatalog.SpectatorInternalName);

        withSpectator.Should().BeTrue("阵营表应包含旁观者");
        // 在测试环境下（无 mod 数据）可能两边都是空——验证一致性即可
        withoutSpectator.Should().BeFalse("includeSpectator=false 时不应包含旁观者");
    }

    [Fact]
    public void Reload_Loads_AiNames_And_TeamNames_From_ProgramConstants()
    {
        ProgramConstants.AI_PLAYER_NAMES.Clear();
        ProgramConstants.AI_PLAYER_NAMES.Add("Easy AI");
        ProgramConstants.AI_PLAYER_NAMES.Add("Medium AI");
        ProgramConstants.TEAMS.Clear();
        ProgramConstants.TEAMS.Add("A");
        ProgramConstants.TEAMS.Add("B");

        var sut = new LobbyCatalogService();
        sut.Reload();

        sut.AiNames.Should().Contain(new[] { "Easy AI", "Medium AI" });
        sut.TeamNames.Should().Contain(new[] { "A", "B" });
    }

    [Fact]
    public void EnvironmentServices_Registers_Singleton_Instance()
    {
        EnvironmentServices.Register<ILobbyCatalogService>(() => LobbyCatalogService.Instance);

        var a = EnvironmentServices.Resolve<ILobbyCatalogService>();
        var b = EnvironmentServices.Resolve<ILobbyCatalogService>();

        a.Should().BeSameAs(b);
        a.Should().BeSameAs(LobbyCatalogService.Instance);
    }
}
