using System;
using System.Reflection;
using System.Threading;
using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Regression test for the shutdown bug: <c>CnCNetSession.Dispose</c> previously did not
/// dispose <c>_gameRoomJoinTimeoutTimer</c>, leaving a stray System.Threading.Timer that
/// could fire after the session is gone. We arm the timer (mimicking a JOIN in flight)
/// then dispose, and confirm the timer reference is cleared.
/// </summary>
public sealed class CnCNetSessionDisposeTests
{
    [Fact]
    public void Dispose_ReleasesGameRoomJoinTimeoutTimer_EvenWhenArmed()
    {
        CnCNetSession session = CnCNetSession.Instance;

        // Arm the join-timeout timer without a real IRC connection: reflection simulates
        // the code path in ArmGameRoomJoinTimeout that runs on a JOIN send.
        ArmJoinTimeoutViaReflection(session);

        // The timer reference must exist before dispose (otherwise the test proves nothing).
        GetTimerField(session).Should().NotBeNull("timer must be armed before dispose");

        session.Dispose();

        GetTimerField(session).Should().BeNull("Dispose must release the join-timeout timer");
    }

    [Fact]
    public void Dispose_TurnsOffAutoReconnect()
    {
        CnCNetSession session = CnCNetSession.Instance;

        SetAutoReconnect(session, true);
        GetAutoReconnect(session).Should().BeTrue();

        session.Dispose();

        GetAutoReconnect(session).Should().BeFalse(
            "after Dispose the session must not silently reconnect on the next server drop");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        CnCNetSession session = CnCNetSession.Instance;
        session.Dispose();

        Action act = () => session.Dispose();

        act.Should().NotThrow(
            "shutdown can fire from multiple sources (Closing + ProcessExit); dispose must be idempotent");
    }

    private static void ArmJoinTimeoutViaReflection(CnCNetSession session)
    {
        MethodInfo? method = typeof(CnCNetSession).GetMethod(
            "ArmGameRoomJoinTimeout",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ArmGameRoomJoinTimeout must exist on CnCNetSession");
        method!.Invoke(session, null);
    }

    private static object? GetTimerField(CnCNetSession session)
    {
        FieldInfo? field = typeof(CnCNetSession).GetField(
            "_gameRoomJoinTimeoutTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("_gameRoomJoinTimeoutTimer field must exist");
        return field!.GetValue(session);
    }

    private static void SetAutoReconnect(CnCNetSession session, bool value)
    {
        FieldInfo? field = typeof(CnCNetSession).GetField(
            "_autoReconnect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("_autoReconnect field must exist");
        field!.SetValue(session, value);
    }

    private static bool GetAutoReconnect(CnCNetSession session)
    {
        FieldInfo? field = typeof(CnCNetSession).GetField(
            "_autoReconnect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (bool)field!.GetValue(session)!;
    }
}
