namespace MeshcomWebDesk.Models;

/// <summary>Art des Konsolenbefehls – bestimmt das UI-Widget.</summary>
public enum ConsoleCommandType
{
    /// <summary>on / off Toggle</summary>
    Toggle,
    /// <summary>Numerischer Wert mit optionaler Einheit</summary>
    Value,
    /// <summary>Text-/String-Wert</summary>
    Text,
    /// <summary>Einmalige Aktion ohne Parameter</summary>
    Action,
}

/// <summary>Gruppe für die Darstellung in der UI.</summary>
public enum ConsoleCommandGroup
{
    LoRa,
    System,
    Netzwerk,
    Debug,
    GPS,
    Sensoren,
    Telemetrie,
}

/// <summary>Definition eines MeshCom-Konsolenbefehls.</summary>
public sealed class ConsoleCommandDef
{
    /// <summary>Befehlsname ohne "--" Präfix, z.B. "txpower".</summary>
    public required string Name { get; init; }

    /// <summary>Gruppe für die Darstellung.</summary>
    public ConsoleCommandGroup Group { get; init; }

    /// <summary>Typ des Befehls (Toggle / Value / Text / Action).</summary>
    public ConsoleCommandType Type { get; init; }

    /// <summary>Kurzbeschreibung für den User.</summary>
    public required string Description { get; init; }

    /// <summary>Einheit für Value-Befehle, z.B. "MHz", "dBm", "kHz".</summary>
    public string? Unit { get; init; }

    /// <summary>Min-Wert für numerische Befehle.</summary>
    public double? Min { get; init; }

    /// <summary>Max-Wert für numerische Befehle.</summary>
    public double? Max { get; init; }

    /// <summary>Schrittweite für numerische Befehle.</summary>
    public double? Step { get; init; }

    /// <summary>Vorbelegte Optionen für Dropdown (leer = freie Eingabe).</summary>
    public string[] Options { get; init; } = [];

    /// <summary>Regex-Muster um den aktuellen Wert aus der Console-Ausgabe zu extrahieren.</summary>
    public string? ParsePattern { get; init; }

    /// <summary>
    /// Optionaler Befehl (ohne "--") der beim Refresh zusätzlich gesendet wird,
    /// um den Status dieses Befehls abzufragen (z.B. "netconsole" → antwortet mit Status-Zeile).
    /// Nur setzen wenn der Befehl selbst einen lesbaren Status ausgibt.
    /// </summary>
    public string? StatusCommand { get; init; }

    /// <summary>
    /// Wenn true, zeigt das UI einen "Meine IP"-Button der die lokalen IPv4-Adressen
    /// des Hosts als Schnellauswahl anbietet (sinnvoll z.B. für --extudpip).
    /// </summary>
    public bool SuggestLocalIp { get; init; }

    /// <summary>Wenn true, wird das Eingabefeld als Passwortfeld (type="password") gerendert.</summary>
    public bool IsPassword { get; init; }

    /// <summary>Bei Action: Bestätigung erforderlich (z.B. Reboot).</summary>
    public bool NeedsConfirm { get; init; }

    /// <summary>
    /// Gibt an dass die Firmware für diesen Befehl keinen Statuswert zurückliefert.
    /// Der gesendete Wert wird nach Timeout optimistisch übernommen,
    /// solange keine Fehlermeldung eingetroffen ist.
    /// </summary>
    public bool OptimisticUpdate { get; init; }

    /// <summary>
    /// Wenn gesetzt, zeigt ein Toggle-Befehl zusätzlich ein Text-Eingabefeld an.
    /// Der eingegebene Wert wird als Parameter an den Befehl gesendet (z.B. --via OE5XYZ).
    /// </summary>
    public string? ExtraTextPlaceholder { get; init; }

    /// <summary>Vollständiger Befehlsstring mit "--" Präfix.</summary>
    public string Command => $"--{Name}";
}
