using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CodexGui.Markdown.Controls;

internal readonly record struct MarkdownCalloutPresentation(string Title, string AccentHex, string BackgroundHex);

internal static class MarkdownCalloutSurfaceFactory
{
    public static string ResolveTitle(string? kind, string fallbackTitle)
    {
        var label = FormatLabel(kind);
        return string.IsNullOrWhiteSpace(label) ? fallbackTitle : label;
    }

    public static string FormatLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Trim()
            .Replace('-', ' ')
            .Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    public static MarkdownCalloutPresentation ResolvePresentation(string? kind, string fallbackTitle)
    {
        var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        var title = ResolveTitle(kind, fallbackTitle);

        return normalizedKind switch
        {
            "caution" or "warning" => new MarkdownCalloutPresentation(title, "#D97706", "#FFFBEB"),
            "danger" or "error" => new MarkdownCalloutPresentation(title, "#DC2626", "#FEF2F2"),
            "important" => new MarkdownCalloutPresentation(title, "#7C3AED", "#F5F3FF"),
            "success" or "tip" => new MarkdownCalloutPresentation(title, "#059669", "#ECFDF5"),
            "info" or "note" => new MarkdownCalloutPresentation(title, "#2563EB", "#EFF6FF"),
            _ => new MarkdownCalloutPresentation(title, "#64748B", "#F8FAFC")
        };
    }

    public static UIElement CreateSurface(
        string title,
        string? subtitle,
        UIElement body,
        string? kind,
        double fontSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);

        var presentation = ResolvePresentation(kind, title);
        var content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12)
        };

        content.Children.Add(new TextBlock
        {
            Text = presentation.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = MarkdownBrushes.Tone(presentation.AccentHex),
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            content.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = MarkdownBrushes.Tone("#6E6E6E"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = Math.Max(fontSize - 2, 11)
            });
        }

        content.Children.Add(body);

        return new Border
        {
            Background = MarkdownBrushes.Tone(presentation.BackgroundHex),
            BorderBrush = MarkdownBrushes.Tone("#D0D7DE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new Border
                    {
                        Width = 4,
                        Background = MarkdownBrushes.Tone(presentation.AccentHex)
                    },
                    content.Also(static panel => Grid.SetColumn(panel, 1))
                }
            }
        };
    }
}

internal static class MarkdownBrushes
{
    public static Brush Tone(string hex) => MarkdownTextBlock.ResolveSharedBrush(hex);
}
