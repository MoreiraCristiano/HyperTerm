using System.Globalization;
using System.Text.RegularExpressions;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PsmuxService(
    ILogger<PsmuxService> logger,
    IPsmuxCommandClient commandClient) : IPsmuxService
{
    private const string Namespace = "hyperterm";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly Regex SessionNamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$",
        RegexOptions.CultureInvariant);

    public async Task<PsmuxAvailability> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        string? executable = commandClient.TryResolveExecutable();
        if (executable is null)
        {
            return new PsmuxAvailability(
                false,
                null,
                null,
                "psmux.exe was not found in HyperTerm’s bundled tools or PATH. " +
                "Use the complete ZIP package or install psmux.");
        }

        try
        {
            PsmuxCommandResult result = await commandClient.RunAsync(
                executable,
                ["--version"],
                CommandTimeout,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                return new PsmuxAvailability(
                    false,
                    executable,
                    null,
                    FormatFailure("psmux --version", result));
            }

            string version = result.StandardOutput.Trim();
            return new PsmuxAvailability(true, executable, version, null);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Failed to probe psmux.");
            return new PsmuxAvailability(false, executable, null, exception.Message);
        }
    }

    public async Task<IReadOnlyList<PsmuxSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        string executable = ResolveExecutable();
        return await ListSessionsAsync(executable, cancellationToken);
    }

    public async Task<TerminalSessionDefinition> CreateSessionDefinitionAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string executable = ResolveExecutable();
        bool sessionCreated = false;
        try
        {
            await RunRequiredAsync(
                executable,
                BuildDetachedSessionArguments(name),
                "psmux new-session",
                cancellationToken);
            sessionCreated = true;
            await RunRequiredAsync(
                executable,
                BuildSplitArguments(name),
                "psmux split-window",
                cancellationToken);
            await RunRequiredAsync(
                executable,
                BuildSelectLeftArguments(name),
                "psmux select-pane",
                cancellationToken);

            return CreateDefinition(
                name,
                BuildAttachSessionArguments(name),
                executable);
        }
        catch
        {
            if (sessionCreated)
            {
                await TryKillSessionAsync(executable, name);
            }

            throw;
        }
    }

    public Task<TerminalSessionDefinition> CreateAttachDefinitionAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDefinition(name, BuildAttachSessionArguments(name)));
    }

    public async Task KillSessionAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        string executable = ResolveExecutable();
        PsmuxCommandResult result = await commandClient.RunAsync(
            executable,
            ["-L", Namespace, "kill-session", "-t", name],
            CommandTimeout,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new TerminalLaunchException(FormatFailure("psmux kill-session", result));
        }
    }

    public async Task<bool> TryStopServerAsync(
        CancellationToken cancellationToken = default)
    {
        string executable = ResolveExecutable();
        IReadOnlyList<PsmuxSessionInfo> sessions = await ListSessionsAsync(
            executable,
            cancellationToken);
        if (sessions.Any(session => session.IsAttached))
        {
            return false;
        }

        PsmuxCommandResult result = await commandClient.RunAsync(
            executable,
            BuildKillServerArguments(),
            CommandTimeout,
            cancellationToken);
        if (result.ExitCode != 0 && !IsNoSessionsResult(result))
        {
            throw new TerminalLaunchException(FormatFailure("psmux kill-server", result));
        }

        return true;
    }

    internal static IReadOnlyList<string> BuildDetachedSessionArguments(string name)
    {
        ValidateName(name);
        return ["-L", Namespace, "new-session", "-d", "-s", name];
    }

    internal static IReadOnlyList<string> BuildSplitArguments(string name)
    {
        ValidateName(name);
        return ["-L", Namespace, "split-window", "-h", "-t", name];
    }

    internal static IReadOnlyList<string> BuildSelectLeftArguments(string name)
    {
        ValidateName(name);
        return ["-L", Namespace, "select-pane", "-L", "-t", name];
    }

    internal static IReadOnlyList<string> BuildAttachSessionArguments(string name)
    {
        ValidateName(name);
        return ["-L", Namespace, "attach-session", "-t", name];
    }

    internal static IReadOnlyList<string> BuildKillServerArguments() =>
        ["-L", Namespace, "kill-server"];

    private async Task<IReadOnlyList<PsmuxSessionInfo>> ListSessionsAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        PsmuxCommandResult result = await commandClient.RunAsync(
            executable,
            [
                "-L",
                Namespace,
                "list-sessions",
                "-F",
                "#{session_name}\t#{session_windows}\t#{session_attached}",
            ],
            CommandTimeout,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsNoSessionsResult(result))
            {
                return [];
            }

            throw new TerminalLaunchException(FormatFailure("psmux list-sessions", result));
        }

        var sessions = new List<PsmuxSessionInfo>();
        foreach (string line in result.StandardOutput.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 3 ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int windows) ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int attached))
            {
                throw new TerminalLaunchException(
                    $"psmux returned an invalid session row: ‘{line}’.");
            }

            sessions.Add(new PsmuxSessionInfo(fields[0], windows, attached > 0));
        }

        return sessions.OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private TerminalSessionDefinition CreateDefinition(
        string name,
        IReadOnlyList<string> arguments,
        string? resolvedExecutable = null)
    {
        return new TerminalSessionDefinition(
            resolvedExecutable ?? ResolveExecutable(),
            arguments,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            TerminalSessionKind.Psmux,
            name);
    }

    private async Task RunRequiredAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string command,
        CancellationToken cancellationToken)
    {
        PsmuxCommandResult result = await commandClient.RunAsync(
            executable,
            arguments,
            CommandTimeout,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new TerminalLaunchException(FormatFailure(command, result));
        }
    }

    private async Task TryKillSessionAsync(string executable, string name)
    {
        try
        {
            await commandClient.RunAsync(
                executable,
                ["-L", Namespace, "kill-session", "-t", name],
                CommandTimeout,
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private string ResolveExecutable() =>
        commandClient.TryResolveExecutable() ??
        throw new TerminalLaunchException(
            "psmux.exe was not found in HyperTerm’s bundled tools or PATH. " +
            "Use the complete ZIP package or install psmux.");

    private static void ValidateName(string name)
    {
        if (!SessionNamePattern.IsMatch(name))
        {
            throw new ArgumentException(
                "Use 1–64 letters, numbers, underscores, or hyphens; start with a letter or number.",
                nameof(name));
        }
    }

    private static bool IsNoSessionsResult(PsmuxCommandResult result)
    {
        string error = $"{result.StandardError}\n{result.StandardOutput}";
        return error.Contains("no server", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("no sessions", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("failed to connect", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFailure(string command, PsmuxCommandResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return string.IsNullOrEmpty(detail)
            ? $"{command} failed with exit code {result.ExitCode}."
            : $"{command} failed: {detail}";
    }

    internal static string GetDefaultStartingDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
