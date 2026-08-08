using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionTreeNodeViewModel : ViewModelBase
{
    private SessionTreeNodeViewModel(
        string name,
        string path,
        SessionListItemViewModel? session,
        bool hasItems = false)
    {
        Name = name;
        Path = path;
        Session = session;
        HasItems = hasItems;
    }

    public string Name { get; }

    public string Path { get; }

    public SessionListItemViewModel? Session { get; }

    public ObservableCollection<SessionTreeNodeViewModel> Children { get; } = [];

    public bool IsFolder => Session is null;

    public bool IsSession => Session is not null;

    public bool HasItems { get; }

    public bool IsEmptyFolder => IsFolder && !HasItems;

    public string Endpoint => Session?.Endpoint ?? string.Empty;

    [ObservableProperty]
    private bool isSelectedForDeletion;

    [ObservableProperty]
    private bool isExpanded;

    public static SessionTreeNodeViewModel CreateFolder(
        string name,
        string path,
        bool hasItems) =>
        new(name, path, null, hasItems);

    public static SessionTreeNodeViewModel CreateSession(SessionListItemViewModel session) =>
        new(session.Name, session.Folder, session);
}
