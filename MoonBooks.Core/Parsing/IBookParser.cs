using MoonBooks.Core.Content;

namespace MoonBooks.Core.Parsing;

public interface IBookParser
{
    bool CanParse(string path);
    Task<BookMetadata> ReadMetadataAsync(Stream stream, CancellationToken ct);
    Task<BookContent> ReadContentAsync(Stream stream, CancellationToken ct);
}
