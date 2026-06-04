using System.Buffers.Binary;
using Avalonia.Media.Imaging;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Resolves theme-relative texture paths (MainMenu/foo.png) against XNA asset search paths.</summary>
public sealed class ResourceResolver
{
    private readonly List<string> _searchRoots = [];

    public ResourceResolver(string? primaryRoot = null)
    {
        if (!string.IsNullOrEmpty(primaryRoot))
            AddSearchRoot(primaryRoot);
    }

    public IReadOnlyList<string> SearchRoots => _searchRoots;

    public void AddSearchRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full) && !_searchRoots.Contains(full, StringComparer.OrdinalIgnoreCase))
            _searchRoots.Add(full);
    }

    public void ConfigureForGame(ClientEnvironment environment)
    {
        _searchRoots.Clear();
        foreach (string path in environment.GetAssetSearchPaths())
            AddSearchRoot(path);
    }

    public void ConfigureFromIniPath(string iniPath)
    {
        string? gameRoot = Path.GetDirectoryName(Path.GetFullPath(iniPath));
        if (gameRoot != null)
            ConfigureForGame(ClientEnvironment.Discover(gameRoot));
    }

    public string? ResolveTexturePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        relativePath = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (string root in _searchRoots)
        {
            string candidate = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;

            string? caseInsensitive = FindFileCaseInsensitive(candidate);
            if (caseInsensitive != null)
                return caseInsensitive;
        }

        return null;
    }

    /// <summary>Finds a file by name across search roots (case-insensitive, shallow scan per root).</summary>
    public string? ResolveFileByName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        foreach (string root in _searchRoots)
        {
            string? found = FindFileCaseInsensitive(Path.Combine(root, fileName));
            if (found != null)
                return found;
        }

        return null;
    }

    public (int Width, int Height)? GetTextureSize(string? relativePath)
    {
        string? full = ResolveTexturePath(relativePath);
        if (full == null)
            return null;

        if (TryReadPngDimensions(full, out int pngW, out int pngH))
            return (pngW, pngH);

        try
        {
            using var stream = File.OpenRead(full);
            using var bitmap = new Bitmap(stream);
            return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }
        catch
        {
            return null;
        }
    }

    public string? ResolveFirstExisting(IEnumerable<string> relativePaths)
    {
        foreach (string relativePath in relativePaths)
        {
            string? resolved = ResolveTexturePath(relativePath);
            if (resolved != null)
                return relativePath.Replace('\\', '/');
        }

        return null;
    }

    public Bitmap? LoadBitmap(string? relativePath)
    {
        string? full = ResolveTexturePath(relativePath);
        if (full == null && !string.IsNullOrWhiteSpace(relativePath) && !relativePath.Contains('/'))
            full = ResolveFileByName(relativePath);

        if (full == null)
            return null;

        return LoadBitmapFromFullPath(full);
    }

    public Bitmap? LoadFirstBitmap(IEnumerable<string> relativePaths)
    {
        foreach (string relativePath in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            Bitmap? bitmap = LoadBitmap(relativePath);
            if (bitmap != null)
                return bitmap;
        }

        return null;
    }

    private static Bitmap? LoadBitmapFromFullPath(string full)
    {
        try
        {
            return new Bitmap(full);
        }
        catch
        {
            try
            {
                using var stream = File.OpenRead(full);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }
    }

    private static string? FindFileCaseInsensitive(string candidatePath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(candidatePath);
            string fileName = Path.GetFileName(candidatePath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                return null;

            if (!Directory.Exists(directory))
                return null;

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (fileName.Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch
        {
            // Ignore unreadable directories.
        }

        return null;
    }

    private static bool TryReadPngDimensions(string path, out int width, out int height)
    {
        width = height = 0;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            Span<byte> header = stackalloc byte[24];
            using FileStream stream = File.OpenRead(path);
            if (stream.Read(header) < 24)
                return false;

            if (header[0] != 0x89 || header[1] != (byte)'P' || header[2] != (byte)'N' || header[3] != (byte)'G')
                return false;

            width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }
}
