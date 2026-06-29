namespace CatClawMusic.Maui.Pages;

public partial class BackupRestorePage : ContentPage
{
    public BackupRestorePage()
    {
        InitializeComponent();
    }

    private void OnDeleteBackupClicked(object? sender, EventArgs e)
    {
        // Handle delete backup button click
        if (sender is Button button && button.BindingContext is CatClawMusic.Maui.ViewModels.BackupFile file)
        {
            // Call ViewModel to delete the file
            if (BindingContext is CatClawMusic.Maui.ViewModels.BackupRestoreViewModel viewModel)
            {
                viewModel.DeleteBackupCommand.ExecuteAsync(file);
            }
        }
    }
}
