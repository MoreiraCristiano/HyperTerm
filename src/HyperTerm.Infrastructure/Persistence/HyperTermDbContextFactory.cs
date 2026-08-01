using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HyperTerm.Infrastructure.Persistence;

public sealed class HyperTermDbContextFactory
    : IDesignTimeDbContextFactory<HyperTermDbContext>
{
    public HyperTermDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<HyperTermDbContext> options =
            new DbContextOptionsBuilder<HyperTermDbContext>()
                .UseSqlite("Data Source=hyperterm.design.db")
                .Options;

        return new HyperTermDbContext(options);
    }
}
