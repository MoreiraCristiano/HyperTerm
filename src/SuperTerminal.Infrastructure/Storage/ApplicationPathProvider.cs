namespace SuperTerminal.Infrastructure.Storage;

internal sealed class ApplicationPathProvider : IApplicationPathProvider
{
    public ApplicationPathProvider()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        ApplicationDirectory = Path.Combine(localApplicationData, "SuperTerminal");
        Directory.CreateDirectory(ApplicationDirectory);

        DatabasePath = Path.Combine(ApplicationDirectory, "superterminal.db");
        SettingsPath = Path.Combine(ApplicationDirectory, "settings.json");
    }

    public string ApplicationDirectory { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }
}
