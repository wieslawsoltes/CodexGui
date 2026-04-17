using CodexGui.App.Services;
using Microsoft.UI.Dispatching;

namespace CodexGui.App.Uno.Services;

internal sealed class UnoUiDispatcher(DispatcherQueue dispatcherQueue) : IUiDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue = dispatcherQueue;

    public bool CheckAccess() => _dispatcherQueue.HasThreadAccess;

    public void Post(Action action) => Post(action, UiDispatchPriority.Normal);

    public void Post(Action action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(MapPriority(priority), () => action()))
        {
            throw new InvalidOperationException("Failed to enqueue work on the UI dispatcher.");
        }
    }

    public Task InvokeAsync(Action action) => InvokeAsync(action, UiDispatchPriority.Normal);

    public Task InvokeAsync(Action action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(MapPriority(priority), () =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("Failed to enqueue work on the UI dispatcher."));
        }

        return completion.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> action) => InvokeAsync(action, UiDispatchPriority.Normal);

    public Task<T> InvokeAsync<T>(Func<T> action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(MapPriority(priority), () =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("Failed to enqueue work on the UI dispatcher."));
        }

        return completion.Task;
    }

    private static DispatcherQueuePriority MapPriority(UiDispatchPriority priority)
    {
        return priority == UiDispatchPriority.Background
            ? DispatcherQueuePriority.Low
            : DispatcherQueuePriority.Normal;
    }
}
