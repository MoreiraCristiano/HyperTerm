namespace HyperTerm.Core.Exceptions;

public sealed class TerminalLaunchException : Exception
{
    public TerminalLaunchException(string message)
        : base(message)
    {
    }

    public TerminalLaunchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
