using MusicLibraryManager.Services;

namespace MusicLibraryManager.Tests;

public class WavWriterTests
{
    [Fact]
    public void WritesValidHeaderAndPcmSize()
    {
        var path = Path.Combine(Path.GetTempPath(), "mlm-wav-test-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
        try
        {
            var pcm = new byte[2352 * 2]; // two CDDA sectors
            for (var i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i & 0xFF);
            using (var w = new WavWriter(path))
                w.WritePcm(pcm);

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 44 + pcm.Length);
            Assert.Equal((byte)'R', bytes[0]);
            Assert.Equal((byte)'I', bytes[1]);
            Assert.Equal((byte)'F', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
            Assert.Equal((byte)'W', bytes[8]);
            var dataSize = BitConverter.ToInt32(bytes, 40);
            Assert.Equal(pcm.Length, dataSize);
            var riffSize = BitConverter.ToInt32(bytes, 4);
            Assert.Equal(36 + pcm.Length, riffSize);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
