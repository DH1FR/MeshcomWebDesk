# Changelog

## [1.11.0] – in development (dev)

### Features
- **Ping confirmation dialog**: A browser confirmation is shown when sending `ping` or `--ping` to a group or broadcast tab, preventing accidental transmissions.
- **Combined ACK display**: The ACK indicator now shows LoRa and Gateway delivery combined (`✓✓ ☁️✓`) – users can see whether their message arrived via LoRa, the gateway, or both.
- **NET Console** (renamed from HMAC Console): New HMAC-TCP console mode added; renamed consistently throughout the UI and settings.
- **Per-node console log**: Console output can optionally be saved to a daily rolling log file per node. Old files are automatically deleted after the configured retention period.
- **Configurable bot/auto-reply delay**: Reply delay configurable from 0 to 30 seconds (default: 3 s).
- **Beacon interval restriction removed**: The 8-hour minimum interval has been removed. The interval is freely configurable from 1 hour. A hint text reminds users to choose intervals responsibly.
- **Server-side tab order persistence**: Chat tab order is now stored server-side and survives page reloads.
- **Tab message limit** (`TabMaxMessages`): Each chat tab can be limited to a maximum number of messages to prevent unbounded memory growth.
- **French TTS support**: French (`fr-FR`) added to the browser TTS locale map.
- **Full i18n coverage**: All new UI texts are fully translated into DE / EN / IT / ES / FR.

### Bugfixes
- **Fix Gateway Source not saved**: The `GatewayServer` setting (OE / DL source selection) was missing from `SettingsService.SaveMeshcomSettingsAsync` and was therefore reset to the default on every restart.
- **Fix ACK not shown in tab**: ACK packets were incorrectly filtered by the `src_type=node` relay filter; they are now exempt so the checkmark is set correctly.
- **Fix cross-node ACK matching**: The ACK checkmark now searches all node states, so multi-node setups correctly mark outgoing messages as delivered.
- **Fix relay echo deduplication**: Relay echoes from foreign callsigns are skipped to prevent deduplication collisions with genuine LoRa reception.
- **Fix notification sound on node switch**: Sound and TTS notifications are now scoped per node; no tone is played when switching nodes without new messages.
- **Fix bot/auto-reply node routing**: Bot and auto-reply messages always use the own primary hardware node.
- **Fix outgoing message callsign (multi-node)**: Outgoing messages now show the correct `msg.From` per node instead of the global callsign.
- **Fix console connected IP display**: The console header now shows the actually connected IP (`ConnectedHost`).
- **Fix console log path resolution**: `ConsoleLogService` now uses the startup-resolved log path (`ResolvedLogPath`) instead of reading an empty override value.
- **Fix console log settings persistence**: `ConsoleLogEnabled` is correctly saved and loaded for both global and per-node settings.
- **Fix iOS Safari clipboard copy**: Fallback implementation for `NotAllowedError` on iOS Safari.
- **Fix LoRa highlight state lost on tab switch**: LoRa highlight state is now persisted in `localStorage`.
- **Fix translation syntax errors in IT/ES/FR**: A broken dictionary entry (`Accept` key split from its value) caused Docker build failures; fixed in all three files.
- **Fix French TTS locale missing**: French was not in the `speakText` locale map and fell back to German; now correctly uses `fr-FR`.

---

## [1.10.1] – released

### Features
- **AI Search: Default date range**: When opening the global AI search the date range is now pre-filled to the last 90 days up to the end of the current day (23:59:59).
- **AI Search: Echo deduplication**: Group messages are deduplicated before prompt construction (LoRa echo creates duplicate DB rows). Reduces token usage and improves answer quality.
- **AI Search: Timestamp fix**: Timestamps read from MySQL are now correctly treated as local time (`DateTimeKind.Local`) – no more UTC shift (+2 h) in AI answers, QSO history and text search.
- **AI Search: Automatic model selection**: New model option `auto` (*Automatic (recommended)*) picks between `gpt-4o-mini`, `gpt-4.1-mini` and `gpt-4.1` depending on prompt size.
- **AI Search: Expanded message scope**: The global search now includes all direct messages, own group messages and group @-mentions. Raw JSON status packets and ACKs are filtered out.

### Bugfixes
- **Fix Telemetry mapping limit**: The entry limit of 3 was incorrectly applied to the telemetry measurement mapping instead of the send-time schedule. Mapping entries are now unlimited (send times: max. 6). Entries beyond 3 were also silently dropped on save – this is now fixed. The telemetry preview now shows all keys from the JSON file, not only configured ones.
- **Fix Group Labels growing**: The group labels list was duplicated on every app start due to ASP.NET Core configuration binding appending defaults instead of replacing them. The list now starts empty; new buttons are available in settings: 🔄 Restore defaults, ➕ Add missing, 🗑️ Clear list.
- **Further improvements and bug fixes.**

---

## [1.10.0] – released

### Features
- **Multi-Node Support**: Multiple MeshCom nodes can be configured and used simultaneously. Each node has its own IP, port, callsign and TLS certificate. A node switcher bar in the chat page allows switching the active node at any time. Chat tabs, monitor, outgoing messages, auto-reply and bot replies are all node-aware. Node online status is shown in the switcher buttons.
- **Multi-language UI (French, Italian, Spanish)**:
- **TLS Console / Serial Console**: The console page now supports either a TLS connection (previous behaviour) or direct USB serial access (115200 baud). Switch mode in **Settings → 🖥️ Console → Serial Console**. TLS Console requires **MeshCom firmware v4.35p.05.13 or later**.
- **OTA update via console**: The command `--ota-update` starts OTA mode with a 5-second countdown dialog; the connection is automatically closed after the OTA page opens.
- **Reboot via console**: The command `--reboot` shows a confirmation dialog; after sending, the connection is closed after a short delay.
- **Beacon interval minimum**: The beacon interval can no longer be set below 8 hours; existing values < 8 h are automatically corrected to 8 h on load.
- **Telemetry mapping limit**: A maximum of 3 telemetry mapping entries are allowed; the add button is disabled when the limit is reached.
- **Docker – Serial interface**: Documentation added for passing through USB/TTY devices in `docker-compose.yml` (see README → *Serial Console (USB) in Docker*).

### Bugfixes
- **Speaker icon**: The speaker icon in the status bar now persists across chat page reloads (state is stored like the bell icon).

---

## [1.9.5] – released

### Features
- **MsgId in monitor**: Incoming messages now show the message ID (`msg_id`) in the monitor.
- **Toast label**: Toast notifications for watchlist and CQ detection now use "received" instead of "heard".
- **Own messages left-aligned**: New option `OwnMessagesAlignLeft` – own sent messages can optionally be left-aligned (instead of right-aligned); configurable in settings.
- **Watchlist TTS**: Voice announcement for watchlist matches is triggered at most once every 5 minutes per callsign (previously: on every packet).

### Bugfixes
- **{last-qso} showed current instead of previous QSO**: `GetLastQsoTimeDbOnlyAsync`, `GetLastQsoTimeAsync` and `GetLastQsoTimeInternalAsync` accept an optional `before` parameter (`AND timestamp < @before`). `ExpandVariablesAsync` passes `DateTime.Now` (auto-reply) or `message.Timestamp` (bot). No previous QSO → output `"no QSO"` instead of empty.
- **Bot: `---===` incorrectly recognised as command**: `BotCommandService.IsCommand()` now checks whether a letter follows `--` or an em-dash (`char.IsLetter`). Decoration strings like `---===` (WebDesk ident auto-reply of other stations) are no longer treated as bot commands.
- **Direct tab without voice not visible**: `HandleNewDirectTab` in `Chat.razor` only called `StateHasChanged` when voice was active – the newly created tab did not appear in the UI. Fix: `StateHasChanged` is now always called; TTS announcement only when voice is active.
- **QRZ race condition (up to 9 parallel requests)**: `_inflightLookups` (`ConcurrentDictionary<string, Task<QrzInfo?>>`) deduplicates in-flight requests – all parallel `LookupAsync` calls for the same callsign now share a single `Task`.

---

## [1.9.4] – released

### Features
- **Variable {last-qso}**: New template variable with date and time of the last direct QSO with the current callsign (DB-first + in-memory fallback). Available in auto-reply, bot, beacon and quick texts.
- **Copy message to clipboard**: In the direct message tab a 📋 button appears on hover to copy the message text to the clipboard (JS Clipboard API).
- **Station / RF parameters** (new settings section):
  - TX power (dBm), cable type, cable length, antenna gain (dBi), **antenna type** (free text, e.g. dipole, 3-el. Yagi), antenna height, frequency and system margin (dB)
  - **15 coax cable types** (all 50 Ω) in three attenuation groups: LMR-600, LMR-400, H-2000 Flex, Ecoflex 15, CFD-400, Ecoflex 10, Aircell 7, LMR-240, H-155, LMR-200, RG-213, RG-8/U, RG-8X, RG-58, RG-174
  - **Manual attenuation input**: Selecting "Enter manually …" shows an additional field for attenuation in dB/10 m (any cable)
  - Live display of **EIRP** (P_TX − cable attenuation + antenna gain) and **theoretical free-space range** (system margin configurable, default 30 dB)
  - All parameters are saved in the backup and are backwards-compatible
- **FSPL range circle** on the map: when the coverage layer is activated an additional yellow circle with the theoretical free-space range is shown (EIRP + frequency + system margin); legend distinguishes "Measured (Convex Hull)" and "FSPL range (theor.)"
- **Station variables** – new template variables for station/RF data, available everywhere (auto-reply, bot, beacon, quick texts, send bar):
  - `{my-tx-power}` – TX power (e.g. 22 dBm)
  - `{my-eirp}` – calculated EIRP (e.g. 23.50 dBm)
  - `{my-antenna}` – antenna gain (e.g. 2.5 dBi)
  - `{my-antenna-type}` – antenna type (e.g. dipole)
  - `{my-antenna-height}` – antenna height (e.g. 10 m)
  - `{my-freq}` – operating frequency (e.g. 433.175 MHz)
- **CQ detection**: Incoming group messages are automatically checked for CQ calls (case-insensitive regex, no false positives for words like "FREQUENCY"):
  - Own callsign is suppressed; only groups from the active group filter are evaluated
  - **Toast notification** (yellow, similar to watchlist toast) with callsign, group and age; disappears automatically after 60 seconds
  - **CQ beep**: Morse pattern **CQ CQ** in CW style (700 Hz, 80 ms unit, correct dit/dah/word-space timing); only when audio is active
  - **TTS announcement**: e.g. "C Q C Q from Delta Hotel One Foxtrot Romeo in group 2 6 2" – only when voice announcements are active; follows the app language (DE/EN)
- **Toast stacking**: Watchlist and CQ toasts are stacked vertically in a shared container so both are visible simultaneously (previously overlapping)
- **OpenAI usage link**: Additional link to `https://platform.openai.com/usage` added in the AI section.

### Bugfixes
- **OpenAI balance link**: Migrated from deprecated dashboard endpoints to `/v1/organization/costs`.
- **CQ beep timing**: Correct Morse timing (inter-element 1u, inter-character 3u, word space 7u) without double gaps.
- **FSPL circle**: Contrast increased; circle is now also redrawn in `UpdateMarkersAsync` (previously only in `ToggleCoverageAsync`).
- **Station/RF fields in backup**: `TxPowerDbm`, `CableType`, `CableLengthM`, `AntennaGainDbi`, `AntennaHeightM`, `FrequencyMhz` were missing from the backup serialisation.

---

## [1.9.3] – released

### Features
- **Variable {telemetry}**: New template variable for the current telemetry string of the own station; available in auto-reply, bot, beacon and quick texts.
- **Maidenhead locator lowercase fix**: QTH locator is now output in normalised form (e.g. `JN48qn` instead of `JN48QN`).
- **@-Mention**: An `@` button appears next to every callsign in the monitor and group/broadcast messages; clicking inserts `@CALLSIGN` into the text input field.
- **Global search button**: New 🔎 button in the tab bar opens an AI search across all direct QSOs simultaneously (cross-channel).
- **Status bar 3-level adaptive**: Three layout levels (iPhone / iPad / Desktop) with `ResizeObserver` + `MutationObserver`; `getBoundingClientRect` for reliable overflow detection including Safari/iOS.
- **Node firmware/HW persistent**: `NodeHwId` and `NodeFirmware` are stored across restarts and displayed in the status bar.
- **Variables `{node-firmware}` / `{node-hw}`**: New template variables for the firmware version and hardware type of the connected MeshCom node; available in bot, quick texts and send bar.
- **Sequence number in monitor**: Outgoing messages show the assigned sequence number as soon as the node confirms it.

### Bugfixes
- **Status bar overflow loop**: `ResizeObserver` callback updates with a 120 ms delay (`setTimeout`) – prevents layout feedback loops on iOS/iPad.
- **iOS/Safari AudioContext**: Unlock on `touchstart`; TTS is skipped on iOS and only the AudioContext beep is used.
- **iOS/Safari TTS**: Speech synthesis is released on first touch; race condition between `cancel()` and `speak()` fixed.
- **ACK fallback time window**: ACK fallback for snapshot messages limited to a maximum of 10 minutes – prevents old messages from consuming new ACKs.
- **Range cloud (multiple fixes)**:
  - Cloud only shows directly heard stations (HopCount == 0) up to max. 500 km; if fewer than 3 direct connections, falls back to all GPS stations.
  - Legend rectangle was not displayed (missing `display:inline-block`).
- **`hw_id` parsing**: More robust evaluation (number + string); `NodeHwId` is set independently of `NodeFirmware`.

---

## [1.9.2] – released

### Performance
- **Settings**: Section contents are only rendered when the section is open (`@if (IsOpen(...))` guards for all 18 sections). On first load only section headers are rendered instead of all ~149 bindings → noticeably faster loading.
- **MH list & map**: New `OnMhChange` event in `ChatService` – MH list and map now only react to MH-relevant updates instead of every incoming packet.
- **Map**: Marker redraw is debounced by 400 ms to avoid multiple redraws on rapid incoming packets.
- **MH list**: QRZ.com lookups are only triggered for callsigns not yet in the cache.

### Features
- **MH list max. age**: Input changed from days to **hours** (`MhMaxAgeHours`). Existing values are automatically migrated (days × 24).
- **MH cleanup on save**: After saving settings, outdated MH entries are deleted immediately (not at the next interval).
- **MH cleanup on startup**: On application startup, outdated MH entries from the saved snapshot are cleaned up immediately.
- **Responsive status bar**: The status bar adapts dynamically to the available width. Elements are progressively hidden when space is insufficient (`ResizeObserver` + `MutationObserver`).

### Bugfixes
- **MH max. age**: The value `MhMaxAgeHours` was reset to 0 after a page change – bug in `SettingsService` fixed; the value is now correctly saved to `appsettings.override.json`.
- **Map – range cloud**: The range cloud (convex hull) was not recalculated after an MH update or MH purge and remained empty. `UpdateMarkersAsync()` now automatically re-invokes `setCoverage` when the display is active.

---

## [1.9.1] – released

### Features
- TTS announcement "New message from" at most once per callsign within 5 minutes.
- Voice announcement "New message from" takes priority over watchlist TTS.
- Welcome dialog revised.
- Beta label removed from database and MQTT.

---

## [1.9.0] – released

### Features
- First release of this major version.