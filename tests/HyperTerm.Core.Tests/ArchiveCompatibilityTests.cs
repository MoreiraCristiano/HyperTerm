using System.Text;
using System.Text.Json;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Services;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Tests;

public sealed class ArchiveCompatibilityTests
{
    [Fact]
    public async Task Export_import_round_trip_preserves_sessions_and_expands_folders()
    {
        DateTime created = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime updated = created.AddDays(1);
        var session = new Session(
            Guid.NewGuid(), "Server", "example.test", 2222, "user", "key",
            "Work/Prod", "note", created, updated);
        var sourceSessions = new MemorySessionRepository(session);
        var sourceFolders = new MemoryFolderRepository("Work");
        var sourceImport = new RecordingImportRepository();
        var exporter = new SessionArchiveService(sourceSessions, sourceFolders, sourceImport);
        await using var archive = new MemoryStream();
        await exporter.ExportAsync(archive, TestContext.Current.CancellationToken);

        archive.Position = 0;
        var destinationImport = new RecordingImportRepository();
        var importer = new SessionArchiveService(
            new MemorySessionRepository(), new MemoryFolderRepository(), destinationImport);
        SessionImportResult result = await importer.ImportAsync(
            archive, TestContext.Current.CancellationToken);

        Assert.Equal(new SessionImportResult(1, 0, 2), result);
        Assert.Single(destinationImport.AddedSessions);
        Session imported = destinationImport.AddedSessions.Single();
        Assert.Equal(
            (session.Id, session.Name, session.Host, session.Port, session.Username,
                session.PrivateKey, session.Folder, session.Notes, created, updated),
            (imported.Id, imported.Name, imported.Host, imported.Port, imported.Username,
                imported.PrivateKey, imported.Folder, imported.Notes,
                imported.CreatedAt, imported.UpdatedAt));
        Assert.Equal(
            ["Work", "Work/Prod"],
            destinationImport.AddedFolders.Select(folder => folder.Path));
    }

    [Fact]
    public async Task Import_updates_existing_session_and_skips_known_folder()
    {
        Guid id = Guid.NewGuid();
        DateTime oldTime = DateTime.UtcNow.AddYears(-2);
        var existing = new Session(
            id, "Old", "old.test", 22, "old", null, "Work", null, oldTime, oldTime);
        var import = new RecordingImportRepository(
            new SessionImportSnapshot(
                [new SessionFolder(Guid.NewGuid(), "Work", oldTime)], [existing]));
        var service = CreateService(import);
        await using MemoryStream archive = Archive(
            sessions: [ValidSession(id, "New", "Work/Prod")]);

        SessionImportResult result = await service.ImportAsync(
            archive, TestContext.Current.CancellationToken);

        Assert.Equal(new SessionImportResult(0, 1, 1), result);
        Assert.Single(import.UpdatedSessions);
        Assert.Equal("New", import.UpdatedSessions.Single().Name);
        Assert.Equal(["Work/Prod"], import.AddedFolders.Select(folder => folder.Path));
    }

    public static TheoryData<object> InvalidDocuments => new()
    {
        new { format = "Other", version = 1, folders = Array.Empty<string>(), sessions = Array.Empty<object>() },
        new { format = "HyperTerm.SessionArchive", version = 2, folders = Array.Empty<string>(), sessions = Array.Empty<object>() },
        new { format = "HyperTerm.SessionArchive", version = 1, folders = new[] { "Work/../Bad" }, sessions = Array.Empty<object>() },
        new { format = "HyperTerm.SessionArchive", version = 1, folders = Array.Empty<string>(), sessions = new[] { ValidSession(Guid.Empty) } },
        new { format = "HyperTerm.SessionArchive", version = 1, folders = Array.Empty<string>(), sessions = new[] { ValidSession(Guid.NewGuid(), port: 0) } },
        new { format = "HyperTerm.SessionArchive", version = 1, folders = Array.Empty<string>(), sessions = new[] { ValidSession(Guid.NewGuid(), invalidTimestamps: true) } },
    };

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task Import_rejects_invalid_documents_without_mutation(object document)
    {
        var import = new RecordingImportRepository();
        var service = CreateService(import);
        await using var archive = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(
            archive, TestContext.Current.CancellationToken));

        Assert.Equal(0, import.ApplyCount);
    }

    [Fact]
    public async Task Import_rejects_duplicate_ids_and_malformed_json()
    {
        Guid id = Guid.NewGuid();
        var import = new RecordingImportRepository();
        var service = CreateService(import);
        await using MemoryStream duplicates = Archive(
            sessions: [ValidSession(id), ValidSession(id)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(
            duplicates, TestContext.Current.CancellationToken));

        await using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("{bad"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(
            malformed, TestContext.Current.CancellationToken));
        Assert.Equal(0, import.ApplyCount);
    }

    [Fact]
    public async Task Import_rejects_seekable_archive_above_exact_limit()
    {
        var service = CreateService(new RecordingImportRepository());
        await using var oversized = new MemoryStream(new byte[(10 * 1024 * 1024) + 1]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportAsync(oversized, TestContext.Current.CancellationToken));

        Assert.Contains("10 MB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_and_import_validate_stream_capabilities()
    {
        var service = CreateService(new RecordingImportRepository());
        await using var readOnly = new MemoryStream(Array.Empty<byte>(), writable: false);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(
            readOnly, TestContext.Current.CancellationToken));
        await using var writeOnly = new WriteOnlyStream();
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(
            writeOnly, TestContext.Current.CancellationToken));
    }

    private static SessionArchiveService CreateService(RecordingImportRepository import) =>
        new(new MemorySessionRepository(), new MemoryFolderRepository(), import);

    private static MemoryStream Archive(
        object[]? sessions = null,
        string[]? folders = null)
    {
        object document = new
        {
            format = "HyperTerm.SessionArchive",
            version = 1,
            exportedAtUtc = DateTime.UtcNow,
            folders = folders ?? [],
            sessions = sessions ?? [],
        };
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document));
    }

    private static object ValidSession(
        Guid id,
        string name = "Server",
        string folder = "Work",
        int port = 22,
        bool invalidTimestamps = false)
    {
        DateTime created = DateTime.UtcNow.AddDays(-1);
        return new
        {
            id,
            name,
            host = "example.test",
            port,
            username = "user",
            privateKey = " key ",
            folder,
            notes = " note ",
            createdAtUtc = created,
            updatedAtUtc = invalidTimestamps ? created.AddDays(-1) : created.AddHours(1),
        };
    }
}

internal sealed class RecordingImportRepository : ISessionImportRepository
{
    private readonly SessionImportSnapshot snapshot;

    public RecordingImportRepository(SessionImportSnapshot? snapshot = null)
    {
        this.snapshot = snapshot ?? new SessionImportSnapshot([], []);
    }

    public IReadOnlyCollection<SessionFolder> AddedFolders { get; private set; } = [];
    public IReadOnlyCollection<Session> AddedSessions { get; private set; } = [];
    public IReadOnlyCollection<Session> UpdatedSessions { get; private set; } = [];
    public int ApplyCount { get; private set; }

    public Task<SessionImportSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

    public Task ApplyAsync(
        IReadOnlyCollection<SessionFolder> addedFolders,
        IReadOnlyCollection<Session> addedSessions,
        IReadOnlyCollection<Session> updatedSessions,
        CancellationToken cancellationToken = default)
    {
        ApplyCount++;
        AddedFolders = addedFolders;
        AddedSessions = addedSessions;
        UpdatedSessions = updatedSessions;
        return Task.CompletedTask;
    }
}

internal sealed class WriteOnlyStream : MemoryStream
{
    public override bool CanRead => false;
}
