using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DeveloperTools.Views;

public sealed record DialogOptions(
    string Title,
    string Message,
    string PrimaryText,
    string? SecondaryText = null,
    bool Danger = false,
    bool HasInput = false,
    string? InitialInput = null,
    Func<string, string?>? Validate = null);

public sealed record DialogResult(string Button, string? Input);

public partial class DialogWindow : Window
{
    private DialogOptions _options = new("", "", "OK");

    public DialogWindow()
    {
        InitializeComponent();
        PrimaryButton.Click += (_, _) => Submit();
        SecondaryButton.Click += (_, _) => Close(new DialogResult("secondary", null));
        CancelButton.Click += (_, _) => Close(null);
        KeyDown += OnKeyDown;
    }

    public static Task<DialogResult?> ShowAsync(Window owner, DialogOptions options)
    {
        var dialog = new DialogWindow();
        dialog.Configure(options);
        return dialog.ShowDialog<DialogResult?>(owner);
    }

    private void Configure(DialogOptions options)
    {
        _options = options;
        Title = options.Title;
        TitleText.Text = options.Title;
        MessageText.Text = options.Message;
        MessageText.IsVisible = !string.IsNullOrEmpty(options.Message);
        PrimaryButton.Content = options.PrimaryText;
        if (options.Danger)
        {
            PrimaryButton.Classes.Remove("primary");
            PrimaryButton.Classes.Add("danger");
        }

        if (options.SecondaryText is not null)
        {
            SecondaryButton.Content = options.SecondaryText;
            SecondaryButton.IsVisible = true;
        }

        if (options.HasInput)
        {
            InputPanel.IsVisible = true;
            InputBox.Text = options.InitialInput ?? "";
            InputBox.TextChanged += (_, _) => ErrorText.IsVisible = false;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_options.HasInput)
        {
            InputBox.Focus();
            // Select the name part only, so typing replaces "untitled" but keeps ".md".
            var text = InputBox.Text ?? "";
            var dot = text.LastIndexOf('.');
            InputBox.SelectionStart = 0;
            InputBox.SelectionEnd = dot > 0 ? dot : text.Length;
        }
        else
        {
            PrimaryButton.Focus();
        }
    }

    private void Submit()
    {
        if (!_options.HasInput)
        {
            Close(new DialogResult("primary", null));
            return;
        }

        var input = (InputBox.Text ?? "").Trim();
        var error = _options.Validate?.Invoke(input);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        Close(new DialogResult("primary", input));
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (_options.HasInput || PrimaryButton.IsFocused))
        {
            Submit();
            e.Handled = true;
        }
    }
}
