using MoonBooks.Core.Domain;

namespace MoonBooks.Core.Parsing;

public sealed class BookMetadata
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> Authors { get; init; } = [];
    public string? Series { get; init; }
    public double? SeriesIndex { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    /// <summary>Raw genre codes (FB2) parallel to <see cref="Genres"/> when the parser knows them.</summary>
    public IReadOnlyList<string> GenreCodes { get; init; } = [];
    public string? Annotation { get; init; }
    public string? Language { get; init; }
    public byte[]? CoverBytes { get; init; }
    public BookFormat Format { get; init; }
}
