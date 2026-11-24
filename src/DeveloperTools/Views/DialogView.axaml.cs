using System;
using Avalonia.Controls;
using Avalonia.Input;

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

/// <summary>Confirm / prompt dialog content, shown in an OS window or as an in-app overlay.</summary>
public partial class DialogView : UserControl
{
    private DialogOptions _options = new("", "", "OK");
    private Action<DialogResult?> _close = _ => { };

    public DialogView()
    {
        InitializeComponent();
        PrimaryButton.Click += (_, _) => Submit();
        SecondaryButton.Click += (_, _) => _close(new DialogResult("secondary", null));
        CancelButton.Click += (_, _) => _close(null);
        KeyDown += OnKeyDown;
    }

    public void Configure(DialogOptions options, Action<DialogResult?> close)
    {
        _options = options;
        _close = close;
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

    public void FocusFirst()
    {
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
            _close(new DialogResult("primary", null));
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

        _close(new DialogResult("primary", input));
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            _close(null);
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && (_options.HasInput || PrimaryButton.IsFocused))
        {
            Submit();
            args.Handled = true;
        }
    }
}
