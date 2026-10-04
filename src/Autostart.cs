using Microsoft.Win32;

namespace SideDim;

/// <summary>"Start with Windows", as a value under HKCU\...\Run (no admin rights needed).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SideDim";

    /// <summary>On only when the Run entry points at this exe; a stale entry for a moved exe counts as off.</summary>
    public static bool IsOn => PointsAt(Stored(), Environment.ProcessPath);

    /// <summary>Turns autostart on or off; returns false (and logs) when the registry refuses.</summary>
    public static bool TrySet(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Write($"Could not change autostart: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Points autostart here when it points at an exe that's gone (moved) or at an older SideDim
    /// (a newer version downloaded next to it). Another, newer or unrelated exe is left alone.
    /// </summary>
    public static void RepairPath()
    {
        if (Stored() is not { } stored || PointsAt(stored, Environment.ProcessPath)) return;
        var target = Unquote(stored);
        var exists = File.Exists(target);
        if (ShouldRepoint(exists, exists ? SideDimVersion(target) : null, Application.ProductVersion) && TrySet(true))
            Log.Write($"Autostart pointed at {stored} ({(exists ? "older version" : "gone")}); now points at {Environment.ProcessPath}");
    }

    internal static bool ShouldRepoint(bool targetExists, string? targetSideDimVersion, string currentVersion) =>
        !targetExists
        || (targetSideDimVersion is not null
            && Version.TryParse(targetSideDimVersion, out var old)
            && Version.TryParse(currentVersion, out var current)
            && old < current);

    /// <summary>The product version if the exe is a SideDim build, else null.</summary>
    private static string? SideDimVersion(string exe)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            return info.ProductName == "SideDim" ? info.ProductVersion : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static bool PointsAt(string? stored, string? exe) =>
        stored is not null && exe is not null && string.Equals(Unquote(stored), exe, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string command) => command.Trim().Trim('"');

    private static string? Stored()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }
}
