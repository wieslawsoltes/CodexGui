using CodexGui.Markdown.Core;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Themes;
using TextMateFontStyle = TextMateSharp.Themes.FontStyle;

namespace CodexGui.Markdown.Plugin.TextMate;

public sealed class TextMateCodeHighlighter : IMarkdownCodeHighlighter
{
    private static readonly RegistryOptions RegistryOptions = new(ThemeName.LightPlus);
    private static readonly Theme Theme = Theme.CreateFromRawTheme(RegistryOptions.LoadTheme(ThemeName.LightPlus), RegistryOptions);
    private static readonly BalancedBracketSelectors EmptyBalancedBracketSelectors = new([], []);
    private static readonly Dictionary<string, string?> ScopeNameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IGrammar?> GrammarCache = new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["axaml"] = ".axaml",
        ["bash"] = ".sh",
        ["c#"] = ".cs",
        ["cs"] = ".cs",
        ["csharp"] = ".cs",
        ["css"] = ".css",
        ["go"] = ".go",
        ["html"] = ".html",
        ["htm"] = ".html",
        ["java"] = ".java",
        ["javascript"] = ".js",
        ["js"] = ".js",
        ["json"] = ".json",
        ["jsonc"] = ".json",
        ["markdown"] = ".md",
        ["md"] = ".md",
        ["powershell"] = ".ps1",
        ["ps1"] = ".ps1",
        ["pwsh"] = ".ps1",
        ["python"] = ".py",
        ["py"] = ".py",
        ["rs"] = ".rs",
        ["rust"] = ".rs",
        ["shell"] = ".sh",
        ["sh"] = ".sh",
        ["sql"] = ".sql",
        ["ts"] = ".ts",
        ["tsx"] = ".tsx",
        ["typescript"] = ".ts",
        ["xaml"] = ".xaml",
        ["xml"] = ".xml",
        ["yml"] = ".yml",
        ["yaml"] = ".yml"
    };

    public int Order => 10;

    public bool CanHighlight(string? languageHint)
    {
        return ResolveScopeName(languageHint) is not null;
    }

    public MarkdownCodeHighlightResult Highlight(string code, string? languageHint)
    {
        var scope = ResolveScopeName(languageHint);
        if (scope is null)
        {
            return new MarkdownCodeHighlightResult("plain", [new MarkdownStyledTextRun(code, new MarkdownTextStyle())]);
        }

        var grammar = TryCreateGrammar(scope);
        if (grammar is null)
        {
            return new MarkdownCodeHighlightResult("plain", [new MarkdownStyledTextRun(code, new MarkdownTextStyle())]);
        }

        var runs = new List<MarkdownStyledTextRun>();
        IStateStack? state = null;
        var lines = NormalizeCode(code).Split('\n', StringSplitOptions.None);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var result = state is null
                ? grammar.TokenizeLine2(line)
                : grammar.TokenizeLine2(line, state, TimeSpan.FromMilliseconds(100));
            state = result.RuleStack;

            if (result.Tokens.Length == 0)
            {
                runs.Add(new MarkdownStyledTextRun(line, new MarkdownTextStyle()));
            }
            else
            {
                for (var tokenIndex = 0; tokenIndex + 1 < result.Tokens.Length; tokenIndex += 2)
                {
                    var start = result.Tokens[tokenIndex];
                    var metadata = result.Tokens[tokenIndex + 1];
                    var end = tokenIndex + 2 < result.Tokens.Length ? result.Tokens[tokenIndex + 2] : line.Length;
                    if (start >= end || start < 0 || end > line.Length)
                    {
                        continue;
                    }

                    runs.Add(new MarkdownStyledTextRun(
                        line[start..end],
                        CreateStyle(metadata),
                        Scope: scope));
                }
            }

            if (lineIndex < lines.Length - 1)
            {
                runs.Add(new MarkdownStyledTextRun("\n", new MarkdownTextStyle()));
            }
        }

        return new MarkdownCodeHighlightResult("textmate", runs);
    }

    public string? ResolveScopeName(string? languageHint)
    {
        var extension = ResolveExtension(languageHint);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        lock (CacheLock)
        {
            if (ScopeNameCache.TryGetValue(extension, out var scopeName))
            {
                return scopeName;
            }

            var language = RegistryOptions.GetLanguageByExtension(extension);
            scopeName = language is null ? null : RegistryOptions.GetScopeByLanguageId(language.Id);
            ScopeNameCache[extension] = scopeName;
            return scopeName;
        }
    }

    public string? ResolveExtension(string? languageHint)
    {
        var normalized = MarkdownSourceEditing.NormalizeLanguageHint(languageHint);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ExtensionMap.TryGetValue(normalized, out var mappedExtension))
        {
            return mappedExtension;
        }

        return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : $".{normalized}";
    }

    private static IGrammar? TryCreateGrammar(string grammarScope)
    {
        lock (CacheLock)
        {
            if (GrammarCache.TryGetValue(grammarScope, out var cachedGrammar))
            {
                return cachedGrammar;
            }

            var rawGrammar = RegistryOptions.GetGrammar(grammarScope);
            if (rawGrammar is null)
            {
                GrammarCache[grammarScope] = null;
                return null;
            }

            var registry = new SyncRegistry(Theme);
            registry.AddGrammar(rawGrammar, RegistryOptions.GetInjections(grammarScope));
            var grammar = registry.GrammarForScopeName(
                grammarScope,
                0,
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal),
                EmptyBalancedBracketSelectors);
            GrammarCache[grammarScope] = grammar;
            return grammar;
        }
    }

    private static MarkdownTextStyle CreateStyle(int metadata)
    {
        var foreground = Theme.GetColor(EncodedTokenAttributes.GetForeground(metadata));
        var fontStyle = EncodedTokenAttributes.GetFontStyle(metadata);
        return new MarkdownTextStyle(
            Bold: HasStyle(fontStyle, TextMateFontStyle.Bold),
            Italic: HasStyle(fontStyle, TextMateFontStyle.Italic),
            Strikethrough: HasStyle(fontStyle, TextMateFontStyle.Strikethrough),
            Underline: HasStyle(fontStyle, TextMateFontStyle.Underline),
            Foreground: NormalizeForeground(foreground));
    }

    private static bool HasStyle(TextMateFontStyle value, TextMateFontStyle flag)
    {
        return (value & flag) == flag;
    }

    private static string? NormalizeForeground(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return null;
        }

        var normalized = colorText.Trim();
        return normalized.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("#FFF", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string NormalizeCode(string code)
    {
        return code
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
    }
}
