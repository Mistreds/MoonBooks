using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MoonBooks.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly string _path;
    private readonly string _defaultLibrary;
    private readonly object _gate = new();
    private readonly Subject<AppSettings> _changes = new();
    private AppSettings _current;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public SettingsService(AppPaths paths)
        : this(paths.SettingsPath, paths.DefaultLibraryPath)
    {
    }

    public SettingsService(string settingsPath, string defaultLibraryPath)
    {
        _path = settingsPath;
        _defaultLibrary = defaultLibraryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _current = Load();
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public IObservable<AppSettings> Changes => _changes.AsObservable();

    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
    {
        AppSettings snapshot;
        lock (_gate)
        {
            mutate(_current);
            if (string.IsNullOrWhiteSpace(_current.LibraryPath))
                _current.LibraryPath = _defaultLibrary;
            if (_current.DeviceId == Guid.Empty)
                _current.DeviceId = Guid.NewGuid();
            if (_current.Reader is null)
                _current.Reader = new ReaderSettings();
            snapshot = Clone(_current);
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct);
        File.Move(temp, _path, overwrite: true);
        _changes.OnNext(snapshot);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            var created = new AppSettings { LibraryPath = _defaultLibrary, DeviceId = Guid.NewGuid() };
            File.WriteAllText(_path, JsonSerializer.Serialize(created, JsonOptions));
            Directory.CreateDirectory(created.LibraryPath);
            return created;
        }

        var json = File.ReadAllText(_path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        if (string.IsNullOrWhiteSpace(settings.LibraryPath))
            settings.LibraryPath = _defaultLibrary;
        settings.Reader ??= new ReaderSettings();
        if (settings.DeviceId == Guid.Empty)
            settings.DeviceId = Guid.NewGuid();
        Directory.CreateDirectory(settings.LibraryPath);
        return settings;
    }

    public static AppSettings Clone(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }
}
