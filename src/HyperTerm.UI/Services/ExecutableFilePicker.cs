using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace HyperTerm.UI.Services;

internal sealed class ExecutableFilePicker : IExecutableFilePicker
{
    public async Task<string?> PickPowerShellAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            throw new InvalidOperationException("The main window is not available.");
        }

        IReadOnlyList<IStorageFile> files = await desktop.MainWindow.StorageProvider
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select PowerShell 7 (pwsh.exe)",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("PowerShell executable")
                    {
                        Patterns = ["pwsh.exe"],
                        MimeTypes = ["application/x-msdownload"],
                    },
                    new FilePickerFileType("Executables")
                    {
                        Patterns = ["*.exe"],
                        MimeTypes = ["application/x-msdownload"],
                    },
                ],
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
