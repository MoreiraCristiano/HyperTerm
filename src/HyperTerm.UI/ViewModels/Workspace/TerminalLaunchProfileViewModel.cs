using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed class TerminalLaunchProfileViewModel(
    TerminalProfile profile,
    bool isAvailable,
    bool isDefault)
{
    public string Id => profile.Id;

    public string Name => profile.Name;

    public bool IsAvailable { get; } = isAvailable;

    public bool IsDefault { get; } = isDefault;
}
