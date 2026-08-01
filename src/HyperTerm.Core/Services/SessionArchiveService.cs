using System.Text.Json;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Services;

internal sealed class SessionArchiveService(
    ISessionRepository sessionRepository,
    ISessionFolderRepository folderRepository) : ISessionArchiveService
{
    private const string FormatName = "HyperTerm.SessionArchive";
    private const int CurrentVersion = 1;
    private const long MaximumArchiveBytes = 10 * 1024 * 1024;
    private const int MaximumItemCount = 10_000;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task ExportAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        }

        IReadOnlyList<SessionFolder> folders =
            await folderRepository.GetAllAsync(cancellationToken);
        IReadOnlyList<Session> sessions =
            await sessionRepository.GetAllAsync(cancellationToken);

        var document = new ArchiveDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            ExportedAtUtc = DateTime.UtcNow,
            Folders = folders.Select(folder => folder.Path).ToArray(),
            Sessions = sessions.Select(ArchiveSession.FromEntity).ToArray(),
        };

        await JsonSerializer.SerializeAsync(
            destination,
            document,
            SerializerOptions,
            cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    public async Task<SessionImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream is not readable.", nameof(source));
        }

        if (source.CanSeek && source.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The archive exceeds the 10 MB size limit.");
        }

        ArchiveDocument document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<ArchiveDocument>(
                           source,
                           SerializerOptions,
                           cancellationToken)
                       ?? throw new InvalidDataException("The archive is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The selected file is not valid HyperTerm JSON.", exception);
        }

        ValidatedArchive archive = Validate(document);
        IReadOnlyList<SessionFolder> existingFolders =
            await folderRepository.GetAllAsync(cancellationToken);
        var knownFolders = existingFolders
            .Select(folder => folder.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int addedFolders = 0;
        foreach (string path in archive.Folders
                     .OrderBy(path => path.Count(character => character == '/'))
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!knownFolders.Add(path))
            {
                continue;
            }

            await folderRepository.AddAsync(
                new SessionFolder(Guid.NewGuid(), path, DateTime.UtcNow),
                cancellationToken);
            addedFolders++;
        }

        IReadOnlyList<Session> existingSessions =
            await sessionRepository.GetAllAsync(cancellationToken);
        Dictionary<Guid, Session> sessionsById =
            existingSessions.ToDictionary(session => session.Id);
        int addedSessions = 0;
        int updatedSessions = 0;

        foreach (ValidatedSession imported in archive.Sessions)
        {
            if (sessionsById.TryGetValue(imported.Id, out Session? existing))
            {
                existing.Restore(
                    imported.Details.Name,
                    imported.Details.Host,
                    imported.Details.Port,
                    imported.Details.Username,
                    imported.Details.PrivateKey,
                    imported.Details.Folder,
                    imported.Details.Notes,
                    imported.CreatedAtUtc,
                    imported.UpdatedAtUtc);
                await sessionRepository.UpdateAsync(existing, cancellationToken);
                updatedSessions++;
                continue;
            }

            var session = new Session(
                imported.Id,
                imported.Details.Name,
                imported.Details.Host,
                imported.Details.Port,
                imported.Details.Username,
                imported.Details.PrivateKey,
                imported.Details.Folder,
                imported.Details.Notes,
                imported.CreatedAtUtc,
                imported.UpdatedAtUtc);
            await sessionRepository.AddAsync(session, cancellationToken);
            sessionsById.Add(session.Id, session);
            addedSessions++;
        }

        return new SessionImportResult(addedSessions, updatedSessions, addedFolders);
    }

    private static ValidatedArchive Validate(ArchiveDocument document)
    {
        if (!string.Equals(document.Format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected file is not a HyperTerm session archive.");
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Archive version {document.Version} is not supported. Expected version {CurrentVersion}.");
        }

        string[] folderEntries = document.Folders ?? [];
        ArchiveSession[] sessionEntries = document.Sessions ?? [];
        if (folderEntries.Length > MaximumItemCount || sessionEntries.Length > MaximumItemCount)
        {
            throw new InvalidDataException("The archive contains too many sessions or folders.");
        }

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string path in folderEntries)
            {
                foreach (string ancestor in SessionFolderPath.ExpandAncestors(
                             SessionFolderPath.Normalize(path)))
                {
                    folders.Add(ancestor);
                }
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The archive contains an invalid folder path.", exception);
        }

        var sessionIds = new HashSet<Guid>();
        var sessions = new List<ValidatedSession>(sessionEntries.Length);
        foreach (ArchiveSession entry in sessionEntries)
        {
            if (entry.Id == Guid.Empty || !sessionIds.Add(entry.Id))
            {
                throw new InvalidDataException("Every archived session must have a unique non-empty ID.");
            }

            string folder;
            try
            {
                folder = SessionFolderPath.NormalizeOptional(entry.Folder);
                var details = new SessionDetails(
                    entry.Name?.Trim() ?? string.Empty,
                    entry.Host?.Trim() ?? string.Empty,
                    entry.Port,
                    entry.Username?.Trim() ?? string.Empty,
                    NormalizeOptional(entry.PrivateKey),
                    folder,
                    NormalizeOptional(entry.Notes));
                SessionValidator.Validate(details);

                if (entry.CreatedAtUtc == default || entry.UpdatedAtUtc == default ||
                    entry.UpdatedAtUtc < entry.CreatedAtUtc)
                {
                    throw new InvalidDataException("The archive contains invalid session timestamps.");
                }

                foreach (string ancestor in SessionFolderPath.ExpandAncestors(folder))
                {
                    folders.Add(ancestor);
                }

                sessions.Add(new ValidatedSession(
                    entry.Id,
                    details,
                    entry.CreatedAtUtc.ToUniversalTime(),
                    entry.UpdatedAtUtc.ToUniversalTime()));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Session '{entry.Name ?? entry.Id.ToString()}' contains invalid data.",
                    exception);
            }
        }

        return new ValidatedArchive(folders.ToArray(), sessions.ToArray());
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ArchiveDocument
    {
        public ArchiveDocument()
        {
        }

        public string? Format { get; init; }

        public int Version { get; init; }

        public DateTime ExportedAtUtc { get; init; }

        public string[]? Folders { get; init; }

        public ArchiveSession[]? Sessions { get; init; }
    }

    private sealed class ArchiveSession
    {
        public ArchiveSession()
        {
        }

        public Guid Id { get; init; }

        public string? Name { get; init; }

        public string? Host { get; init; }

        public int Port { get; init; }

        public string? Username { get; init; }

        public string? PrivateKey { get; init; }

        public string? Folder { get; init; }

        public string? Notes { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime UpdatedAtUtc { get; init; }

        public static ArchiveSession FromEntity(Session session) => new()
        {
            Id = session.Id,
            Name = session.Name,
            Host = session.Host,
            Port = session.Port,
            Username = session.Username,
            PrivateKey = session.PrivateKey,
            Folder = session.Folder,
            Notes = session.Notes,
            CreatedAtUtc = session.CreatedAt.ToUniversalTime(),
            UpdatedAtUtc = session.UpdatedAt.ToUniversalTime(),
        };
    }

    private sealed record ValidatedArchive(
        string[] Folders,
        ValidatedSession[] Sessions);

    private sealed record ValidatedSession(
        Guid Id,
        SessionDetails Details,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
