using System.Collections.ObjectModel;

namespace LiteSnap.Core.Models;

public class FileTreeNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public ObservableCollection<FileTreeNode> Children { get; set; } = [];
}
