using System;
using ClientAvalonia.GlobalState.Environment;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.GlobalState;

[Collection("EnvironmentServicesSerial")]
public sealed class EnvironmentServicesTests
{
    [Fact]
    public void Resolve_Unregistered_Throws_With_Type_Name()
    {
        EnvironmentServices.Reset();
        // Use a dedicated marker type so parallel suites that register
        // IGameEnvironment cannot interfere with this assertion.
        Action act = () => EnvironmentServices.Resolve<INeverRegisteredMarker>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*INeverRegisteredMarker*")
            .WithMessage("*Register*");
    }

    [Fact]
    public void Register_Then_Resolve_Returns_Instance()
    {
        EnvironmentServices.Reset();
        var mock = new MockGameEnvironment { LocalGameValue = "lnod" };
        EnvironmentServices.Register<IGameEnvironment>(() => mock);

        IGameEnvironment resolved = EnvironmentServices.Resolve<IGameEnvironment>();

        resolved.Should().BeSameAs(mock);
        resolved.LocalGame.Should().Be("lnod");
        EnvironmentServices.Reset();
    }

    [Fact]
    public void Reset_Clears_Registrations()
    {
        EnvironmentServices.Reset();
        EnvironmentServices.Register<IGameEnvironment>(() => new MockGameEnvironment());
        EnvironmentServices.Reset();

        Action act = () => EnvironmentServices.Resolve<IGameEnvironment>();
        act.Should().Throw<InvalidOperationException>();
    }

    // ---- TryResolve (used by ShutdownService A4 fix) ----
    // These tests do NOT call EnvironmentServices.Reset() because doing so
    // during parallel test execution would corrupt state for other test
    // collections. Instead we register / resolve a unique marker type that
    // no other test touches. TryResolve returns null for un-registered
    // services without throwing, so the lack of Reset is fine.

    [Fact]
    public void TryResolve_Unregistered_Returns_Null_NoThrow()
    {
        EnvironmentServices.TryResolve<ITryResolveMarker>()
            .Should().BeNull();
    }

    [Fact]
    public void TryResolve_Registered_Returns_Instance()
    {
        var instance = new TryResolveMarkerImpl();
        EnvironmentServices.Register<ITryResolveMarker>(() => instance);

        EnvironmentServices.TryResolve<ITryResolveMarker>()
            .Should().BeSameAs(instance);
    }

    [Fact]
    public void TryResolve_FactoryThrows_Returns_Null_NoThrow()
    {
        EnvironmentServices.Register<IThrowingMarker>(() =>
            throw new InvalidOperationException("boom"));

        EnvironmentServices.TryResolve<IThrowingMarker>()
            .Should().BeNull();
    }
}

[CollectionDefinition("EnvironmentServicesSerial", DisableParallelization = true)]
public sealed class EnvironmentServicesSerialCollection
{
}

internal interface INeverRegisteredMarker
{
}

// Unique markers used only by TryResolve tests — never registered by other suites.
internal interface ITryResolveMarker
{
}

internal sealed class TryResolveMarkerImpl : ITryResolveMarker
{
}

internal interface IThrowingMarker
{
}
