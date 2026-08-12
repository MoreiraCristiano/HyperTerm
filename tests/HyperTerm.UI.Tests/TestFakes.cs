using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;

namespace HyperTerm.UI.Tests;

internal sealed class FakeDatabaseInitializer : IDatabaseInitializer
{
    public int InitializeCalls { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCalls++;
        return Task.CompletedTask;
    }
}

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
        CancellationToken cancellationToken = default)
    {
        SessionFolder[] folders = Folders.Where(folder =>
            folder.Path.Equals(currentPath, StringComparison.OrdinalIgnoreCase) ||
            folder.Path.StartsWith($"{currentPath}/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (SessionFolder folder in folders)
        {
            Folders.Remove(folder);
            string path = folder.Path.Equals(currentPath, StringComparison.OrdinalIgnoreCase)
                ? newPath
                : $"{newPath}{folder.Path[currentPath.Length..]}";
            Folders.Add(new SessionFolder(folder.Id, path, folder.CreatedAt));
        }

        return Task.CompletedTask;
    }

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

    public Task<TerminalSessionDefinition> CreateProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Definition with
        {
            ProfileId = profileId,
            DisplayName = profileId,
        });

    public Task<TerminalSessionDefinition> CreateAsync(
        Session session,
        CancellationToken cancellationToken = default) => Task.FromResult(Definition);
}

internal sealed class FakePtySessionFactory : IPtySessionFactory
{
    public int CreateCount { get; private set; }
    public FakePtySession? LastSession { get; private set; }

    public Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreateCount++;
        LastSession = new FakePtySession();
        return Task.FromResult<IPtySession>(LastSession);
    }
}

internal sealed class FakePtySession : IPtySession
{
    private readonly TaskCompletionSource<int> completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;
    public TerminalSessionState State { get; private set; } = TerminalSessionState.Running;
    public Task<int> Completion => completion.Task;
    public List<string> Writes { get; } = [];
    public int DisposeCount { get; private set; }
    public (int Columns, int Rows)? Size { get; private set; }
    public Task WriteAsync(string data, CancellationToken cancellationToken = default) =>
        State == TerminalSessionState.Running
            ? WriteCoreAsync(data, cancellationToken)
            : Task.CompletedTask;
    public void Resize(int columns, int rows) => Size = (columns, rows);
    public void Kill()
    {
        State = TerminalSessionState.Exited;
        completion.TrySetResult(0);
        Exited?.Invoke(this, 0);
    }
    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        State = TerminalSessionState.Disposed;
        completion.TrySetResult(-1);
        return ValueTask.CompletedTask;
    }

    public void RaiseOutput(string output) => OutputReceived?.Invoke(this, output);

    public void RaiseExit(int exitCode)
    {
        State = TerminalSessionState.Exited;
        completion.TrySetResult(exitCode);
        Exited?.Invoke(this, exitCode);
    }

    private Task WriteCoreAsync(string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add(data);
        return Task.CompletedTask;
    }
}

internal sealed class FakePsmuxService : IPsmuxService
{
    public List<PsmuxSessionInfo> Sessions { get; } = [];
    public List<string> KilledSessions { get; } = [];
    public bool IsAvailable { get; set; } = true;
    public string? Error { get; set; }
    public Exception? KillError { get; set; }
    public bool StopServerResult { get; set; } = true;
    public Exception? StopServerError { get; set; }
    public int StopServerCalls { get; private set; }

    public Task<PsmuxAvailability> ProbeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PsmuxAvailability(
            IsAvailable,
            IsAvailable ? @"C:\Tools\psmux.exe" : null,
            IsAvailable ? "psmux 3.3.7" : null,
            Error));

    public Task<IReadOnlyList<PsmuxSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PsmuxSessionInfo>>(Sessions.ToArray());

    public Task<TerminalSessionDefinition> CreateSessionDefinitionAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        Sessions.Add(new PsmuxSessionInfo(name, 2, true));
        return Task.FromResult(CreateDefinition(name));
    }

    public Task<TerminalSessionDefinition> CreateAttachDefinitionAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDefinition(name));

    public Task KillSessionAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (KillError is not null)
        {
            return Task.FromException(KillError);
        }

        KilledSessions.Add(name);
        Sessions.RemoveAll(session => session.Name == name);
        return Task.CompletedTask;
    }

    public Task<bool> TryStopServerAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopServerCalls++;
        return StopServerError is null
            ? Task.FromResult(StopServerResult)
            : Task.FromException<bool>(StopServerError);
    }

    private static TerminalSessionDefinition CreateDefinition(string name) =>
        new(
            @"C:\Tools\psmux.exe",
            ["-L", "hyperterm", "attach-session", "-t", name],
            string.Empty,
            TerminalSessionKind.Psmux,
            name);
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
    public List<string> AppliedThemes { get; } = [];

    public void Apply(string theme) => AppliedThemes.Add(theme);
}

internal sealed class FakeExecutablePicker(string? selectedPath = null) : IExecutableFilePicker
{
    public Task<string?> PickExecutableAsync(
        string title,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(selectedPath);
}

internal sealed class FakeTerminalProfileResolver(params string[] availableExecutables)
    : ITerminalProfileResolver
{
    public string? TryResolve(string configuredPath) =>
        availableExecutables.Contains(configuredPath, StringComparer.OrdinalIgnoreCase)
            ? configuredPath
            : null;

    public string Resolve(string configuredPath) =>
        TryResolve(configuredPath) ?? throw new InvalidOperationException();
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

internal sealed class FakeApplicationLogService : IApplicationLogService
{
    private readonly TaskCompletionSource readStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsEnabled { get; private set; } = true;
    public bool PreviousRunCrashed { get; init; }
    public string LogsDirectory { get; } = @"C:\Logs";
    public string Content { get; set; } = string.Empty;
    public event EventHandler? LogChanged;

    public void Configure(bool enabled) => IsEnabled = enabled;

    public Task<string> ReadTailAsync(
        int maximumBytes = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        readStarted.TrySetResult();
        return Task.FromResult(Content);
    }

    public Task WaitForReadAsync(CancellationToken cancellationToken) =>
        readStarted.Task.WaitAsync(cancellationToken);

    public void CompleteRun() { }

    public void RaiseChanged() => LogChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeLogInteractionService : ILogInteractionService
{
    public string? CopiedText { get; private set; }
    public string? OpenedPath { get; private set; }

    public Task CopyAsync(string text, CancellationToken cancellationToken = default)
    {
        CopiedText = text;
        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        OpenedPath = path;
        return Task.CompletedTask;
    }
}
