using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeveloperTools.Models;

public sealed class AppSettings
{
    // General
    public string Theme { get; set; } = "System";
    public bool DetectClipboard { get; set; } = true;
    public string Language { get; set; } = "System";

    /// <summary>Tools shown on the Home page ("json", "diff", …), in the order they were added.</summary>
    public List<string> FavoriteTools { get; set; } = new();

    // Updates (desktop): checked against GitHub releases at most once a day
    public bool AutoUpdate { get; set; } = true;
    public System.DateTime? LastUpdateCheck { get; set; }

    // Desktop window placement (position in screen pixels, size in device-independent pixels)
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // Markdown tool
    public List<string> MarkdownFolders { get; set; } = new();
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Preview;
    public int EditorFontSize { get; set; } = 14;
    public bool WordWrap { get; set; } = true;
    public bool AutoSave { get; set; }

    // JSON tool
    /// <summary>Spaces per level; 0 means tabs.</summary>
    public int JsonIndent { get; set; } = 2;
    public bool JsonFormatOnPaste { get; set; } = true;
}

/// <summary>A named piece of input saved by the user for a tool (stored locally).</summary>
public sealed class Snippet
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Tool { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public System.DateTime Created { get; set; } = System.DateTime.Now;
}

public enum ViewMode
{
    Preview,
    Edit,
    Split
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<Snippet>))]
internal partial class AppJsonContext : JsonSerializerContext;
