using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private void AddTerminalProfile()
    {
        string name = CreateUniqueProfileName();
        var profile = new TerminalProfileItemViewModel(new TerminalProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ExecutablePath = string.Empty,
        });
        SubscribeToProfile(profile);
        TerminalProfiles.Add(profile);
        RefreshProfileAvailability(profile);
    }

    [RelayCommand]
    private void DeleteTerminalProfile(TerminalProfileItemViewModel? profile)
    {
        if (profile is null || profile.Id.Equals(
                DefaultTerminalProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        profile.PropertyChanged -= OnTerminalProfilePropertyChanged;
        TerminalProfiles.Remove(profile);
    }

    [RelayCommand]
    private void SetDefaultTerminalProfile(TerminalProfileItemViewModel? profile)
    {
        if (profile?.IsAvailable == true)
        {
            DefaultTerminalProfileId = profile.Id;
            foreach (TerminalProfileItemViewModel item in TerminalProfiles)
            {
                item.IsDefault = item.Id == DefaultTerminalProfileId;
            }
        }
    }

    [RelayCommand]
    private async Task BrowseTerminalProfileAsync(TerminalProfileItemViewModel? profile)
    {
        if (profile is null)
        {
            return;
        }

        try
        {
            string? path = await executableFilePicker.PickExecutableAsync(
                $"Select executable for {profile.Name}");
            if (path is not null)
            {
                profile.ExecutablePath = path;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SettingsError = $"Could not select the executable: {exception.Message}";
        }
    }

    private void LoadTerminalProfiles(ApplicationSettings settings)
    {
        foreach (TerminalProfileItemViewModel profile in TerminalProfiles)
        {
            profile.PropertyChanged -= OnTerminalProfilePropertyChanged;
        }

        TerminalProfiles.Clear();
        foreach (TerminalProfile model in settings.TerminalProfiles)
        {
            var profile = new TerminalProfileItemViewModel(model);
            profile.IsDefault = profile.Id.Equals(
                settings.DefaultTerminalProfileId,
                StringComparison.OrdinalIgnoreCase);
            SubscribeToProfile(profile);
            TerminalProfiles.Add(profile);
            RefreshProfileAvailability(profile);
        }

        DefaultTerminalProfileId = settings.DefaultTerminalProfileId;
    }

    private void SubscribeToProfile(TerminalProfileItemViewModel profile) =>
        profile.PropertyChanged += OnTerminalProfilePropertyChanged;

    private void OnTerminalProfilePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (sender is not TerminalProfileItemViewModel profile)
        {
            return;
        }

        if (eventArgs.PropertyName == nameof(TerminalProfileItemViewModel.ExecutablePath))
        {
            RefreshProfileAvailability(profile);
        }
    }

    private void RefreshProfileAvailability(TerminalProfileItemViewModel profile)
    {
        string path = profile.ExecutablePath.Trim().Trim('"');
        bool available = terminalProfileResolver?.TryResolve(path) is not null ||
            terminalProfileResolver is null && path.Length > 0;
        profile.IsAvailable = available;
        profile.AvailabilityMessage = available ? "Available" : "Executable not found";
    }

    private bool TryBuildTerminalProfiles(out IReadOnlyList<TerminalProfile> profiles)
    {
        profiles = [];
        if (TerminalProfiles.Count == 0)
        {
            SettingsError = "At least one terminal profile is required.";
            return false;
        }

        if (TerminalProfiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
        {
            SettingsError = "Every terminal profile needs a name.";
            return false;
        }

        if (TerminalProfiles.GroupBy(profile => profile.Name.Trim(),
                StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            SettingsError = "Terminal profile names must be unique.";
            return false;
        }

        foreach (TerminalProfileItemViewModel profile in TerminalProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
            {
                SettingsError = $"Choose an executable for ‘{profile.Name}’.";
                return false;
            }

            string directory = profile.StartingDirectory.Trim().Trim('"');
            if (directory.Length > 0 && (!Path.IsPathRooted(directory) || !Directory.Exists(directory)))
            {
                SettingsError = $"Starting directory for ‘{profile.Name}’ does not exist.";
                return false;
            }
        }

        TerminalProfileItemViewModel? defaultProfile = TerminalProfiles.FirstOrDefault(profile =>
            profile.Id == DefaultTerminalProfileId);
        if (defaultProfile is null || !defaultProfile.IsAvailable)
        {
            SettingsError = "Choose an available default terminal profile.";
            return false;
        }

        profiles = TerminalProfiles.Select(profile => profile.ToModel()).ToArray();
        return true;
    }

    private string CreateUniqueProfileName()
    {
        const string baseName = "New profile";
        string name = baseName;
        int suffix = 2;
        while (TerminalProfiles.Any(profile => profile.Name.Equals(
                   name,
                   StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {suffix++}";
        }

        return name;
    }
}
