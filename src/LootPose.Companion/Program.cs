namespace LootPose.Companion.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new Form { Text = CompanionInfo.ProductName });
    }
}
