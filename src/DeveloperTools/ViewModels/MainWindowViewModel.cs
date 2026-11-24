using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

namespace DeveloperTools.ViewModels;

/// <summary>An entry in the command palette.</summary>
public sealed record PaletteItem(string Title, string Group, string Keywords, Action Run, string? Shortcut = null)
{
    public bool HasShortcut => Shortcut is not null;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly List<PaletteItem> _commands;
    private string? _lastClipboardSeen;

    public MainWindowViewModel(SettingsService settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        // Only the start page (and settings, which applies the theme) exist at startup;
        // every other tool is created the first time it is opened.
        Markdown = new MarkdownPageViewModel(settings, dialogs);
        Settings = new SettingsPageViewModel(settings, dialogs);
        Markdown.OpenSettingsRequested += (_, _) => ShowSettings();
        _currentPage = Markdown;
        _commands = BuildCommands();
    }

    private SnippetService? _snippets;
    private SnippetService Snippets => _snippets ??= new SnippetService();

    private JsonPageViewModel? _json;
    private DiffPageViewModel? _diff;
    private RegexPageViewModel? _regex;
    private TimestampPageViewModel? _timestamp;
    private TextPageViewModel? _text;
    private GeneratorsPageViewModel? _generators;
    private EncodePageViewModel? _encode;

    public MarkdownPageViewModel Markdown { get; }
    public JsonPageViewModel Json => _json ??= new JsonPageViewModel(_settings, _dialogs, Snippets);
    public DiffPageViewModel Diff => _diff ??= new DiffPageViewModel(_settings, _dialogs);
    public RegexPageViewModel Regex => _regex ??= new RegexPageViewModel(_settings, _dialogs, Snippets);
    public TimestampPageViewModel Timestamp => _timestamp ??= new TimestampPageViewModel(_dialogs);
    public TextPageViewModel Text => _text ??= new TextPageViewModel(_settings, _dialogs, Snippets);
    public GeneratorsPageViewModel Generators => _generators ??= new GeneratorsPageViewModel(_dialogs);
    public EncodePageViewModel Encode => _encode ??= new EncodePageViewModel(_dialogs);
    public SettingsPageViewModel Settings { get; }

    /// <summary>The JSON page if it was opened (for the unsaved-changes check on exit).</summary>
    public JsonPageViewModel? JsonIfCreated => _json;

    partial void OnCurrentPageChanged(PageViewModel? oldValue, PageViewModel newValue)
    {
        oldValue?.OnHidden();
        newValue.OnShown();
    }

    public string Version { get; } =
        "v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMarkdownActive), nameof(IsJsonActive), nameof(IsDiffActive), nameof(IsRegexActive),
        nameof(IsTimestampActive), nameof(IsTextActive), nameof(IsGeneratorsActive), nameof(IsEncodeActive), nameof(IsSettingsActive))]
    private PageViewModel _currentPage;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    /// <summary>Flips between light and dark (an explicit choice, so it overrides "System").</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        var isDark = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        Settings.Theme = isDark ? "Light" : "Dark"; // saves and applies via the settings page model
    }

    public bool IsMarkdownActive => CurrentPage == Markdown;
    public bool IsJsonActive => CurrentPage is JsonPageViewModel;
    public bool IsDiffActive => CurrentPage is DiffPageViewModel;
    public bool IsRegexActive => CurrentPage is RegexPageViewModel;
    public bool IsTimestampActive => CurrentPage is TimestampPageViewModel;
    public bool IsTextActive => CurrentPage is TextPageViewModel;
    public bool IsGeneratorsActive => CurrentPage is GeneratorsPageViewModel;
    public bool IsEncodeActive => CurrentPage is EncodePageViewModel;
    public bool IsSettingsActive => CurrentPage == Settings;

    [RelayCommand] private void ShowMarkdown() => CurrentPage = Markdown;
    [RelayCommand] private void ShowJson() => CurrentPage = Json;
    [RelayCommand] private void ShowDiff() => CurrentPage = Diff;
    [RelayCommand] private void ShowRegex() => CurrentPage = Regex;
    [RelayCommand] private void ShowTimestamp() => CurrentPage = Timestamp;
    [RelayCommand] private void ShowText() => CurrentPage = Text;
    [RelayCommand] private void ShowGenerators() => CurrentPage = Generators;
    [RelayCommand] private void ShowEncode() => CurrentPage = Encode;
    [RelayCommand] private void ShowSettings() => CurrentPage = Settings;

    // ------------------------------------------------------------------ command palette

    [ObservableProperty] private bool _isPaletteOpen;
    [ObservableProperty] private string _paletteQuery = "";
    [ObservableProperty] private PaletteItem? _paletteSelection;

    public ObservableCollection<PaletteItem> PaletteResults { get; } = new();

    partial void OnPaletteQueryChanged(string value) => FilterPalette();

    [RelayCommand]
    private void OpenPalette()
    {
        if (PaletteResults.Count == 0 && PaletteQuery.Length == 0)
            FilterPalette();
        PaletteQuery = "";
        FilterPalette();
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    [RelayCommand]
    private void RunPaletteItem(PaletteItem? item)
    {
        item ??= PaletteSelection;
        if (item is null)
            return;
        IsPaletteOpen = false;
        item.Run();
    }

    public void MovePaletteSelection(int delta)
    {
        if (PaletteResults.Count == 0)
            return;
        var index = PaletteSelection is null ? -1 : PaletteResults.IndexOf(PaletteSelection);
        index = Math.Clamp(index + delta, 0, PaletteResults.Count - 1);
        PaletteSelection = PaletteResults[index];
    }

    /// <summary>Every word of the query must appear (in title, group or keywords); title-prefix matches rank first.</summary>
    private void FilterPalette()
    {
        var words = PaletteQuery.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _commands
            .Select(c => (c, hay: $"{c.Title} {c.Group} {c.Keywords}".ToLowerInvariant()))
            .Where(x => words.All(w => x.hay.Contains(w)))
            .OrderByDescending(x => words.Length > 0 && x.c.Title.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
            .Select(x => x.c)
            .Take(40)
            .ToList();
        PaletteResults.Clear();
        foreach (var m in matches)
            PaletteResults.Add(m);
        PaletteSelection = PaletteResults.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoPaletteResults));
    }

    public bool HasNoPaletteResults => PaletteResults.Count == 0;

    private List<PaletteItem> BuildCommands()
    {
        void Go(PageViewModel page) => CurrentPage = page;
        return
        [
            new("Markdown", "Go to", "md notes docs files", () => Go(Markdown)),
            new("JSON", "Go to", "format validate repair minify", () => Go(Json)),
            new("Diff", "Go to", "compare difference text files", () => Go(Diff)),
            new("Regex", "Go to", "regular expression test match", () => Go(Regex)),
            new("Timestamp", "Go to", "unix epoch date time zone iso", () => Go(Timestamp)),
            new("Text", "Go to", "case sort lines count words", () => Go(Text)),
            new("Generators", "Go to", "password key uuid guid lorem fake qr", () => Go(Generators)),
            new("Encode / Decode", "Go to", "base64 url html hex jwt hash unicode image", () => Go(Encode)),
            new("Settings", "Go to", "preferences options folders theme", () => Go(Settings)),

            new("Format JSON", "JSON", "pretty beautify indent", () => { Go(Json); Json.FormatCommand.Execute(null); }, "Ctrl+Shift+F"),
            new("Minify JSON", "JSON", "compact", () => { Go(Json); Json.MinifyCommand.Execute(null); }, "Ctrl+Shift+M"),
            new("Repair JSON", "JSON", "fix correct broken", () => { Go(Json); Json.RepairCommand.Execute(null); }, "Ctrl+Shift+R"),
            new("Sort JSON keys", "JSON", "order alphabetical", () => { Go(Json); Json.SortKeysCommand.Execute(null); }),
            new("JSON tree, query, code and YAML", "JSON", "jsonpath tree view kotlin csharp typescript yaml panel", () => { Go(Json); Json.Panel.IsOpen = true; }),
            new("YAML to JSON", "JSON", "convert yml", () => { Go(Json); Json.FromYamlCommand.Execute(null); }),
            new("Open JSON file", "JSON", "load", () => { Go(Json); Json.OpenCommand.Execute(null); }, "Ctrl+O"),

            new("Compare texts", "Diff", "diff run compare", () => { Go(Diff); Diff.CompareCommand.Execute(null); }, "Ctrl+Enter"),
            new("Open original file", "Diff", "left compare", () => { Go(Diff); Diff.OpenLeftCommand.Execute(null); }),
            new("Open modified file", "Diff", "right compare", () => { Go(Diff); Diff.OpenRightCommand.Execute(null); }),
            new("Swap diff sides", "Diff", "reverse", () => { Go(Diff); Diff.SwapCommand.Execute(null); }),
            new("Copy diff as patch", "Diff", "unified git", () => { Go(Diff); Diff.CopyPatchCommand.Execute(null); }),

            new("New password", "Generators", "generate random secure", () => { Go(Generators); Generators.Tab = GeneratorTab.Password; Generators.GeneratePasswordsCommand.Execute(null); }),
            new("New secret key", "Generators", "api jwt aes token random", () => { Go(Generators); Generators.Tab = GeneratorTab.Secret; Generators.GenerateSecretsCommand.Execute(null); }),
            new("New UUID / GUID", "Generators", "guid identifier v4 v7", () => { Go(Generators); Generators.Tab = GeneratorTab.Uuid; Generators.GenerateUuidsCommand.Execute(null); }),
            new("Lorem ipsum", "Generators", "placeholder text dummy", () => { Go(Generators); Generators.Tab = GeneratorTab.Lorem; }),
            new("Fake test data", "Generators", "mock sample names emails csv", () => { Go(Generators); Generators.Tab = GeneratorTab.Fake; }),
            new("QR code", "Generators", "qrcode wifi link", () => { Go(Generators); Generators.Tab = GeneratorTab.Qr; }),

            new("Base64 encode / decode", "Encode", "b64", () => { Go(Encode); Encode.Mode = CodecMode.Base64; }),
            new("Decode JWT", "Encode", "token jwt bearer claims", () => { Go(Encode); Encode.Mode = CodecMode.Jwt; }),
            new("Hash text (SHA-256, MD5…)", "Encode", "sha md5 hmac checksum", () => { Go(Encode); Encode.Mode = CodecMode.Hash; }),
            new("URL encode / decode", "Encode", "percent escape", () => { Go(Encode); Encode.Mode = CodecMode.Url; }),
            new("Image to Base64", "Encode", "data uri picture png", () => { Go(Encode); Encode.Mode = CodecMode.Image; }),
            new("Unicode escape", "Encode", "\\u escape emoji", () => { Go(Encode); Encode.Mode = CodecMode.Unicode; }),

            new("Current Unix time", "Timestamp", "now epoch", () => { Go(Timestamp); Timestamp.UseNowCommand.Execute(null); }),

            new("Toggle light / dark", "App", "theme dark light mode", () => ToggleTheme()),
            new("Toggle sidebar", "App", "collapse expand menu", () => ToggleSidebar()),
        ];
    }

    // ------------------------------------------------------------------ clipboard detection

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboardHint))]
    private string? _clipboardHint;

    [ObservableProperty] private string _clipboardAction = "";

    private ClipKind _clipKind;
    private string _clipText = "";

    public bool HasClipboardHint => ClipboardHint is not null;

    private enum ClipKind { None, Jwt, Json, Timestamp, Base64 }

    /// <summary>Called when the window gets focus: offers to open recognised clipboard content in the right tool.</summary>
    public async Task CheckClipboardAsync()
    {
        if (!_settings.Current.DetectClipboard)
            return;
        string? text;
        try
        {
            text = await _dialogs.GetClipboardTextAsync();
        }
        catch (Exception)
        {
            return; // clipboard busy or unavailable
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > 5_000_000 || text == _lastClipboardSeen)
            return;
        _lastClipboardSeen = text;

        var (kind, hint, action) = Detect(text.Trim());
        if (kind == ClipKind.None)
            return;
        _clipKind = kind;
        _clipText = text.Trim();
        ClipboardAction = action;
        ClipboardHint = hint;
    }

    private static readonly System.Text.RegularExpressions.Regex JwtPattern =
        new(@"^(Bearer\s+)?eyJ[\w-]+\.eyJ[\w-]+\.[\w-]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static (ClipKind, string, string) Detect(string t)
    {
        if (JwtPattern.IsMatch(t))
            return (ClipKind.Jwt, "Your clipboard has a JWT.", "Decode it");
        if (t.Length >= 2 && t[0] is '{' or '[' && JsonTools.Validate(t) is { IsValid: true, RootKind: "object" or "array" })
            return (ClipKind.Json, "Your clipboard has JSON.", "Open in JSON");
        if (t.Length is 10 or 13 && t.All(char.IsAsciiDigit) &&
            TimeTools.Parse(t, TimeZoneInfo.Utc) is { } p && p.Value.Year is > 2000 and < 2100)
            return (ClipKind.Timestamp, $"Your clipboard has a Unix timestamp ({p.Value.ToLocalTime():yyyy-MM-dd HH:mm}).", "Convert it");
        if (t.Length >= 24 && t.Length % 4 == 0 && !t.Contains(' ') &&
            t.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=') && t.Any(char.IsDigit) && t.Any(char.IsUpper) && t.Any(char.IsLower))
        {
            try
            {
                var bytes = Convert.FromBase64String(t);
                var decoded = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                if (decoded.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t'))
                    return (ClipKind.Base64, "Your clipboard looks like Base64 text.", "Decode it");
            }
            catch (Exception e) when (e is FormatException or System.Text.DecoderFallbackException)
            {
            }
        }
        return (ClipKind.None, "", "");
    }

    [RelayCommand]
    private void UseClipboard()
    {
        switch (_clipKind)
        {
            case ClipKind.Jwt:
                Encode.Mode = CodecMode.Jwt;
                Encode.Input = _clipText;
                CurrentPage = Encode;
                break;
            case ClipKind.Json:
                Json.LoadText(_clipText);
                CurrentPage = Json;
                break;
            case ClipKind.Timestamp:
                Timestamp.Input = _clipText;
                CurrentPage = Timestamp;
                break;
            case ClipKind.Base64:
                Encode.Mode = CodecMode.Base64;
                Encode.IsEncode = false;
                Encode.Input = _clipText;
                CurrentPage = Encode;
                break;
        }
        ClipboardHint = null;
    }

    [RelayCommand]
    private void DismissClipboard() => ClipboardHint = null;
}
