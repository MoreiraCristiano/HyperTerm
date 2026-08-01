using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class FolderEditorViewModel(
    ISessionFolderService sessionFolderService) : ViewModelBase
{
    private string? editingFolderPath;
    private string[] foldersPendingDeletion = [];

    public event Action? FoldersChanged;
    public event Action<string>? StatusRequested;

    [ObservableProperty] private bool isFolderEditorOpen;
    [ObservableProperty] private string folderPath = string.Empty;
    [ObservableProperty] private string folderEditorTitle = "New folder";
    [ObservableProperty] private string folderEditorAction = "Create";
    [ObservableProperty] private string? folderError;
    [ObservableProperty] private bool isFolderDeleteConfirmationOpen;
    [ObservableProperty] private string folderDeleteTitle = "Delete folder?";
    [ObservableProperty] private string folderDeleteMessage = string.Empty;
    [ObservableProperty] private bool forceDeleteFolders;
    [ObservableProperty] private string? folderDeleteError;

    public void OpenNew(string initialPath)
    {
        editingFolderPath = null;
        FolderEditorTitle = "New folder";
        FolderEditorAction = "Create";
        FolderPath = initialPath;
        FolderError = null;
        IsFolderEditorOpen = true;
    }

    public void OpenEdit(string path)
    {
        editingFolderPath = path;
        FolderEditorTitle = "Edit folder";
        FolderEditorAction = "Save";
        FolderPath = path;
        FolderError = null;
        IsFolderEditorOpen = true;
    }

    public void OpenDelete(string[] paths)
    {
        foldersPendingDeletion = paths;
        FolderDeleteTitle = paths.Length == 1
            ? "Delete folder?"
            : $"Delete {paths.Length} folders?";
        FolderDeleteMessage = paths.Length == 1
            ? $"Folder ‘{paths[0]}’ and its subfolders will be deleted."
            : $"{paths.Length} selected folders and their subfolders will be deleted.";
        ForceDeleteFolders = false;
        FolderDeleteError = null;
        IsFolderDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    public void CancelFolderEditor()
    {
        IsFolderEditorOpen = false;
        FolderError = null;
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        try
        {
            string resultingPath;
            if (editingFolderPath is null)
            {
                SessionFolder folder = await sessionFolderService.CreateAsync(FolderPath);
                resultingPath = folder.Path;
                StatusRequested?.Invoke($"Folder ‘{resultingPath}’ created");
            }
            else
            {
                await sessionFolderService.RenameAsync(editingFolderPath, FolderPath);
                resultingPath = FolderPath.Trim().Replace('\\', '/').Trim('/');
                StatusRequested?.Invoke($"Folder renamed to ‘{resultingPath}’");
            }

            IsFolderEditorOpen = false;
            FolderError = null;
            editingFolderPath = null;
            FoldersChanged?.Invoke();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            FolderError = exception.Message;
        }
    }

    [RelayCommand]
    public void CancelDeleteFolder()
    {
        IsFolderDeleteConfirmationOpen = false;
        FolderDeleteError = null;
        ForceDeleteFolders = false;
        foldersPendingDeletion = [];
    }

    [RelayCommand]
    private async Task ConfirmDeleteFolderAsync()
    {
        try
        {
            FolderDeleteResult result = await sessionFolderService.DeleteAsync(
                foldersPendingDeletion,
                ForceDeleteFolders);
            IsFolderDeleteConfirmationOpen = false;
            FolderDeleteError = null;
            ForceDeleteFolders = false;
            foldersPendingDeletion = [];
            StatusRequested?.Invoke(result.DeletedSessions == 0
                ? $"Deleted {result.DeletedFolders} folder(s)"
                : $"Deleted {result.DeletedFolders} folder(s) and {result.DeletedSessions} session(s)");
            FoldersChanged?.Invoke();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or KeyNotFoundException)
        {
            FolderDeleteError = exception.Message;
        }
    }
}
