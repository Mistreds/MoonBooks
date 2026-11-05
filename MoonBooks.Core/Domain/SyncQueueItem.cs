namespace MoonBooks.Core.Domain;

public enum SyncQueueKind
{
    UploadPosition = 0,
    UploadBook = 1,
    DownloadBook = 2,
    UploadLibraryMeta = 3,
}

public sealed class SyncQueueItem
{
    public long Id { get; set; }
    public SyncQueueKind Kind { get; set; }
    public long? BookId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public string? LastError { get; set; }
}
