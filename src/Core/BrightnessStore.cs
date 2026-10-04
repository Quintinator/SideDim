using System.Text.Json;

namespace SideDim;

/// <summary>Keeps the pre-dim brightness in a small JSON file; no file means nothing is dimmed.</summary>
/// <param name="legacyPath">Where version 0.1.0 kept the file (roaming); moved to <paramref name="path"/> on load.</param>
internal sealed class FileBrightnessStore(string path, string? legacyPath = null) : IBrightnessStore
{
    private bool _readOnly; // the file exists but couldn't be read: never overwrite the only crash backup

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
            // Keep the damaged file for inspection instead of letting the next save overwrite it.
            var aside = path + ".corrupt";
            try { File.Move(path, aside, overwrite: true); }
            catch (Exception m) when (m is IOException or UnauthorizedAccessException) { aside = path; }
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
        if (_readOnly) return false; // BacklightController then leaves the monitor alone
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
