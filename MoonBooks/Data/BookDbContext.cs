using Microsoft.EntityFrameworkCore;
using MoonBooks.Core.Domain;

namespace MoonBooks.Data;

public class BookDbContext : DbContext
{
    public BookDbContext(DbContextOptions<BookDbContext> options)
        : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BookCategory> BookCategories => Set<BookCategory>();
    public DbSet<ReadingState> ReadingStates => Set<ReadingState>();
    public DbSet<SyncQueueItem> SyncQueue => Set<SyncQueueItem>();
    public DbSet<RemoteFileState> RemoteFileStates => Set<RemoteFileState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(entity =>
        {
            entity.ToTable("Books");
            entity.HasKey(book => book.Id);
            entity.Property(book => book.Title).IsRequired();
            entity.Property(book => book.SortTitle).IsRequired();
            entity.Property(book => book.FileName).IsRequired().UseCollation("NOCASE");
            entity.Property(book => book.FileHash).IsRequired().HasMaxLength(64);
            entity.Property(book => book.Format).HasConversion<int>();
            entity.HasIndex(book => book.FileHash).IsUnique();
            entity.HasIndex(book => book.FileName).IsUnique();
            entity.HasOne(book => book.ReadingState)
                .WithOne(state => state.Book)
                .HasForeignKey<ReadingState>(state => state.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Author>(entity =>
        {
            entity.ToTable("Authors");
            entity.HasKey(author => author.Id);
            entity.Property(author => author.Name).IsRequired();
            entity.HasIndex(author => author.Name);
        });

        modelBuilder.Entity<BookAuthor>(entity =>
        {
            entity.ToTable("BookAuthors");
            entity.HasKey(link => new { link.BookId, link.AuthorId });
            entity.HasOne(link => link.Book)
                .WithMany(book => book.Authors)
                .HasForeignKey(link => link.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.Author)
                .WithMany(author => author.Books)
                .HasForeignKey(link => link.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).IsRequired();
            entity.Property(category => category.Kind).HasConversion<int>();
            entity.HasIndex(category => new { category.Kind, category.Name }).IsUnique();
        });

        modelBuilder.Entity<BookCategory>(entity =>
        {
            entity.ToTable("BookCategories");
            entity.HasKey(link => new { link.BookId, link.CategoryId });
            entity.Property(link => link.Source).HasConversion<int>();
            entity.HasOne(link => link.Book)
                .WithMany(book => book.Categories)
                .HasForeignKey(link => link.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.Category)
                .WithMany(category => category.Books)
                .HasForeignKey(link => link.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingState>(entity =>
        {
            entity.ToTable("ReadingStates");
            entity.HasKey(state => state.BookId);
            entity.Property(state => state.Status).HasConversion<int>();
        });

        modelBuilder.Entity<SyncQueueItem>(entity =>
        {
            entity.ToTable("SyncQueue");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Kind).HasConversion<int>();
            entity.Property(item => item.Payload).IsRequired();
            entity.HasIndex(item => item.NextAttemptUtc);
        });

        modelBuilder.Entity<RemoteFileState>(entity =>
        {
            entity.ToTable("RemoteFileStates");
            entity.HasKey(state => state.Path);
            entity.Property(state => state.Path).IsRequired();
        });
    }
}
