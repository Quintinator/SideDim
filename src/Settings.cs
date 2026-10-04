using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideDim;

internal enum DimMode { Overlay, Hardware, Both }

internal enum DimTrigger { SelectedApps, AnyWindow }

/// <summary>What one screen does while it's dimmed.</summary>
internal readonly record struct ScreenLevels(bool Dim, int BacklightLevel, int OverlayStrength);

/// <summary>Overrides for one physical monitor. Anything left null follows the settings for all screens.</summary>
internal sealed class MonitorSettings
{
    /// <summary>The monitor's name when it was last seen, so settings.json stays readable.</summary>
    public string? Name { get; set; }

    /// <summary>False: SideDim never darkens this screen (not dimmed, no spotlight).</summary>
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

    // Ctrl+Alt+letter is AltGr+letter on many European layouts (AltGr+D types "ð" on US-International),
    // so the default uses a function key, which never produces a character.
    public const string DefaultHotkey = "Ctrl+Alt+F9";

    /// <summary>Shell and screenshot tools that should never count as the focused app. Always applied, never saved.</summary>
    // ApplicationFrameHost only ever wraps a Store app, which SideDim resolves to the real app instead.
    public static readonly IReadOnlyList<string> BuiltInNeverDimFor =
        ["explorer", "ScreenClippingHost", "SnippingTool", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "ApplicationFrameHost"];

    public bool Enabled { get; set; } = true;

    /// <summary>SelectedApps: only the apps in <see cref="Apps"/>. AnyWindow: whatever window has focus.</summary>
    [JsonConverter(typeof(LenientEnumConverter<DimTrigger>))]
    public DimTrigger Trigger { get; set; } = DimTrigger.SelectedApps;

    /// <summary>Exe paths or bare process names. Matched on file name, so an app that moves still matches.</summary>
    public List<string> Apps { get; set; } = [];

    /// <summary>In SelectedApps mode, also dim for any app that covers a whole monitor.</summary>
    public bool AlsoAnyFullscreen { get; set; } = true;

    /// <summary>Overlay draws a black click-through layer, Hardware lowers the backlight over DDC/CI, Both does both.</summary>
    [JsonConverter(typeof(LenientEnumConverter<DimMode>))]
    public DimMode Mode { get; set; } = DimMode.Overlay;

    /// <summary>Monitor brightness (0-100) while dimmed. A screen already below this is left alone.</summary>
    public int BacklightLevel { get; set; } = 10;

    /// <summary>Overlay darkness, 0 = invisible, <see cref="MaxOverlayStrength"/> = nearly black.</summary>
    public int OverlayStrength { get; set; } = 70;

    /// <summary>Also darken the focused window's own screen, leaving a hole for the window (always overlay).</summary>
    public bool Spotlight { get; set; }

    /// <summary>Extra process names that never trigger dimming, on top of <see cref="BuiltInNeverDimFor"/>.</summary>
    public List<string> NeverDimFor { get; set; } = [];

    public int DimDelayMs { get; set; } = 800;
    public int RestoreDelayMs { get; set; } = 200;
    public int FadeMs { get; set; } = 300;

    public string ToggleHotkey { get; set; } = DefaultHotkey;

    /// <summary>Per-monitor overrides, keyed by the monitor's stable id (see <see cref="Monitor.Id"/>).</summary>
    public Dictionary<string, MonitorSettings> Monitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public bool IsFirstRun { get; private set; }

    /// <summary>Set when the file on disk couldn't be read; Save then refuses to overwrite it blindly.</summary>
    [JsonIgnore] public bool LoadFailed { get; private set; }

    [JsonIgnore] public bool UsesBacklight => Mode is DimMode.Hardware or DimMode.Both;

    [JsonIgnore] public bool UsesOverlay => Mode is DimMode.Overlay or DimMode.Both;

    // Hand edits are welcome, so be forgiving about trailing commas and // comments.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static Settings Load() => Load(AppPaths.SettingsFile);

    /// <summary>
    /// Reads settings, repairing anything out of range. A file that isn't valid JSON is renamed to
    /// settings.json.broken-* (so hand edits aren't lost) and replaced with defaults. A file that can't
    /// be read right now (locked, no access) is left alone and defaults are used for this session only.
    /// </summary>
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

    /// <summary>Writes the settings; failures are logged, never thrown, so a full disk can't crash the app.</summary>
    public bool Save(string path)
    {
        if (LoadFailed)
        {
            // The real file was never read: set it aside before writing, or don't write at all.
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

    /// <summary>Brings hand-edited or older settings back into the ranges the app expects.</summary>
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
        // Older files stored the built-in list; keep only what the user added, so new built-ins reach them.
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
            if (!m.IsDefault) result[id] = m; // nothing to remember for a screen that follows the defaults
        }
        return result;
    }

    /// <summary>How a screen is dimmed: its own overrides where set, the settings for all screens otherwise.</summary>
    public ScreenLevels LevelsFor(string monitorId) => Monitors.TryGetValue(monitorId, out var m)
        ? new ScreenLevels(m.Dim, m.BacklightLevel ?? BacklightLevel, m.OverlayStrength ?? OverlayStrength)
        : new ScreenLevels(true, BacklightLevel, OverlayStrength);

    /// <summary>The overrides for a monitor, created on first use. Drop unused ones with <see cref="Normalize"/>.</summary>
    public MonitorSettings MonitorFor(string monitorId, string? name)
    {
        if (!Monitors.TryGetValue(monitorId, out var m)) Monitors[monitorId] = m = new MonitorSettings();
        if (name is not null) m.Name = name;
        return m;
    }

    /// <summary>Adds an exe path or process name. Returns false if an app with that name is already listed.</summary>
    public bool AddApp(string entry)
    {
        if (AppName(entry).Length == 0 || ListContains(Apps, AppName(entry))) return false;
        Apps.Add(entry.Trim());
        return true;
    }

    public bool RemoveApp(string name) =>
        Apps.RemoveAll(a => string.Equals(AppName(a), AppName(name), StringComparison.OrdinalIgnoreCase)) > 0;

    public bool IsNeverDim(string process) => ListContains(BuiltInNeverDimFor, process) || ListContains(NeverDimFor, process);

    /// <summary>
    /// "C:\Games\cs2.exe", "cs2.exe" and "cs2" all become "cs2". Only ".exe" is stripped, so process
    /// names that contain dots, like "Battle.net", survive intact.
    /// </summary>
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

/// <summary>Reads enums as text and falls back to the default for unknown values, instead of failing the whole file.</summary>
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
