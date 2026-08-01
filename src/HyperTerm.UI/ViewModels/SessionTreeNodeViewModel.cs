using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionTreeNodeViewModel : ViewModelBase
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

    public bool IsNestedSession =>
        Session is not null && !string.IsNullOrWhiteSpace(Session.Folder);

    public string Endpoint => Session?.Endpoint ?? string.Empty;

    [ObservableProperty]
    private bool isSelectedForDeletion;

    [ObservableProperty]
    private bool isExpanded;

    public static SessionTreeNodeViewModel CreateFolder(string name, string path) =>
        new(name, path, null);

    public static SessionTreeNodeViewModel CreateSession(SessionListItemViewModel session) =>
        new(session.Name, session.Folder, session);
}
