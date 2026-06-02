namespace Optimizer.WinUI.Helpers;

public static class AppPaths
{
    public const string AppFolderName = "Optimizer";

    public static string AppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string GetDataFile(string name) => Path.Combine(AppDataFolder, name);

    public static void EnsureFolderExists()
    {
        Directory.CreateDirectory(AppDataFolder);
    }
}
