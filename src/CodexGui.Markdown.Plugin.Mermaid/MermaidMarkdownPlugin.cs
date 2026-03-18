using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Mermaid;

public sealed class MermaidMarkdownPlugin : IMarkdownPlugin
{
    public const string MermaidEditorId = "mermaid-diagram-editor";

    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddParserPlugin(new MermaidParserPlugin())
            .AddBlockRenderingPlugin(new MermaidDiagramBlockRenderingPlugin())
            .AddBlockTemplateProvider(new MermaidBlockTemplateProvider())
            .AddEditorPlugin(new MermaidDiagramEditorPlugin());
    }
}

internal sealed class MermaidBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => -50;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "mermaid-diagram-block",
            "Mermaid diagram",
            MarkdownEditorFeature.Mermaid,
            MermaidDiagramEditorPlugin.BuildMermaidBlock(
                "flowchart TD\n    Start[Start] --> Decide{Choice}\n    Decide -->|Yes| Ship[Ship it]\n    Decide -->|No| Revise[Revise]",
                MermaidBlockSyntax.CodeFence,
                string.Empty),
            "Insert a Mermaid diagram block.");

        yield return new MarkdownBlockTemplate(
            "mermaid-diagram-container",
            "Mermaid container",
            MarkdownEditorFeature.Mermaid,
            MermaidDiagramEditorPlugin.BuildMermaidBlock(
                "sequenceDiagram\n    participant User\n    participant App\n    User->>App: Request preview\n    App-->>User: Rendered diagram",
                MermaidBlockSyntax.CustomContainer,
                string.Empty),
            "Insert a Mermaid diagram that preserves :::diagram mermaid container syntax.");
    }
}

internal sealed class MermaidParserPlugin : IMarkdownParserPlugin
{
    public int Order => -100;

    public void Configure(MarkdownPipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Extensions.AddIfNotAlready<MermaidParsingExtension>(new MermaidParsingExtension());
    }
}

internal sealed class MermaidParsingExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        pipeline.BlockParsers.InsertBefore<FencedCodeBlockParser>(new MermaidFencedBlockParser());

        var containerParser = new MermaidContainerParser();
        if (!pipeline.BlockParsers.InsertBefore<CustomContainerParser>(containerParser))
        {
            pipeline.BlockParsers.InsertBefore<ParagraphBlockParser>(containerParser);
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

internal enum MermaidBlockSyntax
{
    CodeFence,
    CustomContainer
}

internal sealed class MermaidDiagramBlock : FencedCodeBlock
{
    public MermaidDiagramBlock(BlockParser parser, MermaidBlockSyntax syntax)
        : base(parser)
    {
        Syntax = syntax;
    }

    public MermaidBlockSyntax Syntax { get; }

    public string NormalizedInfo { get; private set; } = "mermaid";

    public string DiagramArguments { get; private set; } = string.Empty;

    public bool TryInitializeDescriptor()
    {
        if (!MermaidSyntax.TryParseDescriptor(Info, Arguments, out var normalizedInfo, out var normalizedArguments))
        {
            return false;
        }

        NormalizedInfo = normalizedInfo;
        DiagramArguments = normalizedArguments;
        return true;
    }
}

internal abstract class MermaidBlockParserBase : FencedBlockParserBase<MermaidDiagramBlock>
{
    private readonly MermaidBlockSyntax _syntax;

    protected MermaidBlockParserBase(MermaidBlockSyntax syntax, char[] openingCharacters)
    {
        _syntax = syntax;
        OpeningCharacters = openingCharacters;
        InfoPrefix = null;
    }

    protected override MermaidDiagramBlock CreateFencedBlock(BlockProcessor processor)
    {
        return new MermaidDiagramBlock(this, _syntax);
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        var result = base.TryOpen(processor);
        if (result == BlockState.None)
        {
            return result;
        }

        if (processor.NewBlocks.Count == 0 || processor.NewBlocks.Peek() is not MermaidDiagramBlock mermaidBlock)
        {
            return BlockState.None;
        }

        if (mermaidBlock.TryInitializeDescriptor())
        {
            return result;
        }

        processor.NewBlocks.Pop();
        return BlockState.None;
    }
}

internal sealed class MermaidFencedBlockParser : MermaidBlockParserBase
{
    public MermaidFencedBlockParser()
        : base(MermaidBlockSyntax.CodeFence, ['`', '~'])
    {
    }
}

internal sealed class MermaidContainerParser : MermaidBlockParserBase
{
    public MermaidContainerParser()
        : base(MermaidBlockSyntax.CustomContainer, [':'])
    {
    }
}

internal static class MermaidSyntax
{
    private static readonly HashSet<string> MermaidLanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "diagram-mermaid",
        "mmd",
        "mermaid",
        "mermaidjs"
    };

    public static bool TryParseDescriptor(
        string? info,
        string? arguments,
        out string normalizedInfo,
        out string normalizedArguments)
    {
        normalizedInfo = "mermaid";
        normalizedArguments = string.Empty;

        var normalizedDescriptor = NormalizeDescriptor(info);
        if (IsMermaidLanguage(normalizedDescriptor))
        {
            normalizedArguments = arguments?.Trim() ?? string.Empty;
            return true;
        }

        if (!string.Equals(normalizedDescriptor, "diagram", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var trimmedArguments = arguments?.Trim() ?? string.Empty;
        if (trimmedArguments.Length == 0)
        {
            return false;
        }

        var separatorIndex = trimmedArguments.IndexOfAny([' ', '\t']);
        var firstToken = separatorIndex >= 0 ? trimmedArguments[..separatorIndex] : trimmedArguments;
        if (!IsMermaidLanguage(firstToken))
        {
            return false;
        }

        normalizedArguments = separatorIndex >= 0
            ? trimmedArguments[(separatorIndex + 1)..].TrimStart()
            : string.Empty;
        return true;
    }

    public static string NormalizeCode(string source)
    {
        return source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
    }

    public static string NormalizeArguments(string? arguments)
    {
        return arguments?.Trim() ?? string.Empty;
    }

    private static bool IsMermaidLanguage(string? languageHint)
    {
        return MermaidLanguageAliases.Contains(NormalizeDescriptor(languageHint));
    }

    private static string NormalizeDescriptor(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return string.Empty;
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }
}

internal sealed class MermaidDiagramBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -100;

    public bool CanRender(Block block)
    {
        return block is MermaidDiagramBlock;
    }

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        if (context.Block is not MermaidDiagramBlock mermaidBlock)
        {
            return false;
        }

        var diagramSource = MermaidSyntax.NormalizeCode(mermaidBlock.Lines.ToString());
        var renderedDiagram = MermaidDiagramViewFactory.Create(diagramSource, context.AvailableWidth, context.RenderContext);
        context.AddBlockControl(renderedDiagram.Control, renderedDiagram.HitTestHandler);
        return true;
    }
}

internal sealed class MermaidDiagramEditorPlugin : IMarkdownEditorPlugin
{
    private static readonly MermaidBlockSyntaxOption[] SyntaxOptions =
    [
        new(MermaidBlockSyntax.CodeFence, "Code fence", "Preserve ```mermaid fenced syntax for standard markdown documents."),
        new(MermaidBlockSyntax.CustomContainer, "Container", "Preserve :::diagram mermaid container syntax for grouped layouts.")
    ];

    public string EditorId => MermaidMarkdownPlugin.MermaidEditorId;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Mermaid;

    public int Order => -100;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<MermaidDiagramBlock>(out var mermaidBlock, out var depth) || mermaidBlock is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(mermaidBlock.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(mermaidBlock, sourceSpan, mermaidBlock.Line, mermaidBlock.Column),
            depth,
            "Mermaid diagram");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        if (context.Node is not MermaidDiagramBlock mermaidBlock)
        {
            return null;
        }

        var sourceText = MermaidSyntax.NormalizeCode(mermaidBlock.Lines.ToString());
        var syntaxComboBox = new ComboBox
        {
            ItemsSource = SyntaxOptions,
            SelectedItem = ResolveSyntaxOption(mermaidBlock.Syntax),
            MinWidth = 180,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Background = MarkdownEditorUiFactory.InputBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6)
        };
        var syntaxDescription = MarkdownEditorUiFactory.CreateInfoText(((MermaidBlockSyntaxOption?)syntaxComboBox.SelectedItem)?.Description ?? "Mermaid diagram");
        var argumentsTextBox = MarkdownEditorUiFactory.CreateTextEditor(mermaidBlock.DiagramArguments, acceptsReturn: false, minHeight: 42);
        MarkdownEditorUiFactory.ApplyInlineMetadataStyle(argumentsTextBox, context.RenderContext.FontSize);
        var textBox = MarkdownEditorUiFactory.CreateCodeEditor(sourceText);
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineCodeStyle(textBox, context.RenderContext.FontSize);
        }
        else
        {
            argumentsTextBox.FontSize = Math.Max(context.RenderContext.FontSize - 1, 12);
            argumentsTextBox.FontFamily = context.RenderContext.FontFamily;
            argumentsTextBox.Foreground = context.RenderContext.Foreground ?? MarkdownEditorUiFactory.EditorForeground;
        }

        var previewHost = new Border
        {
            Background = MarkdownEditorUiFactory.SectionBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8)
        };

        void UpdatePreview()
        {
            syntaxDescription.Text = ((MermaidBlockSyntaxOption?)syntaxComboBox.SelectedItem)?.Description ?? "Mermaid diagram";
            var renderedDiagram = MermaidDiagramViewFactory.Create(
                MermaidSyntax.NormalizeCode(textBox.Text ?? string.Empty),
                context.AvailableWidth,
                context.RenderContext);
            previewHost.Child = renderedDiagram.Control;
        }

        syntaxComboBox.SelectionChanged += (_, _) => UpdatePreview();
        argumentsTextBox.TextChanged += (_, _) => UpdatePreview();
        textBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit Mermaid source, preserve the preferred block syntax, and review the live native preview before applying changes."));
        }

        string BuildCurrentMarkdown() => BuildMermaidBlock(
            textBox.Text ?? string.Empty,
            ((MermaidBlockSyntaxOption?)syntaxComboBox.SelectedItem)?.Syntax ?? mermaidBlock.Syntax,
            argumentsTextBox.Text);

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Block syntax"));
        body.Children.Add(syntaxComboBox);
        body.Children.Add(syntaxDescription);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Descriptor arguments"));
        body.Children.Add(argumentsTextBox);
        body.Children.Add(textBox);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit Mermaid diagram",
            "Mermaid editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            textBox,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }

    internal static string BuildMermaidBlock(string source, MermaidBlockSyntax syntax, string? arguments)
    {
        var normalized = MermaidSyntax.NormalizeCode(source);
        var normalizedArguments = MermaidSyntax.NormalizeArguments(arguments);
        return syntax switch
        {
            MermaidBlockSyntax.CustomContainer => BuildMermaidContainer(normalized, normalizedArguments),
            _ => BuildMermaidFence(normalized, normalizedArguments)
        };
    }

    private static MermaidBlockSyntaxOption ResolveSyntaxOption(MermaidBlockSyntax syntax)
    {
        return SyntaxOptions.FirstOrDefault(option => option.Syntax == syntax) ?? SyntaxOptions[0];
    }

    private static string BuildMermaidFence(string normalizedSource, string normalizedArguments)
    {
        var fence = normalizedSource.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
        var descriptor = normalizedArguments.Length == 0 ? "mermaid" : $"mermaid {normalizedArguments}";
        return $"{fence}{descriptor}\n{normalizedSource}\n{fence}";
    }

    private static string BuildMermaidContainer(string normalizedSource, string normalizedArguments)
    {
        var fence = new string(':', ResolveContainerFenceLength(normalizedSource, preferredFenceLength: 3));
        var descriptor = normalizedArguments.Length == 0 ? "diagram mermaid" : $"diagram mermaid {normalizedArguments}";
        return $"{fence}{descriptor}\n{normalizedSource}\n{fence}";
    }

    private static int ResolveContainerFenceLength(string normalizedSource, int preferredFenceLength)
    {
        var requiredFenceLength = Math.Max(preferredFenceLength, 3);
        if (normalizedSource.Length == 0)
        {
            return requiredFenceLength;
        }

        foreach (var line in MermaidSyntax.NormalizeCode(normalizedSource).Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimStart();
            var colonCount = 0;
            while (colonCount < trimmed.Length && trimmed[colonCount] == ':')
            {
                colonCount++;
            }

            if (colonCount >= requiredFenceLength)
            {
                requiredFenceLength = colonCount + 1;
            }
        }

        return requiredFenceLength;
    }

    private sealed record MermaidBlockSyntaxOption(MermaidBlockSyntax Syntax, string Label, string Description)
    {
        public override string ToString() => Label;
    }
}
