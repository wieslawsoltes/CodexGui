using System.ComponentModel;
using System.IO;
using CodexGui.Markdown.Controls;
using CodexGui.Markdown.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;

namespace CodexGui.Markdown.Sample.Uno;

public sealed partial class MainPage : Page
{
    private readonly DispatcherQueueTimer _previewTimer;
    private bool _isSyncingFromPreviewEdit;

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();

        _previewTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(150);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            CommitPreviewMarkdown();
        };

        Preview.EditorPreferences = ViewModel.PreviewEditorPreferences;
        Preview.MarkdownEdited += OnPreviewMarkdownEdited;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnMarkdownEditorTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_isSyncingFromPreviewEdit)
        {
            return;
        }

        if (!string.Equals(ViewModel.MarkdownText, MarkdownEditor.Text, StringComparison.Ordinal))
        {
            ViewModel.MarkdownText = MarkdownEditor.Text;
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnResetTextClicked(object sender, RoutedEventArgs args)
    {
        _previewTimer.Stop();
        ViewModel.ResetToSelectedDocument();
    }

    private void OnNewDocumentClicked(object sender, RoutedEventArgs args)
    {
        _previewTimer.Stop();
        ViewModel.ResetToSelectedDocument();
        ViewModel.StatusMessage = "Restored the selected sample document.";
    }

    private async void OnOpenFromPathClicked(object sender, RoutedEventArgs args)
    {
        var path = ViewModel.CurrentFilePath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            ViewModel.StatusMessage = "Enter a markdown file path before opening.";
            return;
        }

        if (!File.Exists(path))
        {
            ViewModel.StatusMessage = $"File not found: {path}";
            return;
        }

        var markdown = await File.ReadAllTextAsync(path);
        _previewTimer.Stop();
        _isSyncingFromPreviewEdit = true;
        try
        {
            ViewModel.LoadExternalDocument(path, markdown);
            if (!string.Equals(MarkdownEditor.Text, markdown, StringComparison.Ordinal))
            {
                MarkdownEditor.Text = markdown;
            }
        }
        finally
        {
            _isSyncingFromPreviewEdit = false;
        }
    }

    private async void OnSaveToPathClicked(object sender, RoutedEventArgs args)
    {
        var path = ViewModel.CurrentFilePath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            ViewModel.StatusMessage = "Enter a markdown file path before saving.";
            return;
        }

        await File.WriteAllTextAsync(path, MarkdownEditor.Text ?? string.Empty);
        ViewModel.MarkSaved(path);
    }

    private void CommitPreviewMarkdown()
    {
        if (!string.Equals(ViewModel.PreviewMarkdownText, MarkdownEditor.Text, StringComparison.Ordinal))
        {
            ViewModel.PreviewMarkdownText = MarkdownEditor.Text;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainPageViewModel.PreviewEditorPreferences))
        {
            Preview.EditorPreferences = ViewModel.PreviewEditorPreferences;
        }
    }

    private void OnPreviewMarkdownEdited(object? sender, MarkdownEditedEventArgs args)
    {
        _previewTimer.Stop();
        _isSyncingFromPreviewEdit = true;
        try
        {
            ViewModel.MarkdownText = args.UpdatedMarkdown;
            ViewModel.PreviewMarkdownText = args.UpdatedMarkdown;
            if (!string.Equals(MarkdownEditor.Text, args.UpdatedMarkdown, StringComparison.Ordinal))
            {
                MarkdownEditor.Text = args.UpdatedMarkdown;
            }
        }
        finally
        {
            _isSyncingFromPreviewEdit = false;
        }

        RevealEditorRange(args.RevealStart, args.RevealLength);
        ClearPreviewHover();
    }

    private void OnPreviewHostPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (Preview.ActiveEditorSession is not null)
        {
            ClearPreviewHover();
            return;
        }

        var point = args.GetCurrentPoint(Preview).Position;
        if (Preview.HitTestMarkdown(point) is not { } hit)
        {
            ClearPreviewHover();
            return;
        }

        ShowPreviewOverlay(hit);
    }

    private void OnPreviewHostPointerExited(object sender, PointerRoutedEventArgs args)
    {
        ClearPreviewHover();
    }

    private void OnPreviewHostPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(PreviewHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = args.GetCurrentPoint(Preview).Position;
        if (ViewModel.IsEditingEnabled && Preview.TryBeginEdit(point))
        {
            ClearPreviewHover();
            args.Handled = true;
            return;
        }

        if (Preview.HitTestMarkdown(point) is { } hit)
        {
            ShowPreviewOverlay(hit);
            RevealEditorSourceSpan(hit.SourceSpan);
        }
    }

    private void ShowPreviewOverlay(MarkdownHitRegion hit)
    {
        Canvas.SetLeft(PreviewOverlayBorder, hit.X);
        Canvas.SetTop(PreviewOverlayBorder, hit.Y);
        PreviewOverlayBorder.Width = Math.Max(hit.Width, 1);
        PreviewOverlayBorder.Height = Math.Max(hit.Height, 1);
        PreviewOverlayBorder.Visibility = Visibility.Visible;
    }

    private void ClearPreviewHover()
    {
        PreviewOverlayBorder.Visibility = Visibility.Collapsed;
        PreviewOverlayBorder.Width = 0;
        PreviewOverlayBorder.Height = 0;
    }

    private void RevealEditorSourceSpan(MarkdownSourceSpan sourceSpan)
    {
        if (!TryResolveEditorRange(sourceSpan, out var start, out var length))
        {
            return;
        }

        RevealEditorRange(start, length);
    }

    private void RevealEditorRange(int start, int length)
    {
        var documentLength = MarkdownEditor.Text?.Length ?? 0;
        start = Math.Clamp(start, 0, documentLength);
        length = Math.Clamp(length, 0, Math.Max(documentLength - start, 0));

        MarkdownEditor.SelectionStart = start;
        MarkdownEditor.SelectionLength = length;
        MarkdownEditor.Focus(FocusState.Programmatic);
    }

    private bool TryResolveEditorRange(MarkdownSourceSpan sourceSpan, out int start, out int length)
    {
        start = 0;
        length = 0;

        var documentLength = MarkdownEditor.Text?.Length ?? 0;
        if (sourceSpan.IsEmpty || documentLength <= 0 || sourceSpan.Start >= documentLength)
        {
            return false;
        }

        start = Math.Max(sourceSpan.Start, 0);
        length = Math.Min(sourceSpan.Length, documentLength - start);
        return length > 0;
    }
}
