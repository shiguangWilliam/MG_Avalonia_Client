using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ClientAvalonia.Assets;

/// <summary>
/// Lazy loader for GLM-Image art embedded under Assets/Glm/. Returns null when
/// a file is missing so callers can fall back to procedural drawing.
/// </summary>
internal static class GlmAssets
{
    private const string BaseUri = "avares://ClientAvalonia/Assets/Glm/";

    private static Bitmap? _starfield;
    private static Bitmap? _genesisHorizon;
    private static Bitmap? _tacticalPanel;
    private static Bitmap? _worldMap;
    private static Bitmap? _holoSunGlow;
    private static bool _starfieldTried;
    private static bool _genesisTried;
    private static bool _panelTried;
    private static bool _worldTried;
    private static bool _holoSunTried;

    public static Bitmap? Starfield => Load(ref _starfieldTried, ref _starfield, "starfield_main.png");
    public static Bitmap? GenesisHorizon => Load(ref _genesisTried, ref _genesisHorizon, "genesis_horizon.png");
    public static Bitmap? TacticalPanel => Load(ref _panelTried, ref _tacticalPanel, "tactical_panel.png");
    public static Bitmap? WorldMap => Load(ref _worldTried, ref _worldMap, "world_map.png");
    public static Bitmap? HoloSunGlow => Load(ref _holoSunTried, ref _holoSunGlow, "holo_sun_glow.png");

    /// <summary>Warm the cache on a background thread (Tactical preload).</summary>
    public static void WarmUp()
    {
        _ = Starfield;
        _ = TacticalPanel;
        _ = WorldMap;
        _ = HoloSunGlow;
    }

    private static Bitmap? Load(ref bool tried, ref Bitmap? cache, string fileName)
    {
        if (tried)
            return cache;
        tried = true;

        try
        {
            var uri = new Uri(BaseUri + fileName);
            if (!AssetLoader.Exists(uri))
                return null;

            using Stream stream = AssetLoader.Open(uri);
            cache = new Bitmap(stream);
            return cache;
        }
        catch
        {
            cache = null;
            return null;
        }
    }
}
