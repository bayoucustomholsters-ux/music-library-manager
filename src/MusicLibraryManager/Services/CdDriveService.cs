using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Detects optical drives and reads the TOC. On Windows, TOC/rip use native CDDA
/// IOCTLs. On other OS, drive listing is best-effort and ripping is not supported.
/// </summary>
public static class CdDriveService
{
    public static IReadOnlyList<CdDriveInfo> ListDrives()
    {
        var list = new List<CdDriveInfo>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return list;
        }

        foreach (var d in drives)
        {
            if (d.DriveType != DriveType.CDRom)
                continue;

            var root = d.Name; // "D:\\" on Windows
            var letter = root.TrimEnd('\\', '/');
            if (letter.Length == 1)
                letter += ":";

            var devicePath = OperatingSystem.IsWindows()
                ? @"\\.\" + (letter.EndsWith(':') ? letter : letter + ":")
                : d.Name;

            var ready = false;
            string? label = null;
            try
            {
                ready = d.IsReady;
                if (ready)
                    label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? null : d.VolumeLabel;
            }
            catch
            {
                ready = false;
            }

            list.Add(new CdDriveInfo
            {
                Letter = letter,
                RootPath = root,
                DevicePath = devicePath,
                IsReady = ready,
                VolumeLabel = label
            });
        }

        return list;
    }

    public static CdToc ReadToc(CdDriveInfo drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Reading a live CD TOC requires Windows CDDA (IOCTL_CDROM_READ_TOC). " +
                "This process is not running on Windows.");

        if (string.IsNullOrWhiteSpace(drive.DevicePath))
            throw new InvalidOperationException("Drive device path is missing.");

        return WindowsCddaAccess.ReadToc(drive.DevicePath, drive.Letter);
    }

    public static Task RipTrackToWavAsync(
        CdDriveInfo drive,
        CdTrack track,
        string wavPath,
        Action<double>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(track);
        if (!track.IsAudio)
            throw new InvalidOperationException($"Track {track.Number} is a data track and cannot be ripped as audio.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "CD ripping uses Windows CDDA sector reads. Run the win-x64 build on Windows 11.");

        return WindowsCddaAccess.RipSectorsToWavAsync(
            drive.DevicePath, track.StartLba, track.LengthSectors, wavPath, progress, ct);
    }

    public static string GetDefaultOutputFolder(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.LastOutputFolder)
            && Directory.Exists(settings.LastOutputFolder))
            return settings.LastOutputFolder!;

        try
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrWhiteSpace(music))
                return music;
        }
        catch { /* ignore */ }

        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
                return Path.Combine(docs, "Music");
        }
        catch { /* ignore */ }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
