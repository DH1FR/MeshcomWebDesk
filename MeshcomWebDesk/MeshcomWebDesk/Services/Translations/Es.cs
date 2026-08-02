namespace MeshcomWebDesk.Services.Translations;

/// <summary>EN → ES translation strings.</summary>
internal static class Es
{
    public static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        // License
        ["Licensed for"]                   = "Licenciado para",
        ["Free unlicensed version"]        = "Versión gratuita sin licencia",
        ["Free – License via PayPal?"]       = "Gratuito – ¿Licencia vía PayPal?",
        ["Get a license: PayPal donation from €10 → callsign license key"] = "Obtén una licencia: donación PayPal desde 10€ → clave de licencia por indicativo",
        ["License"]                        = "Licencia",
        ["License Token"]                  = "Token de licencia",
        ["Licensed"]                       = "Licenciado",
        ["Unlicensed"]                     = "Sin licencia",
        ["Enter your license token here."] = "Introduce tu token de licencia aquí.",
        ["To obtain a license, send your callsign (without SSID) to DH1FR."] = "Para obtener una licencia, envía tu indicativo (sin SSID) a DH1FR.",

        // Chat – ping confirmation
        ["This usually belongs in a direct tab."] = "Esto normalmente pertenece a una pestaña directa.",

        // Beacon
        ["Please choose beacon intervals carefully."] = "Por favor, elige los intervalos de baliza con cuidado.",

        // General
        ["Accept"]                  = "Aceptar",
        ["Actions"]                 = "Acciones",
        ["Active"]                  = "Activo",
        ["Add"]                     = "Añadir",
        ["All"]                     = "Todos",
        ["Apply"]                   = "Aplicar",
        ["Cancel"]                  = "Cancelar",
        ["Clear"]                   = "Borrar",
        ["Close"]                   = "Cerrar",
        ["Copy"]                    = "Copiar",
        ["Create"]                  = "Crear",
        ["Date"]                    = "Fecha",
        ["Delete"]                  = "Eliminar",
        ["Description"]             = "Descripción",
        ["Disabled"]                = "Desactivado",
        ["Done"]                    = "Listo",
        ["Download"]                = "Descargar",
        ["Edit"]                    = "Editar",
        ["Enabled"]                 = "Activado",
        ["Error"]                   = "Error",
        ["Export"]                  = "Exportar",
        ["Help"]                    = "Ayuda",
        ["Hide"]                    = "Ocultar",
        ["History"]                 = "Historial",
        ["Hours"]                   = "Horas",
        ["Import"]                  = "Importar",
        ["Info"]                    = "Info",
        ["Interval"]                = "Intervalo",
        ["Language"]                = "Idioma",
        ["Last"]                    = "Último",
        ["Load"]                    = "Cargar",
        ["Manual"]                  = "Manual",
        ["Messages"]                = "Mensajes",
        ["Model"]                   = "Modelo",
        ["Name"]                    = "Nombre",
        ["New"]                     = "Nuevo",
        ["No"]                      = "No",
        ["OK"]                      = "OK",
        ["Online"]                  = "En línea",
        ["Open"]                    = "Abrir",
        ["Page"]                    = "Página",
        ["Password"]                = "Contraseña",
        ["Pause"]                   = "Pausar",
        ["Port"]                    = "Puerto",
        ["Primary"]                 = "Principal",
        ["Provider"]                = "Proveedor",
        ["Refresh"]                 = "Actualizar",
        ["Remove"]                  = "Eliminar",
        ["Reset"]                   = "Restablecer",
        ["Response"]                = "Respuesta",
        ["Resume"]                  = "Reanudar",
        ["Save"]                    = "Guardar",
        ["Search"]                  = "Buscar",
        ["Send"]                    = "Enviar",
        ["Show"]                    = "Mostrar",
        ["Status"]                  = "Estado",
        ["Stop"]                    = "Detener",
        ["Summary"]                 = "Resumen",
        ["Test"]                    = "Prueba",
        ["Text"]                    = "Texto",
        ["Time"]                    = "Hora",
        ["Timeout"]                 = "Tiempo de espera",
        ["Total"]                   = "Total",
        ["Type"]                    = "Tipo",
        ["Unknown"]                 = "Desconocido",
        ["Update"]                  = "Actualizar",
        ["Upload"]                  = "Subir",
        ["Version"]                 = "Versión",
        ["Yes"]                     = "Sí",

        // Navigation / Pages
        ["About"]                   = "Acerca de",
        ["Chat"]                    = "Chat",
        ["Console"]                 = "Consola",
        ["Map"]                     = "Mapa",
        ["Monitor"]                 = "Monitor",
        ["Settings"]                = "Ajustes",

        // Console page
        ["Connect"]                 = "Conectar",
        ["Connected"]               = "Conectado",
        ["Disconnect"]              = "Desconectar",
        ["Disconnected"]            = "Desconectado",
        ["Not connected"]           = "No conectado",
        ["Enter command …"]         = "Introducir comando…",
        ["LoRa"]                    = "LoRa",
        ["Reboot"]                  = "Reiniciar",
        ["Yes, reboot"]             = "Sí, reiniciar",
        ["OTA"]                     = "OTA",
        ["Serial Console"]          = "Consola serie",
        ["TLS Console"]             = "Consola TLS",
        ["Select node"]             = "Seleccionar nodo",
        ["Trust & save"]            = "Confiar y guardar",
        ["Unknown Certificate"]     = "Certificado desconocido",
        ["Save fingerprint in node settings to permanently trust this node."]
                                    = "Guarda la huella en los ajustes del nodo para confiar permanentemente.",
        ["Reboot node?"]            = "¿Reiniciar el nodo?",
        ["The node will be rebooted. The TLS connection will be disconnected afterwards."]
                                    = "El nodo se reiniciará. La conexión TLS se desconectará después.",
        ["Starting OTA Update"]     = "Iniciando actualización OTA",
        ["The node is starting the OTA web server. Page opens in"]
                                    = "El nodo está iniciando el servidor web OTA. La página se abre en",
        ["seconds"]                 = "segundos",
        ["Pause output (new lines still buffered)"]
                                    = "Pausar salida (nuevas líneas aún en búfer)",
        ["Resume output (scroll re-enabled)"]
                                    = "Reanudar salida (desplazamiento reactivado)",
        ["LoRa debug highlighting on/off"]
                                    = "Resaltado debug LoRa on/off",
        ["Start OTA update – sends --ota-update to node and opens OTA web server"]
                                    = "Iniciar actualización OTA – envía --ota-update al nodo",
        ["Open the node's default web interface"]
                                    = "Abrir la interfaz web predeterminada del nodo",
        ["Reboot node – sends --reboot to node"]
                                    = "Reiniciar nodo – envía --reboot al nodo",
        ["Last seen"]               = "Visto por última vez",
        ["No packet yet"]           = "Ningún paquete aún",

        // Chat
        ["Broadcast"]               = "Difusión",
        ["Direct"]                  = "Directo",
        ["Direct message"]          = "Mensaje directo",
        ["Group"]                   = "Grupo",
        ["Group message"]           = "Mensaje de grupo",
        ["Message"]                 = "Mensaje",
        ["Send message"]            = "Enviar mensaje",
        ["All direct QSOs"]         = "Todos los QSO directos",
        ["Search all direct QSOs"]  = "Buscar todos los QSO directos",
        ["No messages"]             = "Sin mensajes",
        ["Load more"]               = "Cargar más",
        ["New direct message from"] = "Nuevo mensaje directo de",
        ["Copy to clipboard"]       = "Copiar al portapapeles",
        ["Global search"]           = "Búsqueda global",
        ["QSO"]                     = "QSO",
        ["QSO Summary"]             = "Resumen QSO",
        ["AI Summary"]              = "Resumen IA",
        ["AI Search"]               = "Búsqueda IA",
        ["Generate"]                = "Generar",
        ["Regenerate"]              = "Regenerar",
        ["Search in history…"]      = "Buscar en historial…",
        ["Filter by date"]          = "Filtrar por fecha",
        ["Filter by text"]          = "Filtrar por texto",
        ["Date from"]               = "Fecha desde",
        ["Date to"]                 = "Fecha hasta",
        ["Previous page"]           = "Página anterior",
        ["Next page"]               = "Página siguiente",

        // Settings – General
        ["UI Language"]             = "Idioma de la interfaz",
        ["Callsign"]                = "Indicativo",
        ["My callsign"]             = "Mi indicativo",
        ["Own callsign"]            = "Mi indicativo",
        ["Callsign (with SSID, e.g. OE1ABC-1)"]
                                    = "Indicativo (con SSID, ej. OE1ABC-1)",
        ["My locator"]              = "Mi localizador",
        ["Please select"]           = "Por favor selecciona",
        ["Enter manually …"]        = "Introducir manualmente…",
        ["Settings saved"]          = "Ajustes guardados",
        ["Save settings"]           = "Guardar ajustes",
        ["Backup & Restore"]        = "Copia de seguridad y restauración",
        ["Backup settings"]         = "Copia de seguridad",
        ["Restore settings"]        = "Restaurar ajustes",
        ["Download backup"]         = "Descargar copia",
        ["Import settings"]         = "Importar ajustes",
        ["Expand all"]              = "Expandir todo",
        ["Collapse all"]            = "Colapsar todo",
        ["Display name"]            = "Nombre mostrado",

        // Settings – Node
        ["Node"]                    = "Nodo",
        ["Add node"]                = "Añadir nodo",
        ["Delete node"]             = "Eliminar nodo",
        ["Remove node"]             = "Eliminar nodo",
        ["Node name"]               = "Nombre nodo",
        ["Primary node"]            = "Nodo principal",
        ["Add node profile"]        = "Añadir perfil nodo",
        ["All nodes"]               = "Todos los nodos",
        ["Device IP"]               = "IP dispositivo",
        ["Device IP address"]       = "Dirección IP dispositivo",
        ["Device Port"]             = "Puerto dispositivo",
        ["Device port (UDP)"]       = "Puerto dispositivo (UDP)",
        ["Listen IP"]               = "IP escucha",
        ["Listen IP (0.0.0.0 = all interfaces)"]
                                    = "IP escucha (0.0.0.0 = todas las interfaces)",
        ["Listen Port"]             = "Puerto escucha",
        ["Listen port (UDP)"]       = "Puerto escucha (UDP)",
        ["Local UDP port"]          = "Puerto UDP local",
        ["Node port (UDP)"]         = "Puerto nodo (UDP)",
        ["Node settings"]           = "Ajustes nodo",
        ["Certificate fingerprint (SHA-256)"]
                                    = "Huella certificado (SHA-256)",
        ["TLS certificate fingerprint"]
                                    = "Huella certificado TLS",
        ["TLS password"]            = "Contraseña TLS",
        ["TLS enabled"]             = "TLS habilitado",
        ["TLS port"]                = "Puerto TLS",

        // Settings – Console
        ["Console mode"]            = "Modo consola",
        ["Enable serial console"]   = "Habilitar consola serie",
        ["Enable TLS console"]      = "Habilitar consola TLS",
        ["COM Port"]                = "Puerto COM",
        ["COM port (e.g. COM3 or /dev/ttyUSB0)"]
                                    = "Puerto COM (ej. COM3 o /dev/ttyUSB0)",
        ["Baud rate"]               = "Velocidad baudios",
        ["Baud rate (e.g. 115200)"] = "Velocidad baudios (ej. 115200)",
        ["Host / IP"]               = "Host / IP",
        ["Connect to node"]         = "Conectar al nodo",
        ["Fingerprint"]             = "Huella",
        ["Fingerprint saved"]       = "Huella guardada",

        // Settings – Beacon
        ["Beacon"]                  = "Baliza",
        ["Beacon enabled"]          = "Baliza habilitada",
        ["Beacon group"]            = "Grupo baliza",
        ["Beacon group (e.g. #OE)"] = "Grupo baliza (ej. #OE)",
        ["Beacon interval"]         = "Intervalo baliza",
        ["Beacon interval (hours)"] = "Intervalo baliza (horas)",
        ["Interval (hours, min. 1)"]= "Intervalo (horas, mín. 1)",
        ["Beacon text"]             = "Texto baliza",
        ["Test Beacon"]             = "Probar baliza",
        ["Send Beacon Now"]         = "Enviar baliza ahora",
        ["Send now"]                = "Enviar ahora",
        ["Sending…"]                = "Enviando…",

        // Settings – Calendar Beacon
        ["Calendar Beacon"]         = "Baliza de calendario",
        ["Sends beacon announcements for recurring events (e.\u00a0g. club meetings). Each entry can send any number of advance announcements (e. g. 3d, 24h, 2h before the event) and/or a transmission at the event time. All lead times are relative to the event start (“Time” field)."]
                                    = "Envía anuncios de baliza para eventos recurrentes (p. ej. reuniones del club). Cada entrada puede enviar cualquier número de anuncios previos (p. ej. 3d, 24h, 2h antes del evento) y/o una transmisión a la hora del evento. Todos los plazos se refieren al inicio del evento (campo «Hora»).",
        ["Recurrence"]              = "Recurrencia",
        ["Once"]                    = "Una vez",
        ["Weekly"]                  = "Semanal",
        ["Bi-weekly"]               = "Quincenal",
        ["Monthly (Day)"]           = "Mensual (día)",
        ["Nth Weekday"]             = "N.º día laborable",
        ["Last Weekday"]            = "Último día laborable",
        ["Day of Week"]             = "Día de la semana",
        ["Monday"]                  = "Lunes",
        ["Tuesday"]                 = "Martes",
        ["Wednesday"]               = "Miércoles",
        ["Thursday"]                = "Jueves",
        ["Friday"]                  = "Viernes",
        ["Saturday"]                = "Sábado",
        ["Sunday"]                  = "Domingo",
        ["Ordinal (1=first…)"]      = "Ordinal (1=primero…)",
        ["Day of Month"]            = "Día del mes",
        ["Reference Date (anchor)"] = "Fecha de referencia (ancla)",
        ["Announce before"]         = "Anunciar antes",
        ["Comma-separated, e. g. 3d, 24h, 2h (d=days, h=hours, m=minutes). Empty = no advance announcement."]
                                    = "Separado por comas, p. ej. 3d, 24h, 2h (d=días, h=horas, m=minutos). Vacío = sin anuncio previo.",
        ["Invalid value:"]          = "Valor no válido:",
        ["Without a group this entry will not be sent!"]
                                    = "¡Sin grupo, esta entrada no se enviará!",
        ["Send at event time"]      = "Enviar a la hora del evento",
        ["Next event"]              = "Próximo evento",
        ["Next transmission"]       = "Próxima transmisión",
        ["none (nothing configured or event passed)"]
                                    = "ninguna (nada configurado o evento pasado)",
        ["Add Event"]               = "Añadir evento",
        ["Variables:"]              = "Variables:",
        ["Title"]                   = "Título",
        ["Inactive"]                = "Inactivo",

        // Console Command Helper
        ["Not connected"]           = "No conectado",
        ["Last"]                    = "Último",
        ["Status refreshed"]        = "Estado actualizado",
        ["Use own IP"]              = "Usar mi propia IP",
        ["Really send command?"]    = "¿Realmente enviar el comando?",
        ["Yes, send"]               = "Sí, enviar",

        // Settings – Auto-Reply
        ["Auto-Reply"]              = "Respuesta automática",
        ["Auto-reply enabled"]      = "Respuesta auto habilitada",
        ["Auto-reply enabled (first contact only)"]
                                    = "Respuesta auto habilitada (solo primer contacto)",
        ["Auto-reply text"]         = "Texto respuesta auto",
        ["Test auto-reply"]         = "Probar respuesta auto",

        // Settings – Bot
        ["Bot"]                     = "Bot",
        ["Bot commands"]            = "Comandos bot",
        ["Bot enabled"]             = "Bot habilitado",
        ["Bot command name (without --)"]
                                    = "Nombre comando bot (sin --)",
        ["Bot response text"]       = "Texto respuesta bot",
        ["Test bot command"]        = "Probar comando bot",
        ["Export bot commands"]     = "Exportar comandos bot",
        ["Import bot commands"]     = "Importar comandos bot",
        ["User-defined commands"]   = "Comandos personalizados",
        ["Add command"]             = "Añadir comando",

        // Settings – Watchlist
        ["Watchlist"]               = "Lista de seguimiento",
        ["Watchlist enabled"]       = "Lista de seguimiento habilitada",
        ["Add to watchlist"]        = "Añadir a la lista",
        ["CQ detection"]            = "Detección CQ",
        ["CQ detection enabled"]    = "Detección CQ habilitada",
        ["Auto-dismiss after"]      = "Cerrar automáticamente después",

        // Settings – Map
        ["Live Map"]                = "Mapa en vivo",
        ["Map settings"]            = "Ajustes mapa",
        ["Show gateway"]            = "Mostrar pasarela",
        ["Show own position"]       = "Mostrar posición propia",
        ["MH list"]                 = "Lista MH",
        ["MH max. age (hours)"]     = "Edad máx. MH (horas)",
        ["Coverage"]                = "Cobertura",
        ["Gateway"]                 = "Pasarela",
        ["Signal strength"]         = "Potencia de señal",
        ["SNR"]                     = "SNR",

        // Settings – Station / HF
        ["Station / HF parameters"] = "Parámetros estación / HF",
        ["Station parameters"]      = "Parámetros estación",
        ["TX power (dBm)"]          = "Potencia TX (dBm)",
        ["Cable type"]              = "Tipo cable",
        ["Cable length"]            = "Longitud cable",
        ["Cable length (m)"]        = "Longitud cable (m)",
        ["Cable attenuation (dB/10m)"]
                                    = "Atenuación cable (dB/10m)",
        ["Antenna"]                 = "Antena",
        ["Antenna gain"]            = "Ganancia antena",
        ["Antenna height"]          = "Altura antena",
        ["Antenna height (m)"]      = "Altura antena (m)",
        ["Antenna type"]            = "Tipo antena",
        ["Frequency"]               = "Frecuencia",
        ["Frequency (MHz)"]         = "Frecuencia (MHz)",
        ["System margin (dB)"]      = "Margen sistema (dB)",
        ["EIRP (dBm)"]              = "PIRE (dBm)",
        ["Free-space range (km)"]   = "Alcance espacio libre (km)",

        // Settings – Telemetry
        ["Telemetry"]               = "Telemetría",
        ["Telemetry enabled"]       = "Telemetría habilitada",
        ["Telemetry interval (minutes)"]
                                    = "Intervalo telemetría (minutos)",
        ["Temperature"]             = "Temperatura",
        ["Send native telemetry (extudp)"] = "Enviar telemetría nativa (extudp)",
        ["Sends the values marked with a role below (up to 7: temp, humidity, pressure, temp 2, QNH, gas resistance, CO2) as a native telemetry telegram directly to the node as soon as at least one value changes (not time-scheduled). The node writes them into its sensor variables and immediately triggers a new position beacon whose comment includes the values just like real onboard sensors would. It ignores the telegram while it has real sensor hardware (BME280/BMP3xx/AHT20/SHT21) installed."]
                                    = "Envía los valores marcados abajo con un rol (hasta 7: temp., humedad, presión, temp. 2, QNH, resistencia de gas, CO2) como telegrama de telemetría nativo directamente al nodo en cuanto cambia al menos un valor (no programado por tiempo). El nodo los escribe en sus propias variables de sensor y activa de inmediato un nuevo baliza de posición cuyo comentario incluye los valores igual que sensores reales a bordo. Ignora el telegrama mientras tenga instalado hardware de sensor real (BME280/BMP3xx/AHT20/SHT21).",
        ["Minimum interval (minutes)"]
                                    = "Intervalo mínimo (minutos)",
        ["The node applies no throttling of its own – this minimum interval between two extudp sends protects against flooding the mesh when a value jitters."]
                                    = "El nodo no aplica ninguna limitación propia: este intervalo mínimo entre dos envíos extudp protege contra la saturación de la mesh cuando un valor fluctúa.",
        ["Role"]                    = "Rol",
        ["Role for the map popup and the native extudp telegram"]
                                    = "Rol para el popup del mapa y el telegrama extudp nativo",
        ["Role: drives the map popup (temp/humidity/pressure) and/or the target field in the native extudp telegram"]
                                    = "Rol: determina el popup del mapa (temp./humedad/presión) y/o el campo de destino en el telegrama extudp nativo",
        ["Temp. 2"]                 = "Temp. 2",
        ["Gas resistance"]          = "Resistencia de gas",
        ["Extudp is active, but no value has an assigned role."]
                                    = "Extudp está activo, pero ningún valor tiene un rol asignado.",
        ["Preview native telemetry telegram (extudp)"]
                                    = "Vista previa del telegrama de telemetría nativo (extudp)",
        ["None of the assigned extudp values could be found in the file."]
                                    = "No se encontró en el archivo ninguno de los valores extudp asignados.",

        // Settings – Database
        ["Database"]                = "Base de datos",
        ["Database provider"]       = "Proveedor base de datos",
        ["MySQL connection string"]  = "Cadena conexión MySQL",
        ["InfluxDB URL"]            = "URL InfluxDB",
        ["InfluxDB bucket"]         = "Bucket InfluxDB",
        ["InfluxDB token"]          = "Token InfluxDB",

        // Settings – AI
        ["AI"]                      = "IA",
        ["AI API Key"]              = "Clave API IA",
        ["AI Model"]                = "Modelo IA",
        ["AI Provider"]             = "Proveedor IA",
        ["Max. messages for AI summary"]
                                    = "Máx. mensajes para resumen IA",
        ["Token usage"]             = "Uso de tokens",
        ["Generate summary"]        = "Generar resumen",
        ["Message history"]         = "Historial mensajes",
        ["History (paginated)"]     = "Historial (paginado)",

        // Settings – MQTT
        ["MQTT"]                    = "MQTT",
        ["MQTT broker"]             = "Broker MQTT",
        ["MQTT enabled"]            = "MQTT habilitado",
        ["MQTT password"]           = "Contraseña MQTT",
        ["MQTT port"]               = "Puerto MQTT",
        ["MQTT prefix"]             = "Prefijo MQTT",
        ["MQTT username"]           = "Nombre usuario MQTT",

        // Settings – QRZ
        ["QRZ"]                     = "QRZ",
        ["QRZ password"]            = "Contraseña QRZ",
        ["QRZ username"]            = "Nombre usuario QRZ",
        ["Test Connection"]         = "Probar conexión",
        ["Testing…"]                = "Probando…",
        ["Clear Cache"]             = "Limpiar caché",
        ["Max. age (days, 0 = unlimited)"]
                                    = "Edad máx. (días, 0 = ilimitado)",
        ["0 = unlimited (never refresh)"]
                                    = "0 = ilimitado (nunca actualizar)",

        // Settings – Webhook
        ["Webhook"]                 = "Webhook",
        ["Webhook enabled"]         = "Webhook habilitado",
        ["Webhook URL"]             = "URL webhook",
        ["Server URL"]              = "URL servidor",

        // Settings – Quick texts / UI
        ["Quick texts"]             = "Textos rápidos",
        ["Own messages align left"] = "Mensajes propios alineados a la izquierda",
        ["Monitor max. messages"]   = "Máx. mensajes monitor",
        ["Sound"]                   = "Sonido",
        ["Sound enabled"]           = "Sonido habilitado",
        ["Voice"]                   = "Voz",
        ["Voice announcements"]     = "Anuncios de voz",
        ["Voice enabled"]           = "Voz habilitada",

        // Group labels
        ["Group filter"]            = "Filtro grupo",
        ["Group filter enabled"]    = "Filtro grupo habilitado",
        ["Group labels"]            = "Etiquetas grupo",
        ["Add group label"]         = "Añadir etiqueta grupo",

        // Misc
        ["Firmware"]                = "Firmware",
        ["OTA update started"]      = "Actualización OTA iniciada",
        ["Connection refused"]      = "Conexión rechazada",
        ["More info"]               = "Más información",

        // Appearance / Themes
        ["Appearance"] = "Apariencia",
        ["Your selection is applied immediately as a preview – it becomes permanent only after “Save”."] = "La selección se aplica inmediatamente como vista previa – solo se vuelve permanente tras “Guardar”.",
        ["Create custom theme"] = "Crear tema personalizado",
        ["Import theme"] = "Importar tema",
        ["Import theme file (.mctheme.json)"] = "Importar archivo de tema (.mctheme.json)",
        ["Custom themes can be exported as a file and shared with other users."] = "Los temas personalizados se pueden exportar como archivo y compartir con otros usuarios.",
        ["Theme name"] = "Nombre del tema",
        ["My Theme"] = "Mi tema",
        ["Please enter a theme name."] = "Introduce un nombre para el tema.",
        ["A theme with this name already exists."] = "Ya existe un tema con este nombre.",
        ["Import failed: not a valid theme file."] = "Importación fallida: archivo de tema no válido.",
        ["Import failed: no valid colour values found."] = "Importación fallida: no se encontraron valores de color válidos.",
        ["MeshCom Dark (default)"] = "MeshCom Dark (predeterminado)",
        ["Midnight (OLED)"] = "Medianoche (OLED)",
        ["Light"] = "Claro",
        ["High contrast"] = "Contraste alto",
        ["Backgrounds"] = "Fondos",
        ["Accents & structure"] = "Acentos y estructura",
        ["Buttons"] = "Botones",
        ["Links & callsigns"] = "Enlaces e indicativos",
        ["Status colours"] = "Colores de estado",
        ["Messages & notices"] = "Mensajes y avisos",
        ["Monitor & lists"] = "Monitor y listas",

        // UDP diagnostics
        ["Show UDP diagnostics"] = "Mostrar diagnóstico UDP",
        ["UDP: No socket – port not open. Click ❓ for diagnostics."] = "UDP: sin socket – puerto no abierto. Haz clic en ❓ para el diagnóstico.",
        ["UDP: Waiting for signal – usually the node. Click ❓ for diagnostics."] = "UDP: esperando señal – normalmente es el node. Haz clic en ❓ para el diagnóstico.",
        ["UDP receive diagnostics"] = "Diagnóstico de recepción UDP",
        ["No reception is almost always caused by the node, not WebDesk:"] = "La falta de recepción casi siempre se debe al node, no a WebDesk:",
        ["On the node: ext UDP must be set to ON"] = "En el node: ext UDP debe estar en ON",
        ["On the node: the ext UDP target IP must point exactly to the IP below, port 1799"] = "En el node: la IP de destino de ext UDP debe apuntar exactamente a la IP indicada abajo, puerto 1799",
        ["Firewall on this PC: allow inbound UDP on port 1799"] = "Firewall de este PC: permitir UDP entrante en el puerto 1799",
        ["The node itself may not be receiving anything from the mesh"] = "Es posible que el propio node no esté recibiendo nada de la mesh",
        ["Own IP (for ext UDP on the node):"] = "IP propia (para ext UDP en el node):",
        ["Open detailed guide"] = "Abrir guía detallada",
    };
}
