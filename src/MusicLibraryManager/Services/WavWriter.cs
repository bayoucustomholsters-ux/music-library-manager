using System.Buffers.Binary;

namespace MusicLibraryManager.Services;

/// <summary>
/// Streaming PCM WAV writer (44.1 kHz / 16-bit / stereo — CDDA).
/// Header sizes are patched on dispose.
/// </summary>
public sealed class WavWriter : IDisposable
{
    public const int CdSampleRate = 44100;
    public const short CdChannels = 2;
    public const short CdBitsPerSample = 16;
    public const int HeaderSize = 44;

    private readonly FileStream _stream;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(string path, int sampleRate = CdSampleRate, short channels = CdChannels, short bitsPerSample = CdBitsPerSample)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        WriteHeaderPlaceholder(sampleRate, channels, bitsPerSample);
    }

    public void WritePcm(ReadOnlySpan<byte> pcm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stream.Write(pcm);
        _dataBytes += pcm.Length;
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            PatchSizes();
        }
        finally
        {
            _stream.Dispose();
            _disposed = true;
        }
    }

    private void WriteHeaderPlaceholder(int sampleRate, short channels, short bitsPerSample)
    {
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        Span<byte> hdr = stackalloc byte[HeaderSize];
        hdr.Clear();
        // RIFF
        hdr[0] = (byte)'R'; hdr[1] = (byte)'I'; hdr[2] = (byte)'F'; hdr[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(hdr[4..], 36); // patched later
        hdr[8] = (byte)'W'; hdr[9] = (byte)'A'; hdr[10] = (byte)'V'; hdr[11] = (byte)'E';
        hdr[12] = (byte)'f'; hdr[13] = (byte)'m'; hdr[14] = (byte)'t'; hdr[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(hdr[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(hdr[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(hdr[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[28..], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(hdr[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(hdr[34..], bitsPerSample);
        hdr[36] = (byte)'d'; hdr[37] = (byte)'a'; hdr[38] = (byte)'t'; hdr[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(hdr[40..], 0); // patched later
        _stream.Write(hdr);
    }

    private void PatchSizes()
    {
        var riffSize = (int)(36 + _dataBytes);
        _stream.Seek(4, SeekOrigin.Begin);
        Span<byte> n = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(n, riffSize);
        _stream.Write(n);
        _stream.Seek(40, SeekOrigin.Begin);
        BinaryPrimitives.WriteInt32LittleEndian(n, (int)_dataBytes);
        _stream.Write(n);
        _stream.Flush();
    }
}
