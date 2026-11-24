using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Models;
using DeveloperTools.Services;

namespace DeveloperTools.ViewModels;

/// <summary>Save/load named inputs for one tool. The page supplies how to read and restore its state.</summary>
public partial class SnippetsViewModel : ObservableObject
{
    private readonly SnippetService _service;
    private readonly string _tool;
    private readonly Func<string> _capture;
    private readonly Action<string> _restore;

    public SnippetsViewModel(SnippetService service, string tool, Func<string> capture, Action<string> restore)
    {
        _service = service;
        _tool = tool;
        _capture = capture;
        _restore = restore;
        service.Changed += (_, _) => Reload();
        Reload();
    }

    public ObservableCollection<SnippetItem> Items { get; } = new();
    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => Items.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _newName = "";

    /// <summary>Raised after a snippet was loaded (the view closes the flyout).</summary>
    public event EventHandler? Loaded;

    private void Reload()
    {
        Items.Clear();
        foreach (var s in _service.For(_tool))
            Items.Add(new SnippetItem(s, Load, Delete));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
    }

    private bool CanSave() => NewName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _service.Add(_tool, NewName.Trim(), _capture());
        NewName = "";
    }

    private void Load(SnippetItem item)
    {
        _restore(item.Snippet.Content);
        Loaded?.Invoke(this, EventArgs.Empty);
    }

    private void Delete(SnippetItem item) => _service.Remove(item.Snippet.Id);
}

public partial class SnippetItem(Snippet snippet, Action<SnippetItem> load, Action<SnippetItem> delete)
{
    public Snippet Snippet { get; } = snippet;
    public string Name => Snippet.Name;
    public string Info => Snippet.Created.ToString("d MMM yyyy, HH:mm") + "  ·  " + Preview;
    private string Preview
    {
        get
        {
            var flat = Snippet.Content.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length > 40 ? flat[..40] + "…" : flat;
        }
    }

    [RelayCommand]
    private void Load() => load(this);

    [RelayCommand]
    private void Delete() => delete(this);
}
