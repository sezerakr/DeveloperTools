using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Models;
using DeveloperTools.Services;

namespace DeveloperTools.ViewModels;

public partial class SettingsPageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;

    public SettingsPageViewModel(SettingsService settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _theme = settings.Current.Theme;
        _defaultViewMode = settings.Current.DefaultViewMode;
        _editorFontSize = settings.Current.EditorFontSize;
        _wordWrap = settings.Current.WordWrap;
        _autoSave = settings.Current.AutoSave;
        foreach (var path in settings.Current.MarkdownFolders)
            Folders.Add(new FolderItem(path, RemoveFolder));
        settings.Changed += (_, _) => OnPropertyChanged(nameof(JsonIndent)); // also changed from the JSON toolbar
    }

    public override string Title => "Settings";
    public override string Subtitle => "General preferences and per-tool settings";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab), nameof(IsMarkdownTab), nameof(IsJsonTab))]
    private string _tab = "General";

    public bool IsGeneralTab
    {
        get => Tab == "General";
        set { if (value) Tab = "General"; }
    }

    public bool IsMarkdownTab
    {
        get => Tab == "Markdown";
        set { if (value) Tab = "Markdown"; }
    }

    public bool IsJsonTab
    {
        get => Tab == "JSON";
        set { if (value) Tab = "JSON"; }
    }

    // JSON tool
    public IReadOnlyList<string> JsonIndents { get; } = ["2 spaces", "4 spaces", "Tab"];

    public string JsonIndent
    {
        get => _settings.Current.JsonIndent switch { 4 => "4 spaces", 0 => "Tab", _ => "2 spaces" };
        set
        {
            var size = value switch { "4 spaces" => 4, "Tab" => 0, _ => 2 };
            if (size == _settings.Current.JsonIndent)
                return;
            _settings.Current.JsonIndent = size;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool DetectClipboard
    {
        get => _settings.Current.DetectClipboard;
        set
        {
            if (value == _settings.Current.DetectClipboard)
                return;
            _settings.Current.DetectClipboard = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool JsonFormatOnPaste
    {
        get => _settings.Current.JsonFormatOnPaste;
        set
        {
            if (value == _settings.Current.JsonFormatOnPaste)
                return;
            _settings.Current.JsonFormatOnPaste = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _autoSave;

    partial void OnWordWrapChanged(bool value)
    {
        _settings.Current.WordWrap = value;
        _settings.Save();
    }

    partial void OnAutoSaveChanged(bool value)
    {
        _settings.Current.AutoSave = value;
        _settings.Save();
    }

    public ObservableCollection<FolderItem> Folders { get; } = new();
    public bool HasNoFolders => Folders.Count == 0;

    public IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];
    public IReadOnlyList<ViewMode> ViewModes { get; } = Enum.GetValues<ViewMode>();
    public IReadOnlyList<int> FontSizes { get; } = [11, 12, 13, 14, 15, 16, 18, 20, 22];

    public string SettingsFile => SettingsService.Location;
    public bool CanRevealSettings => SettingsService.LocalPath is not null && !AppServices.IsBrowser;
    public bool IsBrowser => AppServices.IsBrowser;
    public bool IsDesktop => !AppServices.IsBrowser;

    [ObservableProperty]
    private string? _folderError;

    [ObservableProperty]
    private string _theme;

    [ObservableProperty]
    private ViewMode _defaultViewMode;

    [ObservableProperty]
    private int _editorFontSize;

    partial void OnThemeChanged(string value)
    {
        _settings.Current.Theme = value;
        _settings.Save();
        App.ApplyTheme(value);
    }

    partial void OnDefaultViewModeChanged(ViewMode value)
    {
        _settings.Current.DefaultViewMode = value;
        _settings.Save();
    }

    partial void OnEditorFontSizeChanged(int value)
    {
        _settings.Current.EditorFontSize = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        FolderError = null;
        var path = await _dialogs.PickFolderAsync("Select a markdown folder");
        if (path is null)
            return;

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (_settings.Current.MarkdownFolders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
        {
            FolderError = "That folder is already in the list.";
            return;
        }

        _settings.Current.MarkdownFolders.Add(path);
        _settings.Save();
        Folders.Add(new FolderItem(path, RemoveFolder));
        OnPropertyChanged(nameof(HasNoFolders));
    }

    private void RemoveFolder(FolderItem item)
    {
        FolderError = null;
        _settings.Current.MarkdownFolders.RemoveAll(f => string.Equals(f, item.Path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Folders.Remove(item);
        OnPropertyChanged(nameof(HasNoFolders));
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        if (SettingsService.LocalPath is { } path)
            _dialogs.RevealInExplorer(path);
    }
}

public partial class FolderItem(string path, Action<FolderItem> remove) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path;
    public bool Exists { get; } = Directory.Exists(path);
    public bool IsMissing => !Exists;

    [RelayCommand]
    private void Remove() => remove(this);
}
