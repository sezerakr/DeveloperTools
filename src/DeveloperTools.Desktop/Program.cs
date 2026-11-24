using System;
using Avalonia;

namespace DeveloperTools.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var test = Array.IndexOf(args, "--selftest");
        if (test >= 0)
            SelfTest.LogPath = test + 1 < args.Length ? args[test + 1] : "selftest.log";
        var page = Array.IndexOf(args, "--page");
        if (page >= 0 && page + 1 < args.Length)
            DeveloperTools.Services.AppServices.StartPage = args[page + 1];
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
