namespace CodexGui.Markdown.Plugin.Math;

internal static class MathMarkdownSyntax
{
    public static string NormalizeLineEndings(string? source)
    {
        return (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? source)
    {
        return NormalizeLineEndings(source).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? source)
    {
        return NormalizeLineEndings(source).Trim();
    }

    public static string NormalizeBlockSource(string? source)
    {
        return NormalizeBlockText(source);
    }

    public static string NormalizeInlineSource(string? source)
    {
        return NormalizeInlineText(source);
    }

    public static string BuildMathBlock(string? expression)
    {
        var normalizedExpression = NormalizeBlockText(expression);
        return $"$$\n{normalizedExpression}\n$$";
    }

    public static string BuildInlineMath(string? expression)
    {
        var normalizedExpression = NormalizeInlineText(expression)
            .Replace("$", "\\$", StringComparison.Ordinal);
        return $"${normalizedExpression}$";
    }
}
