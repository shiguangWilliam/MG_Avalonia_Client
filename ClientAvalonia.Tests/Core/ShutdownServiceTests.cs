using System;
using System.Collections.Generic;
using System.Threading;
using ClientAvalonia.Core;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// Validates <see cref="ShutdownService"/>: idempotency, ordering (CnCNet disposed before
/// lifetime shutdown), and thread-safety under concurrent shutdown triggers
/// (ProcessExit / Closing / btnExit can all fire together on a hard exit).
/// </summary>
public sealed class ShutdownServiceTests
{
    [Fact]
    public void Shutdown_RunsEachStepOnce()
    {
        var trace = new List<string>();
        ShutdownService.ConfigureForTests(
            disposeCnCNet: () => trace.Add("dispose"),
            shutdownLifetime: () => trace.Add("shutdown"));

        ShutdownService.Shutdown("test");
        ShutdownService.Shutdown("test-again");
        ShutdownService.Shutdown(null);

        trace.Should().Equal("dispose", "shutdown");
    }

    [Fact]
    public void Shutdown_DisposesCnCNet_BeforeShuttingDownLifetime()
    {
        var trace = new List<string>();
        ShutdownService.ConfigureForTests(
            disposeCnCNet: () => trace.Add("dispose"),
            shutdownLifetime: () => trace.Add("shutdown"));

        ShutdownService.Shutdown("ordering");

        trace.IndexOf("dispose").Should().BeLessThan(
            trace.IndexOf("shutdown"),
            "CnCNet QUIT must be sent while the UI thread can still flush it.");
    }

    [Fact]
    public void Shutdown_IsIdempotent_AfterReset()
    {
        var calls = 0;
        ShutdownService.ConfigureForTests(
            disposeCnCNet: () => Interlocked.Increment(ref calls),
            shutdownLifetime: () => Interlocked.Increment(ref calls));

        ShutdownService.Shutdown("first");
        ShutdownService.Shutdown("duplicate");

        ShutdownService.ConfigureForTests(
            disposeCnCNet: () => Interlocked.Increment(ref calls),
            shutdownLifetime: () => Interlocked.Increment(ref calls));
        ShutdownService.Shutdown("second-cycle");

        calls.Should().Be(4, "two distinct shutdown cycles each run dispose + shutdown");
        ShutdownService.HasInvoked.Should().BeTrue();
    }

    [Fact]
    public void Shutdown_ConcurrentCallers_RunTeardownExactlyOnce()
    {
        int disposals = 0;
        int shutdowns = 0;
        ShutdownService.ConfigureForTests(
            disposeCnCNet: () => Interlocked.Increment(ref disposals),
            shutdownLifetime: () => Interlocked.Increment(ref shutdowns));

        var threads = new List<Thread>();
        var start = new ManualResetEventSlim(false);
        for (int i = 0; i < 8; i++)
        {
            var t = new Thread(() =>
            {
                start.Wait();
                ShutdownService.Shutdown("concurrent");
            });
            t.Start();
            threads.Add(t);
        }

        start.Set();
        foreach (Thread t in threads)
            t.Join();

        disposals.Should().Be(1);
        shutdowns.Should().Be(1);
    }

    [Fact]
    public void Shutdown_Continues_WhenDisposeThrows()
    {
        // Even if CnCNetDispose blows up, the lifetime must still be shut down so the
        // process doesn't hang on exit. ShutdownService swallows and logs internally;
        // the lifetime shutdown still runs.
        bool lifetimeCalled = false;
        ShutdownService.ConfigureForTests(
            disposeCnCNet: () => throw new InvalidOperationException("boom"),
            shutdownLifetime: () => lifetimeCalled = true);

        ShutdownService.Shutdown("throws");

        lifetimeCalled.Should().BeTrue("lifetime shutdown is independent of CnCNet dispose failures");
    }
}
