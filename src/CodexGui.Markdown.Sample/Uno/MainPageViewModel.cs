using CommunityToolkit.Mvvm.ComponentModel;
using CodexGui.Markdown.Core;
using CodexGui.Markdown.Controls;
using CodexGui.Markdown.Plugin.TextMate;
using System.IO;

namespace CodexGui.Markdown.Sample.Uno;

public sealed partial class MainPageViewModel : ObservableObject
{
    public MainPageViewModel()
    {
        Documents =
        [
            new SampleDocument("Showcase", "Full plugin matrix including alerts, definition lists, figures, footers, math, mermaid, tables, and highlighted fenced code blocks.", ShowcaseMarkdown),
            new SampleDocument("Shell Transcript", "A compact Codex shell-style document that stresses quotes, lists, tables, code fences, and links in a denser layout.", ShellTranscriptMarkdown),
            new SampleDocument("Reference Notes", "A smaller reference document focused on footers, custom containers, glossary blocks, and inline math.", ReferenceMarkdown)
        ];
        CodeEditorOptions =
        [
            new CodeEditorOption("Built-in markdown code editor", MarkdownBuiltInEditorIds.Code),
            new CodeEditorOption("TextMate code editor", TextMateMarkdownPlugin.TextMateCodeEditorId)
        ];

        selectedDocument = Documents[0];
        markdownText = selectedDocument.Markdown;
        previewMarkdownText = selectedDocument.Markdown;
        selectedCodeEditor = CodeEditorOptions[1];
        previewEditorPreferences = new MarkdownEditorPreferences()
            .PreferEditor(MarkdownEditorFeature.Code, selectedCodeEditor.EditorId);
    }

    public IReadOnlyList<SampleDocument> Documents { get; }

    public IReadOnlyList<CodeEditorOption> CodeEditorOptions { get; }

    public IReadOnlyList<MarkdownEditorPresentationMode> PresentationModes { get; } =
    [
        MarkdownEditorPresentationMode.Inline,
        MarkdownEditorPresentationMode.Card
    ];

    public string CurrentFileDisplay =>
        string.IsNullOrWhiteSpace(CurrentFilePath)
            ? "Unsaved sample"
            : Path.GetFileName(CurrentFilePath);

    public string DocumentCountLabel => $"{Documents.Count} sample documents";

    public string SelectedDocumentDescription => SelectedDocument.Description;

    [ObservableProperty]
    private SampleDocument selectedDocument;

    [ObservableProperty]
    private string markdownText;

    [ObservableProperty]
    private string previewMarkdownText;

    [ObservableProperty]
    private MarkdownEditorPreferences previewEditorPreferences;

    [ObservableProperty]
    private Uri? baseUri = new("https://platform.uno/");

    [ObservableProperty]
    private string currentFilePath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Edit a sample, click the preview to reveal source spans, or open a markdown file by path.";

    [ObservableProperty]
    private bool isEditingEnabled = true;

    [ObservableProperty]
    private MarkdownEditorPresentationMode editorPresentationMode = MarkdownEditorPresentationMode.Card;

    [ObservableProperty]
    private CodeEditorOption selectedCodeEditor;

    partial void OnSelectedDocumentChanged(SampleDocument value)
    {
        SetCurrentFilePath(null);
        MarkdownText = value.Markdown;
        PreviewMarkdownText = value.Markdown;
        OnPropertyChanged(nameof(SelectedDocumentDescription));
    }

    public void ResetToSelectedDocument()
    {
        SetCurrentFilePath(null);
        MarkdownText = SelectedDocument.Markdown;
        PreviewMarkdownText = SelectedDocument.Markdown;
    }

    partial void OnSelectedCodeEditorChanged(CodeEditorOption value)
    {
        PreviewEditorPreferences = new MarkdownEditorPreferences()
            .PreferEditor(MarkdownEditorFeature.Code, value.EditorId);
    }

    public void LoadExternalDocument(string path, string markdown)
    {
        SetCurrentFilePath(path);
        MarkdownText = markdown;
        PreviewMarkdownText = markdown;
        StatusMessage = $"Loaded {Path.GetFileName(path)}";
    }

    public void MarkSaved(string path)
    {
        SetCurrentFilePath(path);
        StatusMessage = $"Saved {Path.GetFileName(path)}";
    }

    public void SetCurrentFilePath(string? path)
    {
        CurrentFilePath = path?.Trim() ?? string.Empty;
        BaseUri = string.IsNullOrWhiteSpace(CurrentFilePath)
            ? new Uri("https://platform.uno/")
            : CreateDirectoryUri(CurrentFilePath) ?? new Uri("https://platform.uno/");
        OnPropertyChanged(nameof(CurrentFileDisplay));
    }

    private static Uri? CreateDirectoryUri(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var normalized = directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        return new Uri(normalized);
    }

    public sealed record SampleDocument(string Title, string Description, string Markdown);

    public sealed record CodeEditorOption(string Label, string EditorId)
    {
        public override string ToString() => Label;
    }

    private const string ShowcaseMarkdown =
        """
        ---
        title: CodexGui Markdown Control Showcase
        owner: uno-preview
        summary: Rich markdown rendering, plugin parity, and Pretext-backed layout in Uno Platform.
        ---

        # CodexGui Markdown Control Showcase

        Edit markdown on the **left** and inspect the rendered Uno output on the **right**.

        ## Features in this sample

        - Live markdown preview updates as you type.
        - TextMate-backed syntax highlighting for fenced code blocks when a grammar is available.
        - Plugin-driven block rendering for Mermaid diagrams, alerts, custom containers, figures, definition lists, footers, and math.
        - Shared markdown parsing, IR generation, source-span tracking, and Pretext layout.
        - Table, quote, heading, inline code, link, and nested markdown rendering.

        ## Links and footnotes

        Read the [Uno Platform documentation][uno-docs] or the [Markdig repository](https://github.com/xoofx/markdig) for parser details.[^layout]

        [^layout]: Layout metrics and hit regions are computed from the shared markdown core before Uno elements are materialized.

        [uno-docs]: https://platform.uno/docs/ "Uno Platform documentation"

        *[API]: Application programming interface

        ## Figures

        ^^^ Uno shell surface
        Supporting markdown can live inside a figure body without getting mixed into the opening or closing fence captions.

        - lead captions behave like a figure title
        - closing captions behave like supporting notes

        ```json
        {
          "feature": "figures",
          "renderer": "uno"
        }
        ```
        ^^^ Figure captions stay independent from the body markdown.

        ## Alerts and custom containers

        > [!IMPORTANT]
        > Alert blocks now route through the shared markdown core and render as Uno surfaces.
        >
        > - parser semantics stay intact
        > - nested markdown remains renderable

        :::info Renderer status
        Custom containers keep the type token, optional arguments, and markdown body separated in the IR.
        :::

        ## Footer

        ^^ Footer blocks keep the `^^` source marker out of the rendered body while preserving source spans for editing workflows.

        ## Definitions and math

        Rendering engine
        :   The renderer maps Markdig extension nodes into a reusable markdown IR instead of flattening them into plain text.
        :   Definitions can span multiple paragraphs or nested blocks.

        AST
        :   A structured model used for rich rendering and source-span-aware editing.

        Inline math such as $a^2 + b^2 = c^2$ stays readable in flow.

        API notes now render as abbreviation-aware inline text. ==Marked text==, ++inserted text++, x^^2^^, and H~2~O preserve their extra semantics in the migrated renderer.

        Emoji shortcodes like :sparkles: and smileys like :-) pass through the same parsing pipeline as the original surface.

        $$
        \int_0^1 x^2\,dx = \frac{1}{3}
        $$

        ## Mermaid diagram

        :::mermaid
        flowchart LR
            Editor[Markdown source]
            Parser[Markdig plus plugins]
            IR[Shared markdown IR]
            Preview[Uno preview]

            Editor --> Parser --> IR --> Preview
        :::

        ## Mermaid fenced syntax

        ```mermaid
        sequenceDiagram
            participant User
            participant Core
            participant Uno

            User->>Core: Type markdown
            Core->>Uno: Produce IR and layout
            Uno-->>User: Render preview
        ```

        ## Code fence

        ```csharp
        var state = new Dictionary<string, int>
        {
            ["alerts"] = 1,
            ["definition-lists"] = 1,
            ["mermaid"] = 2,
            ["code-fences"] = 3
        };

        Console.WriteLine($"Active markdown features: {string.Join(", ", state.Keys)}");
        ```

        ```postgresql
        select feature_name, status
        from markdown_features
        where renderer = 'uno'
        order by feature_name;
        ```

        ## Table

        | Feature         | Status | Notes |
        | :-------------- | :----: | ----: |
        | Preview surface | Ready  | Rich blocks stay constrained to the available width. |
        | Advanced nodes  | Ready  | Alerts, figures, containers, footers, definition lists, math, and mermaid render through the shared core. |
        | Code fences     | Ready  | TextMate grammars run first and the built-in tokenizer stays as fallback. |
        """;

    private const string ShellTranscriptMarkdown =
        """
        # Shell Transcript

        > [!TIP]
        > This document is closer to the Codex shell UI shape: dense text, quotes, tool output, and short code fences.

        ## Session timeline

        1. Connect to the local app-server.
        2. Load threads and session metadata.
        3. Select a thread and stream conversation items.
        4. Render markdown bodies through the Uno markdown control.

        ## Command output

        ```bash
        dotnet build CodexGui.slnx
        dotnet test tests/CodexGui.Markdown.Core.Tests/CodexGui.Markdown.Core.Tests.csproj
        ```

        ## Status table

        | Surface | State | Note |
        | --- | --- | --- |
        | Shell chrome | migrated | Uno XAML + semantic tones |
        | Markdown preview | migrated | shared IR + Pretext layout |
        | Code highlighting | migrated | TextMate first, lightweight fallback second |

        ## Definition list

        Session list
        :   Left rail that tracks thread summaries and status.

        Conversation view
        :   Primary reading surface that hosts markdown bodies and approval cards.
        """;

    private const string ReferenceMarkdown =
        """
        # Reference Notes

        :::warning Pretext package note
        The published NuGet surface currently exposes `PrepareWithSegments` and `LayoutWithLines`, so the Uno renderer uses those APIs for deterministic layout work today.
        :::

        ## Inline details

        - Links like [Uno Platform](https://platform.uno/) resolve through `BaseUri`.
        - Inline code such as `MarkdownLayoutService` keeps monospace styling.
        - Tables and nested blocks use recursive Uno materialization.

        ## Footer

        ^^ Reference surfaces keep the closing notes visually separated without losing source-span ownership.
        """;
}
