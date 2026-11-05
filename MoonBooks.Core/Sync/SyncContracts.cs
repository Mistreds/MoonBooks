namespace MoonBooks.Core.Sync;

public sealed record RemoteEntry(string Path, long Size, DateTime? ModifiedUtc, string? ETag);

public interface ISyncProvider
{
    Task TestConnectionAsync(CancellationToken ct);
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string folder, CancellationToken ct);
    Task<RemoteEntry?> GetAsync(string path, CancellationToken ct);
    Task DownloadAsync(string path, Stream target, CancellationToken ct);
    Task UploadAsync(string path, Stream source, CancellationToken ct);
    Task EnsureFolderAsync(string folder, CancellationToken ct);
}

/// <summary>
/// Hypothesis of a Moon+ Reader position file body:
/// <c>timestamp*chapterIndex@splitIndex#positionInChapter:percent%</c>.
/// The timestamp is Unix milliseconds. Percent may be an integer or a decimal.
/// This has not been verified against a real device.
/// </summary>
public readonly record struct MoonPlusPosition(
    long TimestampUnixMs,
    int ChapterIndex,
    int SplitIndex,
    int PositionInChapter,
    double Percent);

public static class MoonPlusPositionCodec
{
    public static MoonPlusPosition Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new FormatException("Пустое тело .po.");

        var text = body.Trim();
        var star = text.IndexOf('*');
        var at = text.IndexOf('@');
        var hash = text.IndexOf('#');
        var colon = text.IndexOf(':');
        if (star < 1 || at < star || hash < at || colon < hash || !text.EndsWith('%'))
            throw new FormatException($"Не удалось разобрать позицию Moon+: {text}");

        var timestamp = long.Parse(text[..star], System.Globalization.CultureInfo.InvariantCulture);
        var chapter = int.Parse(text[(star + 1)..at], System.Globalization.CultureInfo.InvariantCulture);
        var split = int.Parse(text[(at + 1)..hash], System.Globalization.CultureInfo.InvariantCulture);
        var position = int.Parse(text[(hash + 1)..colon], System.Globalization.CultureInfo.InvariantCulture);
        var percentText = text[(colon + 1)..^1];
        var percent = double.Parse(percentText, System.Globalization.CultureInfo.InvariantCulture);
        return new MoonPlusPosition(timestamp, chapter, split, position, percent);
    }

    public static string Format(MoonPlusPosition position)
    {
        var percent = position.Percent % 1 == 0
            ? position.Percent.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : position.Percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{position.TimestampUnixMs}*{position.ChapterIndex}@{position.SplitIndex}#{position.PositionInChapter}:{percent}%");
    }
}
