using TheWitcher2SaveEditor.Models;

namespace TheWitcher2SaveEditor.Services;

public class SaveFileService
{
    private readonly SaveFileParser _parser = new();

    public W2SaveFile? CurrentSave { get; private set; }
    public string? CurrentFilePath { get; private set; }
    public string? CurrentFileName => CurrentFilePath != null ? Path.GetFileName(CurrentFilePath) : null;
    public string? ErrorMessage { get; private set; }
    public bool IsLoaded => CurrentSave != null;

    public event Action? OnStateChanged;

    public Task<bool> LoadFromPathAsync(string filePath)
    {
        return Task.Run(() =>
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                CurrentSave = _parser.Parse(bytes);
                CurrentFilePath = filePath;
                ErrorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                CurrentSave = null;
                CurrentFilePath = null;
                ErrorMessage = $"Failed to load save file: {ex.Message}";
                return false;
            }
        });
    }

    public bool ApplyEdit(string sectionName, string nodePath, string newValue)
    {
        if (CurrentSave == null) return false;
        try { return _parser.ApplyEdit(CurrentSave, sectionName, nodePath, newValue); }
        catch { return false; }
    }

    public Task<bool> SaveToPathAsync(string filePath)
    {
        if (CurrentSave == null) return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                var bytes = _parser.Rebuild(CurrentSave);
                File.WriteAllBytes(filePath, bytes);

                if (SteamCloudService.IsInSteamRemoteFolder(filePath))
                    SteamCloudService.UpdateRemoteCache(filePath);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public Task<bool> SaveAsync()
    {
        if (CurrentFilePath == null) return Task.FromResult(false);
        return SaveToPathAsync(CurrentFilePath);
    }

    public void Close()
    {
        CurrentSave = null;
        CurrentFilePath = null;
        ErrorMessage = null;
        OnStateChanged?.Invoke();
    }
}
