namespace CodexGui.Markdown.Services;

internal sealed class MarkdownEditHistory
{
    private readonly Stack<MarkdownEditTransaction> _undoStack = [];
    private readonly Stack<MarkdownEditTransaction> _redoStack = [];

    public MarkdownEditHistorySnapshot GetSnapshot()
    {
        return new MarkdownEditHistorySnapshot(_undoStack.Count, _redoStack.Count);
    }

    public void Record(MarkdownEditTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        _undoStack.Push(transaction);
        _redoStack.Clear();
    }

    public MarkdownEditTransaction? TryUndo()
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        var transaction = _undoStack.Pop();
        _redoStack.Push(transaction);
        return transaction;
    }

    public MarkdownEditTransaction? TryRedo()
    {
        if (_redoStack.Count == 0)
        {
            return null;
        }

        var transaction = _redoStack.Pop();
        _undoStack.Push(transaction);
        return transaction;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
