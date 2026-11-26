using System.Windows.Threading;
using ZseAdmin.Client.Core.Mvvm;

namespace ZseAdmin.Client;

/// <summary>Mostek między warstwą Core (bez WPF) a wątkiem UI WPF.</summary>
public sealed class DispatcherAdapter : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public DispatcherAdapter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;
}
