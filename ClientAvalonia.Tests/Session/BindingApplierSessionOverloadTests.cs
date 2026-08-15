using System;
using System.Collections.Generic;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientCore;
using FluentAssertions;
using Moq;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Slice 5: BindingApplier + MultiplayerSlotCoordinator 新增的 Session 重载。
/// 见 layered-architecture-progress-report.md §9.5 Slice 5。
/// </summary>
public sealed class BindingApplierSessionOverloadTests : IDisposable
{
    public BindingApplierSessionOverloadTests()
    {
        EnvironmentServices.Reset();
    }

    public void Dispose() => EnvironmentServices.Reset();

    private static ResourceResolver MakeResources()
    {
        return new ResourceResolver();
    }

    private static UiNodeViewModel MakeRoot()
    {
        var node = new UiNode { Id = "root", ControlType = "XNAControl", TemplateKey = "Control" };
        return new UiNodeViewModel(node, MakeResources(), new BehaviorRegistry(), null);
    }

    [Fact]
    public void ApplyWithSession_NullSession_Throws()
    {
        var root = MakeRoot();
        var catalogs = new LobbyCatalogService();
        var resources = MakeResources();
        var behaviors = new BehaviorRegistry();

        Action act = () => LobbyPlayerBindingApplier.ApplyWithSession(
            root, session: null!, catalogs, resources, behaviors);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyWithSession_NullCatalogs_Throws()
    {
        var root = MakeRoot();
        var session = new SkirmishSession();
        var resources = MakeResources();
        var behaviors = new BehaviorRegistry();

        Action act = () => LobbyPlayerBindingApplier.ApplyWithSession(
            root, session, catalogs: null!, resources, behaviors);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyWithSession_NoPanel_Does_Not_Throw()
    {
        var root = MakeRoot();
        var session = new SkirmishSession();
        var catalogs = new LobbyCatalogService();
        var resources = MakeResources();
        var behaviors = new BehaviorRegistry();

        Action act = () => LobbyPlayerBindingApplier.ApplyWithSession(root, session, catalogs, resources, behaviors);

        act.Should().NotThrow();
    }
}

/// <summary>
/// Slice 5: MultiplayerSlotCoordinator.HandleHostSlotEdit Session 重载。
/// </summary>
public sealed class MultiplayerSlotCoordinatorSessionOverloadTests
{
    private static UiNodeViewModel MakeDropDown()
    {
        var node = new UiNode { Id = "dd", ControlType = "XNADropDown", TemplateKey = "DropDown" };
        return new UiNodeViewModel(
            node,
            new ResourceResolver(),
            new BehaviorRegistry(),
            null);
    }

    [Fact]
    public void HandleHostSlotEdit_SessionOverload_NullSession_Throws()
    {
        var dd = MakeDropDown();
        Action act = () => MultiplayerSlotCoordinator.HandleHostSlotEdit(
            session: null!, slotIndex: 0, previous: null!, ddName: dd,
            allowHostPlayerOptions: true, hostPlayerName: "host",
            aiNames: Array.Empty<string>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleHostSlotEdit_SessionOverload_NullAiNames_Throws()
    {
        var mockSession = new Mock<ICnCNetGameSession>();
        var dd = MakeDropDown();

        Action act = () => MultiplayerSlotCoordinator.HandleHostSlotEdit(
            mockSession.Object, slotIndex: 0, previous: null!, ddName: dd,
            allowHostPlayerOptions: true, hostPlayerName: "host",
            aiNames: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleHostSlotEdit_SessionOverload_DisallowHost_Noops()
    {
        var mockSession = new Mock<ICnCNetGameSession>();
        var dd = MakeDropDown();

        Action act = () => MultiplayerSlotCoordinator.HandleHostSlotEdit(
            mockSession.Object, slotIndex: 0, previous: null!, ddName: dd,
            allowHostPlayerOptions: false, hostPlayerName: "host",
            aiNames: Array.Empty<string>());

        act.Should().NotThrow();
        mockSession.Verify(s => s.BroadcastPlayerOptionsFromSlots(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }
}
