using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

namespace HyperTerm.UI.Services;

internal sealed class LogInteractionService : ILogInteractionService
{
    public async Task CopyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not
                IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard is null)
        {
            throw new InvalidOperationException("The main window clipboard is unavailable.");
        }

        using var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await desktop.MainWindow.Clipboard.SetDataAsync(data);
    }

    public Task OpenFolderAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }
}
