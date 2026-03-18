using Avalonia.Input;

namespace CodexGui.Markdown.Services;

internal enum MarkdownKeyboardShortcutAction
{
    None,
    BeginEdit,
    OpenSlashCommands,
    SelectPreviousBlock,
    SelectNextBlock,
    MoveBlockUp,
    MoveBlockDown,
    DuplicateBlock,
    DeleteBlock,
    PromoteBlock,
    DemoteBlock,
    SplitBlock,
    JoinBlock,
    CancelInteraction
}

internal readonly record struct MarkdownKeyboardShortcutContext(bool IsEditing);

internal sealed class MarkdownKeyboardShortcutService
{
    public MarkdownKeyboardShortcutAction Resolve(KeyEventArgs eventArgs, MarkdownKeyboardShortcutContext context)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        var key = eventArgs.Key;
        var modifiers = eventArgs.KeyModifiers;
        var primary = HasPrimaryModifier(modifiers);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);

        if (key == Key.Escape)
        {
            return MarkdownKeyboardShortcutAction.CancelInteraction;
        }

        if (IsSlashKey(key) && modifiers == KeyModifiers.None)
        {
            return context.IsEditing
                ? MarkdownKeyboardShortcutAction.None
                : MarkdownKeyboardShortcutAction.OpenSlashCommands;
        }

        if (!context.IsEditing)
        {
            if (key is Key.Enter or Key.F2 && modifiers == KeyModifiers.None)
            {
                return MarkdownKeyboardShortcutAction.BeginEdit;
            }

            if (key == Key.Up && modifiers == KeyModifiers.None)
            {
                return MarkdownKeyboardShortcutAction.SelectPreviousBlock;
            }

            if (key == Key.Down && modifiers == KeyModifiers.None)
            {
                return MarkdownKeyboardShortcutAction.SelectNextBlock;
            }
        }

        if (alt && shift && key == Key.Up)
        {
            return MarkdownKeyboardShortcutAction.MoveBlockUp;
        }

        if (alt && shift && key == Key.Down)
        {
            return MarkdownKeyboardShortcutAction.MoveBlockDown;
        }

        if (alt && shift && key == Key.Left)
        {
            return MarkdownKeyboardShortcutAction.PromoteBlock;
        }

        if (alt && shift && key == Key.Right)
        {
            return MarkdownKeyboardShortcutAction.DemoteBlock;
        }

        if (primary && shift && key == Key.D)
        {
            return MarkdownKeyboardShortcutAction.DuplicateBlock;
        }

        if (primary && shift && key is Key.Delete or Key.Back)
        {
            return MarkdownKeyboardShortcutAction.DeleteBlock;
        }

        if (context.IsEditing && primary && shift && key == Key.Enter)
        {
            return MarkdownKeyboardShortcutAction.SplitBlock;
        }

        if (primary && key == Key.J)
        {
            return MarkdownKeyboardShortcutAction.JoinBlock;
        }

        return MarkdownKeyboardShortcutAction.None;
    }

    public static bool IsSlashKey(Key key) => key is Key.Oem2 or Key.Divide;

    public static bool HasPrimaryModifier(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
    }
}
