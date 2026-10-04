using System.Text.Json;

namespace SideDim;

/// <summary>Keeps the pre-dim brightness in a small JSON file; no file means nothing is dimmed.</summary>
internal sealed class FileBrightnessStore(string path) : IBrightnessStore
{
    public Dictionary<string, uint> Load()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, uint>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"{Path.GetFileName(path)} is unreadable, so leftover brightness can't be restored: {e.Message}");
        }
        return [];
    }

    public void Save(IReadOnlyDictionary<string, uint> originals)
    {
        try
        {
            if (originals.Count == 0)
            {
                File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(originals));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not fatal: dimming still works, only crash recovery is weaker.
            Log.Write($"Could not save {Path.GetFileName(path)}: {e.Message}");
        }
    }
}
