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
    private const string Name = "snippets.json";

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
        AppServices.Storage.Write(Name, JsonSerializer.Serialize(_all, AppJsonContext.Default.ListSnippet));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<Snippet> Load()
    {
        string? text = null;
        try
        {
            text = AppServices.Storage.Read(Name);
            if (text is not null)
                return JsonSerializer.Deserialize(text, AppJsonContext.Default.ListSnippet) ?? new();
        }
        catch (Exception)
        {
            try { if (text is not null) AppServices.Storage.Write(Name + ".bak", text); } catch { /* best effort */ }
        }
        return new();
    }
}
