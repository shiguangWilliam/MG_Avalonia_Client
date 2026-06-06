using System.Security.Cryptography;

namespace ClientAvalonia.Core;

public static class FileHashHelper
{
    public static string CalculateSha256Hash(string filePath)
    {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(filePath);
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    [Obsolete("Use CalculateSha256Hash.")]
    public static string calCulateHash(string filePath) => CalculateSha256Hash(filePath);
}
