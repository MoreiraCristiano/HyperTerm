using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace SuperTerminal.UI.Services;

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
            throw new InvalidOperationException("Janela principal não está disponível.");
        }

        IReadOnlyList<IStorageFile> files = await desktop.MainWindow.StorageProvider
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecionar PowerShell 7 (pwsh.exe)",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Executável PowerShell")
                    {
                        Patterns = ["pwsh.exe"],
                        MimeTypes = ["application/x-msdownload"],
                    },
                    new FilePickerFileType("Executáveis")
                    {
                        Patterns = ["*.exe"],
                        MimeTypes = ["application/x-msdownload"],
                    },
                ],
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
