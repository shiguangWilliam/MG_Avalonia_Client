using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Integration;

/// <summary>
/// Live GameSurge IRC probe for MG / LNOD welcome JOIN channels.
/// Opt-in: set env <c>CNCNET_LIVE_IRC=1</c> (skipped in normal CI).
/// </summary>
[Collection("ProgramConstantsSerial")]
[Trait("Category", "Integration")]
[Trait("Category", "LiveIrc")]
public sealed class CnCNetLiveIrcJoinIntegrationTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public void Dispose()
    {
        ClientConfiguration.ResetInstance();
        _root.Dispose();
    }

    [SkippableFact]
    public void LiveIrc_LnodChannels_JoinSuccessfully()
    {
        RequireLiveIrc();
        WriteLnodWorkspace();
        Bind("AvaLnod" + Random.Shared.Next(1000, 9999));

        ProbeJoin(
            "lnod",
            CnCNetWelcomeChannelPlan.BuildForLocalGame(RequireLocalGame()),
            TimeSpan.FromSeconds(45));
    }

    [SkippableFact]
    public void LiveIrc_MgChannels_JoinSuccessfully()
    {
        RequireLiveIrc();
        WriteMgWorkspace();
        Bind("AvaMg" + Random.Shared.Next(1000, 9999));

        ProbeJoin(
            "mg",
            CnCNetWelcomeChannelPlan.BuildForLocalGame(RequireLocalGame()),
            TimeSpan.FromSeconds(45));
    }

    [SkippableFact]
    public void LiveIrc_BothModChannelSets_JoinInOneSession()
    {
        RequireLiveIrc();
        // Use LNOD LocalGame for USER ident; still JOIN both mod channel sets.
        WriteLnodWorkspace();
        Bind("AvaBoth" + Random.Shared.Next(1000, 9999));

        var steps = new List<CnCNetWelcomeChannelPlan.JoinStep>
        {
            new("#cncnet-lnod", "ra1-derp", "lnod-chat"),
            new("#cncnet-lnod-games", null, "lnod-broadcast"),
            new("#yuanming-games", "ra1-derp", "mg-chat"),
            new("#yuanming-cg-games", null, "mg-broadcast"),
            new("#cncnet", "ra1-derp", "general"),
        };

        ProbeJoin("both", steps, TimeSpan.FromSeconds(60));
    }

    private CnCNetGameEntry RequireLocalGame()
    {
        var collection = new CnCNetGameCollection();
        collection.Initialize();
        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull();
        CnCNetWelcomeChannelPlan.IsLobbyReady(local).Should().BeTrue();
        return local!;
    }

    private void ProbeJoin(
        string label,
        IReadOnlyList<CnCNetWelcomeChannelPlan.JoinStep> steps,
        TimeSpan timeout)
    {
        string systemId = CnCNetIdentity.CreateSystemId();
        using var connection = new CnCNetIrcConnection(systemId);
        using var welcome = new ManualResetEventSlim(false);
        using var failed = new ManualResetEventSlim(false);
        string failReason = string.Empty;
        var log = new List<string>();

        connection.ActivityLogged += msg =>
        {
            lock (log)
                log.Add(msg);
        };
        connection.WelcomeReceived += _ => welcome.Set();
        connection.ConnectionFailed += msg =>
        {
            failReason = msg;
            failed.Set();
        };
        connection.Disconnected += msg =>
        {
            if (!welcome.IsSet)
            {
                failReason = msg;
                failed.Set();
            }
        };
        connection.ChannelJoinFailed += (code, channel, detail) =>
        {
            failReason = $"JOIN failed IRC {code} on {channel}: {detail}";
            failed.Set();
        };

        connection.ConnectAsync();

        int waitMs = (int)timeout.TotalMilliseconds;
        int signaled = WaitHandle.WaitAny([welcome.WaitHandle, failed.WaitHandle], waitMs);
        signaled.Should().NotBe(WaitHandle.WaitTimeout, $"[{label}] timed out waiting for IRC welcome. Log:\n{Dump(log)}");
        failed.IsSet.Should().BeFalse($"[{label}] connect failed before welcome: {failReason}\n{Dump(log)}");
        connection.IsConnected.Should().BeTrue();

        foreach (CnCNetWelcomeChannelPlan.JoinStep step in steps)
        {
            connection.PrepareChannelJoin(step.Channel);
            connection.JoinChannelInstant(step.Channel, step.Key);
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        var missing = new List<string>();
        while (DateTime.UtcNow < deadline)
        {
            missing.Clear();
            foreach (CnCNetWelcomeChannelPlan.JoinStep step in steps)
            {
                if (!connection.IsLocalOnChannel(step.Channel))
                    missing.Add(step.Channel);
            }

            if (missing.Count == 0)
                break;

            Thread.Sleep(250);
        }

        missing.Should().BeEmpty(
            $"[{label}] failed to JOIN: {string.Join(", ", missing)}. Nick={connection.CurrentNick}. Log:\n{Dump(log)}");

        try
        {
            connection.Disconnect();
        }
        catch
        {
            // best-effort
        }
    }

    private static void RequireLiveIrc()
    {
        string? flag = Environment.GetEnvironmentVariable("CNCNET_LIVE_IRC");
        Skip.If(
            !string.Equals(flag, "1", StringComparison.Ordinal)
            && !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase),
            "Set CNCNET_LIVE_IRC=1 to run live GameSurge IRC probes.");
    }

    private void Bind(string nick)
    {
        _root.BindToProgramConstants();
        ClientConfiguration.ResetInstance();
        ProgramConstants.PLAYERNAME = nick;
    }

    private void WriteLnodWorkspace()
    {
        File.WriteAllText(
            _root.ClientDefinitionsPath,
            """
            [Settings]
            LocalGame=lnod
            LongGameName=La Nuova origine del destino
            SettingsFile=RA2MD.ini
            InstallationPathRegKey=ClientAvaloniaLiveIrcProbe
            CnCNetLiveStatusIdentifier=cncnet5_lnod
            MaxNameLength=16
            """);
        File.WriteAllText(
            Path.Combine(_root.ResourcesPath, "GameCollectionConfig.ini"),
            "[CustomGames]\r\n");
    }

    private void WriteMgWorkspace()
    {
        File.WriteAllText(
            _root.ClientDefinitionsPath,
            """
            [Settings]
            LocalGame=MG
            LongGameName=创世之刻
            SettingsFile=RA2MG.ini
            InstallationPathRegKey=ClientAvaloniaLiveIrcProbe
            CnCNetLiveStatusIdentifier=cncnet5_mg
            MaxNameLength=16
            """);
        File.WriteAllText(
            Path.Combine(_root.ResourcesPath, "GameCollectionConfig.ini"),
            """
            [CustomGames]
            0=CustomGame

            [CustomGame]
            InternalName=MG
            UIName=创世之刻
            ChatChannel=#yuanming-games
            GameBroadcastChannel=#yuanming-cg-games
            IconFilename=friendicon.png
            """);
    }

    private static string Dump(List<string> log)
    {
        lock (log)
            return string.Join(Environment.NewLine, log);
    }
}
