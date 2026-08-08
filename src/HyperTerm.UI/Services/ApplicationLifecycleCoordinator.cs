using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.Services;

internal sealed class ApplicationLifecycleCoordinator(
    IDatabaseInitializer databaseInitializer,
    MainWindowViewModel viewModel,
    ILogger<ApplicationLifecycleCoordinator> logger) : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private Task? initializationTask;
    private Task? shutdownTask;
    private bool disposed;

    public Task InitializeAsync()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return initializationTask ??= InitializeCoreAsync(lifetimeCancellation.Token);
        }
    }

    public Task ShutdownAsync()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return shutdownTask ??= ShutdownCoreAsync();
        }
    }

    public void CancelInitialization()
    {
        lock (gate)
        {
            if (!disposed)
            {
                lifetimeCancellation.Cancel();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Application initialization started.");
            Task databaseInitialization = databaseInitializer.InitializeAsync(cancellationToken);
            await viewModel.InitializeSettingsAsync(cancellationToken);
            await databaseInitialization;
            await viewModel.InitializeWorkspaceAsync(cancellationToken);
            viewModel.CompleteInitialization();
            logger.LogInformation("Application initialization completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Application initialization canceled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application initialization failed.");
            viewModel.ReportStartupFailure(exception);
        }
    }

    private async Task ShutdownCoreAsync()
    {
        logger.LogInformation("Application shutdown started.");
        lifetimeCancellation.Cancel();

        Task? pendingInitialization;
        lock (gate)
        {
            pendingInitialization = initializationTask;
        }

        if (pendingInitialization is not null)
        {
            await pendingInitialization;
        }

        await viewModel.ShutdownAsync();
        logger.LogInformation("Application shutdown completed.");
    }
}
