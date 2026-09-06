using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.I18N;

/// <summary>
/// Issue #20: the Avalonia-added UI keys introduced in the L10N first batch must
/// exist in en / ru / zh-CN packs. English fallbacks in code are the safety net;
/// these pack entries are what users actually see when a locale is loaded.
/// </summary>
public sealed class Issue20L10nKeyCoverageTests
{
    private static readonly string[] Issue20Keys =
    [
        "Client:Main:FileVerifyFailed",
        "Client:Main:StepDisplayOptions",
        "Client:Main:StepAudioOptions",
        "Client:Main:StepUpdaterOptions",
        "Client:Main:RendererConfigError",
        "Client:Main:PartialSaveFailed",
        "Client:Main:LoadingTacticalUI",
        "Client:Main:StyleSwitchReverted",
        "Client:Main:StyleReloadFailed",
        "Client:Main:StatusConnectedShort",
        "Client:Main:PmNeedsConnection",
        "Client:Main:PrivateMessagesTitle",
        "Client:Main:PmStatusPreview",
        "Client:Main:WafSurfacePrivateMessage",
        "Client:Main:WafSurfaceLobbyChat",
        "Client:Main:WafSurfaceGameRoomChat",
        "Client:Main:WafSurfaceListingText",
        "Client:Main:WafSurfaceProtocol",
        "Client:Main:WafUnknownSource",
        "Client:Main:WafSourceLabel",
        "Client:Main:WafSurfaceLabel",
        "Client:Main:WafLevelLabel",
        "Client:Main:WafReasonLabel",
        "Client:Main:WafAlertTitle",
        "Client:Main:WafBlocklistSaved",
        "Client:Main:ButtonExit",
        "Client:Main:ButtonNewCampaign",
        "Client:Main:TacticalMapEditor",
        "Client:Main:OnlinePlayers",
        "Client:Main:ChatTextColor",
        "Client:Main:CampaignLobby",
        "Client:Main:ButtonGotIt",
        "Client:Main:AddToBlocklist",
        "Client:Main:ButtonClose",
        "Client:Main:WafStrategiesTitle",
        "Client:Main:WafModeOff",
        "Client:Main:WafStrategyIdNone",
        "Client:Main:WafStrategyContentNone",
        "Client:Main:WafStrategyIdFmt",
        "Client:Main:WafStrategyContentFmt",
        "Client:Main:WafStrategiesHint",
        "Client:Main:WafEnableStatus",
        "INI:Controls:Global:btnTabDisplay:Text",
        "INI:Controls:Global:btnTabAudio:Text",
        "INI:Controls:Global:btnTabGame:Text",
        "INI:Controls:Global:btnTabSecurity:Text",
        "INI:Controls:Global:btnTabUpdater:Text",
        "INI:Controls:Global:btnTabComponents:Text",
    ];

    public static IEnumerable<object[]> Locales()
    {
        yield return ["en"];
        yield return ["ru"];
        yield return ["zh-CN"];
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Issue20_Keys_Present_In_Locale_Pack(string locale)
    {
        string? path = ResolveTranslationIni(locale);
        Skip.If(path is null, $"Translation.ini for {locale} not found under Packaging/CompiledAvalonia.");

        HashSet<string> keys = LoadKeys(path!);
        IEnumerable<string> missing = Issue20Keys.Where(k => !keys.Contains(k));
        missing.Should().BeEmpty(
            $"{locale} Translation.ini is missing Issue #20 keys: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Issue20_Keys_Have_NonEmpty_Values_In_ZhCn()
    {
        string? path = ResolveTranslationIni("zh-CN");
        Skip.If(path is null, "zh-CN Translation.ini not found.");

        Dictionary<string, string> values = LoadKeyValues(path!);
        foreach (string key in Issue20Keys)
        {
            values.Should().ContainKey(key);
            values[key].Should().NotBeNullOrWhiteSpace(because: $"{key} must have a zh-CN value");
        }
    }

    private static string? ResolveTranslationIni(string locale)
    {
        string relative = Path.Combine(
            "Packaging", "MG-Avalonia", "Resources", "Translations", locale, "Translation.ini");

        // Prefer Packaging (source of truth for MG Avalonia packs). Walk parents
        // from the test binary so we never accidentally pick CompiledAvalonia /
        // DXMainClient snapshots that lag Packaging.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback for CI images that only ship CompiledAvalonia resources.
        string repoRoot = FindRepoRoot();
        string[] fallbacks =
        [
            Path.Combine(repoRoot, "CompiledAvalonia", "Resources", "Translations", locale, "Translation.ini"),
            Path.Combine(repoRoot, "DXMainClient", "Resources", "Translations", locale, "Translation.ini"),
        ];
        return fallbacks.FirstOrDefault(File.Exists);
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "ClientAvalonia.sln"))
                || File.Exists(Path.Combine(dir, "DXClient.slnx"))
                || Directory.Exists(Path.Combine(dir, "ClientAvalonia")))
            {
                return dir;
            }

            string? parent = Directory.GetParent(dir)?.FullName;
            if (parent is null)
                break;
            dir = parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static HashSet<string> LoadKeys(string path)
        => LoadKeyValues(path).Keys.ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> LoadKeyValues(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inValues = false;
        foreach (string raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;
            if (line.StartsWith('['))
            {
                int close = line.IndexOf(']');
                if (close > 1)
                {
                    string section = line[1..close];
                    inValues = section.Equals("Values", StringComparison.OrdinalIgnoreCase);
                }
                continue;
            }

            if (!inValues)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            map[key] = value;
        }

        return map;
    }
}
