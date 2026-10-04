using System.Text.RegularExpressions;

namespace SideDim;

/// <summary>Finds where Steam games are installed, so "Browse for .exe" opens in the right place.</summary>
internal static partial class SteamLibraries
{
    [GeneratedRegex("\"path\"\\s+\"(?<path>(?:[^\"\\\\]|\\\\.)+)\"")]
    private static partial Regex PathEntry();

    /// <summary>Library roots listed in steamapps/libraryfolders.vdf (escaped backslashes undone).</summary>
    public static List<string> ParseLibraryFolders(string vdf) => PathEntry().Matches(vdf)
        .Select(m => m.Groups["path"].Value.Replace(@"\\", @"\"))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>The steamapps\common folder holding the most games, or null when Steam isn't installed.</summary>
    public static string? BiggestGamesFolder()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string steam) return null;
        steam = steam.Replace('/', '\\');

        var roots = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf)) roots.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read {vdf}: {e.Message}");
        }

        return roots
            .Select(r => Path.Combine(r, "steamapps", "common"))
            .Where(Directory.Exists)
            .Select(d => (Folder: d, Games: CountGames(d)))
            .OrderByDescending(x => x.Games)
            .Select(x => x.Folder)
            .FirstOrDefault();
    }

    private static int CountGames(string folder)
    {
        try { return Directory.EnumerateDirectories(folder).Count(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}
