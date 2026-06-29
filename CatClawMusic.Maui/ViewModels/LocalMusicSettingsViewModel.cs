using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class LocalMusicSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _musicFolderPath = "";

    [ObservableProperty]
    private string _scanStatus = "未扫描";

    [ObservableProperty]
    private double _scanProgress = 0;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private bool _isMp3Enabled = true;

    [ObservableProperty]
    private bool _isFlacEnabled = true;

    [ObservableProperty]
    private bool _isWavEnabled = true;

    public IAsyncRelayCommand SelectFolderCommand { get; }
    public IAsyncRelayCommand ScanMusicCommand { get; }

    public LocalMusicSettingsViewModel()
    {
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        ScanMusicCommand = new AsyncRelayCommand(ScanMusicAsync);
    }

    private async Task SelectFolderAsync()
    {
        // Open folder picker
        await Task.CompletedTask;
    }

    private async Task ScanMusicAsync()
    {
        IsScanning = true;
        try
        {
            // Scan music files
            await Task.CompletedTask;
        }
        finally
        {
            IsScanning = false;
        }
    }
}
