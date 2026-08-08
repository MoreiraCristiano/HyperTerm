using System.Text.Json;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Storage;

namespace HyperTerm.Infrastructure.Settings;

internal sealed class JsonSettingsService(IApplicationPathProvider pathProvider)
    : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim accessLock = new(1, 1);

    public bool Exists() => File.Exists(pathProvider.SettingsPath);

    public void Dispose() => accessLock.Dispose();

    public async Task<ApplicationSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(pathProvider.SettingsPath))
            {
                return new ApplicationSettings();
            }

            await using FileStream stream = File.OpenRead(pathProvider.SettingsPath);
            return await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                           stream,
                           SerializerOptions,
                           cancellationToken)
                       .ConfigureAwait(false)
                   ?? new ApplicationSettings();
        }
        finally
        {
            accessLock.Release();
        }
    }

    public async Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            string settingsDirectory = Path.GetDirectoryName(pathProvider.SettingsPath)
                ?? throw new InvalidOperationException("Settings path has no parent directory.");
            Directory.CreateDirectory(settingsDirectory);
            temporaryPath = Path.Combine(
                settingsDirectory,
                $".{Path.GetFileName(pathProvider.SettingsPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(pathProvider.SettingsPath))
            {
                File.Replace(
                    temporaryPath,
                    pathProvider.SettingsPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, pathProvider.SettingsPath);
            }

            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            accessLock.Release();
        }
    }
}
