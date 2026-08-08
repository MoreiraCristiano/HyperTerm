using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private async Task SelectPowerShellAsync()
    {
        string? selectedPath = await PickPowerShellAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SettingsPowerShellPath = selectedPath;
            SettingsError = null;
        }
    }

    [RelayCommand]
    private async Task UseDefaultPowerShellAsync()
    {
        SettingsPowerShellPath = "pwsh.exe";
        await CompletePowerShellSetupAsync();
    }

    [RelayCommand]
    private async Task ChooseInitialPowerShellAsync()
    {
        PowerShellSetupError = null;
        string? selectedPath = await PickPowerShellAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            PowerShellSetupError = SettingsError;
            return;
        }

        SettingsPowerShellPath = selectedPath;
        await CompletePowerShellSetupAsync();
    }
}
