using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideDim;

internal enum DimMode { Overlay, Hardware, Both }

internal enum DimTrigger { SelectedApps, AnyWindow }

internal sealed class Settings
{
    public const int MaxDelayMs = 10_000;
    public const int MaxFadeMs = 2_000;
    public const int MaxOverlayStrength = 95;
    public const string DefaultHotkey = "Ctrl+Alt+D";
    private static readonly string[] DefaultNeverDimFor = ["explorer", "ScreenClippingHost", "SnippingTool"];

    public bool Enabled { get; set; } = true;

    /// <summary>SelectedApps: only the apps in <see cref="Apps"/>. AnyWindow: whatever window has focus.</summary>
    [JsonConverter(typeof(LenientEnumConverter<DimTrigger>))]
    public DimTrigger Trigger { get; set; } = DimTrigger.SelectedApps;

    /// <summary>Exe paths or bare process names. Matched on file name, so a game that moves still matches.</summary>
    public List<string> Apps { get; set; } = [];

    /// <summary>In SelectedApps mode, also dim for any app that covers a whole monitor.</summary>
    public bool AlsoAnyFullscreen { get; set; } = true;

    /// <summary>Overlay draws a black click-through layer, Hardware lowers the backlight over DDC/CI, Both does both.</summary>
    [JsonConverter(typeof(LenientEnumConverter<DimMode>))]
    public DimMode Mode { get; set; } = DimMode.Overlay;

    /// <summary>Monitor brightness (0-100) while dimmed. A screen already below this is left alone.</summary>
    public int BacklightLevel { get; set; } = 10;

    /// <summary>Overlay darkness, 0 = invisible, 95 = nearly black.</summary>
    public int OverlayStrength { get; set; } = 70;

    /// <summary>Also darken the focused window's own screen, leaving a hole for the window (always overlay).</summary>
    public bool Spotlight { get; set; }

    /// <summary>Process names that never trigger dimming.</summary>
    public List<string> NeverDimFor { get; set; } = [.. DefaultNeverDimFor];

    public int DimDelayMs { get; set; } = 800;
    public int RestoreDelayMs { get; set; } = 200;
    public int FadeMs { get; set; } = 300;

    public string ToggleHotkey { get; set; } = DefaultHotkey;

    [JsonIgnore] public bool IsFirstRun { get; private set; }

    [JsonIgnore] public bool UsesBacklight => Mode is DimMode.Hardware or DimMode.Both;

    [JsonIgnore] public bool UsesOverlay => Mode is DimMode.Overlay or DimMode.Both;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load() => Load(AppPaths.SettingsFile);

    /// <summary>
    /// Reads settings, repairing anything out of range. A file that can't be parsed is renamed to
    /// settings.json.broken-* (so hand edits aren't lost) and replaced with defaults.
    /// </summary>
    public static Settings Load(string path)
    {
        Settings? loaded = null;
        if (File.Exists(path))
        {
            try
            {
                loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                var backup = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
                Log.Write($"{Path.GetFileName(path)} is unreadable ({e.Message}); kept it as {Path.GetFileName(backup)} and using defaults.");
                TryMove(path, backup);
            }
        }

        var settings = (loaded ?? new Settings { IsFirstRun = true }).Normalize();
        if (loaded is null) settings.Save(path);
        return settings;
    }

    public void Save() => Save(AppPaths.SettingsFile);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
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
        NeverDimFor = NeverDimFor is null ? [.. DefaultNeverDimFor] : Distinct(NeverDimFor);
        Apps = Apps is null ? [] : Distinct(Apps);
        return this;
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

    /// <summary>"C:\Games\cs2.exe", "cs2.exe" and "cs2" all become "cs2".</summary>
    public static string AppName(string entry) => Path.GetFileNameWithoutExtension(entry.Trim());

    public static bool ListContains(IEnumerable<string> list, string process) =>
        process.Length > 0 && list.Any(e => string.Equals(AppName(e), process, StringComparison.OrdinalIgnoreCase));

    private static List<string> Distinct(IEnumerable<string?> entries) => entries
        .Where(e => !string.IsNullOrWhiteSpace(e) && AppName(e!).Length > 0)
        .Select(e => e!.Trim())
        .DistinctBy(AppName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static void TryMove(string from, string to)
    {
        try { File.Move(from, to, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"Could not back up {from}: {e.Message}"); }
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

internal static class AppPaths
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SideDim");

    public static string SettingsFile => Path.Combine(Folder, "settings.json");
    public static string BrightnessStateFile => Path.Combine(Folder, "hardware-state.json");
    public static string LogFile => Path.Combine(Folder, "sidedim.log");
}

internal static class AtomicFile
{
    /// <summary>Write to a temp file and swap it in, so a crash mid-write can't leave a half-written file.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}

internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    /// <summary>Where log lines go. Tests swap this out so they don't write to the real log.</summary>
    public static Action<string> Sink { get; set; } = WriteToFile;

    public static void Write(string message)
    {
        try { Sink(message); }
        catch { /* logging must never take the app down */ }
    }

    private static void WriteToFile(string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppPaths.Folder);
            var path = AppPaths.LogFile;
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Move(path, path + ".old", overwrite: true); // keep one previous log for bug reports
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
    }
}

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SideDim";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsOn
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>If autostart is on but points at an old location (the exe was moved), point it here.</summary>
    public static void RepairPath()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(ValueName, Command);
            Log.Write($"Autostart pointed at {current}; updated to {Command}");
        }
    }
}
