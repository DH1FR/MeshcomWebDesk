namespace MeshcomWebDesk.Services.Translations;

/// <summary>EN → IT translation strings.</summary>
internal static class It
{
    public static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        // Chat – ping confirmation
        ["This usually belongs in a direct tab."] = "Di solito appartiene a una scheda diretta.",

        // Beacon
        ["Please choose beacon intervals carefully."] = "Si prega di scegliere gli intervalli di beacon con attenzione.",

        // General
        ["Accept"]                  = "Accetta",
        ["Actions"]                 = "Azioni",
        ["Active"]                  = "Attivo",
        ["Add"]                     = "Aggiungi",
        ["All"]                     = "Tutti",
        ["Apply"]                   = "Applica",
        ["Cancel"]                  = "Annulla",
        ["Clear"]                   = "Cancella",
        ["Close"]                   = "Chiudi",
        ["Copy"]                    = "Copia",
        ["Create"]                  = "Crea",
        ["Date"]                    = "Data",
        ["Delete"]                  = "Elimina",
        ["Description"]             = "Descrizione",
        ["Disabled"]                = "Disabilitato",
        ["Done"]                    = "Fatto",
        ["Download"]                = "Scarica",
        ["Edit"]                    = "Modifica",
        ["Enabled"]                 = "Abilitato",
        ["Error"]                   = "Errore",
        ["Export"]                  = "Esporta",
        ["Help"]                    = "Aiuto",
        ["Hide"]                    = "Nascondi",
        ["History"]                 = "Cronologia",
        ["Hours"]                   = "Ore",
        ["Import"]                  = "Importa",
        ["Info"]                    = "Info",
        ["Interval"]                = "Intervallo",
        ["Language"]                = "Lingua",
        ["Last"]                    = "Ultimo",
        ["Load"]                    = "Carica",
        ["Manual"]                  = "Manuale",
        ["Messages"]                = "Messaggi",
        ["Model"]                   = "Modello",
        ["Name"]                    = "Nome",
        ["New"]                     = "Nuovo",
        ["No"]                      = "No",
        ["OK"]                      = "OK",
        ["Online"]                  = "Online",
        ["Open"]                    = "Apri",
        ["Page"]                    = "Pagina",
        ["Password"]                = "Password",
        ["Pause"]                   = "Pausa",
        ["Port"]                    = "Porta",
        ["Primary"]                 = "Principale",
        ["Provider"]                = "Provider",
        ["Refresh"]                 = "Aggiorna",
        ["Remove"]                  = "Rimuovi",
        ["Reset"]                   = "Reimposta",
        ["Response"]                = "Risposta",
        ["Resume"]                  = "Riprendi",
        ["Save"]                    = "Salva",
        ["Search"]                  = "Cerca",
        ["Send"]                    = "Invia",
        ["Show"]                    = "Mostra",
        ["Status"]                  = "Stato",
        ["Stop"]                    = "Ferma",
        ["Summary"]                 = "Riepilogo",
        ["Test"]                    = "Test",
        ["Text"]                    = "Testo",
        ["Time"]                    = "Ora",
        ["Timeout"]                 = "Timeout",
        ["Total"]                   = "Totale",
        ["Type"]                    = "Tipo",
        ["Unknown"]                 = "Sconosciuto",
        ["Update"]                  = "Aggiorna",
        ["Upload"]                  = "Carica",
        ["Version"]                 = "Versione",
        ["Yes"]                     = "Sì",

        // Navigation / Pages
        ["About"]                   = "Informazioni",
        ["Chat"]                    = "Chat",
        ["Console"]                 = "Console",
        ["Map"]                     = "Mappa",
        ["Monitor"]                 = "Monitor",
        ["Settings"]                = "Impostazioni",

        // Console page
        ["Connect"]                 = "Connetti",
        ["Connected"]               = "Connesso",
        ["Disconnect"]              = "Disconnetti",
        ["Disconnected"]            = "Disconnesso",
        ["Not connected"]           = "Non connesso",
        ["Enter command …"]         = "Inserisci comando…",
        ["LoRa"]                    = "LoRa",
        ["Reboot"]                  = "Riavvia",
        ["Yes, reboot"]             = "Sì, riavvia",
        ["OTA"]                     = "OTA",
        ["Serial Console"]          = "Console seriale",
        ["TLS Console"]             = "Console TLS",
        ["Select node"]             = "Seleziona nodo",
        ["Trust & save"]            = "Fidati e salva",
        ["Unknown Certificate"]     = "Certificato sconosciuto",
        ["Save fingerprint in node settings to permanently trust this node."]
                                    = "Salva l'impronta nelle impostazioni del nodo per fidarti permanentemente.",
        ["Reboot node?"]            = "Riavviare il nodo?",
        ["The node will be rebooted. The TLS connection will be disconnected afterwards."]
                                    = "Il nodo verrà riavviato. La connessione TLS sarà disconnessa.",
        ["Starting OTA Update"]     = "Avvio aggiornamento OTA",
        ["The node is starting the OTA web server. Page opens in"]
                                    = "Il nodo sta avviando il server web OTA. La pagina si apre tra",
        ["seconds"]                 = "secondi",
        ["Pause output (new lines still buffered)"]
                                    = "Metti in pausa (nuove righe ancora memorizzate)",
        ["Resume output (scroll re-enabled)"]
                                    = "Riprendi output (scorrimento riattivato)",
        ["LoRa debug highlighting on/off"]
                                    = "Evidenziazione debug LoRa on/off",
        ["Start OTA update – sends --ota-update to node and opens OTA web server"]
                                    = "Avvia aggiornamento OTA – invia --ota-update al nodo",
        ["Reboot node – sends --reboot to node"]
                                    = "Riavvia nodo – invia --reboot al nodo",
        ["Last seen"]               = "Visto l'ultima volta",
        ["No packet yet"]           = "Nessun pacchetto ancora",

        // Chat
        ["Broadcast"]               = "Broadcast",
        ["Direct"]                  = "Diretto",
        ["Direct message"]          = "Messaggio diretto",
        ["Group"]                   = "Gruppo",
        ["Group message"]           = "Messaggio gruppo",
        ["Message"]                 = "Messaggio",
        ["Send message"]            = "Invia messaggio",
        ["All direct QSOs"]         = "Tutti i QSO diretti",
        ["Search all direct QSOs"]  = "Cerca tutti i QSO diretti",
        ["No messages"]             = "Nessun messaggio",
        ["Load more"]               = "Carica altri",
        ["New direct message from"] = "Nuovo messaggio diretto da",
        ["Copy to clipboard"]       = "Copia negli appunti",
        ["Global search"]           = "Ricerca globale",
        ["QSO"]                     = "QSO",
        ["QSO Summary"]             = "Riepilogo QSO",
        ["AI Summary"]              = "Riepilogo IA",
        ["AI Search"]               = "Ricerca IA",
        ["Generate"]                = "Genera",
        ["Regenerate"]              = "Rigenera",
        ["Search in history…"]      = "Cerca nella cronologia…",
        ["Filter by date"]          = "Filtra per data",
        ["Filter by text"]          = "Filtra per testo",
        ["Date from"]               = "Data da",
        ["Date to"]                 = "Data a",
        ["Previous page"]           = "Pagina precedente",
        ["Next page"]               = "Pagina successiva",

        // Settings – General
        ["UI Language"]             = "Lingua interfaccia",
        ["Callsign"]                = "Nominativo",
        ["My callsign"]             = "Il mio nominativo",
        ["Own callsign"]            = "Il mio nominativo",
        ["Callsign (with SSID, e.g. OE1ABC-1)"]
                                    = "Nominativo (con SSID, es. OE1ABC-1)",
        ["My locator"]              = "Il mio locatore",
        ["Please select"]           = "Seleziona",
        ["Enter manually …"]        = "Inserisci manualmente…",
        ["Settings saved"]          = "Impostazioni salvate",
        ["Save settings"]           = "Salva impostazioni",
        ["Backup & Restore"]        = "Backup e ripristino",
        ["Backup settings"]         = "Backup impostazioni",
        ["Restore settings"]        = "Ripristina impostazioni",
        ["Download backup"]         = "Scarica backup",
        ["Import settings"]         = "Importa impostazioni",
        ["Expand all"]              = "Espandi tutto",
        ["Collapse all"]            = "Comprimi tutto",
        ["Display name"]            = "Nome visualizzato",

        // Settings – Node
        ["Node"]                    = "Nodo",
        ["Add node"]                = "Aggiungi nodo",
        ["Delete node"]             = "Elimina nodo",
        ["Remove node"]             = "Rimuovi nodo",
        ["Node name"]               = "Nome nodo",
        ["Primary node"]            = "Nodo principale",
        ["Add node profile"]        = "Aggiungi profilo nodo",
        ["All nodes"]               = "Tutti i nodi",
        ["Device IP"]               = "IP dispositivo",
        ["Device IP address"]       = "Indirizzo IP dispositivo",
        ["Device Port"]             = "Porta dispositivo",
        ["Device port (UDP)"]       = "Porta dispositivo (UDP)",
        ["Listen IP"]               = "IP ascolto",
        ["Listen IP (0.0.0.0 = all interfaces)"]
                                    = "IP ascolto (0.0.0.0 = tutte le interfacce)",
        ["Listen Port"]             = "Porta ascolto",
        ["Listen port (UDP)"]       = "Porta ascolto (UDP)",
        ["Local UDP port"]          = "Porta UDP locale",
        ["Node port (UDP)"]         = "Porta nodo (UDP)",
        ["Node settings"]           = "Impostazioni nodo",
        ["Certificate fingerprint (SHA-256)"]
                                    = "Impronta certificato (SHA-256)",
        ["TLS certificate fingerprint"]
                                    = "Impronta certificato TLS",
        ["TLS password"]            = "Password TLS",
        ["TLS enabled"]             = "TLS abilitato",
        ["TLS port"]                = "Porta TLS",

        // Settings – Console
        ["Console mode"]            = "Modalità console",
        ["Enable serial console"]   = "Abilita console seriale",
        ["Enable TLS console"]      = "Abilita console TLS",
        ["COM Port"]                = "Porta COM",
        ["COM port (e.g. COM3 or /dev/ttyUSB0)"]
                                    = "Porta COM (es. COM3 o /dev/ttyUSB0)",
        ["Baud rate"]               = "Velocità baud",
        ["Baud rate (e.g. 115200)"] = "Velocità baud (es. 115200)",
        ["Host / IP"]               = "Host / IP",
        ["Connect to node"]         = "Connetti al nodo",
        ["Fingerprint"]             = "Impronta",
        ["Fingerprint saved"]       = "Impronta salvata",

        // Settings – Beacon
        ["Beacon"]                  = "Beacon",
        ["Beacon enabled"]          = "Beacon abilitato",
        ["Beacon group"]            = "Gruppo beacon",
        ["Beacon group (e.g. #OE)"] = "Gruppo beacon (es. #OE)",
        ["Beacon interval"]         = "Intervallo beacon",
        ["Beacon interval (hours)"] = "Intervallo beacon (ore)",
        ["Interval (hours, min. 1)"]= "Intervallo (ore, min. 1)",
        ["Beacon text"]             = "Testo beacon",
        ["Test Beacon"]             = "Test beacon",
        ["Send Beacon Now"]         = "Invia beacon ora",
        ["Send now"]                = "Invia ora",
        ["Sending…"]                = "Invio in corso…",

        // Settings – Auto-Reply
        ["Auto-Reply"]              = "Risposta automatica",
        ["Auto-reply enabled"]      = "Risposta auto abilitata",
        ["Auto-reply enabled (first contact only)"]
                                    = "Risposta auto abilitata (solo primo contatto)",
        ["Auto-reply text"]         = "Testo risposta auto",
        ["Test auto-reply"]         = "Test risposta auto",

        // Settings – Bot
        ["Bot"]                     = "Bot",
        ["Bot commands"]            = "Comandi bot",
        ["Bot enabled"]             = "Bot abilitato",
        ["Bot command name (without --)"]
                                    = "Nome comando bot (senza --)",
        ["Bot response text"]       = "Testo risposta bot",
        ["Test bot command"]        = "Test comando bot",
        ["Export bot commands"]     = "Esporta comandi bot",
        ["Import bot commands"]     = "Importa comandi bot",
        ["User-defined commands"]   = "Comandi personalizzati",
        ["Add command"]             = "Aggiungi comando",

        // Settings – Watchlist
        ["Watchlist"]               = "Lista controllo",
        ["Watchlist enabled"]       = "Lista controllo abilitata",
        ["Add to watchlist"]        = "Aggiungi alla lista",
        ["CQ detection"]            = "Rilevamento CQ",
        ["CQ detection enabled"]    = "Rilevamento CQ abilitato",
        ["Auto-dismiss after"]      = "Chiudi automaticamente dopo",

        // Settings – Map
        ["Live Map"]                = "Mappa live",
        ["Map settings"]            = "Impostazioni mappa",
        ["Show gateway"]            = "Mostra gateway",
        ["Show own position"]       = "Mostra posizione propria",
        ["MH list"]                 = "Lista MH",
        ["MH max. age (hours)"]     = "Età max. MH (ore)",
        ["Coverage"]                = "Copertura",
        ["Gateway"]                 = "Gateway",
        ["Signal strength"]         = "Potenza segnale",
        ["SNR"]                     = "SNR",

        // Settings – Station / HF
        ["Station / HF parameters"] = "Parametri stazione / HF",
        ["Station parameters"]      = "Parametri stazione",
        ["TX power (dBm)"]          = "Potenza TX (dBm)",
        ["Cable type"]              = "Tipo cavo",
        ["Cable length"]            = "Lunghezza cavo",
        ["Cable length (m)"]        = "Lunghezza cavo (m)",
        ["Cable attenuation (dB/10m)"]
                                    = "Attenuazione cavo (dB/10m)",
        ["Antenna"]                 = "Antenna",
        ["Antenna gain"]            = "Guadagno antenna",
        ["Antenna height"]          = "Altezza antenna",
        ["Antenna height (m)"]      = "Altezza antenna (m)",
        ["Antenna type"]            = "Tipo antenna",
        ["Frequency"]               = "Frequenza",
        ["Frequency (MHz)"]         = "Frequenza (MHz)",
        ["System margin (dB)"]      = "Margine sistema (dB)",
        ["EIRP (dBm)"]              = "EIRP (dBm)",
        ["Free-space range (km)"]   = "Portata spazio libero (km)",

        // Settings – Telemetry
        ["Telemetry"]               = "Telemetria",
        ["Telemetry enabled"]       = "Telemetria abilitata",
        ["Telemetry interval (minutes)"]
                                    = "Intervallo telemetria (minuti)",
        ["Temperature"]             = "Temperatura",

        // Settings – Database
        ["Database"]                = "Database",
        ["Database provider"]       = "Provider database",
        ["MySQL connection string"]  = "Stringa connessione MySQL",
        ["InfluxDB URL"]            = "URL InfluxDB",
        ["InfluxDB bucket"]         = "Bucket InfluxDB",
        ["InfluxDB token"]          = "Token InfluxDB",

        // Settings – AI
        ["AI"]                      = "IA",
        ["AI API Key"]              = "Chiave API IA",
        ["AI Model"]                = "Modello IA",
        ["AI Provider"]             = "Provider IA",
        ["Max. messages for AI summary"]
                                    = "Max. messaggi per riepilogo IA",
        ["Token usage"]             = "Utilizzo token",
        ["Generate summary"]        = "Genera riepilogo",
        ["Message history"]         = "Cronologia messaggi",
        ["History (paginated)"]     = "Cronologia (paginata)",

        // Settings – MQTT
        ["MQTT"]                    = "MQTT",
        ["MQTT broker"]             = "Broker MQTT",
        ["MQTT enabled"]            = "MQTT abilitato",
        ["MQTT password"]           = "Password MQTT",
        ["MQTT port"]               = "Porta MQTT",
        ["MQTT prefix"]             = "Prefisso MQTT",
        ["MQTT username"]           = "Nome utente MQTT",

        // Settings – QRZ
        ["QRZ"]                     = "QRZ",
        ["QRZ password"]            = "Password QRZ",
        ["QRZ username"]            = "Nome utente QRZ",
        ["Test Connection"]         = "Testa connessione",
        ["Testing…"]                = "Test in corso…",
        ["Clear Cache"]             = "Svuota cache",
        ["Max. age (days, 0 = unlimited)"]
                                    = "Età max. (giorni, 0 = illimitato)",
        ["0 = unlimited (never refresh)"]
                                    = "0 = illimitato (non aggiornare mai)",

        // Settings – Webhook
        ["Webhook"]                 = "Webhook",
        ["Webhook enabled"]         = "Webhook abilitato",
        ["Webhook URL"]             = "URL webhook",
        ["Server URL"]              = "URL server",

        // Settings – Quick texts / UI
        ["Quick texts"]             = "Testi rapidi",
        ["Own messages align left"] = "Messaggi propri allineati a sinistra",
        ["Monitor max. messages"]   = "Max. messaggi monitor",
        ["Sound"]                   = "Suono",
        ["Sound enabled"]           = "Suono abilitato",
        ["Voice"]                   = "Voce",
        ["Voice announcements"]     = "Annunci vocali",
        ["Voice enabled"]           = "Voce abilitata",

        // Group labels
        ["Group filter"]            = "Filtro gruppo",
        ["Group filter enabled"]    = "Filtro gruppo abilitato",
        ["Group labels"]            = "Etichette gruppo",
        ["Add group label"]         = "Aggiungi etichetta gruppo",

        // Misc
        ["Firmware"]                = "Firmware",
        ["OTA update started"]      = "Aggiornamento OTA avviato",
        ["Connection refused"]      = "Connessione rifiutata",
        ["More info"]               = "Ulteriori informazioni",
    };
}
