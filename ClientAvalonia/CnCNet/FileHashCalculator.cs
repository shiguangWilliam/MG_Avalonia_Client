using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using ClientCore;
using ClientCore.I18N;
using ClientCore.Enums;

using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

    public class FileHashCalculator
    {
        private const string CONFIGNAME = "FHCConfig.ini";
        private bool calculateGameExeHash = true;
        private bool useReferenceLauncherHashes;
        private readonly Dictionary<string, string> referenceHashes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>MG 1.0.4.2 release launcher hashes (DXMain log); used when Avalonia publish differs.</summary>
        private static readonly Dictionary<string, string> DefaultMgReferenceHashes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientDefinitionsHash"] = "724e864e34399ff264a6a6f9a232fa95cb37cf77",
            ["FHCConfigHash"] = "edbc6233c4e9b9f408df7b241a7b23abfd3596f7",
            ["GameOptionsHash"] = "001d08f6078d641c3dae6dab074c486dd00c2bcf",
            ["ClientDXHash"] = "87e3cc06fa1a3126f0779918c2851a01174ff261",
            ["ClientXNAHash"] = "2f1f25c83a2d78bea8ac8a080a651656870d5c0f",
            ["ClientOGLHash"] = "610078c9f92bec9c583b06819a763b17542aa033",
            ["ClientDXNET8Hash"] = "a9930c4ca4938353fd7a434e1da7cc6b2ead37b8",
            ["ClientXNANET8Hash"] = "15b016711b055d11cfc0531128f197541f736743",
            ["ClientOGLNET8Hash"] = "0a520117890da4840787a1db4a8605cfec27ebd8",
            ["ClientUGLNET8Hash"] = "012a8191d5f725993a85c3eb22160a6f8d3b189d",
            ["MPMapsHash"] = "c8dbba5bc8e8b128441742318539f52b11036702",
        };

        private static readonly IReadOnlyList<string> knownTextFileExtensions = [".txt", ".ini", ".json", ".xml"];

        private string[] fileNamesToCheck = ClientConfiguration.Instance.ClientGameType switch
        {
            ClientType.TS => new string[]
            {
                "spawner.xdp",
                "rules.ini",
                "ai.ini",
                "art.ini",
                "shroud.shp",
                "INI/Rules.ini",
                "INI/Enhance.ini",
                "INI/Firestrm.ini",
                "INI/Art.ini",
                "INI/ArtE.ini",
                "INI/ArtFS.ini",
                "INI/AI.ini",
                "INI/AIE.ini",
                "INI/AIFS.ini"
            },
            ClientType.YR => new string[]
            {
                "spawner.xdp",
                "spawner2.xdp",
                "artmd.ini",
                "soundmd.ini",
                "aimd.ini",
                "shroud.shp",
                "INI/Map Code/Cooperative.ini",
                "INI/Map Code/Free For All.ini",
                "INI/Map Code/Land Rush.ini",
                "INI/Map Code/Meat Grinder.ini",
                "INI/Map Code/Megawealth.ini",
                "INI/Map Code/Naval War.ini",
                "INI/Map Code/Standard.ini",
                "INI/Map Code/Team Alliance.ini",
                "INI/Map Code/Unholy Alliance.ini",
                "INI/Game Options/Allies Allowed.ini",
                "INI/Game Options/Brutal AI.ini",
                "INI/Game Options/No Dog Engi Eat.ini",
                "INI/Game Options/No Spawn Previews.ini",
                "INI/Game Options/RA2 Classic Mode.ini",
                "INI/Map Code/GlobalCode.ini",
                "INI/Map Code/MultiplayerGlobalCode.ini"
            },
            ClientType.Ares => new string[]
            {
                "Ares.dll",
                "Ares.dll.inj",
                "Ares.mix",
                "Syringe.exe",
                "cncnet5.dll",
                "rulesmd.ini",
                "artmd.ini",
                "soundmd.ini",
                "aimd.ini",
                "shroud.shp"
            },
            _ => new string[] { }
        };

        public FileHashCalculator() => ParseConfigFile();

        private string finalHash = string.Empty;

        public void CalculateHashes()
        {
            FileHashes fh = new()
            {
                ClientDefinitionsHash = ResolveLauncherHash(
                    "ClientDefinitionsHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.CLIENT_DEFS))),
                GameOptionsHash = ResolveLauncherHash(
                    "GameOptionsHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.GAME_OPTIONS))),
                ClientDXHash = ResolveLauncherHash(
                    "ClientDXHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "clientdx.exe"))),
                ClientXNAHash = ResolveLauncherHash(
                    "ClientXNAHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "clientxna.exe"))),
                ClientOGLHash = ResolveLauncherHash(
                    "ClientOGLHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "clientogl.exe"))),
                ClientDXNET8Hash = ResolveLauncherHash(
                    "ClientDXNET8Hash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "BinariesNET8", "Windows", "clientdx.dll"))),
                ClientXNANET8Hash = ResolveLauncherHash(
                    "ClientXNANET8Hash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "BinariesNET8", "XNA", "clientxna.dll"))),
                ClientOGLNET8Hash = ResolveLauncherHash(
                    "ClientOGLNET8Hash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "BinariesNET8", "OpenGL", "clientogl.dll"))),
                ClientUGLNET8Hash = ResolveLauncherHash(
                    "ClientUGLNET8Hash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "BinariesNET8", "UniversalGL", "clientogl.dll"))),
                GameExeHash = calculateGameExeHash
                    ? CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.GetGameExecutableName()))
                    : string.Empty,
                LauncherExeHash = CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.GameLauncherExecutableName)),
                MPMapsHash = ResolveLauncherHash(
                    "MPMapsHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.MPMapsIniPath))),
                FHCConfigHash = ResolveLauncherHash(
                    "FHCConfigHash",
                    CalculateSHA1ForFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), CONFIGNAME))),
            };

            Logger.Log($"Hash for {ProgramConstants.BASE_RESOURCE_PATH}\\{ClientConfiguration.CLIENT_DEFS}: {fh.ClientDefinitionsHash}");
            Logger.Log($"Hash for {ProgramConstants.BASE_RESOURCE_PATH}\\{CONFIGNAME}: {fh.FHCConfigHash}");
            Logger.Log($"Hash for {ProgramConstants.BASE_RESOURCE_PATH}\\{ClientConfiguration.GAME_OPTIONS}: {fh.GameOptionsHash}");
            Logger.Log($"Hash for {ProgramConstants.BASE_RESOURCE_PATH}\\clientdx.exe: {fh.ClientDXHash}");
            Logger.Log($"Hash for {ProgramConstants.BASE_RESOURCE_PATH}\\clientxna.exe: {fh.ClientXNAHash}");
            Logger.Log($"Hash for {ProgramConstants.BASE_RESOURCE_PATH}\\clientogl.exe: {fh.ClientOGLHash}");
            Logger.Log($"Hash for ClientDX NET8: {fh.ClientDXNET8Hash}");
            Logger.Log($"Hash for ClientXNA NET8: {fh.ClientXNANET8Hash}");
            Logger.Log($"Hash for ClientOGL NET8: {fh.ClientOGLNET8Hash}");
            Logger.Log($"Hash for ClientUGL NET8: {fh.ClientUGLNET8Hash}");
            Logger.Log($"Hash for {ClientConfiguration.Instance.MPMapsIniPath}: {fh.MPMapsHash}");

            if (calculateGameExeHash)
                Logger.Log($"Hash for {ClientConfiguration.Instance.GetGameExecutableName()}: {fh.GameExeHash}");

            if (!string.IsNullOrEmpty(ClientConfiguration.Instance.GameLauncherExecutableName))
                Logger.Log($"Hash for {ClientConfiguration.Instance.GameLauncherExecutableName}: {fh.LauncherExeHash}");

            foreach (string relativePath in fileNamesToCheck)
            {
                string fullPath = SafePath.CombineFilePath(ProgramConstants.GamePath, relativePath);
                string hash = fh.AddHashForFileIfExists(relativePath, fullPath);
                if (!string.IsNullOrEmpty(hash))
                    Logger.Log($"Hash for {relativePath}: {hash}");
            }

            List<DirectoryInfo> iniPaths = [SafePath.GetDirectory(ProgramConstants.GamePath, "INI", "Game Options")];

            if (ClientConfiguration.Instance.ClientGameType != ClientType.YR)
                iniPaths.Add(SafePath.GetDirectory(ProgramConstants.GamePath, "INI", "Map Code"));

            foreach (DirectoryInfo path in iniPaths)
            {
                if (path.Exists)
                {
                    foreach (string filename in path.EnumerateFiles("*", SearchOption.AllDirectories).Select(s => s.FullName.Substring(path.FullName.Length)))
                    {
                        string fileRelativePath = SafePath.CombineFilePath(path.Name, filename);
                        string fileFullPath = SafePath.CombineFilePath(path.FullName, filename);
                        Debug.Assert(File.Exists(fileFullPath), $"File {fileFullPath} is supposed to but does not exist.");

                        string hash = fh.AddHashForFileIfExists(fileRelativePath, fileFullPath);
                        if (!string.IsNullOrEmpty(hash))
                            Logger.Log("Hash for " + fileRelativePath + ": " + hash);
                    }
                }
            }

            // Add the hashes for each checked file from the available translations

            if (Directory.Exists(ClientConfiguration.Instance.TranslationsFolderPath))
            {
                DirectoryInfo translationsFolderPath = SafePath.GetDirectory(ClientConfiguration.Instance.TranslationsFolderPath);

                List<TranslationGameFile> translationGameFiles = ClientConfiguration.Instance.TranslationGameFiles
                    .Where(tgf => tgf.Checked).ToList();

                foreach (DirectoryInfo translationFolder in translationsFolderPath.EnumerateDirectories())
                {
                    foreach (TranslationGameFile tgf in translationGameFiles)
                    {
                        string fileRelativePath = SafePath.CombineFilePath(translationFolder.Name, tgf.Source);
                        string fileFullPath = SafePath.CombineFilePath(translationFolder.FullName, tgf.Source);

                        string hash = fh.AddHashForFileIfExists(fileRelativePath, fileFullPath);
                        if (!string.IsNullOrEmpty(hash))
                            Logger.Log($"Hash for {fileRelativePath}: {hash}");
                    }
                }
            }

            finalHash = fh.GetFinalHash();
            Logger.Log($"Complete hash: {finalHash}");
        }

        private string ResolveLauncherHash(string key, string computed)
        {
            if (!useReferenceLauncherHashes)
                return computed;

            if (referenceHashes.TryGetValue(key, out string? reference) && !string.IsNullOrEmpty(reference))
            {
                if (!computed.Equals(reference, StringComparison.OrdinalIgnoreCase))
                    Logger.Log($"FHSH: using reference hash for {key} (local={computed})");
                return reference;
            }

            return computed;
        }

        public string GetCompleteHash() => finalHash;

        private void ParseConfigFile()
        {
            IniFile config = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), CONFIGNAME));
            calculateGameExeHash = config.GetBooleanValue("Settings", "CalculateGameExeHash", true);
            useReferenceLauncherHashes = config.GetBooleanValue("Settings", "UseReferenceLauncherHashes", true);

            referenceHashes.Clear();
            foreach (KeyValuePair<string, string> pair in DefaultMgReferenceHashes)
                referenceHashes[pair.Key] = pair.Value;

            List<string>? referenceKeys = config.GetSectionKeys("ReferenceHashes");
            if (referenceKeys != null)
            {
                foreach (string key in referenceKeys)
                {
                    string value = config.GetStringValue("ReferenceHashes", key, string.Empty);
                    if (!string.IsNullOrWhiteSpace(value))
                        referenceHashes[key] = value.Trim();
                }
            }

            List<string> keys = config.GetSectionKeys("FilenameList");
            if (keys == null || keys.Count < 1)
                return;

            List<string> filenames = new List<string>();
            foreach (string key in keys)
            {
                string value = config.GetStringValue("FilenameList", key, string.Empty);
                filenames.Add(value == string.Empty ? key : value);
            }

            fileNamesToCheck = filenames.ToArray();
        }

        private static string NormalizePath(string path) => path.Replace('\\', '/');

        private static string CalculateSHA1ForFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            FileInfo file = SafePath.GetFile(path);
            if (!file.Exists)
                return string.Empty;

            using Stream inputStream = file.OpenRead();

            if (knownTextFileExtensions.Contains(file.Extension, StringComparer.InvariantCultureIgnoreCase))
            {
                // Normalize line endings to LF
                UTF8Encoding utf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

                using StreamReader reader = new(inputStream, utf8Encoding, detectEncodingFromByteOrderMarks: false);
                string text = reader.ReadToEnd();
                text = text.Replace("\r\n", "\n").Trim();

                byte[] bytes = utf8Encoding.GetBytes(text);

                using SHA1 sha1 = SHA1.Create();
                return BytesToString(sha1.ComputeHash(bytes));
            }
            else
            {
                using SHA1 sha1 = SHA1.Create();
                return BytesToString(sha1.ComputeHash(inputStream));
            }
        }

        private static string BytesToString(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private class FileHashes()
        {
            public string ClientDefinitionsHash;
            public string GameOptionsHash;
            public string ClientDXHash;
            public string ClientXNAHash;
            public string ClientOGLHash;
            public string ClientDXNET8Hash;
            public string ClientXNANET8Hash;
            public string ClientOGLNET8Hash;
            public string ClientUGLNET8Hash;
            public string MPMapsHash;
            public string GameExeHash;
            public string LauncherExeHash;
            public string FHCConfigHash;

            public readonly SortedDictionary<string, string> AdditionalFileHashes = new(StringComparer.InvariantCultureIgnoreCase);

            public string AddHashForFileIfExists(string relativePath) =>
                AddHashForFileIfExists(relativePath, relativePath);

            public string AddHashForFileIfExists(string relativePath, string filePath)
            {
                Debug.Assert(!relativePath.StartsWith(ProgramConstants.GamePath), $"File path {relativePath} should be a relative path.");

                string hash = CalculateSHA1ForFile(filePath);
                if (!string.IsNullOrEmpty(hash))
                {
                    AdditionalFileHashes[NormalizePath(relativePath)] = hash;
                    return hash;
                }
                else
                {
                    return string.Empty;
                }
            }

            public string GetFinalHash()
            {
                var sb = new StringBuilder();
                sb.Append(ClientDefinitionsHash);
                sb.Append(GameOptionsHash);
                sb.Append(ClientDXHash);
                sb.Append(ClientXNAHash);
                sb.Append(ClientOGLHash);
                sb.Append(ClientDXNET8Hash);
                sb.Append(ClientXNANET8Hash);
                sb.Append(ClientOGLNET8Hash);
                sb.Append(ClientUGLNET8Hash);
                sb.Append(GameExeHash);
                sb.Append(LauncherExeHash);
                sb.Append(MPMapsHash);
                sb.Append(FHCConfigHash);

                // Append additional file hashes, ordered by key
                foreach (string fileHash in AdditionalFileHashes.Values)
                    sb.Append(fileHash);

                // Merge hashes
                string finalHash = sb.ToString();
                byte[] buffer = Encoding.ASCII.GetBytes(finalHash);
                using SHA1 sha1 = SHA1.Create();
                byte[] hash = sha1.ComputeHash(buffer);
                return BytesToString(hash);
            }
        }
    }
