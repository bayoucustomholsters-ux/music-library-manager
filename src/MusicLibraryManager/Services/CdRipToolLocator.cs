namespace MusicLibraryManager.Services;

/// <summary>
/// Optional external CD-rip helpers (cdparanoia / cdda2wav / freaccmd).
/// Native Windows CDDA (IOCTL) is the primary engine; these are a fallback
/// if a redistributable binary is dropped under cdrip\.
/// </summary>
public static class CdRipToolLocator
{
    public static readonly string[] ToolFileNames =
    {
        "cdparanoia.exe", "cdda2wav.exe", "freaccmd.exe",
        "cdparanoia", "cdda2wav"
    };

    public static string? Find(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var baseDir = AppContext.BaseDirectory;
        var dirs = new[]
        {
            Path.Combine(baseDir, "cdrip"),
            Path.Combine(baseDir, "tools", "cdrip"),
            Path.Combine(baseDir, "ffmpeg"),
            baseDir
        };

        foreach (var dir in dirs)
        {
            foreach (var name in ToolFileNames)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in ToolFileNames)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { /* skip */ }
            }
        }

        return null;
    }
}
