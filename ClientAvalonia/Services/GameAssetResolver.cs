using Avalonia.Media.Imaging;
using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Themes;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Resolves game-relative assets (side icons, map previews, standard buttons) via search paths.</summary>
public static class GameAssetResolver
{
    private static readonly string[] NoPreviewCandidates = ["nopreview.png", "noMapPreview.png"];

    public static IReadOnlyList<string> NoPreviewFallbackNames => NoPreviewCandidates;

    private static readonly int[] StandardButtonWidths = [147, 160, 133, 142, 121, 110, 97, 92, 75];

    private static readonly string[] MissionPreviewPrefixes =
    [
        "Maps/Cooperative",
        "Maps/Campaign",
        "Maps",
        "Previews",
    ];

    /// <summary>XNA pattern: SideName from Battle.ini + "icon.png".</summary>
    public static string SideIconFileName(string sideName)
        => string.IsNullOrWhiteSpace(sideName) ? string.Empty : $"{sideName}icon.png";

    /// <summary>INI SideName override, else campaign button id mapping.</summary>
    public static string? ResolveSideName(UiNodeViewModel vm)
    {
        string? fromIni = vm.GetIniString("SideName");
        if (!string.IsNullOrWhiteSpace(fromIni))
            return fromIni;

        return SideNameForCampaignButtonId(vm.Id);
    }

    /// <summary>MG CampaignSelector side buttons map to Battle.ini SideName values.</summary>
    public static string? SideNameForCampaignButtonId(string controlId)
        => controlId.ToLowerInvariant() switch
        {
            "gdi" => "Allied",
            "nod" => "Soviet",
            "thirdside" => "Ackville",
            _ => null,
        };

    public static IEnumerable<string> ResolveNoPreviewCandidates(UiNodeViewModel? host)
    {
        var candidates = new List<string>();
        AddTextureCandidate(candidates, host, "NoPreviewTexture", "PreviewFallbackTexture");
        candidates.AddRange(NoPreviewCandidates);
        return candidates;
    }

    public static string ResolveMapPreviewRelativePath(string mapBasePath, string? previewImageFileName = null)
    {
        if (string.IsNullOrWhiteSpace(mapBasePath))
            return string.Empty;

        string baseName = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(previewImageFileName)
                ? mapBasePath
                : previewImageFileName);

        string normalizedMap = mapBasePath.Replace('\\', '/').Trim('/');
        string mapDir = Path.GetDirectoryName(normalizedMap)?.Replace('\\', '/') ?? string.Empty;
        string parentDir = string.IsNullOrEmpty(mapDir)
            ? string.Empty
            : Path.GetDirectoryName(mapDir)?.Replace('\\', '/') ?? string.Empty;

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(parentDir))
            candidates.Add($"{parentDir}/{baseName}.png");
        if (!string.IsNullOrEmpty(mapDir))
            candidates.Add($"{mapDir}/{baseName}.png");
        candidates.Add($"{normalizedMap}.png");
        candidates.Add($"Previews/{baseName}.png");
        candidates.Add($"{baseName}.png");

        foreach (string relative in candidates)
        {
            if (GameFileExists(relative))
                return relative.Replace('\\', '/');
        }

        return string.Empty;
    }

    public static Bitmap? LoadSideIcon(ResourceResolver resources, string? sideName, UiNodeViewModel? host = null)
    {
        Bitmap? fromIni = LoadConfiguredTexture(resources, host, "SideIconTexture", "IconTexture");
        if (fromIni != null)
            return fromIni;

        if (string.IsNullOrWhiteSpace(sideName))
            return null;

        var candidates = new List<string>
        {
            SideIconFileName(sideName),
            $"{sideName.ToLowerInvariant()}icon.png",
        };

        return resources.LoadFirstBitmap(candidates);
    }

    public static Bitmap? LoadMapPreview(
        ResourceResolver resources,
        string? mapBasePath,
        string? storedPreviewPath = null,
        UiNodeViewModel? host = null)
    {
        var candidates = new List<string>();
        AddTextureCandidate(candidates, host, "PreviewTexture", "PreviewImageTexture");

        if (!string.IsNullOrWhiteSpace(storedPreviewPath))
            candidates.Add(storedPreviewPath);

        if (!string.IsNullOrWhiteSpace(mapBasePath))
        {
            string resolved = ResolveMapPreviewRelativePath(
                mapBasePath,
                string.IsNullOrWhiteSpace(storedPreviewPath) ? null : Path.GetFileName(storedPreviewPath));
            if (!string.IsNullOrEmpty(resolved) && !candidates.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                candidates.Add(resolved);

            string fromBase = ResolveMapPreviewRelativePath(mapBasePath);
            if (!string.IsNullOrEmpty(fromBase) && !candidates.Contains(fromBase, StringComparer.OrdinalIgnoreCase))
                candidates.Add(fromBase);
        }

        foreach (string fallback in ResolveNoPreviewCandidates(host))
        {
            if (!candidates.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                candidates.Add(fallback);
        }

        return resources.LoadFirstBitmap(candidates);
    }

    public static Bitmap? LoadMissionPreview(ResourceResolver resources, MissionEntry? mission, UiNodeViewModel? host = null)
    {
        Bitmap? fromIni = LoadConfiguredTexture(resources, host, "PreviewTexture", "MissionPreviewTexture");
        if (fromIni != null)
            return fromIni;

        if (mission == null || mission.IsHeader)
            return resources.LoadFirstBitmap(ResolveNoPreviewCandidates(host));

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(mission.Scenario))
        {
            string scenarioBase = Path.GetFileNameWithoutExtension(mission.Scenario);
            foreach (string prefix in MissionPreviewPrefixes)
                candidates.Add($"{prefix}/{scenarioBase}.png");

            candidates.Add($"Maps/Missions/{scenarioBase}.png");
            candidates.Add($"Maps/{scenarioBase}.png");
            candidates.Add($"{scenarioBase}.png");

            string resolved = ResolveMapPreviewRelativePath(mission.Scenario);
            if (!string.IsNullOrEmpty(resolved))
                candidates.Add(resolved);

            resolved = ResolveMapPreviewRelativePath(scenarioBase);
            if (!string.IsNullOrEmpty(resolved))
                candidates.Add(resolved);
        }

        foreach (string fallback in ResolveNoPreviewCandidates(host))
            candidates.Add(fallback);

        return resources.LoadFirstBitmap(candidates);
    }

    public static void ApplyStandardButtonTextures(UiNodeViewModel vm, ResourceResolver resources)
    {
        if (vm.IdleImage != null)
            return;

        int preferredWidth = (int)Math.Round(vm.Width);
        foreach (int width in EnumerateButtonWidths(preferredWidth))
        {
            Bitmap? idle = resources.LoadBitmap($"{width}pxbtn.png");
            if (idle == null)
                continue;

            Bitmap? hover = resources.LoadBitmap($"{width}pxbtn_c.png");
            vm.SetButtonTextures(idle, hover);
            return;
        }

        // MG ThemeMG provides button.png / button_c.png instead of 92pxbtn.png.
        // Texture names centralized in IniUi.Layout.OverlayLayoutConstants (Issue #5).
        Bitmap? mgIdle = resources.LoadBitmap(IniUi.Layout.OverlayLayoutConstants.MgButtonIdleTexture);
        if (mgIdle != null)
            vm.SetButtonTextures(mgIdle, resources.LoadBitmap(IniUi.Layout.OverlayLayoutConstants.MgButtonHoverTexture));
    }

    public static void ApplyCampaignSideIcons(UiNodeViewModel root, ResourceResolver resources)
    {
        // Tactical replaces faction icons with text-only tabs.
        if (Themes.DxThemeManager.IsTactical)
            return;

        foreach (string controlId in new[] { "GDI", "Nod", "ThirdSide", "FourthSide" })
        {
            UiNodeViewModel? tab = FindVm(root, controlId);
            if (tab == null)
                continue;

            string? sideName = ResolveSideName(tab);
            if (sideName == null && !HasIconTexture(tab))
                continue;

            tab.SetSideIcon(LoadSideIcon(resources, sideName, tab));
        }
    }

    public static void ApplyCampaignActionButtonTextures(UiNodeViewModel root, ResourceResolver resources)
    {
        // Tactical drops PNG button chrome entirely.
        if (Themes.DxThemeManager.IsTactical)
            return;

        foreach (UiNodeViewModel button in EnumerateNodes(root))
        {
            if (!IsActionButton(button))
                continue;

            if (button.IdleImage == null)
                ApplyStandardButtonTextures(button, resources);
        }
    }

    public static void ApplyDifficultyTrackbarTextures(UiNodeViewModel trackbar, ResourceResolver resources)
    {
        Bitmap? thumb = LoadConfiguredTexture(resources, trackbar, "ThumbTexture", "ButtonTexture", "IdleTexture");
        thumb ??= resources.LoadFirstBitmap(["trackbarButton_difficulty.png", "trackbarButton.png"]);
        trackbar.SetThumbImage(thumb);
    }

    private static bool IsActionButton(UiNodeViewModel vm)
        => vm.Id.StartsWith("btn", StringComparison.OrdinalIgnoreCase)
           || vm.ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase);

    private static bool HasIconTexture(UiNodeViewModel vm)
        => !string.IsNullOrWhiteSpace(vm.GetIniString("SideIconTexture"))
           || !string.IsNullOrWhiteSpace(vm.GetIniString("IconTexture"));

    private static Bitmap? LoadConfiguredTexture(ResourceResolver resources, UiNodeViewModel? host, params string[] keys)
    {
        foreach (string key in keys)
        {
            string? path = host?.GetIniString(key);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            Bitmap? bitmap = resources.LoadBitmap(path);
            if (bitmap != null)
                return bitmap;
        }

        return null;
    }

    private static void AddTextureCandidate(List<string> candidates, UiNodeViewModel? host, params string[] keys)
    {
        foreach (string key in keys)
        {
            string? path = host?.GetIniString(key);
            if (!string.IsNullOrWhiteSpace(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
                candidates.Add(path);
        }
    }

    private static IEnumerable<int> EnumerateButtonWidths(int preferredWidth)
    {
        if (preferredWidth > 0)
            yield return preferredWidth;

        foreach (int width in StandardButtonWidths)
        {
            if (width != preferredWidth)
                yield return width;
        }
    }

    private static bool GameFileExists(string gameRelativePath)
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return false;

        try
        {
            string full = Path.Combine(
                AppState.Environment.GamePath,
                gameRelativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<UiNodeViewModel> EnumerateNodes(UiNodeViewModel root)
    {
        yield return root;
        foreach (UiNodeViewModel child in root.Children)
        {
            foreach (UiNodeViewModel node in EnumerateNodes(child))
                yield return node;
        }
    }

    private static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindVm(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
