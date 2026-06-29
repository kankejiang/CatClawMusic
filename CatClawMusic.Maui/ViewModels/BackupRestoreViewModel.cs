using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public class BackupFile
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CreatedDate { get; set; }
}

public partial class BackupRestoreViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<BackupFile> _backupFiles = new();

    public IAsyncRelayCommand CreateBackupCommand { get; }
    public IAsyncRelayCommand RestoreBackupCommand { get; }
    public IAsyncRelayCommand<BackupFile> DeleteBackupCommand { get; }

    public BackupRestoreViewModel()
    {
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync);
        DeleteBackupCommand = new AsyncRelayCommand<BackupFile>(DeleteBackupAsync);
    }

    private async Task CreateBackupAsync()
    {
        // Create backup file
        await Task.CompletedTask;
    }

    private async Task RestoreBackupAsync()
    {
        // Restore from backup file
        await Task.CompletedTask;
    }

    private async Task DeleteBackupAsync(BackupFile? file)
    {
        if (file != null)
        {
            // Delete backup file
            await Task.CompletedTask;
        }
    }
}
