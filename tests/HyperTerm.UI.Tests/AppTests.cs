using Xunit;

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
}
