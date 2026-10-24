using System.Globalization;

namespace MeshcomWebDesk.Helpers;

/// <summary>
/// Geographic utility methods for distance calculation and coordinate formatting.
/// </summary>
public static class GeoHelper
{
    /// <summary>
    /// Calculates the great-circle distance between two GPS coordinates using the Haversine formula.
    /// </summary>
    /// <returns>Distance in kilometres.</returns>
    public static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Human-readable distance string (m / km).</summary>
    public static string FormatDistance(double km) => km switch
    {
        < 1.0   => $"{km * 1000:F0} m",
        < 10.0  => $"{km:F2} km",
        < 100.0 => $"{km:F1} km",
        _       => $"{km:F0} km"
    };

    /// <summary>Format decimal-degree coordinates as "47.12345°N 14.56789°E".</summary>
    public static string FormatCoord(double? lat, double? lon)
    {
        if (lat is null || lon is null) return "–";
        var ns = lat.Value >= 0 ? "N" : "S";
        var ew = lon.Value >= 0 ? "E" : "W";
        return $"{Math.Abs(lat.Value).ToString("F5", CultureInfo.InvariantCulture)}°{ns} {Math.Abs(lon.Value).ToString("F5", CultureInfo.InvariantCulture)}°{ew}";
    }

    /// <summary>OpenStreetMap URL for a single coordinate.</summary>
    public static string OsmUrl(double lat, double lon) =>
        $"https://www.openstreetmap.org/?mlat={lat.ToString("F6", CultureInfo.InvariantCulture)}&mlon={lon.ToString("F6", CultureInfo.InvariantCulture)}&zoom=12";

    /// <summary>
    /// Converts decimal-degree coordinates to a Maidenhead (QTH) locator.
    /// </summary>
    /// <param name="lat">Latitude in decimal degrees (−90 … +90).</param>
    /// <param name="lon">Longitude in decimal degrees (−180 … +180).</param>
    /// <param name="length">4 or 6 characters. Default is 6.</param>
    /// <returns>Locator string, e.g. "JN48QN".</returns>
    public static string ToMaidenhead(double lat, double lon, int length = 6)
    {
        lon += 180.0;
        lat += 90.0;

        char f1 = (char)('A' + (int)(lon / 20));
        char f2 = (char)('A' + (int)(lat / 10));

        lon %= 20;
        lat %= 10;

        char d1 = (char)('0' + (int)(lon / 2));
        char d2 = (char)('0' + (int)lat);

        if (length == 4)
            return $"{f1}{f2}{d1}{d2}";

        lon %= 2;
        lat %= 1;

        char s1 = (char)('A' + (int)(lon * 12));
        char s2 = (char)('A' + (int)(lat * 24));

        return $"{f1}{f2}{d1}{d2}{char.ToLower(s1)}{char.ToLower(s2)}";
    }

    /// <summary>
    /// Parses a 4-, 6- or 8-character Maidenhead locator into the bounding box of its grid square.
    /// </summary>
    public static bool TryParseMaidenhead(string? locator,
        out double south, out double west, out double north, out double east)
    {
        south = west = north = east = 0;
        if (locator is null) return false;
        var l = locator.Trim().ToUpperInvariant();
        if (l.Length is not (4 or 6 or 8)) return false;

        bool Field(char c) => c is >= 'A' and <= 'R';
        bool Digit(char c) => c is >= '0' and <= '9';
        bool Sub(char c)   => c is >= 'A' and <= 'X';
        if (!Field(l[0]) || !Field(l[1]) || !Digit(l[2]) || !Digit(l[3])) return false;
        if (l.Length >= 6 && (!Sub(l[4]) || !Sub(l[5]))) return false;
        if (l.Length == 8 && (!Digit(l[6]) || !Digit(l[7]))) return false;

        var lon = (l[0] - 'A') * 20.0 - 180.0 + (l[2] - '0') * 2.0;
        var lat = (l[1] - 'A') * 10.0 - 90.0  + (l[3] - '0');
        var lonSize = 2.0;
        var latSize = 1.0;

        if (l.Length >= 6)
        {
            lonSize = 2.0 / 24; latSize = 1.0 / 24;
            lon += (l[4] - 'A') * lonSize;
            lat += (l[5] - 'A') * latSize;
        }
        if (l.Length == 8)
        {
            lonSize /= 10; latSize /= 10;
            lon += (l[6] - '0') * lonSize;
            lat += (l[7] - '0') * latSize;
        }

        south = lat; west = lon; north = lat + latSize; east = lon + lonSize;
        return true;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
