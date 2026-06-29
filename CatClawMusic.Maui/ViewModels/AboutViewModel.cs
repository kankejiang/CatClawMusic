using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty]
    private string _version = "v1.5.3";

    [ObservableProperty]
    private string _copyright = "© 2024 CatClawMusic. All rights reserved.";

    public IRelayCommand ViewLicenseCommand { get; }
    public IRelayCommand JoinGroupCommand { get; }
    public IRelayCommand OpenGitHubCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public AboutViewModel()
    {
        ViewLicenseCommand = new RelayCommand(ViewLicense);
        JoinGroupCommand = new RelayCommand(JoinGroup);
        OpenGitHubCommand = new RelayCommand(OpenGitHub);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync);
    }

    private void ViewLicense()
    {
        // Open license page
    }

    private void JoinGroup()
    {
        // Open group chat/join link
    }

    private void OpenGitHub()
    {
        // Open GitHub repository
    }

    private async Task CheckUpdateAsync()
    {
        // Check for app updates
        await Task.CompletedTask;
    }
}
