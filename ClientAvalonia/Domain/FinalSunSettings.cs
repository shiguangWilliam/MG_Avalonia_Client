using System.IO;
using ClientCore;
using ClientCore.PlatformShim;
using Rampastring.Tools;

namespace ClientAvalonia.Domain;

/// <summary>DXMainClient <c>DTAClient.Domain.FinalSunSettings</c>.</summary>
public static class FinalSunSettings
{
    public static void WriteFinalSunIni()
    {
        try
        {
            string finalSunIniPath = ClientConfiguration.Instance.FinalSunIniPath;
            var finalSunIniFile = new FileInfo(Path.Combine(ProgramConstants.GamePath, finalSunIniPath));

            Logger.Log("Checking for the existence of FinalSun.ini.");
            if (finalSunIniFile.Exists)
            {
                Logger.Log("FinalSun settings file exists.");

                var iniFile = new IniFile
                {
                    FileName = finalSunIniFile.FullName,
                    Encoding = EncodingExt.ANSI,
                };
                iniFile.Parse();

                iniFile.SetStringValue("FinalSun", "Language", "English");
                iniFile.SetStringValue("FinalSun", "FileSearchLikeTS", "yes");
                iniFile.SetStringValue("TS", "Exe", SafePath.CombineDirectoryPath(ProgramConstants.GamePath));
                iniFile.WriteIniFile();
                return;
            }

            Logger.Log("FinalSun.ini doesn't exist - writing default settings.");

            if (!finalSunIniFile.Directory!.Exists)
                finalSunIniFile.Directory.Create();

            using var sw = new StreamWriter(finalSunIniFile.FullName, false, EncodingExt.ANSI);
            sw.WriteLine("[FinalSun]");
            sw.WriteLine("Language=English");
            sw.WriteLine("FileSearchLikeTS=yes");
            sw.WriteLine("");
            sw.WriteLine("[TS]");
            sw.WriteLine("Exe=" + SafePath.CombineDirectoryPath(ProgramConstants.GamePath));
            sw.WriteLine("");
            sw.WriteLine("[UserInterface]");
            sw.WriteLine("EasyView=0");
            sw.WriteLine("NoSounds=0");
            sw.WriteLine("DisableAutoLat=0");
            sw.WriteLine("ShowBuildingCells=0");
        }
        catch (Exception ex)
        {
            Logger.Log("An exception occurred while checking the existence of FinalSun settings: " + ex.Message);
        }
    }
}
