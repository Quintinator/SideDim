namespace SideDim.Tests;

public class InstallLocationTests
{
    private static readonly string[] Shared = [@"C:\Users\me\Downloads", @"C:\Users\me\Desktop"];

    [Theory]
    [InlineData(@"C:\Users\me\Downloads", true)]
    [InlineData(@"C:\Users\me\downloads\", true)]
    [InlineData(@"C:\Users\me\Desktop", true)]
    [InlineData(@"C:\Users\me\Downloads\SideDim", false)]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\SideDim", false)]
    public void A_shared_folder_is_risky_but_a_subfolder_is_not(string folder, bool risky) =>
        Assert.Equal(risky, InstallLocation.IsRisky(folder, Shared, [Path.Combine(folder, "SideDim.exe")]));

    [Fact]
    public void Any_dll_next_to_the_exe_is_risky() =>
        Assert.True(InstallLocation.IsRisky(@"C:\Tools\SideDim", Shared, [@"C:\Tools\SideDim\SideDim.exe", @"C:\Tools\SideDim\VERSION.DLL"]));

    [Fact]
    public void Other_files_next_to_the_exe_are_fine() =>
        Assert.False(InstallLocation.IsRisky(@"C:\Tools\SideDim", Shared, [@"C:\Tools\SideDim\SideDim.exe", @"C:\Tools\SideDim\readme.txt"]));

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\SideDim.exe", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", true)]
    [InlineData(@"C:\Users\me\Downloads\SideDim-0.2.1-win-x64.exe", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\SideDim\sidedim.EXE", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", false)]
    [InlineData(@"C:\Users\me\Downloads\setup.exe", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", false)]
    [InlineData(@"C:\Users\me\Downloads\SideDim.dll", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", false)]
    [InlineData(@"C:\Users\me\Documents", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", false)]
    [InlineData("", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", false)]
    [InlineData("  ", @"C:\Users\me\AppData\Local\Programs\SideDim\SideDim.exe", false)]
    public void Only_an_older_SideDim_exe_may_be_removed(string oldExe, string currentExe, bool allowed) =>
        Assert.Equal(allowed, InstallLocation.MayRemoveOldCopy(oldExe, currentExe));
}

public class StartupArgsTests
{
    [Fact]
    public void No_arguments_means_a_normal_start() =>
        Assert.Equal(new StartupArgs(false, false, null, false), StartupArgs.Parse([]));

    [Fact]
    public void Probe_is_recognised_in_any_case() =>
        Assert.True(StartupArgs.Parse(["--PROBE"]).Probe);

    [Fact]
    public void A_moved_copy_waits_removes_the_old_exe_and_can_open_settings() =>
        Assert.Equal(
            new StartupArgs(false, true, @"C:\Users\me\Downloads\SideDim.exe", true),
            StartupArgs.Parse(["--wait", "--remove", @"C:\Users\me\Downloads\SideDim.exe", "--show-settings"]));

    [Fact]
    public void The_removed_path_is_never_read_as_a_flag() =>
        Assert.Equal(
            new StartupArgs(false, false, "--wait", false),
            StartupArgs.Parse(["--remove", "--wait"]));

    [Fact]
    public void Remove_without_a_path_is_ignored() =>
        Assert.Null(StartupArgs.Parse(["--remove"]).RemoveOldCopy);
}

public sealed class SelfMoveTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Install_copies_the_exe_and_adds_a_start_menu_shortcut()
    {
        var source = _dir.File("SideDim-0.2.1-win-x64.exe");
        File.WriteAllText(source, "exe");
        var mover = new SelfMove(_dir.File("Programs\\SideDim"), _dir.File("Start Menu"));

        mover.Install(source);

        Assert.Equal("exe", File.ReadAllText(mover.TargetExe));
        Assert.True(File.Exists(mover.Shortcut));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void A_folder_of_its_own_is_safe_until_a_dll_appears()
    {
        File.WriteAllText(_dir.File("SideDim.exe"), "exe");
        Assert.False(SelfMove.IsRiskyFolder(_dir.Path));

        File.WriteAllText(_dir.File("version.dll"), "dll");
        Assert.True(SelfMove.IsRiskyFolder(_dir.Path));
        Assert.Equal(["version.dll"], SelfMove.DllsIn(_dir.Path));
    }

    [Fact]
    public void Shared_folders_include_temp_and_never_an_empty_path()
    {
        var folders = SelfMove.SharedFolders();
        Assert.DoesNotContain(folders, string.IsNullOrWhiteSpace);
        Assert.Contains(folders, f => InstallLocation.SameFolder(f, Path.GetTempPath()));
    }
}
