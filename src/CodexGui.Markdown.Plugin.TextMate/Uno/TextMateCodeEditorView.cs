using CodexGui.Markdown.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace CodexGui.Markdown.Plugin.TextMate;

public sealed class TextMateCodeEditorView : UserControl
{
    private readonly TextMateCodeHighlighter _highlighter = new();
    private readonly ComboBox _languageBox;
    private readonly TextBox _editor;
    private readonly StackPanel _previewLines;
    private readonly DispatcherQueueTimer _previewTimer;

    public TextMateCodeEditorView(
        string code,
        string? languageHint,
        Action<string, string?> onCommit,
        Action onCancel)
    {
        ArgumentNullException.ThrowIfNull(onCommit);
        ArgumentNullException.ThrowIfNull(onCancel);

        _previewTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(120);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RenderPreview();
        };

        _languageBox = new ComboBox
        {
            ItemsSource = new[]
            {
                "csharp",
                "json",
                "markdown",
                "python",
                "xml",
                "xaml",
                "yaml",
                "bash",
                "typescript"
            },
            SelectedItem = MarkdownSourceEditing.NormalizeLanguageHint(languageHint)
        };
        _languageBox.SelectionChanged += (_, _) => SchedulePreview();

        _editor = new TextBox
        {
            Text = code,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono"),
            MinHeight = 220
        };
        _editor.TextChanged += (_, _) => SchedulePreview();

        _previewLines = new StackPanel
        {
            Spacing = 2
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            },
            RowSpacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Edit fenced code block",
                    FontWeight = FontWeights.SemiBold
                },
                _languageBox.Also(static combo => Grid.SetRow(combo, 1)),
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    ColumnSpacing = 12,
                    Children =
                    {
                        _editor,
                        new ScrollViewer
                        {
                            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252)),
                            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 203, 213, 225)),
                            BorderThickness = new Thickness(1),
                            Content = _previewLines
                        }.Also(static preview => Grid.SetColumn(preview, 1))
                    }
                }.Also(static grid => Grid.SetRow(grid, 2)),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Button
                        {
                            Content = "Commit"
                        }.Also(commit => commit.Click += (_, _) => onCommit(_editor.Text, _languageBox.SelectedItem as string)),
                        new Button
                        {
                            Content = "Cancel"
                        }.Also(cancel => cancel.Click += (_, _) => onCancel())
                    }
                }.Also(static footer => Grid.SetRow(footer, 3))
            }
        };

        RenderPreview();
    }

    private void RenderPreview()
    {
        _previewLines.Children.Clear();
        var result = _highlighter.Highlight(_editor.Text, _languageBox.SelectedItem as string);
        foreach (var lineRuns in MarkdownCodeHighlighting.SplitRunsByLine(result.Runs))
        {
            _previewLines.Children.Add(CreateLine(lineRuns));
        }
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private static TextBlock CreateLine(IReadOnlyList<MarkdownStyledTextRun> lineRuns)
    {
        var textBlock = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono"),
            TextWrapping = TextWrapping.NoWrap
        };

        foreach (var run in lineRuns)
        {
            textBlock.Inlines.Add(new Run
            {
                Text = run.Text,
                Foreground = string.IsNullOrWhiteSpace(run.Style.Foreground)
                    ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 15, 23, 42))
                    : new SolidColorBrush(ParseColor(run.Style.Foreground)),
                FontWeight = run.Style.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStyle = run.Style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
            });
        }

        if (textBlock.Inlines.Count == 0)
        {
            textBlock.Text = string.Empty;
        }

        return textBlock;
    }

    private static Windows.UI.Color ParseColor(string? hex)
    {
        var normalized = (hex ?? "#0F172A").Trim().TrimStart('#');
        if (normalized.Length == 6)
        {
            normalized = $"FF{normalized}";
        }

        return Windows.UI.Color.FromArgb(
            Convert.ToByte(normalized[..2], 16),
            Convert.ToByte(normalized.Substring(2, 2), 16),
            Convert.ToByte(normalized.Substring(4, 2), 16),
            Convert.ToByte(normalized.Substring(6, 2), 16));
    }
}

internal static class TextMateUiExtensions
{
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
