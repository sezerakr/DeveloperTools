using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DeveloperTools.Markdown;
using DeveloperTools.Models;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class MarkdownPage : UserControl
{
    /// <summary>Below this page width the explorer hides automatically.</summary>
    private const double ExplorerBreakpoint = 760;

    private const double DefaultExplorerWidth = 264;

    private readonly MarkdownEditing _editing;
    private readonly CodeBlockColorizer _codeColorizer;
    private readonly PreviewSelection _selection;
    private MarkdownPageViewModel? _vm;
    private MarkdownRenderer? _renderer;
    private bool? _wasNarrow;
    private double _explorerWidth = DefaultExplorerWidth;

    public MarkdownPage()
    {
        InitializeComponent();

        _editing = new MarkdownEditing(Editor);
        _codeColorizer = new CodeBlockColorizer(Editor.TextArea.TextView);
        Editor.TextArea.TextView.LineTransformers.Add(_codeColorizer);
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
        // Breathing room between the line numbers and the text.
        foreach (var margin in Editor.TextArea.LeftMargins)
            if (margin is AvaloniaEdit.Editing.LineNumberMargin)
                margin.Margin = new Thickness(4, 0, 14, 0);

        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncPreviewScroll();

        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyEditorTheme();
            RenderPreview();
        };
        ApplyEditorTheme();

        FindBox.KeyDown += OnFindBoxKeyDown;
        _selection = new PreviewSelection(PreviewScroll, PreviewHost, OnLinkClicked);

        // Scrollbar markers follow layout changes.
        EditorPanel.SizeChanged += (_, _) => UpdateMarkers();
        PreviewScroll.SizeChanged += (_, _) => UpdateMarkers();

        PageGrid.SizeChanged += (_, e) => ApplyResponsive(e.NewSize.Width);
        DocumentArea.SizeChanged += (_, e) => ApplyToolbarDensity(e.NewSize.Width);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.MatchSelected -= OnMatchSelected;
            _vm.SearchFocusRequested -= OnSearchFocusRequested;
            _vm.SearchResultsChanged -= OnSearchResultsChanged;
        }

        _vm = DataContext as MarkdownPageViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.MatchSelected += OnMatchSelected;
            _vm.SearchFocusRequested += OnSearchFocusRequested;
            _vm.SearchResultsChanged += OnSearchResultsChanged;
        }

        ApplyExplorer();
        ApplyLayout();
        RenderPreview();
        UpdateCaret();
        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MarkdownPageViewModel.PreviewSource):
            case nameof(MarkdownPageViewModel.ActiveSearchTerm):
                RenderPreview();
                break;
            case nameof(MarkdownPageViewModel.ViewMode):
                ApplyLayout();
                break;
            case nameof(MarkdownPageViewModel.IsExplorerVisible):
                ApplyExplorer();
                break;
            case nameof(MarkdownPageViewModel.CurrentFilePath):
                PreviewScroll.Offset = default;
                Editor.ScrollToHome();
                Editor.TextArea.Caret.Offset = 0;
                ApplyLayout();
                break;
        }
    }

    // ------------------------------------------------------------------ layout

    private void ApplyLayout()
    {
        var mode = _vm?.ViewMode ?? ViewMode.Preview;
        var hasDocument = _vm?.HasDocument ?? false;
        var columns = DocumentGrid.ColumnDefinitions;
        var showEditor = mode != ViewMode.Preview;
        var showPreview = mode != ViewMode.Edit;

        columns[0].Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        columns[2].Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EditorPanel.IsVisible = showEditor;
        PreviewScroll.IsVisible = showPreview;
        PreviewMarkers.IsVisible = showPreview;
        Splitter.IsVisible = showEditor && showPreview;
        FormatBar.IsVisible = hasDocument && showEditor;

        // Edit mode: keep lines at a readable length on wide screens.
        EditorPanel.MaxWidth = mode == ViewMode.Edit ? 1000 : double.PositiveInfinity;
        EditorPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

        ApplyPreviewMargins();
        UpdateCaret();

        if (showEditor && hasDocument)
            Dispatcher.UIThread.Post(() => Editor.TextArea.Focus(), DispatcherPriority.Background);
    }

    private void ApplyExplorer()
    {
        var visible = _vm?.IsExplorerVisible ?? true;
        var column = PageGrid.ColumnDefinitions[0];
        if (!visible && column.Width.Value > 0)
            _explorerWidth = Math.Clamp(column.Width.Value, 180, 480); // remember a user resize

        column.Width = new GridLength(visible ? _explorerWidth : 0);
        column.MinWidth = visible ? 180 : 0;
        column.MaxWidth = visible ? 480 : 0;
        ExplorerPane.IsVisible = visible;
        ExplorerSplitter.IsVisible = visible;
    }

    private void ApplyResponsive(double width)
    {
        if (_vm is null)
            return;

        // Hide/show the explorer only when crossing the breakpoint so manual toggles stick.
        var narrow = width < ExplorerBreakpoint;
        if (_wasNarrow != narrow)
        {
            _wasNarrow = narrow;
            _vm.IsExplorerVisible = !narrow;
        }

        ApplyPreviewMargins();
    }

    private void ApplyToolbarDensity(double width)
    {
        PathText.IsVisible = width >= 820;
        SaveLabel.IsVisible = width >= 560;
        RevealButton.IsVisible = width >= 480 && !DeveloperTools.Services.AppServices.IsBrowser;
        ActionsSeparator.IsVisible = width >= 480;
        ApplyPreviewMargins();
    }

    private void ApplyPreviewMargins()
    {
        // Tighter margins when the preview is narrow (split mode or small window).
        var width = PreviewScroll.Bounds.Width > 0 ? PreviewScroll.Bounds.Width : DocumentArea.Bounds.Width;
        var horizontal = width switch
        {
            < 520 => 18,
            < 800 => 28,
            _ => 48
        };
        PreviewHost.Margin = new Thickness(horizontal, horizontal < 48 ? 22 : 36, horizontal, 64);
    }

    // ------------------------------------------------------------------ editor

    private void ApplyEditorTheme()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        Editor.SyntaxHighlighting = MarkdownHighlighting.Get(dark);

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
        _codeColorizer.SetPalette(palette);
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void OnFormatClick(object? sender, RoutedEventArgs e)
    {
        switch ((sender as Control)?.Tag as string)
        {
            case "h1": _editing.Heading(1); break;
            case "h2": _editing.Heading(2); break;
            case "h3": _editing.Heading(3); break;
            case "bold": _editing.Bold(); break;
            case "italic": _editing.Italic(); break;
            case "strike": _editing.Strikethrough(); break;
            case "code": _editing.InlineCode(); break;
            case "link": _editing.Link(); break;
            case "bullet": _editing.BulletList(); break;
            case "number": _editing.NumberedList(); break;
            case "task": _editing.TaskList(); break;
            case "quote": _editing.Quote(); break;
            case "codeblock": _editing.CodeBlock(); break;
            case "table": _editing.Table(); break;
            case "rule": _editing.HorizontalRule(); break;
        }
    }

    private void UpdateCaret()
    {
        var caret = Editor.TextArea.Caret;
        CaretText.Text = _vm?.ViewMode == ViewMode.Preview ? "" : $"Ln {caret.Line}, Col {caret.Column}";
    }

    /// <summary>In split mode, keep the preview at the same relative position as the editor.</summary>
    private void SyncPreviewScroll()
    {
        if (_vm?.ViewMode != ViewMode.Split)
            return;

        var textView = Editor.TextArea.TextView;
        var editorRange = textView.DocumentHeight - textView.Bounds.Height;
        var previewRange = PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height;
        if (editorRange <= 0 || previewRange <= 0)
            return;

        var ratio = Math.Clamp(textView.ScrollOffset.Y / editorRange, 0, 1);
        PreviewScroll.Offset = new Vector(0, ratio * previewRange);
    }

    // ------------------------------------------------------------------ search

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        // Pre-fill with the editor selection (single line), like most editors.
        var selected = EditorPanel.IsVisible ? Editor.SelectedText : "";
        if (selected.Length is > 0 and < 200 && !selected.Contains('\n') && _vm is not null)
            _vm.SearchText = selected;

        Dispatcher.UIThread.Post(() =>
        {
            FindBox.Focus();
            FindBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs args)
    {
        if (_vm is null)
            return;

        var pressed = args.Key;
        if (pressed == Key.Enter && args.KeyModifiers.HasFlag(KeyModifiers.Shift))
            _vm.FindPreviousCommand.Execute(null);
        else if (pressed == Key.Enter)
            _vm.FindNextCommand.Execute(null);
        else if (pressed == Key.Escape)
        {
            _vm.CloseSearchCommand.Execute(null);
            if (EditorPanel.IsVisible)
                Editor.TextArea.Focus();
        }
        else
            return;

        args.Handled = true;
    }

    private void OnMatchSelected(object? sender, SearchMatch match)
    {
        var document = Editor.Document;
        if (document is null || match.Offset + match.Length > document.TextLength)
            return;

        // Editor: select the match and scroll to it (focus stays in the find box).
        if (EditorPanel.IsVisible)
        {
            Editor.Select(match.Offset, match.Length);
            var location = document.GetLocation(match.Offset);
            Editor.ScrollTo(location.Line, location.Column);
        }

        // Preview only: bring the block that contains the match into view.
        if (_vm?.ViewMode == ViewMode.Preview && _blocks.Count > 0)
        {
            var index = 0;
            for (var i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].Block.Span.Start > match.Offset)
                    break;
                index = i;
            }

            ScrollToBlock(index);
        }
    }

    // ------------------------------------------------------------------ preview

    /// <summary>New blocks rendered synchronously before the rest is deferred to idle time.</summary>
    private const int SyncRenderBudget = 30;

    private const int BatchSize = 25;

    // Controls currently in the preview, keyed by their block's markdown, so unchanged blocks are reused.
    private Dictionary<string, List<Control>> _blockCache = new();
    private string? _cacheContext;
    private IReadOnlyList<PreviewBlock> _blocks = Array.Empty<PreviewBlock>();
    private int _renderGeneration;

    private void RenderPreview()
    {
        // Keep the reader's place when the same document re-renders (typing, ticking a task).
        var savedOffset = PreviewScroll.Offset;
        var generation = ++_renderGeneration;
        _selection.Reset(); // text positions change with the content

        if (_vm is null || !_vm.HasDocument || _vm.ViewMode == ViewMode.Edit)
        {
            PreviewHost.Children.Clear();
            _blockCache.Clear();
            _blocks = Array.Empty<PreviewBlock>();
            return;
        }

        // Anything that changes how *every* block looks invalidates the cache.
        var context = $"{_vm.CurrentFileDirectory}|{ActualThemeVariant}|{_vm.ActiveSearchTerm}";
        if (context != _cacheContext)
        {
            _blockCache.Clear();
            _cacheContext = context;
        }

        _renderer = new MarkdownRenderer(this, _vm.CurrentFileDirectory, OnLinkClicked, _vm.ToggleTask)
        {
            SearchTerm = _vm.ActiveSearchTerm
        };

        IReadOnlyList<PreviewBlock> blocks;
        try
        {
            blocks = _renderer.Parse(_vm.PreviewSource);
        }
        catch (Exception ex)
        {
            PreviewHost.Children.Clear();
            _blockCache.Clear();
            PreviewHost.Children.Add(new TextBlock { Text = "Preview failed: " + ex.Message, TextWrapping = TextWrapping.Wrap });
            return;
        }

        _blocks = blocks;
        var pool = _blockCache;              // controls from the previous render, free to reuse
        _blockCache = new Dictionary<string, List<Control>>();

        if (TryPatchInPlace(blocks, pool))
        {
            RestoreScroll(savedOffset);
            return;
        }

        PreviewHost.Children.Clear();

        var rendered = 0;
        var index = 0;
        for (; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var control = TakeCached(pool, block.Key);
            if (control is null)
            {
                if (rendered >= SyncRenderBudget)
                    break; // the rest is rendered in idle-time batches below
                control = _renderer.RenderTopLevel(block);
                rendered++;
            }

            AddBlock(block, control);
        }

        if (index < blocks.Count)
            RenderRemaining(generation, blocks, index, pool);

        RestoreScroll(savedOffset);
    }

    private void RestoreScroll(Vector savedOffset) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm?.ViewMode == ViewMode.Split)
                SyncPreviewScroll();
            else
                PreviewScroll.Offset = savedOffset;
            UpdateMarkers();
        }, DispatcherPriority.Loaded);

    // ------------------------------------------------------------------ scrollbar search markers

    private void OnSearchResultsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateMarkers, DispatcherPriority.Loaded);

    /// <summary>Places a tick on each scrollbar for every match (VS-style), current match highlighted.</summary>
    private void UpdateMarkers()
    {
        var matches = _vm?.Matches;
        if (_vm is null || !_vm.IsSearchOpen || matches is null || matches.Count == 0)
        {
            EditorMarkers.Clear();
            PreviewMarkers.Clear();
            return;
        }

        var current = _vm.CurrentMatchIndex;

        if (EditorPanel.IsVisible && Editor.Document is { } document)
        {
            var textView = Editor.TextArea.TextView;
            var height = Math.Max(1, textView.DocumentHeight);
            var positions = new List<double>(matches.Count);
            double currentPosition = -1;
            for (var i = 0; i < matches.Count; i++)
            {
                if (matches[i] > document.TextLength)
                    continue;
                var line = document.GetLineByOffset(matches[i]).LineNumber;
                var y = textView.GetVisualTopByDocumentLine(line) / height;
                positions.Add(y);
                if (i == current)
                    currentPosition = y;
            }

            EditorMarkers.SetMarkers(positions, currentPosition);
        }
        else
        {
            EditorMarkers.Clear();
        }

        if (PreviewScroll.IsVisible && _blocks.Count > 0)
        {
            var extent = Math.Max(1, PreviewScroll.Extent.Height);
            var textLength = Math.Max(1, _vm.PreviewSource.Length);
            var positions = new List<double>(matches.Count);
            double currentPosition = -1;
            var blockIndex = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                var offset = matches[i];
                while (blockIndex + 1 < _blocks.Count && _blocks[blockIndex + 1].Block.Span.Start <= offset)
                    blockIndex++;

                double y;
                if (blockIndex < PreviewHost.Children.Count)
                {
                    // Block position, plus how far into the block the match is.
                    var block = _blocks[blockIndex].Block;
                    var control = PreviewHost.Children[blockIndex];
                    var within = Math.Clamp((offset - block.Span.Start) / (double)Math.Max(1, block.Span.Length), 0, 1);
                    y = (PreviewHost.Margin.Top + control.Bounds.Y + within * control.Bounds.Height) / extent;
                }
                else
                {
                    y = offset / (double)textLength; // not laid out yet: estimate
                }

                positions.Add(y);
                if (i == current)
                    currentPosition = y;
            }

            PreviewMarkers.SetMarkers(positions, currentPosition);
        }
        else
        {
            PreviewMarkers.Clear();
        }
    }

    /// <summary>
    /// Typical edits (typing, ticking a task) change one or two blocks. If only a few blocks need new
    /// controls, swap just those in the panel: re-adding untouched controls would re-style all of them.
    /// </summary>
    private bool TryPatchInPlace(IReadOnlyList<PreviewBlock> blocks, Dictionary<string, List<Control>> pool)
    {
        var children = PreviewHost.Children;
        if (children.Count == 0 || _renderer is null)
            return false;

        var missing = 0;
        foreach (var block in blocks)
        {
            if (!pool.TryGetValue(block.Key, out var list) || list.Count == 0)
                missing++;
        }

        if (missing > SyncRenderBudget)
            return false; // large change (new file, theme): progressive full render instead

        // Count-aware reuse (identical blocks may appear several times).
        var available = pool.ToDictionary(p => p.Key, p => new List<Control>(p.Value));
        var desired = new List<Control>(blocks.Count);
        foreach (var block in blocks)
        {
            var control = TakeCached(available, block.Key) ?? _renderer.RenderTopLevel(block);
            desired.Add(control);
            if (!_blockCache.TryGetValue(block.Key, out var list))
                _blockCache[block.Key] = list = new List<Control>(1);
            list.Add(control);
        }

        // Keep the common prefix and suffix; replace only the middle.
        var prefix = 0;
        while (prefix < desired.Count && prefix < children.Count && ReferenceEquals(desired[prefix], children[prefix]))
            prefix++;

        int currentEnd = children.Count, desiredEnd = desired.Count;
        while (currentEnd > prefix && desiredEnd > prefix && ReferenceEquals(children[currentEnd - 1], desired[desiredEnd - 1]))
        {
            currentEnd--;
            desiredEnd--;
        }

        children.RemoveRange(prefix, currentEnd - prefix);
        children.InsertRange(prefix, desired.GetRange(prefix, desiredEnd - prefix));        return true;
    }

    /// <summary>Renders the remaining blocks a batch at a time so the window stays responsive.</summary>
    private void RenderRemaining(int generation, IReadOnlyList<PreviewBlock> blocks, int start, Dictionary<string, List<Control>> pool)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _renderGeneration || _renderer is null)
                return; // a newer render replaced this one

            var end = Math.Min(blocks.Count, start + BatchSize);
            for (var i = start; i < end; i++)
                AddBlock(blocks[i], TakeCached(pool, blocks[i].Key) ?? _renderer.RenderTopLevel(blocks[i]));

            if (end < blocks.Count)
                RenderRemaining(generation, blocks, end, pool);
            else
                UpdateMarkers(); // all blocks laid out: marker positions are now exact
        }, DispatcherPriority.Background);
    }

    private void AddBlock(PreviewBlock block, Control control)
    {
        if (!_blockCache.TryGetValue(block.Key, out var list))
            _blockCache[block.Key] = list = new List<Control>(1);
        list.Add(control);
        PreviewHost.Children.Add(control);
    }

    private static Control? TakeCached(Dictionary<string, List<Control>> pool, string key)
    {
        if (!pool.TryGetValue(key, out var list) || list.Count == 0)
            return null;
        // First in, first out: identical blocks (repeated snippets) keep their order,
        // so the in-place patch sees them as unchanged.
        var control = list[0];
        list.RemoveAt(0);
        return control;
    }

    private void ScrollToBlock(int index)
    {
        if (index >= 0 && index < PreviewHost.Children.Count)
            PreviewHost.Children[index].BringIntoView();
    }
    private async void OnLinkClicked(string url)
    {
        if (_vm is null)
            return;

        if (url.StartsWith('#'))
        {
            if (_renderer?.AnchorIndex.TryGetValue(url[1..], out var index) == true)
                ScrollToBlock(index);
            return;
        }

        await _vm.FollowLinkAsync(url);
    }
}
