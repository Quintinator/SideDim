namespace SideDim;

internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    /// <summary>Where log lines go. Tests swap this out so they don't write to the real log.</summary>
    public static Action<string> Sink { get; set; } = WriteToFile;

    public static void Write(string message)
    {
        try
        {
            Sink(message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A full disk or locked log must never take the app down.
        }
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
