using Microsoft.EntityFrameworkCore;
using HyperTerm.Core.Entities;

namespace HyperTerm.Infrastructure.Persistence;

public sealed class HyperTermDbContext(DbContextOptions<HyperTermDbContext> options)
    : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<SessionFolder> SessionFolders => Set<SessionFolder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HyperTermDbContext).Assembly);
    }
}
