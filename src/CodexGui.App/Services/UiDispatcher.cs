namespace CodexGui.App.Services;

internal enum UiDispatchPriority
{
    Normal,
    Background
}

internal interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    void Post(Action action, UiDispatchPriority priority);

    Task InvokeAsync(Action action);

    Task InvokeAsync(Action action, UiDispatchPriority priority);

    Task<T> InvokeAsync<T>(Func<T> action);

    Task<T> InvokeAsync<T>(Func<T> action, UiDispatchPriority priority);
}

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();

    public void Post(Action action, UiDispatchPriority priority) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task InvokeAsync(Action action, UiDispatchPriority priority)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

    public Task<T> InvokeAsync<T>(Func<T> action, UiDispatchPriority priority) => Task.FromResult(action());
}
