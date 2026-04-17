using CodexGui.Markdown.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownDefinitionListViewFactory
{
    public static UIElement CreateBlockView(
        MarkdownDefinitionListBlock block,
        Func<string, double, UIElement> renderNested,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(renderNested);

        var content = new StackPanel
        {
            Spacing = 12
        };

        if (block.Items.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Definition list is empty.",
                Foreground = MarkdownBrushes.Tone("#64748B"),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var item in block.Items)
            {
                var termsPanel = new StackPanel
                {
                    Spacing = 8
                };

                foreach (var term in item.Terms)
                {
                    termsPanel.Children.Add(new Border
                    {
                        Background = MarkdownBrushes.Tone("#EFF6FF"),
                        BorderBrush = MarkdownBrushes.Tone("#BFDBFE"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 8, 10, 8),
                        Child = MarkdownNestedContentStyler.Apply(
                            renderNested(term.Markdown, 0),
                            new MarkdownNestedContentStyle(
                                FontSize: Math.Max(fontSize, 12),
                                FontWeight: FontWeights.SemiBold))
                    });
                }

                content.Children.Add(new Border
                {
                    BorderBrush = MarkdownBrushes.Tone("#E2E8F0"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    Child = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = new GridLength(0.34, GridUnitType.Star) },
                            new ColumnDefinition { Width = new GridLength(0.66, GridUnitType.Star) }
                        },
                        ColumnSpacing = 14,
                        Children =
                        {
                            termsPanel,
                            new Border
                            {
                                BorderBrush = MarkdownBrushes.Tone("#E2E8F0"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(8),
                                Padding = new Thickness(12, 10, 12, 10),
                                Child = renderNested(item.DefinitionMarkdown, 16)
                            }.Also(static definition => Grid.SetColumn(definition, 1))
                        }
                    }
                });
            }
        }

        return new Border
        {
            Background = MarkdownBrushes.Tone("#F8FAFC"),
            BorderBrush = MarkdownBrushes.Tone("#D0D7DE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Definition list",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = MarkdownBrushes.Tone("#2563EB")
                    },
                    content
                }
            }
        };
    }
}
