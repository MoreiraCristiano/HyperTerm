using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Tests;

public sealed class PsmuxSessionNameValidatorTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("Session_01")]
    [InlineData("1-production")]
    [InlineData("a23456789012345678901234567890123456789012345678901234567890123")]
    public void Accepts_supported_names(string name) =>
        Assert.True(PsmuxSessionNameValidator.IsValid(name));

    [Fact]
    public void Accepts_64_character_name() =>
        Assert.True(PsmuxSessionNameValidator.IsValid(new string('a', 64)));

    [Theory]
    [InlineData("")]
    [InlineData("_session")]
    [InlineData("-session")]
    [InlineData("has space")]
    [InlineData("has.dot")]
    [InlineData("área")]
    public void Rejects_unsupported_names(string name) =>
        Assert.False(PsmuxSessionNameValidator.IsValid(name));

    [Fact]
    public void Rejects_names_longer_than_64_characters() =>
        Assert.False(PsmuxSessionNameValidator.IsValid(new string('a', 65)));
}
