namespace MeshcomWebDesk.Models;

/// <summary>
/// Defines how often a calendar event repeats.
/// </summary>
public enum CalendarRecurrence
{
    /// <summary>Einmaliger Termin am <see cref="CalendarBeaconEntry.ReferenceDate"/>.</summary>
    Once,
    /// <summary>Jede Woche am konfigurierten Wochentag.</summary>
    Weekly,
    /// <summary>Jede zweite Woche am konfigurierten Wochentag (Ankerpunkt = <see cref="CalendarBeaconEntry.ReferenceDate"/>).</summary>
    BiWeekly,
    /// <summary>Jeden Monat am konfigurierten Tag (<see cref="CalendarBeaconEntry.EventDayOfMonth"/>).</summary>
    Monthly,
    /// <summary>Den N-ten Wochentag im Monat, z.&#160;B. 1.&#160;Freitag.</summary>
    NthWeekday,
    /// <summary>Den letzten Wochentag im Monat, z.&#160;B. letzten Donnerstag.</summary>
    LastWeekday,
}

/// <summary>
/// Ein wiederkehrender Kalender-Termin, der eine Baken-Nachricht auslöst.
/// Ankündigungen können X Tage und/oder X Stunden vor dem Termin sowie
/// zum Terminzeitpunkt selbst gesendet werden.
/// </summary>
public class CalendarBeaconEntry
{
    /// <summary>Eindeutige ID (kurze GUID). Wird beim Erstellen automatisch gesetzt.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Anzeigename des Termins, z.&#160;B. "OV-Abend K01".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Wenn false, wird der Eintrag komplett ignoriert.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Zielgruppe für die Bake, z.&#160;B. "#262" oder "*".
    /// Die führende '#' wird vor dem Senden abgetrennt.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Bakentext. Unterstützt dieselben {variable}-Platzhalter wie der normale Bakentext
    /// sowie zusätzlich: {title}, {event_date}, {event_time}, {days_until}, {hours_until}.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    // ── Wann tritt der Termin auf? ─────────────────────────────────────────

    /// <summary>Art der Wiederholung.</summary>
    public CalendarRecurrence RecurrenceType { get; set; } = CalendarRecurrence.Weekly;

    /// <summary>
    /// Wochentag des Ereignisses.
    /// Relevant für: Weekly, BiWeekly, NthWeekday, LastWeekday.
    /// </summary>
    public DayOfWeek EventDayOfWeek { get; set; } = DayOfWeek.Friday;

    /// <summary>
    /// Tag des Monats (1–31).
    /// Relevant für: Monthly. Bei Monaten mit weniger Tagen wird der letzte gültige Tag verwendet.
    /// </summary>
    public int EventDayOfMonth { get; set; } = 1;

    /// <summary>
    /// Ordnungszahl des Wochentags im Monat (1 = erster, 2 = zweiter, …).
    /// Relevant für: NthWeekday.
    /// </summary>
    public int WeekdayOrdinal { get; set; } = 1;

    /// <summary>Uhrzeit des Termins als String, z.&#160;B. "19:00".</summary>
    public string EventTime { get; set; } = "19:00";

    /// <summary>Geparste Uhrzeit für die interne Verwendung.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeOnly EventTimeParsed =>
        TimeOnly.TryParse(EventTime, out var t) ? t : new TimeOnly(19, 0);

    /// <summary>
    /// Referenzdatum als String (ISO 8601: "yyyy-MM-dd").
    /// – Once: das genaue Datum des Termins.
    /// – BiWeekly: ein bekannter Termin als Ankerpunkt für den 2-Wochen-Rhythmus.
    /// Bei anderen Typen wird dieser Wert ignoriert.
    /// </summary>
    public string? ReferenceDate { get; set; }

    /// <summary>Geparste Referenzdatum für die interne Verwendung.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateOnly? ReferenceDateParsed =>
        DateOnly.TryParse(ReferenceDate, out var d) ? d : null;

    // ── Wann soll die Bake gesendet werden? ───────────────────────────────

    /// <summary>
    /// Veraltet: Ankündigung X Tage vor dem Termin (0 = deaktiviert).
    /// Wird nur noch beim Einlesen alter Konfigurationen berücksichtigt und beim
    /// Speichern nicht mehr geschrieben – siehe <see cref="AnnounceLeadTimes"/>.
    /// </summary>
    public int AnnounceLeadDays { get; set; } = 0;

    /// <summary>
    /// Veraltet: Ankündigung X Stunden vor dem Termin (0 = deaktiviert).
    /// Wird nur noch beim Einlesen alter Konfigurationen berücksichtigt und beim
    /// Speichern nicht mehr geschrieben – siehe <see cref="AnnounceLeadTimes"/>.
    /// </summary>
    public int AnnounceLeadHours { get; set; } = 2;

    private string? _announceLeadTimes;

    /// <summary>
    /// Vorlaufzeiten für Ankündigungen als kommagetrennte Liste, z.&#160;B. "3d, 24h, 2h".
    /// Einheiten: d = Tage, h = Stunden, m = Minuten; ohne Einheit gelten Stunden.
    /// Leer = keine Vorankündigungen. Solange der Wert noch nie gespeichert wurde (null),
    /// werden die alten Felder <see cref="AnnounceLeadDays"/>/<see cref="AnnounceLeadHours"/> übernommen.
    /// </summary>
    public string AnnounceLeadTimes
    {
        get => _announceLeadTimes ?? LegacyLeadTimes();
        set => _announceLeadTimes = value;
    }

    private string LegacyLeadTimes()
    {
        var parts = new List<string>(2);
        if (AnnounceLeadDays  > 0) parts.Add($"{AnnounceLeadDays}d");
        if (AnnounceLeadHours > 0) parts.Add($"{AnnounceLeadHours}h");
        return string.Join(", ", parts);
    }

    /// <summary>Geparste Vorlaufzeiten, absteigend sortiert (größter Vorlauf zuerst).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<CalendarLeadTime> AnnounceLeadTimesParsed => ParseLeadTimes(AnnounceLeadTimes);

    /// <summary>
    /// Parst eine kommagetrennte Liste von Vorlaufzeiten ("3d, 24h, 2h").
    /// Ungültige Angaben und Duplikate (gleicher Offset) werden ignoriert.
    /// </summary>
    public static List<CalendarLeadTime> ParseLeadTimes(string? raw)
    {
        var result = new List<CalendarLeadTime>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var token in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseLeadTime(token, out var lead) && !result.Any(l => l.Offset == lead.Offset))
                result.Add(lead);
        }

        return result.OrderByDescending(l => l.Offset).ToList();
    }

    /// <summary>Parst eine einzelne Vorlaufzeit wie "3d", "24h", "30m" oder "5" (= Stunden).</summary>
    public static bool TryParseLeadTime(string token, out CalendarLeadTime lead)
    {
        lead = default;
        var t = token.Replace(" ", "");
        if (t.Length == 0) return false;

        char unit = char.IsAsciiDigit(t[^1]) ? 'h' : char.ToLowerInvariant(t[^1]);
        var  num  = char.IsAsciiDigit(t[^1]) ? t : t[..^1];

        if (unit is not ('d' or 'h' or 'm')) return false;
        if (!int.TryParse(num, out var value) || value <= 0) return false;

        lead = unit switch
        {
            'd' => new CalendarLeadTime(TimeSpan.FromDays(value),    $"{value}d"),
            'm' => new CalendarLeadTime(TimeSpan.FromMinutes(value), $"{value}m"),
            _   => new CalendarLeadTime(TimeSpan.FromHours(value),   $"{value}h"),
        };
        return true;
    }

    /// <summary>Wenn true, wird die Bake auch genau zum Terminzeitpunkt gesendet.</summary>
    public bool AnnounceAtEvent { get; set; } = true;

    // ── Externer Prozess ──────────────────────────────────────────────────

    /// <summary>
    /// When true, the beacon text is generated by an external process instead of the
    /// static <see cref="Text"/> field.
    /// </summary>
    public bool IsExternal { get; set; } = false;

    /// <summary>
    /// File name (without path) of the external process to launch, e.g. "beacon.ps1".
    /// The file must reside in <see cref="MeshcomSettings.BotExternalCommandsPath"/>.
    /// </summary>
    public string ExternalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum time in seconds to wait for the external process to complete.
    /// Defaults to 10 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>Eine einzelne Vorlaufzeit einer Ankündigung, z.&#160;B. 48 Stunden vor dem Termin.</summary>
/// <param name="Offset">Zeitspanne vor dem Terminbeginn.</param>
/// <param name="Tag">Normalisiertes Kürzel für Slot-Keys und Anzeige, z.&#160;B. "2d", "3h".</param>
public readonly record struct CalendarLeadTime(TimeSpan Offset, string Tag);
