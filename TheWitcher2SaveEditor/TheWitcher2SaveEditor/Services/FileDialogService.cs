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

            // Prefer Steam remote folder (where game actually reads saves)
            var steamPath = FindSteamW2SaveFolder();
            if (steamPath != null)
                dialog.InitialDirectory = steamPath;
            else
            {
                var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var w2SavePath = Path.Combine(docsPath, "Witcher 2", "gamesaves");
                if (Directory.Exists(w2SavePath))
                    dialog.InitialDirectory = w2SavePath;
            }

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

            var steamPath = FindSteamW2SaveFolder();
            if (steamPath != null)
                dialog.InitialDirectory = steamPath;

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        });
    }

    /// <summary>
    /// Finds the Steam userdata remote folder for Witcher 2 (App ID 20920)
    /// </summary>
    public static string? FindSteamW2SaveFolder()
    {
        var steamPaths = new[]
        {
            @"C:\Program Files (x86)\Steam\userdata",
            @"C:\Program Files\Steam\userdata",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", "userdata")
        };

        foreach (var basePath in steamPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            foreach (var userDir in Directory.GetDirectories(basePath))
            {
                var remotePath = Path.Combine(userDir, "20920", "remote");
                if (Directory.Exists(remotePath))
                    return remotePath;
            }
        }
        return null;
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
