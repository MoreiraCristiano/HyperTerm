using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.Services;

internal sealed class ApplicationExceptionMonitor(
    ILogger<ApplicationExceptionMonitor> logger) : IDisposable
{
    private bool started;

    public void Start()
    {
        if (started)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        started = true;
    }

    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        started = false;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        if (eventArgs.ExceptionObject is Exception exception)
        {
            logger.LogCritical(
                exception,
                "Unhandled application exception. Terminating={IsTerminating}.",
                eventArgs.IsTerminating);
        }
        else
        {
            logger.LogCritical(
                "Unhandled non-Exception application failure. Terminating={IsTerminating}.",
                eventArgs.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        logger.LogError(eventArgs.Exception, "Unobserved task exception.");
        eventArgs.SetObserved();
    }

    private void OnDispatcherUnhandledException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs eventArgs) =>
        logger.LogCritical(eventArgs.Exception, "Unhandled UI dispatcher exception.");
}
