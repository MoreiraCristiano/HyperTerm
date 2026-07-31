namespace SuperTerminal.Core.Entities;

public sealed class SessionFolder
{
    private SessionFolder()
    {
    }

    public SessionFolder(Guid id, string path, DateTime createdAt)
    {
        Id = id;
        Path = path;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
}
