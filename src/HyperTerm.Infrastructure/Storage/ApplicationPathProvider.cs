namespace HyperTerm.Infrastructure.Storage;

internal sealed class ApplicationPathProvider : IApplicationPathProvider
{
    public ApplicationPathProvider()
    {
        string? testDataRoot = GetTestDataRoot();
        if (testDataRoot is not null)
        {
            ApplicationDirectory = testDataRoot;
            Directory.CreateDirectory(ApplicationDirectory);
            DatabasePath = Path.Combine(ApplicationDirectory, "hyperterm.db");
            SettingsPath = Path.Combine(ApplicationDirectory, "settings.json");
            LogsDirectory = Path.Combine(ApplicationDirectory, "logs");
            return;
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string previousDirectory = Path.Combine(localApplicationData, "hyperTerms");
        string legacyDirectory = Path.Combine(localApplicationData, "SuperTerminal");
        ApplicationDirectory = Path.Combine(localApplicationData, "HyperTerm");
        Directory.CreateDirectory(ApplicationDirectory);

        DatabasePath = Path.Combine(ApplicationDirectory, "hyperterm.db");
        SettingsPath = Path.Combine(ApplicationDirectory, "settings.json");
        LogsDirectory = Path.Combine(ApplicationDirectory, "logs");

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
    public string LogsDirectory { get; }

    private static string? GetTestDataRoot()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HYPERTERM_TEST_MODE"),
                "1",
                StringComparison.Ordinal))
        {
            return null;
        }

        string? configuredRoot = Environment.GetEnvironmentVariable("HYPERTERM_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException(
                "HYPERTERM_DATA_ROOT is required when HYPERTERM_TEST_MODE=1.");
        }

        return Path.GetFullPath(configuredRoot);
    }

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
