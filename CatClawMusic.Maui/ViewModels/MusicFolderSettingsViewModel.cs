using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class MusicFolderSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<string> _musicFolders = new();

    [ObservableProperty]
    private bool _scanSubfolders = true;

    [ObservableProperty]
    private bool _autoScan = false;

    public IAsyncRelayCommand AddFolderCommand { get; }
    public IRelayCommand<string> RemoveFolderCommand { get; }

    public MusicFolderSettingsViewModel()
    {
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        RemoveFolderCommand = new RelayCommand<string>(RemoveFolder);
    }

    private async Task AddFolderAsync()
    {
        // Open folder picker and add to list
        await Task.CompletedTask;
    }

    private void RemoveFolder(string? folder)
    {
        if (folder != null)
        {
            MusicFolders.Remove(folder);
        }
    }
}
