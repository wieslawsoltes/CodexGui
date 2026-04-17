using System.Globalization;
using CodexGui.Markdown.Core;
using CodexGui.Markdown.Plugin.Mermaid;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownMermaidViewFactory
{
    private const double DefaultWidth = 720;
    private const double SurfacePadding = 16;

    private static readonly Brush SurfaceBackground = Brush("#FFFFFF");
    private static readonly Brush SurfaceBorderBrush = Brush("#D0D7DE");
    private static readonly Brush HeaderBackground = Brush("#EEF2FF");
    private static readonly Brush HeaderForeground = Brush("#4338CA");
    private static readonly Brush MetaForeground = Brush("#6B7280");
    private static readonly Brush SourceBackground = Brush("#F8FAFC");
    private static readonly Brush DiagramBackground = Brush("#FDFEFF");
    private static readonly Brush NodeFill = Brush("#F8FAFF");
    private static readonly Brush NodeBorder = Brush("#7C8BFF");
    private static readonly Brush AccentFill = Brush("#EEF2FF");
    private static readonly Brush AccentStroke = Brush("#4F46E5");
    private static readonly Brush EdgeBrush = Brush("#475569");
    private static readonly Brush SequenceHeaderFill = Brush("#F5F7FF");
    private static readonly Brush SequenceLifelineBrush = Brush("#94A3B8");
    private static readonly Brush[] AccentPalette =
    [
        Brush("#4F46E5"),
        Brush("#0891B2"),
        Brush("#16A34A"),
        Brush("#F59E0B"),
        Brush("#DB2777"),
        Brush("#7C3AED")
    ];
    private static readonly Brush[] SoftPalette =
    [
        Brush("#EEF2FF"),
        Brush("#ECFEFF"),
        Brush("#ECFDF5"),
        Brush("#FFFBEB"),
        Brush("#FDF2F8"),
        Brush("#F5F3FF")
    ];

    public static UIElement CreateBlockView(string diagramSource, double availableWidth, double fontSize, Brush? foreground = null)
    {
        var diagram = MermaidDiagramParser.Parse(diagramSource);
        return diagram is MermaidUnsupportedDiagramDefinition unsupported
            ? CreateUnsupportedSurface(unsupported, fontSize, foreground)
            : CreateSupportedSurface(diagram, ResolvePreferredWidth(availableWidth), fontSize, foreground);
    }

    public static UIElement CreateBlockView(MarkdownMermaidBlock block, double availableWidth, double fontSize, Brush? foreground = null)
    {
        ArgumentNullException.ThrowIfNull(block);

        var diagram = MermaidDiagramParser.Parse(block.DiagramSource);
        var metadataSuffix = block.Syntax == MarkdownMermaidBlockSyntax.CustomContainer && !string.IsNullOrWhiteSpace(block.Arguments)
            ? $"container args: {block.Arguments}"
            : block.Syntax == MarkdownMermaidBlockSyntax.CustomContainer
                ? "container syntax"
                : null;

        return diagram is MermaidUnsupportedDiagramDefinition unsupported
            ? CreateUnsupportedSurface(unsupported, fontSize, foreground, metadataSuffix)
            : CreateSupportedSurface(diagram, ResolvePreferredWidth(availableWidth), fontSize, foreground, metadataSuffix);
    }

    private static UIElement CreateSupportedSurface(
        MermaidDiagramDefinition diagram,
        double availableWidth,
        double fontSize,
        Brush? foreground,
        string? metadataSuffix = null)
    {
        var body = diagram switch
        {
            MermaidFlowchartDiagramDefinition flowchart => CreateGraphSurface(
                flowchart.Nodes.Select(static node => new GraphNode(node.Id, node.Label, node.Shape, [])),
                flowchart.Edges.Select(static edge => new GraphEdge(edge.FromId, edge.ToId, edge.Label, edge.Dotted, Directed: true)),
                flowchart.Direction,
                availableWidth,
                fontSize,
                foreground),
            MermaidStateDiagramDefinition stateDiagram => CreateGraphSurface(
                stateDiagram.States.Select(static node => new GraphNode(node.Id, node.Label, node.Shape, [])),
                stateDiagram.Transitions.Select(static edge => new GraphEdge(edge.FromId, edge.ToId, edge.Label, edge.Dotted, Directed: true)),
                stateDiagram.Direction,
                availableWidth,
                fontSize,
                foreground),
            MermaidMindmapDiagramDefinition mindmap => CreateMindmapSurface(mindmap, availableWidth, fontSize, foreground),
            MermaidSequenceDiagramDefinition sequence => CreateSequenceSurface(sequence, availableWidth, fontSize, foreground),
            MermaidClassDiagramDefinition classDiagram => CreateGraphSurface(
                classDiagram.Classes.Select(static node => new GraphNode(node.Id, node.Label, MermaidNodeShape.Rectangle, node.Members)),
                classDiagram.Relations.Select(static edge => new GraphEdge(edge.FromId, edge.ToId, edge.Label, edge.Dotted, edge.Directed)),
                classDiagram.Direction,
                availableWidth,
                fontSize,
                foreground),
            MermaidErDiagramDefinition erDiagram => CreateGraphSurface(
                erDiagram.Entities.Select(static entity => new GraphNode(
                    entity.Id,
                    entity.Label,
                    MermaidNodeShape.Rectangle,
                    entity.Attributes.Select(FormatErAttribute).ToArray())),
                erDiagram.Relationships.Select(static relation => new GraphEdge(
                    relation.FromId,
                    relation.ToId,
                    relation.Label,
                    Dotted: !relation.Identifying,
                    Directed: true,
                    StartMarker: ToErCardinalityLabel(relation.FromCardinality),
                    EndMarker: ToErCardinalityLabel(relation.ToCardinality))),
                erDiagram.Direction,
                availableWidth,
                fontSize,
                foreground),
            MermaidPieDiagramDefinition pieChart => CreatePieSurface(pieChart, availableWidth, fontSize, foreground),
            MermaidJourneyDiagramDefinition journey => CreateJourneySurface(journey, availableWidth, fontSize, foreground),
            MermaidTimelineDiagramDefinition timeline => CreateTimelineSurface(timeline, availableWidth, fontSize, foreground),
            MermaidQuadrantChartDiagramDefinition quadrant => CreateQuadrantSurface(quadrant, availableWidth, fontSize, foreground),
            _ => CreateSourcePreview(diagram.Source, fontSize, foreground)
        };
        var subtitle = ComposeSubtitle(diagram.Subtitle, metadataSuffix);
        return CreateChrome(diagram.Title, subtitle, body);
    }

    private static UIElement CreateUnsupportedSurface(
        MermaidUnsupportedDiagramDefinition diagram,
        double fontSize,
        Brush? foreground,
        string? metadataSuffix = null)
    {
        var subtitle = ComposeSubtitle(diagram.Reason, metadataSuffix);
        return CreateChrome(
            "Mermaid diagram",
            subtitle,
            new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = diagram.Reason,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = foreground ?? EdgeBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Supported native Uno previews include flowcharts, sequence diagrams, state diagrams, class diagrams, pie charts, user journeys, timelines, quadrant charts, mind maps, and ER diagrams.",
                        Foreground = MetaForeground,
                        TextWrapping = TextWrapping.Wrap
                    },
                    CreateSourcePreview(diagram.Source, fontSize, foreground)
                }
            });
    }

    private static string ComposeSubtitle(string subtitle, string? metadataSuffix)
    {
        return string.IsNullOrWhiteSpace(metadataSuffix)
            ? subtitle
            : $"{subtitle} • {metadataSuffix}";
    }

    private static UIElement CreateChrome(string title, string subtitle, UIElement body)
    {
        return new Border
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                Children =
                {
                    CreateHeader(title, subtitle),
                    new Border
                    {
                        Background = DiagramBackground,
                        Padding = new Thickness(12),
                        Child = body
                    }.Also(static element => Grid.SetRow(element, 1))
                }
            }
        };
    }

    private static UIElement CreateHeader(string title, string subtitle)
    {
        return new Border
        {
            Background = HeaderBackground,
            Padding = new Thickness(16, 10),
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = HeaderForeground,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        Foreground = MetaForeground,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }.Also(static meta => Grid.SetColumn(meta, 1))
                }
            }
        };
    }

    private static UIElement CreateSourcePreview(string source, double fontSize, Brush? foreground)
    {
        return new Border
        {
            Background = SourceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = source,
                    FontFamily = new FontFamily("Cascadia Mono"),
                    FontSize = Math.Max(fontSize - 1, 12),
                    Foreground = foreground ?? EdgeBrush,
                    TextWrapping = TextWrapping.NoWrap,
                    IsTextSelectionEnabled = true
                }
            }
        };
    }

    private static UIElement CreateMindmapSurface(
        MermaidMindmapDiagramDefinition mindmap,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        AppendMindmapNode(mindmap.Root, nodes, edges);
        return CreateGraphSurface(nodes, edges, MermaidFlowDirection.LeftToRight, availableWidth, fontSize, foreground);
    }

    private static void AppendMindmapNode(
        MermaidMindmapNodeDefinition node,
        List<GraphNode> nodes,
        List<GraphEdge> edges)
    {
        nodes.Add(new GraphNode(node.Id, node.Label, node.Shape, []));
        foreach (var child in node.Children)
        {
            edges.Add(new GraphEdge(node.Id, child.Id, Label: null, Dotted: false, Directed: true));
            AppendMindmapNode(child, nodes, edges);
        }
    }

    private static UIElement CreateGraphSurface(
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        MermaidFlowDirection direction,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0)
        {
            return new TextBlock
            {
                Text = "No Mermaid nodes were available to render.",
                Foreground = MetaForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var edgeList = edges.ToList();
        var levels = ComputeNodeLevels(nodeList, edgeList);
        var grouped = nodeList
            .GroupBy(node => levels.TryGetValue(node.Id, out var level) ? level : 0)
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToList();

        var horizontal = direction is MermaidFlowDirection.LeftToRight or MermaidFlowDirection.RightToLeft;
        var reverse = direction is MermaidFlowDirection.RightToLeft or MermaidFlowDirection.BottomToTop;
        var orderedGroups = reverse ? grouped.AsEnumerable().Reverse().ToList() : grouped;

        const double padding = 18;
        const double columnGap = 36;
        const double rowGap = 24;
        var canvasWidth = Math.Max(availableWidth - 24, 320);
        var levelCount = Math.Max(orderedGroups.Count, 1);
        var maxGroupSize = Math.Max(orderedGroups.Max(static group => group.Count), 1);
        var primarySize = horizontal
            ? Math.Max((canvasWidth - (padding * 2) - ((levelCount - 1) * columnGap)) / levelCount, 170)
            : Math.Max((canvasWidth - (padding * 2) - ((maxGroupSize - 1) * columnGap)) / maxGroupSize, 170);

        var placements = new Dictionary<string, GraphNodePlacement>(StringComparer.Ordinal);
        var contentBottom = padding;
        for (var primaryIndex = 0; primaryIndex < orderedGroups.Count; primaryIndex++)
        {
            var group = orderedGroups[primaryIndex];
            for (var secondaryIndex = 0; secondaryIndex < group.Count; secondaryIndex++)
            {
                var node = group[secondaryIndex];
                var size = EstimateNodeSize(node, primarySize, fontSize);
                double x;
                double y;

                if (horizontal)
                {
                    x = padding + (primaryIndex * (primarySize + columnGap)) + Math.Max((primarySize - size.Width) / 2, 0);
                    y = padding + (secondaryIndex * (size.Height + rowGap));
                }
                else
                {
                    x = padding + (secondaryIndex * (primarySize + columnGap)) + Math.Max((primarySize - size.Width) / 2, 0);
                    y = padding + (primaryIndex * (size.Height + rowGap));
                }

                placements[node.Id] = new GraphNodePlacement(node, new Rect(x, y, size.Width, size.Height));
                contentBottom = Math.Max(contentBottom, y + size.Height);
            }
        }

        var canvas = new Canvas
        {
            Width = Math.Max(canvasWidth, placements.Values.Max(static placement => placement.Bounds.Right) + padding),
            Height = Math.Max(contentBottom + padding, 180)
        };

        foreach (var edge in edgeList)
        {
            if (!placements.TryGetValue(edge.FromId, out var fromPlacement) ||
                !placements.TryGetValue(edge.ToId, out var toPlacement))
            {
                continue;
            }

            var (start, end) = ResolveConnectionPoints(fromPlacement.Bounds, toPlacement.Bounds, horizontal);
            var line = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = EdgeBrush,
                StrokeThickness = edge.Dotted ? 1.5 : 1.8
            };
            if (edge.Dotted)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 4 };
            }

            canvas.Children.Add(line);

            if (edge.Directed)
            {
                canvas.Children.Add(CreateArrowHead(start, end));
            }

            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var label = CreateEdgeLabel(edge.Label, fontSize, foreground);
                var midX = (start.X + end.X) / 2;
                var midY = (start.Y + end.Y) / 2;
                Canvas.SetLeft(label, midX - 36);
                Canvas.SetTop(label, midY - 26);
                Canvas.SetZIndex(label, 3);
                canvas.Children.Add(label);
            }

            if (!string.IsNullOrWhiteSpace(edge.StartMarker))
            {
                var marker = CreateMarkerLabel(edge.StartMarker, fontSize, foreground);
                Canvas.SetLeft(marker, start.X - 22);
                Canvas.SetTop(marker, start.Y - 24);
                Canvas.SetZIndex(marker, 3);
                canvas.Children.Add(marker);
            }

            if (!string.IsNullOrWhiteSpace(edge.EndMarker))
            {
                var marker = CreateMarkerLabel(edge.EndMarker, fontSize, foreground);
                Canvas.SetLeft(marker, end.X - 22);
                Canvas.SetTop(marker, end.Y + 6);
                Canvas.SetZIndex(marker, 3);
                canvas.Children.Add(marker);
            }
        }

        foreach (var placement in placements.Values)
        {
            var nodeElement = CreateGraphNodeElement(placement.Node, placement.Bounds.Width, placement.Bounds.Height, fontSize, foreground);
            Canvas.SetLeft(nodeElement, placement.Bounds.X);
            Canvas.SetTop(nodeElement, placement.Bounds.Y);
            Canvas.SetZIndex(nodeElement, 4);
            canvas.Children.Add(nodeElement);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvas
        };
    }

    private static UIElement CreateSequenceSurface(
        MermaidSequenceDiagramDefinition sequence,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        if (sequence.Participants.Count == 0)
        {
            return new TextBlock
            {
                Text = "No sequence participants were available to render.",
                Foreground = MetaForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        const double padding = 18;
        const double columnMinWidth = 150;
        const double headerHeight = 54;
        const double rowHeight = 58;
        var columnWidth = Math.Max(columnMinWidth, (availableWidth - (padding * 2)) / Math.Max(sequence.Participants.Count, 1));
        var canvasWidth = Math.Max((columnWidth * sequence.Participants.Count) + (padding * 2), availableWidth - 16);
        var canvasHeight = headerHeight + (sequence.Messages.Count * rowHeight) + 64;
        var canvas = new Canvas
        {
            Width = canvasWidth,
            Height = Math.Max(canvasHeight, 180)
        };

        var centers = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var index = 0; index < sequence.Participants.Count; index++)
        {
            var participant = sequence.Participants[index];
            var left = padding + (index * columnWidth);
            var width = Math.Max(columnWidth - 16, 120);
            var header = new Border
            {
                Width = width,
                Height = headerHeight,
                Background = SequenceHeaderFill,
                BorderBrush = NodeBorder,
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Child = new TextBlock
                {
                    Text = participant.Label,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foreground ?? EdgeBrush
                }
            };

            Canvas.SetLeft(header, left + ((columnWidth - width) / 2));
            Canvas.SetTop(header, padding);
            Canvas.SetZIndex(header, 3);
            canvas.Children.Add(header);

            var centerX = left + (columnWidth / 2);
            centers[participant.Id] = centerX;
            canvas.Children.Add(new Line
            {
                X1 = centerX,
                X2 = centerX,
                Y1 = padding + headerHeight,
                Y2 = canvasHeight - 18,
                Stroke = SequenceLifelineBrush,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            });
        }

        for (var index = 0; index < sequence.Messages.Count; index++)
        {
            var message = sequence.Messages[index];
            if (!centers.TryGetValue(message.FromId, out var fromX) || !centers.TryGetValue(message.ToId, out var toX))
            {
                continue;
            }

            var y = padding + headerHeight + 28 + (index * rowHeight);
            var start = new Point(fromX, y);
            var end = new Point(toX, y);
            var line = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = EdgeBrush,
                StrokeThickness = message.Emphasized ? 2 : 1.6
            };
            if (message.Dotted)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 4 };
            }

            canvas.Children.Add(line);
            canvas.Children.Add(CreateArrowHead(start, end));

            var label = CreateEdgeLabel(message.Label, fontSize, foreground);
            Canvas.SetLeft(label, Math.Min(fromX, toX) + (Math.Abs(fromX - toX) / 2) - 44);
            Canvas.SetTop(label, y - 28);
            Canvas.SetZIndex(label, 3);
            canvas.Children.Add(label);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvas
        };
    }

    private static UIElement CreatePieSurface(
        MermaidPieDiagramDefinition pieChart,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        if (pieChart.Slices.Count == 0)
        {
            return new TextBlock
            {
                Text = "No pie slices were available to render.",
                Foreground = MetaForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var total = pieChart.Slices.Sum(static slice => Math.Max(slice.Value, 0));
        if (total <= 0)
        {
            total = pieChart.Slices.Count;
        }

        const double chartDiameter = 200;
        const double radius = chartDiameter / 2;
        var chartCanvas = new Canvas
        {
            Width = chartDiameter,
            Height = chartDiameter
        };

        var startAngle = -90d;
        for (var index = 0; index < pieChart.Slices.Count; index++)
        {
            var slice = pieChart.Slices[index];
            var sweep = (Math.Max(slice.Value, 0) / total) * 360d;
            if (sweep <= 0)
            {
                continue;
            }

            chartCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Fill = AccentPalette[index % AccentPalette.Length],
                Stroke = Brush("#FFFFFF"),
                StrokeThickness = 1.4,
                Data = CreatePieSliceGeometry(new Point(radius, radius), radius - 2, startAngle, sweep)
            });
            startAngle += sweep;
        }

        chartCanvas.Children.Add(new Ellipse
        {
            Width = chartDiameter,
            Height = chartDiameter,
            Stroke = NodeBorder,
            StrokeThickness = 1.1
        });

        var legend = new StackPanel
        {
            Spacing = 8
        };
        for (var index = 0; index < pieChart.Slices.Count; index++)
        {
            var slice = pieChart.Slices[index];
            var percentage = total <= 0 ? 0 : (Math.Max(slice.Value, 0) / total) * 100d;
            legend.Children.Add(new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10,
                Children =
                {
                    new Border
                    {
                        Width = 14,
                        Height = 14,
                        Background = AccentPalette[index % AccentPalette.Length],
                        CornerRadius = new CornerRadius(7),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = pieChart.ShowData
                            ? $"{slice.Label} • {slice.Value.ToString("0.##", CultureInfo.InvariantCulture)} ({percentage.ToString("0.#", CultureInfo.InvariantCulture)}%)"
                            : $"{slice.Label} • {percentage.ToString("0.#", CultureInfo.InvariantCulture)}%",
                        Foreground = foreground ?? EdgeBrush,
                        TextWrapping = TextWrapping.Wrap
                    }.Also(static text => Grid.SetColumn(text, 1))
                }
            });
        }

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 18,
            Children =
            {
                chartCanvas,
                legend.Also(static panel => Grid.SetColumn(panel, 1))
            }
        };
    }

    private static UIElement CreateJourneySurface(
        MermaidJourneyDiagramDefinition journey,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        var stack = new StackPanel
        {
            Spacing = 12
        };

        for (var sectionIndex = 0; sectionIndex < journey.Sections.Count; sectionIndex++)
        {
            var section = journey.Sections[sectionIndex];
            var sectionBrush = AccentPalette[sectionIndex % AccentPalette.Length];
            stack.Children.Add(new Border
            {
                Background = SoftPalette[sectionIndex % SoftPalette.Length],
                BorderBrush = sectionBrush,
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
                            Text = section.Name,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = sectionBrush
                        },
                        CreateJourneyTasks(section.Tasks, fontSize, foreground, sectionBrush)
                    }
                }
            });
        }

        return stack;
    }

    private static UIElement CreateJourneyTasks(
        IReadOnlyList<MermaidJourneyTaskDefinition> tasks,
        double fontSize,
        Brush? foreground,
        Brush accentBrush)
    {
        var stack = new StackPanel
        {
            Spacing = 10
        };

        foreach (var task in tasks)
        {
            var actorsText = task.Actors.Count == 0 ? "No actors" : string.Join(", ", task.Actors);
            var scoreDots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };
            for (var index = 0; index < 5; index++)
            {
                scoreDots.Children.Add(new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = index < task.Score ? accentBrush : Brush("#FFFFFF"),
                    Stroke = accentBrush,
                    StrokeThickness = 1
                });
            }

            stack.Children.Add(new Border
            {
                BorderBrush = SurfaceBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 12,
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 4,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = task.Label,
                                    FontWeight = FontWeights.SemiBold,
                                    Foreground = foreground ?? EdgeBrush,
                                    TextWrapping = TextWrapping.Wrap
                                },
                                new TextBlock
                                {
                                    Text = actorsText,
                                    FontSize = Math.Max(fontSize - 1, 11),
                                    Foreground = MetaForeground,
                                    TextWrapping = TextWrapping.Wrap
                                }
                            }
                        },
                        scoreDots.Also(static panel => Grid.SetColumn(panel, 1))
                    }
                }
            });
        }

        return stack;
    }

    private static UIElement CreateTimelineSurface(
        MermaidTimelineDiagramDefinition timeline,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        var stack = new StackPanel
        {
            Spacing = 12
        };

        for (var sectionIndex = 0; sectionIndex < timeline.Sections.Count; sectionIndex++)
        {
            var section = timeline.Sections[sectionIndex];
            var accentBrush = AccentPalette[sectionIndex % AccentPalette.Length];
            stack.Children.Add(new TextBlock
            {
                Text = section.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = accentBrush
            });

            foreach (var entry in section.Entries)
            {
                var events = new StackPanel
                {
                    Spacing = 4
                };
                foreach (var eventText in entry.Events)
                {
                    events.Children.Add(new TextBlock
                    {
                        Text = $"• {eventText}",
                        Foreground = foreground ?? EdgeBrush,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                stack.Children.Add(new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(0.28, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star) }
                    },
                    ColumnSpacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = entry.Period,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = foreground ?? EdgeBrush,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Right
                        },
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Children =
                            {
                                new Ellipse
                                {
                                    Width = 10,
                                    Height = 10,
                                    Fill = accentBrush,
                                    Stroke = accentBrush
                                },
                                new Border
                                {
                                    Width = 2,
                                    Margin = new Thickness(4, 2, 4, 0),
                                    Background = Brush("#CBD5E1"),
                                    Height = Math.Max((entry.Events.Count * 18) + 24, 32)
                                }
                            }
                        }.Also(static marker => Grid.SetColumn(marker, 1)),
                        new Border
                        {
                            BorderBrush = SurfaceBorderBrush,
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(12),
                            Child = events
                        }.Also(static card => Grid.SetColumn(card, 2))
                    }
                });
            }
        }

        return stack;
    }

    private static UIElement CreateQuadrantSurface(
        MermaidQuadrantChartDiagramDefinition quadrantChart,
        double availableWidth,
        double fontSize,
        Brush? foreground)
    {
        var chartSize = Math.Clamp(availableWidth - 28, 280, 420);
        var canvas = new Canvas
        {
            Width = chartSize + 120,
            Height = chartSize + 64
        };

        var chartLeft = 84d;
        var chartTop = 12d;
        var half = chartSize / 2;
        var quadrantRects = new[]
        {
            new Rect(chartLeft, chartTop, half, half),
            new Rect(chartLeft + half, chartTop, half, half),
            new Rect(chartLeft, chartTop + half, half, half),
            new Rect(chartLeft + half, chartTop + half, half, half)
        };

        for (var index = 0; index < quadrantRects.Length; index++)
        {
            var rect = quadrantRects[index];
            canvas.Children.Add(new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Fill = SoftPalette[index % SoftPalette.Length]
            }.Also(element =>
            {
                Canvas.SetLeft(element, rect.X);
                Canvas.SetTop(element, rect.Y);
            }));
        }

        canvas.Children.Add(new Rectangle
        {
            Width = chartSize,
            Height = chartSize,
            Stroke = NodeBorder,
            StrokeThickness = 1.4
        }.Also(element =>
        {
            Canvas.SetLeft(element, chartLeft);
            Canvas.SetTop(element, chartTop);
        }));

        canvas.Children.Add(new Line
        {
            X1 = chartLeft + half,
            X2 = chartLeft + half,
            Y1 = chartTop,
            Y2 = chartTop + chartSize,
            Stroke = SequenceLifelineBrush,
            StrokeThickness = 1.1
        });
        canvas.Children.Add(new Line
        {
            X1 = chartLeft,
            X2 = chartLeft + chartSize,
            Y1 = chartTop + half,
            Y2 = chartTop + half,
            Stroke = SequenceLifelineBrush,
            StrokeThickness = 1.1
        });

        AddPositionedLabel(canvas, quadrantChart.XLeftLabel, new Point(chartLeft + 8, chartTop + chartSize + 10), fontSize - 1, foreground);
        AddPositionedLabel(canvas, quadrantChart.XRightLabel, new Point(chartLeft + half + 8, chartTop + chartSize + 10), fontSize - 1, foreground);
        AddPositionedLabel(canvas, quadrantChart.YTopLabel, new Point(8, chartTop + 8), fontSize - 1, foreground, width: 72);
        AddPositionedLabel(canvas, quadrantChart.YBottomLabel, new Point(8, chartTop + half + 8), fontSize - 1, foreground, width: 72);

        for (var index = 0; index < quadrantChart.QuadrantLabels.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(quadrantChart.QuadrantLabels[index]))
            {
                continue;
            }

            var rect = quadrantRects[index];
            AddPositionedLabel(canvas, quadrantChart.QuadrantLabels[index], new Point(rect.X + 10, rect.Y + 10), fontSize - 0.5, foreground, width: rect.Width - 20);
        }

        for (var index = 0; index < quadrantChart.Points.Count; index++)
        {
            var point = quadrantChart.Points[index];
            var centerX = chartLeft + (point.X * chartSize);
            var centerY = chartTop + ((1 - point.Y) * chartSize);
            var fill = ResolveBrush(point.FillColor, AccentPalette[index % AccentPalette.Length]);
            var stroke = ResolveBrush(point.StrokeColor, fill);
            canvas.Children.Add(new Ellipse
            {
                Width = point.Radius * 2,
                Height = point.Radius * 2,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = point.StrokeWidth
            }.Also(element =>
            {
                Canvas.SetLeft(element, centerX - point.Radius);
                Canvas.SetTop(element, centerY - point.Radius);
            }));

            AddPositionedLabel(canvas, point.Label, new Point(centerX + point.Radius + 4, centerY - 8), fontSize - 1, foreground, width: 140);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvas
        };
    }

    private static void AddPositionedLabel(
        Canvas canvas,
        string text,
        Point origin,
        double fontSize,
        Brush? foreground,
        double width = 140)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = width,
            TextWrapping = TextWrapping.Wrap,
            Foreground = foreground ?? EdgeBrush,
            FontSize = Math.Max(fontSize, 10)
        };
        Canvas.SetLeft(label, origin.X);
        Canvas.SetTop(label, origin.Y);
        Canvas.SetZIndex(label, 3);
        canvas.Children.Add(label);
    }

    private static FrameworkElement CreateGraphNodeElement(GraphNode node, double width, double height, double fontSize, Brush? foreground)
    {
        if (node.Details.Count > 0)
        {
            return new Border
            {
                Width = width,
                Height = height,
                Background = NodeFill,
                BorderBrush = NodeBorder,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Child = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto }
                    },
                    Children =
                    {
                        new Border
                        {
                            Background = AccentFill,
                            Padding = new Thickness(12, 10, 12, 10),
                            Child = new TextBlock
                            {
                                Text = node.Label,
                                FontWeight = FontWeights.SemiBold,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = foreground ?? EdgeBrush
                            }
                        },
                        CreateNodeDetailsPanel(node.Details, fontSize, foreground).Also(static panel => Grid.SetRow(panel, 1))
                    }
                }
            };
        }

        return node.Shape switch
        {
            MermaidNodeShape.Circle => CreateCircleNode(node.Label, width, height, fontSize, foreground),
            MermaidNodeShape.Diamond => CreateDiamondNode(node.Label, width, height, fontSize, foreground),
            MermaidNodeShape.Rectangle => CreateRectangleNode(node.Label, width, height, fontSize, foreground, rounded: false),
            _ => CreateRectangleNode(node.Label, width, height, fontSize, foreground, rounded: true)
        };
    }

    private static FrameworkElement CreateCircleNode(string label, double width, double height, double fontSize, Brush? foreground)
    {
        var size = Math.Max(Math.Min(width, height), 34);
        return new Grid
        {
            Width = size,
            Height = size,
            Children =
            {
                new Ellipse
                {
                    Fill = NodeFill,
                    Stroke = NodeBorder,
                    StrokeThickness = 1.6
                },
                new TextBlock
                {
                    Text = label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = Math.Max(fontSize - 2, 10),
                    Foreground = foreground ?? EdgeBrush
                }
            }
        };
    }

    private static FrameworkElement CreateDiamondNode(string label, double width, double height, double fontSize, Brush? foreground)
    {
        return new Grid
        {
            Width = width,
            Height = height,
            Children =
            {
                new Polygon
                {
                    Fill = NodeFill,
                    Stroke = NodeBorder,
                    StrokeThickness = 1.6,
                    Points =
                    {
                        new Point(width / 2, 0),
                        new Point(width, height / 2),
                        new Point(width / 2, height),
                        new Point(0, height / 2)
                    }
                },
                new TextBlock
                {
                    Text = label,
                    Margin = new Thickness(18, 8, 18, 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = foreground ?? EdgeBrush
                }
            }
        };
    }

    private static FrameworkElement CreateRectangleNode(string label, double width, double height, double fontSize, Brush? foreground, bool rounded)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = NodeFill,
            BorderBrush = NodeBorder,
            BorderThickness = new Thickness(1.6),
            CornerRadius = rounded ? new CornerRadius(Math.Min(height / 2, 24)) : new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new TextBlock
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = foreground ?? EdgeBrush
            }
        };
    }

    private static StackPanel CreateNodeDetailsPanel(IReadOnlyList<string> details, double fontSize, Brush? foreground)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(12, 10, 12, 10),
            Spacing = 4
        };

        foreach (var detail in details)
        {
            panel.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                FontSize = Math.Max(fontSize - 1, 11),
                Foreground = foreground ?? EdgeBrush
            });
        }

        return panel;
    }

    private static FrameworkElement CreateEdgeLabel(string text, double fontSize, Brush? foreground)
    {
        return new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = text,
                MaxWidth = 160,
                TextWrapping = TextWrapping.Wrap,
                FontSize = Math.Max(fontSize - 1, 11),
                Foreground = foreground ?? EdgeBrush
            }
        };
    }

    private static FrameworkElement CreateMarkerLabel(string text, double fontSize, Brush? foreground)
    {
        return new Border
        {
            Background = Brush("#FFFFFF"),
            Padding = new Thickness(2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = Math.Max(fontSize - 2, 10),
                Foreground = foreground ?? MetaForeground
            }
        };
    }

    private static Polygon CreateArrowHead(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            return new Polygon();
        }

        var ux = dx / length;
        var uy = dy / length;
        var nx = -uy;
        var ny = ux;
        const double size = 8;
        var baseX = end.X - (ux * size);
        var baseY = end.Y - (uy * size);

        return new Polygon
        {
            Fill = EdgeBrush,
            Points =
            {
                end,
                new Point(baseX + (nx * (size * 0.45)), baseY + (ny * (size * 0.45))),
                new Point(baseX - (nx * (size * 0.45)), baseY - (ny * (size * 0.45)))
            }
        };
    }

    private static (Point Start, Point End) ResolveConnectionPoints(Rect fromBounds, Rect toBounds, bool horizontal)
    {
        var fromCenter = new Point(fromBounds.X + (fromBounds.Width / 2), fromBounds.Y + (fromBounds.Height / 2));
        var toCenter = new Point(toBounds.X + (toBounds.Width / 2), toBounds.Y + (toBounds.Height / 2));

        if (horizontal)
        {
            return fromCenter.X <= toCenter.X
                ? (new Point(fromBounds.Right, fromCenter.Y), new Point(toBounds.X, toCenter.Y))
                : (new Point(fromBounds.X, fromCenter.Y), new Point(toBounds.Right, toCenter.Y));
        }

        return fromCenter.Y <= toCenter.Y
            ? (new Point(fromCenter.X, fromBounds.Bottom), new Point(toCenter.X, toBounds.Y))
            : (new Point(fromCenter.X, fromBounds.Y), new Point(toCenter.X, toBounds.Bottom));
    }

    private static Size EstimateNodeSize(GraphNode node, double primarySize, double fontSize)
    {
        if (node.Details.Count > 0)
        {
            var detailWidth = node.Details.Max(static detail => detail.Length);
            var titleWidth = node.Label.Length;
            var width = Math.Min(primarySize, Math.Max(180, (Math.Max(detailWidth, titleWidth) * 7.2) + 36));
            var height = Math.Max(84, 48 + (node.Details.Count * 22));
            return new Size(width, height);
        }

        if (node.Shape == MermaidNodeShape.Circle)
        {
            var diameter = Math.Clamp(primarySize * 0.22, 28, 38);
            return new Size(diameter, diameter);
        }

        var minimumWidth = node.Shape == MermaidNodeShape.Diamond ? 100 : 120;
        var widthEstimate = Math.Min(primarySize, Math.Max(minimumWidth, (node.Label.Length * 7.1) + 30));
        var heightEstimate = node.Shape == MermaidNodeShape.Diamond
            ? Math.Max(58, 42 + ((node.Label.Length / 14) * 16))
            : Math.Max(48, 38 + ((node.Label.Length / 18) * 14));
        return new Size(widthEstimate, heightEstimate);
    }

    private static Dictionary<string, int> ComputeNodeLevels(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var nodeIds = new HashSet<string>(nodes.Select(static node => node.Id), StringComparer.Ordinal);
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            outgoing[node.Id] = [];
            indegree[node.Id] = 0;
        }

        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.FromId) || !nodeIds.Contains(edge.ToId) || edge.FromId == edge.ToId)
            {
                continue;
            }

            outgoing[edge.FromId].Add(edge.ToId);
            indegree[edge.ToId]++;
        }

        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>(indegree.Where(static pair => pair.Value == 0).Select(static pair => pair.Key));
        if (queue.Count == 0)
        {
            foreach (var node in nodes)
            {
                levels[node.Id] = 0;
            }

            return levels;
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            levels.TryAdd(nodeId, 0);
            foreach (var target in outgoing[nodeId])
            {
                levels[target] = Math.Max(levels.TryGetValue(target, out var level) ? level : 0, levels[nodeId] + 1);
                indegree[target]--;
                if (indegree[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        var fallbackLevel = levels.Count == 0 ? 0 : levels.Values.Max();
        foreach (var node in nodes)
        {
            levels.TryAdd(node.Id, fallbackLevel);
        }

        return levels;
    }

    private static Geometry CreatePieSliceGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        var endAngle = startAngle + sweepAngle;
        var startPoint = PolarToPoint(center, radius, startAngle);
        var endPoint = PolarToPoint(center, radius, endAngle);
        var largeArc = sweepAngle > 180;

        var figure = new PathFigure
        {
            StartPoint = center,
            Segments =
            {
                new LineSegment { Point = startPoint },
                new ArcSegment
                {
                    Point = endPoint,
                    Size = new Size(radius, radius),
                    IsLargeArc = largeArc,
                    SweepDirection = SweepDirection.Clockwise
                },
                new LineSegment { Point = center }
            },
            IsClosed = true
        };

        return new PathGeometry
        {
            Figures =
            {
                figure
            }
        };
    }

    private static Point PolarToPoint(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(
            center.X + (Math.Cos(radians) * radius),
            center.Y + (Math.Sin(radians) * radius));
    }

    private static string FormatErAttribute(MermaidErAttributeDefinition attribute)
    {
        var keyPrefix = string.IsNullOrWhiteSpace(attribute.Key) ? string.Empty : $"{attribute.Key} ";
        var commentSuffix = string.IsNullOrWhiteSpace(attribute.Comment) ? string.Empty : $" \"{attribute.Comment}\"";
        return $"{keyPrefix}{attribute.Type} {attribute.Name}{commentSuffix}".Trim();
    }

    private static string ToErCardinalityLabel(string token)
    {
        return token switch
        {
            "||" => "1",
            "|o" or "o|" => "0..1",
            "}o" or "o{" => "0..*",
            "}|"
                or "|{" => "1..*",
            _ => token
        };
    }

    private static Brush ResolveBrush(string? hex, Brush fallback)
    {
        return string.IsNullOrWhiteSpace(hex) ? fallback : Brush(hex);
    }

    private static double ResolvePreferredWidth(double availableWidth)
    {
        return availableWidth > 0 ? Math.Max(availableWidth - 24, 320) : DefaultWidth;
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

    private readonly record struct GraphNode(
        string Id,
        string Label,
        MermaidNodeShape Shape,
        IReadOnlyList<string> Details);

    private readonly record struct GraphEdge(
        string FromId,
        string ToId,
        string? Label,
        bool Dotted,
        bool Directed,
        string? StartMarker = null,
        string? EndMarker = null);

    private readonly record struct GraphNodePlacement(GraphNode Node, Rect Bounds);
}
