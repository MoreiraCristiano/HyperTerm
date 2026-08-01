namespace SuperTerminal.Infrastructure.Storage;

internal sealed class ApplicationPathProvider : IApplicationPathProvider
{
    public ApplicationPathProvider()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string legacyDirectory = Path.Combine(localApplicationData, "SuperTerminal");
        ApplicationDirectory = Path.Combine(localApplicationData, "hyperTerms");
        Directory.CreateDirectory(ApplicationDirectory);

        DatabasePath = Path.Combine(ApplicationDirectory, "hyperterms.db");
        SettingsPath = Path.Combine(ApplicationDirectory, "settings.json");

        CopyLegacyFileIfNeeded(
            Path.Combine(legacyDirectory, "superterminal.db"),
            DatabasePath);
        CopyLegacyFileIfNeeded(
            Path.Combine(legacyDirectory, "settings.json"),
            SettingsPath);
    }

    public string ApplicationDirectory { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    private static void CopyLegacyFileIfNeeded(string legacyPath, string destinationPath)
    {
        if (!File.Exists(destinationPath) && File.Exists(legacyPath))
        {
            File.Copy(legacyPath, destinationPath);
        }
    }
}
