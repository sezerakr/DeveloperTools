using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

using DeveloperTools.Localization;

namespace DeveloperTools.ViewModels;

public partial class TextPageViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _statsTimer;

    public TextPageViewModel(SettingsService settings, IDialogService dialogs, SnippetService snippets)
    {
        _settings = settings;
        _dialogs = dialogs;
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _statsTimer.Tick += (_, _) =>
        {
            _statsTimer.Stop();
            UpdateStats();
        };
        Document.TextChanged += (_, _) =>
        {
            IsEmpty = Document.TextLength == 0;
            _statsTimer.Stop();
            _statsTimer.Start();
        };
        Snippets = new SnippetsViewModel(snippets, "text", () => Document.Text, t => Document.Text = t);
        UpdateStats();
    }

    public override string Title => Loc.T("Text");
    public override string Subtitle => Loc.T("Change case, sort and clean lines, count words");

    public TextDocument Document { get; } = new();
    public SnippetsViewModel Snippets { get; }
    public int EditorFontSize => _settings.Current.EditorFontSize;

    [ObservableProperty] private bool _wordWrap = true;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _separator = ", ";

    // ------------------------------------------------------------------ stats

    [ObservableProperty] private string _stats = "";
    [ObservableProperty] private string _selectionStats = "";

    private void UpdateStats()
    {
        var s = TextTools.Stats(Document.Text);
        var minutes = s.ReadingTime.TotalMinutes;
        var reading = s.Words == 0 ? "0 min" : minutes < 1 ? "< 1 min" : $"{Math.Round(minutes):0} min";
        Stats = Loc.F("{0:N0} characters  ·  {1:N0} without spaces  ·  {2:N0} words  ·  {3:N0} lines  ·  {4:N0} sentences  ·  {5:N0} paragraphs  ·  {6:N0} bytes  ·  {7} read",
            s.Characters, s.CharactersNoSpaces, s.Words, s.Lines, s.Sentences, s.Paragraphs, s.Bytes, reading);
    }

    public void UpdateSelection(string selected)
    {
        if (selected.Length == 0)
        {
            SelectionStats = "";
            return;
        }
        var s = TextTools.Stats(selected);
        SelectionStats = Loc.F("Selection: {0:N0} chars, {1:N0} words, {2:N0} lines", s.Characters, s.Words, s.Lines);
    }

    // ------------------------------------------------------------------ transforms

    /// <summary>Asks the view for the selection (or whole text) and replaces it as one undo step.</summary>
    public event EventHandler<Func<string, string>>? TransformRequested;

    private void Transform(Func<string, string> f) => TransformRequested?.Invoke(this, f);

    [RelayCommand]
    private void Case(string style) => Transform(t => TextTools.ConvertCase(t, Enum.Parse<CaseStyle>(style)));

    [RelayCommand]
    private void Lines(string operation) => Transform(operation switch
    {
        "sort" => t => TextTools.SortLines(t, false),
        "sortdesc" => t => TextTools.SortLines(t, true),
        "reverse" => TextTools.ReverseLines,
        "unique" => t => TextTools.UniqueLines(t),
        "uniquei" => t => TextTools.UniqueLines(t, ignoreCase: true),
        "empty" => TextTools.RemoveEmptyLines,
        "trim" => TextTools.TrimLines,
        "shuffle" => TextTools.ShuffleLines,
        "number" => TextTools.NumberLines,
        "join" => t => TextTools.JoinLines(t, Unescape(Separator)),
        "split" => t => TextTools.SplitToLines(t, Unescape(Separator).Trim().Length > 0 ? Unescape(Separator).Trim() : Unescape(Separator)),
        _ => t => t
    });

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");

    [RelayCommand]
    private async Task CopyAll()
    {
        if (Document.TextLength > 0)
            await _dialogs.CopyToClipboardAsync(Document.Text);
    }

    [RelayCommand]
    private void Clear()
    {
        if (Document.TextLength > 0)
            Document.Replace(0, Document.TextLength, "");
    }
}
