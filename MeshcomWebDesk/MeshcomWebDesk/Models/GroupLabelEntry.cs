namespace MeshcomWebDesk.Models;

public class GroupLabelEntry
{
    /// <summary>Group number as string, e.g. "262", "20", "9".</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Short label shown inside the chat tab (e.g. "DL", "DACH", "OE"). Keep brief.</summary>
    public string ShortLabel { get; set; } = string.Empty;

    /// <summary>Full human-readable description shown in settings and as tooltip (e.g. "DL – Deutschland").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Returns the default group label list based on the official MeshCom GRC group list
    /// from https://icssw.org/meshcom-grc-gruppen/
    /// </summary>
    public static List<GroupLabelEntry> Defaults =>
    [
        new() { Group = "2",     ShortLabel = "EU",     Label = "EU – Europa (europaweit)" },
        new() { Group = "3",     ShortLabel = "US",     Label = "US – USA (US-weit)" },
        new() { Group = "9",     ShortLabel = "LOC",    Label = "LOC – Lokal (nur HF-Wolke)" },
        new() { Group = "10",    ShortLabel = "WW-GE",  Label = "WW-GE – Weltweit Deutsch" },
        new() { Group = "13",    ShortLabel = "WW-EN",  Label = "WW-EN – Worldwide English" },
        new() { Group = "20",    ShortLabel = "DACH",   Label = "DACH – Deutschland, Österreich, Schweiz" },
        new() { Group = "30",    ShortLabel = "SV",     Label = "SV – Griechenland" },
        new() { Group = "204",   ShortLabel = "PA",     Label = "PA – Niederlande" },
        new() { Group = "206",   ShortLabel = "ON",     Label = "ON – Belgien" },
        new() { Group = "208",   ShortLabel = "F",      Label = "F – Frankreich" },
        new() { Group = "214",   ShortLabel = "EA",     Label = "EA – Spanien" },
        new() { Group = "222",   ShortLabel = "I",      Label = "I – Italien" },
        new() { Group = "22201", ShortLabel = "I-LAZ",  Label = "I – Lazio" },
        new() { Group = "22202", ShortLabel = "I-SAR",  Label = "I – Sardegna" },
        new() { Group = "22203", ShortLabel = "I-UMB",  Label = "I – Umbria" },
        new() { Group = "22211", ShortLabel = "I-LIG",  Label = "I – Liguria" },
        new() { Group = "22213", ShortLabel = "I-VDA",  Label = "I – Valle d'Aosta" },
        new() { Group = "22221", ShortLabel = "I-LOM",  Label = "I – Lombardia" },
        new() { Group = "22231", ShortLabel = "I-FVG",  Label = "I – Friuli Venezia Giulia" },
        new() { Group = "22232", ShortLabel = "I-TAA",  Label = "I – Trentino Alto Adige" },
        new() { Group = "22233", ShortLabel = "I-VEN",  Label = "I – Veneto" },
        new() { Group = "22241", ShortLabel = "I-EMR",  Label = "I – Emilia Romagna" },
        new() { Group = "22251", ShortLabel = "I-TOS",  Label = "I – Toscana" },
        new() { Group = "22261", ShortLabel = "I-ABR",  Label = "I – Abruzzo" },
        new() { Group = "22262", ShortLabel = "I-MAR",  Label = "I – Marche" },
        new() { Group = "22271", ShortLabel = "I-PUG",  Label = "I – Puglia" },
        new() { Group = "22281", ShortLabel = "I-BAS",  Label = "I – Basilicata" },
        new() { Group = "22282", ShortLabel = "I-CAL",  Label = "I – Calabria" },
        new() { Group = "22283", ShortLabel = "I-CAM",  Label = "I – Campania" },
        new() { Group = "22284", ShortLabel = "I-MOL",  Label = "I – Molise" },
        new() { Group = "22291", ShortLabel = "I-SIC",  Label = "I – Sicilia" },
        new() { Group = "22299", ShortLabel = "I-MET",  Label = "I – Meteo/data/sensors" },
        new() { Group = "226",   ShortLabel = "YO",     Label = "YO – Rumänien" },
        new() { Group = "228",   ShortLabel = "HB",     Label = "HB – Schweiz" },
        new() { Group = "232",   ShortLabel = "OE",     Label = "OE – Österreich" },
        new() { Group = "2321",  ShortLabel = "OE1",    Label = "OE1 – Wien" },
        new() { Group = "2322",  ShortLabel = "OE2",    Label = "OE2 – Salzburg" },
        new() { Group = "2323",  ShortLabel = "OE3",    Label = "OE3 – Niederösterreich" },
        new() { Group = "2324",  ShortLabel = "OE4",    Label = "OE4 – Burgenland" },
        new() { Group = "2325",  ShortLabel = "OE5",    Label = "OE5 – Oberösterreich" },
        new() { Group = "2326",  ShortLabel = "OE6",    Label = "OE6 – Steiermark" },
        new() { Group = "2327",  ShortLabel = "OE7",    Label = "OE7 – Tirol" },
        new() { Group = "2328",  ShortLabel = "OE8",    Label = "OE8 – Kärnten" },
        new() { Group = "2329",  ShortLabel = "OE9",    Label = "OE9 – Vorarlberg" },
        new() { Group = "234",   ShortLabel = "G",      Label = "G – Great Britain" },
        new() { Group = "238",   ShortLabel = "OZ",     Label = "OZ – Dänemark" },
        new() { Group = "240",   ShortLabel = "SA",     Label = "SA – Schweden" },
        new() { Group = "260",   ShortLabel = "SP",     Label = "SP – Polen" },
        new() { Group = "262",   ShortLabel = "DL",     Label = "DL – Deutschland" },
        new() { Group = "2622",  ShortLabel = "DL2",    Label = "DL2 – Schleswig-Holstein" },
        new() { Group = "26206", ShortLabel = "DL06",   Label = "DL06 – DARC OV Dachau C06" },
        new() { Group = "26216", ShortLabel = "DL16",   Label = "DL16 – Chiemgau" },
        new() { Group = "26220", ShortLabel = "DL20",   Label = "DL20 – Großraum Hamburg" },
        new() { Group = "26221", ShortLabel = "DL21",   Label = "DL21 – Stadt Hamburg" },
        new() { Group = "26225", ShortLabel = "DL25",   Label = "DL25 – AFU Nord" },
        new() { Group = "26235", ShortLabel = "DL35",   Label = "DL35 – NI-Südheide" },
        new() { Group = "26242", ShortLabel = "DL42",   Label = "DL42 – Münsterland" },
        new() { Group = "26244", ShortLabel = "DL44",   Label = "DL44 – Freising" },
        new() { Group = "26255", ShortLabel = "DL55",   Label = "DL55 – Pfalz" },
        new() { Group = "26266", ShortLabel = "DL66",   Label = "DL66 – Saar" },
        new() { Group = "26269", ShortLabel = "DL69",   Label = "DL69 – Hessen/RLP" },
        new() { Group = "26289", ShortLabel = "DL89",   Label = "DL89 – München Stadt" },
        new() { Group = "26295", ShortLabel = "DL95",   Label = "DL95 – Ostthüringen" },
        new() { Group = "26298", ShortLabel = "DL98",   Label = "DL98 – Thüringen" },
        new() { Group = "26379", ShortLabel = "DL3-79", Label = "DL3 79 – Hochrhein" },
        new() { Group = "292",   ShortLabel = "T7",     Label = "T7 – San Marino" },
        new() { Group = "293",   ShortLabel = "S5",     Label = "S5 – Slowenien" },
        new() { Group = "460",   ShortLabel = "B",      Label = "B – China" },
        new() { Group = "901",   ShortLabel = "9V",     Label = "9V – Singapur" },
        new() { Group = "19000", ShortLabel = "F-19",   Label = "F – France dép. 19/87" },
    ];
}
