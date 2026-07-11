namespace MeshcomWebDesk.Helpers;

/// <summary>
/// Central signal-quality thresholds for LoRa links (MeshCom: 433 MHz, SF11/BW 250,
/// receiver sensitivity ≈ −120 dBm). All RSSI/SNR colour scales in the app derive
/// from these constants so every page rates a station identically.
/// The tiers correspond to the link margin above receiver sensitivity:
/// Good &gt; 15 dB, Fair 5–15 dB, Weak &lt; 5 dB.
/// </summary>
public static class SignalHelper
{
    /// <summary>RSSI above this = very strong signal (near-field / line of sight).</summary>
    public const int RssiStrongDbm = -95;

    /// <summary>RSSI above this = solid link with &gt;15 dB margin.</summary>
    public const int RssiGoodDbm = -105;

    /// <summary>RSSI above this (but below Good) = usable, 5–15 dB margin; below = at the limit.</summary>
    public const int RssiWeakDbm = -115;

    /// <summary>SNR above this = clean demodulation.</summary>
    public const double SnrGoodDb = 0;

    /// <summary>SNR above this (but below Good) = usable; below = marginal (SF11 limit ≈ −17.5 dB).</summary>
    public const double SnrWeakDb = -10;

    public enum Tier { None = 0, Good = 1, Fair = 2, Weak = 3 }

    /// <summary>Rates a link by the worse of RSSI and SNR (when both are known).</summary>
    public static Tier Rate(int? rssi, double? snr = null)
    {
        var r = rssi switch
        {
            null           => Tier.None,
            > RssiGoodDbm  => Tier.Good,
            > RssiWeakDbm  => Tier.Fair,
            _              => Tier.Weak
        };
        var s = snr switch
        {
            null        => Tier.None,
            > SnrGoodDb => Tier.Good,
            > SnrWeakDb => Tier.Fair,
            _           => Tier.Weak
        };
        return (Tier)Math.Max((int)r, (int)s);
    }
}
