using System.Collections.ObjectModel;

namespace SuperTerminal.UI.ViewModels;

public sealed class SessionTreeNodeViewModel
{
    private SessionTreeNodeViewModel(
        string name,
        string path,
        SessionListItemViewModel? session)
    {
        Name = name;
        Path = path;
        Session = session;
    }

    public string Name { get; }

    public string Path { get; }

    public SessionListItemViewModel? Session { get; }

    public ObservableCollection<SessionTreeNodeViewModel> Children { get; } = [];

    public bool IsFolder => Session is null;

    public bool IsSession => Session is not null;

    public string Endpoint => Session?.Endpoint ?? string.Empty;

    public static SessionTreeNodeViewModel CreateFolder(string name, string path) =>
        new(name, path, null);

    public static SessionTreeNodeViewModel CreateSession(SessionListItemViewModel session) =>
        new(session.Name, session.Folder, session);
}
