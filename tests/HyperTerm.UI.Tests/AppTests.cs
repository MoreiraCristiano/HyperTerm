using Xunit;
using Avalonia.Controls;

namespace HyperTerm.UI.Tests;

public sealed class AppTests
{
    [Fact]
    public void ConstructorDefersDesktopServiceCreation()
    {
        bool mainWindowCreated = false;
        bool lifecycleResolved = false;

        using var app = new App(
            () =>
            {
                mainWindowCreated = true;
                throw new InvalidOperationException("The window factory must remain deferred.");
            },
            () =>
            {
                lifecycleResolved = true;
                throw new InvalidOperationException("The lifecycle factory must remain deferred.");
            });

        Assert.False(mainWindowCreated);
        Assert.False(lifecycleResolved);
    }

    [Theory]
    [InlineData(WindowCloseReason.WindowClosing, true)]
    [InlineData(WindowCloseReason.Undefined, true)]
    [InlineData(WindowCloseReason.ApplicationShutdown, false)]
    [InlineData(WindowCloseReason.OSShutdown, false)]
    public void ClosePolicyOnlyHidesUserRequestedClose(
        WindowCloseReason closeReason,
        bool expected)
    {
        Assert.Equal(
            expected,
            App.ShouldHideToSystemTray(
                closeToSystemTray: true,
                explicitShutdownRequested: false,
                closeReason));
    }

    [Fact]
    public void ClosePolicyPreservesExitWhenDisabledOrExplicit()
    {
        Assert.False(App.ShouldHideToSystemTray(
            closeToSystemTray: false,
            explicitShutdownRequested: false,
            WindowCloseReason.WindowClosing));
        Assert.False(App.ShouldHideToSystemTray(
            closeToSystemTray: true,
            explicitShutdownRequested: true,
            WindowCloseReason.WindowClosing));
    }
}

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task FirstCoordinatorOwnsIdentityAndSecondSignalsIt()
    {
        string identity = "HyperTerm.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = new Services.SingleInstanceCoordinator(identity);
        await using var secondary = new Services.SingleInstanceCoordinator(identity);
        var activation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        primary.SetActivationHandler(() => activation.TrySetResult());
        primary.StartListening();

        Assert.True(await secondary.RequestActivationAsync(TimeSpan.FromSeconds(3)));
        await activation.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ActivationBeforeHandlerIsDeliveredWhenHandlerRegisters()
    {
        string identity = "HyperTerm.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = new Services.SingleInstanceCoordinator(identity);
        await using var secondary = new Services.SingleInstanceCoordinator(identity);
        var activation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening();

        Assert.True(await secondary.RequestActivationAsync(TimeSpan.FromSeconds(3)));
        primary.SetActivationHandler(() => activation.TrySetResult());

        await activation.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task DifferentIdentitiesCanBothBePrimary()
    {
        await using var first = new Services.SingleInstanceCoordinator(
            "HyperTerm.Tests." + Guid.NewGuid().ToString("N"));
        await using var second = new Services.SingleInstanceCoordinator(
            "HyperTerm.Tests." + Guid.NewGuid().ToString("N"));

        Assert.True(first.IsPrimary);
        Assert.True(second.IsPrimary);
    }

    [Fact]
    public async Task SecondaryReturnsFalseWhenPrimaryDoesNotListen()
    {
        string identity = "HyperTerm.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = new Services.SingleInstanceCoordinator(identity);
        await using var secondary = new Services.SingleInstanceCoordinator(identity);

        Assert.False(await secondary.RequestActivationAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task ExternalCancellationIsPreserved()
    {
        string identity = "HyperTerm.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = new Services.SingleInstanceCoordinator(identity);
        await using var secondary = new Services.SingleInstanceCoordinator(identity);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            secondary.RequestActivationAsync(
                TimeSpan.FromSeconds(3),
                cancellation.Token));
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var coordinator = new Services.SingleInstanceCoordinator(
            "HyperTerm.Tests." + Guid.NewGuid().ToString("N"));
        coordinator.StartListening();

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();
    }
}
