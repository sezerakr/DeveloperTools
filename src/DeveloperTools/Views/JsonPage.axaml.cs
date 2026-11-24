using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using DeveloperTools.Markdown;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class JsonPage : UserControl
{
    private readonly LineTokenColorizer _colorizer = new("json");
    private readonly ErrorMarker _errorMarker = new();
    private readonly LineTokenColorizer _queryColors = new("json");
    private readonly LineTokenColorizer _codeColors = new("cs");
    private readonly LineTokenColorizer _yamlColors = new("yaml");
    private JsonPageViewModel? _vm;
    private JsonPanelViewModel? _panel;
    private bool? _stacked;

    public JsonPage()
    {
        InitializeComponent();

        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
        foreach (var margin in Editor.TextArea.LeftMargins)
            if (margin is AvaloniaEdit.Editing.LineNumberMargin)
                margin.Margin = new Thickness(4, 0, 14, 0);

        var textView = Editor.TextArea.TextView;
        textView.LineTransformers.Add(_colorizer);
        textView.BackgroundRenderers.Add(_errorMarker);
        SearchPanel.Install(Editor); // Ctrl+F / F3 inside the editor

        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();

        foreach (var (view, colors) in new[] { (QueryView, _queryColors), (CodeView, _codeColors), (YamlView, _yamlColors) })
        {
            view.Options.EnableHyperlinks = false;
            view.Options.EnableEmailHyperlinks = false;
            view.TextArea.TextView.LineTransformers.Add(colors);
            view.Document = new TextDocument();
        }

        SizeChanged += (_, e) => ApplyBody(e.NewSize.Width < 900);
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        UpdateCaret();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.GoToRequested -= OnGoToRequested;
        }
        if (_panel is not null)
            _panel.PropertyChanged -= OnPanelPropertyChanged;

        _vm = DataContext as JsonPageViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.GoToRequested += OnGoToRequested;
            Dispatcher.UIThread.Post(() => Editor.TextArea.Focus(), DispatcherPriority.Background);
            _panel = _vm.Panel;
            _panel.PropertyChanged += OnPanelPropertyChanged;
            ShowPanelOutputs();
        }

        UpdateErrorMarker();
        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JsonPageViewModel.Check))
            UpdateErrorMarker();
        else if (e.PropertyName == nameof(JsonPageViewModel.SelectedIndent) && _vm is not null)
            Editor.Options.IndentationSize = _vm.SelectedIndent == "4 spaces" ? 4 : 2;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JsonPanelViewModel.QueryOutput) or nameof(JsonPanelViewModel.CodeOutput)
            or nameof(JsonPanelViewModel.YamlOutput) or nameof(JsonPanelViewModel.CodeLanguage))
            ShowPanelOutputs();
        else if (e.PropertyName == nameof(JsonPanelViewModel.IsOpen))
        {
            _stacked = null;
            ApplyBody(Bounds.Width < 900);
        }
    }

    private void ShowPanelOutputs()
    {
        if (_panel is null)
            return;
        _codeColors.Language = _panel.CodeLanguage;
        SetText(QueryView, _panel.QueryOutput);
        SetText(CodeView, _panel.CodeOutput);
        SetText(YamlView, _panel.YamlOutput);
    }

    private static void SetText(AvaloniaEdit.TextEditor view, string text)
    {
        if (view.Document.Text != text)
            view.Document.Text = text;
        view.TextArea.TextView.Redraw();
    }

    /// <summary>Side panel at the right, or under the editor on narrow windows.</summary>
    private void ApplyBody(bool stacked)
    {
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        var editorPanel = Body.Children[0];
        if (stacked)
        {
            Body.ColumnDefinitions = new ColumnDefinitions("*");
            Body.RowDefinitions = new RowDefinitions("*,Auto,*");
            Grid.SetRow(PanelSplitter, 1);
            Grid.SetColumn(PanelSplitter, 0);
            Grid.SetRow(SidePanel, 2);
            Grid.SetColumn(SidePanel, 0);
            PanelSplitter.ResizeDirection = GridResizeDirection.Rows;
            PanelSplitter.Width = double.NaN;
            PanelSplitter.Height = 1;
            SidePanel.Width = double.NaN;
        }
        else
        {
            Body.RowDefinitions = new RowDefinitions("*");
            Body.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
            Grid.SetRow(PanelSplitter, 0);
            Grid.SetColumn(PanelSplitter, 1);
            Grid.SetRow(SidePanel, 0);
            Grid.SetColumn(SidePanel, 2);
            PanelSplitter.ResizeDirection = GridResizeDirection.Columns;
            PanelSplitter.Width = 1;
            PanelSplitter.Height = double.NaN;
            SidePanel.Width = 440;
        }
        Grid.SetRow(editorPanel, 0);
        Grid.SetColumn(editorPanel, 0);
    }

    private void OnClosePanel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_panel is not null)
            _panel.IsOpen = false;
    }

    private void UpdateErrorMarker()
    {
        _errorMarker.Offset = _vm is { IsInvalid: true } vm ? vm.Check.Offset : -1;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void OnGoToRequested(object? sender, int offset)
    {
        var document = Editor.Document;
        if (document is null)
            return;
        offset = Math.Clamp(offset, 0, document.TextLength);
        Editor.TextArea.Caret.Offset = offset;
        Editor.TextArea.ClearSelection();
        var location = document.GetLocation(offset);
        Editor.ScrollTo(location.Line, location.Column);
        Editor.TextArea.Focus();
    }

    private void UpdateCaret()
    {
        var caret = Editor.TextArea.Caret;
        CaretText.Text = $"Ln {caret.Line}, Col {caret.Column}";
    }

    private void ApplyTheme()
    {
        var textView = Editor.TextArea.TextView;
        textView.CurrentLineBackground = Resource("Editor.CurrentLine");
        textView.CurrentLineBorder = new Pen(Brushes.Transparent);
        Editor.TextArea.SelectionBrush = Resource("Selection");
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.CaretBrush = Resource("Fg.Primary");

        var palette = new Dictionary<TokenKind, IBrush>();
        foreach (var kind in Enum.GetValues<TokenKind>())
            palette[kind] = Resource("Syntax." + kind);
        _colorizer.Palette = palette;
        _queryColors.Palette = palette;
        _codeColors.Palette = palette;
        _yamlColors.Palette = palette;
        foreach (var view in new[] { QueryView, CodeView, YamlView })
        {
            view.TextArea.SelectionBrush = Resource("Selection");
            view.TextArea.SelectionBorder = null;
            view.TextArea.SelectionForeground = null;
            view.TextArea.TextView.Redraw();
        }
        _errorMarker.LineBrush = Resource("Error.Line");
        _errorMarker.UnderlineBrush = Resource("Danger");
        textView.Redraw();
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}

/// <summary>Colours each line with the shared code tokenizer (per line, so only visible lines are scanned).</summary>
public sealed class LineTokenColorizer(string? language = null) : DocumentColorizingTransformer
{
    /// <summary>Language for CodeHighlighter (e.g. "json", "cs"); null leaves text plain.</summary>
    public string? Language { get; set; } = language;

    private const int MaxLineLength = 20_000; // very long (minified) lines stay plain for speed

    public IReadOnlyDictionary<TokenKind, IBrush> Palette { get; set; } = new Dictionary<TokenKind, IBrush>();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Language is null || line.Length == 0 || line.Length > MaxLineLength)
            return;

        var text = CurrentContext.Document.GetText(line);
        foreach (var token in CodeHighlighter.Tokenize(text, Language))
        {
            if (token.Length <= 0 || !Palette.TryGetValue(token.Kind, out var brush))
                continue;
            ChangeLinePart(line.Offset + token.Start, line.Offset + token.Start + token.Length,
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}

/// <summary>Tints the line with a syntax error and underlines the character at the error.</summary>
public sealed class ErrorMarker : IBackgroundRenderer
{
    public int Offset { get; set; } = -1;
    public IBrush LineBrush { get; set; } = Brushes.Transparent;
    public IBrush UnderlineBrush { get; set; } = Brushes.Red;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = textView.Document;
        if (Offset < 0 || document is null || Offset > document.TextLength || !textView.VisualLinesValid)
            return;

        var line = document.GetLineByOffset(Offset);
        foreach (var visual in textView.VisualLines)
        {
            if (visual.FirstDocumentLine.LineNumber > line.LineNumber || visual.LastDocumentLine.LineNumber < line.LineNumber)
                continue;
            var top = visual.VisualTop - textView.VerticalOffset;
            drawingContext.FillRectangle(LineBrush, new Rect(0, top, textView.Bounds.Width, visual.Height));
        }

        // Underline the offending character (or the last one when the error is at the end of a line).
        var start = Offset;
        if (start >= line.EndOffset && line.Length > 0)
            start = line.EndOffset - 1;
        var segment = new TextSegment { StartOffset = start, Length = Math.Min(1, document.TextLength - start) };
        if (segment.Length == 0)
            return;
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            drawingContext.FillRectangle(UnderlineBrush, new Rect(rect.Left, rect.Bottom - 2, Math.Max(rect.Width, 6), 2));
    }
}
