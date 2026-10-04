using System.Text.Json;

namespace SideDim;

internal sealed class FileBrightnessStore(string path, string? legacyPath = null) : IBrightnessStore
{
    /// <summary>Set when the file exists but couldn't be read; Save then fails so the only crash backup is never overwritten.</summary>
    private bool _readOnly;

    public Dictionary<string, uint> Load()
    {
        MigrateLegacy();
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, uint>>(RetryRead.Text(path)) ?? [];
        }
        catch (JsonException e)
        {
            var aside = path + ".corrupt";
            try
            {
                File.Move(path, aside, overwrite: true);
            }
            catch (Exception m) when (m is IOException or UnauthorizedAccessException)
            {
                aside = path;
                _readOnly = true;
            }
            Log.Write($"{Path.GetFileName(path)} is unreadable ({e.Message}); kept as {Path.GetFileName(aside)}. "
                + "If a screen stayed dark, set its brightness in the monitor's own menu.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            Log.Write($"Could not read {Path.GetFileName(path)} ({e.Message}); backlight dimming is off until the next start so it isn't overwritten.");
        }
        return [];
    }

    private void MigrateLegacy()
    {
        if (legacyPath is null || File.Exists(path) || !File.Exists(legacyPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(legacyPath, path);
            Log.Write($"Moved {legacyPath} to {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not move {legacyPath}: {e.Message}");
        }
    }

    public bool Save(IReadOnlyDictionary<string, uint> originals)
    {
        if (_readOnly) return false;
        try
        {
            if (originals.Count == 0)
            {
                File.Delete(path);
                return true;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(originals));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save {Path.GetFileName(path)}: {e.Message}");
            return false;
        }
    }
}
