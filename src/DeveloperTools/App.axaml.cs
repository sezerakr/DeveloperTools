using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using DeveloperTools.Services;
using DeveloperTools.ViewModels;

namespace DeveloperTools;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            ApplyTheme(settings.Current.Theme);

            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(settings, new DialogService(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            _ = viewModel.Markdown.RefreshAsync();

            if (SelfTest.LogPath is not null)
                window.Opened += async (_, _) => await SelfTest.RunAsync(window, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string theme)
    {
        if (Current is null)
            return;
        Current.RequestedThemeVariant = ThemeFromSetting(theme);
    }

    public static ThemeVariant ThemeFromSetting(string theme) => theme switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}
