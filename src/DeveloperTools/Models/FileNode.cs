using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DeveloperTools.Models;

/// <summary>A folder or markdown file in the explorer tree.</summary>
public partial class FileNode : ObservableObject
{
    public FileNode(string fullPath, bool isDirectory, FileNode? parent = null)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Parent = parent;
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Name = string.IsNullOrEmpty(name) ? fullPath : name;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsFile => !IsDirectory;
    public FileNode? Parent { get; }
    public bool IsRoot => Parent is null;
    public bool IsMissing { get; set; }

    public ObservableCollection<FileNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    public string DirectoryPath => IsDirectory ? FullPath : Path.GetDirectoryName(FullPath)!;
}
