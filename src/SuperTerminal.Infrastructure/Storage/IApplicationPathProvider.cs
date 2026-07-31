namespace SuperTerminal.Infrastructure.Storage;

internal interface IApplicationPathProvider
{
    string ApplicationDirectory { get; }

    string DatabasePath { get; }

    string SettingsPath { get; }
}
