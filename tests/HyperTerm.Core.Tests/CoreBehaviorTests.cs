using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using HyperTerm.Core.Services;

namespace HyperTerm.Core.Tests;

public sealed class SessionValidatorTests
{
    public static TheoryData<SessionDetails> InvalidDetails => new()
    {
        ValidDetails() with { Name = " " },
        ValidDetails() with { Host = "" },
        ValidDetails() with { Username = "\t" },
        ValidDetails() with { Port = 0 },
        ValidDetails() with { Port = 65_536 },
        ValidDetails() with { Name = new string('n', 201) },
        ValidDetails() with { Host = new string('h', 254) },
        ValidDetails() with { Username = new string('u', 129) },
        ValidDetails() with { PrivateKey = new string('k', 1025) },
        ValidDetails() with { Folder = new string('f', 501) },
        ValidDetails() with { Notes = new string('x', 4001) },
    };

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public void Validate_rejects_invalid_boundaries(SessionDetails details) =>
        Assert.ThrowsAny<ArgumentException>(() => SessionValidator.Validate(details));

    [Theory]
    [InlineData(1)]
    [InlineData(22)]
    [InlineData(65535)]
    public void Validate_accepts_port_boundaries(int port) =>
        SessionValidator.Validate(ValidDetails() with { Port = port });

    internal static SessionDetails ValidDetails() =>
        new("Server", "example.test", 22, "user", null, "Work/Prod", null);
}

public sealed class SessionFolderPathTests
{
    [Theory]
    [InlineData(" Work\\Production/ ", "Work/Production")]
    [InlineData("///Work//Production///", "Work/Production")]
    [InlineData("Single", "Single")]
    public void Normalize_canonicalizes_separators(string input, string expected) =>
        Assert.Equal(expected, SessionFolderPath.Normalize(input));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Work/../Production")]
    [InlineData("///")]
    public void Normalize_rejects_traversal_and_empty_paths(string input) =>
        Assert.Throws<ArgumentException>(() => SessionFolderPath.Normalize(input));

    [Fact]
    public void ExpandAncestors_returns_parent_before_child() =>
        Assert.Equal(
            ["Work", "Work/Production", "Work/Production/Primary"],
            SessionFolderPath.ExpandAncestors("Work/Production/Primary"));
}

public sealed class SessionServiceTests
{
    [Fact]
    public async Task Create_trims_and_normalizes_all_values()
    {
        var repository = new MemorySessionRepository();
        var service = new SessionService(repository);

        Session created = await service.CreateAsync(new SessionDetails(
            " Server ", " example.test ", 22, " user ", " key ",
            " Work\\Production ", " notes "));

        Assert.Equal("Server", created.Name);
        Assert.Equal("example.test", created.Host);
        Assert.Equal("user", created.Username);
        Assert.Equal("key", created.PrivateKey);
        Assert.Equal("Work/Production", created.Folder);
        Assert.Equal("notes", created.Notes);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
        Assert.Same(created, Assert.Single(repository.Sessions));
    }

    [Fact]
    public async Task Update_preserves_creation_and_changes_update_timestamp()
    {
        var existing = CreateSession(DateTime.UtcNow.AddDays(-2));
        var repository = new MemorySessionRepository(existing);
        var service = new SessionService(repository);

        Session updated = await service.UpdateAsync(
            existing.Id,
            SessionValidatorTests.ValidDetails() with { Name = "Changed" });

        Assert.Equal("Changed", updated.Name);
        Assert.Equal(existing.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > updated.CreatedAt);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public async Task Operations_reject_empty_or_unknown_ids()
    {
        var service = new SessionService(new MemorySessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetByIdAsync(Guid.Empty));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateAsync(Guid.NewGuid(), SessionValidatorTests.ValidDetails()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_repository()
    {
        var repository = new MemorySessionRepository();
        var service = new SessionService(repository);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAllAsync(source.Token));
    }

    [Fact]
    public async Task Move_and_delete_update_repository_state()
    {
        var existing = CreateSession(DateTime.UtcNow.AddDays(-1));
        var repository = new MemorySessionRepository(existing);
        var service = new SessionService(repository);

        Session moved = await service.MoveAsync(
            existing.Id, " Work\\Prod ", TestContext.Current.CancellationToken);
        Assert.Equal("Work/Prod", moved.Folder);
        Assert.Equal(1, repository.UpdateCount);

        await service.DeleteAsync(existing.Id, TestContext.Current.CancellationToken);
        Assert.Empty(repository.Sessions);
    }

    [Fact]
    public async Task Create_normalizes_blank_optional_values_to_null()
    {
        var service = new SessionService(new MemorySessionRepository());

        Session created = await service.CreateAsync(
            SessionValidatorTests.ValidDetails() with
            {
                PrivateKey = " ",
                Notes = "\t",
                Folder = " ",
            },
            TestContext.Current.CancellationToken);

        Assert.Null(created.PrivateKey);
        Assert.Null(created.Notes);
        Assert.Equal(string.Empty, created.Folder);
    }

    private static Session CreateSession(DateTime createdAt) => new(
        Guid.NewGuid(), "Old", "old.test", 22, "user", null, "", null,
        createdAt, createdAt);
}

public sealed class SessionFolderServiceTests
{
    [Fact]
    public async Task Create_rejects_case_insensitive_duplicate()
    {
        var repository = new MemoryFolderRepository("Work/Production");
        var service = new SessionFolderService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync("work/production"));
    }

    [Fact]
    public async Task Delete_collapses_descendants_to_root()
    {
        var repository = new MemoryFolderRepository("Work", "Work/Prod");
        var service = new SessionFolderService(repository);

        await service.DeleteAsync(["Work/Prod", "Work", "WORK/Prod"], force: true);

        Assert.Equal(["Work"], repository.LastDeletePaths);
        Assert.True(repository.LastForce);
    }

    [Fact]
    public async Task Rename_rejects_move_inside_itself()
    {
        var service = new SessionFolderService(new MemoryFolderRepository("Work"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RenameAsync("Work", "Work/Child"));
    }

    [Fact]
    public async Task Create_and_rename_cover_success_noop_and_missing_cases()
    {
        var repository = new MemoryFolderRepository();
        var service = new SessionFolderService(repository);
        SessionFolder created = await service.CreateAsync(
            " Work\\Prod ", TestContext.Current.CancellationToken);
        Assert.Equal("Work/Prod", created.Path);

        await service.RenameAsync("Work/Prod", "work/prod", TestContext.Current.CancellationToken);
        Assert.Equal(0, repository.RenameCount);

        repository.RenameResult = false;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RenameAsync(
            "Work/Prod", "Archive/Prod", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_requires_at_least_one_path()
    {
        var service = new SessionFolderService(new MemoryFolderRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.DeleteAsync(null!, force: false, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync([], force: false, TestContext.Current.CancellationToken));
    }
}

internal sealed class MemorySessionRepository(params Session[] sessions) : ISessionRepository
{
    public List<Session> Sessions { get; } = [.. sessions];
    public int UpdateCount { get; private set; }

    public Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Session>>(Sessions);
    }

    public Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sessions.SingleOrDefault(session => session.Id == id));
    }

    public Task AddAsync(Session session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateCount++;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sessions.RemoveAll(session => session.Id == id) > 0);
    }
}

internal sealed class MemoryFolderRepository(params string[] paths) : ISessionFolderRepository
{
    private readonly List<SessionFolder> folders = paths
        .Select(path => new SessionFolder(Guid.NewGuid(), path, DateTime.UtcNow))
        .ToList();

    public IReadOnlyCollection<string>? LastDeletePaths { get; private set; }
    public bool LastForce { get; private set; }
    public bool RenameResult { get; set; } = true;
    public int RenameCount { get; private set; }

    public Task<IReadOnlyList<SessionFolder>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionFolder>>(folders);

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(folders.Any(folder =>
            folder.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(SessionFolder folder, CancellationToken cancellationToken = default)
    {
        folders.Add(folder);
        return Task.CompletedTask;
    }

    public Task<bool> RenameTreeAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        RenameCount++;
        return Task.FromResult(RenameResult);
    }

    public Task<FolderDeleteResult> DeleteTreesAsync(
        IReadOnlyCollection<string> paths,
        bool force,
        CancellationToken cancellationToken = default)
    {
        LastDeletePaths = paths;
        LastForce = force;
        return Task.FromResult(new FolderDeleteResult(paths.Count, 0));
    }
}
