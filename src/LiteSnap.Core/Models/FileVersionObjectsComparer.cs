namespace LiteSnap.Core.Models;

public class FileVersionObjectsComparer : IEqualityComparer<FileVersionObjects>
{
    public static readonly FileVersionObjectsComparer Instance = new();

    public bool Equals(FileVersionObjects? x, FileVersionObjects? y)
    {
        if (x is null || y is null) return false;
        return x.Hash == y.Hash || x.Path == y.Path;
    }

    public int GetHashCode(FileVersionObjects obj)
    {
        return obj.Hash.GetHashCode();
    }
}
