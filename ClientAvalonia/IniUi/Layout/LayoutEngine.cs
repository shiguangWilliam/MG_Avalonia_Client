using ClientAvalonia.IniUi;
using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;

namespace ClientAvalonia.IniUi.Layout;

/// <summary>INI 鈫?AST 鈫?UiNode tree 鈫?precomputed layout (M2: single pass at load).</summary>
public sealed class LayoutEngine
{
    private readonly IniUiTreeBuilder _treeBuilder;
    private readonly LayoutResolver _layoutResolver;
    private readonly MeasurePass _measurePass;
    private readonly PanelLayoutPass _panelLayoutPass;
    private readonly ResourceResolver _resources;
    private LayoutContext _context;

    public LayoutEngine(LayoutContext context, ILocalizationService? localization = null, ResourceResolver? resources = null)
    {
        _context = context;
        _resources = resources ?? new ResourceResolver();
        ControlRegistry registry = DefaultControlRegistry.Create();
        var evaluator = new ExpressionEvaluator(context.Width, context.Height, context.ParserConstants);
        _layoutResolver = new LayoutResolver(evaluator);
        _measurePass = new MeasurePass(_resources);
        _panelLayoutPass = new PanelLayoutPass();
        var propertyResolver = new PropertyResolver(registry, localization ?? new PassthroughLocalizationService());
        _treeBuilder = new IniUiTreeBuilder(registry, propertyResolver);
    }

    public ResourceResolver Resources => _resources;

    public static LayoutEngine CreateFor(ClientEnvironment environment, ResourceResolver? resources = null)
    {
        resources ??= new ResourceResolver();
        resources.ConfigureForGame(environment);
        return new LayoutEngine(new LayoutContext(environment.ClientRenderWidth, environment.ClientRenderHeight), resources: resources);
    }

    public static LayoutEngine CreateForWindow(
        ClientEnvironment environment,
        string iniPath,
        string windowSectionName,
        ResourceResolver? resources = null)
    {
        resources ??= new ResourceResolver();
        resources.ConfigureForGame(environment);
        ClientCoreBootstrap.TryEnsureInitialized(environment.GameRoot, out _);
        LayoutContext context = environment.CreateLayoutContextForWindow(iniPath, windowSectionName);
        ILocalizationService localization = ClientCoreBootstrap.IsInitialized
            ? new CoreLocalizationService()
            : new PassthroughLocalizationService();
        return new LayoutEngine(context, localization, resources);
    }

    public static LayoutEngine CreateM2(ILocalizationService? localization = null, ResourceResolver? resources = null)
        => new(LayoutContext.M2Default, localization, resources);

    public LayoutContext Context => _context;

    public UiNodeTree LoadWindow(string iniPath, string windowSectionName)
    {
        string gameRoot = ClientEnvironment.FindGameRoot(Path.GetDirectoryName(Path.GetFullPath(iniPath)) ?? AppContext.BaseDirectory);
        _resources.ConfigureForGame(ClientEnvironment.Discover(gameRoot));
        IniFileAst ast = IniAstBuilder.BuildFromFile(iniPath);
        UiNodeTree tree = _treeBuilder.Build(ast, windowSectionName);
        WindowTreePostProcessor.Apply(tree, windowSectionName, _context, ast.OverlaySectionNames);
        ApplyLayout(tree);

        if (windowSectionName.Equals(WindowKind.OptionsWindow, StringComparison.OrdinalIgnoreCase))
            OptionsWindowLayout.FinalizeLayout(tree);

        if (windowSectionName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            LobbyLayout.ApplyMapToolbarLayout(tree);

        if (IsChannelLobbyWindow(windowSectionName))
            ChannelLobbyLayout.Apply(tree, _context, windowSectionName);

        if (windowSectionName.Equals(WindowKind.MultiplayerGameLobby, StringComparison.OrdinalIgnoreCase))
            MultiplayerGameLobbyLayout.Apply(tree);

        if (windowSectionName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            LobbyOptionsPanelLayoutPolicy.Apply(tree, windowSectionName);

        return tree;
    }

    public void ApplyLayout(UiNodeTree tree)
    {
        _measurePass.Apply(tree);
        _layoutResolver.ApplyLayoutPass(tree);
        _measurePass.Apply(tree);
        _panelLayoutPass.Apply(tree);
    }

    /// <summary>M5+: switch discrete resolution preset and recompute all layout Props.</summary>
    public void Relayout(UiNodeTree tree, LayoutContext newContext)
    {
        _context = newContext;
        _layoutResolver.UpdateResolution(newContext.Width, newContext.Height, newContext.ParserConstants);
        ApplyLayout(tree);
    }

    private static bool IsChannelLobbyWindow(string windowSectionName)
        => windowSectionName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
           || windowSectionName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase);
}
