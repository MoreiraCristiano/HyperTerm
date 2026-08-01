namespace HyperTerm.Infrastructure.Storage;

internal sealed class ApplicationPathProvider : IApplicationPathProvider
{
    public ApplicationPathProvider()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string previousDirectory = Path.Combine(localApplicationData, "hyperTerms");
        string legacyDirectory = Path.Combine(localApplicationData, "SuperTerminal");
        ApplicationDirectory = Path.Combine(localApplicationData, "HyperTerm");
        Directory.CreateDirectory(ApplicationDirectory);

        DatabasePath = Path.Combine(ApplicationDirectory, "hyperterm.db");
        SettingsPath = Path.Combine(ApplicationDirectory, "settings.json");

        CopyFirstAvailableIfNeeded(
            [
                Path.Combine(previousDirectory, "hyperterms.db"),
                Path.Combine(legacyDirectory, "superterminal.db"),
            ],
            DatabasePath);
        CopyFirstAvailableIfNeeded(
            [
                Path.Combine(previousDirectory, "settings.json"),
                Path.Combine(legacyDirectory, "settings.json"),
            ],
            SettingsPath);
    }

    public string ApplicationDirectory { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    private static void CopyFirstAvailableIfNeeded(
        IEnumerable<string> sourcePaths,
        string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        string? sourcePath = sourcePaths.FirstOrDefault(File.Exists);
        if (sourcePath is not null)
        {
            File.Copy(sourcePath, destinationPath);
        }
    }
}
