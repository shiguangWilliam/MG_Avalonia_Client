using System;
using System.IO;
using ClientCore;

namespace ClientAvalonia.Tests.Fixture;

/// <summary>
/// Creates a throwaway directory tree that looks like a CnCNet game root to
/// <see cref="ClientAvalonia.IniUi.Loading.ClientEnvironment.FindGameRoot"/> /
/// <see cref="ClientAvalonia.Core.InstallationRegistry.IsInstallPathValid"/>:
///
///   {TempRoot}                ← the "game root"
///     Resources\
///       ClientDefinitions.ini ← marker file (must exist for valid install paths)
///       dummy.ini             ← needed so the second WalkUpForGameRoot branch matches
///
/// Disposing drops the whole tree and resets <see cref="ProgramConstants"/> game-root override.
/// </summary>
internal sealed class TempGameRoot : IDisposable
{
    private bool _disposed;

    public TempGameRoot()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "ClientAvaloniaTests_" + Guid.NewGuid().ToString("N"));
        ResourcesPath = Path.Combine(RootPath, "Resources");
        Directory.CreateDirectory(ResourcesPath);

        File.WriteAllText(Path.Combine(ResourcesPath, "ClientDefinitions.ini"),
            "[Settings]\r\n" +
            "SettingsFile=Settings.ini\r\n" +
            "InstallationPathRegKey=MomentOfGenesis\r\n" +
            "MaxNameLength=16\r\n");

        // Marker INI for the WalkUpForGameRoot fallback branch
        // ("Resources/ exists and has at least one *.ini"). ClientDefinitions.ini already satisfies this,
        // but we add an explicit one in case ClientDefinitions.ini is filtered.
        File.WriteAllText(Path.Combine(ResourcesPath, "dummy.ini"), "[X]\r\n");

        GameRoot = RootPath;
    }

    /// <summary>The resolved install-root-equivalent directory. Same as <see cref="RootPath"/>.</summary>
    public string GameRoot { get; }

    public string RootPath { get; }

    public string ResourcesPath { get; }

    public string ClientDefinitionsPath => Path.Combine(ResourcesPath, "ClientDefinitions.ini");

    /// <summary>Binds <see cref="ProgramConstants.SetHostedGameRoot"/> so ClientCore finds our tree.</summary>
    public void BindToProgramConstants()
    {
        ProgramConstants.SetHostedGameRoot(GameRoot);
        Environment.CurrentDirectory = GameRoot;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try { Directory.Delete(RootPath, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
