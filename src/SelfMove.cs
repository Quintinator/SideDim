using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SideDim;

/// <summary>Copies SideDim into a folder of its own and adds a Start menu shortcut.</summary>
internal sealed class SelfMove(string targetFolder, string startMenuFolder)
{
    public static SelfMove ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SideDim"),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    public string TargetFolder => targetFolder;
    public string TargetExe => Path.Combine(targetFolder, "SideDim.exe");
    public string Shortcut => Path.Combine(startMenuFolder, "SideDim.lnk");

    public void Install(string sourceExe)
    {
        Directory.CreateDirectory(targetFolder);
        File.Copy(sourceExe, TargetExe, overwrite: true);
        Directory.CreateDirectory(startMenuFolder);
        CreateShortcut(Shortcut, TargetExe);
    }

    /// <remarks>The moved copy waits until this process has exited, then deletes <paramref name="oldExe"/> when one is given.</remarks>
    public void StartMovedCopy(string? oldExe, bool showSettings)
    {
        var start = new ProcessStartInfo(TargetExe) { UseShellExecute = false, WorkingDirectory = targetFolder };
        start.ArgumentList.Add(StartupArgs.WaitFlag);
        if (oldExe is not null)
        {
            start.ArgumentList.Add(StartupArgs.RemoveFlag);
            start.ArgumentList.Add(oldExe);
        }
        if (showSettings) start.ArgumentList.Add(StartupArgs.ShowSettingsFlag);
        Process.Start(start)?.Dispose();
    }

    private static void CreateShortcut(string lnk, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new COMException("WScript.Shell is not available");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.Description = "SideDim: dims everything except what you're focused on";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static bool IsRiskyFolder(string folder) => InstallLocation.IsRisky(folder, SharedFolders(), FilesIn(folder));

    public static IEnumerable<string> DllsIn(string folder) =>
        FilesIn(folder).Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(f => Path.GetFileName(f));

    private static string[] FilesIn(string folder)
    {
        try
        {
            return Directory.GetFiles(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Folders where other downloads can sit next to SideDim.exe. One that Windows can't resolve, such as a redirected Desktop that is offline, is left out.</summary>
    public static IReadOnlyList<string> SharedFolders()
    {
        var folders = new List<string>();
        var result = Native.SHGetKnownFolderPath(Native.FOLDERID_Downloads, 0, IntPtr.Zero, out var downloads);
        try
        {
            if (result == 0) folders.Add(Marshal.PtrToStringUni(downloads) ?? "");
        }
        finally
        {
            Marshal.FreeCoTaskMem(downloads);
        }
        folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        folders.Add(Path.GetTempPath());
        folders.RemoveAll(string.IsNullOrWhiteSpace);
        return folders;
    }
}
