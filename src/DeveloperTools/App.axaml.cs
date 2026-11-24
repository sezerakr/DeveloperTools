using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using DeveloperTools.Services;
using Avalonia.Controls;
using DeveloperTools.ViewModels;
using DeveloperTools.Views;

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
            var viewModel = new MainWindowViewModel(settings, new DialogService(() => window, window.View));
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            _ = viewModel.Markdown.RefreshAsync();
            viewModel.NavigateTo(AppServices.StartPage);

            if (SelfTest.LogPath is not null)
                window.Opened += async (_, _) => await SelfTest.RunAsync(viewModel);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            // Browser: the whole app is one view inside the page.
            var settings = new SettingsService();
            ApplyTheme(settings.Current.Theme);

            var view = new MainView();
            var viewModel = new MainWindowViewModel(settings, new DialogService(() => TopLevel.GetTopLevel(view), view));
            view.DataContext = viewModel;
            single.MainView = view;
            _ = viewModel.Markdown.RefreshAsync();
            if (AppServices.StartPage == "selftest")
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1000); // let the first frame render
                    try { await SelfTest.RunAsync(viewModel); }
                    catch (System.Exception ex) { AppServices.SelfTestReport?.Invoke("SELFTEST CRASHED: " + ex); }
                });
            else
                viewModel.NavigateTo(AppServices.StartPage);
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
