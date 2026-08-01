using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;

namespace HyperTerm.Tests;

internal sealed class FakeSessionService : ISessionService
{
    public List<Session> Sessions { get; } = [];

    public Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Session>>(Sessions);

    public Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sessions.FirstOrDefault(session => session.Id == id));

    public Task<Session> CreateAsync(
        SessionDetails details,
        CancellationToken cancellationToken = default)
    {
        Session session = CreateSession(Guid.NewGuid(), details);
        Sessions.Add(session);
        return Task.FromResult(session);
    }

    public Task<Session> UpdateAsync(
        Guid id,
        SessionDetails details,
        CancellationToken cancellationToken = default)
    {
        int index = Sessions.FindIndex(session => session.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException();
        }

        Session session = CreateSession(id, details);
        Sessions[index] = session;
        return Task.FromResult(session);
    }

    public Task<Session> MoveAsync(
        Guid id,
        string folder,
        CancellationToken cancellationToken = default)
    {
        Session current = Sessions.FirstOrDefault(session => session.Id == id)
            ?? throw new KeyNotFoundException();
        var details = new SessionDetails(
            current.Name,
            current.Host,
            current.Port,
            current.Username,
            current.PrivateKey,
            folder,
            current.Notes);
        return UpdateAsync(id, details, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        int removed = Sessions.RemoveAll(session => session.Id == id);
        return removed == 0 ? Task.FromException(new KeyNotFoundException()) : Task.CompletedTask;
    }

    public static Session CreateSession(
        Guid id,
        SessionDetails details) =>
        new(
            id,
            details.Name,
            details.Host,
            details.Port,
            details.Username,
            details.PrivateKey,
            details.Folder,
            details.Notes,
            DateTime.UtcNow,
            DateTime.UtcNow);
}

internal sealed class FakeFolderService : ISessionFolderService
{
    public List<SessionFolder> Folders { get; } = [];
    public IReadOnlyCollection<string>? DeletedPaths { get; private set; }
    public bool UsedForceDelete { get; private set; }

    public Task<IReadOnlyList<SessionFolder>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionFolder>>(Folders);

    public Task<SessionFolder> CreateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var folder = new SessionFolder(Guid.NewGuid(), path, DateTime.UtcNow);
        Folders.Add(folder);
        return Task.FromResult(folder);
    }

    public Task RenameAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<FolderDeleteResult> DeleteAsync(
        IReadOnlyCollection<string> paths,
        bool force,
        CancellationToken cancellationToken = default)
    {
        DeletedPaths = paths;
        UsedForceDelete = force;
        return Task.FromResult(new FolderDeleteResult(paths.Count, force ? 2 : 0));
    }
}

internal sealed class FakeTerminalSessionFactory : ITerminalSessionFactory
{
    private static readonly TerminalSessionDefinition Definition =
        new("pwsh.exe", [], string.Empty);

    public Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(Definition);

    public Task<TerminalSessionDefinition> CreateAsync(
        Session session,
        CancellationToken cancellationToken = default) => Task.FromResult(Definition);
}

internal sealed class FakePtySessionFactory : IPtySessionFactory
{
    public Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IPtySession>(new FakePtySession());
}

internal sealed class FakePtySession : IPtySession
{
    public event EventHandler<string>? OutputReceived
    {
        add { }
        remove { }
    }
    public event EventHandler<int>? Exited;
    public Task WriteAsync(string data, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public void Resize(int columns, int rows) { }
    public void Kill() => Exited?.Invoke(this, 0);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeSettingsService(bool exists) : ISettingsService
{
    public ApplicationSettings Value { get; private set; } = new();
    public bool Exists() => exists;
    public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Value);
    public Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        Value = settings;
        return Task.CompletedTask;
    }
}

internal sealed class FakeThemeService : IThemeService
{
    public void Apply(string theme) { }
}

internal sealed class FakeExecutablePicker : IExecutableFilePicker
{
    public Task<string?> PickPowerShellAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

internal sealed class FakeArchiveService : ISessionArchiveService
{
    public Task ExportAsync(Stream destination, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task<SessionImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SessionImportResult(0, 0, 0));
}

internal sealed class FakeArchiveFilePicker : ISessionArchiveFilePicker
{
    public Task<Stream?> OpenExportStreamAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(null);
    public Task<Stream?> OpenImportStreamAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(null);
}

internal sealed class FakeSystemFontService : ISystemFontService
{
    public IReadOnlyList<string> GetInstalledFontFamilies() => ["Cascadia Mono"];
}
