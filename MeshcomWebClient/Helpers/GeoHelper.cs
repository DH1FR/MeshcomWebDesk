namespace MeshcomWebClient.Helpers;

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
        return $"{Math.Abs(lat.Value):F5}°{ns} {Math.Abs(lon.Value):F5}°{ew}";
    }

    /// <summary>OpenStreetMap URL for a single coordinate.</summary>
    public static string OsmUrl(double lat, double lon) =>
        $"https://www.openstreetmap.org/?mlat={lat:F6}&mlon={lon:F6}&zoom=12";

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
