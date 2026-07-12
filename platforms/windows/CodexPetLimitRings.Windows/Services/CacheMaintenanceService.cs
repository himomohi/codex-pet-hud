namespace CodexPetLimitRings.Windows.Services;

public sealed class CacheMaintenanceService(SettingsStore store)
{
    public long Clean()
    {
        long freed = 0;
        foreach (var name in new[] { "Cache", "Temp" })
        {
            var path = Path.Combine(store.DataDirectory, name);
            freed += DirectorySize(path);
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
        var log = Path.Combine(store.DataDirectory, "Logs", "stderr.log");
        try
        {
            var info = new FileInfo(log);
            if (info.Exists && info.Length > 1_048_576)
            {
                freed += info.Length;
                File.WriteAllText(log, string.Empty);
            }
        }
        catch { }
        return freed;
    }

    private static long DirectorySize(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0; }
        catch { return 0; }
    }
}
