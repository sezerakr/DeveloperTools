using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

using DeveloperTools.Localization;

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
        Updates = new UpdateService(settings);
        Updates.Changed += (_, _) =>
        {
            if (Updates.State == UpdateState.Installed)
                UpdateHint = Updates.Status;
        };
        Settings = new SettingsPageViewModel(settings, dialogs, Updates);
        Settings.RestartRequested += (_, _) => RestartRequested?.Invoke(this, EventArgs.Empty);
        Settings.QuitRequested += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        Markdown.OpenSettingsRequested += (_, _) => ShowSettings();
        Home = new HomePageViewModel(settings, Tools, NavigateTo);
        Home.FavoritesChanged += (_, _) => OnPropertyChanged(nameof(IsCurrentFavorite));
        // Start on Home once the user has favorites; otherwise on Markdown as before.
        _currentPage = settings.Current.FavoriteTools.Count > 0 ? Home : Markdown;
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
    public HomePageViewModel Home { get; }

    /// <summary>Every tool, in sidebar order (used by Home and favorites).</summary>
    public static IReadOnlyList<ToolInfo> Tools { get; } =
    [
        new("markdown", Loc.T("Markdown"), Loc.T("Browse, preview and edit markdown files"), "Icon.Markdown"),
        new("json", Loc.T("JSON"), Loc.T("Format, validate and repair JSON"), "Icon.Json"),
        new("diff", Loc.T("Diff"), Loc.T("Compare two texts, files or JSON documents"), "Icon.Diff"),
        new("regex", Loc.T("Regex"), Loc.T("Test .NET regular expressions with live matches"), "Icon.Regex"),
        new("timestamp", Loc.T("Timestamp"), Loc.T("Unix time, ISO 8601 and time zones"), "Icon.Clock"),
        new("text", Loc.T("Text"), Loc.T("Change case, sort and clean lines, count words"), "Icon.Text"),
        new("generators", Loc.T("Generators"), Loc.T("Passwords, keys, UUIDs, test data and QR codes"), "Icon.Generate"),
        new("encode", Loc.T("Encode / Decode"), Loc.T("Base64, URL, HTML, hex, Unicode, images, JWT and hashes"), "Icon.Swap"),
    ];

    /// <summary>Short key of the visible tool ("json", …), or null for Home and Settings.</summary>
    public string? CurrentToolKey => CurrentPage switch
    {
        MarkdownPageViewModel => "markdown",
        JsonPageViewModel => "json",
        DiffPageViewModel => "diff",
        RegexPageViewModel => "regex",
        TimestampPageViewModel => "timestamp",
        TextPageViewModel => "text",
        GeneratorsPageViewModel => "generators",
        EncodePageViewModel => "encode",
        _ => null
    };

    public bool CanFavorite => CurrentToolKey is not null;
    public bool IsCurrentFavorite => CurrentToolKey is { } key && Home.IsFavorite(key);

    [RelayCommand]
    private void ToggleCurrentFavorite()
    {
        if (CurrentToolKey is { } key)
            Home.Toggle(key);
    }
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
        OnPropertyChanged(nameof(CurrentToolKey));
        OnPropertyChanged(nameof(CanFavorite));
        OnPropertyChanged(nameof(IsCurrentFavorite));
    }

    public string Version { get; } =
        "v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeActive), nameof(IsMarkdownActive), nameof(IsJsonActive), nameof(IsDiffActive), nameof(IsRegexActive),
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

    public bool IsHomeActive => CurrentPage == Home;
    public bool IsMarkdownActive => CurrentPage == Markdown;
    public bool IsJsonActive => CurrentPage is JsonPageViewModel;
    public bool IsDiffActive => CurrentPage is DiffPageViewModel;
    public bool IsRegexActive => CurrentPage is RegexPageViewModel;
    public bool IsTimestampActive => CurrentPage is TimestampPageViewModel;
    public bool IsTextActive => CurrentPage is TextPageViewModel;
    public bool IsGeneratorsActive => CurrentPage is GeneratorsPageViewModel;
    public bool IsEncodeActive => CurrentPage is EncodePageViewModel;
    public bool IsSettingsActive => CurrentPage == Settings;

    /// <summary>Opens a tool by short name (deep links, --page).</summary>
    public void NavigateTo(string? name)
    {
        PageViewModel? page = name?.Trim().TrimStart('#').ToLowerInvariant() switch
        {
            "home" => Home,
            "markdown" or "md" => Markdown,
            "json" => Json,
            "diff" or "compare" => Diff,
            "regex" => Regex,
            "timestamp" or "time" => Timestamp,
            "text" => Text,
            "generators" or "password" => Generators,
            "encode" or "decode" or "base64" or "jwt" => Encode,
            "settings" => Settings,
            _ => null
        };
        if (page is not null)
            CurrentPage = page;
    }

    [RelayCommand] private void ShowHome() => CurrentPage = Home;
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
            new(Loc.T("Home"), Loc.T("Go to"), "home favorites start", () => Go(Home)),
            new(Loc.T("Markdown"), Loc.T("Go to"), "markdown md notes docs files", () => Go(Markdown)),
            new(Loc.T("JSON"), Loc.T("Go to"), "json format validate repair minify", () => Go(Json)),
            new(Loc.T("Diff"), Loc.T("Go to"), "diff compare difference text files", () => Go(Diff)),
            new(Loc.T("Regex"), Loc.T("Go to"), "regex regular expression test match", () => Go(Regex)),
            new(Loc.T("Timestamp"), Loc.T("Go to"), "timestamp unix epoch date time zone iso", () => Go(Timestamp)),
            new(Loc.T("Text"), Loc.T("Go to"), "text case sort lines count words", () => Go(Text)),
            new(Loc.T("Generators"), Loc.T("Go to"), "generators password key uuid guid lorem fake qr", () => Go(Generators)),
            new(Loc.T("Encode / Decode"), Loc.T("Go to"), "encode / decode base64 url html hex jwt hash unicode image", () => Go(Encode)),
            new(Loc.T("Settings"), Loc.T("Go to"), "settings preferences options folders theme", () => Go(Settings)),

            new(Loc.T("Format JSON"), Loc.T("JSON"), "format json pretty beautify indent", () => { Go(Json); Json.FormatCommand.Execute(null); }, "Ctrl+Shift+F"),
            new(Loc.T("Minify JSON"), Loc.T("JSON"), "minify json compact", () => { Go(Json); Json.MinifyCommand.Execute(null); }, "Ctrl+Shift+M"),
            new(Loc.T("Repair JSON"), Loc.T("JSON"), "repair json fix correct broken", () => { Go(Json); Json.RepairCommand.Execute(null); }, "Ctrl+Shift+R"),
            new(Loc.T("Sort JSON keys"), Loc.T("JSON"), "sort json keys order alphabetical", () => { Go(Json); Json.SortKeysCommand.Execute(null); }),
            new(Loc.T("JSON tree, query, code and YAML"), Loc.T("JSON"), "json tree, query, code and yaml jsonpath tree view kotlin csharp typescript yaml panel", () => { Go(Json); Json.Panel.IsOpen = true; }),
            new(Loc.T("YAML to JSON"), Loc.T("JSON"), "yaml to json convert yml", () => { Go(Json); Json.FromYamlCommand.Execute(null); }),
            new(Loc.T("Open JSON file"), Loc.T("JSON"), "open json file load", () => { Go(Json); Json.OpenCommand.Execute(null); }, "Ctrl+O"),

            new(Loc.T("Compare texts"), Loc.T("Diff"), "compare texts diff run compare", () => { Go(Diff); Diff.CompareCommand.Execute(null); }, "Ctrl+Enter"),
            new(Loc.T("Open original file"), Loc.T("Diff"), "open original file left compare", () => { Go(Diff); Diff.OpenLeftCommand.Execute(null); }),
            new(Loc.T("Open modified file"), Loc.T("Diff"), "open modified file right compare", () => { Go(Diff); Diff.OpenRightCommand.Execute(null); }),
            new(Loc.T("Swap diff sides"), Loc.T("Diff"), "swap diff sides reverse", () => { Go(Diff); Diff.SwapCommand.Execute(null); }),
            new(Loc.T("Copy diff as patch"), Loc.T("Diff"), "copy diff as patch unified git", () => { Go(Diff); Diff.CopyPatchCommand.Execute(null); }),

            new(Loc.T("New password"), Loc.T("Generators"), "new password generate random secure", () => { Go(Generators); Generators.Tab = GeneratorTab.Password; Generators.GeneratePasswordsCommand.Execute(null); }),
            new(Loc.T("New secret key"), Loc.T("Generators"), "new secret key api jwt aes token random", () => { Go(Generators); Generators.Tab = GeneratorTab.Secret; Generators.GenerateSecretsCommand.Execute(null); }),
            new(Loc.T("New UUID / GUID"), Loc.T("Generators"), "new uuid / guid guid identifier v4 v7", () => { Go(Generators); Generators.Tab = GeneratorTab.Uuid; Generators.GenerateUuidsCommand.Execute(null); }),
            new(Loc.T("Lorem ipsum"), Loc.T("Generators"), "lorem ipsum placeholder text dummy", () => { Go(Generators); Generators.Tab = GeneratorTab.Lorem; }),
            new(Loc.T("Fake test data"), Loc.T("Generators"), "fake test data mock sample names emails csv", () => { Go(Generators); Generators.Tab = GeneratorTab.Fake; }),
            new(Loc.T("QR code"), Loc.T("Generators"), "qr code qrcode wifi link", () => { Go(Generators); Generators.Tab = GeneratorTab.Qr; }),

            new(Loc.T("Base64 encode / decode"), Loc.T("Encode"), "base64 encode / decode b64", () => { Go(Encode); Encode.Mode = CodecMode.Base64; }),
            new(Loc.T("Decode JWT"), Loc.T("Encode"), "decode jwt token jwt bearer claims", () => { Go(Encode); Encode.Mode = CodecMode.Jwt; }),
            new(Loc.T("Hash text (SHA-256, MD5…)"), Loc.T("Encode"), "hash text (sha-256, md5…) sha md5 hmac checksum", () => { Go(Encode); Encode.Mode = CodecMode.Hash; }),
            new(Loc.T("URL encode / decode"), Loc.T("Encode"), "url encode / decode percent escape", () => { Go(Encode); Encode.Mode = CodecMode.Url; }),
            new(Loc.T("Image to Base64"), Loc.T("Encode"), "image to base64 data uri picture png", () => { Go(Encode); Encode.Mode = CodecMode.Image; }),
            new(Loc.T("Unicode escape"), Loc.T("Encode"), "unicode escape \\u escape emoji", () => { Go(Encode); Encode.Mode = CodecMode.Unicode; }),

            new(Loc.T("Current Unix time"), Loc.T("Timestamp"), "current unix time now epoch", () => { Go(Timestamp); Timestamp.UseNowCommand.Execute(null); }),

            new(Loc.T("Toggle light / dark"), Loc.T("App"), "toggle light / dark theme dark light mode", () => ToggleTheme()),
            new(Loc.T("Toggle sidebar"), Loc.T("App"), "toggle sidebar collapse expand menu", () => ToggleSidebar()),
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
            return (ClipKind.Jwt, Loc.T("Your clipboard has a JWT."), Loc.T("Decode it"));
        if (t.Length >= 2 && t[0] is '{' or '[' && JsonTools.Validate(t) is { IsValid: true, RootKind: "object" or "array" })
            return (ClipKind.Json, Loc.T("Your clipboard has JSON."), Loc.T("Open in JSON"));
        if (t.Length is 10 or 13 && t.All(char.IsAsciiDigit) &&
            TimeTools.Parse(t, TimeZoneInfo.Utc) is { } p && p.Value.Year is > 2000 and < 2100)
            return (ClipKind.Timestamp, Loc.F("Your clipboard has a Unix timestamp ({0:yyyy-MM-dd HH:mm}).", p.Value.ToLocalTime()), Loc.T("Convert it"));
        if (t.Length >= 24 && t.Length % 4 == 0 && !t.Contains(' ') &&
            t.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=') && t.Any(char.IsDigit) && t.Any(char.IsUpper) && t.Any(char.IsLower))
        {
            try
            {
                var bytes = Convert.FromBase64String(t);
                var decoded = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                if (decoded.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t'))
                    return (ClipKind.Base64, Loc.T("Your clipboard looks like Base64 text."), Loc.T("Decode it"));
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

    // ------------------------------------------------------------------ updates

    public UpdateService Updates { get; }

    /// <summary>"Version x is installed. Restart to use it." (shown once an update has been put in place).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateHint))]
    private string? _updateHint;

    public bool HasUpdateHint => UpdateHint is not null;

    /// <summary>Raised to close the window and start the updated app.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Raised to close the app (e.g. the uninstaller has been started).</summary>
    public event EventHandler? QuitRequested;

    [RelayCommand]
    private void RestartForUpdate() => RestartRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DismissUpdate() => UpdateHint = null;
}
