using System.Text;

namespace SideDim;

internal static class AppPaths
{
    /// <summary>Roaming: settings follow the user to other PCs.</summary>
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SideDim");

    /// <summary>Local: brightness to restore belongs to this PC's monitors and must never roam to another one.</summary>
    public static string LocalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideDim");

    public static string SettingsFile => Path.Combine(Folder, "settings.json");
    public static string LogFile => Path.Combine(Folder, "sidedim.log");
    public static string BrightnessStateFile => Path.Combine(LocalFolder, "hardware-state.json");
}

internal static class RetryRead
{
    /// <summary>
    /// Reads a file, retrying for about a second on sharing violations: at logon, sync and antivirus
    /// tools often hold files briefly. Still throws if the file stays unreadable.
    /// </summary>
    public static string Text(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 5 && File.Exists(path))
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}

internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes to a temp file, flushes it to disk, then swaps it in, so a crash or power cut leaves either
    /// the old file or the new one, never a half-written one.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(Utf8NoBom.GetBytes(contents));
            fs.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}
