namespace MusicLibraryManager.Services;

public static class FfmpegLocator
{
    public static string? FindFfmpeg(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled))
            return bundled;

        var beside = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(beside))
            return beside;

        // PATH lookup
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { /* skip bad path entries */ }
        }

        return null;
    }
}
