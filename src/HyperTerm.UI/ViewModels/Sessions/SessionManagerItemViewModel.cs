using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperTerm.Core.Entities;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionManagerItemViewModel : ObservableObject
{
    public SessionManagerItemViewModel(Session session)
    {
        Id = session.Id;
        Name = session.Name;
        Host = session.Host;
        Port = session.Port;
        Username = session.Username;
        PrivateKey = session.PrivateKey;
        Folder = session.Folder;
        Notes = session.Notes;
        CreatedAt = session.CreatedAt;
        UpdatedAt = session.UpdatedAt;
    }

    private SessionManagerItemViewModel()
    {
        Id = Guid.Empty;
        IsDraft = true;
        Port = 22;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }

    public bool IsDraft { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Endpoint))]
    private string host = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Endpoint))]
    private int port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Endpoint))]
    private string username = string.Empty;

    internal string? PrivateKey { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderDisplay))]
    private string folder = string.Empty;

    [ObservableProperty]
    private string? notes;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }

    public string DisplayName => IsDraft && string.IsNullOrWhiteSpace(Name)
        ? "New session"
        : Name;

    public string Endpoint
    {
        get
        {
            if (IsDraft && string.IsNullOrWhiteSpace(Host))
            {
                return "Connection not configured";
            }

            string hostEndpoint = $"{Host}:{Port}";
            return string.IsNullOrWhiteSpace(Username)
                ? hostEndpoint
                : $"{Username}@{hostEndpoint}";
        }
    }

    public string FolderDisplay => string.IsNullOrWhiteSpace(Folder) ? "Root" : Folder;

    public string UpdatedAtDisplay => UpdatedAt.ToLocalTime().ToString(
        "g",
        CultureInfo.CurrentCulture);

    public static SessionManagerItemViewModel CreateDraft() => new();
}

public enum SessionManagerSortField
{
    Name,
    Host,
    Username,
    Folder,
    UpdatedAt,
}

public sealed record SessionManagerSortOption(string Name, SessionManagerSortField Field);

public sealed record SessionManagerFolderOption(string Name, string Value);
