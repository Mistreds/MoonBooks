using System;
using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    IObservable<AppSettings> Changes { get; }
    Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default);
}
