using System.Diagnostics;
using System.Globalization;
using System.Text;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

public sealed class FfmpegService
{
    private readonly string _ffmpegPath;

    public FfmpegService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task ExtractAndConvertAsync(
        string inputPath,
        string outputPath,
        TimeSpan start,
        TimeSpan? end,
        OutputFormat format,
        AppSettings settings,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var args = new StringBuilder();
        args.Append("-y -hide_banner -loglevel error ");
        args.Append(CultureInfo.InvariantCulture, $"-ss {FormatTs(start)} ");
        args.Append(CultureInfo.InvariantCulture, $"-i \"{inputPath}\" ");
        if (end.HasValue)
        {
            var duration = end.Value - start;
            if (duration > TimeSpan.Zero)
                args.Append(CultureInfo.InvariantCulture, $"-t {FormatTs(duration)} ");
        }

        switch (format)
        {
            case OutputFormat.Mp3:
                args.Append("-vn -codec:a libmp3lame ");
                if (settings.BitrateMode == BitrateMode.Vbr)
                    args.Append(CultureInfo.InvariantCulture, $"-q:a {settings.VbrQuality} ");
                else
                    args.Append(CultureInfo.InvariantCulture, $"-b:a {settings.Mp3BitrateKbps}k ");
                break;
            case OutputFormat.Flac:
                args.Append("-vn -codec:a flac ");
                break;
            case OutputFormat.Alac:
                args.Append("-vn -codec:a alac ");
                break;
            case OutputFormat.KeepOriginal:
                args.Append("-vn -codec:a copy ");
                break;
        }

        args.Append(CultureInfo.InvariantCulture, $"\"{outputPath}\"");

        await RunAsync(args.ToString(), ct).ConfigureAwait(false);
    }

    public async Task ConvertWholeFileAsync(
        string inputPath,
        string outputPath,
        OutputFormat format,
        AppSettings settings,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var args = new StringBuilder();
        args.Append("-y -hide_banner -loglevel error ");
        args.Append(CultureInfo.InvariantCulture, $"-i \"{inputPath}\" ");

        switch (format)
        {
            case OutputFormat.Mp3:
                args.Append("-vn -codec:a libmp3lame ");
                if (settings.BitrateMode == BitrateMode.Vbr)
                    args.Append(CultureInfo.InvariantCulture, $"-q:a {settings.VbrQuality} ");
                else
                    args.Append(CultureInfo.InvariantCulture, $"-b:a {settings.Mp3BitrateKbps}k ");
                break;
            case OutputFormat.Flac:
                args.Append("-vn -codec:a flac ");
                break;
            case OutputFormat.Alac:
                args.Append("-vn -codec:a alac ");
                break;
            case OutputFormat.KeepOriginal:
                // Just copy file if same container, else remux
                File.Copy(inputPath, outputPath, overwrite: true);
                return;
        }

        args.Append(CultureInfo.InvariantCulture, $"\"{outputPath}\"");
        await RunAsync(args.ToString(), ct).ConfigureAwait(false);
    }

    private async Task RunAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException("Failed to start ffmpeg.");

        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed (exit {proc.ExitCode}): {stderr}");
    }

    private static string FormatTs(TimeSpan ts)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}");
    }
}
