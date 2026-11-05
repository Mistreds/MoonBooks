using System.Reflection;
using System.Text.Json;

namespace MoonBooks.Fb2Parser;

public static class Fb2GenreMap
{
    private static readonly Dictionary<string, string> Map = Load();

    public static string ToDisplayName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Без жанра";

        var key = code.Trim();
        return Map.TryGetValue(key, out var name) ? name : key;
    }

    private static Dictionary<string, string> Load()
    {
        var assembly = typeof(Fb2GenreMap).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(resource => resource.EndsWith("fb2-genres.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Не найден ресурс fb2-genres.json.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }
}
