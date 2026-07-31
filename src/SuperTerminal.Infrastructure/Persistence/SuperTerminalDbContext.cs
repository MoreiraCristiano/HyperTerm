using Microsoft.EntityFrameworkCore;
using SuperTerminal.Core.Entities;

namespace SuperTerminal.Infrastructure.Persistence;

public sealed class SuperTerminalDbContext(DbContextOptions<SuperTerminalDbContext> options)
    : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SuperTerminalDbContext).Assembly);
    }
}
