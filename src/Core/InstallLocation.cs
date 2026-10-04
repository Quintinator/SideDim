namespace SideDim;

internal static class InstallLocation
{
    public const string SelfContainedSwitch = "SideDim.SelfContained";

    /// <summary>Set by the build for the self-contained exe, the only one that needs a folder of its own.</summary>
    public static bool ThisBuildNeedsOwnFolder => AppContext.TryGetSwitch(SelfContainedSwitch, out var on) && on;

    /// <remarks>
    /// A self-contained single-file exe loads a few Windows DLLs from its own folder before Main runs, so it is only
    /// safe in a folder of its own. Any .dll next to it is foreign, because SideDim ships none.
    /// </remarks>
    public static bool IsRisky(string exeFolder, IEnumerable<string> sharedFolders, IEnumerable<string> filesInFolder) =>
        sharedFolders.Any(f => SameFolder(f, exeFolder))
        || filesInFolder.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

    public static bool SameFolder(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <remarks>Only ever deletes an older SideDim exe, never this one and never anything else.</remarks>
    public static bool MayRemoveOldCopy(string oldExe, string currentExe) =>
        !string.IsNullOrWhiteSpace(oldExe)
        && !string.Equals(Path.GetFullPath(oldExe), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(oldExe).StartsWith("SideDim", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(oldExe).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

/// <param name="WaitForPrevious">Set by a copy that just moved SideDim: wait for it to exit instead of handing over to it.</param>
/// <param name="RemoveOldCopy">The exe the previous copy ran from, deleted once it has exited.</param>
/// <param name="ShowSettings">The move happened on the first run, so the settings window still has to open.</param>
internal sealed record StartupArgs(bool Probe, bool WaitForPrevious, string? RemoveOldCopy, bool ShowSettings)
{
    public const string ProbeFlag = "--probe";
    public const string WaitFlag = "--wait";
    public const string RemoveFlag = "--remove";
    public const string ShowSettingsFlag = "--show-settings";

    public static StartupArgs Parse(IReadOnlyList<string> args)
    {
        bool probe = false, wait = false, showSettings = false;
        string? remove = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (Is(args[i], ProbeFlag)) probe = true;
            else if (Is(args[i], WaitFlag)) wait = true;
            else if (Is(args[i], ShowSettingsFlag)) showSettings = true;
            else if (Is(args[i], RemoveFlag) && i + 1 < args.Count) remove = args[++i];
        }
        return new StartupArgs(probe, wait, remove, showSettings);
    }

    private static bool Is(string arg, string flag) => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);
}
