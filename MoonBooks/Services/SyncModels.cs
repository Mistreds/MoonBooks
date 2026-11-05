using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MoonBooks.Core.Domain;

namespace MoonBooks.Services;

public sealed record SyncStatus(SyncState State, string Message, DateTimeOffset? LastSyncedAt);

public interface ISyncService
{
    IObservable<SyncStatus> Status { get; }
    DateTimeOffset? LastSyncedAt { get; }
    Task SyncNowAsync(CancellationToken ct = default);
}

public sealed record PositionPrompt(long BookId, string Title, double RemotePercent, double LocalPercent, MoonBooks.Core.Sync.MoonPlusPosition Remote);

public sealed class SyncRunResult
{
    public bool Offline { get; init; }
    public bool Disabled { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<PositionPrompt> Prompts { get; init; } = [];
}

public sealed class MoonBooksSyncDocument
{
    public int SchemaVersion { get; set; } = 1;
    public long UpdatedAtUtc { get; set; }
    public List<SyncCategoryItem> Categories { get; set; } = [];
    public List<SyncAssignmentItem> Assignments { get; set; } = [];
    public ReaderSettings? Reader { get; set; }
    public long ReaderUpdatedAtUtc { get; set; }
}

public sealed class SyncCategoryItem
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Collection";
    public string? SourceCode { get; set; }
    public bool IsSystem { get; set; }
    public long UpdatedAtUtc { get; set; }
}

public sealed class SyncAssignmentItem
{
    public string FileName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Source { get; set; } = "Manual";
    public long UpdatedAtUtc { get; set; }
}
