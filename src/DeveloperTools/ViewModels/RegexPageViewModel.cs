using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;

using DeveloperTools.Localization;

namespace DeveloperTools.ViewModels;

public sealed record RegexGroupItem(string Name, string Value, bool Success);

public partial class RegexMatchItem(int number, int index, int length, string value, IReadOnlyList<RegexGroupItem> groups, Action<RegexMatchItem> select)
{
    public int Number { get; } = number;
    public int Index { get; } = index;
    public int Length { get; } = length;
    public string Title => $"Match {Number}";
    public string Position => Length == 0 ? $"at {Index}" : $"{Index}–{Index + Length}";
    public string Value { get; } = value.Length == 0 ? "(empty)" : value.Replace("\n", "⏎").Replace("\r", "");
    public IReadOnlyList<RegexGroupItem> Groups { get; } = groups;
    public bool HasGroups => Groups.Count > 0;

    [RelayCommand]
    private void Select() => select(this);
}

public enum RegexPanel
{
    Matches,
    Replace,
    Code,
    Reference
}

public partial class RegexPageViewModel : PageViewModel
{
    private const int MaxListed = 1000;
    private const int MaxHighlighted = 20000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private int _version;

    public RegexPageViewModel(SettingsService settings, IDialogService dialogs, SnippetService snippets)
    {
        _settings = settings;
        _dialogs = dialogs;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _ = RunAsync();
        };
        TestDocument.TextChanged += (_, _) => Schedule();
        PropertyChanged += OnChanged;
        Snippets = new SnippetsViewModel(snippets, "regex", Capture, Restore);

        _pattern = @"(?<user>[\w.+-]+)@(?<domain>[\w-]+\.[\w.]+)";
        TestDocument.Text = "Contact: ada@example.com, grace.h@example.org\nInvalid: not-an-email@, @example.com\nSupport <help+tag@example.co.uk>";
    }

    public override string Title => Loc.T("Regex");
    public override string Subtitle => Loc.T("Test .NET regular expressions with live matches");

    public SnippetsViewModel Snippets { get; }
    public TextDocument TestDocument { get; } = new();
    public int EditorFontSize => _settings.Current.EditorFontSize;

    [ObservableProperty] private string _pattern;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private bool _multiline = true;
    [ObservableProperty] private bool _singleline;
    [ObservableProperty] private bool _ignoreWhitespace;
    [ObservableProperty] private bool _explicitCapture;
    [ObservableProperty] private string _replacement = "${user} at ${domain}";
    [ObservableProperty] private RegexPanel _panel = RegexPanel.Matches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasMatches;
    [ObservableProperty] private string _replaceOutput = "";
    [ObservableProperty] private string _csharpCode = "";
    [ObservableProperty] private string _javaScriptCode = "";

    public ObservableCollection<RegexMatchItem> Matches { get; } = new();

    /// <summary>Match ranges to highlight in the test text.</summary>
    public IReadOnlyList<(int Start, int Length)> Highlights { get; private set; } = Array.Empty<(int, int)>();

    public event EventHandler? HighlightsChanged;
    public event EventHandler<(int Start, int Length)>? SelectRequested;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Pattern) or nameof(IgnoreCase) or nameof(Multiline) or nameof(Singleline)
            or nameof(IgnoreWhitespace) or nameof(ExplicitCapture) or nameof(Replacement))
            Schedule();
    }

    private void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void RunNow() => _ = RunAsync();

    private RegexOptions Options =>
        (IgnoreCase ? RegexOptions.IgnoreCase : 0) |
        (Multiline ? RegexOptions.Multiline : 0) |
        (Singleline ? RegexOptions.Singleline : 0) |
        (IgnoreWhitespace ? RegexOptions.IgnorePatternWhitespace : 0) |
        (ExplicitCapture ? RegexOptions.ExplicitCapture : 0);

    private async Task RunAsync()
    {
        var version = ++_version;
        var pattern = Pattern;
        var text = TestDocument.Text;
        var options = Options;
        var replacement = Replacement;
        UpdateCode(pattern, options);

        if (pattern.Length == 0)
        {
            Apply(version, null, [], [], 0, "", "Type a pattern to see matches.");
            return;
        }

        try
        {
            var result = await Task.Run(() =>
            {
                var regex = new Regex(pattern, options, Timeout);
                var names = regex.GetGroupNames().Skip(1).ToArray(); // skip group 0
                var listed = new List<(int, int, string, List<RegexGroupItem>)>();
                var ranges = new List<(int, int)>();
                var count = 0;
                for (var m = regex.Match(text); m.Success; m = m.NextMatch())
                {
                    count++;
                    if (ranges.Count < MaxHighlighted)
                        ranges.Add((m.Index, m.Length));
                    if (listed.Count < MaxListed)
                    {
                        var groups = names.Select(n => m.Groups[n]).Select(g =>
                            new RegexGroupItem(char.IsDigit(g.Name[0]) ? "Group " + g.Name : g.Name,
                                g.Success ? (g.Value.Length == 0 ? "(empty)" : g.Value) : "(no match)", g.Success)).ToList();
                        listed.Add((m.Index, m.Length, m.Value, groups));
                    }
                }

                string replaced;
                try
                {
                    replaced = regex.Replace(text, replacement);
                }
                catch (ArgumentException ex)
                {
                    replaced = "Replacement error: " + ex.Message;
                }
                return (listed, ranges, count, replaced);
            });

            var summary = result.count switch
            {
                0 => Loc.T("No matches"),
                1 => Loc.T("1 match"),
                _ => Loc.F("{0:N0} matches", result.count)
            } + (result.count > MaxListed ? $" (first {MaxListed:N0} listed)" : "");
            Apply(version, null, result.listed, result.ranges, result.count, result.replaced, summary);
        }
        catch (RegexParseException ex)
        {
            Apply(version, $"Position {ex.Offset}: {Describe(ex.Error)}", [], [], 0, "", "Invalid pattern");
        }
        catch (ArgumentException ex)
        {
            Apply(version, ex.Message, [], [], 0, "", "Invalid pattern");
        }
        catch (RegexMatchTimeoutException)
        {
            Apply(version, "The pattern took too long (over 2 seconds). It may backtrack catastrophically: avoid nested quantifiers like (a+)+.", [], [], 0, "", "Timed out");
        }
    }

    private static string Describe(RegexParseError error)
    {
        // "UnterminatedBracket" -> "Unterminated bracket"
        var words = Tools.TextTools.Words(error.ToString());
        var text = string.Join(" ", words).ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    private void Apply(int version, string? error, List<(int, int, string, List<RegexGroupItem>)> listed,
        List<(int, int)> ranges, int count, string replaced, string summary)
    {
        if (version != _version)
            return;
        Error = error;
        Summary = summary;
        HasMatches = count > 0;
        Matches.Clear();
        var n = 0;
        foreach (var (index, length, value, groups) in listed)
            Matches.Add(new RegexMatchItem(++n, index, length, value, groups, SelectMatch));
        Highlights = ranges;
        ReplaceOutput = replaced;
        HighlightsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectMatch(RegexMatchItem item) => SelectRequested?.Invoke(this, (item.Index, item.Length));

    private void UpdateCode(string pattern, RegexOptions options)
    {
        var flags = new List<string>();
        if (options.HasFlag(RegexOptions.IgnoreCase)) flags.Add("RegexOptions.IgnoreCase");
        if (options.HasFlag(RegexOptions.Multiline)) flags.Add("RegexOptions.Multiline");
        if (options.HasFlag(RegexOptions.Singleline)) flags.Add("RegexOptions.Singleline");
        if (options.HasFlag(RegexOptions.IgnorePatternWhitespace)) flags.Add("RegexOptions.IgnorePatternWhitespace");
        if (options.HasFlag(RegexOptions.ExplicitCapture)) flags.Add("RegexOptions.ExplicitCapture");
        var verbatim = "@\"" + pattern.Replace("\"", "\"\"") + "\"";
        var opts = flags.Count == 0 ? "RegexOptions.None" : string.Join(" | ", flags);
        CsharpCode =
            $"// Source-generated (fast, checked at compile time)\n" +
            $"[GeneratedRegex({verbatim}, {opts})]\n" +
            $"private static partial Regex MyRegex();\n\n" +
            $"// Or at runtime\n" +
            $"var regex = new Regex({verbatim}, {opts}, TimeSpan.FromSeconds(1));\n" +
            $"foreach (Match m in regex.Matches(input))\n" +
            $"    Console.WriteLine(m.Value);";

        var js = pattern.Replace("/", "\\/").Replace("(?<", "(?<"); // named groups have the same syntax
        var jsFlags = "g" + (options.HasFlag(RegexOptions.IgnoreCase) ? "i" : "") + (options.HasFlag(RegexOptions.Multiline) ? "m" : "") +
                      (options.HasFlag(RegexOptions.Singleline) ? "s" : "");
        JavaScriptCode =
            $"const regex = /{js}/{jsFlags};\n" +
            "for (const m of input.matchAll(regex)) {\n" +
            "  console.log(m[0], m.groups);\n" +
            "}" +
            (options.HasFlag(RegexOptions.IgnorePatternWhitespace) ? "\n// Note: JavaScript has no ignore-whitespace (x) flag." : "");
    }

    [RelayCommand]
    private Task CopyCSharp() => _dialogs.CopyToClipboardAsync(CsharpCode);

    [RelayCommand]
    private Task CopyJavaScript() => _dialogs.CopyToClipboardAsync(JavaScriptCode);

    [RelayCommand]
    private Task CopyReplaceOutput() => _dialogs.CopyToClipboardAsync(ReplaceOutput);

    [RelayCommand]
    private Task CopyMatches() => _dialogs.CopyToClipboardAsync(string.Join("\n",
        Highlights.Select(h => TestDocument.GetText(h.Start, h.Length))));

    // ------------------------------------------------------------------ snippets

    // Written by hand (no reflection) so it also works in the native AOT build.
    private string Capture()
    {
        using var stream = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("pattern", Pattern);
            w.WriteString("text", TestDocument.Text);
            w.WriteString("replacement", Replacement);
            w.WriteBoolean("i", IgnoreCase);
            w.WriteBoolean("m", Multiline);
            w.WriteBoolean("s", Singleline);
            w.WriteBoolean("x", IgnoreWhitespace);
            w.WriteBoolean("n", ExplicitCapture);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private void Restore(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var r = doc.RootElement;
            Pattern = r.GetProperty("pattern").GetString() ?? "";
            TestDocument.Text = r.GetProperty("text").GetString() ?? "";
            Replacement = r.GetProperty("replacement").GetString() ?? "";
            IgnoreCase = r.GetProperty("i").GetBoolean();
            Multiline = r.GetProperty("m").GetBoolean();
            Singleline = r.GetProperty("s").GetBoolean();
            IgnoreWhitespace = r.GetProperty("x").GetBoolean();
            ExplicitCapture = r.GetProperty("n").GetBoolean();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Error = Loc.T("This snippet could not be read.");
        }
    }

    /// <summary>Quick reference shown in the side panel.</summary>
    public IReadOnlyList<(string Token, string Meaning)> Reference { get; } =
    [
        (".", "Any character except newline"),
        (@"\d  \w  \s", "Digit, word character, whitespace"),
        (@"\D  \W  \S", "Not digit / word / whitespace"),
        (@"\b", "Word boundary"),
        ("^  $", "Start / end of line (Multiline) or text"),
        ("[abc]  [^abc]  [a-z]", "Any of, none of, range"),
        ("*  +  ?", "0 or more, 1 or more, optional"),
        ("{3}  {2,5}  {2,}", "Exact, between, at least"),
        ("*?  +?", "Lazy (as few as possible)"),
        ("(…)", "Capturing group"),
        ("(?<name>…)", "Named group"),
        ("(?:…)", "Non-capturing group"),
        ("a|b", "Either a or b"),
        ("(?=…)  (?!…)", "Lookahead, negative lookahead"),
        ("(?<=…)  (?<!…)", "Lookbehind, negative lookbehind"),
        (@"\1  \k<name>", "Back-reference"),
        ("$1  ${name}  $0", "In replacement: group, named group, whole match")
    ];

    public IEnumerable<RefItem> ReferenceItems => Reference.Select(r => new RefItem(r.Token, r.Meaning));
}

public sealed record RefItem(string Token, string Meaning);
