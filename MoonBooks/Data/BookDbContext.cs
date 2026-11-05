using Microsoft.EntityFrameworkCore;

namespace MoonBooks.Data;

public class BookDbContext : DbContext
{
    public BookDbContext (DbContextOptions<BookDbContext > options)
        : base(options)
    {
    }
}