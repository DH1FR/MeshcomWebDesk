namespace MeshcomWebDesk.Services.Translations;

/// <summary>EN → IT translation strings.</summary>
internal static class It
{
    public static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        // License
        ["Licensed for"]                   = "Licenziato per",
        ["Free unlicensed version"]        = "Versione gratuita non licenziata",
        ["Free \u2013 License via PayPal?"]       = "Gratuito \u2013 Licenza via PayPal?",
        ["Get a license: PayPal donation from \u20ac10 \u2192 callsign license key"] = "Ottieni una licenza: donazione PayPal da 10\u20ac \u2192 chiave di licenza per nominativo",
        ["License"]                        = "Licenza",
        ["License Token"]                  = "Token di licenza",
        ["Licensed"]                       = "Licenziato",
        ["Unlicensed"]                     = "Non licenziato",
        ["Enter your license token here."] = "Inserisci qui il tuo token di licenza.",
        ["To obtain a license, send your callsign (without SSID) to DH1FR."] = "Per ottenere una licenza, invia il tuo nominativo (senza SSID) a DH1FR.",

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
        ["Open the node's default web interface"]
                                    = "Apri l'interfaccia web predefinita del nodo",
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

        // Settings – Calendar Beacon
        ["Calendar Beacon"]         = "Beacon calendario",
        ["Sends beacon announcements for recurring events (e.\u00a0g. club meetings). Each entry can send any number of advance announcements (e. g. 3d, 24h, 2h before the event) and/or a transmission at the event time. All lead times are relative to the event start (“Time” field)."]
                                    = "Invia annunci beacon per eventi ricorrenti (es. serate del club). Ogni voce può inviare un numero qualsiasi di preannunci (ad es. 3d, 24h, 2h prima dell'evento) e/o una trasmissione all'ora dell'evento. Tutti i tempi di anticipo si riferiscono all'inizio dell'evento (campo «Ora»).",
        ["Recurrence"]              = "Ricorrenza",
        ["Once"]                    = "Una volta",
        ["Weekly"]                  = "Settimanale",
        ["Bi-weekly"]               = "Bisettimanale",
        ["Monthly (Day)"]           = "Mensile (giorno)",
        ["Nth Weekday"]             = "Ennesimo giorno feriale",
        ["Last Weekday"]            = "Ultimo giorno feriale",
        ["Day of Week"]             = "Giorno della settimana",
        ["Monday"]                  = "Lunedì",
        ["Tuesday"]                 = "Martedì",
        ["Wednesday"]               = "Mercoledì",
        ["Thursday"]                = "Giovedì",
        ["Friday"]                  = "Venerdì",
        ["Saturday"]                = "Sabato",
        ["Sunday"]                  = "Domenica",
        ["Ordinal (1=first…)"]      = "Ordinale (1=primo…)",
        ["Day of Month"]            = "Giorno del mese",
        ["Reference Date (anchor)"] = "Data di riferimento (ancora)",
        ["Announce before"]         = "Annuncia prima",
        ["Comma-separated, e. g. 3d, 24h, 2h (d=days, h=hours, m=minutes). Empty = no advance announcement."]
                                    = "Separato da virgole, ad es. 3d, 24h, 2h (d=giorni, h=ore, m=minuti). Vuoto = nessun preannuncio.",
        ["Invalid value:"]          = "Valore non valido:",
        ["Without a group this entry will not be sent!"]
                                    = "Senza gruppo questa voce non verrà inviata!",
        ["Send at event time"]      = "Invia all'ora dell'evento",
        ["Next event"]              = "Prossimo evento",
        ["Next transmission"]       = "Prossima trasmissione",
        ["none (nothing configured or event passed)"]
                                    = "nessuna (niente configurato o evento passato)",
        ["Add Event"]               = "Aggiungi evento",
        ["Variables:"]              = "Variabili:",
        ["Title"]                   = "Titolo",
        ["Inactive"]                = "Inattivo",

        // Console Command Helper
        ["Status refreshed"]        = "Stato aggiornato",
        ["Use own IP"]              = "Usa il mio IP",
        ["Really send command?"]    = "Inviare davvero il comando?",
        ["Yes, send"]               = "Sì, invia",

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
        ["Send native telemetry (extudp)"] = "Invia telemetria nativa (extudp)",
        ["Sends the values marked with a role below (up to 7: temp, humidity, pressure, temp 2, QNH, gas resistance, CO2) as a native telemetry telegram directly to the node as soon as at least one value changes (not time-scheduled). The node writes them into its sensor variables and immediately triggers a new position beacon whose comment includes the values just like real onboard sensors would. It ignores the telegram while it has real sensor hardware (BME280/BMP3xx/AHT20/SHT21) installed."]
                                    = "Invia i valori contrassegnati qui sotto con un ruolo (fino a 7: temp., umidità, pressione, temp. 2, QNH, resistenza gas, CO2) come telegramma di telemetria nativo direttamente al nodo non appena cambia almeno un valore (non pianificato nel tempo). Il nodo li scrive nelle proprie variabili sensore e attiva subito un nuovo beacon di posizione, nel cui commento i valori compaiono come con sensori reali a bordo. Ignora il telegramma finché ha installato sensori reali (BME280/BMP3xx/AHT20/SHT21).",
        ["Minimum interval (minutes)"]
                                    = "Intervallo minimo (minuti)",
        ["The node applies no throttling of its own – this minimum interval between two extudp sends protects against flooding the mesh when a value jitters."]
                                    = "Il nodo non applica alcuna limitazione propria: questo intervallo minimo tra due invii extudp protegge dal sovraccarico della mesh quando un valore oscilla.",
        ["Role"]                    = "Ruolo",
        ["Role for the map popup and the native extudp telegram"]
                                    = "Ruolo per il popup della mappa e il telegramma extudp nativo",
        ["Role: drives the map popup (temp/humidity/pressure) and/or the target field in the native extudp telegram"]
                                    = "Ruolo: determina il popup della mappa (temp./umidità/pressione) e/o il campo di destinazione nel telegramma extudp nativo",
        ["Temp. 2"]                 = "Temp. 2",
        ["Gas resistance"]          = "Resistenza gas",
        ["Extudp is active, but no value has an assigned role."]
                                    = "Extudp è attivo, ma nessun valore ha un ruolo assegnato.",
        ["Preview native telemetry telegram (extudp)"]
                                    = "Anteprima telegramma di telemetria nativo (extudp)",
        ["None of the assigned extudp values could be found in the file."]
                                    = "Nessuno dei valori extudp assegnati è stato trovato nel file.",

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

        // Appearance / Themes
        ["Appearance"] = "Aspetto",
        ["Your selection is applied immediately as a preview – it becomes permanent only after “Save”."] = "La selezione viene applicata subito come anteprima – diventa permanente solo dopo “Salva”.",
        ["Create custom theme"] = "Crea tema personalizzato",
        ["Import theme"] = "Importa tema",
        ["Import theme file (.mctheme.json)"] = "Importa file tema (.mctheme.json)",
        ["Custom themes can be exported as a file and shared with other users."] = "I temi personalizzati possono essere esportati come file e condivisi con altri utenti.",
        ["Theme name"] = "Nome del tema",
        ["My Theme"] = "Il mio tema",
        ["Please enter a theme name."] = "Inserisci un nome per il tema.",
        ["A theme with this name already exists."] = "Esiste già un tema con questo nome.",
        ["Import failed: not a valid theme file."] = "Importazione non riuscita: file tema non valido.",
        ["Import failed: no valid colour values found."] = "Importazione non riuscita: nessun valore colore valido trovato.",
        ["MeshCom Dark (default)"] = "MeshCom Dark (predefinito)",
        ["Midnight (OLED)"] = "Mezzanotte (OLED)",
        ["Light"] = "Chiaro",
        ["High contrast"] = "Contrasto elevato",
        ["Backgrounds"] = "Sfondi",
        ["Accents & structure"] = "Accenti e struttura",
        ["Buttons"] = "Pulsanti",
        ["Links & callsigns"] = "Link e nominativi",
        ["Status colours"] = "Colori di stato",
        ["Messages & notices"] = "Messaggi e avvisi",
        ["Monitor & lists"] = "Monitor e liste",
    };
}
