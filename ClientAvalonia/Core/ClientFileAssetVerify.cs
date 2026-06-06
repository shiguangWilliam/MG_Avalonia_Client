namespace ClientAvalonia.Core;

public sealed class ClientFileAssetVerify
{
    public ClientFileAssetVerify(string relativePath, string expectedSha256Hash)
    {
        RelativePath = relativePath.Replace('\\', '/');
        ExpectedSha256Hash = NormalizeHash(expectedSha256Hash);
    }

    public string RelativePath { get; }

    public string ExpectedSha256Hash { get; }

    public bool Verify(string installRoot, out string? actualHash, out string? error)
    {
        actualHash = null;
        error = null;

        string fullPath = Path.Combine(installRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            error = "missing";
            return false;
        }

        try
        {
            actualHash = FileHashHelper.CalculateSha256Hash(fullPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!string.Equals(actualHash, ExpectedSha256Hash, StringComparison.OrdinalIgnoreCase))
        {
            error = "hash mismatch";
            return false;
        }

        return true;
    }

    private static string NormalizeHash(string hash)
        => hash.Trim().Replace("-", string.Empty).ToLowerInvariant();
}
