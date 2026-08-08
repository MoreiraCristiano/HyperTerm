using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Persistence;
using HyperTerm.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HyperTerm.Infrastructure.Tests;

public sealed class PersistenceIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migrations_create_complete_schema_from_empty_database()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using HyperTermDbContext context = await database.Factory.CreateDbContextAsync();

        string[] migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(2, migrations.Length);
        Assert.Contains("20260731131844_InitialCreate", migrations);
        Assert.Contains("20260731184526_AddSessionFolders", migrations);
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Session_repository_round_trips_orders_updates_and_deletes()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        var repository = new SessionRepository(database.Factory);
        DateTime now = DateTime.UtcNow;
        var second = Session("Zulu", "B", now);
        var first = Session("Alpha", "A", now);
        await repository.AddAsync(second);
        await repository.AddAsync(first);

        IReadOnlyList<Session> all = await repository.GetAllAsync();
        Assert.Equal([first.Id, second.Id], all.Select(item => item.Id));

        Session? loaded = await repository.GetByIdAsync(first.Id);
        Assert.NotNull(loaded);
        loaded.Update("Changed", loaded.Host, 2200, loaded.Username, null, "C", "note", now.AddHours(1));
        await repository.UpdateAsync(loaded);
        Session? changed = await repository.GetByIdAsync(first.Id);
        Assert.NotNull(changed);
        Assert.Equal(("Changed", 2200, "C"), (changed.Name, changed.Port, changed.Folder));

        Assert.True(await repository.DeleteAsync(first.Id));
        Assert.False(await repository.DeleteAsync(first.Id));
        Assert.Null(await repository.GetByIdAsync(first.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Folder_rename_moves_descendants_and_sessions_transactionally()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        var folders = new SessionFolderRepository(database.Factory);
        var sessions = new SessionRepository(database.Factory);
        DateTime now = DateTime.UtcNow;
        await folders.AddAsync(new SessionFolder(Guid.NewGuid(), "Work", now));
        await folders.AddAsync(new SessionFolder(Guid.NewGuid(), "Work/Prod", now));
        Session session = Session("Server", "Work/Prod", now);
        await sessions.AddAsync(session);

        Assert.True(await folders.RenameTreeAsync("Work", "Company"));

        Assert.Equal(
            ["Company", "Company/Prod"],
            (await folders.GetAllAsync()).Select(folder => folder.Path));
        Assert.Equal("Company/Prod", (await sessions.GetByIdAsync(session.Id))!.Folder);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Folder_delete_without_force_rolls_back_everything()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        var folders = new SessionFolderRepository(database.Factory);
        var sessions = new SessionRepository(database.Factory);
        DateTime now = DateTime.UtcNow;
        await folders.AddAsync(new SessionFolder(Guid.NewGuid(), "Work", now));
        Session session = Session("Server", "Work", now);
        await sessions.AddAsync(session);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            folders.DeleteTreesAsync(["Work"], force: false));

        Assert.Single(await folders.GetAllAsync());
        Assert.NotNull(await sessions.GetByIdAsync(session.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Import_apply_is_atomic_on_constraint_failure()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        var repository = new SessionImportRepository(database.Factory);
        var folderRepository = new SessionFolderRepository(database.Factory);
        DateTime now = DateTime.UtcNow;
        await folderRepository.AddAsync(new SessionFolder(Guid.NewGuid(), "Existing", now));
        Session candidate = Session("Candidate", "", now);
        var duplicateFolder = new SessionFolder(Guid.NewGuid(), "Existing", now);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => repository.ApplyAsync(
            [duplicateFolder], [candidate], []));

        Assert.Empty((await repository.GetSnapshotAsync()).Sessions);
        Assert.Single(await folderRepository.GetAllAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Import_apply_with_no_changes_is_a_noop()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        var repository = new SessionImportRepository(database.Factory);

        await repository.ApplyAsync(
            [], [], [], TestContext.Current.CancellationToken);

        SessionImportSnapshot snapshot = await repository.GetSnapshotAsync(
            TestContext.Current.CancellationToken);
        Assert.Empty(snapshot.Folders);
        Assert.Empty(snapshot.Sessions);
    }

    private static Session Session(
        string name,
        string folder,
        DateTime now,
        Guid? id = null) => new(
            id ?? Guid.NewGuid(), name, "example.test", 22, "user", null, folder, null, now, now);
}

internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private TemporaryDatabase(string directory, TestDbContextFactory factory)
    {
        DirectoryPath = directory;
        Factory = factory;
    }

    public string DirectoryPath { get; }
    public TestDbContextFactory Factory { get; }

    public static async Task<TemporaryDatabase> CreateAsync()
    {
        TemporaryDatabase database = await CreateUninitializedAsync();
        await using HyperTermDbContext context = await database.Factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return database;
    }

    public static Task<TemporaryDatabase> CreateUninitializedAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HyperTerm.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var factory = new TestDbContextFactory(Path.Combine(directory, "test.db"));
        return Task.FromResult(new TemporaryDatabase(directory, factory));
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(DirectoryPath, recursive: true);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestDbContextFactory(string databasePath)
    : IDbContextFactory<HyperTermDbContext>
{
    private readonly DbContextOptions<HyperTermDbContext> options =
        new DbContextOptionsBuilder<HyperTermDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

    public HyperTermDbContext CreateDbContext() => new(options);

    public Task<HyperTermDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}
