using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public enum ConflictResolution
{
    Replace,
    KeepBoth,
    Skip,
}

public enum ImportItemStatus
{
    Imported,
    SkippedDuplicate,
    Conflict,
    Failed,
    Skipped,
}

public sealed record ImportProgress(int Completed, int Total, string? CurrentFile);

public sealed record ImportConflict(string SourcePath, string FileName, long ExistingBookId);

public sealed record ImportItemResult(
    string SourcePath,
    ImportItemStatus Status,
    long? BookId,
    string? Message,
    ImportConflict? Conflict);

public sealed record ImportBatchResult(IReadOnlyList<ImportItemResult> Items);

public interface IBookImportService
{
    Task<ImportBatchResult> ImportAsync(IReadOnlyList<string> paths, IProgress<ImportProgress>? progress, CancellationToken ct);
    Task<ImportBatchResult> ImportFolderAsync(string folder, IProgress<ImportProgress>? progress, CancellationToken ct);
    Task<ImportItemResult> ResolveConflictAsync(ImportConflict conflict, ConflictResolution resolution, CancellationToken ct);
}
