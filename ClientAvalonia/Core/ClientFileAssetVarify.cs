using ClientCore;

namespace ClientAvalonia.Core;
public sealed class FileAssetVarify
{
    private string _filePath;
    private string _fileHash;

    public FileAssetVarify(string filePath, string fileHash)
    {
        _filePath = filePath;
        _fileHash = fileHash;
    }

    public string FilePath => _filePath;
    public string FileHash => _fileHash;

    public bool Verify()
    {
        // Implement file hash verification logic here.
        // This is a placeholder implementation and should be replaced with actual hash checking code.
        return FileHashHelper.calCulateHash(FilePath) == FileHash;
    }
}