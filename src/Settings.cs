using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideDim;

internal enum DimMode { Overlay, Hardware, Both }

internal enum DimTrigger { SelectedApps, AnyWindow }

internal readonly record struct ScreenLevels(bool Dim, int BacklightLevel, int OverlayStrength);

/// <summary>Null levels follow the settings for all screens.</summary>
internal sealed class MonitorSettings
{
    public string? Name { get; set; }

    public bool Dim { get; set; } = true;

    public int? BacklightLevel { get; set; }
    public int? OverlayStrength { get; set; }

    [JsonIgnore] public bool HasOwnLevels => BacklightLevel is not null || OverlayStrength is not null;

    [JsonIgnore] public bool IsDefault => Dim && !HasOwnLevels;
}

internal sealed class Settings
{
    public const int MaxDelayMs = 10_000;
    public const int MaxFadeMs = 2_000;

    /// <summary>Below 100 on purpose: a stuck topmost click-through overlay must never fully black out a screen.</summary>
    public const int MaxOverlayStrength = 95;

    /// <summary>A function key on purpose: Ctrl+Alt+letter is AltGr+letter on many European layouts and types a character.</summary>
    public const string DefaultHotkey = "Ctrl+Alt+F9";

    /// <summary>Always applied, never saved. ApplicationFrameHost only wraps a Store app, which SideDim resolves to the real app.</summary>
    public static readonly IReadOnlyList<string> BuiltInNeverDimFor =
        ["explorer", "ScreenClippingHost", "SnippingTool", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "ApplicationFrameHost"];

    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(LenientEnumConverter<DimTrigger>))]
    public DimTrigger Trigger { get; set; } = DimTrigger.SelectedApps;

    /// <summary>Exe paths or bare process names, matched on file name only so a moved app still matches.</summary>
    public List<string> Apps { get; set; } = [];

    public bool AlsoAnyFullscreen { get; set; } = true;

    [JsonConverter(typeof(LenientEnumConverter<DimMode>))]
    public DimMode Mode { get; set; } = DimMode.Overlay;

    /// <summary>Brightness 0-100 while dimmed; a screen already below it is never brightened.</summary>
    public int BacklightLevel { get; set; } = 10;

    public int OverlayStrength { get; set; } = 70;

    public bool Spotlight { get; set; }

    public List<string> NeverDimFor { get; set; } = [];

    public int DimDelayMs { get; set; } = 800;
    public int RestoreDelayMs { get; set; } = 200;
    public int FadeMs { get; set; } = 300;

    public string ToggleHotkey { get; set; } = DefaultHotkey;

    /// <summary>Offer to move the self-contained exe out of a shared folder such as Downloads.</summary>
    public bool WarnAboutFolder { get; set; } = true;

    /// <summary>Keyed by the stable <see cref="Monitor.Id"/>.</summary>
    public Dictionary<string, MonitorSettings> Monitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public bool IsFirstRun { get; private set; }

    /// <summary>Set when the file on disk couldn't be read; Save then refuses to overwrite it blindly.</summary>
    [JsonIgnore] public bool LoadFailed { get; private set; }

    [JsonIgnore] public bool UsesBacklight => Mode is DimMode.Hardware or DimMode.Both;

    [JsonIgnore] public bool UsesOverlay => Mode is DimMode.Overlay or DimMode.Both;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static Settings Load() => Load(AppPaths.SettingsFile);

    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return CreateDefaults(path);

        try
        {
            return (JsonSerializer.Deserialize<Settings>(RetryRead.Text(path), Json) ?? new Settings()).Normalize();
        }
        catch (JsonException e)
        {
            var backup = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (TryMove(path, backup))
            {
                Log.Write($"{Path.GetFileName(path)} is not valid JSON ({e.Message}); kept it as {Path.GetFileName(backup)} and using defaults.");
                return CreateDefaults(path);
            }
            Log.Write($"{Path.GetFileName(path)} is not valid JSON ({e.Message}) and can't be moved aside; using defaults for this session.");
            return new Settings { LoadFailed = true }.Normalize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read {Path.GetFileName(path)} ({e.Message}); using defaults for this session.");
            return new Settings { LoadFailed = true }.Normalize();
        }
    }

    private static Settings CreateDefaults(string path)
    {
        var settings = new Settings { IsFirstRun = true }.Normalize();
        settings.Save(path);
        return settings;
    }

    public bool Save() => Save(AppPaths.SettingsFile);

    public bool Save(string path)
    {
        if (LoadFailed)
        {
            if (File.Exists(path) && !TryMove(path, $"{path}.unread-{DateTime.Now:yyyyMMdd-HHmmss}"))
            {
                Log.Write($"Not saving: {Path.GetFileName(path)} couldn't be read at startup and is still locked.");
                return false;
            }
            LoadFailed = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save {Path.GetFileName(path)}: {e.Message}");
            return false;
        }
    }

    /// <summary>Older files saved the built-ins in <see cref="NeverDimFor"/>; they are dropped so later changes to the built-in list reach those users.</summary>
    public Settings Normalize()
    {
        BacklightLevel = Math.Clamp(BacklightLevel, 0, 100);
        OverlayStrength = Math.Clamp(OverlayStrength, 0, MaxOverlayStrength);
        DimDelayMs = Math.Clamp(DimDelayMs, 0, MaxDelayMs);
        RestoreDelayMs = Math.Clamp(RestoreDelayMs, 0, MaxDelayMs);
        FadeMs = Math.Clamp(FadeMs, 0, MaxFadeMs);
        if (!Enum.IsDefined(Mode)) Mode = DimMode.Overlay;
        if (!Enum.IsDefined(Trigger)) Trigger = DimTrigger.SelectedApps;
        if (!Hotkey.TryParse(ToggleHotkey, out _)) ToggleHotkey = DefaultHotkey;
        NeverDimFor = NeverDimFor is null ? [] : Distinct(NeverDimFor.Where(n => n is not null && !ListContains(BuiltInNeverDimFor, AppName(n))));
        Apps = Apps is null ? [] : Distinct(Apps);
        Monitors = NormalizeMonitors(Monitors);
        return this;
    }

    private static Dictionary<string, MonitorSettings> NormalizeMonitors(Dictionary<string, MonitorSettings>? monitors)
    {
        var result = new Dictionary<string, MonitorSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, m) in monitors ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || m is null) continue;
            if (m.BacklightLevel is { } b) m.BacklightLevel = Math.Clamp(b, 0, 100);
            if (m.OverlayStrength is { } o) m.OverlayStrength = Math.Clamp(o, 0, MaxOverlayStrength);
            if (!m.IsDefault) result[id] = m;
        }
        return result;
    }

    public ScreenLevels LevelsFor(string monitorId) => Monitors.TryGetValue(monitorId, out var m)
        ? new ScreenLevels(m.Dim, m.BacklightLevel ?? BacklightLevel, m.OverlayStrength ?? OverlayStrength)
        : new ScreenLevels(true, BacklightLevel, OverlayStrength);

    public MonitorSettings MonitorFor(string monitorId, string? name)
    {
        if (!Monitors.TryGetValue(monitorId, out var m)) Monitors[monitorId] = m = new MonitorSettings();
        if (name is not null) m.Name = name;
        return m;
    }

    public bool AddApp(string entry)
    {
        if (AppName(entry).Length == 0 || ListContains(Apps, AppName(entry))) return false;
        Apps.Add(entry.Trim());
        return true;
    }

    public bool RemoveApp(string name) =>
        Apps.RemoveAll(a => string.Equals(AppName(a), AppName(name), StringComparison.OrdinalIgnoreCase)) > 0;

    public bool IsNeverDim(string process) => ListContains(BuiltInNeverDimFor, process) || ListContains(NeverDimFor, process);

    /// <summary>Strips only ".exe", so dotted process names like "Battle.net" survive.</summary>
    public static string AppName(string entry)
    {
        var name = Path.GetFileName(entry.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public static bool ListContains(IEnumerable<string> list, string process) =>
        process.Length > 0 && list.Any(e => string.Equals(AppName(e), process, StringComparison.OrdinalIgnoreCase));

    private static List<string> Distinct(IEnumerable<string?> entries) => entries
        .Where(e => !string.IsNullOrWhiteSpace(e) && AppName(e!).Length > 0)
        .Select(e => e!.Trim())
        .DistinctBy(AppName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not back up {from}: {e.Message}");
            return false;
        }
    }
}

internal sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value)
            && Enum.IsDefined(value))
            return value;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), n))
            return (T)Enum.ToObject(typeof(T), n);
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
