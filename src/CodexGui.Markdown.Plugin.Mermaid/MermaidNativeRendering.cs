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
                        Text = "Native Mermaid preview currently supports flowcharts, sequence diagrams, state diagrams, and class diagrams. The source remains visible below.",
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
    ClassDiagram
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
        var lines = normalized
            .Split('\n', StringSplitOptions.None)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("%%", StringComparison.Ordinal))
            .ToList();

        if (lines.Count == 0)
        {
            return new MermaidUnsupportedDiagramDefinition(normalized, "The Mermaid source is empty.");
        }

        var header = lines[0];
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

    private static string StripComment(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("%%", StringComparison.Ordinal) ? string.Empty : trimmed;
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

    private readonly record struct MermaidClassRelationPattern(string Token, bool Reverse, bool Dotted, bool Directed);

    private readonly record struct MermaidParsedFlowNode(string Id, string Label, MermaidNodeShape Shape, bool HasExplicitLabel);
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

    private sealed record ClassCardMetrics(
        TextLayout Title,
        IReadOnlyList<TextLayout> Members,
        double Width,
        double Height,
        double HeaderHeight);

    private readonly record struct MermaidEdgeRef(string FromId, string ToId);
}
