using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using DeveloperTools;
using DeveloperTools.Services;

internal sealed partial class Program
{
    private static Task Main(string[] args)
    {
        // Settings and snippets live in this site's localStorage (per browser, per site).
        AppServices.Storage = new BrowserStorage();

        // Deep links: https://…/#json opens the JSON tool; #selftest runs the built-in checks.
        var hash = JSHost.GlobalThis.GetPropertyAsJSObject("location")?.GetPropertyAsString("hash")?.TrimStart('#');
        AppServices.StartPage = string.IsNullOrEmpty(hash) ? null : hash;
        AppServices.SelfTestReport = report =>
        {
            System.Console.WriteLine(report);
            JSHost.GlobalThis.GetPropertyAsJSObject("document")?.SetProperty("title", report.Split('\n')[0]);
        };
        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}

/// <summary>localStorage-backed app storage (keys are prefixed with "developertools:").</summary>
internal sealed partial class BrowserStorage : IAppStorage
{
    private const string Prefix = "developertools:";

    [JSImport("globalThis.localStorage.getItem")]
    private static partial string? GetItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    private static partial void SetItem(string key, string value);

    public string? Read(string name)
    {
        try { return GetItem(Prefix + name); }
        catch (JSException) { return null; } // storage disabled (private mode, blocked site data)
    }

    public void Write(string name, string content)
    {
        try { SetItem(Prefix + name, content); }
        catch (JSException) { /* storage full or disabled: keep working without persistence */ }
    }

    public string Describe(string name) => DeveloperTools.Localization.Loc.F("This browser’s local storage for this site ({0})", name);

    public string? LocalPath(string name) => null;
}
