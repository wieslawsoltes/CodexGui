using CodexGui.Markdown.Core;
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
        registry.AddParserPlugin(new MermaidParserPlugin());
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
    protected MermaidBlockParserBase(MermaidBlockSyntax syntax, char[] openingCharacters)
    {
        Syntax = syntax;
        OpeningCharacters = openingCharacters;
        InfoPrefix = null;
    }

    protected MermaidBlockSyntax Syntax { get; }

    protected override MermaidDiagramBlock CreateFencedBlock(BlockProcessor processor)
    {
        return new MermaidDiagramBlock(this, Syntax);
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
