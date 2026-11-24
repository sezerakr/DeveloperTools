using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DeveloperTools.Tools;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class DiffPage : UserControl
{
    private const double StackBreakpoint = 760;

    private readonly DiffPalette _palette = new();
    private readonly System.Collections.Generic.List<LineTokenColorizer> _syntax = new();
    private readonly ResultView _left, _right, _inline;
    private DiffPageViewModel? _vm;
    private bool _syncing;
    private bool? _stacked;

    /// <summary>A read-only result editor with its renderers.</summary>
    private sealed class ResultView
    {
        public readonly TextEditor Editor;
        public readonly DiffBackgroundRenderer Background;
        public readonly FoldColorizer Folds;
        public readonly DiffMargin Margin;
        public DiffDocument? Doc;

        public ResultView(TextEditor editor, DiffPalette palette, bool twoColumns)
        {
            Editor = editor;
            Background = new DiffBackgroundRenderer(palette);
            Folds = new FoldColorizer(palette);
            Margin = new DiffMargin(palette, twoColumns);
            editor.ShowLineNumbers = false;
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;
            editor.Options.HighlightCurrentLine = false;
            editor.TextArea.LeftMargins.Insert(0, Margin);
            editor.TextArea.TextView.BackgroundRenderers.Add(Background);
            editor.TextArea.TextView.LineTransformers.Add(Folds);
            editor.Document = new TextDocument();
        }

        public void Show(DiffDocument doc)
        {
            Doc = doc;
            Background.Lines = doc.Lines;
            Background.Current = (-1, -1);
            Folds.Lines = doc.Lines;
            Margin.Lines = doc.Lines;
            var max = doc.Lines.Count == 0 ? 0 : doc.Lines.Max(l => Math.Max(l.Number, l.Number2));
            Margin.Digits = Math.Max(2, max.ToString().Length);
            Margin.FontSize = Editor.FontSize;
            Margin.Font = Editor.FontFamily;
            Editor.Document = new TextDocument(doc.Text);
            Margin.Refresh();
        }
    }

    public DiffPage()
    {
        InitializeComponent();

        foreach (var editor in new[] { LeftEditor, RightEditor })
        {
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;
            editor.Options.HighlightCurrentLine = true;
            editor.Options.ConvertTabsToSpaces = false;
            foreach (var margin in editor.TextArea.LeftMargins)
                if (margin is AvaloniaEdit.Editing.LineNumberMargin)
                    margin.Margin = new Thickness(4, 0, 12, 0);
            DragDrop.SetAllowDrop(editor, true);
            editor.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            editor.AddHandler(DragDrop.DropEvent, OnDrop);
        }

        foreach (var editor in new[] { LeftEditor, RightEditor, LeftView, RightView, InlineView })
        {
            var colorizer = new LineTokenColorizer();
            _syntax.Add(colorizer);
            editor.TextArea.TextView.LineTransformers.Insert(0, colorizer);
        }

        _left = new ResultView(LeftView, _palette, twoColumns: false);
        _right = new ResultView(RightView, _palette, twoColumns: false);
        _inline = new ResultView(InlineView, _palette, twoColumns: true);

        // Side-by-side scrolls together (vertically and horizontally).
        LeftView.TextArea.TextView.ScrollOffsetChanged += (_, _) => Sync(LeftView, RightView);
        RightView.TextArea.TextView.ScrollOffsetChanged += (_, _) => Sync(RightView, LeftView);
        InlineView.TextArea.TextView.ScrollOffsetChanged += (_, _) => UpdateViewport();
        LeftView.TextArea.TextView.SizeChanged += (_, _) => UpdateViewport();
        InlineView.TextArea.TextView.SizeChanged += (_, _) => UpdateViewport();

        SideOverview.Navigate += (_, f) => ScrollToFraction(LeftView, f);
        InlineOverview.Navigate += (_, f) => ScrollToFraction(InlineView, f);

        // Double-click a line in the result to edit it at that spot.
        LeftView.DoubleTapped += (_, e) => JumpToSource(_left, e, leftSide: true);
        RightView.DoubleTapped += (_, e) => JumpToSource(_right, e, leftSide: false);
        InlineView.DoubleTapped += (_, e) => JumpToSource(_inline, e, leftSide: null);

        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < StackBreakpoint);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ResultReady -= OnResultReady;
            _vm.NavigateRequested -= OnNavigateRequested;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as DiffPageViewModel;
        if (_vm is not null)
        {
            _vm.ResultReady += OnResultReady;
            _vm.NavigateRequested += OnNavigateRequested;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            if (_vm.LeftResult is not null)
                OnResultReady(this, EventArgs.Empty);
            ApplyLanguage();
        }

        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPageViewModel.View) && _vm is { IsResultView: true })
        {
            _vm.OnViewSwitched();
            Dispatcher.UIThread.Post(UpdateViewport, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(DiffPageViewModel.Language))
        {
            ApplyLanguage();
        }
        else if (e.PropertyName == nameof(DiffPageViewModel.EditorFontSize))
        {
            OnResultReady(this, EventArgs.Empty);
        }
    }

    // ------------------------------------------------------------------ layout + theme

    private void ApplyLayout(bool stacked)
    {
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        if (stacked)
        {
            EditGrid.ColumnDefinitions = new ColumnDefinitions("*");
            EditGrid.RowDefinitions = new RowDefinitions("*,Auto,*");
            Grid.SetColumn(RightPane, 0);
            Grid.SetRow(EditDivider, 1);
            Grid.SetColumn(EditDivider, 0);
            Grid.SetRow(RightPane, 2);
            EditDivider.Width = double.NaN;
            EditDivider.Height = 1;
        }
        else
        {
            EditGrid.RowDefinitions = new RowDefinitions("*");
            EditGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto,*");
            Grid.SetRow(RightPane, 0);
            Grid.SetRow(EditDivider, 0);
            Grid.SetColumn(EditDivider, 1);
            Grid.SetColumn(RightPane, 2);
            EditDivider.Width = 1;
            EditDivider.Height = double.NaN;
        }
    }

    private void ApplyTheme()
    {
        _palette.AddedBg = Resource("Diff.AddedBg");
        _palette.AddedStrong = Resource("Diff.AddedStrong");
        _palette.RemovedBg = Resource("Diff.RemovedBg");
        _palette.RemovedStrong = Resource("Diff.RemovedStrong");
        _palette.Filler = Resource("Diff.Filler");
        _palette.FillerHatch = new Pen(Resource("Diff.Hatch"), 1);
        _palette.FoldBg = Resource("Bg.Subtle");
        _palette.FoldFg = Resource("Fg.Muted");
        _palette.AddedFg = Resource("Diff.Added");
        _palette.RemovedFg = Resource("Diff.Removed");
        _palette.ModifiedMark = Resource("Diff.Modified");
        _palette.NumberFg = Resource("Fg.Muted");
        _palette.CurrentChange = Resource("Diff.Modified");

        foreach (var overview in new[] { SideOverview, InlineOverview })
        {
            overview.Palette = _palette;
            overview.Track = Resource("Bg.Subtle");
            overview.ViewportBrush = Resource("Diff.Viewport");
            overview.InvalidateVisual();
        }

        foreach (var editor in new[] { LeftEditor, RightEditor, LeftView, RightView, InlineView })
        {
            editor.TextArea.TextView.CurrentLineBackground = Resource("Editor.CurrentLine");
            editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent);
            editor.TextArea.SelectionBrush = Resource("Selection");
            editor.TextArea.SelectionBorder = null;
            editor.TextArea.SelectionForeground = null;
            editor.TextArea.CaretBrush = Resource("Fg.Primary");
            editor.TextArea.TextView.Redraw();
        }

        foreach (var view in new[] { _left, _right, _inline })
            view.Margin.InvalidateVisual();

        var syntax = new System.Collections.Generic.Dictionary<DeveloperTools.Markdown.TokenKind, IBrush>();
        foreach (var kind in Enum.GetValues<DeveloperTools.Markdown.TokenKind>())
            syntax[kind] = Resource("Syntax." + kind);
        foreach (var colorizer in _syntax)
            colorizer.Palette = syntax;
    }

    private void ApplyLanguage()
    {
        var language = _vm?.Language;
        foreach (var colorizer in _syntax)
            colorizer.Language = language;
        foreach (var editor in new[] { LeftEditor, RightEditor, LeftView, RightView, InlineView })
            editor.TextArea.TextView.Redraw();
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    // ------------------------------------------------------------------ results

    private void OnResultReady(object? sender, EventArgs e)
    {
        if (_vm?.LeftResult is null || _vm.RightResult is null || _vm.InlineResult is null)
            return;
        _left.Show(_vm.LeftResult);
        _right.Show(_vm.RightResult);
        _inline.Show(_vm.InlineResult);
        SideOverview.SetLines(_vm.LeftResult.Lines, _vm.RightResult.Lines);
        InlineOverview.SetLines(_vm.InlineResult.Lines);
        Dispatcher.UIThread.Post(UpdateViewport, DispatcherPriority.Background);
    }

    private void OnNavigateRequested(object? sender, int line)
    {
        var views = _vm is { IsInline: true } ? new[] { _inline } : new[] { _left, _right };
        foreach (var view in views)
        {
            if (view.Doc is null)
                continue;
            // Highlight the whole change block with a bar in the gutter edge.
            var last = line;
            while (last + 1 < view.Doc.Lines.Count && view.Doc.Lines[last + 1].Tone is LineTone.Added or LineTone.Removed or LineTone.Filler)
                last++;
            view.Background.Current = (line, last);
            view.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }

        var editor = views[0].Editor;
        Dispatcher.UIThread.Post(() =>
        {
            var textView = editor.TextArea.TextView;
            textView.EnsureVisualLines();
            var top = textView.GetVisualTopByDocumentLine(Math.Clamp(line + 1, 1, Math.Max(1, editor.Document.LineCount)));
            editor.ScrollToVerticalOffset(Math.Max(0, top - textView.Bounds.Height / 3));
        }, DispatcherPriority.Background);
    }

    private void Sync(TextEditor from, TextEditor to)
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            to.ScrollToVerticalOffset(from.VerticalOffset);
            to.ScrollToHorizontalOffset(from.HorizontalOffset);
        }
        finally
        {
            _syncing = false;
        }

        UpdateViewport();
    }

    private void UpdateViewport()
    {
        void Set(TextEditor editor, DiffOverview overview)
        {
            var textView = editor.TextArea.TextView;
            var height = textView.DocumentHeight;
            if (height <= 0)
            {
                overview.SetViewport(0, 1);
                return;
            }
            overview.SetViewport(editor.VerticalOffset / height, Math.Min(1, textView.Bounds.Height / height));
        }

        Set(LeftView, SideOverview);
        Set(InlineView, InlineOverview);
    }

    private static void ScrollToFraction(TextEditor editor, double fraction)
    {
        var textView = editor.TextArea.TextView;
        editor.ScrollToVerticalOffset(Math.Max(0, fraction * textView.DocumentHeight - textView.Bounds.Height / 2));
    }

    /// <summary>Opens the edit view at the source line under the pointer.</summary>
    private void JumpToSource(ResultView view, TappedEventArgs e, bool? leftSide)
    {
        if (_vm is null || view.Doc is null)
            return;
        var textView = view.Editor.TextArea.TextView;
        var position = e.GetPosition(textView);
        var visual = textView.GetVisualLineFromVisualTop(position.Y + textView.VerticalOffset);
        if (visual is null)
            return;
        var index = visual.FirstDocumentLine.LineNumber - 1;
        if (index < 0 || index >= view.Doc.Lines.Count)
            return;
        var line = view.Doc.Lines[index];

        var useLeft = leftSide ?? line.Number > 0;
        var number = leftSide is null ? (useLeft ? line.Number : line.Number2) : line.Number;
        if (number <= 0)
            return;

        _vm.View = DiffView.Edit;
        var editor = useLeft ? LeftEditor : RightEditor;
        Dispatcher.UIThread.Post(() =>
        {
            var doc = editor.Document;
            var target = Math.Clamp(number, 1, doc.LineCount);
            editor.TextArea.Caret.Line = target;
            editor.TextArea.Caret.Column = 1;
            editor.ScrollTo(target, 1);
            editor.TextArea.Focus();
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null)
            return;
        var files = e.DataTransfer.TryGetFiles();
        var paths = files?.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
        if (paths is null || paths.Count == 0)
            return;
        e.Handled = true;

        if (paths.Count >= 2)
        {
            // Two files dropped at once: left and right.
            await _vm.LoadFileAsync(paths[0], left: true);
            await _vm.LoadFileAsync(paths[1], left: false);
            return;
        }

        await _vm.LoadFileAsync(paths[0], left: ReferenceEquals(sender, LeftEditor));
    }
}
