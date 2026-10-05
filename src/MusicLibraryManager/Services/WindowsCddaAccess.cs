using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Windows CDDA via SPTI/IOCTL: <c>IOCTL_CDROM_READ_TOC</c> and
/// <c>IOCTL_CDROM_RAW_READ</c> (CDDA = 2352-byte sectors).
/// Primary ripping engine for the published win-x64 app — no Exact Audio Copy,
/// no libcdio, no extra installer. Limitations documented in README.
/// </summary>
internal static class WindowsCddaAccess
{
    public const int SectorSizeCdda = 2352;
    /// <summary>Cooked sector size used in RAW_READ_INFO.DiskOffset (LBA * 2048).</summary>
    public const int CookedSectorSize = 2048;
    /// <summary>Safe per-IOCTL chunk; many USB bridges cap around 64 KiB (≈27 sectors).</summary>
    public const int MaxSectorsPerRead = 26;
    public const int TrackModeCdda = 2; // TRACK_MODE_TYPE.CDDA

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlCdromReadToc = 0x00024000;
    private const uint IoctlCdromRawRead = 0x0002403E;

    public static SafeFileHandle OpenDrive(string devicePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native CDDA ripping is implemented for Windows only.");

        var handle = CreateFileW(
            devicePath,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var err = Marshal.GetLastPInvokeError();
            throw new Win32Exception(err,
                $"Could not open optical drive {devicePath} (Win32 {err}). Close other CD apps and retry.");
        }
        return handle;
    }

    public static byte[] ReadTocBytes(SafeFileHandle handle)
    {
        var buf = new byte[CdTocParser.WindowsTocSize];
        var gch = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            var ok = DeviceIoControl(
                handle,
                IoctlCdromReadToc,
                IntPtr.Zero,
                0,
                gch.AddrOfPinnedObject(),
                (uint)buf.Length,
                out _,
                IntPtr.Zero);
            if (!ok)
            {
                var err = Marshal.GetLastPInvokeError();
                throw new Win32Exception(err,
                    $"Could not read the CD table of contents (Win32 {err}). Insert an audio CD and retry.");
            }
            return buf;
        }
        finally
        {
            gch.Free();
        }
    }

    public static CdToc ReadToc(string devicePath, string? driveLetter)
    {
        using var handle = OpenDrive(devicePath);
        var bytes = ReadTocBytes(handle);
        return CdTocParser.ParseWindowsToc(bytes, driveLetter, devicePath);
    }

    public static async Task RipSectorsToWavAsync(
        string devicePath,
        int startLba,
        int sectorCount,
        string wavPath,
        Action<double>? progress,
        CancellationToken ct)
    {
        if (sectorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sectorCount));

        await Task.Run(() =>
        {
            using var handle = OpenDrive(devicePath);
            using var wav = new WavWriter(wavPath);
            var remaining = sectorCount;
            var lba = startLba;
            var done = 0;
            var failedSectors = 0;
            var sector = new byte[SectorSizeCdda];

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = Math.Min(MaxSectorsPerRead, remaining);
                var read = TryReadChunk(handle, lba, chunk, wav, sector, ref failedSectors, ct);
                lba += read;
                remaining -= read;
                done += read;
                progress?.Invoke(done / (double)sectorCount);
            }

            var failRate = sectorCount == 0 ? 0 : failedSectors / (double)sectorCount;
            if (failRate > 0.05)
                throw new InvalidOperationException(
                    $"Too many unreadable CDDA sectors ({failedSectors}/{sectorCount}). " +
                    "The disc may be copy-protected, badly scratched, or the drive may not support digital audio extraction.");
        }, ct).ConfigureAwait(false);
    }

    private static int TryReadChunk(
        SafeFileHandle handle,
        int lba,
        int count,
        WavWriter wav,
        byte[] oneSector,
        ref int failedSectors,
        CancellationToken ct)
    {
        var bytes = count * SectorSizeCdda;
        var buf = new byte[bytes];
        if (TryRawRead(handle, lba, count, buf))
        {
            wav.WritePcm(buf);
            return count;
        }

        // Retry one sector at a time (USB ATAPI quirks / scratches)
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ok = false;
            for (var attempt = 0; attempt < 3 && !ok; attempt++)
            {
                ok = TryRawRead(handle, lba + i, 1, oneSector);
                if (!ok && attempt < 2)
                    Thread.Sleep(20 * (attempt + 1));
            }

            if (ok)
            {
                wav.WritePcm(oneSector);
            }
            else
            {
                failedSectors++;
                Array.Clear(oneSector);
                wav.WritePcm(oneSector); // silence for the bad sector
            }
        }
        return count;
    }

    private static bool TryRawRead(SafeFileHandle handle, int lba, int sectorCount, byte[] output)
    {
        var info = new RawReadInfo
        {
            DiskOffset = (long)lba * CookedSectorSize,
            SectorCount = (uint)sectorCount,
            TrackMode = TrackModeCdda
        };

        var inSize = Marshal.SizeOf<RawReadInfo>();
        var inPtr = Marshal.AllocHGlobal(inSize);
        var gch = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(info, inPtr, false);
            return DeviceIoControl(
                handle,
                IoctlCdromRawRead,
                inPtr,
                (uint)inSize,
                gch.AddrOfPinnedObject(),
                (uint)output.Length,
                out _,
                IntPtr.Zero);
        }
        finally
        {
            gch.Free();
            Marshal.FreeHGlobal(inPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawReadInfo
    {
        public long DiskOffset;
        public uint SectorCount;
        public int TrackMode;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
