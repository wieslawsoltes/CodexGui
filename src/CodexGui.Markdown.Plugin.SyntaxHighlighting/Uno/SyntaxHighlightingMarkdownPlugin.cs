using System.Collections.Frozen;
using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.SyntaxHighlighting;

public sealed class SyntaxHighlightingMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddCodeHighlighter(new SimpleCodeHighlighter());
    }
}

internal sealed class SimpleCodeHighlighter : IMarkdownCodeHighlighter
{
    private static readonly FrozenDictionary<string, string[]> KeywordMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = ["using", "namespace", "class", "public", "private", "internal", "sealed", "static", "void", "string", "int", "return", "new", "if", "else", "switch", "case", "async", "await", "record"],
            ["cs"] = ["using", "namespace", "class", "public", "private", "internal", "sealed", "static", "void", "string", "int", "return", "new", "if", "else", "switch", "case", "async", "await", "record"],
            ["json"] = [],
            ["javascript"] = ["function", "const", "let", "var", "return", "if", "else", "await", "async", "class", "import", "export"],
            ["js"] = ["function", "const", "let", "var", "return", "if", "else", "await", "async", "class", "import", "export"],
            ["typescript"] = ["function", "const", "let", "var", "return", "if", "else", "await", "async", "class", "interface", "type", "import", "export"],
            ["ts"] = ["function", "const", "let", "var", "return", "if", "else", "await", "async", "class", "interface", "type", "import", "export"],
            ["python"] = ["def", "class", "return", "if", "elif", "else", "import", "from", "for", "while", "async", "await", "None", "True", "False"],
            ["py"] = ["def", "class", "return", "if", "elif", "else", "import", "from", "for", "while", "async", "await", "None", "True", "False"],
            ["xml"] = [],
            ["xaml"] = [],
            ["axaml"] = []
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public int Order => 100;

    public bool CanHighlight(string? languageHint) => true;

    public MarkdownCodeHighlightResult Highlight(string code, string? languageHint)
    {
        var normalized = NormalizeLanguage(languageHint);
        return normalized switch
        {
            "json" => HighlightJson(code),
            "xml" or "xaml" or "axaml" => HighlightMarkup(code),
            _ => HighlightKeywords(code, normalized)
        };
    }

    private static MarkdownCodeHighlightResult HighlightKeywords(string code, string normalized)
    {
        var keywordSet = KeywordMap.TryGetValue(normalized, out var keywords)
            ? new HashSet<string>(keywords, StringComparer.Ordinal)
            : null;

        var runs = new List<MarkdownStyledTextRun>();
        var index = 0;
        while (index < code.Length)
        {
            if (TryReadComment(code, ref index, out var comment))
            {
                runs.Add(new MarkdownStyledTextRun(comment, new MarkdownTextStyle(Foreground: "#15803D"), "comment"));
                continue;
            }

            if (TryReadString(code, ref index, out var literal))
            {
                runs.Add(new MarkdownStyledTextRun(literal, new MarkdownTextStyle(Foreground: "#B91C1C"), "string"));
                continue;
            }

            if (char.IsDigit(code[index]))
            {
                var start = index;
                index++;
                while (index < code.Length && (char.IsDigit(code[index]) || code[index] is '.' or '_'))
                {
                    index++;
                }

                runs.Add(new MarkdownStyledTextRun(code[start..index], new MarkdownTextStyle(Foreground: "#7C3AED"), "number"));
                continue;
            }

            if (char.IsLetter(code[index]) || code[index] == '_')
            {
                var start = index;
                index++;
                while (index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] == '_'))
                {
                    index++;
                }

                var token = code[start..index];
                var isKeyword = keywordSet?.Contains(token) == true;
                runs.Add(new MarkdownStyledTextRun(
                    token,
                    isKeyword ? new MarkdownTextStyle(Bold: true, Foreground: "#1D4ED8") : new MarkdownTextStyle(),
                    isKeyword ? "keyword" : "identifier"));
                continue;
            }

            runs.Add(new MarkdownStyledTextRun(code[index].ToString(), new MarkdownTextStyle(), "punctuation"));
            index++;
        }

        return new MarkdownCodeHighlightResult("built-in", runs);
    }

    private static MarkdownCodeHighlightResult HighlightJson(string code)
    {
        var runs = new List<MarkdownStyledTextRun>();
        var index = 0;
        while (index < code.Length)
        {
            if (TryReadString(code, ref index, out var literal))
            {
                var scope = SkipWhitespace(code, index) < code.Length && code[SkipWhitespace(code, index)] == ':'
                    ? "property"
                    : "string";
                runs.Add(new MarkdownStyledTextRun(
                    literal,
                    scope == "property"
                        ? new MarkdownTextStyle(Bold: true, Foreground: "#0369A1")
                        : new MarkdownTextStyle(Foreground: "#B91C1C"),
                    scope));
                continue;
            }

            if (char.IsDigit(code[index]) || code[index] == '-')
            {
                var start = index++;
                while (index < code.Length && (char.IsDigit(code[index]) || code[index] is '.' or '-' or '+' or 'e' or 'E'))
                {
                    index++;
                }

                runs.Add(new MarkdownStyledTextRun(code[start..index], new MarkdownTextStyle(Foreground: "#7C3AED"), "number"));
                continue;
            }

            if (StartsWith(code, index, "true") || StartsWith(code, index, "false") || StartsWith(code, index, "null"))
            {
                var keyword = StartsWith(code, index, "true") ? "true" : StartsWith(code, index, "false") ? "false" : "null";
                runs.Add(new MarkdownStyledTextRun(keyword, new MarkdownTextStyle(Bold: true, Foreground: "#1D4ED8"), "keyword"));
                index += keyword.Length;
                continue;
            }

            runs.Add(new MarkdownStyledTextRun(code[index].ToString(), new MarkdownTextStyle(), "punctuation"));
            index++;
        }

        return new MarkdownCodeHighlightResult("built-in", runs);
    }

    private static MarkdownCodeHighlightResult HighlightMarkup(string code)
    {
        var runs = new List<MarkdownStyledTextRun>();
        var index = 0;
        while (index < code.Length)
        {
            if (StartsWith(code, index, "<!--"))
            {
                var start = index;
                var end = code.IndexOf("-->", index, StringComparison.Ordinal);
                index = end >= 0 ? end + 3 : code.Length;
                runs.Add(new MarkdownStyledTextRun(code[start..index], new MarkdownTextStyle(Foreground: "#15803D"), "comment"));
                continue;
            }

            if (code[index] == '<')
            {
                var start = index++;
                while (index < code.Length && code[index] != '>')
                {
                    if (code[index] == '"' || code[index] == '\'')
                    {
                        break;
                    }

                    index++;
                }

                runs.Add(new MarkdownStyledTextRun(code[start..Math.Min(index, code.Length)], new MarkdownTextStyle(Bold: true, Foreground: "#1D4ED8"), "tag"));
                continue;
            }

            if (TryReadString(code, ref index, out var literal))
            {
                runs.Add(new MarkdownStyledTextRun(literal, new MarkdownTextStyle(Foreground: "#B91C1C"), "string"));
                continue;
            }

            runs.Add(new MarkdownStyledTextRun(code[index].ToString(), new MarkdownTextStyle(), "text"));
            index++;
        }

        return new MarkdownCodeHighlightResult("built-in", runs);
    }

    private static bool TryReadComment(string code, ref int index, out string comment)
    {
        if (StartsWith(code, index, "//"))
        {
            var start = index;
            var end = code.IndexOf('\n', index);
            index = end >= 0 ? end : code.Length;
            comment = code[start..index];
            return true;
        }

        if (StartsWith(code, index, "#"))
        {
            var start = index;
            var end = code.IndexOf('\n', index);
            index = end >= 0 ? end : code.Length;
            comment = code[start..index];
            return true;
        }

        comment = string.Empty;
        return false;
    }

    private static bool TryReadString(string code, ref int index, out string literal)
    {
        literal = string.Empty;
        if (index >= code.Length || (code[index] != '"' && code[index] != '\''))
        {
            return false;
        }

        var quote = code[index];
        var start = index++;
        var escaped = false;
        while (index < code.Length)
        {
            if (!escaped && code[index] == quote)
            {
                index++;
                literal = code[start..index];
                return true;
            }

            escaped = !escaped && code[index] == '\\';
            index++;
        }

        literal = code[start..index];
        return true;
    }

    private static bool StartsWith(string code, int index, string value)
    {
        return index + value.Length <= code.Length &&
               string.Compare(code, index, value, 0, value.Length, StringComparison.Ordinal) == 0;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static string NormalizeLanguage(string? languageHint)
    {
        return MarkdownSourceEditing.NormalizeLanguageHint(languageHint);
    }
}
