// Fallback when GitVersion.MsBuild is disabled (standalone Avalonia / no .git builds).

public static class GitVersionInformation
{
    public const string AssemblySemVer = "0.0.0-local";

    public const string InformationalVersion = "0.0.0-local";

    public const string BranchName = "local";

    public const string CommitDate = "0001-01-01";

    public const string ShortSha = "local";
}
