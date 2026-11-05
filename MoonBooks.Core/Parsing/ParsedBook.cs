using MoonBooks.Core.Content;

namespace MoonBooks.Core.Parsing;

public sealed class ParsedBook
{
    public required BookMetadata Metadata { get; init; }
    public required BookContent Content { get; init; }
}
