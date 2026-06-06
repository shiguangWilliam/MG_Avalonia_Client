using ClientCore;
using System.Security.Cryptography;

namespace ClientAvalonia.Core;

public static class FileHashHelper
{
    public static string calCulateHash(string filePath)
    {
        using(var sha256 = SHA256.Create())
        {
            using(var stream = File.OpenRead(filePath))
            {
                var hashBytes = sha256.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    
}