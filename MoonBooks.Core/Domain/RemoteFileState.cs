namespace MoonBooks.Core.Domain;

public sealed class RemoteFileState
{
    public string Path { get; set; } = string.Empty;
    public string? ETag { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public string? LocalHash { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
