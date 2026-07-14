namespace CodexPetLimitRings.Windows.Services;

internal static class CodexPaths
{
    public static string Home { get; } = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
        : Environment.GetEnvironmentVariable("CODEX_HOME")!;
}
