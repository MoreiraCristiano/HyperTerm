using System.Text.Json;
using HyperTerm.Core;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HyperTerm.Tests;

public sealed class SessionArchiveServiceTests
{
    [Fact]
    public async Task ImportRejectsOversizedNonSeekableStreamBeforeMutation()
    {
        var importRepository = new RecordingSessionImportRepository(
            new SessionImportSnapshot([], []));
        var services = new ServiceCollection();
        services.AddCore();
        services.AddSingleton<ISessionRepository, UnusedSessionRepository>();
        services.AddSingleton<ISessionFolderRepository, UnusedSessionFolderRepository>();
        services.AddSingleton<ISessionImportRepository>(importRepository);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISessionArchiveService archiveService =
            provider.GetRequiredService<ISessionArchiveService>();
        await using var inner = new MemoryStream(new byte[10 * 1024 * 1024 + 1]);
        await using var source = new NonSeekableReadStream(inner);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archiveService.ImportAsync(
                source,
                TestContext.Current.CancellationToken));

        Assert.Contains("10 MB", exception.Message);
        Assert.Equal(0, importRepository.ApplyCount);
    }

    [Fact]
    public async Task ImportAppliesFoldersAndSessionsInOneBatch()
    {
        Guid existingId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        var existingSession = new Session(
            existingId,
            "Old",
            "old.example.test",
            22,
            "old-user",
            null,
            "Existing",
            null,
            now.AddDays(-2),
            now.AddDays(-1));
        var importRepository = new RecordingSessionImportRepository(
            new SessionImportSnapshot(
                [new SessionFolder(Guid.NewGuid(), "Existing", now.AddDays(-2))],
                [existingSession]));
        var services = new ServiceCollection();
        services.AddCore();
        services.AddSingleton<ISessionRepository, UnusedSessionRepository>();
        services.AddSingleton<ISessionFolderRepository, UnusedSessionFolderRepository>();
        services.AddSingleton<ISessionImportRepository>(importRepository);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISessionArchiveService archiveService =
            provider.GetRequiredService<ISessionArchiveService>();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "HyperTerm.SessionArchive",
            version = 1,
            exportedAtUtc = now,
            folders = new[] { "Existing", "New/Child" },
            sessions = new object[]
            {
                new
                {
                    id = existingId,
                    name = "Updated",
                    host = "updated.example.test",
                    port = 22,
                    username = "updated-user",
                    folder = "Existing",
                    createdAtUtc = now.AddDays(-2),
                    updatedAtUtc = now,
                },
                new
                {
                    id = Guid.NewGuid(),
                    name = "Added",
                    host = "added.example.test",
                    port = 22,
                    username = "added-user",
                    folder = "New/Child",
                    createdAtUtc = now,
                    updatedAtUtc = now,
                },
            },
        });
        await using var source = new MemoryStream(json);

        SessionImportResult result = await archiveService.ImportAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, importRepository.ApplyCount);
        Assert.Equal(2, importRepository.AddedFolders.Count);
        Assert.Single(importRepository.AddedSessions);
        Assert.Single(importRepository.UpdatedSessions);
        Assert.Equal("Updated", importRepository.UpdatedSessions[0].Name);
        Assert.Equal(new SessionImportResult(1, 1, 2), result);
    }

    private sealed class RecordingSessionImportRepository(SessionImportSnapshot snapshot)
        : ISessionImportRepository
    {
        public int ApplyCount { get; private set; }

        public IReadOnlyList<SessionFolder> AddedFolders { get; private set; } = [];

        public IReadOnlyList<Session> AddedSessions { get; private set; } = [];

        public IReadOnlyList<Session> UpdatedSessions { get; private set; } = [];

        public Task<SessionImportSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public Task ApplyAsync(
            IReadOnlyCollection<SessionFolder> addedFolders,
            IReadOnlyCollection<Session> addedSessions,
            IReadOnlyCollection<Session> updatedSessions,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            AddedFolders = addedFolders.ToArray();
            AddedSessions = addedSessions.ToArray();
            UpdatedSessions = updatedSessions.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedSessionRepository : ISessionRepository
    {
        public Task<IReadOnlyList<Session>> GetAllAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Session?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddAsync(
            Session session,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(
            Session session,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedSessionFolderRepository : ISessionFolderRepository
    {
        public Task<IReadOnlyList<SessionFolder>> GetAllAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddAsync(
            SessionFolder folder,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> RenameTreeAsync(
            string currentPath,
            string newPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FolderDeleteResult> DeleteTreesAsync(
            IReadOnlyCollection<string> paths,
            bool force,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
