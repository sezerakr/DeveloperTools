using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Models;
using DeveloperTools.Services;

namespace DeveloperTools.ViewModels;

public sealed record SearchMatch(int Offset, int Length);

public partial class MarkdownPageViewModel : PageViewModel
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _autoSaveTimer;
    private string _folderSignature = "";
    private bool _suppressDirty;
    private bool _suppressSelection;

    public MarkdownPageViewModel(SettingsService settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _viewMode = settings.Current.DefaultViewMode;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer.Stop();
            if (IsDirty && _settings.Current.AutoSave)
                await SaveCoreAsync();
        };

        Document.TextChanged += (_, _) =>
        {
            if (_suppressDirty)
                return;
            IsDirty = true;
            _previewTimer.Stop();
            _previewTimer.Start();
            if (_settings.Current.AutoSave)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }
        };

        _settings.Changed += async (_, _) =>
        {
            OnPropertyChanged(nameof(EditorFontSize));
            OnPropertyChanged(nameof(WordWrap));
            if (FolderSignature() != _folderSignature)
                await RefreshAsync();
        };
    }

    public override string Title => "Markdown";
    public override string Subtitle => "Browse, preview and edit markdown files";

    public event EventHandler? OpenSettingsRequested;

    public ObservableCollection<FileNode> Roots { get; } = new();
    public TextDocument Document { get; } = new();
    public int EditorFontSize => _settings.Current.EditorFontSize;
    public bool WordWrap => _settings.Current.WordWrap;

    [ObservableProperty]
    private bool _isExplorerVisible = true;

    [RelayCommand]
    private void ToggleExplorer() => IsExplorerVisible = !IsExplorerVisible;
    public bool HasFolders => _settings.Current.MarkdownFolders.Count > 0;
    public bool ShowFolderHint => !HasFolders && !AppServices.IsBrowser;
    public bool ShowBrowserHint => !HasFolders && AppServices.IsBrowser;

    // ---------------------------------------------------------------- files outside the folders

    /// <summary>
    /// Files opened through the file picker that have no local path (browser). Their text lives in a scratch
    /// folder (the browser's in-memory file system) and is written back through the handle on save.
    /// </summary>
    private readonly Dictionary<string, IStorageFile> _handles = new(StringComparer.Ordinal);

    private static readonly string ScratchFolder = Path.Combine(Path.GetTempPath(), "developertools-files");

    [RelayCommand]
    private async Task OpenExternal()
    {
        if (!await ConfirmLeaveAsync())
            return;
        var file = await _dialogs.OpenFileAsync("Open markdown file", "Markdown", "*.md", "*.markdown", "*.mdx", "*.txt");
        if (file is null)
            return;

        if (file.TryGetLocalPath() is { } local)
        {
            IsDirty = false;
            await OpenFileAsync(local);
            return;
        }

        try
        {
            Directory.CreateDirectory(ScratchFolder);
            var path = Path.Combine(ScratchFolder, file.Name);
            await File.WriteAllTextAsync(path, await StorageFiles.ReadTextAsync(file), Utf8NoBom);
            _handles[path] = file;
            IsDirty = false;
            await OpenFileAsync(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusMessage = $"Could not open {file.Name}: {e.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocumentCheck))]
    private async Task SaveAs()
    {
        if (CurrentFilePath is null)
            return;
        var file = await _dialogs.SaveFileAsync("Save markdown as", CurrentFileName ?? "untitled.md", "md", "Markdown");
        if (file is null)
            return;
        try
        {
            await StorageFiles.WriteTextAsync(file, Document.Text);
            if (file.TryGetLocalPath() is { } local)
            {
                IsDirty = false;
                await RefreshAsync();
                await OpenFileAsync(local);
            }
            else
            {
                _handles[CurrentFilePath] = file;
                await File.WriteAllTextAsync(CurrentFilePath, Document.Text, Utf8NoBom);
                IsDirty = false;
            }
            StatusMessage = $"Saved {file.Name}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusMessage = $"Save failed: {e.Message}";
        }
    }

    [ObservableProperty]
    private FileNode? _selectedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(HasNoDocument), nameof(CurrentFileName), nameof(CurrentFileDirectory))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(RevealCommand), nameof(SaveCommand))]
    private string? _currentFilePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewMode), nameof(IsEditMode), nameof(IsSplitMode))]
    private ViewMode _viewMode;

    [ObservableProperty]
    private string _previewSource = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _documentStats = "";

    public bool HasDocument => CurrentFilePath is not null;
    public bool HasNoDocument => CurrentFilePath is null;
    public string? CurrentFileName => Path.GetFileName(CurrentFilePath);
    public string? CurrentFileDirectory => CurrentFilePath is null ? null : Path.GetDirectoryName(CurrentFilePath);

    public bool IsPreviewMode
    {
        get => ViewMode == ViewMode.Preview;
        set { if (value) ViewMode = ViewMode.Preview; }
    }

    public bool IsEditMode
    {
        get => ViewMode == ViewMode.Edit;
        set { if (value) ViewMode = ViewMode.Edit; }
    }

    public bool IsSplitMode
    {
        get => ViewMode == ViewMode.Split;
        set { if (value) ViewMode = ViewMode.Split; }
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        // Leaving the editor: make sure the preview shows the latest text right away.
        if (value != ViewMode.Edit)
            UpdatePreview();
    }

    partial void OnSelectedNodeChanged(FileNode? value)
    {
        if (_suppressSelection || value is not { IsFile: true })
            return;
        if (string.Equals(value.FullPath, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
            return;
        _ = OpenFromSelectionAsync(value.FullPath);
    }

    private async Task OpenFromSelectionAsync(string path)
    {
        try
        {
            await OpenFileAsync(path);
        }
        catch (Exception e)
        {
            // Never let a failure here disappear silently (fire-and-forget call).
            StatusMessage = $"Could not open {Path.GetFileName(path)}: {e.Message}";
            System.Diagnostics.Trace.WriteLine(e);
        }
    }

    // ---------------------------------------------------------------- loading

    public async Task RefreshAsync()
    {
        _folderSignature = FolderSignature();
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(ShowFolderHint));
        OnPropertyChanged(nameof(ShowBrowserHint));

        var expanded = MarkdownTree.Flatten(Roots)
            .Where(n => n.IsDirectory && n.IsExpanded)
            .Select(n => n.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousRoots = Roots.Select(r => r.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        IsLoading = true;
        var folders = _settings.Current.MarkdownFolders.ToList();
        var roots = await Task.Run(() => folders.Select(MarkdownTree.BuildRoot).ToList());
        IsLoading = false;

        _suppressSelection = true;
        Roots.Clear();
        foreach (var root in roots)
        {
            foreach (var dir in MarkdownTree.Flatten([root]).Where(n => n.IsDirectory))
                // New roots start expanded; everything else keeps its previous state.
                dir.IsExpanded = expanded.Contains(dir.FullPath) || (dir.IsRoot && !previousRoots.Contains(dir.FullPath));
            Roots.Add(root);
        }

        SelectedNode = CurrentFilePath is null ? null : FindNode(CurrentFilePath);
        _suppressSelection = false;

        var fileCount = MarkdownTree.Flatten(Roots).Count(n => n.IsFile);
        StatusMessage = HasFolders ? $"{fileCount} markdown file{(fileCount == 1 ? "" : "s")} in {Roots.Count} folder{(Roots.Count == 1 ? "" : "s")}" : null;
    }

    public async Task<bool> OpenFileAsync(string path)
    {
        if (!await ConfirmLeaveAsync())
        {
            RestoreSelection();
            return false;
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception e)
        {
            StatusMessage = $"Could not open {Path.GetFileName(path)}: {e.Message}";
            RestoreSelection();
            return false;
        }

        _suppressDirty = true;
        Document.Text = text;
        Document.UndoStack.ClearAll();
        _suppressDirty = false;

        CurrentFilePath = path;
        IsDirty = false;
        UpdatePreview();
        StatusMessage = null;

        var node = FindNode(path);
        if (node is not null)
        {
            ExpandTo(node);
            _suppressSelection = true;
            SelectedNode = node;
            _suppressSelection = false;
        }

        return true;
    }

    /// <summary>Asks to save pending changes. Returns false when the user cancels.</summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsDirty || CurrentFileName is null)
            return true;

        return await _dialogs.ConfirmUnsavedAsync(CurrentFileName) switch
        {
            UnsavedChoice.Save => await SaveCoreAsync(),
            UnsavedChoice.Discard => true,
            _ => false
        };
    }

    // ---------------------------------------------------------------- commands

    private bool CanSave() => HasDocument && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => SaveCoreAsync();

    private async Task<bool> SaveCoreAsync()
    {
        if (CurrentFilePath is null)
            return false;
        try
        {
            await File.WriteAllTextAsync(CurrentFilePath, Document.Text, Utf8NoBom);
            if (_handles.TryGetValue(CurrentFilePath, out var handle))
                await StorageFiles.WriteTextAsync(handle, Document.Text); // browser: back to the picked file
            else if (AppServices.IsBrowser)
            {
                // Not linked to a file on the computer yet: ask where to save it.
                IsDirty = false;
                await SaveAs();
                return !IsDirty;
            }
            IsDirty = false;
            StatusMessage = $"Saved {DateTime.Now:HH:mm:ss}";
            return true;
        }
        catch (Exception e)
        {
            StatusMessage = $"Save failed: {e.Message}";
            return false;
        }
    }

    [RelayCommand]
    private async Task NewFile()
    {
        if (!HasFolders && !AppServices.IsBrowser)
        {
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (AppServices.IsBrowser)
            Directory.CreateDirectory(ScratchFolder);
        var folder = SelectedNode?.DirectoryPath
                     ?? CurrentFileDirectory
                     ?? Roots.FirstOrDefault(r => !r.IsMissing)?.FullPath
                     ?? (AppServices.IsBrowser ? ScratchFolder : null);
        if (folder is null || !Directory.Exists(folder))
        {
            StatusMessage = "None of the configured folders exist.";
            return;
        }

        if (!await ConfirmLeaveAsync())
            return;

        var name = await _dialogs.PromptAsync(
            "New markdown file",
            AppServices.IsBrowser ? "Use Save to store it on your computer." : $"Create in {folder}",
            "untitled.md",
            "Create",
            input => ValidateNewFileName(folder, input));
        if (name is null)
            return;

        var path = Path.Combine(folder, NormalizeFileName(name));
        var title = Path.GetFileNameWithoutExtension(path).Replace('-', ' ').Replace('_', ' ');
        try
        {
            await File.WriteAllTextAsync(path, $"# {title}\n\n", Utf8NoBom);
        }
        catch (Exception e)
        {
            StatusMessage = $"Could not create file: {e.Message}";
            return;
        }

        IsDirty = false; // already confirmed above
        await RefreshAsync();
        if (await OpenFileAsync(path) && ViewMode == ViewMode.Preview)
            ViewMode = ViewMode.Split;
    }

    private bool HasDocumentCheck() => HasDocument;

    [RelayCommand(CanExecute = nameof(HasDocumentCheck))]
    private async Task Delete()
    {
        if (CurrentFilePath is not { } path)
            return;

        var recycle = OperatingSystem.IsWindows();
        var ok = await _dialogs.ConfirmAsync(
            "Delete file",
            recycle
                ? $"Move \"{Path.GetFileName(path)}\" to the Recycle Bin?"
                : $"Permanently delete \"{Path.GetFileName(path)}\"? This cannot be undone.",
            "Delete",
            danger: true);
        if (!ok)
            return;

        try
        {
            if (recycle)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                File.Delete(path);
        }
        catch (Exception e)
        {
            StatusMessage = $"Delete failed: {e.Message}";
            return;
        }

        CloseDocument();
        await RefreshAsync();
        StatusMessage = $"Deleted {Path.GetFileName(path)}";
    }

    [RelayCommand(CanExecute = nameof(HasDocumentCheck))]
    private void Reveal()
    {
        if (CurrentFilePath is not null)
            _dialogs.RevealInExplorer(CurrentFilePath);
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- search (Ctrl+F)

    private readonly List<int> _matches = new();
    private int _currentMatch = -1;

    /// <summary>Raised when the active match changes, so the view can select / scroll to it.</summary>
    public event EventHandler<SearchMatch>? MatchSelected;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _searchStatus = "";

    /// <summary>Term the preview should highlight (only while the find bar is open).</summary>
    public string? ActiveSearchTerm => IsSearchOpen && SearchText.Length > 0 ? SearchText : null;

    partial void OnSearchTextChanged(string value)
    {
        RecomputeMatches();
        _currentMatch = -1;
        OnPropertyChanged(nameof(ActiveSearchTerm));
        if (_matches.Count > 0)
            MoveMatch(+1);
    }

    partial void OnIsSearchOpenChanged(bool value) => OnPropertyChanged(nameof(ActiveSearchTerm));

    [RelayCommand]
    private void OpenSearch()
    {
        if (!HasDocument)
            return;
        IsSearchOpen = true;
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks the view to focus the find box (also when it is already open).</summary>
    public event EventHandler? SearchFocusRequested;

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchText = "";
        UpdateSearchStatus(); // clears the scrollbar markers
    }

    [RelayCommand]
    private void FindNext() => MoveMatch(+1);

    [RelayCommand]
    private void FindPrevious() => MoveMatch(-1);

    private void MoveMatch(int direction)
    {
        if (_matches.Count == 0)
        {
            UpdateSearchStatus();
            return;
        }

        _currentMatch = _currentMatch < 0
            ? (direction > 0 ? 0 : _matches.Count - 1)
            : (_currentMatch + direction + _matches.Count) % _matches.Count;
        UpdateSearchStatus();
        MatchSelected?.Invoke(this, new SearchMatch(_matches[_currentMatch], SearchText.Length));
    }

    private void RecomputeMatches()
    {
        _matches.Clear();
        if (SearchText.Length > 0)
        {
            var text = Document.Text;
            var index = 0;
            while ((index = text.IndexOf(SearchText, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                _matches.Add(index);
                index += SearchText.Length;
            }
        }

        if (_currentMatch >= _matches.Count)
            _currentMatch = _matches.Count - 1;
        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        SearchStatus = SearchText.Length == 0 ? ""
            : _matches.Count == 0 ? "No results"
            : _currentMatch < 0 ? $"{_matches.Count} results"
            : $"{_currentMatch + 1} of {_matches.Count}";
        SearchResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Offsets of all matches (in the document text), for scrollbar markers.</summary>
    public IReadOnlyList<int> Matches => _matches;

    public int CurrentMatchIndex => _currentMatch;

    /// <summary>Raised whenever the match list or the current match changes.</summary>
    public event EventHandler? SearchResultsChanged;

    /// <summary>Flips a task checkbox ("[ ]" / "[x]") at the given source offset.</summary>
    public void ToggleTask(int offset)
    {
        // The preview may lag behind the editor; only touch the text if it is still a task box.
        if (offset < 0 || offset + 2 >= Document.TextLength
            || Document.GetCharAt(offset) != '[' || Document.GetCharAt(offset + 2) != ']')
            return;

        var mark = Document.GetCharAt(offset + 1);
        Document.Replace(offset + 1, 1, mark == ' ' ? "x" : " ");
        _previewTimer.Stop();
        UpdatePreview();
    }

    /// <summary>Handles a link clicked in the preview.</summary>
    public async Task FollowLinkAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https" or "mailto")
        {
            await _dialogs.OpenUrlAsync(url);
            return;
        }

        if (CurrentFileDirectory is null)
            return;

        var target = url.Split('#', '?')[0];
        if (target.Length == 0)
            return;
        var path = Path.GetFullPath(Path.Combine(CurrentFileDirectory, Uri.UnescapeDataString(target)));

        if (File.Exists(path) && MarkdownTree.IsMarkdown(path))
            await OpenFileAsync(path);
        else if (File.Exists(path) || Directory.Exists(path))
            _dialogs.RevealInExplorer(path);
        else
            StatusMessage = $"Link target not found: {target}";
    }

    // ---------------------------------------------------------------- helpers

    private void UpdatePreview()
    {
        var text = Document.Text;
        PreviewSource = text;
        if (IsSearchOpen)
            RecomputeMatches(); // keep counts right while editing

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        DocumentStats = HasDocument ? $"{words:N0} words · {Document.LineCount:N0} lines" : "";
    }

    private void CloseDocument()
    {
        _suppressDirty = true;
        Document.Text = "";
        Document.UndoStack.ClearAll();
        _suppressDirty = false;
        CurrentFilePath = null;
        IsDirty = false;
        UpdatePreview();
    }

    private void RestoreSelection()
    {
        _suppressSelection = true;
        SelectedNode = CurrentFilePath is null ? null : FindNode(CurrentFilePath);
        _suppressSelection = false;
    }

    private FileNode? FindNode(string path) =>
        MarkdownTree.Flatten(Roots).FirstOrDefault(n => string.Equals(n.FullPath, path, StringComparison.OrdinalIgnoreCase));

    private static void ExpandTo(FileNode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;
    }

    private string FolderSignature() => string.Join("|", _settings.Current.MarkdownFolders);

    private static string NormalizeFileName(string name) =>
        MarkdownTree.IsMarkdown(name) ? name : name + ".md";

    private static string? ValidateNewFileName(string folder, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Enter a file name.";
        if (input.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "The name contains characters that are not allowed in file names.";
        if (File.Exists(Path.Combine(folder, NormalizeFileName(input))))
            return "A file with that name already exists.";
        return null;
    }
}
