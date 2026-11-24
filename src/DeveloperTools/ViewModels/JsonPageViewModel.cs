using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

namespace DeveloperTools.ViewModels;

public partial class JsonPageViewModel : PageViewModel
{
    private const long MaxFileBytes = 100L * 1024 * 1024;

    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _validateTimer;
    private bool _programmatic;
    private int _version;

    public JsonPageViewModel(SettingsService settings, IDialogService dialogs, SnippetService snippets)
    {
        _settings = settings;
        _dialogs = dialogs;
        _selectedIndent = IndentName(settings.Current.JsonIndent);

        Document = new TextDocument();
        Panel = new JsonPanelViewModel(dialogs, () => Document.Text);
        Snippets = new SnippetsViewModel(snippets, "json", () => Document.Text, t => Replace(t));
        Document.Changed += OnDocumentChanged;
        Document.UndoStack.PropertyChanged += OnUndoStackChanged;

        _validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _validateTimer.Tick += (_, _) =>
        {
            _validateTimer.Stop();
            Validate();
        };

        settings.Changed += (_, _) =>
        {
            SelectedIndent = IndentName(settings.Current.JsonIndent);
            OnPropertyChanged(nameof(EditorFontSize));
        };
    }

    public override string Title => "JSON";
    public override string Subtitle => "Format, validate and repair JSON";

    public TextDocument Document { get; }
    public JsonPanelViewModel Panel { get; }
    public SnippetsViewModel Snippets { get; }

    public int EditorFontSize => _settings.Current.EditorFontSize;

    /// <summary>Raised to move the caret to a character offset (an error position).</summary>
    public event EventHandler<int>? GoToRequested;

    // ------------------------------------------------------------------ validation state

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsInvalid), nameof(IsEmpty), nameof(StatusLabel), nameof(StatusText))]
    private JsonCheck _check = JsonCheck.Empty;

    public bool IsValid => Check.IsValid;
    public bool IsInvalid => !Check.IsValid && !Check.IsEmpty;
    public bool IsEmpty => Check.IsEmpty;

    public string StatusLabel => IsValid ? "Valid" : IsInvalid ? "Invalid" : "Empty";

    public string StatusText
    {
        get
        {
            if (IsEmpty)
                return "Paste or type JSON, or open a file (Ctrl+O).";
            if (IsInvalid)
                return $"Line {Check.Line}, column {Check.Column}: {Check.Message}";

            var parts = new List<string> { Check.RootKind ?? "value" };
            if (Check.Keys > 0) parts.Add($"{Check.Keys:N0} keys");
            if (Check.Depth > 1) parts.Add($"depth {Check.Depth}");
            parts.Add(FormatSize(Encoding.UTF8.GetByteCount(Document.Text)));
            return string.Join("  ·  ", parts);
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB"
    };

    private void Validate()
    {
        Check = JsonTools.Validate(Document.Text);
        Panel.Refresh();
    }

    /// <summary>Replaces the editor text (undoable), formatting it when valid.</summary>
    public void LoadText(string text)
    {
        Replace(text);
        if (JsonTools.Validate(text).IsValid)
        {
            var formatted = JsonTools.Format(text, IndentUnit);
            if (formatted != text)
                Replace(formatted);
        }
        ShowNotice("Loaded from the clipboard. Ctrl+Z restores what was here before.");
    }

    [RelayCommand]
    private void TogglePanel() => Panel.IsOpen = !Panel.IsOpen;

    /// <summary>Treats the editor text as YAML and replaces it with JSON.</summary>
    [RelayCommand]
    private Task FromYaml() => TransformAsync(t => YamlTools.YamlToJson(t, IndentUnit), _ => "Converted YAML to JSON. Ctrl+Z restores the YAML.");

    // ------------------------------------------------------------------ notice strip

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    [ObservableProperty]
    private bool _isNoticeError;

    public bool HasNotice => Notice is not null;

    private void ShowNotice(string text, bool error = false)
    {
        IsNoticeError = error;
        Notice = text;
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    // ------------------------------------------------------------------ options

    public IReadOnlyList<string> IndentOptions { get; } = ["2 spaces", "4 spaces", "Tab"];

    [ObservableProperty]
    private string _selectedIndent;

    partial void OnSelectedIndentChanged(string value)
    {
        var size = value switch { "4 spaces" => 4, "Tab" => 0, _ => 2 };
        if (_settings.Current.JsonIndent == size)
            return;
        _settings.Current.JsonIndent = size;
        _settings.Save();
    }

    private static string IndentName(int size) => size switch { 4 => "4 spaces", 0 => "Tab", _ => "2 spaces" };

    private string IndentUnit => _settings.Current.JsonIndent switch { 4 => "    ", 0 => "\t", _ => "  " };

    // ------------------------------------------------------------------ file

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(HasFile), nameof(IsDirty))]
    private string? _filePath;

    public string FileName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);
    public bool HasFile => FilePath is not null;
    public bool IsDirty => FilePath is not null && !Document.UndoStack.IsOriginalFile;

    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
            OnPropertyChanged(nameof(IsDirty));
    }

    // ------------------------------------------------------------------ editing

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        _version++;
        if (_programmatic)
            return;

        Notice = null;
        _validateTimer.Interval = TimeSpan.FromMilliseconds(Document.TextLength > 2_000_000 ? 700 : 250);
        _validateTimer.Stop();
        _validateTimer.Start();

        // A paste that is now the whole document: format it if it is valid JSON.
        if (_settings.Current.JsonFormatOnPaste && e.Offset == 0 && e.InsertionLength > 1 &&
            e.InsertionLength == Document.TextLength)
        {
            Dispatcher.UIThread.Post(FormatPasted, DispatcherPriority.Background);
        }
    }

    private void FormatPasted()
    {
        var text = Document.Text;
        if (!JsonTools.Validate(text).IsValid)
            return;
        var formatted = JsonTools.Format(text, IndentUnit);
        if (formatted == text)
            return;
        Replace(formatted);
        ShowNotice("Pasted JSON formatted. Ctrl+Z restores the original.");
    }

    private void Replace(string text)
    {
        _programmatic = true;
        try
        {
            Document.Replace(0, Document.TextLength, text);
        }
        finally
        {
            _programmatic = false;
        }

        _validateTimer.Stop();
        Validate();
    }

    /// <summary>Runs a transform (off the UI thread for big documents) and replaces the text as one undo step.</summary>
    private async Task TransformAsync(Func<string, string> transform, Func<string, string>? message = null)
    {
        var text = Document.Text;
        var version = _version;
        try
        {
            var result = text.Length > 200_000 ? await Task.Run(() => transform(text)) : transform(text);
            if (version != _version)
                return; // edited meanwhile
            if (result != text)
                Replace(result);
            if (message is not null)
                ShowNotice(message(result));
        }
        catch (FormatException ex)
        {
            ShowNotice(ex.Message, error: true);
        }
    }

    [RelayCommand]
    private Task Format() => TransformAsync(t => Fail(t) ?? JsonTools.Format(t, IndentUnit));

    [RelayCommand]
    private Task Minify() => TransformAsync(t => Fail(t) ?? JsonTools.Format(t, null));

    [RelayCommand]
    private Task SortKeys() => TransformAsync(t => Fail(t) ?? JsonTools.Format(t, IndentUnit, sortKeys: true),
        _ => "Keys sorted alphabetically at every level.");

    /// <summary>Format/minify only valid JSON; point at Repair otherwise.</summary>
    private static string? Fail(string text)
    {
        var check = JsonTools.Validate(text);
        if (check.IsEmpty)
            throw new FormatException("There is no JSON yet. Paste or type some first.");
        if (!check.IsValid)
            throw new FormatException($"Line {check.Line}, column {check.Column}: {check.Message} Use Repair to fix common mistakes automatically.");
        return null;
    }

    [RelayCommand]
    private async Task Repair()
    {
        if (string.IsNullOrWhiteSpace(Document.Text))
        {
            ShowNotice("There is no JSON yet. Paste or type some first.", error: true);
            return;
        }

        IReadOnlyList<string> fixes = [];
        await TransformAsync(t =>
        {
            var result = JsonTools.Repair(t, IndentUnit);
            fixes = result.Fixes;
            return result.Text;
        }, _ => fixes.Count == 0
            ? "Nothing to repair. The JSON was already valid, so it was just formatted."
            : "Repaired: " + string.Join(" · ", fixes) + ".");
    }

    [RelayCommand]
    private Task EscapeString() => TransformAsync(JsonTools.Escape, _ => "Converted to a JSON string literal.");

    [RelayCommand]
    private Task UnescapeString() => TransformAsync(t =>
    {
        var plain = JsonTools.Unescape(t);
        return JsonTools.Validate(plain).IsValid ? JsonTools.Format(plain, IndentUnit) : plain;
    }, _ => "Unescaped the JSON string.");

    [RelayCommand]
    private async Task CopyAll()
    {
        if (Document.TextLength == 0)
            return;
        await _dialogs.CopyToClipboardAsync(Document.Text);
        ShowNotice("Copied to the clipboard.");
    }

    [RelayCommand]
    private void Clear()
    {
        if (Document.TextLength > 0)
            Replace("");
        Notice = null;
    }

    [RelayCommand]
    private void GoToError()
    {
        if (IsInvalid)
            GoToRequested?.Invoke(this, Check.Offset);
    }

    // ------------------------------------------------------------------ files

    [RelayCommand]
    private async Task Open()
    {
        if (!await ConfirmLeaveAsync())
            return;

        var path = await _dialogs.PickOpenFileAsync("Open JSON file", "JSON", "*.json", "*.jsonc", "*.json5", "*.geojson", "*.har");
        if (path is null)
            return;

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                ShowNotice($"\"{info.Name}\" is larger than 100 MB, too big to open here.", error: true);
                return;
            }

            var text = await File.ReadAllTextAsync(path);
            Replace(text);
            Document.UndoStack.ClearAll();
            Document.UndoStack.MarkAsOriginalFile();
            FilePath = path;
            Notice = null;
            GoToRequested?.Invoke(this, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("Could not open the file: " + ex.Message, error: true);
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (FilePath is null)
        {
            await SaveAs();
            return;
        }

        await WriteAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        var path = await _dialogs.PickSaveFileAsync("Save JSON", FilePath is null ? "data.json" : FileName, "json", "JSON");
        if (path is null)
            return;
        if (await WriteAsync(path))
            FilePath = path;
    }

    private async Task<bool> WriteAsync(string path)
    {
        try
        {
            await File.WriteAllTextAsync(path, Document.Text, new UTF8Encoding(false));
            Document.UndoStack.MarkAsOriginalFile();
            OnPropertyChanged(nameof(IsDirty));
            ShowNotice($"Saved {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("Could not save: " + ex.Message, error: true);
            return false;
        }
    }

    /// <summary>Asks about unsaved changes to an opened file. Returns false when the user cancels.</summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsDirty)
            return true;

        switch (await _dialogs.ConfirmUnsavedAsync(FileName))
        {
            case UnsavedChoice.Save:
                await Save();
                return !IsDirty;
            case UnsavedChoice.Discard:
                return true;
            default:
                return false;
        }
    }
}
