using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class TextPage : UserControl
{
    private TextPageViewModel? _vm;

    public TextPage()
    {
        InitializeComponent();
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = true;
        foreach (var margin in Editor.TextArea.LeftMargins)
            if (margin is AvaloniaEdit.Editing.LineNumberMargin)
                margin.Margin = new Thickness(4, 0, 12, 0);
        Editor.TextArea.SelectionChanged += (_, _) => _vm?.UpdateSelection(Editor.SelectedText);
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
            _vm.TransformRequested -= OnTransform;
        _vm = DataContext as TextPageViewModel;
        if (_vm is not null)
            _vm.TransformRequested += OnTransform;
        base.OnDataContextChanged(e);
    }

    /// <summary>Applies to the selection when there is one, else the whole text; one undo step.</summary>
    private void OnTransform(object? sender, Func<string, string> transform)
    {
        var doc = Editor.Document;
        if (doc is null || doc.TextLength == 0)
            return;
        var area = Editor.TextArea;
        if (Editor.SelectionLength > 0)
        {
            var start = Editor.SelectionStart;
            var result = transform(Editor.SelectedText);
            doc.Replace(start, Editor.SelectionLength, result);
            Editor.Select(start, result.Length);
        }
        else
        {
            doc.Replace(0, doc.TextLength, transform(doc.Text));
        }
        area.Focus();
    }

    private void ApplyTheme()
    {
        Editor.TextArea.TextView.CurrentLineBackground = Resource("Editor.CurrentLine");
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent);
        Editor.TextArea.SelectionBrush = Resource("Selection");
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.CaretBrush = Resource("Fg.Primary");
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
