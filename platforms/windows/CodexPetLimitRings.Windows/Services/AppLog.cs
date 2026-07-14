namespace CodexPetLimitRings.Windows.Services;

internal static class AppLog
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexPetLimitRings",
        "Logs",
        "runtime.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTimeOffset.Now:s} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
