using System.Text.Json;
using SuperTerminal.Core.Abstractions.Settings;
using SuperTerminal.Core.Models;
using SuperTerminal.Infrastructure.Storage;

namespace SuperTerminal.Infrastructure.Settings;

internal sealed class JsonSettingsService(IApplicationPathProvider pathProvider) : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim accessLock = new(1, 1);

    public async Task<ApplicationSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await accessLock.WaitAsync(cancellationToken);
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

        await accessLock.WaitAsync(cancellationToken);
        try
        {
            string temporaryPath = $"{pathProvider.SettingsPath}.tmp";
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
            }

            File.Move(temporaryPath, pathProvider.SettingsPath, overwrite: true);
        }
        finally
        {
            accessLock.Release();
        }
    }
}
