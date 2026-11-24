using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;
using UiInline = Avalonia.Controls.Documents.Inline;

namespace DeveloperTools.Markdown;

/// <summary>
/// Turns markdown into native Avalonia controls, using the app's theme brushes.
/// </summary>
public sealed class MarkdownRenderer
{
    public static readonly FontFamily MonoFont = new("Cascadia Mono, Cascadia Code, Consolas, Menlo, monospace");

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };

    private const double BodySize = 15;

    // content-copy (Material Design Icons)
    private static readonly Geometry CopyIcon = Geometry.Parse(
        "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z");

    /// <summary>Text to highlight in the preview (Ctrl+F), or null.</summary>
    public string? SearchTerm { get; init; }

    private readonly Control _host;
    private readonly string? _baseDirectory;
    private readonly Action<string> _onLink;
    private readonly Action<int>? _onToggleTask;

    /// <param name="onToggleTask">Called with the source offset of a task's "[ ]" when its checkbox is clicked.</param>
    public MarkdownRenderer(Control host, string? baseDirectory, Action<string> onLink, Action<int>? onToggleTask = null)
    {
        _host = host;
        _baseDirectory = baseDirectory;
        _onLink = onLink;
        _onToggleTask = onToggleTask;
    }

    /// <summary>Heading anchors (e.g. "getting-started") to their controls, for in-page links.</summary>
    public Dictionary<string, Control> Anchors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Heading anchors to the index of their top-level block (works before the block is rendered).</summary>
    public Dictionary<string, int> AnchorIndex { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the document into top-level blocks without creating any controls.
    /// The preview list virtualizes them and calls <see cref="RenderTopLevel"/> only for blocks on screen,
    /// so large documents open instantly.
    /// </summary>
    public IReadOnlyList<PreviewBlock> Parse(string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var blocks = new List<PreviewBlock>();
        foreach (var block in document)
        {
            if (block is LinkReferenceDefinitionGroup)
                continue;
            if (block is HtmlBlock html && html.Lines.ToString().TrimStart().StartsWith("<!--", StringComparison.Ordinal))
                continue;
            if (block is HeadingBlock heading && heading.GetAttributes().Id is { Length: > 0 } id)
                AnchorIndex.TryAdd(id, blocks.Count);

            // The block's own markdown identifies it across re-renders (used to reuse unchanged controls).
            var start = Math.Clamp(block.Span.Start, 0, markdown.Length);
            var end = Math.Clamp(block.Span.End + 1, start, markdown.Length);
            blocks.Add(new PreviewBlock(block, blocks.Count, markdown[start..end]));
        }

        return blocks;
    }

    public Control RenderTopLevel(PreviewBlock item)
    {
        var control = RenderBlock(item.Block, tight: false, Brush("Fg.Primary")) ?? new Panel();
        var top = item.Block is HeadingBlock h && item.Index > 0 ? (h.Level <= 2 ? 28 : 20) : 0;
        control.Margin = new Thickness(0, top, 0, 14);
        return control;
    }

    // ------------------------------------------------------------------ blocks

    private List<Control> RenderBlocks(ContainerBlock container, bool tight, IBrush fg)
    {
        var result = new List<Control>();
        foreach (var block in container)
        {
            var control = RenderBlock(block, tight, fg);
            if (control is null)
                continue;

            var top = 0.0;
            if (block is HeadingBlock h && result.Count > 0)
                top = h.Level <= 2 ? 28 : 20;
            control.Margin = new Thickness(0, top, 0, tight ? 4 : 14);
            result.Add(control);
        }

        if (result.Count > 0)
        {
            var last = result[^1];
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        }

        return result;
    }

    private Control? RenderBlock(Block block, bool tight, IBrush fg) => block switch
    {
        HeadingBlock h => Heading(h),
        ParagraphBlock p => Paragraph(p.Inline, fg),
        QuoteBlock q => Quote(q),
        ListBlock l => List(l, fg),
        FencedCodeBlock f => CodeBlock(f.Lines.ToString(), f.Info),
        CodeBlock c => CodeBlock(c.Lines.ToString(), null),
        ThematicBreakBlock => Rule(),
        Table t => TableBlock(t),
        HtmlBlock html => Html(html),
        LinkReferenceDefinitionGroup => null,
        ContainerBlock c => Stack(RenderBlocks(c, tight, fg)),
        LeafBlock leaf when leaf.Inline is not null => Paragraph(leaf.Inline, fg),
        LeafBlock leaf => Paragraph(leaf.Lines.ToString(), fg),
        _ => null
    };

    private Control Heading(HeadingBlock heading)
    {
        var (size, weight) = heading.Level switch
        {
            1 => (28.0, FontWeight.SemiBold),
            2 => (22.0, FontWeight.SemiBold),
            3 => (18.0, FontWeight.SemiBold),
            4 => (16.0, FontWeight.SemiBold),
            5 => (15.0, FontWeight.SemiBold),
            _ => (14.0, FontWeight.SemiBold)
        };

        var text = TextBlock(heading.Level == 6 ? Brush("Fg.Secondary") : Brush("Fg.Primary"), size);
        text.FontWeight = weight;
        text.LineHeight = size * 1.3;
        FillInlines(text, heading.Inline);

        Control result = text;
        if (heading.Level <= 2)
        {
            result = new Border
            {
                BorderBrush = Brush("Line"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Child = text
            };
        }

        var id = heading.GetAttributes().Id;
        if (!string.IsNullOrEmpty(id))
            Anchors.TryAdd(id, result);
        return result;
    }

    private SelectableTextBlock Paragraph(ContainerInline? inline, IBrush fg)
    {
        var text = TextBlock(fg, BodySize);
        FillInlines(text, inline);
        return text;
    }

    private SelectableTextBlock Paragraph(string content, IBrush fg)
    {
        var text = TextBlock(fg, BodySize);
        text.Text = content;
        return text;
    }

    private Control Quote(QuoteBlock quote) => new Border
    {
        BorderBrush = Brush("Line.Strong"),
        BorderThickness = new Thickness(3, 0, 0, 0),
        Background = Brush("Bg.Subtle"),
        CornerRadius = new CornerRadius(0, 4, 4, 0),
        Padding = new Thickness(16, 10, 12, 10),
        Child = Stack(RenderBlocks(quote, tight: false, Brush("Fg.Secondary")))
    };

    private Control List(ListBlock list, IBrush fg)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        var markerWidth = list.IsOrdered ? 28 : 18;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var task = (item.FirstOrDefault() as ParagraphBlock)?.Inline?.FirstChild as TaskList;

            Control marker;
            if (task is not null)
            {
                marker = Checkbox(task);
            }
            else
            {
                marker = new TextBlock
                {
                    Text = list.IsOrdered ? $"{number}." : Bullet(list),
                    Foreground = Brush("Fg.Muted"),
                    FontSize = BodySize,
                    LineHeight = BodySize * 1.65,
                    HorizontalAlignment = list.IsOrdered ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    Margin = list.IsOrdered ? new Thickness(0, 0, 8, 0) : new Thickness(4, 0, 0, 0)
                };
            }

            number++;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{(task is not null ? 26 : markerWidth)},*") };
            // Completed tasks are dimmed so open ones stand out.
            var content = Stack(RenderBlocks(item, tight: true, task?.Checked == true ? Brush("Fg.Muted") : fg));
            Grid.SetColumn(content, 1);
            grid.Children.Add(marker);
            grid.Children.Add(content);
            panel.Children.Add(grid);
        }

        return panel;
    }

    private Control Checkbox(TaskList task)
    {
        var isChecked = task.Checked;
        var box = new Border
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1.5),
            BorderBrush = isChecked ? Brush("Accent") : Brush("Line.Strong"),
            Background = isChecked ? Brush("Accent") : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Centre the box on the first text line (line height = BodySize * 1.65).
            Margin = new Thickness(0, (BodySize * 1.65 - 15) / 2, 0, 0)
        };
        if (isChecked)
        {
            box.Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M2,6.5 L5,9.5 L10.5,3"),
                Stroke = Brush("Accent.Fg"),
                StrokeThickness = 1.8,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (_onToggleTask is null || task.Span.IsEmpty)
            return box;

        // Clicking the box flips "[ ]" <-> "[x]" in the markdown source.
        box.Cursor = new Cursor(StandardCursorType.Hand);
        box.Classes.Add("task-box"); // the preview selection layer lets clicks on this through
        ToolTip.SetTip(box, isChecked ? "Mark as not done" : "Mark as done");
        box.PointerEntered += (_, _) => box.BorderBrush = Brush(isChecked ? "Accent.Hover" : "Fg.Muted");
        box.PointerExited += (_, _) => box.BorderBrush = isChecked ? Brush("Accent") : Brush("Line.Strong");
        box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            _onToggleTask(task.Span.Start);
        };
        return box;
    }

    private static string Bullet(ListBlock list)
    {
        var depth = 0;
        for (var parent = list.Parent; parent is not null; parent = parent.Parent)
            if (parent is ListBlock)
                depth++;
        return (depth % 3) switch { 0 => "•", 1 => "◦", _ => "▪" };
    }

    private Control CodeBlock(string code, string? language)
    {
        code = code.TrimEnd('\n', '\r');

        var text = new SelectableTextBlock
        {
            Inlines = HighlightedCode(code, language),
            FontFamily = MonoFont,
            FontSize = 13,
            LineHeight = 20,
            Foreground = Brush("Fg.Primary"),
            TextWrapping = TextWrapping.NoWrap,
            // Small vertical padding: a press in the padding doesn't start a selection.
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 4, 0, 4),
            SelectionBrush = Brush("Selection")
        };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = text
        };
        AllowTextSelection(scroller);

        var header = new DockPanel
        {
            Height = 30,
            LastChildFill = false,
            Background = Brush("Bg.Subtle")
        };
        var lang = new TextBlock
        {
            Text = CodeHighlighter.DisplayName(language),
            FontSize = 11.5,
            Foreground = Brush("Fg.Muted"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        var copyLabel = new TextBlock { Text = "Copy", VerticalAlignment = VerticalAlignment.Center };
        var copy = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new PathIcon { Data = CopyIcon, Width = 12, Height = 12 },
                    copyLabel
                }
            },
            Classes = { "btn", "ghost", "xs" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        ToolTip.SetTip(copy, "Copy code to clipboard");
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(copy)?.Clipboard;
            if (clipboard is null)
                return;
            await clipboard.SetTextAsync(code);
            copyLabel.Text = "Copied";
            await System.Threading.Tasks.Task.Delay(1200);
            copyLabel.Text = "Copy";
        };
        DockPanel.SetDock(lang, Dock.Left);
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(lang);
        header.Children.Add(copy);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(new Border { Height = 1, Background = Brush("Code.Border"), [DockPanel.DockProperty] = Dock.Top });
        layout.Children.Add(scroller);

        return new Border
        {
            Background = Brush("Code.Bg"),
            BorderBrush = Brush("Code.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = layout
        };
    }

    /// <summary>
    /// A horizontally scrolling ScrollViewer pans on mouse drag, which steals the drag from text selection.
    /// Turn that pan off: the scrollbar and Shift+wheel still scroll, and dragging selects text.
    /// </summary>
    private static void AllowTextSelection(ScrollViewer scroller)
    {
        scroller.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Avalonia.Controls.Presenters.ScrollContentPresenter>("PART_ContentPresenter") is not { } presenter)
                return;
            foreach (var recognizer in presenter.GestureRecognizers.OfType<Avalonia.Input.GestureRecognizers.ScrollGestureRecognizer>())
            {
                recognizer.CanHorizontallyScroll = false;
                recognizer.CanVerticallyScroll = false;
            }
        };
    }

    /// <summary>Code split into coloured runs (plain text in between keeps the default colour).</summary>
    private InlineCollection HighlightedCode(string code, string? language)
    {
        var inlines = new InlineCollection();
        var position = 0;
        foreach (var token in CodeHighlighter.Tokenize(code, language))
        {
            if (token.Start < position)
                continue; // defensive: overlapping tokens
            if (token.Start > position)
                inlines.AddRange(SplitForSearch(new Run(code[position..token.Start])));

            var run = new Run(code.Substring(token.Start, token.Length)) { Foreground = Brush("Syntax." + token.Kind) };
            if (token.Kind == TokenKind.Comment)
                run.FontStyle = FontStyle.Italic;
            inlines.AddRange(SplitForSearch(run));
            position = token.Start + token.Length;
        }

        if (position < code.Length)
            inlines.AddRange(SplitForSearch(new Run(code[position..])));
        return inlines;
    }

    private Control Rule() => new Border
    {
        Height = 1,
        Background = Brush("Line"),
        Margin = new Thickness(0, 8)
    };

    private Control? Html(HtmlBlock html)
    {
        var raw = html.Lines.ToString().Trim();
        if (raw.StartsWith("<!--", StringComparison.Ordinal))
            return null; // comments are invisible in rendered markdown
        return Paragraph(raw, Brush("Fg.Muted"));
    }

    private Control TableBlock(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var grid = new Grid();
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = rows[r];
            var isHeader = row.IsHeader;

            for (var c = 0; c < columnCount; c++)
            {
                var cell = c < row.Count ? row[c] as TableCell : null;
                var alignment = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;

                var text = TextBlock(Brush("Fg.Primary"), 14);
                text.MaxWidth = 420;
                text.TextAlignment = alignment switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                };
                if (isHeader)
                    text.FontWeight = FontWeight.SemiBold;
                if (cell?.OfType<LeafBlock>().FirstOrDefault() is { } paragraph)
                    FillInlines(text, paragraph.Inline);

                var border = new Border
                {
                    BorderBrush = Brush("Line"),
                    BorderThickness = new Thickness(c > 0 ? 1 : 0, r > 0 ? 1 : 0, 0, 0),
                    Background = isHeader ? Brush("Bg.Subtle") : r % 2 == 0 ? Brush("Bg.Stripe") : Brushes.Transparent,
                    Padding = new Thickness(12, 7),
                    Child = text
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }

        var frame = new Border
        {
            BorderBrush = Brush("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = grid
        };

        var tableScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = frame
        };
        AllowTextSelection(tableScroller);
        return tableScroller;
    }

    // ------------------------------------------------------------------ inlines

    /// <summary>Character ranges of links inside one text block, used for hit testing clicks.</summary>
    private sealed class InlineState
    {
        public int Offset;
        public readonly List<LinkRange> Links = new();
    }

    private sealed record LinkRange(int Start, int End, string Url, Span Span);

    private void FillInlines(SelectableTextBlock text, ContainerInline? container)
    {
        var state = new InlineState();
        AddInlines(text.Inlines!, container, state);
        AttachLinks(text, state);
    }

    private void AddInlines(InlineCollection target, ContainerInline? container, InlineState state)
    {
        if (container is null)
            return;
        foreach (var inline in container)
            AddInline(target, inline, state);
    }

    private void AddRun(InlineCollection target, Run run, InlineState state)
    {
        state.Offset += run.Text?.Length ?? 0;
        foreach (var part in SplitForSearch(run))
            target.Add(part);
    }

    /// <summary>Splits a run so occurrences of the search term get a highlight background.</summary>
    private IEnumerable<Run> SplitForSearch(Run run)
    {
        var text = run.Text ?? "";
        if (string.IsNullOrEmpty(SearchTerm) || text.IndexOf(SearchTerm, StringComparison.OrdinalIgnoreCase) < 0)
        {
            yield return run;
            yield break;
        }

        var position = 0;
        while (position < text.Length)
        {
            var hit = text.IndexOf(SearchTerm, position, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                yield return CloneRun(run, text[position..], highlight: false);
                yield break;
            }

            if (hit > position)
                yield return CloneRun(run, text[position..hit], highlight: false);
            yield return CloneRun(run, text.Substring(hit, SearchTerm.Length), highlight: true);
            position = hit + SearchTerm.Length;
        }
    }

    private Run CloneRun(Run source, string text, bool highlight)
    {
        var run = new Run(text);
        foreach (var property in new AvaloniaProperty[]
                 {
                     TextElement.ForegroundProperty, TextElement.BackgroundProperty, TextElement.FontFamilyProperty,
                     TextElement.FontSizeProperty, TextElement.FontStyleProperty, TextElement.FontWeightProperty
                 })
        {
            if (source.IsSet(property))
                run.SetValue(property, source.GetValue(property));
        }

        if (highlight)
        {
            run.Background = Brush("Search.Bg");
            run.Foreground = Brush("Search.Fg");
        }

        return run;
    }

    private void AddInline(InlineCollection target, MdInline inline, InlineState state)
    {
        switch (inline)
        {
            case LiteralInline literal:
                AddRun(target, new Run(literal.Content.ToString()), state);
                break;

            case CodeInline code:
                AddRun(target, new Run(code.Content)
                {
                    FontFamily = MonoFont,
                    FontSize = BodySize * 0.88,
                    Background = Brush("Code.Inline"),
                    Foreground = Brush("Code.InlineFg")
                }, state);
                break;

            case LineBreakInline { IsHard: true }:
                target.Add(new LineBreak());
                state.Offset += 1;
                break;

            case LineBreakInline:
                AddRun(target, new Run(" "), state);
                break;

            case EmphasisInline emphasis:
                var span = new Span();
                switch (emphasis.DelimiterChar)
                {
                    case '~' when emphasis.DelimiterCount >= 2:
                        span.TextDecorations = TextDecorations.Strikethrough;
                        break;
                    case '=':
                        span.Background = Brush("Mark.Bg");
                        break;
                    case '+':
                        span.TextDecorations = TextDecorations.Underline;
                        break;
                    case '*' or '_' when emphasis.DelimiterCount >= 2:
                        span.FontWeight = FontWeight.SemiBold;
                        break;
                    case '*' or '_':
                        span.FontStyle = FontStyle.Italic;
                        break;
                }

                AddInlines(span.Inlines, emphasis, state);
                target.Add(span);
                break;

            case LinkInline { IsImage: true } image:
                AddImage(target, image, state);
                break;

            case LinkInline link:
                AddLink(target, link.Url, state, (inlines, s) => AddInlines(inlines, link, s));
                break;

            case AutolinkInline auto:
                AddLink(target, auto.IsEmail ? "mailto:" + auto.Url : auto.Url, state,
                    (inlines, s) => AddRun(inlines, new Run(auto.Url), s));
                break;

            case TaskList:
                break; // drawn as a checkbox in the list marker column

            case HtmlEntityInline entity:
                AddRun(target, new Run(entity.Transcoded.ToString()), state);
                break;

            case HtmlInline html:
                if (html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
                {
                    target.Add(new LineBreak());
                    state.Offset += 1;
                }

                break; // other inline tags are dropped; their text content still renders

            case ContainerInline container:
                AddInlines(target, container, state);
                break;

            default:
                var text = inline.ToString();
                if (!string.IsNullOrEmpty(text))
                    AddRun(target, new Run(text), state);
                break;
        }
    }

    private void AddLink(InlineCollection target, string? url, InlineState state,
        Action<InlineCollection, InlineState> fill)
    {
        var span = new Span { Foreground = Brush("Link") };
        var start = state.Offset;
        fill(span.Inlines, state);
        target.Add(span);
        if (!string.IsNullOrEmpty(url))
            state.Links.Add(new LinkRange(start, state.Offset, url, span));
    }

    // Link ranges per text block, so the preview's selection layer can open a link on a plain click.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SelectableTextBlock, InlineState> LinkStates = new();

    /// <summary>The link URL under a point (in the text block's coordinates), if any.</summary>
    public static string? LinkAt(SelectableTextBlock text, Point point)
    {
        if (!LinkStates.TryGetValue(text, out var state))
            return null;
        var hit = text.TextLayout.HitTestPoint(point - new Point(text.Padding.Left, text.Padding.Top));
        if (!hit.IsInside)
            return null;
        var position = hit.TextPosition;
        return state.Links.FirstOrDefault(l => position >= l.Start && position < l.End)?.Url;
    }

    /// <summary>Hover feedback for links. Clicks are handled by the preview's selection layer (see <see cref="LinkAt"/>).</summary>
    private void AttachLinks(SelectableTextBlock text, InlineState state)
    {
        if (state.Links.Count == 0)
            return;

        LinkStates.AddOrUpdate(text, state);
        LinkRange? hovered = null;

        LinkRange? HitTest(PointerEventArgs e)
        {
            var point = e.GetPosition(text) - new Point(text.Padding.Left, text.Padding.Top);
            var hit = text.TextLayout.HitTestPoint(point);
            if (!hit.IsInside)
                return null;
            var position = hit.TextPosition;
            return state.Links.FirstOrDefault(l => position >= l.Start && position < l.End);
        }

        void SetHovered(LinkRange? link)
        {
            if (ReferenceEquals(link, hovered))
                return;
            if (hovered is not null)
                hovered.Span.TextDecorations = null;
            hovered = link;
            if (link is not null)
            {
                link.Span.TextDecorations = TextDecorations.Underline;
                text.Cursor = new Cursor(StandardCursorType.Hand);
            }
            else
            {
                text.ClearValue(InputElement.CursorProperty);
            }
        }

        text.PointerMoved += (_, e) => SetHovered(HitTest(e));
        text.PointerExited += (_, _) => SetHovered(null);
    }

    private void AddImage(InlineCollection target, LinkInline image, InlineState state)
    {
        var alt = string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
        var path = ResolveLocal(image.Url);

        if (path is not null && File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path)))
        {
            try
            {
                var bitmap = new Bitmap(path);
                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    MaxWidth = Math.Min(bitmap.Size.Width, 780),
                    Margin = new Thickness(0, 4)
                };
                if (alt.Length > 0)
                    ToolTip.SetTip(img, alt);
                target.Add(new InlineUIContainer { Child = img });
                state.Offset += 1;
                return;
            }
            catch (Exception)
            {
                // fall through to the placeholder
            }
        }

        var label = alt.Length > 0 ? alt : Path.GetFileName(image.Url ?? "image");
        if (image.Url is { } url && url.Contains("://"))
            AddLink(target, url, state, (inlines, s) => AddRun(inlines, new Run($"[image: {label}]"), s));
        else
            AddRun(target, new Run($"[image: {label}]") { Foreground = Brush("Fg.Muted") }, state);
    }

    // ------------------------------------------------------------------ helpers

    private string? ResolveLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || _baseDirectory is null)
            return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
            return abs.IsFile ? abs.LocalPath : null;

        var clean = Uri.UnescapeDataString(url.Split('#', '?')[0]);
        try
        {
            return Path.GetFullPath(Path.Combine(_baseDirectory, clean));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private SelectableTextBlock TextBlock(IBrush fg, double size) => new()
    {
        Foreground = fg,
        FontSize = size,
        LineHeight = size * 1.65,
        TextWrapping = TextWrapping.Wrap,
        SelectionBrush = Brush("Selection"),
        Inlines = new InlineCollection()
    };

    private static StackPanel Stack(IEnumerable<Control> children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private readonly Dictionary<string, IBrush> _brushes = new();

    private IBrush Brush(string key)
    {
        if (_brushes.TryGetValue(key, out var cached))
            return cached;
        var brush = _host.TryFindResource(key, _host.ActualThemeVariant, out var value) && value is IBrush b
            ? b
            : Brushes.Gray;
        _brushes[key] = brush;
        return brush;
    }
}

/// <summary>A top-level markdown block awaiting (lazy) rendering in the preview.</summary>
public sealed record PreviewBlock(Block Block, int Index, string Source)
{
    /// <summary>Cache key: same markdown in the same position class renders the same control.</summary>
    public string Key => (Index == 0 ? "0|" : "n|") + Source;
}
