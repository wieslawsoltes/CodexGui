namespace CodexGui.Markdown.Core;

public static class MarkdownCodeHighlighting
{
    public static MarkdownStyledTextRun[][] SplitRunsByLine(IReadOnlyList<MarkdownStyledTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var lines = new List<MarkdownStyledTextRun[]>();
        var current = new List<MarkdownStyledTextRun>();

        foreach (var run in runs)
        {
            var segments = run.Text.Split('\n', StringSplitOptions.None);
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length > 0)
                {
                    current.Add(run with { Text = segments[index] });
                }

                if (index < segments.Length - 1)
                {
                    lines.Add(current.Count == 0 ? [] : current.ToArray());
                    current.Clear();
                }
            }
        }

        lines.Add(current.Count == 0 ? [] : current.ToArray());
        return lines.ToArray();
    }
}
