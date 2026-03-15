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
            MermaidDiagramEditorPlugin.BuildMermaidFence("flowchart TD\n    Start[Start] --> Decide{Choice}\n    Decide -->|Yes| Ship[Ship it]\n    Decide -->|No| Revise[Revise]"),
            "Insert a Mermaid diagram block.");
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
        var textBox = MarkdownEditorUiFactory.CreateCodeEditor(sourceText);
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineCodeStyle(textBox, context.RenderContext.FontSize);
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
            var renderedDiagram = MermaidDiagramViewFactory.Create(
                MermaidSyntax.NormalizeCode(textBox.Text ?? string.Empty),
                context.AvailableWidth,
                context.RenderContext);
            previewHost.Child = renderedDiagram.Control;
        }

        textBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit Mermaid source and review the live native preview below before applying the fenced diagram block."));
        }

        string BuildCurrentMarkdown() => BuildMermaidFence(textBox.Text ?? string.Empty);

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

    internal static string BuildMermaidFence(string source)
    {
        var normalized = MermaidSyntax.NormalizeCode(source);
        var fence = normalized.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
        return $"{fence}mermaid\n{normalized}\n{fence}";
    }
}
