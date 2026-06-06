using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

public sealed class ClientFileIntegrityResult
{
    public bool Success { get; init; }

    public bool Skipped { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> FailedRelativePaths { get; init; } = [];

    public static ClientFileIntegrityResult SkippedResult(string reason)
        => new() { Success = true, Skipped = true, Message = reason };

    public static ClientFileIntegrityResult Passed()
        => new() { Success = true };

    public static ClientFileIntegrityResult Failed(IReadOnlyList<string> failedPaths, string message)
        => new()
        {
            Success = false,
            FailedRelativePaths = failedPaths,
            Message = message,
        };
}

public static class ClientFileIntegrityService
{
    public static ClientFileIntegrityResult Verify(
        FileSystemManager? fileSystem = null,
        string? manifestRelativePath = null,
        Action<string>? reportStatus = null)
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return ClientFileIntegrityResult.SkippedResult("ClientCore is not initialized.");

        fileSystem ??= new FileSystemManager();
        string installRoot = fileSystem.InstallRoot;

        reportStatus?.Invoke("正在更新安装路径注册表...");
        InstallationRegistry.TryUpdateInstallPath(installRoot);

        string manifestPath = fileSystem.ResolveManifestPath(manifestRelativePath);
        ClientFileIntegrityManifest? manifest = ClientFileIntegrityManifest.TryLoad(manifestPath);
        if (manifest == null)
        {
            Logger.Log($"ClientFileIntegrity: manifest not found at {manifestPath}; skipping verification.");
            return ClientFileIntegrityResult.SkippedResult($"Manifest not found: {manifestPath}");
        }

        if (!manifest.Enabled)
        {
            Logger.Log("ClientFileIntegrity: manifest disabled; skipping verification.");
            return ClientFileIntegrityResult.SkippedResult("Integrity verification disabled in manifest.");
        }

        if (manifest.FileEntries.Count == 0 && manifest.DirectoryEntries.Count == 0)
        {
            Logger.Log("ClientFileIntegrity: manifest has no entries; skipping verification.");
            return ClientFileIntegrityResult.SkippedResult("Integrity manifest has no entries.");
        }

        reportStatus?.Invoke("正在校验安装文件...");
        var failed = new List<string>();
        var expectedByPath = manifest.FileEntries.ToDictionary(
            e => e.RelativePath,
            e => e,
            StringComparer.OrdinalIgnoreCase);

        foreach (ClientFileAssetVerify entry in manifest.FileEntries)
        {
            if (!entry.Verify(installRoot, out _, out _))
                failed.Add(entry.RelativePath);
        }

        foreach ((string directoryRelativePath, IntegrityDirectoryScanMode mode) in manifest.DirectoryEntries)
        {
            reportStatus?.Invoke($"正在校验 {directoryRelativePath}...");
            VerifyDirectoryCoverage(
                installRoot,
                directoryRelativePath,
                mode,
                expectedByPath,
                failed);
        }

        if (failed.Count == 0)
            return ClientFileIntegrityResult.Passed();

        string failedList = string.Join(Environment.NewLine, failed.Take(12));
        if (failed.Count > 12)
            failedList += Environment.NewLine + $"... and {failed.Count - 12} more";

        string message =
            "当前安装目录下检测到文件被篡改或损坏，请重新安装或移除不当文件。" +
            Environment.NewLine + Environment.NewLine +
            failedList;

        Logger.Log($"ClientFileIntegrity: verification failed for {failed.Count} path(s).");
        return ClientFileIntegrityResult.Failed(failed, message);
    }

    private static void VerifyDirectoryCoverage(
        string installRoot,
        string directoryRelativePath,
        IntegrityDirectoryScanMode mode,
        IReadOnlyDictionary<string, ClientFileAssetVerify> expectedByPath,
        List<string> failed)
    {
        string directoryFullPath = SafePath.CombineDirectoryPath(
            installRoot,
            directoryRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(directoryFullPath))
        {
            AddFailure(directoryRelativePath, failed);
            return;
        }

        SearchOption searchOption = mode == IntegrityDirectoryScanMode.Recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        string prefix = directoryRelativePath.TrimEnd('/') + "/";
        var onDiskRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string fileFullPath in Directory.EnumerateFiles(directoryFullPath, "*", searchOption))
        {
            string relative = prefix + fileFullPath[directoryFullPath.Length..].TrimStart('\\', '/').Replace('\\', '/');
            onDiskRelativePaths.Add(relative);

            if (!expectedByPath.TryGetValue(relative, out ClientFileAssetVerify? expected))
            {
                AddFailure(relative + " (unexpected file)", failed);
                continue;
            }

            if (!expected.Verify(installRoot, out _, out _))
                AddFailure(relative, failed);
        }

        foreach (string expectedRelativePath in expectedByPath.Keys)
        {
            if (!expectedRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!onDiskRelativePaths.Contains(expectedRelativePath))
                AddFailure(expectedRelativePath + " (missing)", failed);
        }
    }

    private static void AddFailure(string path, List<string> failed)
    {
        if (!failed.Contains(path, StringComparer.OrdinalIgnoreCase))
            failed.Add(path);
    }
}
