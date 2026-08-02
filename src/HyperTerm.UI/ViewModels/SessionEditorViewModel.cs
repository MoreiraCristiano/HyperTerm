using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionEditorViewModel(ISessionService sessionService)
    : ViewModelBase
{
    private Guid? editingSessionId;
    private string editingSessionFolder = string.Empty;
    private SessionListItemViewModel? sessionPendingDeletion;

    public event Action<Guid?>? SessionsChanged;
    public event Action<string>? StatusRequested;

    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private bool isDeleteConfirmationOpen;
    [ObservableProperty] private string editorTitle = "New session";
    [ObservableProperty] private string editorName = string.Empty;
    [ObservableProperty] private string editorHost = string.Empty;
    [ObservableProperty] private decimal editorPort = 22;
    [ObservableProperty] private string editorUsername = string.Empty;
    [ObservableProperty] private string editorNotes = string.Empty;
    [ObservableProperty] private string? editorError;

    public void OpenNew(string _)
    {
        editingSessionId = null;
        editingSessionFolder = string.Empty;
        EditorTitle = "New session";
        EditorName = string.Empty;
        EditorHost = string.Empty;
        EditorPort = 22;
        EditorUsername = string.Empty;
        EditorNotes = string.Empty;
        EditorError = null;
        IsEditorOpen = true;
    }

    public void OpenEdit(SessionListItemViewModel session)
    {
        editingSessionId = session.Id;
        editingSessionFolder = session.Folder;
        EditorTitle = "Edit session";
        EditorName = session.Name;
        EditorHost = session.Host;
        EditorPort = session.Port;
        EditorUsername = session.Username;
        EditorNotes = session.Notes ?? string.Empty;
        EditorError = null;
        IsEditorOpen = true;
    }

    public void RequestDelete(SessionListItemViewModel session)
    {
        sessionPendingDeletion = session;
        IsDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    public void CancelEditor()
    {
        IsEditorOpen = false;
        EditorError = null;
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        EditorError = null;
        try
        {
            var details = new SessionDetails(
                EditorName,
                EditorHost,
                decimal.ToInt32(EditorPort),
                EditorUsername,
                null,
                editingSessionFolder,
                EditorNotes);
            Session session = editingSessionId is Guid id
                ? await sessionService.UpdateAsync(id, details)
                : await sessionService.CreateAsync(details);
            bool wasEditing = editingSessionId.HasValue;
            IsEditorOpen = false;
            StatusRequested?.Invoke(wasEditing ? "Session updated" : "Session created");
            SessionsChanged?.Invoke(session.Id);
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException or KeyNotFoundException)
        {
            EditorError = exception.Message;
        }
    }

    [RelayCommand]
    public void CancelDeleteSession()
    {
        IsDeleteConfirmationOpen = false;
        sessionPendingDeletion = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSessionAsync()
    {
        if (sessionPendingDeletion is null)
        {
            IsDeleteConfirmationOpen = false;
            return;
        }

        Guid id = sessionPendingDeletion.Id;
        string name = sessionPendingDeletion.Name;
        try
        {
            await sessionService.DeleteAsync(id);
            StatusRequested?.Invoke($"Session ‘{name}’ deleted");
        }
        catch (KeyNotFoundException)
        {
            StatusRequested?.Invoke("Session not found");
        }

        IsDeleteConfirmationOpen = false;
        sessionPendingDeletion = null;
        SessionsChanged?.Invoke(null);
    }
}
