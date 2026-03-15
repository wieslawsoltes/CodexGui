using Avalonia.Threading;

namespace CodexGui.App.Services;

internal interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    void Post(Action action, DispatcherPriority priority);

    Task InvokeAsync(Action action);

    Task InvokeAsync(Action action, DispatcherPriority priority);

    Task<T> InvokeAsync<T>(Func<T> action);

    Task<T> InvokeAsync<T>(Func<T> action, DispatcherPriority priority);
}

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action)
        => Dispatcher.UIThread.Post(action, DispatcherPriority.Normal);

    public void Post(Action action, DispatcherPriority priority)
        => Dispatcher.UIThread.Post(action, priority);

    public Task InvokeAsync(Action action)
        => Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal).GetTask();

    public Task InvokeAsync(Action action, DispatcherPriority priority)
        => Dispatcher.UIThread.InvokeAsync(action, priority).GetTask();

    public Task<T> InvokeAsync<T>(Func<T> action)
        => Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal).GetTask();

    public Task<T> InvokeAsync<T>(Func<T> action, DispatcherPriority priority)
        => Dispatcher.UIThread.InvokeAsync(action, priority).GetTask();
}
