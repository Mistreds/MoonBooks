using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;
using MoonBooks.Core.Parsing;
using MoonBooks.Data;
using MoonBooks.EpubParser;
using MoonBooks.Fb2Parser;
using MoonBooks.Services;

namespace MoonBooks.Tests;

public class ParserAndImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moonbooks-import-" + Guid.NewGuid().ToString("N"));

    public ParserAndImportTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Fb2_utf8_reads_metadata_chapters_and_plain_text()
    {
        var path = Path.Combine(_root, "utf8.fb2");
        await File.WriteAllTextAsync(path, SampleFb2("utf-8"), Encoding.UTF8);
        var parser = new Fb2BookParser();
        await using var stream = File.OpenRead(path);
        var metadata = await parser.ReadMetadataAsync(stream, default);
        stream.Position = 0;
        var content = await parser.ReadContentAsync(stream, default);

        Assert.Equal("Тестовая книга", metadata.Title);
        Assert.Equal(["Иван Бунин"], metadata.Authors);
        Assert.Equal("Серия", metadata.Series);
        Assert.Equal(2, metadata.SeriesIndex);
        Assert.Equal(["Фэнтези"], metadata.Genres);
        Assert.Equal(["sf_fantasy"], metadata.GenreCodes);
        Assert.Equal("ru", metadata.Language);
        Assert.Contains("Коротко", metadata.Annotation);
        Assert.NotNull(metadata.CoverBytes);
        Assert.Equal(2, content.Chapters.Count);
        Assert.Equal("Первый абзац.\nВложенная\nТекст внутри.\n\nПосле пустой.", content.Chapters[0].PlainText);
        Assert.Equal(content.Chapters[0].PlainText.Length, content.Chapters[0].PlainTextLength);
        Assert.True(content.Footnotes.TryGet("n1", out var note));
        Assert.Contains("Сноска", note.Blocks.OfType<MoonBooks.Core.Content.ParagraphBlock>().First().Inlines[0].Text);
    }

    [Fact]
    public async Task Fb2_windows1251_honors_xml_encoding()
    {
        var xml = SampleFb2("windows-1251");
        var path = Path.Combine(_root, "cp1251.fb2");
        await File.WriteAllBytesAsync(path, Encoding.GetEncoding(1251).GetBytes(xml));
        var parser = new Fb2BookParser();
        await using var stream = File.OpenRead(path);
        var metadata = await parser.ReadMetadataAsync(stream, default);
        Assert.Equal("Тестовая книга", metadata.Title);
        Assert.Equal("Иван Бунин", metadata.Authors[0]);
    }

    [Fact]
    public async Task Epub_reads_spine_subject_and_footnote()
    {
        var path = WriteSampleEpub(Path.Combine(_root, "tale.epub"));
        var parser = new EpubBookParser();
        await using var stream = File.OpenRead(path);
        var metadata = await parser.ReadMetadataAsync(stream, default);
        stream.Position = 0;
        var content = await parser.ReadContentAsync(stream, default);

        Assert.Equal("Epub Tale", metadata.Title);
        Assert.Equal(["Ann Writer"], metadata.Authors);
        Assert.Equal(["Fantasy", "Space"], metadata.Genres);
        Assert.Equal("en", metadata.Language);
        Assert.Equal("Orbit", metadata.Series);
        Assert.Single(content.Chapters);
        Assert.Contains("Hello paragraph.", content.Chapters[0].PlainText);
        Assert.True(content.Chapters[0].PlainTextLength > 0);
        Assert.Contains(content.Footnotes.Notes.Keys, key => key == "fn1");
        Assert.NotEmpty(content.Toc);
    }

    [Fact]
    public async Task Import_skips_duplicate_and_reports_name_conflict()
    {
        var library = Path.Combine(_root, "library");
        Directory.CreateDirectory(library);
        var dbPath = Path.Combine(_root, "import.db");
        var options = new DbContextOptionsBuilder<BookDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        await using (var db = new BookDbContext(options))
            await db.Database.MigrateAsync();

        var settings = new SettingsService(Path.Combine(_root, "settings.json"), library);
        var factory = new TestDbFactory(options);
        var categories = new CategoryService(factory);
        var covers = new CoverCache(Path.Combine(_root, "covers"));
        var import = new BookImportService([new Fb2BookParser(), new EpubBookParser()], settings, factory, covers, categories);

        var first = Path.Combine(_root, "inbox", "Same.fb2");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        await File.WriteAllTextAsync(first, SampleFb2("utf-8"));

        var imported = await import.ImportAsync([first], null, default);
        Assert.Equal(ImportItemStatus.Imported, imported.Items[0].Status);
        Assert.True(File.Exists(Path.Combine(library, "Same.fb2")));

        var again = await import.ImportAsync([first], null, default);
        Assert.Equal(ImportItemStatus.SkippedDuplicate, again.Items[0].Status);

        var other = Path.Combine(_root, "inbox", "other", "Same.fb2");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        await File.WriteAllTextAsync(other, SampleFb2("utf-8").Replace("Тестовая книга", "Другая книга"));
        var conflict = await import.ImportAsync([other], null, default);
        Assert.Equal(ImportItemStatus.Conflict, conflict.Items[0].Status);

        var kept = await import.ResolveConflictAsync(conflict.Items[0].Conflict!, ConflictResolution.KeepBoth, default);
        Assert.Equal(ImportItemStatus.Imported, kept.Status);
        Assert.Contains("Moon+", kept.Message);
        Assert.True(File.Exists(Path.Combine(library, "Same (2).fb2")));

        await using var check = factory.CreateDbContext();
        var books = await check.Books.Include(book => book.Categories).ThenInclude(link => link.Category).ToListAsync();
        Assert.Equal(2, books.Count);
        Assert.Contains(books, book => book.Categories.Any(link => link.Source == CategorySource.Auto && link.Category.Name == "Фэнтези"));
    }

    [Fact]
    public async Task Deleted_library_file_is_marked_missing()
    {
        var library = Path.Combine(_root, "watched");
        Directory.CreateDirectory(library);
        var dbPath = Path.Combine(_root, "watch.db");
        var options = new DbContextOptionsBuilder<BookDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        await using (var db = new BookDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Books.Add(new Book
            {
                Title = "Gone",
                SortTitle = "gone",
                Format = BookFormat.Fb2,
                FileName = "Gone.fb2",
                FileHash = new string('c', 64),
                AddedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var settings = new SettingsService(Path.Combine(_root, "watch.json"), library);
        var factory = new TestDbFactory(options);
        var watcher = new LibraryWatcher(settings, new BookImportService([], settings, factory, new CoverCache(Path.Combine(_root, "c2")), new CategoryService(factory)), factory);
        await watcher.HandleDeletedAsync(Path.Combine(library, "Gone.fb2"), default);

        await using var check = factory.CreateDbContext();
        Assert.True((await check.Books.SingleAsync()).IsMissing);
    }

    private static string SampleFb2(string encoding) =>
        $"""
        <?xml version="1.0" encoding="{encoding}"?>
        <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0" xmlns:l="http://www.w3.org/1999/xlink">
          <description>
            <title-info>
              <genre>sf_fantasy</genre>
              <author><first-name>Иван</first-name><last-name>Бунин</last-name></author>
              <book-title>Тестовая книга</book-title>
              <annotation><p>Коротко.</p></annotation>
              <lang>ru</lang>
              <coverpage><image l:href="#cover.jpg"/></coverpage>
              <sequence name="Серия" number="2"/>
            </title-info>
          </description>
          <body>
            <section>
              <title><p>Глава 1</p></title>
              <p>Первый абзац.</p>
              <section>
                <title><p>Вложенная</p></title>
                <p>Текст внутри.</p>
              </section>
              <empty-line/>
              <p>После пустой.</p>
            </section>
            <section>
              <title><p>Глава 2</p></title>
              <p>Вторая.</p>
            </section>
          </body>
          <body name="notes">
            <section id="n1"><p>Сноска.</p></section>
          </body>
          <binary id="cover.jpg" content-type="image/jpeg">iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==</binary>
        </FictionBook>
        """;

    private static string WriteSampleEpub(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(mime.Open(), new UTF8Encoding(false)))
            writer.Write("application/epub+zip");

        Write(archive, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);
        Write(archive, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="uid">id-1</dc:identifier>
                <dc:title>Epub Tale</dc:title>
                <dc:creator>Ann Writer</dc:creator>
                <dc:language>en</dc:language>
                <dc:subject>fantasy, space</dc:subject>
                <dc:description>An annotation.</dc:description>
                <meta name="calibre:series" content="Orbit"/>
              </metadata>
              <manifest>
                <item id="chap" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
              </manifest>
              <spine>
                <itemref idref="chap"/>
              </spine>
            </package>
            """);
        Write(archive, "OEBPS/chapter1.xhtml", """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <head><title>Chapter One</title></head>
              <body>
                <h1>Chapter One</h1>
                <p>Hello paragraph.</p>
                <p>See <a epub:type="noteref" href="#fn1">1</a>.</p>
                <aside id="fn1" epub:type="footnote"><p>A note.</p></aside>
              </body>
            </html>
            """);
        Write(archive, "OEBPS/nav.xhtml", """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body>
                <nav epub:type="toc"><ol><li><a href="chapter1.xhtml">Chapter One</a></li></ol></nav>
              </body>
            </html>
            """);
        return path;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class TestDbFactory(DbContextOptions<BookDbContext> options) : IDbContextFactory<BookDbContext>
    {
        public BookDbContext CreateDbContext() => new(options);
        public Task<BookDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
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
}
