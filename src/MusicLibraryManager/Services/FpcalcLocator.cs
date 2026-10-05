namespace MusicLibraryManager.Services;

public static class FpcalcLocator
{
    public static string? FindFpcalc(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "fpcalc", "fpcalc.exe"),
            Path.Combine(baseDir, "ffmpeg", "fpcalc.exe"),
            Path.Combine(baseDir, "tools", "fpcalc", "fpcalc.exe"),
            Path.Combine(baseDir, "fpcalc.exe"),
            Path.Combine(baseDir, OperatingSystem.IsWindows() ? "fpcalc.exe" : "fpcalc"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var name = OperatingSystem.IsWindows() ? "fpcalc.exe" : "fpcalc";
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { /* skip */ }
        }

        return null;
    }
}
