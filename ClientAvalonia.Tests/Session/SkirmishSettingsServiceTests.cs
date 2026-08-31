using System;
using System.IO;
using System.Linq;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// <see cref="ISkirmishSettingsService"/> 单测——见 layered-architecture-progress-report.md §9.5 Slice 3。
/// 使用绝对路径注入，避免触碰 <c>ProgramConstants</c> 进程级静态。
/// </summary>
public sealed class SkirmishSettingsServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _absPath;

    public SkirmishSettingsServiceTests()
    {
        EnvironmentServices.Reset();
        _tempRoot = Path.Combine(Path.GetTempPath(), "SkirmishSettings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _absPath = Path.Combine(_tempRoot, "Client", "SkirmishSettings.ini");
    }

    public void Dispose()
    {
        EnvironmentServices.Reset();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private ISkirmishSettingsService NewSvc() => new SkirmishSettingsService(_absPath, absolute: true);

    [Fact]
    public void TryLoad_Returns_Null_When_File_Does_Not_Exist()
    {
        var svc = NewSvc();
        svc.TryLoad().Should().BeNull();
    }

    [Fact]
    public void Save_Writes_File_Then_TryLoad_RoundTrips()
    {
        var svc = NewSvc();
        var dto = new SkirmishSettingsDto
        {
            Human = new SkirmishPlayerDto
            {
                Name = "Player1", SideIndex = 1, StartIndex = 2,
                ColorIndex = 3, TeamIndex = 0, AiLevel = 0, IsAi = false, Index = 0,
            },
        };
        dto.Ais.Add(new SkirmishPlayerDto
        {
            Name = "EasyAI", SideIndex = 0, StartIndex = 1,
            ColorIndex = 2, TeamIndex = 1, AiLevel = 1, IsAi = true, Index = 1,
        });

        svc.Save(dto);
        File.Exists(svc.CurrentPath).Should().BeTrue();

        var loaded = svc.TryLoad();
        loaded.Should().NotBeNull();
        loaded!.Human!.Name.Should().Be("Player1");
        loaded.Human.SideIndex.Should().Be(1);
        loaded.Human.IsAi.Should().BeFalse();

        loaded.Ais.Should().HaveCount(1);
        loaded.Ais[0].Name.Should().Be("EasyAI");
        loaded.Ais[0].IsAi.Should().BeTrue();
        loaded.Ais[0].AiLevel.Should().Be(1);
    }

    [Fact]
    public void TryParseLine_Rejects_Too_Few_Fields()
    {
        SkirmishSettingsService.TryParseLine("a,b,c", out var slot).Should().BeFalse();
        slot.Should().BeNull();
    }

    [Fact]
    public void MapSha1_And_GameModeFilter_RoundTrip()
    {
        var svc = NewSvc();
        var dto = new SkirmishSettingsDto
        {
            Human = new SkirmishPlayerDto { Name = "P", Index = 0 },
            MapSha1 = "ABC123DEF456",
            GameModeMapFilter = "常规作战",
        };
        svc.Save(dto);

        var loaded = svc.TryLoad();
        loaded.Should().NotBeNull();
        loaded!.MapSha1.Should().Be("ABC123DEF456");
        loaded.GameModeMapFilter.Should().Be("常规作战");
    }

    [Fact]
    public void Missing_Map_Settings_Default_To_Empty()
    {
        var svc = NewSvc();
        var dto = new SkirmishSettingsDto
        {
            Human = new SkirmishPlayerDto { Name = "P", Index = 0 },
        };
        svc.Save(dto);

        var loaded = svc.TryLoad();
        loaded.Should().NotBeNull();
        loaded!.MapSha1.Should().BeEmpty();
        loaded.GameModeMapFilter.Should().BeEmpty();
    }

    [Fact]
    public void GameOptions_RoundTrip_And_Stale_Keys_Dropped()
    {
        var svc = NewSvc();
        var first = new SkirmishSettingsDto
        {
            Human = new SkirmishPlayerDto { Name = "P", Index = 0 },
        };
        first.GameOptions["chkBrutalAI"] = "False";
        first.GameOptions["cmbCredits"] = "3";
        first.GameOptions["chkShortGame"] = "True";
        svc.Save(first);

        var second = new SkirmishSettingsDto
        {
            Human = new SkirmishPlayerDto { Name = "P", Index = 0 },
        };
        second.GameOptions["chkBrutalAI"] = "True";
        svc.Save(second);

        var loaded = svc.TryLoad();
        loaded.Should().NotBeNull();
        loaded!.GameOptions.Should().ContainKey("chkBrutalAI").WhoseValue.Should().Be("True");
        // Keys absent from the new save must not linger from the previous session.
        loaded.GameOptions.Should().NotContainKey("chkShortGame");
        loaded.GameOptions.Should().NotContainKey("cmbCredits");
    }

    [Fact]
    public void TryParseLine_Rejects_Empty_Name()
    {
        SkirmishSettingsService.TryParseLine(",1,2,3,4,5,true,0", out var slot).Should().BeFalse();
        slot.Should().BeNull();
    }

    [Fact]
    public void TryParseLine_Accepts_Seven_Fields_No_Index()
    {
        bool ok = SkirmishSettingsService.TryParseLine("Bob,1,2,3,4,5,true", out var slot);
        ok.Should().BeTrue();
        slot!.Name.Should().Be("Bob");
        slot.Index.Should().Be(0);
    }

    [Fact]
    public void EnvironmentServices_Resolves_Registered_Instance()
    {
        EnvironmentServices.Register<ISkirmishSettingsService>(() => new SkirmishSettingsService(_absPath, absolute: true));
        var svc = EnvironmentServices.Resolve<ISkirmishSettingsService>();
        svc.CurrentPath.Should().Be(_absPath);
    }
}
