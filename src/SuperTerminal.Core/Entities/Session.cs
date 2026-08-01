namespace SuperTerminal.Core.Entities;

public sealed class Session
{
    private Session()
    {
    }

    public Session(
        Guid id,
        string name,
        string host,
        int port,
        string username,
        string? privateKey,
        string folder,
        string? notes,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        Name = name;
        Host = host;
        Port = port;
        Username = username;
        PrivateKey = privateKey;
        Folder = folder;
        Notes = notes;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; }

    public string Username { get; private set; } = string.Empty;

    public string? PrivateKey { get; private set; }

    public string Folder { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    internal void Update(
        string name,
        string host,
        int port,
        string username,
        string? privateKey,
        string folder,
        string? notes,
        DateTime updatedAt)
    {
        Name = name;
        Host = host;
        Port = port;
        Username = username;
        PrivateKey = privateKey;
        Folder = folder;
        Notes = notes;
        UpdatedAt = updatedAt;
    }

    internal void Restore(
        string name,
        string host,
        int port,
        string username,
        string? privateKey,
        string folder,
        string? notes,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Update(name, host, port, username, privateKey, folder, notes, updatedAt);
        CreatedAt = createdAt;
    }
}
