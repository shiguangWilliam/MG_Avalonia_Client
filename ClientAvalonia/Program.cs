using System;
using System.IO;
using Avalonia;
using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (TryValidateIni(args))
            return;

        ClientStartupService.Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool TryValidateIni(string[] args)
    {
        if (args.Length == 0)
            return false;

        if (args[0] == "--dump-tree")
        {
            DumpTree(args);
            return true;
        }

        if (args[0] == "--validate-bindings")
        {
            ValidateBindings(args);
            return true;
        }

        if (args[0] == "--validate-resources")
        {
            ValidateGameResources();
            return true;
        }

        if (args[0] != "--validate-ini")
            return false;

        ClientEnvironment env = ClientEnvironment.Discover();
        string iniPath = args.Length > 1
            ? args[1]
            : env.ResolveWindowIni("MainMenu")
              ?? throw new FileNotFoundException("MainMenu.ini not found for active theme.");

        var engine = LayoutEngine.CreateForWindow(env, iniPath, "MainMenu");
        UiNodeTree tree = engine.LoadWindow(iniPath, "MainMenu");
        int nodes = tree.AllNodes().Count();
        UiNode? btnSkirmish = tree.FindNode("btnSkirmish");
        ResourceResolver resources = engine.Resources;
        string? idleTex = btnSkirmish?.Props.TryGetValue("IdleTexture", out object? tex) == true ? tex?.ToString() : null;
        string? idlePath = resources.ResolveTexturePath(idleTex);
        (int Width, int Height)? texSize = resources.GetTextureSize(idleTex);

        Console.WriteLine(
            $"OK: {nodes} nodes, rootChildren={tree.Root.Children.Count}, viewport {engine.Context.Width}x{engine.Context.Height}, " +
            $"root {tree.Root.GetIntProp("Width")}x{tree.Root.GetIntProp("Height")}, " +
            $"theme={env.ThemeFolderPath}, ini={iniPath}, " +
            $"btnSkirmish @ ({btnSkirmish?.GetIntProp("CanvasLeft")},{btnSkirmish?.GetIntProp("CanvasTop")}) " +
            $"{btnSkirmish?.GetIntProp("Width")}x{btnSkirmish?.GetIntProp("Height")}, " +
            $"texture={(idlePath != null ? Path.GetFileName(idlePath) : "missing")}" +
            (texSize != null ? $", texSize={texSize.Value.Width}x{texSize.Value.Height}" : ", texSize=missing"));
        return true;
    }

    private static void DumpTree(string[] args)
    {
        ClientEnvironment env = ClientEnvironment.Discover();
        string iniPath = args.Length > 1
            ? args[1]
            : env.ResolveWindowIni("MainMenu")
              ?? throw new FileNotFoundException("MainMenu.ini not found for active theme.");

        string windowSection = args.Length > 2
            ? args[2]
            : Path.GetFileNameWithoutExtension(iniPath);

        var engine = LayoutEngine.CreateForWindow(env, iniPath, windowSection);
        UiNodeTree tree = engine.LoadWindow(iniPath, windowSection);
        ResourceResolver resources = engine.Resources;

        Console.WriteLine($"Tree: {tree.SourcePath}");
        Console.WriteLine($"Root children: {tree.Root.Children.Count}");
        foreach (UiNode node in tree.AllNodes())
        {
            string text = node.Props.TryGetValue("Text", out object? t) ? t?.ToString() ?? "" : "";
            bool visible = !node.Props.TryGetValue("IsVisible", out object? v) || v is not bool b || b;
            string idle = node.Props.TryGetValue("IdleTexture", out object? tex) ? tex?.ToString() ?? "" : "";
            bool bitmapOk = string.IsNullOrEmpty(idle) || resources.LoadBitmap(idle) != null;
            Console.WriteLine(
                $"  {node.Id,-18} {node.TemplateKey,-12} @({node.GetIntProp("CanvasLeft")},{node.GetIntProp("CanvasTop")}) " +
                $"{node.GetIntProp("Width")}x{node.GetIntProp("Height")} visible={visible} text=\"{text}\" idle={idle} bmp={(bitmapOk ? "ok" : "fail")}");
        }
    }

    private static void ValidateGameResources()
    {
        ClientStartupService.Run();
        var catalog = Services.GameResourceCatalog.Instance;
        catalog.EnsureLoaded();
        Console.WriteLine(
            $"OK: maps={catalog.Maps.Count}, gameModes={catalog.GameModes.Count}, missions={catalog.Missions.Count}, " +
            $"gameRoot={ClientEnvironment.Discover().GameRoot}");
        if (catalog.GameModes.Count > 0)
        {
            var sample = catalog.GetMapsForFilterIndex(LobbySessionState.FavoriteFilterIndex + 1);
            Console.WriteLine($"  mode[0]={catalog.GameModes[0].DisplayName}, maps={sample.Count}, first={(sample.Count > 0 ? sample[0].DisplayName : "none")}");
            Console.WriteLine($"  favorites={catalog.GetFavoriteMaps().Count}, custom={catalog.Maps.Count(m => m.IsCustom)}");
        }

        if (catalog.Missions.Count > 0)
            Console.WriteLine($"  firstMission={catalog.Missions[0].DisplayName}, playable={catalog.Missions.Count(m => !m.IsHeader)}");
    }

    private static void ValidateBindings(string[] args)
    {
        ClientEnvironment env = ClientEnvironment.Discover();
        string iniPath = args.Length > 1
            ? args[1]
            : env.ResolveWindowIni("OptionsWindow")
              ?? throw new FileNotFoundException("OptionsWindow.ini not found for active theme.");

        string windowSection = args.Length > 2
            ? args[2]
            : "OptionsWindow";

        var engine = LayoutEngine.CreateForWindow(env, iniPath, windowSection);
        UiNodeTree tree = engine.LoadWindow(iniPath, windowSection);
        var factory = new UiViewModelFactory(engine.Resources, new IniUi.Behaviors.BehaviorRegistry());
        UiNodeViewModel vm = factory.CreateTree(tree);

        var session = new UiBindingSession(env);
        session.ApplyToTree(vm, windowSection);

        UiNodeViewModel? lblVersion = FindVm(vm, "lblVersion");
        UiNodeViewModel? chkPersistent = FindVm(vm, "chkPersistentMode");

        Console.WriteLine(
            $"OK: window={windowSection}, settingsPath={session.Settings.SettingsPath}, " +
            $"settingBindings={session.SettingBindingCount}, version=\"{lblVersion?.Text}\", " +
            $"updateStatus=\"{FindVm(vm, "lblUpdateStatus")?.Text}\", " +
            $"chkPersistentMode={(chkPersistent != null ? chkPersistent.IsChecked.ToString() : "missing")}");
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
