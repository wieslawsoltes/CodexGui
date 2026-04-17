using CodexGui.Markdown.Plugin.TextMate;
using Xunit;

namespace CodexGui.Markdown.Plugin.TextMate.Tests;

public sealed class TextMateCodeHighlighterTests
{
    [Fact]
    public void Resolve_scope_name_maps_common_language_hints()
    {
        var highlighter = new TextMateCodeHighlighter();

        Assert.Equal(".cs", highlighter.ResolveExtension("csharp"));
        Assert.Equal(".json", highlighter.ResolveExtension("json"));
        Assert.NotNull(highlighter.ResolveScopeName("csharp"));
        Assert.NotNull(highlighter.ResolveScopeName("json"));
    }

    [Fact]
    public void Highlight_uses_textmate_when_grammar_is_available()
    {
        var highlighter = new TextMateCodeHighlighter();

        var result = highlighter.Highlight("public class Demo { }", "csharp");

        Assert.Equal("textmate", result.Engine);
        Assert.Contains(result.Runs, static run => !string.IsNullOrWhiteSpace(run.Style.Foreground));
    }

    [Fact]
    public void Highlight_falls_back_to_plain_when_language_is_unknown()
    {
        var highlighter = new TextMateCodeHighlighter();

        var result = highlighter.Highlight("plain text", "unknown-language");

        Assert.Equal("plain", result.Engine);
        Assert.Single(result.Runs);
        Assert.Equal("plain text", result.Runs[0].Text);
    }
}
