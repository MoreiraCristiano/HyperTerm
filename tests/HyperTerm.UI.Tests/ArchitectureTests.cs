using NetArchTest.Rules;

namespace HyperTerm.UI.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void View_models_do_not_depend_on_infrastructure()
    {
        NetArchTest.Rules.TestResult result = Types
            .InAssembly(typeof(HyperTerm.UI.AssemblyMarker).Assembly)
            .That()
            .ResideInNamespace("HyperTerm.UI.ViewModels")
            .ShouldNot()
            .HaveDependencyOn("HyperTerm.Infrastructure")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
