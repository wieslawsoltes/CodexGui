using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Mermaid;

internal static class MermaidDiagramViewFactory
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush SurfaceBackground = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush HeaderBackground = new SolidColorBrush(Color.Parse("#EEF2FF"));
    private static readonly IBrush HeaderForeground = new SolidColorBrush(Color.Parse("#4338CA"));
    private static readonly IBrush MetaForeground = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush SourceBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush DiagramBackground = new SolidColorBrush(Color.Parse("#FDFEFF"));
    private const double DefaultDiagramWidth = 720;

    public static MermaidRenderedDiagram Create(string diagramSource, double availableWidth, MarkdownRenderContext renderContext)
    {
        var diagram = MermaidDiagramParser.Parse(diagramSource);
        return diagram is MermaidUnsupportedDiagramDefinition unsupported
            ? CreateUnsupportedSurface(unsupported, renderContext)
            : CreateSupportedSurface(diagram, availableWidth, renderContext);
    }

    private static MermaidRenderedDiagram CreateSupportedSurface(
        MermaidDiagramDefinition diagram,
        double availableWidth,
        MarkdownRenderContext renderContext)
    {
        var diagramControl = new MermaidDiagramControl(
            diagram,
            ResolvePreferredWidth(availableWidth, renderContext),
            renderContext.FontSize,
            renderContext.Foreground ?? Brushes.Black);

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        layout.Children.Add(CreateHeader(diagram.Title, diagram.Subtitle));

        var body = new Border
        {
            Background = DiagramBackground,
            Padding = new Thickness(12),
            Child = diagramControl
        };
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var root = new Border
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = layout
        };
        return new MermaidRenderedDiagram(
            root,
            request => CreateSupportedHitTestResult(request, diagramControl, body));
    }

    private static MermaidRenderedDiagram CreateUnsupportedSurface(MermaidUnsupportedDiagramDefinition diagram, MarkdownRenderContext renderContext)
    {
        var layout = new StackPanel
        {
            Spacing = 0
        };
        layout.Children.Add(CreateHeader("Mermaid diagram", diagram.Reason));

        layout.Children.Add(new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = diagram.Reason,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = renderContext.Foreground,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Native Mermaid preview currently supports flowcharts, sequence diagrams, state diagrams, class diagrams, pie charts, user journeys, timelines, quadrant charts, mind maps, and ER diagrams. The source remains visible below.",
                        Foreground = MetaForeground,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        });
        var sourcePreview = CreateSourcePreview(diagram.Source, renderContext);
        layout.Children.Add(sourcePreview);

        var root = new Border
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = layout
        };
        return new MermaidRenderedDiagram(
            root,
            request => CreateUnsupportedHitTestResult(request, sourcePreview));
    }

    private static Control CreateHeader(string title, string status)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            VerticalAlignment = VerticalAlignment.Center
        });

        var meta = new TextBlock
        {
            Text = status,
            Foreground = MetaForeground,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(meta, 1);
        grid.Children.Add(meta);

        return new Border
        {
            Background = HeaderBackground,
            Padding = new Thickness(16, 10),
            Child = grid
        };
    }

    private static Control CreateSourcePreview(string source, MarkdownRenderContext renderContext)
    {
        return new Border
        {
            Background = SourceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12),
            Child = new SelectableTextBlock
            {
                FontFamily = MonospaceFamily,
                FontSize = Math.Max(renderContext.FontSize - 1, 12),
                Foreground = renderContext.Foreground,
                TextWrapping = renderContext.TextWrapping == TextWrapping.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
                Text = source
            }
        };
    }

    private static double ResolvePreferredWidth(double availableWidth, MarkdownRenderContext renderContext)
    {
        return availableWidth > 0
            ? Math.Max(280, availableWidth - 24)
            : renderContext.AvailableWidth > 0
                ? Math.Max(280, renderContext.AvailableWidth - 24)
            : DefaultDiagramWidth;
    }

    private static MarkdownVisualHitTestResult CreateSupportedHitTestResult(
        MarkdownVisualHitTestRequest request,
        MermaidDiagramControl diagramControl,
        Border body)
    {
        if (request.Control.TranslatePoint(request.LocalPoint, diagramControl) is { } diagramPoint &&
            diagramControl.TryHitTestElement(diagramPoint, out var diagramRects))
        {
            return new MarkdownVisualHitTestResult
            {
                LocalHighlightRects = TranslateRectsToRoot(diagramControl, request.Control, diagramRects)
            };
        }

        return new MarkdownVisualHitTestResult
        {
            LocalHighlightRects = ResolveControlBounds(body, request.Control)
        };
    }

    private static MarkdownVisualHitTestResult CreateUnsupportedHitTestResult(
        MarkdownVisualHitTestRequest request,
        Control sourcePreview)
    {
        return new MarkdownVisualHitTestResult
        {
            LocalHighlightRects = ResolveControlBounds(sourcePreview, request.Control)
        };
    }

    private static IReadOnlyList<Rect> TranslateRectsToRoot(Control source, Control root, IReadOnlyList<Rect> localRects)
    {
        var translatedRects = new List<Rect>(localRects.Count);
        foreach (var rect in localRects)
        {
            if (source.TranslatePoint(rect.TopLeft, root) is not { } topLeft)
            {
                continue;
            }

            translatedRects.Add(new Rect(topLeft, rect.Size));
        }

        return translatedRects;
    }

    private static IReadOnlyList<Rect> ResolveControlBounds(Control control, Control root)
    {
        return TranslateRectsToRoot(control, root, [new Rect(control.Bounds.Size)]);
    }
}

internal sealed record MermaidRenderedDiagram(Control Control, MarkdownVisualHitTestHandler? HitTestHandler);

internal enum MermaidDiagramKind
{
    Unsupported,
    Flowchart,
    SequenceDiagram,
    StateDiagram,
    ClassDiagram,
    PieChart,
    UserJourney,
    Timeline,
    QuadrantChart,
    Mindmap,
    EntityRelationshipDiagram
}

internal abstract class MermaidDiagramDefinition
{
    protected MermaidDiagramDefinition(MermaidDiagramKind kind, string source, string title, string subtitle)
    {
        Kind = kind;
        Source = source;
        Title = title;
        Subtitle = subtitle;
    }

    public MermaidDiagramKind Kind { get; }

    public string Source { get; }

    public string Title { get; }

    public string Subtitle { get; }
}

internal sealed class MermaidUnsupportedDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidUnsupportedDiagramDefinition(string source, string reason)
        : base(MermaidDiagramKind.Unsupported, source, "Mermaid diagram", reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}

internal enum MermaidFlowDirection
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop
}

internal enum MermaidNodeShape
{
    Rounded,
    Rectangle,
    Diamond,
    Circle
}

internal sealed class MermaidFlowchartDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidFlowchartDiagramDefinition(
        string source,
        MermaidFlowDirection direction,
        IReadOnlyList<MermaidFlowNodeDefinition> nodes,
        IReadOnlyList<MermaidFlowEdgeDefinition> edges)
        : base(MermaidDiagramKind.Flowchart, source, "Mermaid flowchart", DirectionToSubtitle(direction))
    {
        Direction = direction;
        Nodes = nodes;
        Edges = edges;
    }

    public MermaidFlowDirection Direction { get; }

    public IReadOnlyList<MermaidFlowNodeDefinition> Nodes { get; }

    public IReadOnlyList<MermaidFlowEdgeDefinition> Edges { get; }

    private static string DirectionToSubtitle(MermaidFlowDirection direction)
    {
        return direction switch
        {
            MermaidFlowDirection.LeftToRight => "Rendered natively • left to right",
            MermaidFlowDirection.RightToLeft => "Rendered natively • right to left",
            MermaidFlowDirection.BottomToTop => "Rendered natively • bottom to top",
            _ => "Rendered natively • top to bottom"
        };
    }
}

internal sealed record MermaidFlowNodeDefinition(string Id, string Label, MermaidNodeShape Shape);

internal sealed record MermaidFlowEdgeDefinition(string FromId, string ToId, string? Label, bool Dotted);

internal sealed class MermaidSequenceDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidSequenceDiagramDefinition(
        string source,
        IReadOnlyList<MermaidSequenceParticipantDefinition> participants,
        IReadOnlyList<MermaidSequenceMessageDefinition> messages)
        : base(MermaidDiagramKind.SequenceDiagram, source, "Mermaid sequence diagram", "Rendered natively • sequence interactions")
    {
        Participants = participants;
        Messages = messages;
    }

    public IReadOnlyList<MermaidSequenceParticipantDefinition> Participants { get; }

    public IReadOnlyList<MermaidSequenceMessageDefinition> Messages { get; }
}

internal sealed record MermaidSequenceParticipantDefinition(string Id, string Label);

internal sealed record MermaidSequenceMessageDefinition(string FromId, string ToId, string Label, bool Dotted, bool Emphasized);

internal sealed class MermaidStateDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidStateDiagramDefinition(
        string source,
        MermaidFlowDirection direction,
        IReadOnlyList<MermaidStateNodeDefinition> states,
        IReadOnlyList<MermaidStateTransitionDefinition> transitions)
        : base(MermaidDiagramKind.StateDiagram, source, "Mermaid state diagram", DirectionToSubtitle(direction))
    {
        Direction = direction;
        States = states;
        Transitions = transitions;
    }

    public MermaidFlowDirection Direction { get; }

    public IReadOnlyList<MermaidStateNodeDefinition> States { get; }

    public IReadOnlyList<MermaidStateTransitionDefinition> Transitions { get; }

    private static string DirectionToSubtitle(MermaidFlowDirection direction)
    {
        return direction switch
        {
            MermaidFlowDirection.LeftToRight => "Rendered natively • state flow left to right",
            MermaidFlowDirection.RightToLeft => "Rendered natively • state flow right to left",
            MermaidFlowDirection.BottomToTop => "Rendered natively • state flow bottom to top",
            _ => "Rendered natively • state flow top to bottom"
        };
    }
}

internal sealed record MermaidStateNodeDefinition(string Id, string Label, MermaidNodeShape Shape);

internal sealed record MermaidStateTransitionDefinition(string FromId, string ToId, string? Label, bool Dotted);

internal sealed class MermaidClassDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidClassDiagramDefinition(
        string source,
        MermaidFlowDirection direction,
        IReadOnlyList<MermaidClassNodeDefinition> classes,
        IReadOnlyList<MermaidClassRelationDefinition> relations)
        : base(MermaidDiagramKind.ClassDiagram, source, "Mermaid class diagram", DirectionToSubtitle(direction))
    {
        Direction = direction;
        Classes = classes;
        Relations = relations;
    }

    public MermaidFlowDirection Direction { get; }

    public IReadOnlyList<MermaidClassNodeDefinition> Classes { get; }

    public IReadOnlyList<MermaidClassRelationDefinition> Relations { get; }

    private static string DirectionToSubtitle(MermaidFlowDirection direction)
    {
        return direction switch
        {
            MermaidFlowDirection.TopToBottom => "Rendered natively • class relationships top to bottom",
            MermaidFlowDirection.BottomToTop => "Rendered natively • class relationships bottom to top",
            MermaidFlowDirection.RightToLeft => "Rendered natively • class relationships right to left",
            _ => "Rendered natively • class relationships left to right"
        };
    }
}

internal sealed record MermaidClassNodeDefinition(string Id, string Label, IReadOnlyList<string> Members);

internal sealed record MermaidClassRelationDefinition(string FromId, string ToId, string? Label, bool Dotted, bool Directed);

internal sealed class MermaidPieDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidPieDiagramDefinition(
        string source,
        string? chartTitle,
        bool showData,
        IReadOnlyList<MermaidPieSliceDefinition> slices)
        : base(
            MermaidDiagramKind.PieChart,
            source,
            string.IsNullOrWhiteSpace(chartTitle) ? "Mermaid pie chart" : chartTitle,
            showData ? "Rendered natively • pie chart with values" : "Rendered natively • pie chart")
    {
        ChartTitle = chartTitle;
        ShowData = showData;
        Slices = slices;
    }

    public string? ChartTitle { get; }

    public bool ShowData { get; }

    public IReadOnlyList<MermaidPieSliceDefinition> Slices { get; }
}

internal sealed record MermaidPieSliceDefinition(string Label, double Value);

internal sealed class MermaidJourneyDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidJourneyDiagramDefinition(
        string source,
        string? chartTitle,
        IReadOnlyList<MermaidJourneySectionDefinition> sections)
        : base(
            MermaidDiagramKind.UserJourney,
            source,
            string.IsNullOrWhiteSpace(chartTitle) ? "Mermaid user journey" : chartTitle,
            $"Rendered natively • {sections.Sum(static section => section.Tasks.Count)} tasks across {sections.Count} sections")
    {
        ChartTitle = chartTitle;
        Sections = sections;
    }

    public string? ChartTitle { get; }

    public IReadOnlyList<MermaidJourneySectionDefinition> Sections { get; }
}

internal sealed record MermaidJourneySectionDefinition(string Name, IReadOnlyList<MermaidJourneyTaskDefinition> Tasks);

internal sealed record MermaidJourneyTaskDefinition(string Label, int Score, IReadOnlyList<string> Actors);

internal sealed class MermaidTimelineDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidTimelineDiagramDefinition(
        string source,
        string? chartTitle,
        IReadOnlyList<MermaidTimelineSectionDefinition> sections)
        : base(
            MermaidDiagramKind.Timeline,
            source,
            string.IsNullOrWhiteSpace(chartTitle) ? "Mermaid timeline" : chartTitle,
            $"Rendered natively • {sections.Sum(static section => section.Entries.Count)} periods across {sections.Count} sections")
    {
        ChartTitle = chartTitle;
        Sections = sections;
    }

    public string? ChartTitle { get; }

    public IReadOnlyList<MermaidTimelineSectionDefinition> Sections { get; }
}

internal sealed record MermaidTimelineSectionDefinition(string Name, IReadOnlyList<MermaidTimelineEntryDefinition> Entries);

internal sealed record MermaidTimelineEntryDefinition(string Period, IReadOnlyList<string> Events);

internal sealed class MermaidQuadrantChartDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidQuadrantChartDiagramDefinition(
        string source,
        string? chartTitle,
        string xLeftLabel,
        string xRightLabel,
        string yBottomLabel,
        string yTopLabel,
        IReadOnlyList<string> quadrantLabels,
        IReadOnlyList<MermaidQuadrantPointDefinition> points)
        : base(
            MermaidDiagramKind.QuadrantChart,
            source,
            string.IsNullOrWhiteSpace(chartTitle) ? "Mermaid quadrant chart" : chartTitle,
            points.Count == 0
                ? "Rendered natively • strategic quadrant chart"
                : $"Rendered natively • strategic quadrant chart with {points.Count} points")
    {
        ChartTitle = chartTitle;
        XLeftLabel = xLeftLabel;
        XRightLabel = xRightLabel;
        YBottomLabel = yBottomLabel;
        YTopLabel = yTopLabel;
        QuadrantLabels = quadrantLabels;
        Points = points;
    }

    public string? ChartTitle { get; }

    public string XLeftLabel { get; }

    public string XRightLabel { get; }

    public string YBottomLabel { get; }

    public string YTopLabel { get; }

    public IReadOnlyList<string> QuadrantLabels { get; }

    public IReadOnlyList<MermaidQuadrantPointDefinition> Points { get; }
}

internal sealed record MermaidQuadrantPointDefinition(
    string Label,
    double X,
    double Y,
    double Radius,
    double StrokeWidth,
    Color? FillColor,
    Color? StrokeColor);

internal sealed class MermaidMindmapDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidMindmapDiagramDefinition(
        string source,
        MermaidMindmapNodeDefinition root,
        int nodeCount)
        : base(
            MermaidDiagramKind.Mindmap,
            source,
            "Mermaid mind map",
            $"Rendered natively • {nodeCount} nodes rooted at {root.Label}")
    {
        Root = root;
        NodeCount = nodeCount;
    }

    public MermaidMindmapNodeDefinition Root { get; }

    public int NodeCount { get; }
}

internal sealed record MermaidMindmapNodeDefinition(
    string Id,
    string Label,
    MermaidNodeShape Shape,
    IReadOnlyList<MermaidMindmapNodeDefinition> Children);

internal sealed class MermaidErDiagramDefinition : MermaidDiagramDefinition
{
    public MermaidErDiagramDefinition(
        string source,
        MermaidFlowDirection direction,
        IReadOnlyList<MermaidErEntityDefinition> entities,
        IReadOnlyList<MermaidErRelationshipDefinition> relationships)
        : base(
            MermaidDiagramKind.EntityRelationshipDiagram,
            source,
            "Mermaid ER diagram",
            DirectionToSubtitle(direction, entities.Count, relationships.Count))
    {
        Direction = direction;
        Entities = entities;
        Relationships = relationships;
    }

    public MermaidFlowDirection Direction { get; }

    public IReadOnlyList<MermaidErEntityDefinition> Entities { get; }

    public IReadOnlyList<MermaidErRelationshipDefinition> Relationships { get; }

    private static string DirectionToSubtitle(MermaidFlowDirection direction, int entityCount, int relationshipCount)
    {
        var directionText = direction switch
        {
            MermaidFlowDirection.LeftToRight => "left to right",
            MermaidFlowDirection.RightToLeft => "right to left",
            MermaidFlowDirection.BottomToTop => "bottom to top",
            _ => "top to bottom"
        };

        return $"Rendered natively • {entityCount} entities and {relationshipCount} relationships • {directionText}";
    }
}

internal sealed record MermaidErEntityDefinition(string Id, string Label, IReadOnlyList<MermaidErAttributeDefinition> Attributes);

internal sealed record MermaidErAttributeDefinition(string Type, string Name, string? Key, string? Comment);

internal sealed record MermaidErRelationshipDefinition(
    string FromId,
    string ToId,
    string FromCardinality,
    string ToCardinality,
    string? Label,
    bool Identifying);

internal static class MermaidStandardPreprocessor
{
    public static IReadOnlyList<string> Preprocess(string normalizedSource)
    {
        var rawLines = normalizedSource.Split('\n', StringSplitOptions.None);
        var result = new List<string>(rawLines.Length);
        var index = 0;

        while (index < rawLines.Length && string.IsNullOrWhiteSpace(rawLines[index]))
        {
            index++;
        }

        if (index < rawLines.Length && string.Equals(rawLines[index].Trim(), "---", StringComparison.Ordinal))
        {
            index++;
            while (index < rawLines.Length && !string.Equals(rawLines[index].Trim(), "---", StringComparison.Ordinal))
            {
                index++;
            }

            if (index < rawLines.Length)
            {
                index++;
            }
        }

        var inDirective = false;
        for (; index < rawLines.Length; index++)
        {
            var rawLine = rawLines[index];
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (inDirective)
            {
                if (trimmed.Contains("}%%", StringComparison.Ordinal))
                {
                    inDirective = false;
                }

                continue;
            }

            if (trimmed.StartsWith("%%{", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("}%%", StringComparison.Ordinal))
                {
                    inDirective = true;
                }

                continue;
            }

            var withoutComment = StripInlineComment(rawLine);
            if (string.IsNullOrWhiteSpace(withoutComment))
            {
                continue;
            }

            result.Add(withoutComment.TrimEnd());
        }

        return result;
    }

    private static string StripInlineComment(string line)
    {
        var commentIndex = line.IndexOf("%%", StringComparison.Ordinal);
        return commentIndex >= 0 ? line[..commentIndex].TrimEnd() : line;
    }
}

internal static class MermaidDiagramParser
{
    private static readonly MermaidClassRelationPattern[] ClassRelationPatterns =
    [
        new("<|..", Reverse: true, Dotted: true, Directed: true),
        new("..|>", Reverse: false, Dotted: true, Directed: true),
        new("<|--", Reverse: true, Dotted: false, Directed: true),
        new("--|>", Reverse: false, Dotted: false, Directed: true),
        new("*..", Reverse: false, Dotted: true, Directed: false),
        new("..*", Reverse: true, Dotted: true, Directed: false),
        new("o..", Reverse: false, Dotted: true, Directed: false),
        new("..o", Reverse: true, Dotted: true, Directed: false),
        new("*--", Reverse: false, Dotted: false, Directed: false),
        new("--*", Reverse: true, Dotted: false, Directed: false),
        new("o--", Reverse: false, Dotted: false, Directed: false),
        new("--o", Reverse: true, Dotted: false, Directed: false),
        new("<..", Reverse: true, Dotted: true, Directed: true),
        new("..>", Reverse: false, Dotted: true, Directed: true),
        new("<--", Reverse: true, Dotted: false, Directed: true),
        new("-->", Reverse: false, Dotted: false, Directed: true),
        new("..", Reverse: false, Dotted: true, Directed: false),
        new("--", Reverse: false, Dotted: false, Directed: false)
    ];

    public static MermaidDiagramDefinition Parse(string source)
    {
        var normalized = MermaidSyntax.NormalizeCode(source);
        var lines = MermaidStandardPreprocessor.Preprocess(normalized);

        if (lines.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(normalized, "The Mermaid source is empty.");
        }

        var header = lines[0].Trim();
        if (header.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
            header.StartsWith("graph", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFlowchart(normalized, lines);
        }

        if (header.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSequenceDiagram(normalized, lines);
        }

        if (header.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return ParseStateDiagram(normalized, lines);
        }

        if (header.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return ParseClassDiagram(normalized, lines);
        }

        if (header.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
        {
            return ParsePieChart(normalized, lines);
        }

        if (header.StartsWith("journey", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUserJourney(normalized, lines);
        }

        if (header.StartsWith("timeline", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTimeline(normalized, lines);
        }

        if (header.StartsWith("quadrantChart", StringComparison.OrdinalIgnoreCase))
        {
            return ParseQuadrantChart(normalized, lines);
        }

        if (header.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMindmap(normalized, lines);
        }

        if (header.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return ParseErDiagram(normalized, lines);
        }

        return new MermaidUnsupportedDiagramDefinition(normalized, $"Unsupported Mermaid syntax: {header}.");
    }

    private static MermaidDiagramDefinition ParseFlowchart(string source, IReadOnlyList<string> lines)
    {
        var direction = ParseFlowDirection(lines[0]);
        var nodeBuilders = new Dictionary<string, MermaidFlowNodeBuilder>(StringComparer.Ordinal);
        var nodeOrder = new List<string>();
        var edges = new List<MermaidFlowEdgeDefinition>();

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var lineSpan = line.AsSpan();
            if (TryParseFlowEdgeChain(lineSpan, nodeBuilders, nodeOrder, edges))
            {
                continue;
            }

            var standaloneIndex = 0;
            if (TryParseFlowNode(lineSpan, ref standaloneIndex, out var node) &&
                ConsumeRemainingWhitespace(lineSpan, standaloneIndex))
            {
                RegisterNode(nodeBuilders, nodeOrder, node);
            }
        }

        if (nodeOrder.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No flowchart nodes could be parsed from the Mermaid source.");
        }

        var nodes = nodeOrder
            .Select(id => nodeBuilders[id].Build())
            .ToList();

        return new MermaidFlowchartDiagramDefinition(source, direction, nodes, edges);
    }

    private static MermaidDiagramDefinition ParseSequenceDiagram(string source, IReadOnlyList<string> lines)
    {
        var participants = new Dictionary<string, MermaidSequenceParticipantDefinition>(StringComparer.Ordinal);
        var participantOrder = new List<string>();
        var messages = new List<MermaidSequenceMessageDefinition>();

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseParticipant(line, out var participant))
            {
                RegisterParticipant(participants, participantOrder, participant);
                continue;
            }

            if (TryParseMessage(line, out var message))
            {
                RegisterParticipant(participants, participantOrder, new MermaidSequenceParticipantDefinition(message.FromId, message.FromId));
                RegisterParticipant(participants, participantOrder, new MermaidSequenceParticipantDefinition(message.ToId, message.ToId));
                messages.Add(message);
            }
        }

        if (participantOrder.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No sequence participants could be parsed from the Mermaid source.");
        }

        if (messages.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No sequence messages could be parsed from the Mermaid source.");
        }

        var orderedParticipants = participantOrder
            .Select(id => participants[id])
            .ToList();

        return new MermaidSequenceDiagramDefinition(source, orderedParticipants, messages);
    }

    private static MermaidDiagramDefinition ParseStateDiagram(string source, IReadOnlyList<string> lines)
    {
        var direction = MermaidFlowDirection.TopToBottom;
        var stateBuilders = new Dictionary<string, MermaidStateNodeBuilder>(StringComparer.Ordinal);
        var stateOrder = new List<string>();
        var transitions = new List<MermaidStateTransitionDefinition>();

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line) || line is "{" or "}")
            {
                continue;
            }

            if (TryParseDiagramDirection(line, out var parsedDirection))
            {
                direction = parsedDirection;
                continue;
            }

            if (TryParseStateTransition(line, out var transition, out var fromState, out var toState))
            {
                RegisterStateNode(stateBuilders, stateOrder, fromState);
                RegisterStateNode(stateBuilders, stateOrder, toState);
                transitions.Add(transition);
                continue;
            }

            if (TryParseStateDeclaration(line, out var state))
            {
                RegisterStateNode(stateBuilders, stateOrder, state);
            }
        }

        if (stateOrder.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No state nodes could be parsed from the Mermaid source.");
        }

        if (transitions.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No state transitions could be parsed from the Mermaid source.");
        }

        var states = stateOrder
            .Select(id => stateBuilders[id].Build())
            .ToList();

        return new MermaidStateDiagramDefinition(source, direction, states, transitions);
    }

    private static MermaidDiagramDefinition ParseClassDiagram(string source, IReadOnlyList<string> lines)
    {
        var direction = MermaidFlowDirection.LeftToRight;
        var classBuilders = new Dictionary<string, MermaidClassNodeBuilder>(StringComparer.Ordinal);
        var classOrder = new List<string>();
        var relations = new List<MermaidClassRelationDefinition>();
        string? activeClassId = null;

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseDiagramDirection(line, out var parsedDirection))
            {
                direction = parsedDirection;
                continue;
            }

            if (activeClassId is not null)
            {
                if (line == "}")
                {
                    activeClassId = null;
                    continue;
                }

                if (TryParseClassBodyMember(line, out var bodyMember))
                {
                    RegisterClassMember(classBuilders, classOrder, activeClassId, bodyMember);
                    continue;
                }
            }

            if (TryParseClassDeclaration(line, out var classId, out var classLabel, out var opensBody))
            {
                RegisterClass(classBuilders, classOrder, classId, classLabel);
                activeClassId = opensBody ? classId : null;
                continue;
            }

            if (line == "}")
            {
                activeClassId = null;
                continue;
            }

            if (TryParseClassMemberDeclaration(line, out var memberClassId, out var member))
            {
                RegisterClassMember(classBuilders, classOrder, memberClassId, member);
                continue;
            }

            if (TryParseClassRelation(line, out var relation))
            {
                RegisterClass(classBuilders, classOrder, relation.FromId, relation.FromId);
                RegisterClass(classBuilders, classOrder, relation.ToId, relation.ToId);
                relations.Add(relation);
            }
        }

        if (classOrder.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No classes could be parsed from the Mermaid source.");
        }

        if (relations.Count == 0 && classBuilders.Values.All(builder => builder.Members.Count == 0))
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No class relationships or members could be parsed from the Mermaid source.");
        }

        var classes = classOrder
            .Select(id => classBuilders[id].Build())
            .ToList();

        return new MermaidClassDiagramDefinition(source, direction, classes, relations);
    }

    private static MermaidDiagramDefinition ParsePieChart(string source, IReadOnlyList<string> lines)
    {
        var showData = lines[0].Trim().Contains("showData", StringComparison.OrdinalIgnoreCase);
        string? chartTitle = null;
        var slices = new List<MermaidPieSliceDefinition>();

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                chartTitle = CleanLabel(line["title ".Length..].Trim());
                continue;
            }

            if (TryParsePieSlice(line, out var slice))
            {
                slices.Add(slice);
            }
        }

        return slices.Count == 0
            ? new MermaidUnsupportedDiagramDefinition(source, "No pie chart slices could be parsed from the Mermaid source.")
            : new MermaidPieDiagramDefinition(source, chartTitle, showData, slices);
    }

    private static MermaidDiagramDefinition ParseUserJourney(string source, IReadOnlyList<string> lines)
    {
        string? chartTitle = null;
        var sections = new List<MermaidJourneySectionDefinition>();
        var currentSectionName = "Journey";
        var currentTasks = new List<MermaidJourneyTaskDefinition>();
        var sectionDeclared = false;

        void CommitSection()
        {
            if (currentTasks.Count == 0 && !sectionDeclared)
            {
                return;
            }

            sections.Add(new MermaidJourneySectionDefinition(currentSectionName, currentTasks.ToList()));
            currentTasks.Clear();
            sectionDeclared = false;
        }

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                chartTitle = CleanLabel(line["title ".Length..].Trim());
                continue;
            }

            if (line.StartsWith("section ", StringComparison.OrdinalIgnoreCase))
            {
                CommitSection();
                currentSectionName = CleanLabel(line["section ".Length..].Trim());
                sectionDeclared = true;
                continue;
            }

            if (TryParseJourneyTask(line, out var task))
            {
                currentTasks.Add(task);
            }
        }

        CommitSection();

        return sections.Count == 0 || sections.All(static section => section.Tasks.Count == 0)
            ? new MermaidUnsupportedDiagramDefinition(source, "No journey tasks could be parsed from the Mermaid source.")
            : new MermaidJourneyDiagramDefinition(source, chartTitle, sections);
    }

    private static MermaidDiagramDefinition ParseTimeline(string source, IReadOnlyList<string> lines)
    {
        string? chartTitle = null;
        var sections = new List<MermaidTimelineSectionDefinition>();
        var currentSectionName = "Timeline";
        var currentEntries = new List<MermaidTimelineEntryDefinition>();
        string? activePeriod = null;
        List<string>? activeEvents = null;
        var sectionDeclared = false;

        void CommitEntry()
        {
            if (activePeriod is null || activeEvents is null || activeEvents.Count == 0)
            {
                activePeriod = null;
                activeEvents = null;
                return;
            }

            currentEntries.Add(new MermaidTimelineEntryDefinition(activePeriod, activeEvents.ToList()));
            activePeriod = null;
            activeEvents = null;
        }

        void CommitSection()
        {
            CommitEntry();
            if (currentEntries.Count == 0 && !sectionDeclared)
            {
                return;
            }

            sections.Add(new MermaidTimelineSectionDefinition(currentSectionName, currentEntries.ToList()));
            currentEntries.Clear();
            sectionDeclared = false;
        }

        for (var index = 1; index < lines.Count; index++)
        {
            var rawLine = lines[index];
            var trimmedLine = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            if (trimmedLine.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                chartTitle = CleanLabel(trimmedLine["title ".Length..].Trim());
                continue;
            }

            if (trimmedLine.StartsWith("section ", StringComparison.OrdinalIgnoreCase))
            {
                CommitSection();
                currentSectionName = CleanLabel(trimmedLine["section ".Length..].Trim());
                sectionDeclared = true;
                continue;
            }

            if (!TryParseTimelineEntry(rawLine, out var period, out var events, out var continuation))
            {
                continue;
            }

            if (continuation)
            {
                if (activeEvents is not null)
                {
                    activeEvents.AddRange(events);
                }

                continue;
            }

            CommitEntry();
            activePeriod = period;
            activeEvents = new List<string>(events);
        }

        CommitSection();

        return sections.Count == 0 || sections.All(static section => section.Entries.Count == 0)
            ? new MermaidUnsupportedDiagramDefinition(source, "No timeline periods could be parsed from the Mermaid source.")
            : new MermaidTimelineDiagramDefinition(source, chartTitle, sections);
    }

    private static MermaidDiagramDefinition ParseQuadrantChart(string source, IReadOnlyList<string> lines)
    {
        string? chartTitle = null;
        var xLeftLabel = "Low";
        var xRightLabel = "High";
        var yBottomLabel = "Low";
        var yTopLabel = "High";
        var quadrantLabels = new string[4];
        var points = new List<MermaidQuadrantPointDefinition>();

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                chartTitle = CleanLabel(line["title ".Length..].Trim());
                continue;
            }

            if (line.StartsWith("x-axis ", StringComparison.OrdinalIgnoreCase))
            {
                ParseAxisDescriptor(line["x-axis ".Length..], out xLeftLabel, out xRightLabel);
                continue;
            }

            if (line.StartsWith("y-axis ", StringComparison.OrdinalIgnoreCase))
            {
                ParseAxisDescriptor(line["y-axis ".Length..], out yBottomLabel, out yTopLabel);
                continue;
            }

            if (TryParseQuadrantLabel(line, out var quadrantIndex, out var quadrantLabel))
            {
                quadrantLabels[quadrantIndex] = quadrantLabel;
                continue;
            }

            if (TryParseQuadrantPoint(line, out var point))
            {
                points.Add(point);
            }
        }

        var hasQuadrantMetadata = quadrantLabels.Any(static label => !string.IsNullOrWhiteSpace(label));
        return !hasQuadrantMetadata && points.Count == 0 && string.IsNullOrWhiteSpace(chartTitle)
            ? new MermaidUnsupportedDiagramDefinition(source, "No quadrant chart labels or points could be parsed from the Mermaid source.")
            : new MermaidQuadrantChartDiagramDefinition(
                source,
                chartTitle,
                xLeftLabel,
                xRightLabel,
                yBottomLabel,
                yTopLabel,
                quadrantLabels,
                points);
    }

    private static MermaidDiagramDefinition ParseMindmap(string source, IReadOnlyList<string> lines)
    {
        MermaidMindmapNodeBuilder? root = null;
        var stack = new Stack<MermaidMindmapNodeContext>();
        var nextNodeIndex = 0;

        for (var index = 1; index < lines.Count; index++)
        {
            var rawLine = lines[index];
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var trimmedLine = rawLine.Trim();
            if (trimmedLine.Length == 0)
            {
                continue;
            }

            var indent = CountLeadingIndent(rawLine);
            var node = CreateMindmapNode(trimmedLine, nextNodeIndex++);
            if (root is null)
            {
                root = node;
                stack.Push(new MermaidMindmapNodeContext(indent, node));
                continue;
            }

            while (stack.Count > 0 && indent <= stack.Peek().Indent)
            {
                stack.Pop();
            }

            var parent = stack.Count > 0 ? stack.Peek().Node : root;
            parent.Children.Add(node);
            stack.Push(new MermaidMindmapNodeContext(indent, node));
        }

        return root is null
            ? new MermaidUnsupportedDiagramDefinition(source, "No mind map nodes could be parsed from the Mermaid source.")
            : new MermaidMindmapDiagramDefinition(source, root.Build(), CountMindmapNodes(root));
    }

    private static MermaidDiagramDefinition ParseErDiagram(string source, IReadOnlyList<string> lines)
    {
        var direction = MermaidFlowDirection.TopToBottom;
        var entityBuilders = new Dictionary<string, MermaidErEntityBuilder>(StringComparer.Ordinal);
        var entityOrder = new List<string>();
        var relationships = new List<MermaidErRelationshipDefinition>();
        MermaidErEntityBuilder? activeEntity = null;

        for (var index = 1; index < lines.Count; index++)
        {
            var line = StripComment(lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseDiagramDirection(line, out var parsedDirection))
            {
                direction = parsedDirection;
                continue;
            }

            if (activeEntity is not null)
            {
                if (line == "}")
                {
                    activeEntity = null;
                    continue;
                }

                if (TryParseErAttribute(line, out var attribute))
                {
                    activeEntity.Attributes.Add(attribute);
                    continue;
                }
            }

            if (TryParseErRelationship(line, out var relationship))
            {
                RegisterErEntity(entityBuilders, entityOrder, relationship.FromId, relationship.FromId);
                RegisterErEntity(entityBuilders, entityOrder, relationship.ToId, relationship.ToId);
                relationships.Add(relationship);
                continue;
            }

            if (!TryParseErEntityDeclaration(line, out var entityId, out var entityLabel, out var opensBody))
            {
                continue;
            }

            activeEntity = RegisterErEntity(entityBuilders, entityOrder, entityId, entityLabel);
            if (!opensBody)
            {
                activeEntity = null;
            }
        }

        if (entityOrder.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(source, "No ER entities could be parsed from the Mermaid source.");
        }

        var entities = entityOrder
            .Select(id => entityBuilders[id].Build())
            .ToList();

        return new MermaidErDiagramDefinition(source, direction, entities, relationships);
    }

    private static bool TryParseDiagramDirection(string line, out MermaidFlowDirection direction)
    {
        direction = MermaidFlowDirection.TopToBottom;
        if (!line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        direction = ParseFlowDirection(line);
        return true;
    }

    private static MermaidFlowDirection ParseFlowDirection(string header)
    {
        var tokens = header.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return MermaidFlowDirection.TopToBottom;
        }

        return tokens[^1].ToUpperInvariant() switch
        {
            "LR" => MermaidFlowDirection.LeftToRight,
            "RL" => MermaidFlowDirection.RightToLeft,
            "BT" => MermaidFlowDirection.BottomToTop,
            _ => MermaidFlowDirection.TopToBottom
        };
    }

    private static bool TryParseStateDeclaration(string line, out MermaidStateNodeDefinition state)
    {
        state = new MermaidStateNodeDefinition(string.Empty, string.Empty, MermaidNodeShape.Rounded);
        if (!line.StartsWith("state ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var descriptor = line["state ".Length..].Trim();
        if (descriptor.Length == 0)
        {
            return false;
        }

        descriptor = descriptor.TrimEnd('{').Trim();
        if (descriptor.Length == 0 || descriptor == "[*]")
        {
            return false;
        }

        var shape = descriptor.Contains("<<choice>>", StringComparison.OrdinalIgnoreCase)
            ? MermaidNodeShape.Diamond
            : MermaidNodeShape.Rounded;
        descriptor = descriptor.Replace("<<choice>>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        state = ParseStateReference(descriptor, isSource: false, shapeOverride: shape);
        return !string.IsNullOrWhiteSpace(state.Id);
    }

    private static bool TryParseStateTransition(
        string line,
        out MermaidStateTransitionDefinition transition,
        out MermaidStateNodeDefinition fromState,
        out MermaidStateNodeDefinition toState)
    {
        transition = new MermaidStateTransitionDefinition(string.Empty, string.Empty, null, Dotted: false);
        fromState = new MermaidStateNodeDefinition(string.Empty, string.Empty, MermaidNodeShape.Rounded);
        toState = new MermaidStateNodeDefinition(string.Empty, string.Empty, MermaidNodeShape.Rounded);

        var colonIndex = line.IndexOf(':');
        var relation = colonIndex >= 0 ? line[..colonIndex].Trim() : line.Trim();
        var label = colonIndex >= 0 ? CleanLabel(line[(colonIndex + 1)..].Trim()) : null;
        if (relation.Length == 0)
        {
            return false;
        }

        foreach (var arrow in new[] { "..>", "-->", "->" })
        {
            var arrowIndex = relation.IndexOf(arrow, StringComparison.Ordinal);
            if (arrowIndex < 0)
            {
                continue;
            }

            var fromToken = relation[..arrowIndex].Trim();
            var toToken = relation[(arrowIndex + arrow.Length)..].Trim();
            if (fromToken.Length == 0 || toToken.Length == 0)
            {
                return false;
            }

            fromState = ParseStateReference(fromToken, isSource: true);
            toState = ParseStateReference(toToken, isSource: false);
            transition = new MermaidStateTransitionDefinition(fromState.Id, toState.Id, label, Dotted: arrow.Contains("..", StringComparison.Ordinal));
            return true;
        }

        return false;
    }

    private static MermaidStateNodeDefinition ParseStateReference(
        string token,
        bool isSource,
        MermaidNodeShape? shapeOverride = null)
    {
        var descriptor = token.Trim();
        if (descriptor == "[*]")
        {
            return isSource
                ? new MermaidStateNodeDefinition("__state_start", string.Empty, MermaidNodeShape.Circle)
                : new MermaidStateNodeDefinition("__state_end", string.Empty, MermaidNodeShape.Circle);
        }

        if (descriptor.StartsWith("state ", StringComparison.OrdinalIgnoreCase))
        {
            descriptor = descriptor["state ".Length..].Trim();
        }

        descriptor = descriptor.TrimEnd('{').Trim();
        var id = descriptor;
        var label = descriptor;

        var asIndex = descriptor.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        if (asIndex >= 0)
        {
            var left = descriptor[..asIndex].Trim();
            var right = descriptor[(asIndex + 4)..].Trim();
            if (left.StartsWith('"') && left.EndsWith('"'))
            {
                id = right;
                label = CleanLabel(left);
            }
            else
            {
                id = left;
                label = CleanLabel(right);
            }
        }

        label = CleanLabel(label);
        id = CleanDiagramIdentifier(id);
        if (label.Length == 0)
        {
            label = id;
        }

        return new MermaidStateNodeDefinition(id, label, shapeOverride ?? MermaidNodeShape.Rounded);
    }

    private static void RegisterStateNode(
        Dictionary<string, MermaidStateNodeBuilder> stateBuilders,
        List<string> stateOrder,
        MermaidStateNodeDefinition state)
    {
        if (!stateBuilders.TryGetValue(state.Id, out var builder))
        {
            builder = new MermaidStateNodeBuilder(state.Id, state.Label, state.Shape);
            stateBuilders.Add(state.Id, builder);
            stateOrder.Add(state.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.Label))
        {
            builder.Label = state.Label;
        }

        builder.Shape = state.Shape;
    }

    private static bool TryParseClassDeclaration(string line, out string classId, out string classLabel, out bool opensBody)
    {
        classId = string.Empty;
        classLabel = string.Empty;
        opensBody = false;

        if (!line.StartsWith("class ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var descriptor = line["class ".Length..].Trim();
        if (descriptor.Length == 0)
        {
            return false;
        }

        opensBody = descriptor.EndsWith("{", StringComparison.Ordinal);
        descriptor = descriptor.TrimEnd('{').Trim();
        descriptor = StripDiagramClassifier(descriptor);
        if (descriptor.Length == 0)
        {
            return false;
        }

        var bracketIndex = descriptor.IndexOf('[');
        if (bracketIndex > 0 && descriptor.EndsWith("]", StringComparison.Ordinal))
        {
            classId = CleanDiagramIdentifier(descriptor[..bracketIndex]);
            classLabel = CleanLabel(descriptor[(bracketIndex + 1)..^1]);
            return classId.Length > 0;
        }

        classId = CleanDiagramIdentifier(descriptor);
        classLabel = classId;
        return classId.Length > 0;
    }

    private static bool TryParseClassBodyMember(string line, out string member)
    {
        member = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed == "}")
        {
            return false;
        }

        member = trimmed;
        return true;
    }

    private static bool TryParseClassMemberDeclaration(string line, out string classId, out string member)
    {
        classId = string.Empty;
        member = string.Empty;

        if (ContainsClassRelation(line))
        {
            return false;
        }

        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= line.Length - 1)
        {
            return false;
        }

        classId = CleanDiagramIdentifier(line[..colonIndex]);
        member = line[(colonIndex + 1)..].Trim();
        return classId.Length > 0 && member.Length > 0;
    }

    private static bool TryParseClassRelation(string line, out MermaidClassRelationDefinition relation)
    {
        relation = new MermaidClassRelationDefinition(string.Empty, string.Empty, null, Dotted: false, Directed: false);
        var colonIndex = line.IndexOf(':');
        var relationPart = colonIndex >= 0 ? line[..colonIndex].Trim() : line.Trim();
        var label = colonIndex >= 0 ? CleanLabel(line[(colonIndex + 1)..].Trim()) : null;
        if (relationPart.Length == 0)
        {
            return false;
        }

        foreach (var pattern in ClassRelationPatterns)
        {
            var operatorIndex = relationPart.IndexOf(pattern.Token, StringComparison.Ordinal);
            if (operatorIndex < 0)
            {
                continue;
            }

            var left = CleanDiagramIdentifier(relationPart[..operatorIndex]);
            var right = CleanDiagramIdentifier(relationPart[(operatorIndex + pattern.Token.Length)..]);
            if (left.Length == 0 || right.Length == 0)
            {
                return false;
            }

            relation = pattern.Reverse
                ? new MermaidClassRelationDefinition(right, left, label, pattern.Dotted, pattern.Directed)
                : new MermaidClassRelationDefinition(left, right, label, pattern.Dotted, pattern.Directed);
            return true;
        }

        return false;
    }

    private static void RegisterClass(
        Dictionary<string, MermaidClassNodeBuilder> classBuilders,
        List<string> classOrder,
        string classId,
        string classLabel)
    {
        if (!classBuilders.TryGetValue(classId, out var builder))
        {
            builder = new MermaidClassNodeBuilder(classId, classLabel);
            classBuilders.Add(classId, builder);
            classOrder.Add(classId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(classLabel) &&
            string.Equals(builder.Label, builder.Id, StringComparison.Ordinal))
        {
            builder.Label = classLabel;
        }
    }

    private static void RegisterClassMember(
        Dictionary<string, MermaidClassNodeBuilder> classBuilders,
        List<string> classOrder,
        string classId,
        string member)
    {
        RegisterClass(classBuilders, classOrder, classId, classId);
        classBuilders[classId].AddMember(member);
    }

    private static bool TryParsePieSlice(string line, out MermaidPieSliceDefinition slice)
    {
        slice = new MermaidPieSliceDefinition(string.Empty, 0);
        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= line.Length - 1)
        {
            return false;
        }

        var label = CleanLabel(line[..colonIndex].Trim());
        var valueText = line[(colonIndex + 1)..].Trim();
        if (label.Length == 0 ||
            !double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            return false;
        }

        slice = new MermaidPieSliceDefinition(label, value);
        return true;
    }

    private static bool TryParseJourneyTask(string line, out MermaidJourneyTaskDefinition task)
    {
        task = new MermaidJourneyTaskDefinition(string.Empty, 0, []);
        if (!TrySplitColonTriplet(line, out var label, out var scoreText, out var actorText))
        {
            return false;
        }

        if (!int.TryParse(scoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) || score is < 1 or > 5)
        {
            return false;
        }

        var actors = actorText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static actor => actor.Length > 0)
            .ToList();

        task = new MermaidJourneyTaskDefinition(CleanLabel(label), score, actors);
        return task.Label.Length > 0;
    }

    private static bool TryParseTimelineEntry(
        string line,
        out string period,
        out IReadOnlyList<string> events,
        out bool continuation)
    {
        period = string.Empty;
        events = Array.Empty<string>();
        continuation = false;

        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith(':'))
        {
            var eventText = trimmed[1..].Trim();
            if (eventText.Length == 0)
            {
                return false;
            }

            continuation = true;
            events = eventText
                .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static entry => entry.Length > 0)
                .Select(CleanLabel)
                .ToList();
            return events.Count > 0;
        }

        var parts = trimmed
            .Split(':', StringSplitOptions.TrimEntries)
            .Where(static part => part.Length > 0)
            .ToArray();
        if (parts.Length < 2)
        {
            return false;
        }

        period = CleanLabel(parts[0]);
        events = parts
            .Skip(1)
            .Select(CleanLabel)
            .Where(static entry => entry.Length > 0)
            .ToList();
        return period.Length > 0 && events.Count > 0;
    }

    private static void ParseAxisDescriptor(string descriptor, out string firstLabel, out string secondLabel)
    {
        var arrowIndex = descriptor.IndexOf("-->", StringComparison.Ordinal);
        if (arrowIndex < 0)
        {
            firstLabel = CleanLabel(descriptor.Trim());
            secondLabel = string.Empty;
            return;
        }

        firstLabel = CleanLabel(descriptor[..arrowIndex].Trim());
        secondLabel = CleanLabel(descriptor[(arrowIndex + 3)..].Trim());
    }

    private static bool TryParseQuadrantLabel(string line, out int quadrantIndex, out string label)
    {
        quadrantIndex = -1;
        label = string.Empty;
        if (!line.StartsWith("quadrant-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = line.IndexOf(' ');
        if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(line["quadrant-".Length..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex) ||
            parsedIndex is < 1 or > 4)
        {
            return false;
        }

        quadrantIndex = parsedIndex - 1;
        label = CleanLabel(line[(separatorIndex + 1)..].Trim());
        return label.Length > 0;
    }

    private static bool TryParseQuadrantPoint(string line, out MermaidQuadrantPointDefinition point)
    {
        point = new MermaidQuadrantPointDefinition(string.Empty, 0, 0, 6, 1.5, null, null);
        var bracketStart = line.IndexOf('[');
        var bracketEnd = line.IndexOf(']', bracketStart >= 0 ? bracketStart + 1 : 0);
        if (bracketStart <= 0 || bracketEnd <= bracketStart)
        {
            return false;
        }

        var label = StripDiagramClassifier(line[..bracketStart].Trim().TrimEnd(':').Trim());
        label = CleanLabel(label);
        if (label.Length == 0)
        {
            return false;
        }

        var coordinates = line[(bracketStart + 1)..bracketEnd]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (coordinates.Length != 2 ||
            !double.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            x < 0 || x > 1 || y < 0 || y > 1)
        {
            return false;
        }

        var radius = 6d;
        var strokeWidth = 1.5d;
        Color? fillColor = null;
        Color? strokeColor = null;
        var styleText = line[(bracketEnd + 1)..].Trim();
        if (styleText.Length > 0)
        {
            var styleParts = styleText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var stylePart in styleParts)
            {
                var separatorIndex = stylePart.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex >= stylePart.Length - 1)
                {
                    continue;
                }

                var key = stylePart[..separatorIndex].Trim();
                var value = stylePart[(separatorIndex + 1)..].Trim();
                switch (key.ToLowerInvariant())
                {
                    case "radius" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius):
                        radius = Math.Clamp(parsedRadius, 3, 24);
                        break;
                    case "stroke-width" when TryParseDimension(value, out var parsedStrokeWidth):
                        strokeWidth = Math.Clamp(parsedStrokeWidth, 0.5, 8);
                        break;
                    case "color" when TryParseHexColor(value, out var parsedFill):
                        fillColor = parsedFill;
                        break;
                    case "stroke-color" when TryParseHexColor(value, out var parsedStroke):
                        strokeColor = parsedStroke;
                        break;
                }
            }
        }

        point = new MermaidQuadrantPointDefinition(label, x, y, radius, strokeWidth, fillColor, strokeColor);
        return true;
    }

    private static MermaidMindmapNodeBuilder CreateMindmapNode(string line, int nodeIndex)
    {
        var descriptor = StripMindmapMetadata(line);
        var descriptorSpan = descriptor.AsSpan();
        var index = 0;
        if (TryParseFlowNode(descriptorSpan, ref index, out var parsedNode) &&
            ConsumeRemainingWhitespace(descriptorSpan, index))
        {
            return new MermaidMindmapNodeBuilder($"mindmap-{nodeIndex}-{parsedNode.Id}", parsedNode.Label, parsedNode.Shape);
        }

        var label = CleanLabel(descriptor);
        return new MermaidMindmapNodeBuilder($"mindmap-{nodeIndex}", label.Length == 0 ? $"Node {nodeIndex + 1}" : label, MermaidNodeShape.Rounded);
    }

    private static string StripMindmapMetadata(string line)
    {
        var withoutClasses = StripDiagramClassifier(line);
        var iconIndex = withoutClasses.IndexOf("::icon(", StringComparison.OrdinalIgnoreCase);
        return (iconIndex >= 0 ? withoutClasses[..iconIndex] : withoutClasses).Trim();
    }

    private static int CountMindmapNodes(MermaidMindmapNodeBuilder builder)
    {
        var count = 1;
        foreach (var child in builder.Children)
        {
            count += CountMindmapNodes(child);
        }

        return count;
    }

    private static bool TryParseErEntityDeclaration(
        string line,
        out string entityId,
        out string entityLabel,
        out bool opensBody)
    {
        entityId = string.Empty;
        entityLabel = string.Empty;
        opensBody = false;

        var descriptor = StripDiagramClassifier(line).Trim();
        if (descriptor.Length == 0 || ContainsErRelationshipToken(descriptor))
        {
            return false;
        }

        opensBody = descriptor.EndsWith("{", StringComparison.Ordinal);
        descriptor = descriptor.TrimEnd('{').TrimEnd();
        return TryParseErEntityDescriptor(descriptor, out entityId, out entityLabel);
    }

    private static bool TryParseErEntityDescriptor(string descriptor, out string entityId, out string entityLabel)
    {
        entityId = string.Empty;
        entityLabel = string.Empty;

        var normalized = StripDiagramClassifier(descriptor).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        string? alias = null;
        if (normalized.EndsWith("]", StringComparison.Ordinal))
        {
            var aliasStart = normalized.LastIndexOf('[');
            if (aliasStart > 0)
            {
                alias = CleanLabel(normalized[(aliasStart + 1)..^1].Trim());
                normalized = normalized[..aliasStart].TrimEnd();
            }
        }

        if (normalized.Length > 0 && normalized[0] == '"')
        {
            if (!TryReadErEntityToken(normalized, 0, out entityId, out var endIndex))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(normalized[endIndex..]))
            {
                return false;
            }
        }
        else
        {
            entityId = normalized.Trim().Trim('"');
        }

        if (entityId.Length == 0)
        {
            return false;
        }

        entityLabel = !string.IsNullOrWhiteSpace(alias) ? alias : entityId;
        return true;
    }

    private static bool TryParseErAttribute(string line, out MermaidErAttributeDefinition attribute)
    {
        attribute = new MermaidErAttributeDefinition(string.Empty, string.Empty, null, null);
        var normalized = line.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        string? comment = null;
        var quoteStart = normalized.IndexOf('"');
        if (quoteStart >= 0 && normalized.Length > 0 && normalized[^1] == '"' && quoteStart < normalized.Length - 1)
        {
            comment = normalized[(quoteStart + 1)..^1];
            normalized = normalized[..quoteStart].TrimEnd();
        }

        var parts = normalized.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var type = parts[0];
        var name = parts[1];
        var key = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : null;
        attribute = new MermaidErAttributeDefinition(type, name, key, string.IsNullOrWhiteSpace(comment) ? null : comment);
        return true;
    }

    private static bool TryParseErRelationship(string line, out MermaidErRelationshipDefinition relationship)
    {
        relationship = new MermaidErRelationshipDefinition(string.Empty, string.Empty, string.Empty, string.Empty, null, Identifying: true);
        var colonIndex = line.IndexOf(':');
        var relationPart = colonIndex >= 0 ? line[..colonIndex].Trim() : line.Trim();
        var label = colonIndex >= 0 ? CleanLabel(line[(colonIndex + 1)..].Trim()) : null;
        if (relationPart.Length == 0 ||
            !TryReadErEntityToken(relationPart, 0, out var fromEntityId, out var afterLeftToken) ||
            !TryReadErEntityTokenFromEnd(relationPart, out var toEntityId, out var beforeRightToken))
        {
            return false;
        }

        var descriptorLength = beforeRightToken - afterLeftToken;
        if (descriptorLength <= 0)
        {
            return false;
        }

        var descriptor = relationPart.Substring(afterLeftToken, descriptorLength).Trim();
        if (!TryParseErRelationshipDescriptor(descriptor, out var fromCardinality, out var toCardinality, out var identifying))
        {
            return false;
        }

        relationship = new MermaidErRelationshipDefinition(
            fromEntityId,
            toEntityId,
            fromCardinality,
            toCardinality,
            label,
            identifying);
        return true;
    }

    private static bool TryParseErRelationshipDescriptor(
        string descriptor,
        out string fromCardinality,
        out string toCardinality,
        out bool identifying)
    {
        fromCardinality = string.Empty;
        toCardinality = string.Empty;
        identifying = true;

        var normalized = descriptor.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        string leftToken;
        string rightToken;
        var solidIndex = normalized.IndexOf("--", StringComparison.Ordinal);
        if (solidIndex >= 0)
        {
            leftToken = normalized[..solidIndex].Trim();
            rightToken = normalized[(solidIndex + 2)..].Trim();
            identifying = true;
        }
        else
        {
            var dottedIndex = normalized.IndexOf("..", StringComparison.Ordinal);
            if (dottedIndex >= 0)
            {
                leftToken = normalized[..dottedIndex].Trim();
                rightToken = normalized[(dottedIndex + 2)..].Trim();
                identifying = false;
            }
            else
            {
                var optionallyToIndex = normalized.IndexOf(" optionally to ", StringComparison.OrdinalIgnoreCase);
                if (optionallyToIndex >= 0)
                {
                    leftToken = normalized[..optionallyToIndex].Trim();
                    rightToken = normalized[(optionallyToIndex + " optionally to ".Length)..].Trim();
                    identifying = false;
                }
                else
                {
                    var toIndex = normalized.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
                    if (toIndex < 0)
                    {
                        return false;
                    }

                    leftToken = normalized[..toIndex].Trim();
                    rightToken = normalized[(toIndex + " to ".Length)..].Trim();
                    identifying = true;
                }
            }
        }

        return TryNormalizeErCardinality(leftToken, leftSide: true, out fromCardinality) &&
               TryNormalizeErCardinality(rightToken, leftSide: false, out toCardinality);
    }

    private static bool TryNormalizeErCardinality(string value, bool leftSide, out string token)
    {
        var normalized = NormalizeErAliasToken(value);
        token = normalized switch
        {
            "|o" or "o|" or "oneorzero" or "zeroorone" => leftSide ? "|o" : "o|",
            "||" or "onlyone" or "1" => "||",
            "}o" or "o{" or "zeroormore" or "zeroormany" or "many(0)" or "0+" => leftSide ? "}o" : "o{",
            "}|" or "|{" or "oneormore" or "oneormany" or "many(1)" or "1+" => leftSide ? "}|" : "|{",
            _ => string.Empty
        };
        return token.Length > 0;
    }

    private static string NormalizeErAliasToken(string value)
    {
        return new string(value
            .Where(static ch => !char.IsWhiteSpace(ch) && ch != '-')
            .ToArray())
            .Trim()
            .Trim('"')
            .ToLowerInvariant();
    }

    private static bool TryReadErEntityToken(string value, int startIndex, out string token, out int endIndex)
    {
        token = string.Empty;
        endIndex = startIndex;

        var index = startIndex;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        if (index >= value.Length)
        {
            return false;
        }

        if (value[index] == '"')
        {
            var closingQuote = value.IndexOf('"', index + 1);
            if (closingQuote < 0)
            {
                return false;
            }

            token = value[(index + 1)..closingQuote];
            endIndex = closingQuote + 1;
            return token.Length > 0;
        }

        var start = index;
        while (index < value.Length && !char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        token = value[start..index].Trim().Trim('"');
        endIndex = index;
        return token.Length > 0;
    }

    private static bool TryReadErEntityTokenFromEnd(string value, out string token, out int startIndex)
    {
        token = string.Empty;
        startIndex = value.Length;

        var index = value.Length - 1;
        while (index >= 0 && char.IsWhiteSpace(value[index]))
        {
            index--;
        }

        if (index < 0)
        {
            return false;
        }

        if (value[index] == '"')
        {
            var openingQuote = value.LastIndexOf('"', index - 1);
            if (openingQuote < 0)
            {
                return false;
            }

            token = value[(openingQuote + 1)..index];
            startIndex = openingQuote;
            return token.Length > 0;
        }

        var end = index;
        while (index >= 0 && !char.IsWhiteSpace(value[index]))
        {
            index--;
        }

        startIndex = index + 1;
        token = value[startIndex..(end + 1)].Trim().Trim('"');
        return token.Length > 0;
    }

    private static bool ContainsErRelationshipToken(string value)
    {
        return value.Contains("--", StringComparison.Ordinal) ||
               value.Contains("..", StringComparison.Ordinal) ||
               value.Contains(" to ", StringComparison.OrdinalIgnoreCase);
    }

    private static MermaidErEntityBuilder RegisterErEntity(
        Dictionary<string, MermaidErEntityBuilder> entityBuilders,
        List<string> entityOrder,
        string entityId,
        string entityLabel)
    {
        if (!entityBuilders.TryGetValue(entityId, out var builder))
        {
            builder = new MermaidErEntityBuilder(entityId, entityLabel);
            entityBuilders.Add(entityId, builder);
            entityOrder.Add(entityId);
            return builder;
        }

        if (!string.IsNullOrWhiteSpace(entityLabel) &&
            string.Equals(builder.Label, builder.Id, StringComparison.Ordinal))
        {
            builder.Label = entityLabel;
        }

        return builder;
    }

    private static bool TrySplitColonTriplet(string line, out string first, out string second, out string third)
    {
        first = string.Empty;
        second = string.Empty;
        third = string.Empty;

        var firstSeparator = line.IndexOf(':');
        if (firstSeparator <= 0 || firstSeparator >= line.Length - 1)
        {
            return false;
        }

        var secondSeparator = line.IndexOf(':', firstSeparator + 1);
        if (secondSeparator <= firstSeparator || secondSeparator >= line.Length - 1)
        {
            return false;
        }

        first = line[..firstSeparator].Trim();
        second = line[(firstSeparator + 1)..secondSeparator].Trim();
        third = line[(secondSeparator + 1)..].Trim();
        return first.Length > 0 && second.Length > 0 && third.Length > 0;
    }

    private static bool TryParseDimension(string value, out double dimension)
    {
        var normalized = value.Trim();
        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2].TrimEnd();
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out dimension);
    }

    private static bool TryParseHexColor(string value, out Color color)
    {
        color = default;
        var normalized = value.Trim();
        if (!normalized.StartsWith('#') || (normalized.Length != 7 && normalized.Length != 9))
        {
            return false;
        }

        if (!uint.TryParse(normalized[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        color = normalized.Length == 7
            ? Color.FromRgb((byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed)
            : Color.FromArgb((byte)(parsed >> 24), (byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed);
        return true;
    }

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf("%%", StringComparison.Ordinal);
        var content = commentIndex >= 0 ? line[..commentIndex] : line;
        return content.Trim();
    }

    private static bool TryParseFlowEdgeChain(
        ReadOnlySpan<char> line,
        Dictionary<string, MermaidFlowNodeBuilder> nodeBuilders,
        List<string> nodeOrder,
        List<MermaidFlowEdgeDefinition> edges)
    {
        var index = 0;
        if (!TryParseFlowNode(line, ref index, out var currentNode))
        {
            return false;
        }

        var hadEdge = false;
        RegisterNode(nodeBuilders, nodeOrder, currentNode);

        while (true)
        {
            SkipWhitespace(line, ref index);
            if (index >= line.Length)
            {
                break;
            }

            if (!TryParseFlowConnector(line, ref index, out var edgeLabel, out var dotted))
            {
                return false;
            }

            SkipWhitespace(line, ref index);
            if (!TryParseFlowNode(line, ref index, out var nextNode))
            {
                return false;
            }

            RegisterNode(nodeBuilders, nodeOrder, nextNode);
            edges.Add(new MermaidFlowEdgeDefinition(currentNode.Id, nextNode.Id, edgeLabel, dotted));
            currentNode = nextNode;
            hadEdge = true;
        }

        return hadEdge;
    }

    private static bool TryParseFlowNode(ReadOnlySpan<char> line, ref int index, out MermaidParsedFlowNode node)
    {
        SkipWhitespace(line, ref index);
        node = default;
        if (index >= line.Length)
        {
            return false;
        }

        var idStart = index;
        while (index < line.Length && IsNodeIdentifierChar(line[index]))
        {
            index++;
        }

        if (index == idStart)
        {
            return false;
        }

        var id = line[idStart..index].ToString();
        var label = id;
        var shape = MermaidNodeShape.Rounded;
        var hasExplicitLabel = false;

        if (index < line.Length)
        {
            if (line[index] == '[' && TryParseDelimited(line, ref index, '[', ']', out var rectangleLabel))
            {
                label = rectangleLabel;
                shape = MermaidNodeShape.Rectangle;
                hasExplicitLabel = true;
            }
            else if (line[index] == '{' && TryParseDelimited(line, ref index, '{', '}', out var diamondLabel))
            {
                label = diamondLabel;
                shape = MermaidNodeShape.Diamond;
                hasExplicitLabel = true;
            }
            else if (line[index] == '(' && index + 1 < line.Length && line[index + 1] == '(' && TryParseDoubleParen(line, ref index, out var circleLabel))
            {
                label = circleLabel;
                shape = MermaidNodeShape.Circle;
                hasExplicitLabel = true;
            }
            else if (line[index] == '(' && TryParseDelimited(line, ref index, '(', ')', out var roundedLabel))
            {
                label = roundedLabel;
                shape = MermaidNodeShape.Rounded;
                hasExplicitLabel = true;
            }
        }

        node = new MermaidParsedFlowNode(id, CleanLabel(label), shape, hasExplicitLabel);
        return true;
    }

    private static bool TryParseDelimited(ReadOnlySpan<char> line, ref int index, char open, char close, out string content)
    {
        content = string.Empty;
        if (index >= line.Length || line[index] != open)
        {
            return false;
        }

        index++;
        var start = index;
        while (index < line.Length && line[index] != close)
        {
            index++;
        }

        if (index >= line.Length)
        {
            return false;
        }

        content = line[start..index].ToString();
        index++;
        return true;
    }

    private static bool TryParseDoubleParen(ReadOnlySpan<char> line, ref int index, out string content)
    {
        content = string.Empty;
        if (index + 1 >= line.Length || line[index] != '(' || line[index + 1] != '(')
        {
            return false;
        }

        index += 2;
        var start = index;
        while (index + 1 < line.Length && !(line[index] == ')' && line[index + 1] == ')'))
        {
            index++;
        }

        if (index + 1 >= line.Length)
        {
            return false;
        }

        content = line[start..index].ToString();
        index += 2;
        return true;
    }

    private static bool TryParseFlowConnector(ReadOnlySpan<char> line, ref int index, out string? label, out bool dotted)
    {
        label = null;
        dotted = false;
        SkipWhitespace(line, ref index);

        var sawArrowHead = false;
        var sawConnectorContent = false;

        while (index < line.Length)
        {
            var ch = line[index];
            if (ch == '|')
            {
                index++;
                var labelStart = index;
                while (index < line.Length && line[index] != '|')
                {
                    index++;
                }

                label = line[labelStart..Math.Min(index, line.Length)].ToString().Trim();
                if (index < line.Length && line[index] == '|')
                {
                    index++;
                }

                sawConnectorContent = true;
                continue;
            }

            if (ch == '>')
            {
                sawArrowHead = true;
                index++;
                break;
            }

            if (ch is '-' or '=' or '.')
            {
                dotted |= ch == '.';
                sawConnectorContent = true;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            break;
        }

        return sawArrowHead && sawConnectorContent;
    }

    private static void RegisterNode(
        Dictionary<string, MermaidFlowNodeBuilder> nodeBuilders,
        List<string> nodeOrder,
        MermaidParsedFlowNode parsedNode)
    {
        if (!nodeBuilders.TryGetValue(parsedNode.Id, out var builder))
        {
            builder = new MermaidFlowNodeBuilder(parsedNode.Id, parsedNode.Label, parsedNode.Shape);
            nodeBuilders.Add(parsedNode.Id, builder);
            nodeOrder.Add(parsedNode.Id);
            return;
        }

        if (parsedNode.HasExplicitLabel)
        {
            builder.Label = parsedNode.Label;
            builder.Shape = parsedNode.Shape;
        }
    }

    private static bool ConsumeRemainingWhitespace(ReadOnlySpan<char> line, int index)
    {
        while (index < line.Length)
        {
            if (!char.IsWhiteSpace(line[index]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool TryParseParticipant(string line, out MermaidSequenceParticipantDefinition participant)
    {
        participant = new MermaidSequenceParticipantDefinition(string.Empty, string.Empty);

        var trimmed = line.Trim();
        var prefix = trimmed.StartsWith("participant ", StringComparison.OrdinalIgnoreCase)
            ? "participant "
            : trimmed.StartsWith("actor ", StringComparison.OrdinalIgnoreCase)
                ? "actor "
                : null;
        if (prefix is null)
        {
            return false;
        }

        var descriptor = trimmed[prefix.Length..].Trim();
        if (descriptor.Length == 0)
        {
            return false;
        }

        var asIndex = descriptor.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        if (asIndex >= 0)
        {
            var id = descriptor[..asIndex].Trim();
            var label = descriptor[(asIndex + 4)..].Trim();
            if (id.Length == 0 || label.Length == 0)
            {
                return false;
            }

            participant = new MermaidSequenceParticipantDefinition(id, CleanLabel(label));
            return true;
        }

        participant = new MermaidSequenceParticipantDefinition(descriptor, CleanLabel(descriptor));
        return true;
    }

    private static bool TryParseMessage(string line, out MermaidSequenceMessageDefinition message)
    {
        message = new MermaidSequenceMessageDefinition(string.Empty, string.Empty, string.Empty, Dotted: false, Emphasized: false);
        var colonIndex = line.IndexOf(':');
        if (colonIndex < 0)
        {
            return false;
        }

        var relation = line[..colonIndex].Trim();
        var label = CleanLabel(line[(colonIndex + 1)..].Trim());
        if (relation.Length == 0 || label.Length == 0)
        {
            return false;
        }

        foreach (var arrow in new[] { "-->>", "->>", "-->", "->" })
        {
            var arrowIndex = relation.IndexOf(arrow, StringComparison.Ordinal);
            if (arrowIndex < 0)
            {
                continue;
            }

            var from = relation[..arrowIndex].Trim();
            var to = relation[(arrowIndex + arrow.Length)..].Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                return false;
            }

            message = new MermaidSequenceMessageDefinition(
                from,
                to,
                label,
                Dotted: arrow.Contains("--", StringComparison.Ordinal),
                Emphasized: arrow.EndsWith(">>", StringComparison.Ordinal));
            return true;
        }

        return false;
    }

    private static void RegisterParticipant(
        Dictionary<string, MermaidSequenceParticipantDefinition> participants,
        List<string> participantOrder,
        MermaidSequenceParticipantDefinition participant)
    {
        if (participants.ContainsKey(participant.Id))
        {
            return;
        }

        participants.Add(participant.Id, participant);
        participantOrder.Add(participant.Id);
    }

    private static void SkipWhitespace(ReadOnlySpan<char> line, ref int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }
    }

    private static bool IsNodeIdentifierChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.';
    }

    private static string CleanLabel(string value)
    {
        return value.Trim().Trim('"');
    }

    private static string CleanDiagramIdentifier(string value)
    {
        var cleaned = StripDiagramClassifier(value.Trim());
        if (cleaned.StartsWith("class ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["class ".Length..].Trim();
        }

        if (cleaned.StartsWith("state ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["state ".Length..].Trim();
        }

        cleaned = cleaned.Trim().Trim('"');
        if (cleaned.EndsWith("{", StringComparison.Ordinal))
        {
            cleaned = cleaned[..^1].TrimEnd();
        }

        var bracketIndex = cleaned.IndexOf('[');
        if (bracketIndex > 0)
        {
            cleaned = cleaned[..bracketIndex].TrimEnd();
        }

        return cleaned;
    }

    private static string StripDiagramClassifier(string value)
    {
        var classifierIndex = value.IndexOf(":::", StringComparison.Ordinal);
        return classifierIndex >= 0 ? value[..classifierIndex].TrimEnd() : value;
    }

    private static int CountLeadingIndent(string line)
    {
        var indent = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                indent++;
                continue;
            }

            if (ch == '\t')
            {
                indent += 4;
                continue;
            }

            break;
        }

        return indent;
    }

    private static bool ContainsClassRelation(string line)
    {
        return ClassRelationPatterns.Any(pattern => line.Contains(pattern.Token, StringComparison.Ordinal));
    }

    private sealed class MermaidFlowNodeBuilder
    {
        public MermaidFlowNodeBuilder(string id, string label, MermaidNodeShape shape)
        {
            Id = id;
            Label = label;
            Shape = shape;
        }

        public string Id { get; }

        public string Label { get; set; }

        public MermaidNodeShape Shape { get; set; }

        public MermaidFlowNodeDefinition Build() => new(Id, Label, Shape);
    }

    private sealed class MermaidStateNodeBuilder
    {
        public MermaidStateNodeBuilder(string id, string label, MermaidNodeShape shape)
        {
            Id = id;
            Label = label;
            Shape = shape;
        }

        public string Id { get; }

        public string Label { get; set; }

        public MermaidNodeShape Shape { get; set; }

        public MermaidStateNodeDefinition Build() => new(Id, Label, Shape);
    }

    private sealed class MermaidClassNodeBuilder
    {
        public MermaidClassNodeBuilder(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        public string Label { get; set; }

        public List<string> Members { get; } = [];

        public void AddMember(string member)
        {
            if (!string.IsNullOrWhiteSpace(member))
            {
                Members.Add(member.Trim());
            }
        }

        public MermaidClassNodeDefinition Build() => new(Id, Label, Members.ToList());
    }

    private sealed class MermaidMindmapNodeBuilder
    {
        public MermaidMindmapNodeBuilder(string id, string label, MermaidNodeShape shape)
        {
            Id = id;
            Label = label;
            Shape = shape;
        }

        public string Id { get; }

        public string Label { get; }

        public MermaidNodeShape Shape { get; }

        public List<MermaidMindmapNodeBuilder> Children { get; } = [];

        public MermaidMindmapNodeDefinition Build()
        {
            return new MermaidMindmapNodeDefinition(
                Id,
                Label,
                Shape,
                Children.Select(static child => child.Build()).ToList());
        }
    }

    private sealed class MermaidErEntityBuilder
    {
        public MermaidErEntityBuilder(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        public string Label { get; set; }

        public List<MermaidErAttributeDefinition> Attributes { get; } = [];

        public MermaidErEntityDefinition Build() => new(Id, Label, Attributes.ToList());
    }

    private readonly record struct MermaidClassRelationPattern(string Token, bool Reverse, bool Dotted, bool Directed);

    private readonly record struct MermaidParsedFlowNode(string Id, string Label, MermaidNodeShape Shape, bool HasExplicitLabel);

    private readonly record struct MermaidMindmapNodeContext(int Indent, MermaidMindmapNodeBuilder Node);
}

internal sealed class MermaidDiagramControl : Control
{
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.Parse("#F8FAFF"));
    private static readonly IBrush NodeBorder = new SolidColorBrush(Color.Parse("#7C8BFF"));
    private static readonly IBrush AccentFill = new SolidColorBrush(Color.Parse("#EEF2FF"));
    private static readonly IBrush AccentStroke = new SolidColorBrush(Color.Parse("#4F46E5"));
    private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush SequenceHeaderFill = new SolidColorBrush(Color.Parse("#F5F7FF"));
    private static readonly IBrush SequenceLifelineBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush SecondaryTextBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly Color[] AccentPaletteColors =
    [
        Color.Parse("#4F46E5"),
        Color.Parse("#0891B2"),
        Color.Parse("#16A34A"),
        Color.Parse("#F59E0B"),
        Color.Parse("#DB2777"),
        Color.Parse("#7C3AED")
    ];
    private static readonly IBrush[] AccentPaletteBrushes = AccentPaletteColors
        .Select(static color => new SolidColorBrush(color))
        .ToArray();
    private static readonly IBrush[] SoftPaletteBrushes = AccentPaletteColors
        .Select(static color => new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B)))
        .ToArray();
    private static readonly IBrush[] StrongSoftPaletteBrushes = AccentPaletteColors
        .Select(static color => new SolidColorBrush(Color.FromArgb(56, color.R, color.G, color.B)))
        .ToArray();
    private readonly MermaidDiagramDefinition _diagram;
    private readonly double _preferredWidth;
    private readonly double _fontSize;
    private readonly Typeface _typeface;
    private readonly IBrush _foreground;
    private MermaidLayoutSnapshot? _snapshot;
    private double _snapshotWidth = double.NaN;

    public MermaidDiagramControl(MermaidDiagramDefinition diagram, double preferredWidth, double fontSize, IBrush foreground)
    {
        _diagram = diagram;
        _preferredWidth = preferredWidth;
        _fontSize = Math.Max(fontSize - 0.5, 12);
        _typeface = new Typeface(new FontFamily("Inter, Segoe UI, Arial"));
        _foreground = foreground;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ResolveWidth(availableSize.Width);
        return EnsureSnapshot(width).Size;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        EnsureSnapshot().Draw(context);
    }

    public bool TryHitTestElement(Point point, out IReadOnlyList<Rect> highlightRects)
    {
        var hitRects = EnsureSnapshot().HitTest(point);
        if (hitRects is { Count: > 0 })
        {
            highlightRects = hitRects;
            return true;
        }

        highlightRects = Array.Empty<Rect>();
        return false;
    }

    private double ResolveWidth(double availableWidth)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return availableWidth;
        }

        return _preferredWidth;
    }

    private MermaidLayoutSnapshot EnsureSnapshot(double? widthOverride = null)
    {
        var width = widthOverride ?? (Bounds.Width > 0 ? Bounds.Width : _preferredWidth);
        if (_snapshot is null || Math.Abs(_snapshotWidth - width) > 0.5)
        {
            _snapshot = MermaidLayoutBuilder.Build(_diagram, width, _typeface, _fontSize, _foreground);
            _snapshotWidth = width;
        }

        return _snapshot;
    }

    private static bool IsPointNearSegment(Point point, Point start, Point end, double threshold)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2)) <= threshold;
        }

        var lengthSquared = (dx * dx) + (dy * dy);
        var projection = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);

        var nearestX = start.X + (projection * dx);
        var nearestY = start.Y + (projection * dy);
        var distance = Math.Sqrt(Math.Pow(point.X - nearestX, 2) + Math.Pow(point.Y - nearestY, 2));
        return distance <= threshold;
    }

    private static Rect CreateLineHighlightRect(Point start, Point end, double padding)
    {
        var left = Math.Min(start.X, end.X) - padding;
        var top = Math.Min(start.Y, end.Y) - padding;
        var right = Math.Max(start.X, end.X) + padding;
        var bottom = Math.Max(start.Y, end.Y) + padding;
        return new Rect(left, top, Math.Max(right - left, padding * 2), Math.Max(bottom - top, padding * 2));
    }

    private static IBrush GetAccentBrush(int index) => AccentPaletteBrushes[index % AccentPaletteBrushes.Length];

    private static IBrush GetSoftBrush(int index) => SoftPaletteBrushes[index % SoftPaletteBrushes.Length];

    private static IBrush GetStrongSoftBrush(int index) => StrongSoftPaletteBrushes[index % StrongSoftPaletteBrushes.Length];

    private static IBrush CreateBrush(Color color) => new SolidColorBrush(color);

    private abstract class MermaidLayoutSnapshot
    {
        protected MermaidLayoutSnapshot(Size size)
        {
            Size = size;
        }

        public Size Size { get; }

        public abstract void Draw(DrawingContext context);

        public abstract IReadOnlyList<Rect>? HitTest(Point point);
    }

    private sealed class MermaidFlowchartSnapshot : MermaidLayoutSnapshot
    {
        private readonly IReadOnlyList<FlowEdgeVisual> _edges;
        private readonly IReadOnlyList<FlowNodeVisual> _nodes;

        public MermaidFlowchartSnapshot(Size size, IReadOnlyList<FlowEdgeVisual> edges, IReadOnlyList<FlowNodeVisual> nodes)
            : base(size)
        {
            _edges = edges;
            _nodes = nodes;
        }

        public override void Draw(DrawingContext context)
        {
            foreach (var edge in _edges)
            {
                var pen = new Pen(EdgeBrush, edge.Dotted ? 1.5 : 1.8, edge.Dotted ? DashStyle.Dash : null, PenLineCap.Round, PenLineJoin.Round);
                context.DrawLine(pen, edge.Start, edge.End);

                if (edge.LabelBounds is { } labelBounds && edge.LabelLayout is not null)
                {
                    context.DrawRectangle(Brushes.White, null, labelBounds);
                    edge.LabelLayout.Draw(context, new Point(labelBounds.X + 6, labelBounds.Y + 3));
                }

                if (edge.ArrowHead is not null)
                {
                    context.DrawGeometry(EdgeBrush, null, edge.ArrowHead);
                }
            }

            foreach (var node in _nodes)
            {
                context.DrawGeometry(node.Fill, new Pen(node.Stroke, 1.6), node.Geometry);
                node.Text.Draw(context, new Point(
                    node.Bounds.X + ((node.Bounds.Width - node.Text.Width) / 2),
                    node.Bounds.Y + ((node.Bounds.Height - node.Text.Height) / 2)));
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var node in _nodes)
            {
                if (node.Bounds.Contains(point))
                {
                    return [node.Bounds];
                }
            }

            foreach (var edge in _edges)
            {
                if (edge.LabelBounds is { } labelBounds && labelBounds.Contains(point))
                {
                    return [labelBounds];
                }

                if (IsPointNearSegment(point, edge.Start, edge.End, 8))
                {
                    return [CreateLineHighlightRect(edge.Start, edge.End, 6)];
                }
            }

            return null;
        }
    }

    private sealed class MermaidSequenceSnapshot : MermaidLayoutSnapshot
    {
        private readonly IReadOnlyList<SequenceParticipantVisual> _participants;
        private readonly IReadOnlyList<SequenceMessageVisual> _messages;
        private readonly double _lifelineBottom;

        public MermaidSequenceSnapshot(
            Size size,
            IReadOnlyList<SequenceParticipantVisual> participants,
            IReadOnlyList<SequenceMessageVisual> messages,
            double lifelineBottom)
            : base(size)
        {
            _participants = participants;
            _messages = messages;
            _lifelineBottom = lifelineBottom;
        }

        public override void Draw(DrawingContext context)
        {
            var lifelinePen = new Pen(SequenceLifelineBrush, 1.2, DashStyle.Dash, PenLineCap.Round, PenLineJoin.Round);

            foreach (var participant in _participants)
            {
                context.DrawGeometry(SequenceHeaderFill, new Pen(NodeBorder, 1.4), new RectangleGeometry(participant.Bounds, 10, 10));
                participant.Label.Draw(context, new Point(
                    participant.Bounds.X + ((participant.Bounds.Width - participant.Label.Width) / 2),
                    participant.Bounds.Y + ((participant.Bounds.Height - participant.Label.Height) / 2)));

                context.DrawLine(
                    lifelinePen,
                    new Point(participant.CenterX, participant.Bounds.Bottom + 10),
                    new Point(participant.CenterX, _lifelineBottom));
            }

            foreach (var message in _messages)
            {
                var pen = new Pen(EdgeBrush, message.Emphasized ? 2 : 1.6, message.Dotted ? DashStyle.Dash : null, PenLineCap.Round, PenLineJoin.Round);
                context.DrawLine(pen, message.Start, message.End);
                context.DrawGeometry(EdgeBrush, null, message.ArrowHead);
                message.Label.Draw(context, message.LabelOrigin);
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var participant in _participants)
            {
                if (participant.Bounds.Contains(point))
                {
                    return [participant.Bounds];
                }
            }

            foreach (var message in _messages)
            {
                var labelBounds = new Rect(message.LabelOrigin, new Size(message.Label.Width, message.Label.Height));
                if (labelBounds.Contains(point))
                {
                    return [labelBounds];
                }

                if (IsPointNearSegment(point, message.Start, message.End, 8))
                {
                    return [CreateLineHighlightRect(message.Start, message.End, 6)];
                }
            }

            return null;
        }
    }

    private sealed class MermaidClassDiagramSnapshot : MermaidLayoutSnapshot
    {
        private readonly IReadOnlyList<FlowEdgeVisual> _relations;
        private readonly IReadOnlyList<ClassNodeVisual> _classes;

        public MermaidClassDiagramSnapshot(
            Size size,
            IReadOnlyList<FlowEdgeVisual> relations,
            IReadOnlyList<ClassNodeVisual> classes)
            : base(size)
        {
            _relations = relations;
            _classes = classes;
        }

        public override void Draw(DrawingContext context)
        {
            foreach (var relation in _relations)
            {
                var pen = new Pen(EdgeBrush, relation.Dotted ? 1.4 : 1.7, relation.Dotted ? DashStyle.Dash : null, PenLineCap.Round, PenLineJoin.Round);
                context.DrawLine(pen, relation.Start, relation.End);

                if (relation.LabelBounds is { } labelBounds && relation.LabelLayout is not null)
                {
                    context.DrawRectangle(Brushes.White, null, labelBounds);
                    relation.LabelLayout.Draw(context, new Point(labelBounds.X + 6, labelBounds.Y + 3));
                }

                if (relation.ArrowHead is not null)
                {
                    context.DrawGeometry(EdgeBrush, null, relation.ArrowHead);
                }
            }

            foreach (var @class in _classes)
            {
                context.DrawGeometry(NodeFill, new Pen(NodeBorder, 1.5), new RectangleGeometry(@class.Bounds, 10, 10));
                context.DrawGeometry(AccentFill, null, new RectangleGeometry(@class.HeaderBounds, 10, 10));
                context.DrawLine(new Pen(NodeBorder, 1.1), new Point(@class.Bounds.X, @class.SeparatorY), new Point(@class.Bounds.Right, @class.SeparatorY));
                @class.Title.Draw(context, @class.TitleOrigin);

                foreach (var member in @class.Members)
                {
                    member.Text.Draw(context, member.Origin);
                }
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var @class in _classes)
            {
                if (@class.Bounds.Contains(point))
                {
                    return [@class.Bounds];
                }
            }

            foreach (var relation in _relations)
            {
                if (relation.LabelBounds is { } labelBounds && labelBounds.Contains(point))
                {
                    return [labelBounds];
                }

                if (IsPointNearSegment(point, relation.Start, relation.End, 8))
                {
                    return [CreateLineHighlightRect(relation.Start, relation.End, 6)];
                }
            }

            return null;
        }
    }

    private sealed class MermaidPieChartSnapshot : MermaidLayoutSnapshot
    {
        private readonly Rect _pieBounds;
        private readonly Point _center;
        private readonly double _radius;
        private readonly IReadOnlyList<PieSliceVisual> _slices;

        public MermaidPieChartSnapshot(
            Size size,
            Rect pieBounds,
            Point center,
            double radius,
            IReadOnlyList<PieSliceVisual> slices)
            : base(size)
        {
            _pieBounds = pieBounds;
            _center = center;
            _radius = radius;
            _slices = slices;
        }

        public override void Draw(DrawingContext context)
        {
            foreach (var slice in _slices)
            {
                context.DrawGeometry(slice.Fill, new Pen(Brushes.White, 1.4), slice.Geometry);
                context.DrawRectangle(slice.Fill, null, slice.SwatchBounds);
                slice.LegendLayout.Draw(context, slice.LegendOrigin);
            }

            context.DrawGeometry(null, new Pen(NodeBorder, 1.1), new EllipseGeometry(_pieBounds));
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var slice in _slices)
            {
                if (slice.LegendBounds.Contains(point))
                {
                    return [slice.LegendBounds];
                }
            }

            if (!_pieBounds.Contains(point))
            {
                return null;
            }

            var dx = point.X - _center.X;
            var dy = point.Y - _center.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance > _radius)
            {
                return null;
            }

            var angle = Math.Atan2(dy, dx) * (180 / Math.PI);
            angle = angle < 0 ? angle + 360 : angle;
            foreach (var slice in _slices)
            {
                if (IsAngleWithinSlice(angle, slice.StartAngle, slice.SweepAngle))
                {
                    return [_pieBounds];
                }
            }

            return [_pieBounds];
        }

        private static bool IsAngleWithinSlice(double angle, double startAngle, double sweepAngle)
        {
            if (sweepAngle >= 359.9)
            {
                return true;
            }

            var normalizedStart = NormalizeAngle(startAngle);
            var normalizedEnd = NormalizeAngle(startAngle + sweepAngle);
            if (normalizedStart <= normalizedEnd)
            {
                return angle >= normalizedStart && angle <= normalizedEnd;
            }

            return angle >= normalizedStart || angle <= normalizedEnd;
        }

        private static double NormalizeAngle(double angle)
        {
            var normalized = angle % 360;
            return normalized < 0 ? normalized + 360 : normalized;
        }
    }

    private sealed class MermaidJourneySnapshot : MermaidLayoutSnapshot
    {
        private readonly IReadOnlyList<JourneySectionVisual> _sections;

        public MermaidJourneySnapshot(Size size, IReadOnlyList<JourneySectionVisual> sections)
            : base(size)
        {
            _sections = sections;
        }

        public override void Draw(DrawingContext context)
        {
            foreach (var section in _sections)
            {
                context.DrawGeometry(section.Fill, new Pen(section.Stroke, 1.2), new RectangleGeometry(section.Bounds, 12, 12));
                section.Title.Draw(context, section.TitleOrigin);

                foreach (var task in section.Tasks)
                {
                    context.DrawGeometry(task.Fill, new Pen(task.Stroke, 1.2), new RectangleGeometry(task.Bounds, 10, 10));
                    task.Title.Draw(context, task.TitleOrigin);
                    task.Actors.Draw(context, task.ActorsOrigin);

                    for (var index = 0; index < task.ScoreDots.Count; index++)
                    {
                        var dotBounds = task.ScoreDots[index];
                        var fill = index < task.Score ? task.Stroke : Brushes.White;
                        context.DrawGeometry(fill, new Pen(task.Stroke, 1), new EllipseGeometry(dotBounds));
                    }
                }
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var section in _sections)
            {
                if (section.Bounds.Contains(point))
                {
                    return [section.Bounds];
                }

                foreach (var task in section.Tasks)
                {
                    if (task.Bounds.Contains(point))
                    {
                        return [task.Bounds];
                    }
                }
            }

            return null;
        }
    }

    private sealed class MermaidTimelineSnapshot : MermaidLayoutSnapshot
    {
        private readonly IReadOnlyList<TimelineSectionVisual> _sections;
        private readonly IReadOnlyList<TimelineEntryVisual> _entries;
        private readonly Point _spineStart;
        private readonly Point _spineEnd;

        public MermaidTimelineSnapshot(
            Size size,
            IReadOnlyList<TimelineSectionVisual> sections,
            IReadOnlyList<TimelineEntryVisual> entries,
            Point spineStart,
            Point spineEnd)
            : base(size)
        {
            _sections = sections;
            _entries = entries;
            _spineStart = spineStart;
            _spineEnd = spineEnd;
        }

        public override void Draw(DrawingContext context)
        {
            if (_entries.Count > 0)
            {
                context.DrawLine(new Pen(SequenceLifelineBrush, 1.4), _spineStart, _spineEnd);
            }

            foreach (var section in _sections)
            {
                context.DrawGeometry(section.Fill, new Pen(section.Stroke, 1.1), new RectangleGeometry(section.Bounds, 12, 12));
                section.Title.Draw(context, section.TitleOrigin);
            }

            foreach (var entry in _entries)
            {
                entry.Period.Draw(context, entry.PeriodOrigin);
                context.DrawLine(new Pen(SequenceLifelineBrush, 1.2), entry.MarkerCenter, new Point(entry.CardBounds.X, entry.MarkerCenter.Y));
                context.DrawGeometry(entry.Fill, new Pen(entry.Stroke, 1.2), new RectangleGeometry(entry.CardBounds, 10, 10));
                context.DrawGeometry(entry.Stroke, new Pen(entry.Stroke, 1), new EllipseGeometry(new Rect(entry.MarkerCenter.X - 5, entry.MarkerCenter.Y - 5, 10, 10)));

                foreach (var eventVisual in entry.Events)
                {
                    eventVisual.Text.Draw(context, eventVisual.Origin);
                }
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var section in _sections)
            {
                if (section.Bounds.Contains(point))
                {
                    return [section.Bounds];
                }
            }

            foreach (var entry in _entries)
            {
                if (entry.PeriodBounds.Contains(point))
                {
                    return [entry.PeriodBounds];
                }

                if (entry.CardBounds.Contains(point))
                {
                    return [entry.CardBounds];
                }
            }

            return null;
        }
    }

    private sealed class MermaidQuadrantSnapshot : MermaidLayoutSnapshot
    {
        private readonly Rect _chartBounds;
        private readonly IReadOnlyList<Rect> _quadrants;
        private readonly IReadOnlyList<IBrush> _quadrantFills;
        private readonly IReadOnlyList<PositionedTextVisual> _quadrantLabels;
        private readonly IReadOnlyList<PositionedTextVisual> _axisLabels;
        private readonly IReadOnlyList<QuadrantPointVisual> _points;

        public MermaidQuadrantSnapshot(
            Size size,
            Rect chartBounds,
            IReadOnlyList<Rect> quadrants,
            IReadOnlyList<IBrush> quadrantFills,
            IReadOnlyList<PositionedTextVisual> quadrantLabels,
            IReadOnlyList<PositionedTextVisual> axisLabels,
            IReadOnlyList<QuadrantPointVisual> points)
            : base(size)
        {
            _chartBounds = chartBounds;
            _quadrants = quadrants;
            _quadrantFills = quadrantFills;
            _quadrantLabels = quadrantLabels;
            _axisLabels = axisLabels;
            _points = points;
        }

        public override void Draw(DrawingContext context)
        {
            for (var index = 0; index < _quadrants.Count; index++)
            {
                context.DrawRectangle(_quadrantFills[index], null, _quadrants[index]);
            }

            context.DrawRectangle(null, new Pen(NodeBorder, 1.4), _chartBounds);
            var verticalAxisX = _chartBounds.X + (_chartBounds.Width / 2);
            var horizontalAxisY = _chartBounds.Y + (_chartBounds.Height / 2);
            context.DrawLine(new Pen(SequenceLifelineBrush, 1.1), new Point(verticalAxisX, _chartBounds.Y), new Point(verticalAxisX, _chartBounds.Bottom));
            context.DrawLine(new Pen(SequenceLifelineBrush, 1.1), new Point(_chartBounds.X, horizontalAxisY), new Point(_chartBounds.Right, horizontalAxisY));

            foreach (var label in _quadrantLabels)
            {
                label.Text.Draw(context, label.Origin);
            }

            foreach (var label in _axisLabels)
            {
                label.Text.Draw(context, label.Origin);
            }

            foreach (var point in _points)
            {
                context.DrawGeometry(point.Fill, new Pen(point.Stroke, point.StrokeWidth), new EllipseGeometry(point.Bounds));
                point.Label.Draw(context, point.LabelOrigin);
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var quadrantPoint in _points)
            {
                if (quadrantPoint.Bounds.Contains(point))
                {
                    return [quadrantPoint.Bounds];
                }
            }

            return _chartBounds.Contains(point) ? [_chartBounds] : null;
        }
    }

    private sealed class MermaidErDiagramSnapshot : MermaidLayoutSnapshot
    {
        private readonly IReadOnlyList<ErRelationVisual> _relations;
        private readonly IReadOnlyList<ErEntityVisual> _entities;

        public MermaidErDiagramSnapshot(
            Size size,
            IReadOnlyList<ErRelationVisual> relations,
            IReadOnlyList<ErEntityVisual> entities)
            : base(size)
        {
            _relations = relations;
            _entities = entities;
        }

        public override void Draw(DrawingContext context)
        {
            foreach (var relation in _relations)
            {
                var pen = new Pen(EdgeBrush, relation.Dotted ? 1.3 : 1.6, relation.Dotted ? DashStyle.Dash : null, PenLineCap.Round, PenLineJoin.Round);
                context.DrawLine(pen, relation.Start, relation.End);

                if (relation.LabelBounds is { } labelBounds && relation.LabelLayout is not null)
                {
                    context.DrawRectangle(Brushes.White, null, labelBounds);
                    relation.LabelLayout.Draw(context, new Point(labelBounds.X + 6, labelBounds.Y + 3));
                }

                relation.StartMarker.Text.Draw(context, relation.StartMarker.Origin);
                relation.EndMarker.Text.Draw(context, relation.EndMarker.Origin);
            }

            foreach (var entity in _entities)
            {
                context.DrawGeometry(NodeFill, new Pen(NodeBorder, 1.5), new RectangleGeometry(entity.Bounds, 10, 10));
                context.DrawGeometry(AccentFill, null, new RectangleGeometry(entity.HeaderBounds, 10, 10));
                context.DrawLine(new Pen(NodeBorder, 1.1), new Point(entity.Bounds.X, entity.SeparatorY), new Point(entity.Bounds.Right, entity.SeparatorY));
                entity.Title.Draw(context, entity.TitleOrigin);

                foreach (var attribute in entity.Attributes)
                {
                    attribute.Text.Draw(context, attribute.Origin);
                }
            }
        }

        public override IReadOnlyList<Rect>? HitTest(Point point)
        {
            foreach (var entity in _entities)
            {
                if (entity.Bounds.Contains(point))
                {
                    return [entity.Bounds];
                }
            }

            foreach (var relation in _relations)
            {
                if (relation.LabelBounds is { } labelBounds && labelBounds.Contains(point))
                {
                    return [labelBounds];
                }

                if (IsPointNearSegment(point, relation.Start, relation.End, 8))
                {
                    return [CreateLineHighlightRect(relation.Start, relation.End, 6)];
                }
            }

            return null;
        }
    }

    private static class MermaidLayoutBuilder
    {
        public static MermaidLayoutSnapshot Build(
            MermaidDiagramDefinition diagram,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            return diagram switch
            {
                MermaidFlowchartDiagramDefinition flowchart => BuildFlowchart(flowchart, width, typeface, fontSize, foreground),
                MermaidSequenceDiagramDefinition sequence => BuildSequence(sequence, width, typeface, fontSize, foreground),
                MermaidStateDiagramDefinition state => BuildState(state, width, typeface, fontSize, foreground),
                MermaidClassDiagramDefinition classDiagram => BuildClassDiagram(classDiagram, width, typeface, fontSize, foreground),
                MermaidPieDiagramDefinition pieChart => BuildPieChart(pieChart, width, typeface, fontSize, foreground),
                MermaidJourneyDiagramDefinition journey => BuildJourney(journey, width, typeface, fontSize, foreground),
                MermaidTimelineDiagramDefinition timeline => BuildTimeline(timeline, width, typeface, fontSize, foreground),
                MermaidQuadrantChartDiagramDefinition quadrantChart => BuildQuadrantChart(quadrantChart, width, typeface, fontSize, foreground),
                MermaidMindmapDiagramDefinition mindmap => BuildMindmap(mindmap, width, typeface, fontSize, foreground),
                MermaidErDiagramDefinition erDiagram => BuildErDiagram(erDiagram, width, typeface, fontSize, foreground),
                _ => new MermaidFlowchartSnapshot(new Size(width, 120), [], [])
            };
        }

        private static MermaidLayoutSnapshot BuildFlowchart(
            MermaidFlowchartDiagramDefinition flowchart,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double columnGap = 36;
            const double rowGap = 24;

            var nodeLevelMap = ComputeNodeLevels(flowchart);
            var groupedLevels = flowchart.Nodes
                .GroupBy(node => nodeLevelMap[node.Id])
                .OrderBy(group => group.Key)
                .Select(group => group.ToList())
                .ToList();

            var horizontal = flowchart.Direction is MermaidFlowDirection.LeftToRight or MermaidFlowDirection.RightToLeft;
            var primaryCount = Math.Max(1, groupedLevels.Count);
            var secondaryCount = Math.Max(1, groupedLevels.Max(group => group.Count));

            var contentWidth = Math.Max(width - (padding * 2), 200);
            var cellPrimarySize = horizontal
                ? Math.Max((contentWidth - ((primaryCount - 1) * columnGap)) / primaryCount, 120)
                : Math.Max((contentWidth - ((secondaryCount - 1) * columnGap)) / secondaryCount, 120);
            var labelMaxWidth = Math.Max(80, cellPrimarySize - 28);

            var textLayouts = new Dictionary<string, TextLayout>(StringComparer.Ordinal);
            var maxNodeHeight = 0d;
            foreach (var node in flowchart.Nodes)
            {
                var layout = CreateTextLayout(node.Label, typeface, fontSize, foreground, labelMaxWidth, TextWrapping.Wrap);
                textLayouts.Add(node.Id, layout);
                maxNodeHeight = Math.Max(maxNodeHeight, layout.Height + 22);
            }

            var cellSecondarySize = Math.Max(maxNodeHeight + rowGap, 88);
            var nodes = new List<FlowNodeVisual>(flowchart.Nodes.Count);
            var nodeBounds = new Dictionary<string, Rect>(StringComparer.Ordinal);

            for (var levelIndex = 0; levelIndex < groupedLevels.Count; levelIndex++)
            {
                var group = groupedLevels[levelIndex];
                var primaryIndex = flowchart.Direction switch
                {
                    MermaidFlowDirection.RightToLeft or MermaidFlowDirection.BottomToTop => groupedLevels.Count - 1 - levelIndex,
                    _ => levelIndex
                };

                var secondaryOffset = (secondaryCount - group.Count) / 2d;
                for (var nodeIndex = 0; nodeIndex < group.Count; nodeIndex++)
                {
                    var node = group[nodeIndex];
                    var textLayout = textLayouts[node.Id];
                    var nodeSize = ResolveFlowNodeSize(node, textLayout, cellPrimarySize);
                    var nodeWidth = nodeSize.Width;
                    var nodeHeight = nodeSize.Height;

                    double x;
                    double y;
                    if (horizontal)
                    {
                        x = padding + (primaryIndex * (cellPrimarySize + columnGap)) + ((cellPrimarySize - nodeWidth) / 2);
                        y = padding + ((secondaryOffset + nodeIndex) * cellSecondarySize) + ((cellSecondarySize - nodeHeight) / 2);
                    }
                    else
                    {
                        x = padding + ((secondaryOffset + nodeIndex) * (cellPrimarySize + columnGap)) + ((cellPrimarySize - nodeWidth) / 2);
                        y = padding + (primaryIndex * (cellSecondarySize + rowGap));
                    }

                    var bounds = new Rect(x, y, nodeWidth, nodeHeight);
                    nodes.Add(new FlowNodeVisual(
                        bounds,
                        CreateNodeGeometry(bounds, node.Shape),
                        textLayout,
                        node.Shape == MermaidNodeShape.Diamond ? AccentFill : NodeFill,
                        node.Shape == MermaidNodeShape.Diamond ? AccentStroke : NodeBorder));
                    nodeBounds.Add(node.Id, bounds);
                }
            }

            var edges = new List<FlowEdgeVisual>(flowchart.Edges.Count);
            foreach (var edge in flowchart.Edges)
            {
                if (!nodeBounds.TryGetValue(edge.FromId, out var fromBounds) || !nodeBounds.TryGetValue(edge.ToId, out var toBounds))
                {
                    continue;
                }

                var (start, end) = ResolveConnectionPoints(fromBounds, toBounds);
                var arrowHead = CreateArrowHead(end, start, 12);
                TextLayout? labelLayout = null;
                Rect? labelBounds = null;
                if (!string.IsNullOrWhiteSpace(edge.Label))
                {
                    labelLayout = CreateTextLayout(edge.Label!, typeface, Math.Max(fontSize - 1, 11), foreground, 160, TextWrapping.Wrap);
                    var midX = (start.X + end.X) / 2;
                    var midY = (start.Y + end.Y) / 2;
                    labelBounds = new Rect(midX - (labelLayout.Width / 2) - 6, midY - labelLayout.Height - 8, labelLayout.Width + 12, labelLayout.Height + 6);
                }

                edges.Add(new FlowEdgeVisual(start, end, labelLayout, labelBounds, arrowHead, edge.Dotted));
            }

            var totalHeight = horizontal
                ? padding + (secondaryCount * cellSecondarySize) + padding
                : padding + (groupedLevels.Count * (cellSecondarySize + rowGap)) + padding;
            return new MermaidFlowchartSnapshot(new Size(width, Math.Max(totalHeight, 180)), edges, nodes);
        }

        private static MermaidLayoutSnapshot BuildSequence(
            MermaidSequenceDiagramDefinition sequence,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double verticalGap = 22;

            var participantCount = Math.Max(1, sequence.Participants.Count);
            var columnWidth = Math.Max((width - (padding * 2)) / participantCount, 120);
            var participantLayouts = sequence.Participants.ToDictionary(
                participant => participant.Id,
                participant => CreateTextLayout(participant.Label, typeface, fontSize, foreground, Math.Max(columnWidth - 26, 80), TextWrapping.Wrap),
                StringComparer.Ordinal);
            var maxParticipantHeight = participantLayouts.Values.Max(layout => layout.Height + 20);

            var participantVisuals = new List<SequenceParticipantVisual>(sequence.Participants.Count);
            var participantCenters = new Dictionary<string, double>(StringComparer.Ordinal);
            for (var index = 0; index < sequence.Participants.Count; index++)
            {
                var participant = sequence.Participants[index];
                var layout = participantLayouts[participant.Id];
                var centerX = padding + (index * columnWidth) + (columnWidth / 2);
                var boxWidth = Math.Min(columnWidth - 16, Math.Max(96, layout.Width + 28));
                var boxBounds = new Rect(centerX - (boxWidth / 2), padding, boxWidth, Math.Max(52, layout.Height + 18));
                participantVisuals.Add(new SequenceParticipantVisual(boxBounds, centerX, layout));
                participantCenters.Add(participant.Id, centerX);
            }

            var messageVisuals = new List<SequenceMessageVisual>(sequence.Messages.Count);
            var labelMaxWidth = Math.Max(columnWidth - 24, 90);
            var currentY = padding + maxParticipantHeight + 28;
            foreach (var message in sequence.Messages)
            {
                if (!participantCenters.TryGetValue(message.FromId, out var fromX) || !participantCenters.TryGetValue(message.ToId, out var toX))
                {
                    continue;
                }

                var labelLayout = CreateTextLayout(message.Label, typeface, Math.Max(fontSize - 0.5, 11), foreground, labelMaxWidth, TextWrapping.Wrap);
                var labelOrigin = new Point(((fromX + toX) / 2) - (labelLayout.Width / 2), currentY - labelLayout.Height - 6);
                var start = new Point(fromX, currentY);
                var end = new Point(toX, currentY);
                messageVisuals.Add(new SequenceMessageVisual(
                    start,
                    end,
                    CreateArrowHead(end, start, message.Emphasized ? 12 : 10),
                    labelLayout,
                    labelOrigin,
                    message.Dotted,
                    message.Emphasized));
                currentY += Math.Max(labelLayout.Height + verticalGap, 40);
            }

            var totalHeight = currentY + 24;
            return new MermaidSequenceSnapshot(new Size(width, Math.Max(totalHeight, 180)), participantVisuals, messageVisuals, totalHeight - 16);
        }

        private static MermaidLayoutSnapshot BuildState(
            MermaidStateDiagramDefinition state,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            var flowchart = new MermaidFlowchartDiagramDefinition(
                state.Source,
                state.Direction,
                state.States.Select(static stateNode => new MermaidFlowNodeDefinition(stateNode.Id, stateNode.Label, stateNode.Shape)).ToList(),
                state.Transitions.Select(static transition => new MermaidFlowEdgeDefinition(transition.FromId, transition.ToId, transition.Label, transition.Dotted)).ToList());
            return BuildFlowchart(flowchart, width, typeface, fontSize, foreground);
        }

        private static MermaidLayoutSnapshot BuildClassDiagram(
            MermaidClassDiagramDefinition classDiagram,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double columnGap = 28;
            const double rowGap = 24;
            const double memberSpacing = 6;

            var levelMap = ComputeGraphLevels(
                classDiagram.Classes.Select(static node => node.Id),
                classDiagram.Relations.Select(static relation => new MermaidEdgeRef(relation.FromId, relation.ToId)));
            var groupedLevels = classDiagram.Classes
                .GroupBy(node => levelMap[node.Id])
                .OrderBy(group => group.Key)
                .Select(group => group.ToList())
                .ToList();

            var horizontal = classDiagram.Direction is MermaidFlowDirection.LeftToRight or MermaidFlowDirection.RightToLeft;
            var primaryCount = Math.Max(1, groupedLevels.Count);
            var secondaryCount = Math.Max(1, groupedLevels.Max(group => group.Count));
            var contentWidth = Math.Max(width - (padding * 2), 240);
            var cellPrimarySize = horizontal
                ? Math.Max((contentWidth - ((primaryCount - 1) * columnGap)) / primaryCount, 180)
                : Math.Max((contentWidth - ((secondaryCount - 1) * columnGap)) / secondaryCount, 180);
            var textMaxWidth = Math.Max(cellPrimarySize - 28, 120);

            var cards = new Dictionary<string, ClassCardMetrics>(StringComparer.Ordinal);
            var maxCardHeight = 0d;
            foreach (var classNode in classDiagram.Classes)
            {
                var titleLayout = CreateTextLayout(classNode.Label, typeface, fontSize, foreground, textMaxWidth, TextWrapping.Wrap);
                var memberLayouts = classNode.Members
                    .Select(member => CreateTextLayout(member, typeface, Math.Max(fontSize - 1, 11), foreground, textMaxWidth, TextWrapping.Wrap))
                    .ToList();
                var memberWidth = memberLayouts.Count == 0 ? 0 : memberLayouts.Max(static layout => layout.Width);
                var contentMaxWidth = Math.Max(titleLayout.Width, memberWidth);
                var cardWidth = Math.Min(cellPrimarySize, Math.Max(170, contentMaxWidth + 28));
                var headerHeight = Math.Max(46, titleLayout.Height + 18);
                var membersHeight = memberLayouts.Count == 0
                    ? 0
                    : memberLayouts.Sum(static layout => layout.Height) + ((memberLayouts.Count - 1) * memberSpacing);
                var cardHeight = memberLayouts.Count == 0
                    ? headerHeight + 14
                    : headerHeight + membersHeight + 24;
                cards.Add(classNode.Id, new ClassCardMetrics(titleLayout, memberLayouts, cardWidth, cardHeight, headerHeight));
                maxCardHeight = Math.Max(maxCardHeight, cardHeight);
            }

            var cellSecondarySize = Math.Max(maxCardHeight + rowGap, 140);
            var classVisuals = new List<ClassNodeVisual>(classDiagram.Classes.Count);
            var classBounds = new Dictionary<string, Rect>(StringComparer.Ordinal);

            for (var levelIndex = 0; levelIndex < groupedLevels.Count; levelIndex++)
            {
                var group = groupedLevels[levelIndex];
                var primaryIndex = classDiagram.Direction switch
                {
                    MermaidFlowDirection.RightToLeft or MermaidFlowDirection.BottomToTop => groupedLevels.Count - 1 - levelIndex,
                    _ => levelIndex
                };

                var secondaryOffset = (secondaryCount - group.Count) / 2d;
                for (var classIndex = 0; classIndex < group.Count; classIndex++)
                {
                    var classNode = group[classIndex];
                    var card = cards[classNode.Id];

                    double x;
                    double y;
                    if (horizontal)
                    {
                        x = padding + (primaryIndex * (cellPrimarySize + columnGap)) + ((cellPrimarySize - card.Width) / 2);
                        y = padding + ((secondaryOffset + classIndex) * cellSecondarySize) + ((cellSecondarySize - card.Height) / 2);
                    }
                    else
                    {
                        x = padding + ((secondaryOffset + classIndex) * (cellPrimarySize + columnGap)) + ((cellPrimarySize - card.Width) / 2);
                        y = padding + (primaryIndex * (cellSecondarySize + rowGap));
                    }

                    var bounds = new Rect(x, y, card.Width, card.Height);
                    var headerBounds = new Rect(bounds.X, bounds.Y, bounds.Width, card.HeaderHeight);
                    var titleOrigin = new Point(bounds.X + ((bounds.Width - card.Title.Width) / 2), bounds.Y + ((card.HeaderHeight - card.Title.Height) / 2));
                    var separatorY = bounds.Y + card.HeaderHeight;
                    var memberOriginY = separatorY + 8;
                    var members = new List<ClassMemberVisual>(card.Members.Count);
                    foreach (var memberLayout in card.Members)
                    {
                        var origin = new Point(bounds.X + 12, memberOriginY);
                        members.Add(new ClassMemberVisual(memberLayout, origin));
                        memberOriginY += memberLayout.Height + memberSpacing;
                    }

                    classVisuals.Add(new ClassNodeVisual(bounds, headerBounds, card.Title, titleOrigin, members, separatorY));
                    classBounds.Add(classNode.Id, bounds);
                }
            }

            var relationVisuals = new List<FlowEdgeVisual>(classDiagram.Relations.Count);
            foreach (var relation in classDiagram.Relations)
            {
                if (!classBounds.TryGetValue(relation.FromId, out var fromBounds) || !classBounds.TryGetValue(relation.ToId, out var toBounds))
                {
                    continue;
                }

                var (start, end) = ResolveConnectionPoints(fromBounds, toBounds);
                var arrowHead = relation.Directed ? CreateArrowHead(end, start, 11) : null;
                TextLayout? labelLayout = null;
                Rect? labelBounds = null;
                if (!string.IsNullOrWhiteSpace(relation.Label))
                {
                    labelLayout = CreateTextLayout(relation.Label!, typeface, Math.Max(fontSize - 1, 11), foreground, 180, TextWrapping.Wrap);
                    var midX = (start.X + end.X) / 2;
                    var midY = (start.Y + end.Y) / 2;
                    labelBounds = new Rect(midX - (labelLayout.Width / 2) - 6, midY - labelLayout.Height - 8, labelLayout.Width + 12, labelLayout.Height + 6);
                }

                relationVisuals.Add(new FlowEdgeVisual(start, end, labelLayout, labelBounds, arrowHead, relation.Dotted));
            }

            var totalHeight = horizontal
                ? padding + (secondaryCount * cellSecondarySize) + padding
                : padding + (groupedLevels.Count * (cellSecondarySize + rowGap)) + padding;
            return new MermaidClassDiagramSnapshot(new Size(width, Math.Max(totalHeight, 220)), relationVisuals, classVisuals);
        }

        private static MermaidLayoutSnapshot BuildPieChart(
            MermaidPieDiagramDefinition pieChart,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double swatchSize = 14;
            const double legendGap = 24;
            const double legendSpacing = 12;

            var contentWidth = Math.Max(width - (padding * 2), 260);
            var stacked = contentWidth < 460;
            var chartDiameter = Math.Clamp(
                stacked ? contentWidth - 20 : contentWidth * 0.42,
                140,
                220);
            var radius = chartDiameter / 2;
            var pieX = stacked ? padding + ((contentWidth - chartDiameter) / 2) : padding + 8;
            var pieY = padding + 6;
            var pieBounds = new Rect(pieX, pieY, chartDiameter, chartDiameter);
            var center = new Point(pieBounds.X + radius, pieBounds.Y + radius);

            var legendX = stacked ? padding : pieBounds.Right + legendGap;
            var legendY = stacked ? pieBounds.Bottom + 18 : pieBounds.Y + 2;
            var legendWidth = stacked ? contentWidth : Math.Max(width - legendX - padding, 150);

            var slices = new List<PieSliceVisual>(pieChart.Slices.Count);
            var totalValue = pieChart.Slices.Sum(static slice => slice.Value);
            var accumulatedAngle = 0d;
            var currentLegendY = legendY;
            for (var index = 0; index < pieChart.Slices.Count; index++)
            {
                var slice = pieChart.Slices[index];
                var startAngle = -90 + accumulatedAngle;
                var sweepAngle = index == pieChart.Slices.Count - 1
                    ? 360 - accumulatedAngle
                    : (slice.Value / totalValue) * 360;
                accumulatedAngle += sweepAngle;

                var percentage = totalValue <= double.Epsilon ? 0 : (slice.Value / totalValue) * 100;
                var legendText = pieChart.ShowData
                    ? $"{slice.Label} - {slice.Value:0.##} ({percentage:0.#}%)"
                    : $"{slice.Label} - {percentage:0.#}%";
                var legendLayout = CreateTextLayout(
                    legendText,
                    typeface,
                    Math.Max(fontSize - 0.5, 11),
                    foreground,
                    Math.Max(legendWidth - swatchSize - 12, 80),
                    TextWrapping.Wrap);
                var legendHeight = Math.Max(legendLayout.Height, swatchSize);
                var swatchBounds = new Rect(legendX, currentLegendY + ((legendHeight - swatchSize) / 2), swatchSize, swatchSize);
                var legendOrigin = new Point(swatchBounds.Right + 8, currentLegendY);
                var legendBounds = new Rect(legendX, currentLegendY, legendWidth, legendHeight);

                slices.Add(new PieSliceVisual(
                    CreatePieSliceGeometry(center, radius, startAngle, sweepAngle),
                    GetAccentBrush(index),
                    startAngle,
                    sweepAngle,
                    legendBounds,
                    swatchBounds,
                    legendLayout,
                    legendOrigin));

                currentLegendY += legendHeight + legendSpacing;
            }

            var totalHeight = stacked
                ? currentLegendY + padding
                : Math.Max(pieBounds.Bottom + padding, currentLegendY - legendSpacing + padding);
            return new MermaidPieChartSnapshot(new Size(width, Math.Max(totalHeight, 220)), pieBounds, center, radius, slices);
        }

        private static MermaidLayoutSnapshot BuildJourney(
            MermaidJourneyDiagramDefinition journey,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double sectionSpacing = 16;
            const double taskSpacing = 10;
            const double scoreDotSize = 10;
            const double scoreGap = 6;
            const double scoreAreaWidth = 108;

            var contentWidth = Math.Max(width - (padding * 2), 260);
            var sections = new List<JourneySectionVisual>(journey.Sections.Count);
            var currentY = padding;

            for (var sectionIndex = 0; sectionIndex < journey.Sections.Count; sectionIndex++)
            {
                var section = journey.Sections[sectionIndex];
                var sectionLayout = CreateTextLayout(section.Name, typeface, fontSize + 0.5, foreground, contentWidth - 24, TextWrapping.Wrap);
                var sectionBounds = new Rect(padding, currentY, contentWidth, Math.Max(38, sectionLayout.Height + 16));
                var sectionOrigin = new Point(sectionBounds.X + 14, sectionBounds.Y + ((sectionBounds.Height - sectionLayout.Height) / 2));
                currentY = sectionBounds.Bottom + 10;

                var tasks = new List<JourneyTaskVisual>(section.Tasks.Count);
                foreach (var task in section.Tasks)
                {
                    var textWidth = Math.Max(contentWidth - scoreAreaWidth - 32, 140);
                    var titleLayout = CreateTextLayout(task.Label, typeface, fontSize, foreground, textWidth, TextWrapping.Wrap);
                    var actorsText = task.Actors.Count == 0 ? "No actors" : string.Join(", ", task.Actors);
                    var actorsLayout = CreateTextLayout(actorsText, typeface, Math.Max(fontSize - 1, 11), SecondaryTextBrush, textWidth, TextWrapping.Wrap);
                    var cardHeight = Math.Max(64, titleLayout.Height + actorsLayout.Height + 24);
                    var cardBounds = new Rect(padding, currentY, contentWidth, cardHeight);
                    var titleOrigin = new Point(cardBounds.X + 14, cardBounds.Y + 10);
                    var actorsOrigin = new Point(cardBounds.X + 14, titleOrigin.Y + titleLayout.Height + 4);

                    var dots = new List<Rect>(5);
                    var dotStartX = cardBounds.Right - 16 - ((scoreDotSize * 5) + (scoreGap * 4));
                    var dotY = cardBounds.Y + ((cardBounds.Height - scoreDotSize) / 2);
                    for (var dotIndex = 0; dotIndex < 5; dotIndex++)
                    {
                        dots.Add(new Rect(dotStartX + (dotIndex * (scoreDotSize + scoreGap)), dotY, scoreDotSize, scoreDotSize));
                    }

                    tasks.Add(new JourneyTaskVisual(
                        cardBounds,
                        titleLayout,
                        titleOrigin,
                        actorsLayout,
                        actorsOrigin,
                        dots,
                        task.Score,
                        GetSoftBrush(sectionIndex),
                        GetAccentBrush(sectionIndex)));

                    currentY = cardBounds.Bottom + taskSpacing;
                }

                sections.Add(new JourneySectionVisual(
                    sectionBounds,
                    sectionLayout,
                    sectionOrigin,
                    GetStrongSoftBrush(sectionIndex),
                    GetAccentBrush(sectionIndex),
                    tasks));

                currentY += sectionSpacing;
            }

            if (sections.Count > 0)
            {
                currentY -= sectionSpacing;
            }

            return new MermaidJourneySnapshot(new Size(width, Math.Max(currentY + padding, 220)), sections);
        }

        private static MermaidLayoutSnapshot BuildTimeline(
            MermaidTimelineDiagramDefinition timeline,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double entrySpacing = 14;
            const double sectionSpacing = 18;

            var contentWidth = Math.Max(width - (padding * 2), 280);
            var periodColumnWidth = Math.Clamp(contentWidth * 0.24, 100, 150);
            var spineX = padding + periodColumnWidth + 18;
            var cardX = spineX + 24;
            var cardWidth = Math.Max(width - cardX - padding, 160);

            var sectionVisuals = new List<TimelineSectionVisual>(timeline.Sections.Count);
            var entryVisuals = new List<TimelineEntryVisual>();
            var currentY = padding;
            var spineStart = default(Point);
            var spineEnd = default(Point);
            var hasEntries = false;

            for (var sectionIndex = 0; sectionIndex < timeline.Sections.Count; sectionIndex++)
            {
                var section = timeline.Sections[sectionIndex];
                var sectionLayout = CreateTextLayout(section.Name, typeface, fontSize + 0.5, foreground, contentWidth - 12, TextWrapping.Wrap);
                var sectionBounds = new Rect(padding, currentY, contentWidth, Math.Max(38, sectionLayout.Height + 16));
                var sectionOrigin = new Point(sectionBounds.X + 14, sectionBounds.Y + ((sectionBounds.Height - sectionLayout.Height) / 2));
                sectionVisuals.Add(new TimelineSectionVisual(sectionBounds, sectionLayout, sectionOrigin, GetStrongSoftBrush(sectionIndex), GetAccentBrush(sectionIndex)));
                currentY = sectionBounds.Bottom + 12;

                foreach (var entry in section.Entries)
                {
                    var periodLayout = CreateTextLayout(entry.Period, typeface, Math.Max(fontSize - 0.2, 11.5), foreground, periodColumnWidth, TextWrapping.Wrap);
                    var eventVisuals = new List<PositionedTextVisual>(entry.Events.Count);
                    var eventY = currentY + 10;
                    foreach (var eventText in entry.Events)
                    {
                        var eventLayout = CreateTextLayout($"- {eventText}", typeface, Math.Max(fontSize - 1, 11), foreground, cardWidth - 24, TextWrapping.Wrap);
                        eventVisuals.Add(new PositionedTextVisual(eventLayout, new Point(cardX + 12, eventY)));
                        eventY += eventLayout.Height + 6;
                    }

                    var cardHeight = Math.Max(44, (eventY - currentY) + 6);
                    var cardBounds = new Rect(cardX, currentY, cardWidth, cardHeight);
                    var periodBounds = new Rect(padding, currentY, periodColumnWidth, Math.Max(periodLayout.Height + 12, cardHeight));
                    var periodOrigin = new Point(periodBounds.Right - periodLayout.Width, periodBounds.Y + 8);
                    var markerCenter = new Point(spineX, currentY + Math.Min(cardHeight / 2, 18));

                    entryVisuals.Add(new TimelineEntryVisual(
                        periodBounds,
                        periodLayout,
                        periodOrigin,
                        markerCenter,
                        cardBounds,
                        eventVisuals,
                        GetSoftBrush(sectionIndex),
                        GetAccentBrush(sectionIndex)));

                    if (!hasEntries)
                    {
                        spineStart = markerCenter;
                        hasEntries = true;
                    }

                    spineEnd = markerCenter;
                    currentY = Math.Max(periodBounds.Bottom, cardBounds.Bottom) + entrySpacing;
                }

                currentY += sectionSpacing;
            }

            if (hasEntries)
            {
                currentY -= sectionSpacing;
            }

            return new MermaidTimelineSnapshot(
                new Size(width, Math.Max(currentY + padding, 220)),
                sectionVisuals,
                entryVisuals,
                spineStart,
                spineEnd);
        }

        private static MermaidLayoutSnapshot BuildQuadrantChart(
            MermaidQuadrantChartDiagramDefinition quadrantChart,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;

            var chartSize = Math.Clamp(width - (padding * 2), 260, 420);
            var chartX = padding + Math.Max((width - (padding * 2) - chartSize) / 2, 0);
            var chartY = padding + 12;
            var chartBounds = new Rect(chartX, chartY, chartSize, chartSize);
            var halfWidth = chartSize / 2;
            var halfHeight = chartSize / 2;

            var quadrants = new List<Rect>(4)
            {
                new(chartX + halfWidth, chartY, halfWidth, halfHeight),
                new(chartX, chartY, halfWidth, halfHeight),
                new(chartX, chartY + halfHeight, halfWidth, halfHeight),
                new(chartX + halfWidth, chartY + halfHeight, halfWidth, halfHeight)
            };
            var quadrantFills = new List<IBrush>(4)
            {
                GetSoftBrush(0),
                GetSoftBrush(1),
                GetSoftBrush(2),
                GetSoftBrush(3)
            };

            var quadrantLabels = new List<PositionedTextVisual>(4);
            for (var index = 0; index < quadrants.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(quadrantChart.QuadrantLabels[index]))
                {
                    continue;
                }

                var layout = CreateTextLayout(quadrantChart.QuadrantLabels[index], typeface, Math.Max(fontSize - 0.5, 11), foreground, quadrants[index].Width - 24, TextWrapping.Wrap);
                var origin = new Point(
                    quadrants[index].X + ((quadrants[index].Width - layout.Width) / 2),
                    quadrants[index].Y + 10);
                quadrantLabels.Add(new PositionedTextVisual(layout, origin));
            }

            var axisLabels = new List<PositionedTextVisual>(4);
            if (!string.IsNullOrWhiteSpace(quadrantChart.XLeftLabel))
            {
                var layout = CreateTextLayout(quadrantChart.XLeftLabel, typeface, Math.Max(fontSize - 1, 11), foreground, halfWidth - 12, TextWrapping.Wrap);
                axisLabels.Add(new PositionedTextVisual(layout, new Point(chartX + 8, chartBounds.Bottom + 10)));
            }

            if (!string.IsNullOrWhiteSpace(quadrantChart.XRightLabel))
            {
                var layout = CreateTextLayout(quadrantChart.XRightLabel, typeface, Math.Max(fontSize - 1, 11), foreground, halfWidth - 12, TextWrapping.Wrap);
                axisLabels.Add(new PositionedTextVisual(layout, new Point(chartX + halfWidth + 8, chartBounds.Bottom + 10)));
            }

            if (!string.IsNullOrWhiteSpace(quadrantChart.YTopLabel))
            {
                var layout = CreateTextLayout(quadrantChart.YTopLabel, typeface, Math.Max(fontSize - 1, 11), foreground, 120, TextWrapping.Wrap);
                axisLabels.Add(new PositionedTextVisual(layout, new Point(Math.Max(4, chartX - layout.Width - 8), chartY + 10)));
            }

            if (!string.IsNullOrWhiteSpace(quadrantChart.YBottomLabel))
            {
                var layout = CreateTextLayout(quadrantChart.YBottomLabel, typeface, Math.Max(fontSize - 1, 11), foreground, 120, TextWrapping.Wrap);
                axisLabels.Add(new PositionedTextVisual(layout, new Point(Math.Max(4, chartX - layout.Width - 8), chartY + halfHeight + 10)));
            }

            var pointVisuals = new List<QuadrantPointVisual>(quadrantChart.Points.Count);
            var contentBottom = chartBounds.Bottom;
            for (var index = 0; index < quadrantChart.Points.Count; index++)
            {
                var point = quadrantChart.Points[index];
                var centerX = chartBounds.X + (point.X * chartBounds.Width);
                var centerY = chartBounds.Bottom - (point.Y * chartBounds.Height);
                var bounds = new Rect(centerX - point.Radius, centerY - point.Radius, point.Radius * 2, point.Radius * 2);
                var labelLayout = CreateTextLayout(point.Label, typeface, Math.Max(fontSize - 1, 11), foreground, 120, TextWrapping.Wrap);
                var labelOrigin = new Point(
                    Math.Clamp(centerX - (labelLayout.Width / 2), padding, width - padding - labelLayout.Width),
                    centerY + point.Radius + 4);

                pointVisuals.Add(new QuadrantPointVisual(
                    bounds,
                    labelLayout,
                    labelOrigin,
                    point.FillColor is { } fillColor ? CreateBrush(fillColor) : GetAccentBrush(index),
                    point.StrokeColor is { } strokeColor ? CreateBrush(strokeColor) : NodeBorder,
                    point.StrokeWidth));

                contentBottom = Math.Max(contentBottom, labelOrigin.Y + labelLayout.Height);
            }

            foreach (var axisLabel in axisLabels)
            {
                contentBottom = Math.Max(contentBottom, axisLabel.Origin.Y + axisLabel.Text.Height);
            }

            return new MermaidQuadrantSnapshot(
                new Size(width, Math.Max(contentBottom + padding, chartBounds.Bottom + 48)),
                chartBounds,
                quadrants,
                quadrantFills,
                quadrantLabels,
                axisLabels,
                pointVisuals);
        }

        private static MermaidLayoutSnapshot BuildMindmap(
            MermaidMindmapDiagramDefinition mindmap,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            var nodes = new List<MermaidFlowNodeDefinition>();
            var edges = new List<MermaidFlowEdgeDefinition>();
            AppendMindmapNodes(mindmap.Root, nodes, edges);

            var flowchart = new MermaidFlowchartDiagramDefinition(
                mindmap.Source,
                MermaidFlowDirection.TopToBottom,
                nodes,
                edges);
            return BuildFlowchart(flowchart, width, typeface, fontSize, foreground);
        }

        private static MermaidLayoutSnapshot BuildErDiagram(
            MermaidErDiagramDefinition erDiagram,
            double width,
            Typeface typeface,
            double fontSize,
            IBrush foreground)
        {
            const double padding = 20;
            const double columnGap = 28;
            const double rowGap = 24;
            const double attributeSpacing = 6;

            var levelMap = ComputeGraphLevels(
                erDiagram.Entities.Select(static entity => entity.Id),
                erDiagram.Relationships.Select(static relation => new MermaidEdgeRef(relation.FromId, relation.ToId)));
            var groupedLevels = erDiagram.Entities
                .GroupBy(entity => levelMap[entity.Id])
                .OrderBy(group => group.Key)
                .Select(group => group.ToList())
                .ToList();

            var horizontal = erDiagram.Direction is MermaidFlowDirection.LeftToRight or MermaidFlowDirection.RightToLeft;
            var primaryCount = Math.Max(1, groupedLevels.Count);
            var secondaryCount = Math.Max(1, groupedLevels.Max(group => group.Count));
            var contentWidth = Math.Max(width - (padding * 2), 240);
            var cellPrimarySize = horizontal
                ? Math.Max((contentWidth - ((primaryCount - 1) * columnGap)) / primaryCount, 190)
                : Math.Max((contentWidth - ((secondaryCount - 1) * columnGap)) / secondaryCount, 190);
            var textMaxWidth = Math.Max(cellPrimarySize - 28, 120);

            var cards = new Dictionary<string, ErCardMetrics>(StringComparer.Ordinal);
            var maxCardHeight = 0d;
            foreach (var entity in erDiagram.Entities)
            {
                var titleLayout = CreateTextLayout(entity.Label, typeface, fontSize, foreground, textMaxWidth, TextWrapping.Wrap);
                var attributeLayouts = entity.Attributes
                    .Select(attribute => CreateTextLayout(FormatErAttribute(attribute), typeface, Math.Max(fontSize - 1, 11), foreground, textMaxWidth, TextWrapping.Wrap))
                    .ToList();
                var attributeWidth = attributeLayouts.Count == 0 ? 0 : attributeLayouts.Max(static layout => layout.Width);
                var contentMaxWidth = Math.Max(titleLayout.Width, attributeWidth);
                var cardWidth = Math.Min(cellPrimarySize, Math.Max(180, contentMaxWidth + 28));
                var headerHeight = Math.Max(46, titleLayout.Height + 18);
                var attributesHeight = attributeLayouts.Count == 0
                    ? 0
                    : attributeLayouts.Sum(static layout => layout.Height) + ((attributeLayouts.Count - 1) * attributeSpacing);
                var cardHeight = attributeLayouts.Count == 0
                    ? headerHeight + 14
                    : headerHeight + attributesHeight + 24;
                cards.Add(entity.Id, new ErCardMetrics(titleLayout, attributeLayouts, cardWidth, cardHeight, headerHeight));
                maxCardHeight = Math.Max(maxCardHeight, cardHeight);
            }

            var cellSecondarySize = Math.Max(maxCardHeight + rowGap, 150);
            var entityVisuals = new List<ErEntityVisual>(erDiagram.Entities.Count);
            var entityBounds = new Dictionary<string, Rect>(StringComparer.Ordinal);

            for (var levelIndex = 0; levelIndex < groupedLevels.Count; levelIndex++)
            {
                var group = groupedLevels[levelIndex];
                var primaryIndex = erDiagram.Direction switch
                {
                    MermaidFlowDirection.RightToLeft or MermaidFlowDirection.BottomToTop => groupedLevels.Count - 1 - levelIndex,
                    _ => levelIndex
                };

                var secondaryOffset = (secondaryCount - group.Count) / 2d;
                for (var entityIndex = 0; entityIndex < group.Count; entityIndex++)
                {
                    var entity = group[entityIndex];
                    var card = cards[entity.Id];

                    double x;
                    double y;
                    if (horizontal)
                    {
                        x = padding + (primaryIndex * (cellPrimarySize + columnGap)) + ((cellPrimarySize - card.Width) / 2);
                        y = padding + ((secondaryOffset + entityIndex) * cellSecondarySize) + ((cellSecondarySize - card.Height) / 2);
                    }
                    else
                    {
                        x = padding + ((secondaryOffset + entityIndex) * (cellPrimarySize + columnGap)) + ((cellPrimarySize - card.Width) / 2);
                        y = padding + (primaryIndex * (cellSecondarySize + rowGap));
                    }

                    var bounds = new Rect(x, y, card.Width, card.Height);
                    var headerBounds = new Rect(bounds.X, bounds.Y, bounds.Width, card.HeaderHeight);
                    var titleOrigin = new Point(bounds.X + ((bounds.Width - card.Title.Width) / 2), bounds.Y + ((card.HeaderHeight - card.Title.Height) / 2));
                    var separatorY = bounds.Y + card.HeaderHeight;
                    var attributeOriginY = separatorY + 8;
                    var attributes = new List<ErAttributeVisual>(card.Attributes.Count);
                    foreach (var attributeLayout in card.Attributes)
                    {
                        var origin = new Point(bounds.X + 12, attributeOriginY);
                        attributes.Add(new ErAttributeVisual(attributeLayout, origin));
                        attributeOriginY += attributeLayout.Height + attributeSpacing;
                    }

                    entityVisuals.Add(new ErEntityVisual(bounds, headerBounds, card.Title, titleOrigin, attributes, separatorY));
                    entityBounds.Add(entity.Id, bounds);
                }
            }

            var relationVisuals = new List<ErRelationVisual>(erDiagram.Relationships.Count);
            foreach (var relation in erDiagram.Relationships)
            {
                if (!entityBounds.TryGetValue(relation.FromId, out var fromBounds) || !entityBounds.TryGetValue(relation.ToId, out var toBounds))
                {
                    continue;
                }

                var (start, end) = ResolveConnectionPoints(fromBounds, toBounds);
                var startMarkerLayout = CreateTextLayout(ToErCardinalityLabel(relation.FromCardinality), typeface, Math.Max(fontSize - 2, 10.5), foreground, 42, TextWrapping.NoWrap);
                var endMarkerLayout = CreateTextLayout(ToErCardinalityLabel(relation.ToCardinality), typeface, Math.Max(fontSize - 2, 10.5), foreground, 42, TextWrapping.NoWrap);
                var (startMarkerOrigin, endMarkerOrigin) = ResolveCardinalityMarkerOrigins(start, end, startMarkerLayout, endMarkerLayout);

                TextLayout? labelLayout = null;
                Rect? labelBounds = null;
                if (!string.IsNullOrWhiteSpace(relation.Label))
                {
                    labelLayout = CreateTextLayout(relation.Label!, typeface, Math.Max(fontSize - 1, 11), foreground, 180, TextWrapping.Wrap);
                    var midX = (start.X + end.X) / 2;
                    var midY = (start.Y + end.Y) / 2;
                    labelBounds = new Rect(midX - (labelLayout.Width / 2) - 6, midY - labelLayout.Height - 8, labelLayout.Width + 12, labelLayout.Height + 6);
                }

                relationVisuals.Add(new ErRelationVisual(
                    start,
                    end,
                    new PositionedTextVisual(startMarkerLayout, startMarkerOrigin),
                    new PositionedTextVisual(endMarkerLayout, endMarkerOrigin),
                    labelLayout,
                    labelBounds,
                    Dotted: !relation.Identifying));
            }

            var totalHeight = horizontal
                ? padding + (secondaryCount * cellSecondarySize) + padding
                : padding + (groupedLevels.Count * (cellSecondarySize + rowGap)) + padding;
            return new MermaidErDiagramSnapshot(new Size(width, Math.Max(totalHeight, 220)), relationVisuals, entityVisuals);
        }

        private static void AppendMindmapNodes(
            MermaidMindmapNodeDefinition node,
            List<MermaidFlowNodeDefinition> nodes,
            List<MermaidFlowEdgeDefinition> edges)
        {
            nodes.Add(new MermaidFlowNodeDefinition(node.Id, node.Label, node.Shape));
            foreach (var child in node.Children)
            {
                edges.Add(new MermaidFlowEdgeDefinition(node.Id, child.Id, null, Dotted: false));
                AppendMindmapNodes(child, nodes, edges);
            }
        }

        private static string FormatErAttribute(MermaidErAttributeDefinition attribute)
        {
            var keyPrefix = string.IsNullOrWhiteSpace(attribute.Key) ? string.Empty : $"[{attribute.Key}] ";
            var commentSuffix = string.IsNullOrWhiteSpace(attribute.Comment) ? string.Empty : $" - {attribute.Comment}";
            return $"{keyPrefix}{attribute.Type} {attribute.Name}{commentSuffix}";
        }

        private static string ToErCardinalityLabel(string token)
        {
            return token switch
            {
                "|o" or "o|" => "0..1",
                "||" => "1",
                "}o" or "o{" => "0..*",
                "}|" or "|{" => "1..*",
                _ => token
            };
        }

        private static (Point StartOrigin, Point EndOrigin) ResolveCardinalityMarkerOrigins(
            Point start,
            Point end,
            TextLayout startLayout,
            TextLayout endLayout)
        {
            if (Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y))
            {
                var startX = end.X >= start.X ? start.X + 4 : start.X - startLayout.Width - 4;
                var endX = start.X >= end.X ? end.X + 4 : end.X - endLayout.Width - 4;
                var markerY = Math.Min(start.Y, end.Y) - Math.Max(startLayout.Height, endLayout.Height) - 4;
                return (new Point(startX, markerY), new Point(endX, markerY));
            }

            var markerX = Math.Max(start.X, end.X) + 6;
            var startY = end.Y >= start.Y ? start.Y + 4 : start.Y - startLayout.Height - 4;
            var endY = start.Y >= end.Y ? end.Y + 4 : end.Y - endLayout.Height - 4;
            return (new Point(markerX, startY), new Point(markerX, endY));
        }

        private static Dictionary<string, int> ComputeNodeLevels(MermaidFlowchartDiagramDefinition flowchart)
        {
            return ComputeGraphLevels(
                flowchart.Nodes.Select(static node => node.Id),
                flowchart.Edges.Select(static edge => new MermaidEdgeRef(edge.FromId, edge.ToId)));
        }

        private static Dictionary<string, int> ComputeGraphLevels(
            IEnumerable<string> nodeIds,
            IEnumerable<MermaidEdgeRef> edges)
        {
            var orderedNodeIds = nodeIds.Distinct(StringComparer.Ordinal).ToList();
            var levels = orderedNodeIds.ToDictionary(static id => id, static _ => 0, StringComparer.Ordinal);
            var incomingCounts = orderedNodeIds.ToDictionary(static id => id, static _ => 0, StringComparer.Ordinal);
            var outgoing = orderedNodeIds.ToDictionary(static id => id, static _ => new List<string>(), StringComparer.Ordinal);

            foreach (var edge in edges)
            {
                if (!incomingCounts.ContainsKey(edge.FromId) || !incomingCounts.ContainsKey(edge.ToId))
                {
                    continue;
                }

                incomingCounts[edge.ToId]++;
                outgoing[edge.FromId].Add(edge.ToId);
            }

            var queue = new Queue<string>(orderedNodeIds.Where(id => incomingCounts[id] == 0));
            if (queue.Count == 0 && orderedNodeIds.Count > 0)
            {
                queue.Enqueue(orderedNodeIds[0]);
            }

            var processed = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                if (!processed.Add(nodeId))
                {
                    continue;
                }

                foreach (var targetId in outgoing[nodeId])
                {
                    levels[targetId] = Math.Max(levels[targetId], levels[nodeId] + 1);
                    incomingCounts[targetId]--;
                    if (incomingCounts[targetId] <= 0)
                    {
                        queue.Enqueue(targetId);
                    }
                }
            }

            var nextLevel = levels.Values.DefaultIfEmpty(0).Max();
            foreach (var nodeId in orderedNodeIds)
            {
                if (processed.Contains(nodeId))
                {
                    continue;
                }

                nextLevel++;
                levels[nodeId] = nextLevel;
            }

            return levels;
        }

        private static Size ResolveFlowNodeSize(MermaidFlowNodeDefinition node, TextLayout textLayout, double cellPrimarySize)
        {
            if (node.Shape == MermaidNodeShape.Circle && string.IsNullOrWhiteSpace(node.Label))
            {
                var diameter = Math.Clamp(cellPrimarySize * 0.22, 22, 34);
                return new Size(diameter, diameter);
            }

            var minimumWidth = node.Shape == MermaidNodeShape.Circle ? 56 : 110;
            var minimumHeight = node.Shape == MermaidNodeShape.Circle ? 56 : 52;
            var width = Math.Min(cellPrimarySize, Math.Max(minimumWidth, textLayout.Width + 28));
            var height = Math.Max(minimumHeight, textLayout.Height + 20);
            return new Size(width, height);
        }

        private static Geometry CreateNodeGeometry(Rect bounds, MermaidNodeShape shape)
        {
            return shape switch
            {
                MermaidNodeShape.Rectangle => new RectangleGeometry(bounds, 8, 8),
                MermaidNodeShape.Diamond => CreateDiamondGeometry(bounds),
                MermaidNodeShape.Circle => new EllipseGeometry(bounds),
                _ => new RectangleGeometry(bounds, bounds.Height / 2, bounds.Height / 2)
            };
        }

        private static Geometry CreateDiamondGeometry(Rect bounds)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var top = new Point(bounds.X + (bounds.Width / 2), bounds.Y);
                var right = new Point(bounds.Right, bounds.Y + (bounds.Height / 2));
                var bottom = new Point(bounds.X + (bounds.Width / 2), bounds.Bottom);
                var left = new Point(bounds.X, bounds.Y + (bounds.Height / 2));
                context.BeginFigure(top, true);
                context.LineTo(right);
                context.LineTo(bottom);
                context.LineTo(left);
                context.EndFigure(true);
            }

            return geometry;
        }

        private static Geometry CreatePieSliceGeometry(Point center, double radius, double startAngle, double sweepAngle)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(center, true);
                var segmentCount = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepAngle) / 12));
                for (var index = 0; index <= segmentCount; index++)
                {
                    var angle = (startAngle + ((sweepAngle * index) / segmentCount)) * (Math.PI / 180);
                    var point = new Point(center.X + (Math.Cos(angle) * radius), center.Y + (Math.Sin(angle) * radius));
                    context.LineTo(point);
                }

                context.EndFigure(true);
            }

            return geometry;
        }

        private static (Point Start, Point End) ResolveConnectionPoints(Rect fromBounds, Rect toBounds)
        {
            var fromCenter = GetCenter(fromBounds);
            var toCenter = GetCenter(toBounds);
            var deltaX = toCenter.X - fromCenter.X;
            var deltaY = toCenter.Y - fromCenter.Y;

            if (Math.Abs(deltaX) >= Math.Abs(deltaY))
            {
                return deltaX >= 0
                    ? (new Point(fromBounds.Right, fromCenter.Y), new Point(toBounds.X, toCenter.Y))
                    : (new Point(fromBounds.X, fromCenter.Y), new Point(toBounds.Right, toCenter.Y));
            }

            return deltaY >= 0
                ? (new Point(fromCenter.X, fromBounds.Bottom), new Point(toCenter.X, toBounds.Y))
                : (new Point(fromCenter.X, fromBounds.Y), new Point(toCenter.X, toBounds.Bottom));
        }

        private static Geometry CreateArrowHead(Point tip, Point tail, double size)
        {
            var vector = tip - tail;
            var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
            if (length <= double.Epsilon)
            {
                return new StreamGeometry();
            }

            var direction = new Vector(vector.X / length, vector.Y / length);
            var normal = new Vector(-direction.Y, direction.X);
            var basePoint = tip - (direction * size);
            var left = basePoint + (normal * (size * 0.45));
            var right = basePoint - (normal * (size * 0.45));

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(tip, true);
                context.LineTo(left);
                context.LineTo(right);
                context.EndFigure(true);
            }

            return geometry;
        }

        private static Point GetCenter(Rect rect)
        {
            return new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        }

        private static TextLayout CreateTextLayout(
            string text,
            Typeface typeface,
            double fontSize,
            IBrush foreground,
            double maxWidth,
            TextWrapping wrapping)
        {
            return new TextLayout(
                text,
                typeface,
                fontSize,
                foreground,
                textWrapping: wrapping,
                maxWidth: Math.Max(maxWidth, 1));
        }
    }

    private sealed record FlowNodeVisual(Rect Bounds, Geometry Geometry, TextLayout Text, IBrush Fill, IBrush Stroke);

    private sealed record FlowEdgeVisual(Point Start, Point End, TextLayout? LabelLayout, Rect? LabelBounds, Geometry? ArrowHead, bool Dotted);

    private sealed record PieSliceVisual(
        Geometry Geometry,
        IBrush Fill,
        double StartAngle,
        double SweepAngle,
        Rect LegendBounds,
        Rect SwatchBounds,
        TextLayout LegendLayout,
        Point LegendOrigin);

    private sealed record SequenceParticipantVisual(Rect Bounds, double CenterX, TextLayout Label);

    private sealed record SequenceMessageVisual(
        Point Start,
        Point End,
        Geometry ArrowHead,
        TextLayout Label,
        Point LabelOrigin,
        bool Dotted,
        bool Emphasized);

    private sealed record ClassNodeVisual(
        Rect Bounds,
        Rect HeaderBounds,
        TextLayout Title,
        Point TitleOrigin,
        IReadOnlyList<ClassMemberVisual> Members,
        double SeparatorY);

    private sealed record ClassMemberVisual(TextLayout Text, Point Origin);

    private sealed record JourneySectionVisual(
        Rect Bounds,
        TextLayout Title,
        Point TitleOrigin,
        IBrush Fill,
        IBrush Stroke,
        IReadOnlyList<JourneyTaskVisual> Tasks);

    private sealed record JourneyTaskVisual(
        Rect Bounds,
        TextLayout Title,
        Point TitleOrigin,
        TextLayout Actors,
        Point ActorsOrigin,
        IReadOnlyList<Rect> ScoreDots,
        int Score,
        IBrush Fill,
        IBrush Stroke);

    private sealed record TimelineSectionVisual(
        Rect Bounds,
        TextLayout Title,
        Point TitleOrigin,
        IBrush Fill,
        IBrush Stroke);

    private sealed record TimelineEntryVisual(
        Rect PeriodBounds,
        TextLayout Period,
        Point PeriodOrigin,
        Point MarkerCenter,
        Rect CardBounds,
        IReadOnlyList<PositionedTextVisual> Events,
        IBrush Fill,
        IBrush Stroke);

    private sealed record QuadrantPointVisual(
        Rect Bounds,
        TextLayout Label,
        Point LabelOrigin,
        IBrush Fill,
        IBrush Stroke,
        double StrokeWidth);

    private sealed record PositionedTextVisual(TextLayout Text, Point Origin);

    private sealed record ErEntityVisual(
        Rect Bounds,
        Rect HeaderBounds,
        TextLayout Title,
        Point TitleOrigin,
        IReadOnlyList<ErAttributeVisual> Attributes,
        double SeparatorY);

    private sealed record ErAttributeVisual(TextLayout Text, Point Origin);

    private sealed record ErRelationVisual(
        Point Start,
        Point End,
        PositionedTextVisual StartMarker,
        PositionedTextVisual EndMarker,
        TextLayout? LabelLayout,
        Rect? LabelBounds,
        bool Dotted);

    private sealed record ClassCardMetrics(
        TextLayout Title,
        IReadOnlyList<TextLayout> Members,
        double Width,
        double Height,
        double HeaderHeight);

    private sealed record ErCardMetrics(
        TextLayout Title,
        IReadOnlyList<TextLayout> Attributes,
        double Width,
        double Height,
        double HeaderHeight);

    private readonly record struct MermaidEdgeRef(string FromId, string ToId);
}
