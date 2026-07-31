using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SuperTerminal.Infrastructure.Persistence;

public sealed class SuperTerminalDbContextFactory
    : IDesignTimeDbContextFactory<SuperTerminalDbContext>
{
    public SuperTerminalDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<SuperTerminalDbContext> options =
            new DbContextOptionsBuilder<SuperTerminalDbContext>()
                .UseSqlite("Data Source=superterminal.design.db")
                .Options;

        return new SuperTerminalDbContext(options);
    }
}
