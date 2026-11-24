using System.IO;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace DeveloperTools.Markdown;

/// <summary>Markdown syntax highlighting whose colours match the app's light or dark palette.</summary>
public static class MarkdownHighlighting
{
    private static IHighlightingDefinition? _light;
    private static IHighlightingDefinition? _dark;

    public static IHighlightingDefinition Get(bool dark) =>
        dark
            ? _dark ??= Load(heading: "#F4F4F5", marker: "#71717A", code: "#C4B5A0", link: "#8AB4F8", quote: "#A1A1AA")
            : _light ??= Load(heading: "#09090B", marker: "#A1A1AA", code: "#9A3412", link: "#2563EB", quote: "#71717A");

    private static IHighlightingDefinition Load(string heading, string marker, string code, string link, string quote)
    {
        var xshd = $$"""
            <SyntaxDefinition name="Markdown" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Heading" foreground="{{heading}}" fontWeight="bold" />
              <Color name="Marker" foreground="{{marker}}" />
              <Color name="Code" foreground="{{code}}" />
              <Color name="Link" foreground="{{link}}" />
              <Color name="Quote" foreground="{{quote}}" fontStyle="italic" />
              <Color name="Strong" fontWeight="bold" />
              <Color name="Emphasis" fontStyle="italic" />
              <RuleSet name="FencedCode" />
              <RuleSet>
                <!-- Fenced blocks: markdown rules don't apply inside; CodeBlockColorizer colours the code. -->
                <Span multiline="true" ruleSet="FencedCode">
                  <Begin color="Marker">^[ \t]*(```|~~~).*$</Begin>
                  <End color="Marker">^[ \t]*(```|~~~)[ \t]*$</End>
                </Span>
                <Span color="Code">
                  <Begin>`</Begin>
                  <End>`</End>
                </Span>
                <Rule color="Heading">^\#{1,6}[ \t].*$</Rule>
                <Rule color="Quote">^[ \t]*&gt;.*$</Rule>
                <Rule color="Marker">^[ \t]*([-*_][ \t]*){3,}$</Rule>
                <Rule color="Marker">^[ \t]*([-*+]|\d+[.)])[ \t]+(\[[ xX]\][ \t]+)?</Rule>
                <Rule color="Marker">^[ \t]*\|.*\|[ \t]*$</Rule>
                <Rule color="Link">!?\[[^\]\n]*\]\([^)\n]*\)</Rule>
                <Rule color="Link">&lt;https?://[^&gt;\s]+&gt;|https?://[^\s)&gt;]+</Rule>
                <Rule color="Strong">\*\*[^*\n]+\*\*|__[^_\n]+__</Rule>
                <Rule color="Emphasis">(?&lt;![*\w])\*[^*\s][^*\n]*\*(?!\*)|(?&lt;![_\w])_[^_\s][^_\n]*_(?![_\w])</Rule>
                <Rule color="Marker">~~[^~\n]+~~</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;

        using var reader = XmlReader.Create(new StringReader(xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
