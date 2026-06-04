using Avalonia;
using Avalonia.Media;
using ClientAvalonia.Core;
using ClientCore;

namespace ClientAvalonia.Platform;

/// <summary>M5: OS → font / IME / window behavior mapping (Windows first).</summary>
public static class WindowsPlatformProfile
{
    public static OSVersion OsVersion { get; private set; } = OSVersion.UNKNOWN;

    public static FontFamily UiFontFamily { get; private set; } = new("Microsoft YaHei UI, Segoe UI, Noto Sans CJK SC, sans-serif");

    public static void Apply(Application app)
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return;

        OsVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();

        UiFontFamily = OsVersion switch
        {
            OSVersion.UNIX => new FontFamily("Noto Sans CJK SC, WenQuanYi Micro Hei, sans-serif"),
            _ => new FontFamily("Microsoft YaHei UI, Segoe UI, Noto Sans CJK SC, sans-serif"),
        };

        app.Resources["DxUiFontFamily"] = UiFontFamily;
    }
}
