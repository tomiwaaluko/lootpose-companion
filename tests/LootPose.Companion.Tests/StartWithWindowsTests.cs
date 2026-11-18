using Microsoft.Win32;

namespace LootPose.Companion.Tests;

public class StartWithWindowsTests
{
    private const string TestRoot = @"Software\LootPoseCompanionTests";

    [Fact]
    public void Off_until_turned_on_then_starts_in_the_tray_and_follows_the_exe()
    {
        var path = TestRoot + @"\" + Guid.NewGuid().ToString("N");
        try
        {
            var start = new StartWithWindows(path);
            Assert.False(start.IsOn());

            Assert.True(start.Set(true, @"C:\Apps\LootPoseCompanion.exe"));
            Assert.True(start.IsOn());
            Assert.Equal("\"C:\\Apps\\LootPoseCompanion.exe\" --tray", Value(path));

            start.Refresh(@"D:\Moved\LootPoseCompanion.exe");
            Assert.Equal("\"D:\\Moved\\LootPoseCompanion.exe\" --tray", Value(path));

            Assert.True(start.Set(false, @"D:\Moved\LootPoseCompanion.exe"));
            Assert.False(start.IsOn());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void The_real_value_lives_in_the_users_Run_key()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", StartWithWindows.RunKey);
        Assert.Equal("LootPose Companion", StartWithWindows.ValueName);
    }

    private static object? Value(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(StartWithWindows.ValueName);
    }
}
