using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Localization;
using DeveloperTools.Services;

namespace DeveloperTools.ViewModels;

/// <summary>A tool in the app (navigation, Home cards, favorites).</summary>
public sealed record ToolInfo(string Key, string Name, string Description, string Icon);

/// <summary>A card on the Home page.</summary>
public partial class ToolCard(ToolInfo tool, bool isFavorite, Action<ToolCard> open, Action<ToolCard> toggle) : ObservableObject
{
    public ToolInfo Tool { get; } = tool;
    public string Name => Tool.Name;
    public string Description => Tool.Description;
    public string Icon => Tool.Icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StarTip))]
    private bool _isFavorite = isFavorite;

    public string StarTip => IsFavorite ? Loc.T("Remove from favorites") : Loc.T("Add to favorites");

    [RelayCommand]
    private void Open() => open(this);

    [RelayCommand]
    private void ToggleFavorite() => toggle(this);
}

/// <summary>Home: the user's favorite tools as large cards (or every tool to pick from when there are none).</summary>
public partial class HomePageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly IReadOnlyList<ToolInfo> _tools;
    private readonly Action<string> _navigate;

    public HomePageViewModel(SettingsService settings, IReadOnlyList<ToolInfo> tools, Action<string> navigate)
    {
        _settings = settings;
        _tools = tools;
        _navigate = navigate;
        Rebuild();
    }

    public override string Title => Loc.T("Home");
    public override string Subtitle => Loc.T("Your favorite tools");

    public ObservableCollection<ToolCard> Favorites { get; } = new();
    public ObservableCollection<ToolCard> Others { get; } = new();

    public bool HasFavorites => Favorites.Count > 0;
    public bool HasNoFavorites => Favorites.Count == 0;

    /// <summary>Show the other tools under the favorites (always on when there are no favorites yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOthers))]
    private bool _isEditing;

    public bool ShowOthers => IsEditing || HasNoFavorites;

    public bool IsFavorite(string key) => _settings.Current.FavoriteTools.Contains(key);

    /// <summary>Adds or removes a tool (also used by the star in the header).</summary>
    public void Toggle(string key)
    {
        var list = _settings.Current.FavoriteTools;
        if (!list.Remove(key))
            list.Add(key);
        _settings.Save();
        Rebuild();
    }

    /// <summary>Raised when favorites change (the header star refreshes).</summary>
    public event EventHandler? FavoritesChanged;

    private void Rebuild()
    {
        var favorites = _settings.Current.FavoriteTools;
        Favorites.Clear();
        Others.Clear();
        foreach (var key in favorites)
            if (_tools.FirstOrDefault(t => t.Key == key) is { } tool)
                Favorites.Add(Card(tool, true));
        foreach (var tool in _tools.Where(t => !favorites.Contains(t.Key)))
            Others.Add(Card(tool, false));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasNoFavorites));
        OnPropertyChanged(nameof(ShowOthers));
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    private ToolCard Card(ToolInfo tool, bool favorite) =>
        new(tool, favorite, c => _navigate(c.Tool.Key), c => Toggle(c.Tool.Key));

    [RelayCommand]
    private void ToggleEditing() => IsEditing = !IsEditing;
}
