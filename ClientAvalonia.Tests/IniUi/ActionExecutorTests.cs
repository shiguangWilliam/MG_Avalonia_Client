using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Actions.Lobby;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Unit tests for the Action / Executor infrastructure (auto-refresh-design.md v2).
/// </summary>
[Collection("EnvironmentServicesSerial")]
public sealed class ActionExecutorTests
{
    [Fact]
    public void Execute_Invokes_All_Refresh_Steps()
    {
        int callCount = 0;
        LobbyActionContext ctx = NewContext();
        var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(ctx, new Action<LobbyActionContext>[]
        {
            _ => callCount++,
            _ => callCount++,
            _ => callCount++,
        });

        executor.Execute(new SetPlayerColorAction(slotIndex: 0, colorIndex: 3));

        callCount.Should().Be(3);
    }

    [Fact]
    public void Execute_Swallows_Refresh_Step_Exceptions()
    {
        bool secondRan = false;
        LobbyActionContext ctx = NewContext();
        var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(ctx, new Action<LobbyActionContext>[]
        {
            _ => throw new InvalidOperationException("step 1 blew up"),
            _ => secondRan = true,
        });

        Action act = () => executor.Execute(new SetPlayerColorAction(0, 1));

        act.Should().NotThrow();
        secondRan.Should().BeTrue();
    }

    [Fact]
    public void Execute_Without_Refresh_Does_Not_Invoke_Steps()
    {
        int callCount = 0;
        LobbyActionContext ctx = NewContext();
        var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(ctx, new Action<LobbyActionContext>[]
        {
            _ => callCount++,
        });

        executor.ExecuteWithoutRefresh(new SetPlayerColorAction(0, 5));

        callCount.Should().Be(0);
        ctx.Game.PlayerSlots[0].ColorIndex.Should().Be(5);
    }

    [Fact]
    public void SetPlayerColorAction_Updates_StateColorIndex()
    {
        LobbyActionContext ctx = NewContext();
        var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(ctx, NoRefresh);

        executor.ExecuteWithoutRefresh(new SetPlayerColorAction(slotIndex: 1, colorIndex: 3));

        ctx.Game.PlayerSlots[1].ColorIndex.Should().Be(3);
    }

    [Fact]
    public void SetPlayerColorAction_Undo_RestoresPrevious()
    {
        LobbyActionContext ctx = NewContext();
        ctx.Game.PlayerSlots[1].ColorIndex = 5;
        var action = new SetPlayerColorAction(slotIndex: 1, colorIndex: 3);

        action.Execute(ctx);
        ctx.Game.PlayerSlots[1].ColorIndex.Should().Be(3);

        action.Undo(ctx);
        ctx.Game.PlayerSlots[1].ColorIndex.Should().Be(5);
    }

    [Fact]
    public void SetPlayerColorAction_OutOfRangeSlot_Is_Noop()
    {
        LobbyActionContext ctx = NewContext();
        var action = new SetPlayerColorAction(slotIndex: 99, colorIndex: 7);

        Action act = () => action.Execute(ctx);
        act.Should().NotThrow();
    }

    [Fact]
    public void ChangeMapAction_Refills_Ai_For_Skirmish()
    {
        LobbyActionContext ctx = NewContext(windowName: "SkirmishLobby");
        var map = NewMap(maxPlayers: 4);
        ctx.Game.PlayerSlots[1].ColorIndex = 5;

        var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(ctx, NoRefresh);
        executor.ExecuteWithoutRefresh(new ChangeMapAction(map));

        if (ctx.Game is SkirmishSession skirmish)
            skirmish.OccupiedSlotCount().Should().Be(4);
        ctx.Game.PlayerSlots[1].ColorIndex.Should().NotBe(5);
        ctx.Game.Map.Should().BeSameAs(map);
    }

    [Fact]
    public void ChangeMapAction_Does_Not_Refill_For_CnCNetLobby()
    {
        LobbyActionContext ctx = NewContext(windowName: "CnCNetGameLobby");
        var map = NewMap(maxPlayers: 2);
        ctx.Game.PlayerSlots[0].Name = "host";
        ctx.Game.PlayerSlots[1].Name = "guest";

        var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(ctx, NoRefresh);
        executor.ExecuteWithoutRefresh(new ChangeMapAction(map));

        ctx.Game.PlayerSlots[0].Name.Should().Be("host");
        ctx.Game.PlayerSlots[1].Name.Should().Be("guest");
        ctx.Game.PlayerSlots[2].IsOccupied.Should().BeFalse();
    }

    private static IReadOnlyList<Action<LobbyActionContext>> NoRefresh => Array.Empty<Action<LobbyActionContext>>();

    private static MapEntry NewMap(int maxPlayers) => new()
    {
        BaseFilePath = $"map{maxPlayers}.map",
        DisplayName = $"Test {maxPlayers}p",
        UntranslatedName = $"Test {maxPlayers}p",
        GameModes = new[] { "Standard" },
        MaxPlayers = maxPlayers,
    };

    private static LobbyActionContext NewContext(string windowName = "SkirmishLobby")
    {
        EnvironmentServices.Reset();
        EnvironmentServices.Register<IMultiplayerColorCatalog>(
            () => new FixedColorCatalog());
        EnvironmentServices.Register<IGameEnvironment>(
            () => new MockGameEnvironment { PlayerNameValue = "TestPlayer" });

        LobbyCatalogService.Instance.Reload(includeSpectator: false);

        return new LobbyActionContext
        {
            Root = NewRoot(),
            Behaviors = new BehaviorRegistry(),
            WindowName = windowName,
            Game = new SkirmishSession(),
            Session = new LobbySessionState(),
            Resources = new GameResourceCatalogAdapter(GameResourceCatalog.Instance),
            ResourceResolver = new ResourceResolver(primaryRoot: "."),
        };
    }

    private static UiNodeViewModel NewRoot()
    {
        var node = new UiNode
        {
            Id = "TestRoot",
            ControlType = "XNAPanel",
            TemplateKey = "DxPanel",
        };
        var resources = new ResourceResolver(primaryRoot: ".");
        var behaviors = new BehaviorRegistry();
        return new UiNodeViewModel(node, resources, behaviors);
    }

    private sealed class FixedColorCatalog : IMultiplayerColorCatalog
    {
        public IReadOnlyList<MultiplayerColorCatalog.MultiplayerColorEntry> Load()
            => Enumerable.Range(0, 8)
                .Select(i => new MultiplayerColorCatalog.MultiplayerColorEntry
                {
                    Name = $"C{i}",
                    GameColorIndex = i,
                    R = 1,
                    G = 2,
                    B = 3,
                })
                .ToList();
    }
}
