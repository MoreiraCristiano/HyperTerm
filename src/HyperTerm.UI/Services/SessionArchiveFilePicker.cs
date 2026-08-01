using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace HyperTerm.UI.Services;

internal sealed class SessionArchiveFilePicker : ISessionArchiveFilePicker
{
    private static readonly FilePickerFileType HyperTermArchiveType = new(
        "HyperTerm session archive")
    {
        Patterns = ["*.hyperterm.json"],
        MimeTypes = ["application/json"],
    };

    private static readonly FilePickerFileType JsonType = new("JSON file")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    public async Task<Stream?> OpenExportStreamAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IStorageProvider storageProvider = GetStorageProvider();
        IStorageFile? file = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export sessions and folders",
                SuggestedFileName =
                    $"hyperterm-sessions-{DateTime.Now:yyyyMMdd}.hyperterm.json",
                DefaultExtension = "json",
                ShowOverwritePrompt = true,
                FileTypeChoices = [HyperTermArchiveType],
            });
        cancellationToken.ThrowIfCancellationRequested();
        return file is null ? null : await file.OpenWriteAsync();
    }

    public async Task<Stream?> OpenImportStreamAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IStorageProvider storageProvider = GetStorageProvider();
        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import sessions and folders",
                AllowMultiple = false,
                FileTypeFilter = [HyperTermArchiveType, JsonType],
            });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : await files[0].OpenReadAsync();
    }

    private static IStorageProvider GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is not
                IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            throw new InvalidOperationException("The main window is not available.");
        }

        return desktop.MainWindow.StorageProvider;
    }
}
