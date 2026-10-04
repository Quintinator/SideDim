namespace SideDim;

internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static Action<string> Sink { get; set; } = WriteToFile;

    /// <summary>Swallows I/O errors: a full disk or locked log must never take the app down.</summary>
    public static void Write(string message)
    {
        try
        {
            Sink(message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void WriteToFile(string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppPaths.Folder);
            var path = AppPaths.LogFile;
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
    }
}
