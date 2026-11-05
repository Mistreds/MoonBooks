using System;
using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public sealed class SyncScheduler : IDisposable
{
    private readonly ISyncService _sync;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _loop;

    public SyncScheduler(ISyncService sync, ISettingsService settings)
    {
        _sync = sync;
        _settings = settings;
    }

    public void Start()
    {
        _loop?.Cancel();
        _loop = new CancellationTokenSource();
        _ = LoopAsync(_loop.Token);
        _ = _sync.SyncNowAsync();
    }

    public Task OnBookOpenCloseAsync() => _sync.SyncNowAsync();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var minutes = Math.Max(1, _settings.Current.SyncIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), ct);
                await _sync.SyncNowAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // The sync service already publishes the error state.
            }
        }
    }

    public void Dispose() => _loop?.Cancel();
}
