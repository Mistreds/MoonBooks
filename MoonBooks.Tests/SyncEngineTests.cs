using System.Text;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Content;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;
using MoonBooks.Core.Reading;
using MoonBooks.Core.Sync;
using MoonBooks.Data;
using MoonBooks.Services;

namespace MoonBooks.Tests;

public class MoonPlusCodecTests
{
    [Fact]
    public void Parses_integer_percent_decimal_percent_and_split_index()
    {
        var whole = MoonPlusPositionCodec.Parse("1710000000000*2@0#150:45%");
        Assert.Equal(1710000000000, whole.TimestampUnixMs);
        Assert.Equal(2, whole.ChapterIndex);
        Assert.Equal(0, whole.SplitIndex);
        Assert.Equal(150, whole.PositionInChapter);
        Assert.Equal(45, whole.Percent);

        var fraction = MoonPlusPositionCodec.Parse("1710000000000*2@3#15:45.5%");
        Assert.Equal(3, fraction.SplitIndex);
        Assert.Equal(45.5, fraction.Percent);
        Assert.Equal("1710000000000*2@3#15:45.5%", MoonPlusPositionCodec.Format(fraction));
    }

    [Fact]
    public void Codec_is_about_the_body_not_the_file_name()
    {
        var body = MoonPlusPositionCodec.Format(new MoonPlusPosition(10, 1, 0, 4, 12));
        var parsed = MoonPlusPositionCodec.Parse(body);
        Assert.Equal(4, parsed.PositionInChapter);
        Assert.DoesNotContain("Маяк", body);
    }

    [Fact]
    public void Round_trip_inside_one_chapter_keeps_offset_when_split_is_zero()
    {
        var content = new BookContent
        {
            Chapters =
            [
                ChapterContent.Create("Глава", [new ParagraphBlock([new TextRun("Привет, мир")])]),
            ],
        };
        var book = new Book { Title = "Книга", ChapterCount = 1, TotalChars = content.TotalChars };
        var mapper = new MoonPlusPositionMapper();
        var local = new BookPosition(0, 4);
        var remote = mapper.ToMoonPlus(local, book, content, 1_000);
        var back = mapper.ToBookPosition(remote, book, content);

        Assert.Equal(0, remote.SplitIndex);
        Assert.Equal(4, remote.PositionInChapter);
        Assert.Equal(local, back);
    }

    [Fact]
    public void Different_chapter_count_falls_back_to_percent()
    {
        var content = new BookContent
        {
            Chapters =
            [
                ChapterContent.Create("A", [new ParagraphBlock([new TextRun("0123456789")])]),
                ChapterContent.Create("B", [new ParagraphBlock([new TextRun("0123456789")])]),
            ],
        };
        var book = new Book { ChapterCount = 2, TotalChars = content.TotalChars };
        var mapper = new MoonPlusPositionMapper();
        var remote = new MoonPlusPosition(1, 9, 0, 0, 100);
        var position = mapper.ToBookPosition(remote, book, content);
        Assert.Equal(1, position.ChapterIndex);
    }
}

public class SyncEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moonbooks-sync-" + Guid.NewGuid().ToString("N"));

    public SyncEngineTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Newer_remote_position_updates_local()
    {
        var (engine, provider, db) = await CreateAsync(localStamp: 1_000, localPercent: 10, localOffset: 1);
        provider.Files["Books/.Moon+/Cache/Tale.fb2.po"] = Encoding.UTF8.GetBytes("5000*0@0#20:12%");

        var result = await engine.RunAsync(default);

        Assert.Empty(result.Prompts);
        db.ChangeTracker.Clear();
        var state = await db.ReadingStates.SingleAsync();
        Assert.Equal(20, state.CharOffset);
        Assert.Equal(12, state.Percent);
        Assert.Equal(5000, state.UpdatedAtUtc);
    }

    [Fact]
    public async Task Newer_local_position_is_uploaded()
    {
        var (engine, provider, db) = await CreateAsync(localStamp: 9_000, localPercent: 30, localOffset: 12);
        provider.Files["Books/.Moon+/Cache/Tale.fb2.po"] = Encoding.UTF8.GetBytes("1000*0@0#1:1%");

        await engine.RunAsync(default);

        var body = Encoding.UTF8.GetString(provider.Files["Books/.Moon+/Cache/Tale.fb2.po"]);
        var parsed = MoonPlusPositionCodec.Parse(body);
        Assert.Equal(12, parsed.PositionInChapter);
        Assert.Equal(9_000, parsed.TimestampUnixMs);
        db.ChangeTracker.Clear();
        Assert.Equal(9_000, (await db.ReadingStates.SingleAsync()).SyncedAtUtc);
    }

    [Fact]
    public async Task Divergence_over_five_points_returns_a_prompt()
    {
        var (engine, provider, db) = await CreateAsync(localStamp: 1_000, localPercent: 10, localOffset: 2);
        provider.Files["Books/.Moon+/Cache/Tale.fb2.po"] = Encoding.UTF8.GetBytes("8000*0@0#40:40%");

        var result = await engine.RunAsync(default);

        var prompt = Assert.Single(result.Prompts);
        Assert.Equal(40, prompt.RemotePercent);
        Assert.Equal(10, prompt.LocalPercent);
        Assert.Equal(2, (await db.ReadingStates.SingleAsync()).CharOffset);
    }

    [Fact]
    public async Task Offline_run_enqueues_and_later_flush_uploads()
    {
        var (engine, provider, db) = await CreateAsync(localStamp: 4_000, localPercent: 15, localOffset: 7);
        provider.Offline = true;

        var offline = await engine.RunAsync(default);
        Assert.True(offline.Offline);
        Assert.Equal(1, await db.SyncQueue.CountAsync());

        provider.Offline = false;
        var online = await engine.RunAsync(default);
        Assert.False(online.Offline);
        Assert.Equal(0, await db.SyncQueue.CountAsync());
        Assert.True(provider.Files.ContainsKey("Books/.Moon+/Cache/Tale.fb2.po"));
        var parsed = MoonPlusPositionCodec.Parse(Encoding.UTF8.GetString(provider.Files["Books/.Moon+/Cache/Tale.fb2.po"]));
        Assert.Equal(7, parsed.PositionInChapter);
    }

    [Fact]
    public async Task Google_provider_without_client_id_is_not_configured()
    {
        var provider = new GoogleDriveSyncProvider(null, null, null, Path.Combine(_root, "tokens"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TestConnectionAsync(default));
        Assert.Contains("не настроен", error.Message);
    }

    private async Task<(SyncEngine Engine, MemoryProvider Provider, BookDbContext Db)> CreateAsync(long localStamp, double localPercent, int localOffset)
    {
        var library = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(library);
        var options = new DbContextOptionsBuilder<BookDbContext>().UseSqlite($"Data Source={Path.Combine(_root, Guid.NewGuid().ToString("N"))}.db").Options;
        var db = new BookDbContext(options);
        await db.Database.MigrateAsync();
        var book = new Book
        {
            Title = "Tale",
            SortTitle = "tale",
            Format = BookFormat.Fb2,
            FileName = "Tale.fb2",
            FileHash = new string('a', 64),
            ChapterCount = 1,
            TotalChars = 100,
            AddedAtUtc = DateTime.UtcNow,
            ReadingState = new ReadingState
            {
                Status = BookStatus.Reading,
                ChapterIndex = 0,
                CharOffset = localOffset,
                Percent = localPercent,
                UpdatedAtUtc = localStamp,
            },
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        var settings = new SettingsService(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json"), library);
        await settings.UpdateAsync(value =>
        {
            value.SyncProvider = SyncProviderKind.WebDav;
            value.SyncMode = SyncMode.ReadingStatus;
            value.MoonPlusBooksFolder = "Books";
        });
        var provider = new MemoryProvider();
        var factory = new TestDbFactory(options);
        var engine = new SyncEngine(settings, factory, new FixedFactory(provider), new MoonPlusPositionMapper(), [], new NoImport());
        return (engine, provider, db);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FixedFactory(MemoryProvider provider) : ISyncProviderFactory
    {
        public ISyncProvider Create(AppSettings settings) => provider;
    }

    private sealed class NoImport : IBookImportService
    {
        public Task<ImportBatchResult> ImportAsync(IReadOnlyList<string> paths, IProgress<ImportProgress>? progress, CancellationToken ct) =>
            Task.FromResult(new ImportBatchResult([]));

        public Task<ImportBatchResult> ImportFolderAsync(string folder, IProgress<ImportProgress>? progress, CancellationToken ct) =>
            Task.FromResult(new ImportBatchResult([]));

        public Task<ImportItemResult> ResolveConflictAsync(ImportConflict conflict, ConflictResolution resolution, CancellationToken ct) =>
            Task.FromResult(new ImportItemResult(conflict.SourcePath, ImportItemStatus.Skipped, null, null, null));
    }

    private sealed class TestDbFactory(DbContextOptions<BookDbContext> options) : IDbContextFactory<BookDbContext>
    {
        public BookDbContext CreateDbContext() => new(options);
        public Task<BookDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class MemoryProvider : ISyncProvider
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Offline { get; set; }

        public Task TestConnectionAsync(CancellationToken ct)
        {
            if (Offline)
                throw new IOException("offline");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RemoteEntry>> ListAsync(string folder, CancellationToken ct)
        {
            if (Offline)
                throw new IOException("offline");
            var prefix = folder.Trim('/') + "/";
            IReadOnlyList<RemoteEntry> list = Files
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(pair => new RemoteEntry(pair.Key, pair.Value.Length, DateTime.UtcNow, "etag"))
                .ToList();
            return Task.FromResult(list);
        }

        public Task<RemoteEntry?> GetAsync(string path, CancellationToken ct)
        {
            if (Offline)
                throw new IOException("offline");
            if (!Files.TryGetValue(path, out var bytes))
                return Task.FromResult<RemoteEntry?>(null);
            return Task.FromResult<RemoteEntry?>(new RemoteEntry(path, bytes.Length, DateTime.UtcNow, "etag"));
        }

        public Task DownloadAsync(string path, Stream target, CancellationToken ct)
        {
            if (!Files.TryGetValue(path, out var bytes))
                throw new FileNotFoundException(path);
            target.Write(bytes);
            return Task.CompletedTask;
        }

        public Task UploadAsync(string path, Stream source, CancellationToken ct)
        {
            if (Offline)
                throw new IOException("offline");
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            Files[path] = buffer.ToArray();
            return Task.CompletedTask;
        }

        public Task EnsureFolderAsync(string folder, CancellationToken ct) => Task.CompletedTask;
    }
}
