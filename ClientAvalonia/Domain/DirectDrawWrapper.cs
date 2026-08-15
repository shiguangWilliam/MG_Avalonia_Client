using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Domain;

/// <summary>DirectDraw wrapper option (DXMainClient <c>DTAClient.Domain.DirectDrawWrapper</c>).</summary>
public class DirectDrawWrapper
{
    public DirectDrawWrapper(string internalName, IniFile iniFile)
    {
        InternalName = internalName;
        Parse(iniFile.GetSection(InternalName));
    }

    public string InternalName { get; private set; } = string.Empty;
    public string UIName { get; private set; } = string.Empty;
    public string WindowedModeSection { get; private set; } = string.Empty;
    public string WindowedModeKey { get; private set; } = string.Empty;
    public string BorderlessWindowedModeKey { get; private set; } = string.Empty;
    public bool IsBorderlessWindowedModeKeyReversed { get; private set; }
    public bool Hidden { get; private set; }
    public bool UseQres { get; private set; } = true;
    public bool SingleCoreAffinity { get; private set; } = true;
    public string ConfigFileName { get; private set; } = string.Empty;
    public bool IsDummy => string.IsNullOrEmpty(_ddrawDllPath);

    /// <summary>Relative path under Resources\ for the renderer DLL (Renderers.ini DLLName=).</summary>
    public string DdrawDllResourcePath => _ddrawDllPath;

    private string _ddrawDllPath = string.Empty;
    private string _resConfigFileName = string.Empty;
    private List<string> _filesToCopy = [];
    private List<OSVersion> _disallowedOsList = [];

    private void Parse(IniSection? section)
    {
        if (section == null)
        {
            Logger.Log("DirectDrawWrapper: Configuration for renderer '" + InternalName + "' not found!");
            return;
        }

        UIName = section.GetStringValue("UIName", "Unnamed renderer");

        if (section.GetBooleanValue("IsDxWnd", false))
        {
            WindowedModeSection = "DxWnd";
            WindowedModeKey = "RunInWindow";
            BorderlessWindowedModeKey = "NoWindowFrame";
        }

        WindowedModeSection = section.GetStringValue("WindowedModeSection", WindowedModeSection);
        WindowedModeKey = section.GetStringValue("WindowedModeKey", WindowedModeKey);
        BorderlessWindowedModeKey = section.GetStringValue("BorderlessWindowedModeKey", BorderlessWindowedModeKey);
        IsBorderlessWindowedModeKeyReversed = section.GetBooleanValue(
            "IsBorderlessWindowedModeKeyReversed",
            IsBorderlessWindowedModeKeyReversed);

        Hidden = section.GetBooleanValue("Hidden", false);
        UseQres = section.GetBooleanValue("UseQres", UseQres);
        SingleCoreAffinity = section.GetBooleanValue("SingleCoreAffinity", SingleCoreAffinity);
        _ddrawDllPath = section.GetStringValue("DLLName", string.Empty);
        ConfigFileName = section.GetStringValue("ConfigFileName", string.Empty);
        _resConfigFileName = section.GetStringValue("ResConfigFileName", ConfigFileName);

        _filesToCopy = section.GetStringValue("AdditionalFiles", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        foreach (string os in section.GetStringValue("DisallowedOperatingSystems", string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            _disallowedOsList.Add((OSVersion)Enum.Parse(typeof(OSVersion), os.Trim()));
        }
    }

    public bool IsCompatibleWithOS(OSVersion os) => !_disallowedOsList.Contains(os);

    public void Apply()
    {
        string ddrawDllSourcePath = SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, _ddrawDllPath);
        string ddrawDllTargetPath = SafePath.CombineFilePath(AppState.Environment.GamePath, "ddraw.dll");

        if (!string.IsNullOrEmpty(_ddrawDllPath))
        {
            if (!File.Exists(ddrawDllSourcePath))
            {
                Logger.Log($"DirectDrawWrapper: renderer '{InternalName}' DLL missing at {ddrawDllSourcePath}.");
                return;
            }

            FileExtensions.CreateHardLinkFromSource(ddrawDllSourcePath, ddrawDllTargetPath);
            new FileInfo(ddrawDllSourcePath).IsReadOnly = true;
            new FileInfo(ddrawDllTargetPath).IsReadOnly = true;
            Logger.Log($"DirectDrawWrapper: applied {InternalName} → ddraw.dll ({new FileInfo(ddrawDllTargetPath).Length} bytes).");
        }
        else if (File.Exists(ddrawDllTargetPath))
        {
            new FileInfo(ddrawDllTargetPath).IsReadOnly = false;
            File.Delete(ddrawDllTargetPath);
        }

        if (!string.IsNullOrEmpty(ConfigFileName)
            && !string.IsNullOrEmpty(_resConfigFileName)
            && !SafePath.GetFile(AppState.Environment.GamePath, ConfigFileName).Exists)
        {
            File.Copy(
                SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, _resConfigFileName),
                SafePath.CombineFilePath(AppState.Environment.GamePath, Path.GetFileName(ConfigFileName)));
        }

        foreach (string file in _filesToCopy)
        {
            File.Copy(
                SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, file),
                SafePath.CombineFilePath(AppState.Environment.GamePath, Path.GetFileName(file)),
                true);
        }
    }

    public void Clean()
    {
        if (!string.IsNullOrEmpty(ConfigFileName))
            SafePath.DeleteFileIfExists(AppState.Environment.GamePath, Path.GetFileName(ConfigFileName));

        foreach (string file in _filesToCopy)
            SafePath.DeleteFileIfExists(AppState.Environment.GamePath, Path.GetFileName(file));
    }

    public bool UsesCustomWindowedOption()
        => !string.IsNullOrEmpty(WindowedModeSection) && !string.IsNullOrEmpty(WindowedModeKey);

    public static bool operator ==(DirectDrawWrapper? a, DirectDrawWrapper? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a is null || b is null)
            return false;

        return a.InternalName == b.InternalName;
    }

    public static bool operator !=(DirectDrawWrapper? a, DirectDrawWrapper? b) => !(a == b);

    public override bool Equals(object? obj) => obj is DirectDrawWrapper other && this == other;

    public override int GetHashCode() => InternalName.GetHashCode();
}
