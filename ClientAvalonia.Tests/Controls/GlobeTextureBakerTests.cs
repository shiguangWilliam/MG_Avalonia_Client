using System;
using Avalonia.Platform;
using Xunit;

namespace ClientAvalonia.Tests.Controls;

/// <summary>
/// Verifies the GL albedo pixel pipeline: the embedded world_map asset loads
/// and decodes (when the test host has an AssetLoader) and the baker always
/// yields a valid equirectangular RGBA buffer — real map or procedural
/// fallback — catching pixel-data bugs before they surface as a black sphere.
/// </summary>
public sealed class GlobeTextureBakerTests
{
    private static bool AssetLoaderAvailable
    {
        get
        {
            try
            {
                _ = AssetLoader.Open(new Uri("avares://ClientAvalonia/Assets/Glm/world_map.png"));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    [SkippableFact]
    public void WorldMap_Asset_Loads_When_AssetLoader_Registered()
    {
        Skip.IfNot(AssetLoaderAvailable,
            "Test host has no Avalonia AssetLoader; avares:// cannot resolve.");

        var bmp = ClientAvalonia.Assets.GlmAssets.WorldMap;

        Assert.NotNull(bmp);
        Assert.True(bmp!.PixelSize.Width >= 1024,
            $"world_map too small: {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
        Assert.True(bmp.PixelSize.Height >= 512,
            $"world_map too small: {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
    }

    [Fact]
    public void TryGetPixels_Returns_Valid_Equirectangular_Buffer()
    {
        // Real asset (1728x850) or procedural fallback (512x256) — both ~2:1.
        bool ok = ClientAvalonia.Controls.GlobeTextureBaker.TryGetPixels(
            out byte[] pixels, out int width, out int height);

        Assert.True(ok);
        Assert.True(width > 0 && height > 0, $"dims {width}x{height}");
        Assert.Equal(width * height * 4, pixels.Length);

        double ratio = (double)width / height;
        Assert.InRange(ratio, 1.9, 2.1);

        // Not entirely black (a real holographic map or the ocean gradient).
        long sum = 0;
        int stride = pixels.Length / 256;
        for (int i = 0; i < pixels.Length; i += stride)
            sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
        Assert.True(sum > 0, "albedo buffer is entirely black");
    }
}
