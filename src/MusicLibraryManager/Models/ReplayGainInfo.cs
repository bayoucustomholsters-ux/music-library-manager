namespace MusicLibraryManager.Models;

public sealed class ReplayGainInfo
{
    /// <summary>Track gain in dB (e.g. -6.54).</summary>
    public double TrackGainDb { get; set; }

    /// <summary>Track peak as linear amplitude 0..1+ (e.g. 0.988).</summary>
    public double TrackPeak { get; set; }

    public string TrackGainTag => $"{TrackGainDb:0.00} dB".Replace(',', '.');
    public string TrackPeakTag => TrackPeak.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
}
