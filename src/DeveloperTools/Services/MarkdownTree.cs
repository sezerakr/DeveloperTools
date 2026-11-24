using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeveloperTools.Models;

namespace DeveloperTools.Services;

/// <summary>Builds explorer trees that only contain folders holding markdown files.</summary>
public static class MarkdownTree
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "dist", "target"
    };

    public static bool IsMarkdown(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }

    public static FileNode BuildRoot(string path)
    {
        var root = new FileNode(path, isDirectory: true);
        if (!Directory.Exists(path))
        {
            root.IsMissing = true;
            return root;
        }

        Fill(root);
        return root;
    }

    /// <returns>True when the folder contains at least one markdown file (directly or nested).</returns>
    private static bool Fill(FileNode node)
    {
        List<string> dirs, files;
        try
        {
            var info = new DirectoryInfo(node.FullPath);
            dirs = info.EnumerateDirectories()
                .Where(d => !d.Name.StartsWith('.')
                            && !IgnoredDirectories.Contains(d.Name)
                            && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)
                            && !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.FullName)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
            files = info.EnumerateFiles()
                .Where(f => IsMarkdown(f.Name))
                .Select(f => f.FullName)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return false;
        }

        foreach (var dir in dirs)
        {
            var child = new FileNode(dir, isDirectory: true, node);
            if (Fill(child))
                node.Children.Add(child);
        }

        foreach (var file in files)
            node.Children.Add(new FileNode(file, isDirectory: false, node));

        return node.Children.Count > 0;
    }

    public static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }
}
