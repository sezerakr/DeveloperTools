using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeveloperTools.Models;

namespace DeveloperTools.Services;

/// <summary>Saved snippets in %AppData%\DeveloperTools\snippets.json (this computer only).</summary>
public sealed class SnippetService
{
    public static readonly string File = Path.Combine(SettingsService.SettingsDirectory, "snippets.json");

    private readonly List<Snippet> _all;

    public SnippetService()
    {
        _all = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<Snippet> For(string tool) =>
        _all.Where(s => s.Tool == tool).OrderByDescending(s => s.Created).ToList();

    public void Add(string tool, string name, string content)
    {
        // Same name replaces the older one.
        _all.RemoveAll(s => s.Tool == tool && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _all.Add(new Snippet { Tool = tool, Name = name, Content = content });
        Save();
    }

    public void Remove(string id)
    {
        _all.RemoveAll(s => s.Id == id);
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(SettingsService.SettingsDirectory);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(_all, AppJsonContext.Default.ListSnippet));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<Snippet> Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return JsonSerializer.Deserialize(System.IO.File.ReadAllText(File), AppJsonContext.Default.ListSnippet) ?? new();
        }
        catch (Exception)
        {
            try { System.IO.File.Copy(File, File + ".bak", overwrite: true); } catch { /* best effort */ }
        }
        return new();
    }
}
