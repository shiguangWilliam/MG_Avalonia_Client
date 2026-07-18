using System.Globalization;
using System.IO;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.I18N;
using ClientCore.PlatformShim;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>Initializes ClientCore user settings and translation (DXMainClient PreStartup, post-logger).</summary>
public static class ClientCoreBootstrap
{
    private static bool _initialized;

    public static bool IsInitialized => _initialized;

    public static void EnsureInitialized(string? gameRoot = null)
    {
        if (_initialized)
            return;

        if (!TryEnsureInitialized(gameRoot, out string? error))
            throw new InvalidOperationException(error ?? "ClientCore bootstrap failed.");
    }

    public static bool TryEnsureInitialized(string? gameRoot, out string? error)
    {
        if (_initialized)
        {
            error = null;
            return true;
        }

        try
        {
            gameRoot ??= ClientEnvironment.FindGameRoot(Directory.GetCurrentDirectory());
            Environment.CurrentDirectory = gameRoot;
            ProgramConstants.SetHostedGameRoot(gameRoot);

            if (!ClientLogService.IsInitialized)
            {
                error = "Logger must be initialized before ClientCoreBootstrap.";
                return false;
            }

            if (!TryCompleteUserSettingsInitialization(out error))
                return false;

            _initialized = true;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryCompleteUserSettingsInitialization(out string? error)
    {
        try
        {
            Logger.Log("Loading settings.");

            UserINISettings.Initialize(ClientConfiguration.Instance.SettingsIniName);

            ProgramConstants.RESOURCES_DIR = SafePath.CombineDirectoryPath(
                ProgramConstants.BASE_RESOURCE_PATH,
                UserINISettings.Instance.ThemeFolderPath);

            LoadTranslation();
            CultureInfo.CurrentUICulture = Translation.Instance.Culture;

            PlayerNameSettings.ApplyFromUserSettings();

            _initialized = true;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void LoadTranslation()
    {
        try
        {
            FileInfo translationThemeFile = SafePath.GetFile(
                UserINISettings.Instance.TranslationThemeFolderPath,
                ClientConfiguration.Instance.TranslationIniName);
            FileInfo translationFile = SafePath.GetFile(
                UserINISettings.Instance.TranslationFolderPath,
                ClientConfiguration.Instance.TranslationIniName);

            if (translationFile.Exists)
            {
                Logger.Log($"Loading generic translation file at {translationFile.FullName}");
                Translation translation = new(translationFile.FullName, UserINISettings.Instance.Translation.Value);
                if (translationThemeFile.Exists)
                {
                    Logger.Log($"Loading theme-specific translation file at {translationThemeFile.FullName}");
                    translation.AppendValuesFromIniFile(translationThemeFile.FullName);
                }

                Translation.Instance = translation;
            }
            else
            {
                Logger.Log(
                    $"Failed to load a translation file. Neither {translationThemeFile.FullName} nor {translationFile.FullName} exist.");
                Translation.Instance = new Translation(UserINISettings.Instance.Translation.Value);
            }

            Logger.Log("Loaded translation: " + Translation.Instance.Name);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load the translation file. " + ex);
            Translation.Instance = new Translation(UserINISettings.Instance.Translation.Value);
        }
    }

    /// <summary>
    /// Clears ClientCore singletons so a new workspace can bootstrap in-process
    /// (Avalonia multi-mod return-to-picker). DXMainClient does not use this.
    /// </summary>
    public static void ResetForWorkspaceRebind()
    {
        _initialized = false;
        UserINISettings.ResetInstance();
        ClientConfiguration.ResetInstance();
        Logger.Log("ClientCoreBootstrap: reset for workspace rebind.");
    }
}
