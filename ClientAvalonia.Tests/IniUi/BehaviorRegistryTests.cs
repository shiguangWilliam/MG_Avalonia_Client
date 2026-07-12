using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Rendering;
using FluentAssertions;
using Moq;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// BehaviorRegistry is a thin lookup table from control id → IUiBehavior.
/// Tests verify the registry contract (register, register-after chaining, noop fallback, clear)
/// without building a real UiNodeViewModel (that needs a ResourceResolver/LayoutEngine — integration territory).
/// </summary>
public sealed class BehaviorRegistryTests
{
    [Fact]
    public void Resolve_UnregisteredControl_ReturnsNoopBehavior()
    {
        var registry = new BehaviorRegistry();
        var behavior = registry.Resolve("anything-unknown");
        behavior.Should().NotBeNull();
        // Invoking noop must not throw.
        behavior.OnClick(null!);
    }

    [Fact]
    public void Register_OverwritesPreviousBinding()
    {
        var registry = new BehaviorRegistry();
        var first = new Mock<IUiBehavior>();
        var second = new Mock<IUiBehavior>();

        registry.Register("btnTest", first.Object);
        registry.Register("btnTest", second.Object); // overwrite
        registry.Resolve("btnTest").Should().BeSameAs(second.Object);

        first.Verify(b => b.OnClick(It.IsAny<UiNodeViewModel>()), Times.Never);
        registry.Resolve("btnTest").OnClick(null!);
        second.Verify(b => b.OnClick(It.IsAny<UiNodeViewModel>()), Times.Once);
    }

    [Fact]
    public void Register_Action_InvokesDelegateOnClick()
    {
        var registry = new BehaviorRegistry();
        bool invoked = false;
        registry.Register("btnTest", _ => invoked = true);

        registry.Resolve("btnTest").OnClick(null!);
        invoked.Should().BeTrue();
    }

    [Fact]
    public void RegisterAfter_RunsExistingHandlerThenNewHandler()
    {
        var registry = new BehaviorRegistry();
        int callOrder = 0;
        int firstOrder = 0;
        int secondOrder = 0;

        registry.Register("btnTest", _ => firstOrder = ++callOrder);
        registry.RegisterAfter("btnTest", _ => secondOrder = ++callOrder);

        registry.Resolve("btnTest").OnClick(null!);
        firstOrder.Should().Be(1);
        secondOrder.Should().Be(2);
    }

    [Fact]
    public void RegisterAfter_OnUnregisteredControl_ChainsOntoNoop()
    {
        var registry = new BehaviorRegistry();
        bool invoked = false;
        registry.RegisterAfter("newId", _ => invoked = true);

        registry.Resolve("newId").OnClick(null!);
        invoked.Should().BeTrue();
    }

    [Fact]
    public void Clear_RemovesAllBindings()
    {
        var registry = new BehaviorRegistry();
        var behavior = new Mock<IUiBehavior>();
        registry.Register("btnTest", behavior.Object);

        registry.Clear();
        registry.Resolve("btnTest").Should().NotBeSameAs(behavior.Object);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var registry = new BehaviorRegistry();
        var behavior = new Mock<IUiBehavior>();
        registry.Register("BtnTest", behavior.Object);

        registry.Resolve("btnTEST").Should().BeSameAs(behavior.Object);
    }
}
