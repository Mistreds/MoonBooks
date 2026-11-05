using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Domain;

namespace MoonBooks.Services;

public sealed class SyncService : ISyncService
{
    private readonly SyncEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly BehaviorSubject<SyncStatus> _status;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(SyncEngine engine, IDialogService dialogs, ISettingsService settings)
    {
        _engine = engine;
        _dialogs = dialogs;
        _settings = settings;
        _status = new BehaviorSubject<SyncStatus>(new SyncStatus(SyncState.Disabled, "Синхронизация выключена", null));
    }

    public IObservable<SyncStatus> Status => _status.AsObservable();
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return;
        try
        {
            if (_settings.Current.SyncProvider == SyncProviderKind.None)
            {
                Publish(SyncState.Disabled, "Синхронизация выключена");
                return;
            }

            Publish(SyncState.Syncing, "Синхронизация…");
            var result = await _engine.RunAsync(ct);
            foreach (var prompt in result.Prompts)
            {
                var go = await _dialogs.ShowRemoteProgressAsync(
                    $"На другом устройстве вы на {prompt.RemotePercent:0}%, перейти?");
                if (go)
                    await _engine.ApplyRemoteAsync(prompt, ct);
            }

            if (result.Disabled)
            {
                Publish(SyncState.Disabled, "Синхронизация выключена");
                return;
            }

            if (result.Offline)
            {
                Publish(SyncState.Offline, "Нет сети. Изменения поставлены в очередь.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Publish(SyncState.Error, result.Error);
                return;
            }

            LastSyncedAt = DateTimeOffset.Now;
            Publish(SyncState.Ready, "Синхронизировано");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Publish(SyncState.Error, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Publish(SyncState state, string message) =>
        _status.OnNext(new SyncStatus(state, message, LastSyncedAt));
}
