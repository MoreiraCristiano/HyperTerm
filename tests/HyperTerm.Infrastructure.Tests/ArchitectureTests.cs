using NetArchTest.Rules;

namespace HyperTerm.Infrastructure.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Infrastructure_does_not_depend_on_ui()
    {
        NetArchTest.Rules.TestResult result = Types
            .InAssembly(typeof(HyperTerm.Infrastructure.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("HyperTerm.UI")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
