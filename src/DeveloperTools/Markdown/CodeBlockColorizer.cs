using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace DeveloperTools.Markdown;

/// <summary>
/// Colours the code inside fenced blocks (```kotlin … ```) in the markdown editor,
/// using the same tokenizer and palette as the preview.
/// </summary>
public sealed partial class CodeBlockColorizer : DocumentColorizingTransformer
{
    [GeneratedRegex(@"^[ ]{0,3}(?<fence>`{3,}|~{3,})[ \t]*(?<lang>[^\s`{]*)")]
    private static partial Regex FenceOpenRegex();

    private sealed class Block
    {
        public int FirstLine;   // first content line (after the opening fence)
        public int LastLine;    // last content line (before the closing fence)
        public string? Language;
        public List<CodeToken>? Tokens;
        public int ContentOffset;
    }

    private readonly TextView _textView;
    private TextDocument? _document;
    private List<Block> _blocks = new();
    private bool _stale = true;
    private IReadOnlyDictionary<TokenKind, IBrush> _palette = new Dictionary<TokenKind, IBrush>();

    public CodeBlockColorizer(TextView textView)
    {
        _textView = textView;
    }

    public void SetPalette(IReadOnlyDictionary<TokenKind, IBrush> palette)
    {
        _palette = palette;
        _textView.Redraw();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var document = CurrentContext.Document;
        Attach(document);
        if (_stale)
            Rebuild(document);

        var block = FindBlock(line.LineNumber);
        if (block?.Language is null || !CodeHighlighter.IsSupported(block.Language))
            return;

        if (block.Tokens is null)
        {
            var endLine = document.GetLineByNumber(block.LastLine);
            var text = document.GetText(block.ContentOffset, endLine.EndOffset - block.ContentOffset);
            block.Tokens = CodeHighlighter.Tokenize(text, block.Language);
        }

        var lineStart = line.Offset - block.ContentOffset;
        var lineEnd = lineStart + line.Length;
        foreach (var token in block.Tokens)
        {
            var start = Math.Max(token.Start, lineStart);
            var end = Math.Min(token.Start + token.Length, lineEnd);
            if (end <= start)
            {
                if (token.Start >= lineEnd)
                    break; // tokens are in order
                continue;
            }

            if (!_palette.TryGetValue(token.Kind, out var brush))
                continue;
            var italic = token.Kind == TokenKind.Comment;
            ChangeLinePart(block.ContentOffset + start, block.ContentOffset + end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (italic)
                {
                    var face = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, FontStyle.Italic, face.Weight, face.Stretch));
                }
            });
        }
    }

    private void Attach(TextDocument document)
    {
        if (ReferenceEquals(document, _document))
            return;
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
        _document = document;
        _document.Changed += OnDocumentChanged;
        _stale = true;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        _stale = true;
        // Fences change how every following line is coloured, so redraw everything (cheap for typical docs).
        _textView.Redraw();
    }

    private void Rebuild(TextDocument document)
    {
        var blocks = new List<Block>();
        Block? open = null;
        string? openFence = null;

        for (var n = 1; n <= document.LineCount; n++)
        {
            var line = document.GetLineByNumber(n);
            var text = document.GetText(line);

            if (open is null)
            {
                var m = FenceOpenRegex().Match(text);
                if (!m.Success)
                    continue;
                openFence = m.Groups["fence"].Value;
                var lang = m.Groups["lang"].Value;
                open = new Block { FirstLine = n + 1, Language = lang.Length > 0 ? lang : null };
            }
            else
            {
                var trimmed = text.Trim();
                if (trimmed.Length >= openFence!.Length && trimmed[0] == openFence[0] && trimmed.Trim(openFence[0]).Length == 0)
                {
                    open.LastLine = n - 1;
                    Finish(document, open, blocks);
                    open = null;
                }
            }
        }

        if (open is not null)
        {
            open.LastLine = document.LineCount; // unterminated fence runs to the end
            Finish(document, open, blocks);
        }

        _blocks = blocks;
        _stale = false;
    }

    private static void Finish(TextDocument document, Block block, List<Block> blocks)
    {
        if (block.LastLine < block.FirstLine || block.FirstLine > document.LineCount)
            return;
        block.ContentOffset = document.GetLineByNumber(block.FirstLine).Offset;
        blocks.Add(block);
    }

    private Block? FindBlock(int lineNumber)
    {
        // Binary search: blocks are sorted and don't overlap.
        int lo = 0, hi = _blocks.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var b = _blocks[mid];
            if (lineNumber < b.FirstLine) hi = mid - 1;
            else if (lineNumber > b.LastLine) lo = mid + 1;
            else return b;
        }

        return null;
    }
}
