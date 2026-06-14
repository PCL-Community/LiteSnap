namespace LiteSnap.Core.Models;

public class FileVersionObjects
{
    public string Path { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public long Length { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public ObjectType ObjectType { get; set; }
}
