using SuperTerminal.Core.Entities;

namespace SuperTerminal.UI.ViewModels;

public sealed class SessionListItemViewModel(Session session)
{
    public Guid Id { get; } = session.Id;

    public string Name { get; } = session.Name;

    public string Host { get; } = session.Host;

    public int Port { get; } = session.Port;

    public string Username { get; } = session.Username;

    public string? PrivateKey { get; } = session.PrivateKey;

    public string Folder { get; } = session.Folder;

    public string? Notes { get; } = session.Notes;

    public string Endpoint => $"{Username}@{Host}:{Port}";
}
