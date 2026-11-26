using Avalonia.Controls;
using DeveloperTools.Localization;
using DeveloperTools.Services;

namespace DeveloperTools.Views;

/// <summary>Shown for "DeveloperTools --uninstall": one question (keep settings?) and a confirmation.</summary>
public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close();
        CloseButton.Click += (_, _) => Close();
        UninstallButton.Click += (_, _) =>
        {
            var keep = KeepSettings.IsChecked == true;
            UninstallService.Uninstall(keep);
            AskPanel.IsVisible = false;
            DonePanel.IsVisible = true;
            DoneText.Text = keep
                ? Loc.T("Your settings and snippets were kept. Thanks for using DeveloperTools.")
                : Loc.T("Your settings and snippets were removed too. Thanks for using DeveloperTools.");
        };
    }
}
