using System.ComponentModel;
using System.Diagnostics;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.Services;

internal sealed class WindowsApplicationUpdateLauncher : IApplicationUpdateLauncher
{
    public Task<bool> LaunchAsync(
        PreparedApplicationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return Task.Run(() => Launch(update, cancellationToken), cancellationToken);
    }

    private static bool Launch(
        PreparedApplicationUpdate update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string targetDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (!File.Exists(Path.Combine(targetDirectory, "HyperTerm.manifest.json")))
        {
            throw new InvalidOperationException(
                "Automatic installation is available only in packaged HyperTerm releases.");
        }

        bool requiresElevation = !CanWriteToDirectory(targetDirectory);
        var startInfo = new ProcessStartInfo(update.ExecutablePath)
        {
            WorkingDirectory = update.StagingDirectory,
            UseShellExecute = requiresElevation,
        };
        if (requiresElevation)
        {
            startInfo.Verb = "runas";
        }

        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(targetDirectory);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            using Process process = Process.Start(startInfo) ??
                throw new InvalidOperationException("The update installer did not start.");
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return false;
        }
    }

    private static bool CanWriteToDirectory(string directory)
    {
        string probePath = Path.Combine(directory, $".hyperterm-update-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }
}
