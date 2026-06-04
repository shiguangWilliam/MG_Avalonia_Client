using System.Globalization;
using System.IO;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.I18N;
using ClientCore.PlatformShim;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>Initializes ClientCore (settings, translation, resource paths) aligned with DXMainClient PreStartup.</summary>
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

            _ = EncodingExt.UTF8NoBOM;

            Translation.InitialUICulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = new CultureInfo(ProgramConstants.HARDCODED_LOCALE_CODE);

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
                Translation translation = new(translationFile.FullName, UserINISettings.Instance.Translation.Value);
                if (translationThemeFile.Exists)
                    translation.AppendValuesFromIniFile(translationThemeFile.FullName);

                Translation.Instance = translation;
            }
            else
                Translation.Instance = new Translation(UserINISettings.Instance.Translation.Value);
        }
        catch
        {
            Translation.Instance = new Translation(UserINISettings.Instance.Translation.Value);
        }
    }
}
