using CodexGui.Markdown.Plugin.Math;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SystemMath = System.Math;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownMathViewFactory
{
    private static readonly FontFamily MathFontFamily = new("Cambria Math, STIX Two Math, Times New Roman");
    private static readonly FontFamily SansSerifFamily = new("Inter, Segoe UI, Arial");
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Brush FormulaForeground = Brush("#312E81");
    private static readonly Brush FormulaBackground = Brush("#F5F3FF");
    private static readonly Brush FormulaBorder = Brush("#C4B5FD");
    private static readonly Brush DiagnosticForeground = Brush("#B42318");

    public static UIElement CreateInlineView(string expression, double fontSize, Brush? foreground = null)
    {
        var document = MarkdownMathParser.ParseInline(expression);
        var view = new Border
        {
            Background = FormulaBackground,
            BorderBrush = FormulaBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Child = RenderExpression(
                document.Root,
                new MathRenderContext(
                    MarkdownMathDisplayMode.Inline,
                    SystemMath.Max(fontSize, 13),
                    MarkdownMathTextStyle.Normal,
                    0,
                    foreground ?? FormulaForeground))
        };

        return view;
    }

    public static UIElement CreateBlockView(string expression, double fontSize, Brush? foreground = null)
    {
        var document = MarkdownMathParser.ParseBlock(expression);
        var renderedExpression = RenderExpression(
            document.Root,
            new MathRenderContext(
                MarkdownMathDisplayMode.Block,
                SystemMath.Max(fontSize + 2, 15),
                MarkdownMathTextStyle.Normal,
                0,
                foreground ?? FormulaForeground));
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Border
                {
                    Background = FormulaBackground,
                    BorderBrush = FormulaBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 12),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = renderedExpression
                    }
                }
            }
        };

        if (document.HasDiagnostics)
        {
            var diagnostics = new StackPanel { Spacing = 2 };
            foreach (var diagnostic in document.Diagnostics)
            {
                diagnostics.Children.Add(new TextBlock
                {
                    Text = diagnostic.Message,
                    Foreground = DiagnosticForeground,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11
                });
            }

            content.Children.Add(diagnostics);
        }

        return content;
    }

    private static UIElement RenderExpression(MarkdownMathExpression expression, MathRenderContext context)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = context.DisplayMode == MarkdownMathDisplayMode.Block
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left
        };

        foreach (var child in expression.Children)
        {
            row.Children.Add(RenderNode(child, context));
        }

        return row;
    }

    private static UIElement RenderNode(MarkdownMathNode node, MathRenderContext context)
    {
        return node switch
        {
            MarkdownMathExpression expression => RenderExpression(expression, context),
            MarkdownMathGroupedExpression grouped => RenderExpression(grouped.Content, context),
            MarkdownMathIdentifier identifier => CreateText(identifier.Text, context, italic: true),
            MarkdownMathNumber number => CreateText(number.Text, context),
            MarkdownMathOperator op => CreateText(op.Text, context),
            MarkdownMathSpace space => new Border { Width = context.FontSize * space.WidthEm },
            MarkdownMathTextRun textRun => CreateStyledText(textRun, context),
            MarkdownMathSymbol symbol => CreateText(
                symbol.RenderText,
                context,
                fontSize: symbol.IsLargeOperator && context.DisplayMode == MarkdownMathDisplayMode.Block
                    ? context.FontSize * 1.2
                    : context.FontSize,
                italic: !symbol.IsLargeOperator),
            MarkdownMathCommand command => CreateText($"\\{command.Name}", context, foreground: DiagnosticForeground),
            MarkdownMathStyledExpression styled => RenderExpression(styled.Content, context.WithStyle(styled.Style)),
            MarkdownMathFraction fraction => RenderFraction(fraction, context),
            MarkdownMathRoot root => RenderRoot(root, context),
            MarkdownMathScript script => RenderScript(script, context),
            MarkdownMathDelimited delimited => RenderDelimited(delimited, context),
            MarkdownMathAccent accent => RenderAccent(accent, context),
            MarkdownMathEnvironment environment => RenderEnvironment(environment, context),
            MarkdownMathError error => CreateText(error.Text, context, foreground: DiagnosticForeground),
            _ => CreateText(node.ToString() ?? string.Empty, context)
        };
    }

    private static UIElement RenderFraction(MarkdownMathFraction fraction, MathRenderContext context)
    {
        return new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = RenderExpression(fraction.Numerator, context.ForScript())
                },
                new Border
                {
                    Height = 1,
                    MinWidth = SystemMath.Max(context.FontSize * 1.8, 18),
                    Background = context.Foreground
                },
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = RenderExpression(fraction.Denominator, context.ForScript())
                }
            }
        };
    }

    private static UIElement RenderRoot(MarkdownMathRoot root, MathRenderContext context)
    {
        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                CreateText("√", context, fontSize: context.FontSize * 1.25, italic: false),
                new Border
                {
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    BorderBrush = context.Foreground,
                    Padding = new Thickness(4, 3, 1, 0),
                    Child = RenderExpression(root.Radicand, context)
                }
            }
        };

        if (root.Degree is null)
        {
            return body;
        }

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var degree = new Border
        {
            Margin = new Thickness(0, 0, 2, 0),
            Child = RenderExpression(root.Degree, context.ForScript())
        };
        grid.Children.Add(degree);
        Grid.SetRow(body, 1);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static UIElement RenderScript(MarkdownMathScript script, MathRenderContext context)
    {
        var baseControl = RenderNode(script.Base, context);
        var scriptContext = context.ForScript();

        if (script.Base is MarkdownMathSymbol { IsLargeOperator: true } && context.DisplayMode == MarkdownMathDisplayMode.Block)
        {
            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (script.Superscript is not null)
            {
                grid.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = RenderExpression(script.Superscript, scriptContext)
                });
            }

            Grid.SetRow(baseControl, 1);
            grid.Children.Add(baseControl);

            if (script.Subscript is not null)
            {
                var subscript = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = RenderExpression(script.Subscript, scriptContext)
                };
                Grid.SetRow(subscript, 2);
                grid.Children.Add(subscript);
            }

            return grid;
        }

        var scriptStack = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (script.Superscript is not null)
        {
            scriptStack.Children.Add(new Border
            {
                Margin = new Thickness(0, -2, 0, 0),
                Child = RenderExpression(script.Superscript, scriptContext)
            });
        }

        if (script.Subscript is not null)
        {
            scriptStack.Children.Add(new Border
            {
                Margin = new Thickness(0, -1, 0, 0),
                Child = RenderExpression(script.Subscript, scriptContext)
            });
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                baseControl,
                scriptStack
            }
        };
    }

    private static UIElement RenderDelimited(MarkdownMathDelimited delimited, MathRenderContext context)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.Equals(delimited.LeftDelimiter, ".", StringComparison.Ordinal))
        {
            row.Children.Add(CreateText(delimited.LeftDelimiter, context, fontSize: context.FontSize * 1.1, italic: false));
        }

        row.Children.Add(RenderExpression(delimited.Content, context));

        if (!string.Equals(delimited.RightDelimiter, ".", StringComparison.Ordinal))
        {
            row.Children.Add(CreateText(delimited.RightDelimiter, context, fontSize: context.FontSize * 1.1, italic: false));
        }

        return row;
    }

    private static UIElement RenderAccent(MarkdownMathAccent accent, MathRenderContext context)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var accentControl = accent.Underline
            ? new Border
            {
                Height = 1,
                MinWidth = SystemMath.Max(context.FontSize * 0.7, 10),
                Background = context.Foreground,
                Margin = new Thickness(0, 1, 0, 0)
            }
            : (UIElement)CreateText(accent.AccentText, context.ForScript(), italic: false);
        var baseControl = RenderNode(accent.Base, context);

        if (accent.Underline)
        {
            Grid.SetRow(baseControl, 0);
            Grid.SetRow(accentControl, 1);
        }
        else
        {
            Grid.SetRow(accentControl, 0);
            Grid.SetRow(baseControl, 1);
        }

        grid.Children.Add(accentControl);
        grid.Children.Add(baseControl);
        return grid;
    }

    private static UIElement RenderEnvironment(MarkdownMathEnvironment environment, MathRenderContext context)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var maxColumns = environment.Rows.Count == 0 ? 0 : environment.Rows.Max(static row => row.Count);
        for (var columnIndex = 0; columnIndex < maxColumns; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var rowIndex = 0; rowIndex < environment.Rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var environmentRow = environment.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < environmentRow.Count; columnIndex++)
            {
                var cell = new Border
                {
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = RenderExpression(environmentRow[columnIndex], context)
                };
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        var (leftDelimiter, rightDelimiter) = ResolveEnvironmentDelimiters(environment.Name);
        if (leftDelimiter is null && rightDelimiter is null)
        {
            return grid;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrEmpty(leftDelimiter))
        {
            row.Children.Add(CreateText(leftDelimiter, context, fontSize: context.FontSize * 1.15, italic: false));
        }

        row.Children.Add(grid);

        if (!string.IsNullOrEmpty(rightDelimiter))
        {
            row.Children.Add(CreateText(rightDelimiter, context, fontSize: context.FontSize * 1.15, italic: false));
        }

        return row;
    }

    private static (string? Left, string? Right) ResolveEnvironmentDelimiters(string environmentName)
    {
        return environmentName switch
        {
            "pmatrix" => ("(", ")"),
            "bmatrix" => ("[", "]"),
            "Bmatrix" => ("{", "}"),
            "vmatrix" => ("|", "|"),
            "Vmatrix" => ("‖", "‖"),
            "cases" => ("{", null),
            _ => (null, null)
        };
    }

    private static TextBlock CreateStyledText(MarkdownMathTextRun textRun, MathRenderContext context)
    {
        return textRun.Style switch
        {
            MarkdownMathTextStyle.Bold => CreateText(textRun.Text, context, italic: false, weight: FontWeights.SemiBold),
            MarkdownMathTextStyle.Italic => CreateText(textRun.Text, context, italic: true),
            MarkdownMathTextStyle.Roman => CreateText(textRun.Text, context, italic: false, family: MathFontFamily),
            MarkdownMathTextStyle.SansSerif => CreateText(textRun.Text, context, italic: false, family: SansSerifFamily),
            MarkdownMathTextStyle.Monospace => CreateText(textRun.Text, context, italic: false, family: MonospaceFamily),
            MarkdownMathTextStyle.Operator => CreateText(textRun.Text, context, italic: false, weight: FontWeights.SemiBold),
            _ => CreateText(textRun.Text, context)
        };
    }

    private static TextBlock CreateText(
        string text,
        MathRenderContext context,
        double? fontSize = null,
        bool italic = false,
        Brush? foreground = null,
        Windows.UI.Text.FontWeight? weight = null,
        FontFamily? family = null)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = family ?? MathFontFamily,
            FontSize = fontSize ?? context.FontSize,
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = foreground ?? context.Foreground,
            IsTextSelectionEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static SolidColorBrush Brush(string hex)
    {
        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length == 6)
        {
            normalized = $"FF{normalized}";
        }

        return new SolidColorBrush(Color.FromArgb(
            Convert.ToByte(normalized[..2], 16),
            Convert.ToByte(normalized.Substring(2, 2), 16),
            Convert.ToByte(normalized.Substring(4, 2), 16),
            Convert.ToByte(normalized.Substring(6, 2), 16)));
    }

    private readonly record struct MathRenderContext(
        MarkdownMathDisplayMode DisplayMode,
        double FontSize,
        MarkdownMathTextStyle Style,
        int ScriptDepth,
        Brush Foreground)
    {
        public MathRenderContext ForScript()
        {
            return this with
            {
                FontSize = SystemMath.Max(FontSize * 0.78, 10),
                ScriptDepth = ScriptDepth + 1
            };
        }

        public MathRenderContext WithStyle(MarkdownMathTextStyle style)
        {
            return this with { Style = style };
        }
    }
}
