using System.Windows.Forms;

namespace TheWitcher2SaveEditor.Services;

public sealed class FileDialogService
{
    public Task<string?> PickSaveFileAsync()
    {
        return RunStaDialogAsync(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select a Witcher 2 save file",
                Filter = "Witcher 2 Save Files (*.sav)|*.sav|All Files (*.*)|*.*"
            };

            // Try common Witcher 2 save locations
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var w2SavePath = Path.Combine(docsPath, "Witcher 2", "gamesaves");
            if (Directory.Exists(w2SavePath))
                dialog.InitialDirectory = w2SavePath;

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        });
    }

    public Task<string?> PickSaveLocationAsync(string defaultName)
    {
        return RunStaDialogAsync(() =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save modified file",
                Filter = "Witcher 2 Save Files (*.sav)|*.sav|All Files (*.*)|*.*",
                FileName = defaultName
            };

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        });
    }

    private static Task<string?> RunStaDialogAsync(Func<string?> showDialog)
    {
        var tcs = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(showDialog());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
