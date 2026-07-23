using System;
using ClientAvalonia.IniUi.Actions;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// <see cref="CmdResult"/> 与 <see cref="ActionKind"/> 的契约测试。
/// 见 docs/design/layered-architecture.md §2.2 / §3.3。
/// </summary>
public sealed class UIActionPrimitivesTests
{
    [Fact]
    public void CmdResult_Ok_NoArg_Has_Success_True()
    {
        CmdResult r = CmdResult.Ok();
        r.Success.Should().BeTrue();
        r.Message.Should().BeNull();
        r.Data.Should().BeNull();
    }

    [Fact]
    public void CmdResult_Ok_With_Message_Keeps_Data_Null()
    {
        CmdResult r = CmdResult.Ok("Done");
        r.Success.Should().BeTrue();
        r.Message.Should().Be("Done");
        r.Data.Should().BeNull();
    }

    [Fact]
    public void CmdResult_Ok_With_Data_Carries_It()
    {
        CmdResult r = CmdResult.Ok("PID", 12345);
        r.Success.Should().BeTrue();
        r.Message.Should().Be("PID");
        r.Data.Should().Be(12345);
    }

    [Fact]
    public void CmdResult_Fail_Has_Success_False()
    {
        CmdResult r = CmdResult.Fail("Network error");
        r.Success.Should().BeFalse();
        r.Message.Should().Be("Network error");
    }

    [Fact]
    public void CmdResult_FromException_Packs_Type_And_Message()
    {
        CmdResult r = CmdResult.FromException(new InvalidOperationException("boom"));
        r.Success.Should().BeFalse();
        r.Message.Should().Contain("InvalidOperationException");
        r.Message.Should().Contain("boom");
    }

    [Fact]
    public void ActionKind_Has_Exactly_Two_Values()
    {
        Enum.GetValues<ActionKind>().Should().BeEquivalentTo(new[] { ActionKind.State, ActionKind.Command });
    }
}

/// <summary>
/// <see cref="UIActionContext"/> struct 构造测试。
/// </summary>
public sealed class UIActionContextTests
{
    [Fact]
    public void Create_Populates_All_Fields_And_Nulls_Args()
    {
        var ctx = UIActionContext.Create(
            args: null,
            source: null,
            session: null,
            services: null,
            host: null);

        ctx.Args.Should().BeEmpty("null args 默认空字符串");
        ctx.Source.Should().BeNull();
        ctx.Session.Should().BeNull();
        ctx.Services.Should().BeNull();
        ctx.Host.Should().BeNull();
    }

    [Fact]
    public void Create_Keeps_Given_Args()
    {
        var ctx = UIActionContext.Create(
            args: "SkirmishLobby",
            source: null,
            session: null,
            services: null,
            host: null);

        ctx.Args.Should().Be("SkirmishLobby");
    }

    [Fact]
    public void Default_Constructor_Leaves_All_Null()
    {
        // struct 默认值：所有引用类型字段为 null，string 字段为 null
        var ctx = default(UIActionContext);
        ctx.Args.Should().BeNull();
        ctx.Session.Should().BeNull();
        ctx.Services.Should().BeNull();
    }
}

/// <summary>
/// 简单 IUIAction 实现样例——用于测试 catalog 注册路径。
/// </summary>
public sealed class DelegateUIAction : IUIAction
{
    private readonly Func<UIActionContext, CmdResult> _impl;
    public DelegateUIAction(string name, ActionKind kind, Func<UIActionContext, CmdResult> impl)
    {
        Name = name;
        Kind = kind;
        _impl = impl;
    }
    public string Name { get; }
    public ActionKind Kind { get; }
    public CmdResult Execute(in UIActionContext context) => _impl(context);
}

/// <summary>
/// IUIAction 派发与异常处理测试。
/// </summary>
public sealed class UIActionDispatchTests
{
    [Fact]
    public void Execute_Returns_Ok_On_Success_Path()
    {
        var action = new DelegateUIAction("Test", ActionKind.State, _ => CmdResult.Ok("worked"));
        var result = action.Execute(default);
        result.Success.Should().BeTrue();
        result.Message.Should().Be("worked");
    }

    [Fact]
    public void Execute_Returns_Fail_When_Handler_Reports_Failure()
    {
        var action = new DelegateUIAction("Test", ActionKind.Command, _ => CmdResult.Fail("nope"));
        var result = action.Execute(default);
        result.Success.Should().BeFalse();
        result.Message.Should().Be("nope");
    }

    [Fact]
    public void Execute_Can_Read_Context_Args()
    {
        var action = new DelegateUIAction("Echo", ActionKind.State,
            ctx => string.IsNullOrEmpty(ctx.Args)
                ? CmdResult.Fail("empty")
                : CmdResult.Ok(ctx.Args));

        var ok = action.Execute(new UIActionContext { Args = "hello" });
        ok.Success.Should().BeTrue();
        ok.Message.Should().Be("hello");

        var fail = action.Execute(default);
        fail.Success.Should().BeFalse();
    }
}
