using System.Diagnostics;

namespace HyperTerm.UI.Controls;

internal static class WebTerminalPageResolver
{
    public static string Resolve()
    {
        string relativePath = Path.Combine("WebTerminal", "dist", "index.html");
        string deployedPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(deployedPath))
        {
            return deployedPath;
        }

        using Process process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            string? moduleDirectory = Path.GetDirectoryName(module.FileName);
            if (string.IsNullOrEmpty(moduleDirectory))
            {
                continue;
            }

            string extractedPath = Path.Combine(moduleDirectory, relativePath);
            if (File.Exists(extractedPath))
            {
                return extractedPath;
            }
        }

        throw new FileNotFoundException(
            "The web terminal page was not found in the application or bundle extraction directories.",
            deployedPath);
    }
}
