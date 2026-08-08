using NetArchTest.Rules;

namespace HyperTerm.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Core_does_not_depend_on_infrastructure_or_ui()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(typeof(HyperTerm.Core.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("HyperTerm.Infrastructure", "HyperTerm.UI")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
