namespace LiteSnap.Core.Models;

public class VersionData
{
    public string NodeId { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Desc { get; set; } = string.Empty;
    public long Version { get; set; }
}
