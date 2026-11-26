using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using DeveloperTools.Services;

namespace DeveloperTools.Localization;

/// <summary>A language the user can pick (built in, or a JSON file in the user's lang folder).</summary>
public sealed record LanguageInfo(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Translations. Language files are flat JSON objects mapping the English text to the translation:
/// <c>{ "_language": "Türkçe", "Copy all": "Tümünü kopyala" }</c>. Built-in files live in Assets/Lang;
/// a file with the same name in the user's lang folder (%AppData%\DeveloperTools\lang) overrides single
/// entries, and new files there add languages. Loaded once at startup; a lookup is one dictionary read.
/// Missing entries fall back to the English text.
/// </summary>
public static class Loc
{
    private const string BuiltInFolder = "avares://DeveloperTools.Core/Assets/Lang/";
    private static readonly string[] BuiltIn = ["en", "tr"];
    private static Dictionary<string, string> _map = new(StringComparer.Ordinal);

    /// <summary>The active language code ("en", "tr", …).</summary>
    public static string Code { get; private set; } = "en";

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>Folder for user translation files (desktop only).</summary>
    public static string? UserFolder => AppServices.Storage.LocalPath("lang");

    /// <summary>Translates an English UI text (returns it unchanged when there is no translation).</summary>
    public static string T(string text) => _map.TryGetValue(text, out var t) && t.Length > 0 ? t : text;

    /// <summary>Translates a format string, then fills in the arguments.</summary>
    public static string F(string format, params object?[] args) => string.Format(Culture, T(format), args);

    /// <summary>Loads a language: "System" picks Turkish on Turkish systems and English otherwise.</summary>
    public static void Load(string? setting)
    {
        var code = string.IsNullOrEmpty(setting) || setting == "System"
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en")
            : setting;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (code != "en")
        {
            Merge(map, ReadBuiltIn(code));
            Merge(map, ReadUser(code));
        }
        else
        {
            Merge(map, ReadUser("en")); // lets users reword the English texts too
        }

        _map = map;
        Code = code;
        try { Culture = CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
    }

    /// <summary>Built-in languages plus any valid file in the user's lang folder.</summary>
    public static IReadOnlyList<LanguageInfo> Available()
    {
        var list = new List<LanguageInfo> { new("System", T("System language")) };
        var seen = new HashSet<string>();
        foreach (var code in BuiltIn)
        {
            seen.Add(code);
            list.Add(new LanguageInfo(code, NameOf(code, ReadBuiltIn(code))));
        }

        if (UserFolder is { } folder && Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (!seen.Add(code))
                    continue;
                var entries = ReadUser(code);
                if (entries.Count > 0)
                    list.Add(new LanguageInfo(code, NameOf(code, entries)));
            }
        }
        return list;
    }

    private static string NameOf(string code, Dictionary<string, string> entries) =>
        entries.TryGetValue("_language", out var n) && n.Length > 0 ? n : code == "en" ? "English" : code;

    /// <summary>
    /// Copies the full built-in file for a language into the user folder (if not there yet), so it can be
    /// edited in any text editor. Returns its path, or null where there is no file system (browser).
    /// </summary>
    public static string? ExportForEditing(string code)
    {
        if (UserFolder is not { } folder || code == "System")
            return null;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, code + ".json");
        if (File.Exists(path))
            return path;

        var builtIn = code == "en" ? new Dictionary<string, string>() : ReadBuiltIn(code);
        if (code == "en")
        {
            // English: list every known text (keys of the Turkish file) mapped to itself.
            builtIn["_language"] = "English";
            foreach (var key in ReadBuiltIn("tr").Keys.Where(k => !k.StartsWith('_')))
                builtIn[key] = key;
        }

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();
        foreach (var (key, value) in builtIn)
            writer.WriteString(key, value);
        writer.WriteEndObject();
        return path;
    }

    // ------------------------------------------------------------------ reading

    private static Dictionary<string, string> ReadBuiltIn(string code)
    {
        try
        {
            var uri = new Uri(BuiltInFolder + code + ".json");
            if (!AssetLoader.Exists(uri))
                return new();
            using var stream = AssetLoader.Open(uri);
            using var doc = JsonDocument.Parse(stream);
            return ToMap(doc);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new();
        }
    }

    private static Dictionary<string, string> ReadUser(string code)
    {
        try
        {
            var text = AppServices.Storage.Read(Path.Combine("lang", code + ".json"));
            if (string.IsNullOrWhiteSpace(text))
                return new();
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return ToMap(doc);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(); // a broken user file never breaks the app
        }
    }

    private static Dictionary<string, string> ToMap(JsonDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return map;
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String)
                map[p.Name] = p.Value.GetString() ?? "";
        return map;
    }

    private static void Merge(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var (k, v) in source)
            if (v.Length > 0)
                target[k] = v;
    }
}

/// <summary>XAML: <c>Text="{l:T 'Copy all'}"</c>, translated once when the view is created.</summary>
public sealed class T : MarkupExtension
{
    public T(string text) => Text = text;

    public string Text { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
