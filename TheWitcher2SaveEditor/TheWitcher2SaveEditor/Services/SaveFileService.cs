using TheWitcher2SaveEditor.Models;

namespace TheWitcher2SaveEditor.Services;

/// <summary>
/// Manages the currently loaded save file state for the application
/// </summary>
public class SaveFileService
{
    private readonly SaveFileParser _parser = new();

    public W2SaveFile? CurrentSave { get; private set; }
    public string? CurrentFileName { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool IsLoaded => CurrentSave != null;

    public event Action? OnStateChanged;

    public async Task LoadFromStream(Stream stream, string fileName)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            CurrentSave = _parser.Parse(bytes);
            CurrentFileName = fileName;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            CurrentSave = null;
            CurrentFileName = null;
            ErrorMessage = $"Failed to load save file: {ex.Message}";
        }
        OnStateChanged?.Invoke();
    }

    public void LoadFromBytes(byte[] bytes, string fileName)
    {
        try
        {
            CurrentSave = _parser.Parse(bytes);
            CurrentFileName = fileName;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            CurrentSave = null;
            CurrentFileName = null;
            ErrorMessage = $"Failed to load save file: {ex.Message}";
        }
        OnStateChanged?.Invoke();
    }

    public bool ApplyEdit(string sectionName, string nodePath, string newValue)
    {
        if (CurrentSave == null) return false;

        try
        {
            return _parser.ApplyEdit(CurrentSave, sectionName, nodePath, newValue);
        }
        catch
        {
            return false;
        }
    }

    public byte[]? Export()
    {
        if (CurrentSave == null) return null;

        try
        {
            return _parser.Rebuild(CurrentSave);
        }
        catch
        {
            return null;
        }
    }

    public void Close()
    {
        CurrentSave = null;
        CurrentFileName = null;
        ErrorMessage = null;
        OnStateChanged?.Invoke();
    }
}
