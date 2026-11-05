namespace MoonBooks.Core.Domain;

public sealed class ReadingState
{
    public long BookId { get; set; }
    public Book Book { get; set; } = null!;
    public BookStatus Status { get; set; }
    public int ChapterIndex { get; set; }
    public int CharOffset { get; set; }
    /// <summary>Progress in percentage points, 0–100.</summary>
    public double Percent { get; set; }
    /// <summary>Unix epoch milliseconds.</summary>
    public long UpdatedAtUtc { get; set; }
    /// <summary>Unix epoch milliseconds of the last successful position sync, if any.</summary>
    public long? SyncedAtUtc { get; set; }
}
