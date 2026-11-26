using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Markdown;
using DeveloperTools.Services;
using DeveloperTools.Tools;

using DeveloperTools.Localization;

namespace DeveloperTools.ViewModels;

public enum DiffView
{
    Edit,
    SideBySide,
    Inline
}

public partial class DiffPageViewModel : PageViewModel
{
    private const long MaxFileBytes = 50L * 1024 * 1024;

    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private int _runVersion;

    public DiffPageViewModel(SettingsService settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        LeftDocument.TextChanged += (_, _) => OnPropertyChanged(nameof(LeftStats));
        RightDocument.TextChanged += (_, _) => OnPropertyChanged(nameof(RightStats));
        settings.Changed += (_, _) => OnPropertyChanged(nameof(EditorFontSize));
    }

    public override string Title => Loc.T("Diff");
    public override string Subtitle => Loc.T("Compare two texts, files or JSON documents");

    public TextDocument LeftDocument { get; } = new();
    public TextDocument RightDocument { get; } = new();
    public int EditorFontSize => _settings.Current.EditorFontSize;

    [ObservableProperty] private string _leftName = "Original";
    [ObservableProperty] private string _rightName = "Modified";
    [ObservableProperty] private string? _leftPath;
    [ObservableProperty] private string? _rightPath;

    /// <summary>Syntax colouring language: from the file extension, JSON mode, or JSON-looking content.</summary>
    public string? Language
    {
        get
        {
            if (CompareAsJson) return "json";
            foreach (var path in new[] { LeftPath, RightPath, LeftName, RightName })
            {
                var ext = Path.GetExtension(path ?? "").TrimStart('.').ToLowerInvariant();
                if (ext.Length > 0 && CodeHighlighter.IsSupported(ext)) return ext;
            }
            var sample = LeftDocument.TextLength > 0 ? LeftDocument.GetText(0, Math.Min(200, LeftDocument.TextLength)).TrimStart() : "";
            return sample.StartsWith('{') || sample.StartsWith('[') ? "json" : null;
        }
    }

    public string LeftStats => Stats(LeftDocument);
    public string RightStats => Stats(RightDocument);

    private static string Stats(TextDocument d) =>
        d.TextLength == 0 ? "" : $"{d.LineCount:N0} lines";

    // ------------------------------------------------------------------ view + options

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditView), nameof(IsResultView), nameof(IsSideBySide), nameof(IsInline))]
    private DiffView _view = DiffView.Edit;

    public bool IsEditView => View == DiffView.Edit;
    public bool IsResultView => View != DiffView.Edit;
    public bool IsSideBySide => View == DiffView.SideBySide;
    public bool IsInline => View == DiffView.Inline;

    partial void OnViewChanged(DiffView value)
    {
        if (value != DiffView.Edit)
            _ = RunAsync();
    }

    [ObservableProperty] private bool _ignoreWhitespace;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private bool _compareAsJson;
    [ObservableProperty] private bool _onlyChanges;

    partial void OnIgnoreWhitespaceChanged(bool value) => Rerun();
    partial void OnIgnoreCaseChanged(bool value) => Rerun();
    partial void OnCompareAsJsonChanged(bool value)
    {
        OnPropertyChanged(nameof(Language));
        Rerun();
    }

    partial void OnLeftPathChanged(string? value) => OnPropertyChanged(nameof(Language));
    partial void OnRightPathChanged(string? value) => OnPropertyChanged(nameof(Language));
    partial void OnOnlyChangesChanged(bool value) => Rerun();

    private void Rerun()
    {
        if (IsResultView)
            _ = RunAsync();
    }

    // ------------------------------------------------------------------ result

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(IsIdentical), nameof(HasChanges), nameof(AddedText),
        nameof(RemovedText), nameof(ModifiedText), nameof(HasAdded), nameof(HasRemoved), nameof(HasModified))]
    private DiffResult? _result;

    public DiffDocument? LeftResult { get; private set; }
    public DiffDocument? RightResult { get; private set; }
    public DiffDocument? InlineResult { get; private set; }

    public bool HasResult => Result is not null;
    public bool IsIdentical => Result is { Identical: true };
    public bool HasChanges => Result is { Identical: false };
    public string AddedText => $"+{Result?.Additions ?? 0:N0}";
    public string RemovedText => $"−{Result?.Deletions ?? 0:N0}";
    public string ModifiedText => $"~{Result?.Modifications ?? 0:N0}";
    public bool HasAdded => Result?.Additions > 0;
    public bool HasRemoved => Result?.Deletions > 0;
    public bool HasModified => Result?.Modifications > 0;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    public bool HasNote => Note is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangePosition))]
    private int _currentChange = -1;

    public IReadOnlyList<int> ChangeLines =>
        (IsInline ? InlineResult?.ChangeLines : LeftResult?.ChangeLines) ?? Array.Empty<int>();

    public string ChangePosition => ChangeLines.Count == 0
        ? "No changes"
        : CurrentChange < 0 ? $"{ChangeLines.Count} change{(ChangeLines.Count == 1 ? "" : "s")}" : $"{CurrentChange + 1} of {ChangeLines.Count}";

    /// <summary>Raised when new result documents are ready for the views.</summary>
    public event EventHandler? ResultReady;

    /// <summary>Raised to scroll the result to a display line (and mark that change current).</summary>
    public event EventHandler<int>? NavigateRequested;

    private async Task RunAsync()
    {
        var version = ++_runVersion;
        var left = LeftDocument.Text;
        var right = RightDocument.Text;
        var options = new DiffOptions(IgnoreWhitespace, IgnoreCase);
        var asJson = CompareAsJson;
        var only = OnlyChanges;
        IsBusy = true;
        try
        {
            var (result, leftDoc, rightDoc, inlineDoc, note) = await Task.Run(() =>
            {
                string? jsonNote = null;
                if (asJson)
                {
                    var l = TryNormalizeJson(left);
                    var r = TryNormalizeJson(right);
                    if (l is not null && r is not null)
                    {
                        left = l;
                        right = r;
                        jsonNote = Loc.T("Compared as JSON: both sides were formatted and keys sorted, so only real data changes show.");
                    }
                    else
                    {
                        jsonNote = Loc.F("{0} is not valid JSON, so the texts were compared as is.", (l is null ? LeftName : RightName));
                    }
                }

                var res = DiffEngine.Compare(left, right, options);
                var (ld, rd) = DiffDisplay.SideBySide(res, only);
                var id = DiffDisplay.Inline(res, only);
                var notes = new List<string>();
                if (jsonNote is not null) notes.Add(jsonNote);
                if (res.LineEndingNote is not null) notes.Add(res.LineEndingNote);
                if (res.TimedOut) notes.Add("The texts differ a lot, so the diff was simplified to finish quickly. Some blocks may show as fully replaced.");
                return (res, ld, rd, id, notes.Count == 0 ? null : string.Join("  ", notes));
            });

            if (version != _runVersion)
                return;

            LeftResult = leftDoc;
            RightResult = rightDoc;
            InlineResult = inlineDoc;
            Note = note;
            Result = result;
            CurrentChange = -1;
            OnPropertyChanged(nameof(ChangeLines));
            OnPropertyChanged(nameof(ChangePosition));
            OnPropertyChanged(nameof(Language));
            ResultReady?.Invoke(this, EventArgs.Empty);
            if (ChangeLines.Count > 0)
                GoToChange(0);
        }
        finally
        {
            if (version == _runVersion)
                IsBusy = false;
        }
    }

    private static string? TryNormalizeJson(string text)
    {
        try
        {
            return JsonTools.Format(text, "  ", sortKeys: true);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private void Compare()
    {
        if (View == DiffView.Edit)
            View = DiffView.SideBySide;
        else
            _ = RunAsync();
    }

    [RelayCommand]
    private void ShowEdit() => View = DiffView.Edit;

    [RelayCommand]
    private void NextChange()
    {
        if (ChangeLines.Count > 0)
            GoToChange((CurrentChange + 1) % ChangeLines.Count);
    }

    [RelayCommand]
    private void PreviousChange()
    {
        if (ChangeLines.Count > 0)
            GoToChange(CurrentChange <= 0 ? ChangeLines.Count - 1 : CurrentChange - 1);
    }

    public void GoToChange(int index)
    {
        CurrentChange = index;
        NavigateRequested?.Invoke(this, ChangeLines[index]);
    }

    /// <summary>Called by the view when the inline/side-by-side switch changes which documents are shown.</summary>
    public void OnViewSwitched()
    {
        OnPropertyChanged(nameof(ChangeLines));
        CurrentChange = -1;
        OnPropertyChanged(nameof(ChangePosition));
    }

    [RelayCommand]
    private void Swap()
    {
        var left = LeftDocument.Text;
        LeftDocument.Text = RightDocument.Text;
        RightDocument.Text = left;
        (LeftName, RightName) = (RightName, LeftName);
        (LeftPath, RightPath) = (RightPath, LeftPath);
        Rerun();
    }

    [RelayCommand]
    private async Task CopyPatch()
    {
        if (Result is null)
            return;
        var patch = DiffEngine.UnifiedPatch(Result, "a/" + LeftName, "b/" + RightName);
        await _dialogs.CopyToClipboardAsync(patch);
        Note = Loc.T("Unified diff (patch) copied to the clipboard.");
    }

    [RelayCommand]
    private Task OpenLeft() => OpenAsync(left: true);

    [RelayCommand]
    private Task OpenRight() => OpenAsync(left: false);

    private async Task OpenAsync(bool left)
    {
        var file = await _dialogs.OpenFileAsync(left ? "Open original file" : "Open modified file", "All files", "*.*");
        if (file is not null)
            await LoadFileAsync(file, left);
    }

    /// <summary>Loads a file into one side (also used for drag and drop).</summary>
    public async Task LoadFileAsync(IStorageFile file, bool left)
    {
        try
        {
            var name = file.Name;
            var path = StorageFiles.Describe(file);
            if (await StorageFiles.SizeAsync(file) > MaxFileBytes)
            {
                Note = Loc.F("\"{0}\" is larger than 50 MB, too big to compare here.", name);
                return;
            }

            var text = await StorageFiles.ReadTextAsync(file);
            if (text.IndexOf('\0') >= 0)
            {
                Note = Loc.F("\"{0}\" looks like a binary file. Only text files can be compared.", name);
                return;
            }

            var doc = left ? LeftDocument : RightDocument;
            doc.Text = text;
            doc.UndoStack.ClearAll();
            if (left)
            {
                LeftName = name;
                LeftPath = path;
            }
            else
            {
                RightName = name;
                RightPath = path;
            }

            if (CompareAsJson is false && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                (left ? RightPath : LeftPath) is { } other && other.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                CompareAsJson = true;
            Rerun();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note = Loc.T("Could not open the file: ") + ex.Message;
        }
    }

    [RelayCommand]
    private async Task PasteLeft() => await PasteAsync(LeftDocument, left: true);

    [RelayCommand]
    private async Task PasteRight() => await PasteAsync(RightDocument, left: false);

    private async Task PasteAsync(TextDocument doc, bool left)
    {
        var text = await _dialogs.GetClipboardTextAsync();
        if (text is null)
            return;
        doc.Text = text;
        if (left) { LeftName = "Original"; LeftPath = null; }
        else { RightName = "Modified"; RightPath = null; }
        Rerun();
    }

    [RelayCommand]
    private void ClearLeft()
    {
        LeftDocument.Text = "";
        LeftName = "Original";
        LeftPath = null;
    }

    [RelayCommand]
    private void ClearRight()
    {
        RightDocument.Text = "";
        RightName = "Modified";
        RightPath = null;
    }

    [RelayCommand]
    private void DismissNote() => Note = null;
}
