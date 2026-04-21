namespace MeshcomWebDesk.Helpers;

/// <summary>
/// Static lookup tables for MeshCom protocol constants.
/// Source: https://icssw.org/meshcom-2-0-protokoll/
///         https://github.com/icssw-org/MeshCom-Firmware (README – Hardware ID table)
/// </summary>
public static class MeshcomLookup
{
    /// <summary>
    /// Maps hardware_id to a short human-readable name.
    /// Returns "HW-{id}" for unknown IDs.
    /// </summary>
    public static string HwName(int? hwId) => hwId switch
    {
        1  => "TLORA-V2",
        2  => "TLORA-V1",
        3  => "TLORA-V2.1",
        4  => "T-BEAM",
        5  => "T-BEAM-1268",
        6  => "T-BEAM-0.7",
        7  => "T-ECHO",
        8  => "T-DECK",
        9  => "RAK4631",
        10 => "HELTEC-V2",
        11 => "HELTEC-V1",
        12 => "T-BEAM-AXP",
        39 => "EBYTE-E22",
        40 => "T5-EPAPER",
        41 => "HELTEC-TRACK",
        42 => "HELTEC-STICK",
        43 => "HELTEC-V3",
        44 => "HELTEC-E290",
        45 => "T-BEAM-1262",
        46 => "T-DECK-PLUS",
        47 => "TBEAM-SUP",
        48 => "EBYTE-E22-S3",
        49 => "T-LORA-PAGER",
        50 => "T-DECK-PRO",
        51 => "T-BEAM-1W",
        52 => "HELTEC-V4",
        53 => "T-ETH-ELITE",
        null => string.Empty,
        var id => $"HW-{id}"
    };

    /// <summary>
    /// Builds a display firmware string from the raw firmware value and sub-version character.
    /// Handles both integer (35) and string ("4.35") firmware values.
    /// Returns empty string when firmware is 0 or missing.
    /// </summary>
    public static string FormatFirmware(string? rawFirmware, string? fwSub)
    {
        if (string.IsNullOrEmpty(rawFirmware) || rawFirmware == "0")
            return string.Empty;

        // Firmware can be "4.35" (string) or "35" (integer as string)
        // If it looks like a plain integer and is >= 10, format as "major.minor"
        var display = rawFirmware;
        if (int.TryParse(rawFirmware, out var intVer) && intVer > 0 && !rawFirmware.Contains('.'))
            display = $"{intVer / 100}.{intVer % 100:D2}";

        // Append sub-version letter if meaningful (not "#" which means unknown)
        if (!string.IsNullOrEmpty(fwSub) && fwSub != "#")
            display += fwSub;

        return display;
    }
}
