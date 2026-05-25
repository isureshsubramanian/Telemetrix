using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Telemetrix.Storage;

namespace Telemetrix.Diagnostics;

/// <summary>
/// A hosted service that watches every <see cref="DiagnosticListener"/> in the process and
/// attaches an <see cref="EfCoreCommandObserver"/> whenever Entity Framework Core's listener
/// appears. This is what powers the SQL parameter inspector with zero configuration.
/// </summary>
internal sealed class TelemetrixDiagnosticSubscriber : IObserver<DiagnosticListener>, IHostedService, IDisposable
{
    private const string EfCoreListenerName = "Microsoft.EntityFrameworkCore";
    private const string CommandEventPrefix = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly TelemetrixStore _store;
    private readonly TelemetrixOptions _options;
    private readonly List<IDisposable> _subscriptions = [];
    private IDisposable? _allListenersSubscription;
    private int _disposed;

    public TelemetrixDiagnosticSubscriber(TelemetrixStore store, TelemetrixOptions options)
    {
        _store = store;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.CaptureSql)
        {
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (!string.Equals(listener.Name, EfCoreListenerName, StringComparison.Ordinal))
        {
            return;
        }

        var observer = new EfCoreCommandObserver(_store, _options);
        var subscription = listener.Subscribe(observer, IsCommandEvent);

        lock (_subscriptions)
        {
            _subscriptions.Add(subscription);
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _allListenersSubscription?.Dispose();

        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
            {
                try
                {
                    subscription.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _subscriptions.Clear();
        }
    }

    private static bool IsCommandEvent(string eventName)
        => eventName.StartsWith(CommandEventPrefix, StringComparison.Ordinal);
}
