using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MoonBooks.Data;

public sealed class BookDbContextFactory : IDesignTimeDbContextFactory<BookDbContext>
{
    public BookDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite("Data Source=moonbooks.design.db")
            .Options;
        return new BookDbContext(options);
    }
}
