using System;
using System.IO;

namespace DeveloperTools.Services;

/// <summary>Where the app keeps its own small files (settings, snippets).</summary>
public interface IAppStorage
{
    string? Read(string name);
    void Write(string name, string content);

    /// <summary>Human-readable location of an item (shown in Settings).</summary>
    string Describe(string name);

    /// <summary>A real file on disk that can be shown in the file manager.</summary>
    string? LocalPath(string name);
}

/// <summary>Desktop: %AppData%\DeveloperTools (Windows) or ~/.config/DeveloperTools (Linux).</summary>
public sealed class FileAppStorage : IAppStorage
{
    public static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeveloperTools");

    public string? Read(string name)
    {
        var path = Path.Combine(Directory, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Write(string name, string content)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, name), content);
    }

    public string Describe(string name) => Path.Combine(Directory, name);

    public string? LocalPath(string name) => Path.Combine(Directory, name);
}

/// <summary>Platform hooks set by the host app before startup.</summary>
public static class AppServices
{
    /// <summary>Settings/snippets storage (browser host replaces it with localStorage).</summary>
    public static IAppStorage Storage { get; set; } = new FileAppStorage();

    public static bool IsBrowser => OperatingSystem.IsBrowser();

    /// <summary>Tool to open at startup ("json", "diff", …), e.g. from a #json link in the browser.</summary>
    public static string? StartPage { get; set; }

    /// <summary>Receives the self-test summary (browser: shown in the page title).</summary>
    public static Action<string>? SelfTestReport { get; set; }
}
