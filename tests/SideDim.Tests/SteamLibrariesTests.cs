namespace SideDim.Tests;

public class SteamLibrariesTests
{
    private const string Vdf = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "apps" { "228980" "1234" }
            }
            "1"
            {
                "path"		"G:\\SteamLibrary"
                "label"		"Games"
            }
            "2"
            {
                "path"		"g:\\steamlibrary"
            }
        }
        """;

    [Fact]
    public void Reads_every_library_path_and_unescapes_backslashes() =>
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"G:\SteamLibrary"], SteamLibraries.ParseLibraryFolders(Vdf));

    [Fact]
    public void Ignores_other_keys_and_empty_files()
    {
        Assert.Empty(SteamLibraries.ParseLibraryFolders(""));
        Assert.Empty(SteamLibraries.ParseLibraryFolders("\"label\"\t\"G:\\\\Games\""));
    }
}
