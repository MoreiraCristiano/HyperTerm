using CommunityToolkit.Mvvm.ComponentModel;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class TerminalProfileItemViewModel : ViewModelBase
{
    public TerminalProfileItemViewModel(TerminalProfile profile)
    {
        Id = profile.Id;
        name = profile.Name;
        executablePath = profile.ExecutablePath;
        argumentsText = string.Join(Environment.NewLine, profile.Arguments);
        startingDirectory = profile.StartingDirectory;
    }

    public string Id { get; }

    public bool IsRecommended => Id.Equals(
        TerminalProfileIds.PowerShell,
        StringComparison.OrdinalIgnoreCase);

    public bool CanDelete => !IsDefault;

    public bool CanSetDefault => !IsDefault && IsAvailable;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string executablePath;

    [ObservableProperty]
    private string argumentsText;

    [ObservableProperty]
    private string startingDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanSetDefault))]
    private bool isDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetDefault))]
    private bool isAvailable;

    [ObservableProperty]
    private string availabilityMessage = string.Empty;

    public TerminalProfile ToModel() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        ExecutablePath = ExecutablePath.Trim().Trim('"'),
        Arguments = ArgumentsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToArray(),
        StartingDirectory = StartingDirectory.Trim().Trim('"'),
    };
}
