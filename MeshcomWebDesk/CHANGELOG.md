# Changelog

## [1.19.0] – unreleased

### Features
- **User manual in the app (❓ Help)** – WebDesk now has a manual of its own, in **German and English**. **❓ Help** opens it inside the application (page `/help`: chapter list on the left, text on the right) instead of a page on GitHub. It works **without an internet connection** and always matches the installed version, because the Markdown files are built into the program. It consists of a welcome page and eleven chapters: *Quick start and installation*, *Connecting the node*, *The interface*, *Writing messages*, *Notifications*, *Automation*, *Map and range*, *Integrations*, *Security and access*, *Backup, maintenance and troubleshooting* and a *Reference* (ports, parameters, bot commands, settings at a glance). The language follows the interface language; the other languages show the English text with a note. The same files are on GitHub in `docs/manual`.
- **Trend sparklines (last 24 h)** – the station card in the **MH list** and the hover popup of a callsign in the chat now show small trend lines for the last 24 hours: temperature 🌡️, humidity 💧, pressure 🧭, battery 🔋, RSSI and SNR, each with the latest value (min/max in the tooltip). They are loaded when you hover, cached for a minute, and rows without enough data are simply left out. The data comes from the configured database; without a database (or if it is unreachable) WebDesk uses a **built-in local history** instead: 15-minute averages per station, kept for 7 days in `telemetry-history.json` in the data folder (so it also survives restarts and updates in Docker).
- **Database: how long to keep the data** – **Settings → Database → Keep data**: *Unlimited* (default, nothing changes for existing installations), 30, 90, 180 or 365 days. **MySQL / MariaDB:** WebDesk deletes older rows of the monitor table once a day (in small batches, never touching other tables). **InfluxDB:** the retention rule of the bucket is set to the chosen period (the token needs write access to buckets); *Unlimited* never changes an existing rule. The minimum is 7 days. **🧹 Check cleanup** shows how many entries would be deleted and asks before deleting anything.

### Improvements
- **Variable picker `{}` in the send bar** – the list is now compact (at most 440 px wide, anchored above the button) instead of spanning the whole width of the window.

---

## [1.18.4] – 2026-11-28

### Features
- **Optional access protection (password)** – WebDesk can now ask for a shared password so that not everybody in the local network can open it. **Settings → System → Access protection**: choose *Only from other devices* (recommended – the PC running WebDesk gets in without a password, phones, tablets and other PCs log in once per device and stay logged in for 90 days), *Always* (this PC too) or *Off* (default, nothing changes for existing installations), enter the password twice and press *Apply* – it takes effect at once and this browser is signed in automatically. Also there: *Sign out all devices* and *Sign out this device*. The password is stored only as a salted PBKDF2 hash in `<DataPath>/access.json` (not part of the settings backup, not touched by *Save*); wrong passwords are throttled per address (5 tries, then 30 s doubling up to 5 min). A small standalone login page (`/login`) is shown in the interface language; everything else – pages, the live connection and the `/api/*` endpoints – needs the login. `POST /api/telemetry` keeps its own `X-Api-Key` and stays reachable for Home Assistant & co. If WebDesk also listens on https (profile `lan-https`, port `5163`), a protected request over plain http from another device is redirected there first so the password does not cross the network in clear text; otherwise the login page warns that the connection is not encrypted. **Forgot the password?** Start WebDesk once with `--reset-password` (switches the protection off; `--reset-password NEWPASSWORD` sets a new one), in Docker `docker exec <container> ./MeshcomWebDesk --reset-password`, or – without a shell – start the container once with `MESHCOM_RESET_ACCESS=1` (remove it afterwards). A running WebDesk picks the change up within seconds. The 📱 QR dialog now only warns about missing protection when none is set.
- **All gateway dashboards are queried by default** – the gateway highlight list and the "last seen on the server" presence now read all built-in MeshCom dashboards (OE, DL, IT, HB, SP) instead of OE only. The dashboards hardly overlap (about 880 gateways in total, OE alone knows only about 480), so with the old default many gateways were not marked and many nodes showed no server presence. New option **Settings → Gateway source → All (OE, DL, IT, HB, SP)**, now the default; a single dashboard or *None* can still be chosen. At the update every installation is switched to *All* once, whatever was selected before; after that the choice is yours (selecting e.g. OE again and saving keeps it).
- **`{}` button in the send bar** – inserts the current value of a variable (e.g. `{callsign}`, `{locator}`, `{last-qso}`) at the cursor. A scrollable table lists the variables with a short description; values that take a moment to determine are loaded lazily.
- **Text emoticons become emoji** – `;-)`, `:D`, `<3` and similar are converted in the message field while typing, once a space follows, and the last word when the message is sent. Switch: **Settings → Chat & Monitor** (on by default). Also works on iOS keyboards.
- **Tabs open at the "New messages" line** – a tab with many unread messages opens at the line where the new messages start. A button *↑ N unread* jumps back to that line. Unread counts survive a restart.
- **Unread badge shows total and direct/mention count** – in the browser tab title, in the favicon dots and on the app icon badge.
- **New app icon** – new mesh-network icon (higher contrast) for the favicon, PWA manifest, iOS touch icon, Windows `app.ico`, the header, the About page and desktop notifications.
- **Tab bar switches to a one-row swipeable layout when too many tabs** would take the chat area, on any screen size (not only on phones).

### Improvements
- **Bot commands work with `--` again** – besides `>` the bot answers direct messages that start with `--` (two hyphens) or `—` (em dash, as typed by many mobile keyboards) and a letter: `--ping` and `>ping` are the same, likewise `--version`, `--help`, `--time`, … Reason: some devices, e.g. the LilyGo T-Deck, have no `>` key. `---===` style decoration and a bare `ping` are still ordinary chat text. The ping round-trip time works for both forms.
- **Header title fits small and mid-size screens** – up to 900 px the license hint is dropped and the title never wraps (version small underneath); below 700 px only "WebDesk" is shown. In the compact mode (phone held sideways) the name stays visible from 600 px width.
- **Dialogs and pages scroll on small displays** – the About, Update and Clear pages and the QR dialog are reachable on low screens.

### Bugfixes
- **`>rx` or `--rx` over KISS no longer answers "signal unknown" on the first try** – with a KISS/TCP node the signal values (RSSI/SNR) arrive in a separate frame just after the message, but the bot had already built its reply by then. It now waits up to 1 s for them before answering `rx`.
- **extudp telemetry stays enabled across restarts** – a missing node echo within 20 s used to switch the setting off and save it, so it had to be switched on again after every restart. Sending is now only suspended at runtime; the setting is kept and the next start tries again.
- **KISS relay hops with a clamped SSID** – hops with SSID −15 showed as `-?`; they are now repaired from the full ext-udp path.
- **Unread favicon dots show again** – the static icon links are lifted out while the badge is active.

---

## [1.18.3] – released

### Features
- **Alert when somebody mentions you in a group** – a group (or broadcast) message from somebody else that contains `@<your callsign>` (e.g. `@DH1FR-4`) is recognised when it arrives. The row gets a strong tint with a coloured edge, a pill *@ for you* and a short glow, the callsign in the text is highlighted, and the tab gets an *@N* badge next to the unread counter. On top of that you get a toast *You were mentioned* (a click opens the tab), its own two-pair tone (follows the 🔔 button) and a voice announcement in the five languages, at most once a minute per tab (follows the 🔊 button). No alert while you are looking at exactly that tab. Matching is case-insensitive and for the whole callsign: `@DH1FR-4` counts for your DH1FR-4, `@DH1FR` without SSID for every callsign with that base; other SSIDs, `/P`, e-mail addresses and foreign callsigns do not. The callsigns of all enabled nodes count; ACKs, positions, telemetry and your own messages never do. Switch for the alert: **Settings → Chat & display → Watchlist → Mention** (default on); the row mark and the tab badge stay on either way.
- **CQ calls to everybody are detected too** – until now only a CQ in a *group* raised the CQ alert; a CQ sent to everybody (it lands in the *All* tab) was ignored. It now raises the same alert: the yellow toast *CQ All*, the CQ tone, the voice announcement (*… to everybody*) and, if enabled, the desktop notification. Acknowledgements, positions, telemetry, your own messages and messages that start with `@callsign` are never a CQ. Switch: **Settings → Chat & display → Watchlist → CQ calls to everybody** (default on).
- **Your callsign is always highlighted in message text** – `@DH1FR-4` / `@DH1FR` of your own callsigns is marked in every incoming message, in old history and after a restart too, with a clearly visible background (50 % of the theme's warning colour, readable on the dark panel and on an iPhone in daylight). Nothing is stored; the row mark, the pill, the tab badge and the alert still exist only for mentions that arrive as new messages.
- **Unread count in the browser tab** – with the sound off you still see that something happened: the tab title becomes `(3) MeshCom WebDesk` and the favicon gets a red dot while messages are unread. It uses the same criterion as the unread alert: a tab only counts as read while somebody is *really looking* at it (browser tab visible, window focused, recent mouse/keyboard activity), so with WebDesk in the background the number keeps growing and drops to 0 as soon as you look. Direct, group and *All* tabs of all nodes are counted. On an installed web app (PWA) the number is also shown on the app icon (taskbar on Windows, Dock on macOS; Chrome/Edge on Linux and Firefox have no app badge – title and favicon work everywhere). Switch: **Settings → Chat & display → Show unread count in browser tab** (default on).
- **Desktop notifications** – operating-system pop-ups for the alerts that also appear as toasts at the top right, plus direct messages: *direct messages* (with the message text, shortened), *mentions* (`@callsign` in a group), *watchlist callsigns*, *watched groups* and *CQ calls*. They appear **only while nobody is looking at WebDesk**, so there is no double signal. A click brings the window to the front and opens the matching tab (on the right node). Off by default – **Settings → Chat & display → Watchlist → Desktop notifications**: enable it (the browser asks for permission once), choose which kinds you want, optionally *Without system sound*, and try it with the test button. Shown through the service worker, so it also works on Android Chrome while the page is open. Needs a secure context (HTTPS or `localhost`); a phone that has locked the screen or closed the app receives nothing – for that use the MQTT unread alert (Home Assistant).

### Improvements
- **Phone held sideways** – on screens up to 500 px high in landscape the automatic *compact* display mode now starts (before: 340 px, meant for 480×320 touch displays): one-row tab bar, slim header and send bar, monitor collapsed. At 844×390 the conversation gets 255 px instead of 31 px (tabs used to wrap onto four or five rows). The tab bar stays one swipeable row at that height in every mode, with the *+* and search buttons sticky at the edges. Portrait and normal windows are unchanged; *Normal* can still be chosen in Settings → Appearance.
- **iPhone in landscape stays inside the safe area** – the page no longer fills the area under the notch and the rounded corners; the right end of the *Send* and the *+* button is no longer cut off. The send field can shrink below its intrinsic width on phones. Alert toasts sit below the header, node switcher and tab bar on small screens (they used to cover the navigation) and keep clear of the notch.
- **Hint for an empty telemetry target** – under *Telemetry* a note says that an empty target sends native telemetry (extudp) only – then no text message is sent, not even via *Send Telemetry Now* (#24).

### Bugfixes
- **Nodes at the same location on the map** – stations at identical coordinates are spread on a small pixel circle, so each marker stays clickable; recomputed on update and zoom (#31).
- **Stale routing after a direct reception** – a packet received directly by radio (no relay path, not via UDP) now resets the hop count and the last relay path and removes relay paths last seen more than an hour before (#31).
- **MySQL monitor storage no longer floods the log** – with an unreachable database every incoming message started its own connect attempt (about 15 s) and logged a full error. After a failed write the storage now pauses for 60 seconds: the first failure is logged once (and says that messages in the pause are not stored), repeated failures only as a short warning, the first success logs how many messages were skipped. Saving the settings (e.g. a corrected connection string) ends the pause at once.
- **`install.sh` update over a running service** – copying the binary failed with *Text file busy*; the service is stopped first if it is active (#18).
- **No scary error at every start** – a browser carrying an antiforgery cookie from another instance (the cookie is shared by every port on `localhost`) made WebDesk log *key was not found in the key ring* with a long stack trace right after the start, although the request succeeded. That one harmless message is hidden now.
- **Monitor could not be collapsed or resized in some Firefox browsers** – where Firefox blocks site storage (blocked cookies/site data, strict tracking or fingerprinting protection, *delete cookies on close*), reading or writing `localStorage` threw an error: the ▼/▲ toggle did nothing and the divider could not be dragged. All storage access in `chat.js` is now guarded and falls back to memory, so both work; the setting is just not kept after a reload in such a browser.

---

## [1.18.2] – released

### Improvements
- **Optimized license key request** – the coffee dialog and the About page now have a small form (name, callsign, *PayPal already donated* or *request payment via Wero*, *Request a license key*) that opens a prefilled e-mail to the author in the interface language, followed by the German text. The e-mail is only offered once name and payment option are filled in; for a license key the callsign is required as well. Wero and PayPal only – the bank transfer option is gone. Texts in German, English, Spanish, French and Italian.
- **Settings: category tabs, search and a fixed Save bar** – the 29 sections are grouped into five tabs (*Connection*, *Sending & automation*, *Chat & display*, *Integrations*, *System*). A search field filters the sections by title and keywords, and the *Save* button stays at the bottom edge of the window instead of at the very end of the page.
- **One "Nodes" section** – *Connection* and *Additional Nodes* are merged. The shared UDP receive settings (*Listen IP* / *Listen Port*) appear once, followed by one card per node with the same layout, the primary node first. KISS/TCP, node password and console log sit under *Advanced*. The *Listen IP/Port* fields of the additional nodes are gone – the UDP socket is opened once from the global values. A local hub on port 1798 gets a *Bluetooth hub* badge. Nodes can be added with the setup wizard or manually.
- **Setup wizard** – it starts with *MeshCom node on WiFi/LAN*; the node search shows a spinner, a progress bar and a highlighted frame while it runs; buttons and selection list have one height; the node's fields are hidden while the wizard runs, and after *Apply to form* the wizard closes and the card reminds you to save. A node that still has its default name ("Node 2") is named after its callsign.

### Bugfixes
- **Docker update waits for the new image** – the update page reported a newer dev build as soon as the commit was on GitHub, but the image is only published by the CI about three minutes later; updating in that window pulled the old image again and changed nothing. The check now also asks the registry which commit the published image carries: while it lags behind, the page says the image is still being built, the *Update now* button is disabled and the banner stays hidden. If the registry cannot be read, nothing is blocked.
- **Backup restore lost settings** – restoring a backup reset the appearance (themes and custom themes), the quick texts, the frequency and the system margin to their defaults. They are restored now.
- **"Max. messages per tab" was not saved** – the value never reached the settings file and fell back to 500 after a restart.
- **Connection check texts named a field that no longer exists** – three messages told you to change *Own Callsign* under Settings → Connection, and one to look in the *Connection section*. They now point to **Connection → Nodes** and the card's *Callsign* field (*Receiving (UDP)* for Listen IP / Listen Port).

### Docs
- **Current screenshots** – the README screenshots show WebDesk v1.18.2 instead of v1.11.1: chat with monitor, MH list, live map (a 274 KB JPEG instead of a 5.7 MB PNG), the phone view with the one-row tab bar, the Console Command Helper and the AI search dialog. Names and places from QRZ, positions and locators are blurred or masked. The outdated terminal banner screenshot (v1.6.11) is gone; the *Startup* section still describes the banner.
- **Node connection and Docker update guides** – the node guides (de/en/es/it) and the README describe the merged *Nodes* section, the wizard and the card layout; the Docker update guides explain the *image is still being built* check.

---

## [1.18.1] – released

### Features
- **Bot command `>rx`** – ask a neighbour station running WebDesk how it receives you: `>rx` answers *direct* or *via RELAY-12 (1 hop)* together with RSSI/SNR, e.g. `RX DL2BBB-2: via RELAY-12 (1 hop) | heard from RELAY-12: RSSI -101 dBm, SNR -3.5 dB | 11:54:25`. The answer says whose signal the values are (always the last radio hop, i.e. the relay that was heard); packets that came in via the gateway/server have no radio value. Like `>ping` it is answered even with the bot switched off. German, English, Spanish, French and Italian.
- **Bot commands only with `>`** – the bot now reacts only to direct messages that start with `>` and a letter (`>ping`, `>version`, `>help`, `>time`, …). `--…` belongs to the node firmware's own console commands, and a bare `ping`, `--ping` or the em dash form are ordinary chat text and are no longer answered. With the bot switched off (default) `>ping`, `>version` and `>help` still answer. WebDesk's own ping (and its round-trip time display) uses `>ping`; other apps or older versions that still send `ping`/`--ping` get no reply any more. Settings texts, the `>help` output and the README are adjusted (de/en/es/fr/it).
- **Easier to grab divider on touch screens** – the divider between chat and monitor is only 6 px high; on touch devices it now has an invisible touch area of about 20 px, so resizing the monitor with a finger no longer needs a precise hit. The look and the layout are unchanged.
- **Tab bar on phones takes one row** – on screens up to 600 px wide the tabs sit in a single swipeable row instead of wrapping onto several rows (with 15 open tabs the bar was about 260 px high, now about 33 px). The tapped or newly active tab scrolls into view, the search buttons and *+* stay in reach, and dragging tabs to reorder them (long press) works as before. Wide screens are unchanged.
- **Show/hide the monitor on every screen size** – the ▼/▲ button in the monitor title bar, until now only on phones and small displays, is now available everywhere. With the monitor hidden only the title bar stays and the chat uses the freed height; the choice is remembered per browser.
- **Chat list updates only what changed** – chat rows are keyed, so with a full tab (500 messages) a new message no longer rewrites every row (about 8000 DOM changes for 4 messages before, about 150 now). Following the end of the list now only stops when *you* scroll (mouse wheel, touch, keys, scrollbar), not when the browser adjusts the scroll position itself, and the *N New Messages* button counts correctly even when the tab buffer is full.
- **Faster switch back to Chat, lighter monitor** – the monitor now builds only its newest 200 rows (a *Show older entries* button adds 200 more; the filter still searches all entries) and its rows are keyed, so a new message inserts one row instead of rewriting the whole list. Switching from e.g. MH back to Chat with a full monitor takes about half the time (about 1 s of blocked browser instead of up to 3 s).
- **"N new messages" button in the chat** – while you read older messages (scrolled up) and new ones arrive, a *↓ N New Messages* button appears at the bottom of the list; one click jumps to the end, scrolling down yourself dismisses it.

### Bugfixes
- **Identical messages were swallowed as "duplicates"** – besides the message id, WebDesk also treated two messages with the same sender, destination and text within 30 minutes as one, even when their ids differed. A second `>ping`, `73` or `ok` to the same station was dropped. A message that carries an id is now identified by the id alone (retries of the same message keep their id); the text comparison remains for copies without an id and, for 60 seconds, for the same packet arriving once with and once without an id (node and LoRa copy). ACKs keep the old rule.
- **`>ping` route counts only the relays** – the route in the `>ping` reply started with the sender itself and counted it as a hop (a path `YOU,RELAY-12` was shown as *2 hops*). It now lists only the relay stations and counts them, the same way `>rx` and the MH list do.
- **Decimal separator follows the language** – SNR in `>ping`/`>rx` replies and the values in `>weather` use a decimal comma (de/es/fr/it) or a point (en) according to the configured language instead of the server's operating-system setting.
- **Monitor hidden after the toggle became available everywhere** – a stale *hidden* setting from an earlier phone-sized window, harmless on a big screen until now, hid the monitor on the normal screen. The setting is stored under a new key, so the monitor is visible by default again.
- **No unwanted zoom on the iPhone** – iOS Safari zoomed into a text field with a font smaller than 16 px (the send field had 14 px) and stayed zoomed, so the page became wider than the screen and *Send* was hard to hit. On touch devices all text fields now use at least 16 px, and double-tap zoom is switched off. Pinch zoom with two fingers still works.
- **Chat and monitor jumped when switching pages** – going from e.g. MH back to Chat built the page with default sizes and only afterwards applied the saved monitor height / visibility, so the monitor and the message input visibly jumped (twice on a big screen, once in compact mode with the monitor open). The saved size and visibility are now applied before the first paint, and the resize animation only runs when you toggle the monitor yourself.
- **Setup assistant asked although a node was set up** – a saved single-node connection (no node profile) with your own callsign was treated as "not set up" as long as the node had not delivered data yet, so the first-start prompt and banner reappeared on every start. They now only appear for the placeholder callsigns (`NOCALL…`, `DH1FR-2`) and not at all once messages from the node exist.
- **Chat jumped to the end on every new message** – the message list was forced to the bottom on each refresh, so scrolling up to read was constantly interrupted. It now only follows the end while you are at the end; switching tabs and sending your own message still scroll to the end.

### Docs
- **Bluetooth hub linked** – the README (intro) and the node connection guides (de/en/es/it) point to the [MeshCom UDP Bluetooth Hub](https://github.com/DH1FR/MeshComUDPBluetoothHub) for nodes without WiFi (e.g. Heltec T114). The Features sections of the README now describe the current scrolling, the monitor (show/hide on every screen size, newest 200 rows), the one-row tab bar on phones and the deduplication (message id first, 30 minutes); the firmware note says the source of v4.40a.11.22 was checked.

---

## [1.18.0] – released

### Features
- **Connection check** (Settings → Connection → *Check settings*) – checks the current, even unsaved, values and explains what does not fit and what to change: IP/port format, whether *Listen IP* belongs to this PC (typical mistake: the node's IP instead of `0.0.0.0`), the real bind error, ping, port conflicts, Bluetooth hub, KISS reachability, nodes that share an IP or callsign, and whether callsign + SSID match the node. Nothing is changed automatically.
- **Setup wizard** (*Setup wizard*, also per additional node) – five steps: node kind (WiFi/LAN node, Bluetooth hub on this PC or another machine), automatic or manual configuration, node and PC data with a **node search in the local network** (hostname/mDNS, packets already received, scan for the node's web page) and a selection list for the node IP, node setup, check. Values reach the form only on *Apply to form*; saving stays with you.
- **First-start prompt** – if no node is set up and no data arrives, WebDesk asks right at startup whether to start the wizard (once per browser tab; *Later* leaves a banner, *Do not ask again* hides both).
- **Read and correct the node over the net console** – the check reads `--info` (read-only) and compares ext UDP, target IP, the node's IP, KISS switches, `--nopmother` and the callsign with the WebDesk configuration. A button at the end of each finding corrects it on the node – only after your click and confirmation. Only `--extudpip`, `--extudp on`, `--kiss on|tx on|meta on` and `--setcall` can be sent; `--extudpip` always goes first (the firmware refuses `--extudp on` without a target IP) and the node's reply is shown.
- **Callsign on either side** – on a mismatch take over the node's callsign into WebDesk, or set the node to the WebDesk callsign (`--setcall`, with a warning that the node's identity changes and it may restart).
- **KISS button** – with KISS/TCP switched on in Settings, *Activate KISS on the node via the net console* switches KISS, TX and RxMeta on at the node (after confirmation).
- **Status bar** – a red/yellow UDP or KISS badge opens the check when clicked; a new *Callsign?* badge appears when the node returns a sent message under a different callsign.
- **One-click Docker update** – on the update page of a Docker installation, *Update now* pulls the newer image and restarts WebDesk (the page reloads itself). A second container, Watchtower, does the Docker work, so WebDesk gets no access to Docker. Two update channels: **`latest`** (releases, recommended) and **`dev`** (every push to the dev branch, built by GitHub as `ghcr.io/dh1fr/meshcomwebdesk:dev`, amd64). A banner announces a newer build of the channel you use. New `docker-compose.image.yml`, `.env.example` and `scripts/update-docker.sh` (`--image` or built from source); guide: `docs/docker-update-en.md`.
- **Check now** – the update page has a *Check now* button that asks GitHub immediately (release or dev channel) and shows when it last checked; it also works for installs that update by swapping files.
- **QR code for phone/tablet** – a 📱 button in the navigation bar (wide screens only) shows, on request, a QR code with the address under which WebDesk can be opened on another device in the same network. https addresses come first, the address can be set in Settings → *Access from other devices* (Docker / reverse proxy), and the dialog warns that WebDesk has no access protection.
- **Bluetooth hub** – recognised by the check and the wizard (hub running? port conflict? nodes on the same IP). The hub's `--info` now also reports callsign, ext UDP and node values in the firmware's wording (hub update included in its own repository). With the current hub the check also compares the hub's **UDP port** and the **port it sends to** with the WebDesk settings and checks that WebDesk listens on the address the hub sends to.

### Bugfixes
- **Bind errors were invisible** – a failed UDP bind was only written to the log; the red status badge now shows the reason in its tooltip.
- **Cancelled JS calls** (reload, navigation, hot reload) no longer end as an unhandled exception in `OnAfterRenderAsync`.

### Docs
- **Node connection guides** (de/en/es/it) describe the check, the wizard, the node search, correcting settings over the net console, the callsign handling, KISS activation and the Bluetooth hub. All new texts are available in German, English, Spanish, French and Italian.

---

## [1.17.4] – released

### Features
- **Donate via Wero or bank transfer** – the coffee dialog, the About page and the README now point out that a donation also works via Wero or SEPA bank transfer: a short e-mail with a prefilled subject is enough, the bank details are sent back (or the amount is requested via Wero). Texts in German, English, Spanish, French and Italian.
- **Windows setup in five languages** – all setup texts (tasks, shortcuts, status messages, service description) are now translated; Spanish, French and Italian are added as wizard languages next to German and English. The setup script also got a UTF-8 BOM so umlauts and accents are shown correctly.

### Docs
- **Italian translation improved** – thanks to giovanni69 (pull request #30).

---

## [1.17.3] – released

### Features
- **One-click update** – the new page **/update** (linked from the update banner and from About) downloads the matching release, verifies its SHA-256 checksum, backs up the program files **and your data**, swaps the files and restarts WebDesk. If the new version does not become healthy (3 starts without a stable minute), it is **rolled back automatically**; any backup can also be restored manually. Supports the Windows service, systemd and manually started installations (Windows, Linux, Raspberry Pi, macOS); Docker and development runs are detected and not updated. Starting an update is only possible from the machine itself (localhost). The release now ships `.sha256` files and a self-contained Windows ZIP for installer-based installations. *The first version with this feature has to be installed manually once.*
- **MQTT: live subscriber/prefix changes** – enabling the send-topic subscriber or changing the topic prefix now takes effect immediately, no restart needed.

### Bugfixes
- **Ping on group/broadcast tabs** – the node firmware (4.40a.10+) only accepts a callsign as ping target. A ping on a group or broadcast tab is now blocked with a hint instead of being sent (and never answered).

### Docs
- **Firmware 4.40a.10+: ext-udp target** – the node refuses broadcast/multicast/own-IP as UDP target (`--extudpip`). The node-connection guides (de/en/es/it) now say to enter the specific IP of the WebDesk PC.
- **Translations** – all new texts plus a few previously missing ones are available in Spanish, French and Italian.

---

## [1.17.2] – 2026-11-09

### Features
- **Adjustable UDP ports** – Listen Port and Device Port (Settings → Connection and per additional node) are editable now (default 1799), so WebDesk can talk to a UDP hub on another port, e.g. [MeshComUDPBluetoothHub](https://github.com/DH1FR/MeshComUDPBluetoothHub). Before they were read-only.

---

## [1.17.1] – 2026-11-09

### Features
- **Compact display mode for tiny screens (e.g. 480×320 touch displays)** – slim icon-only header, single-row tabs, non-wrapping status badges, finger-sized send bar, slimmer node switcher and update banner. The monitor is hidden on first visit, and the header/status stay out of the way while typing. A compact MH list shows only callsign, name, last heard, distance, RSSI and SNR; the multi-node button bar is replaced by a header node dropdown; map controls form one scrollable row with larger zoom buttons; Settings uses a single column with finger-sized inputs. Compact mode is auto-detected for very small viewports or chosen in Settings → Appearance → display mode (auto / normal / compact, stored per browser). `?display=compact|normal|auto` forces it for the current browser tab only. Compact mode now survives Blazor page navigation.

### Bugfixes
- **CQ detection: false positives** – a message addressed to one station (`@DH1FR …`) is no longer treated as a CQ call, and neither is "CQ" glued to `/` or `-` (e.g. the magazine "CQ/DL"). Normal calls such as `CQ de OE6TZD` or `IY6GM CQ 144300` are still detected.
- **Compact mode** – the monitor toggle stays reachable when collapsed, tabs and send bar stay visible with an open monitor, the saved monitor height is ignored when collapsed, and the header stays visible while the send bar has focus.

### Security
- **MySqlConnector 2.5.0 → 2.6.1** (GHSA-473q-m89c-ghf8): with `SslMode=VerifyCA`/`VerifyFull` and no `SslCa`, the DB password could be sent in clear text to a man-in-the-middle before the certificate was verified. The default `Preferred` mode and local connections were not affected.

---

## [1.17.0] – 2026-11-01

### Features
- **Online indicator on direct chat tabs** – a coloured dot on every direct tab shows whether the station is probably reachable: green (recently active), yellow (uncertain), grey (offline); 📡 means it was heard directly by radio. Based on the dashboard's `nodes.html` (server last-seen, refreshed every 5 minutes) and the local MH list. Settings → Chat & Monitor: on/off and the two thresholds (default 45 / 120 minutes).
- **Clickable Maidenhead locators** – a locator in chat or monitor (e.g. `JO41sb`) links to the map and highlights its square; hovering it shows a small preview map with a corner label.
- **Map legend collapsed by default.**
- **Node delivery status (firmware 4.40a)** – the node's JSON `{"type":"ack","msg_id":…,"status":…}` datagram is now evaluated. It is matched to your outgoing message by the `msg_id` of the node echo: status 1/2 (gateway / destination ACK) sets the delivery ticks, status 3 shows ❌ "delivery failed" once the node's retransmit ladder gave up, status 4 shows the 📬 mailbox marker with the holding node. Status 0 (heard by a neighbour) only confirms transmission. Unknown `msg_id`s (sent from another client) are ignored.
- **Station card: modulation, hop budget, APRS symbol** – the MHeard station card shows the LoRa modulation ID (`lora_mod`), the remaining hop budget (`max_hop`) and the APRS symbol (`aprs_symbol`/`aprs_symbol_group`, with a glyph for common symbols).
- **Gateway presets HB and SP** – Settings → Gateway Source now offers 🇨🇭 HB (meshcom.emcomm.ch) and 🇵🇱 SP (sp.meshcom.pl).

### Bugfixes
- **Online indicator / locator preview** – the online-indicator settings were not saved to the appsettings override, and the locator hover preview was lost when Blazor re-rendered the link or navigated away; both fixed.
- **DL gateway dashboard returned 404** – the DL preset pointed to `/meshcom/rakgw.html`; the page is at `http://meshcom.hamnet.network/rakgw.html`. Stored custom sources with the old URL are repaired automatically.

---

## [1.16.0] – released

### Features
- **MQTT unread alert**: unseen direct messages to your own callsign are published to `{prefix}/unread/{callsign}`, `{prefix}/read/{callsign}` follows once seen (e.g. Home Assistant notification). New settings `UnreadAlertEnabled` / `UnreadAlertSeconds` / `UnreadActiveSeconds`.
- A conversation counts as read only while somebody is really looking (visible tab + recent activity).
- Unread badge per node in the node switcher.
- Calendar beacon: weekly entries accept several weekdays.

### Improvements
- IT/ES/FR translations completed.

## [1.15.4] – 2026-10-11

### Bugfixes
- **Direct messages to other stations opened a fake group tab** – a message addressed to a callsign that is not yours (e.g. a station heard on the mesh/RF, `To=DL6WAB`) was taken for a group and opened a tab `#DL6WAB` whenever the group filter was off (the default). Group numbers are purely numeric, so a callsign-like destination is never a group any more: such messages appear **only in the monitor** (no tab, no CQ alert). Before, this only worked for the callsigns of your own configured nodes.
- **Group labels missing on a fresh installation** – the group tabs did not show their short names (DACH, EU, …) because the label list started empty and the official MeshCom list was only available via the "restore defaults" button. A fresh installation (no saved settings yet) is now pre-filled with the official list again; once you have saved settings, an emptied list stays empty.
- **Windows installer: generic icon** – the executable had no icon, so the desktop/start-menu/autostart shortcuts and the entry in "Apps & features" showed the Windows default. The WebDesk icon is now embedded in the exe and used by the installer shortcuts.
- **Header: licensed callsign wrapped awkwardly** – when the header line was full, "Licensed for" stayed on the first line and the callsign jumped to the start of the title line. The license badge is now its own block, the callsign stays together and wraps below the badge text.

---

## [1.15.3] – 2026-10-04

### Features
- **Blacklist** – new **Settings → 🚫 Blacklist** (callsign list, one per line). Messages from these stations appear **only in the monitor** – no chat or group tab, no direct-message/watchlist/CQ/bot handling, no webhook/MQTT. `DH1FR` or `DH1FR-*` blocks every SSID of that callsign, `DH1FR-55` only exactly that one. The MH list and the map are still updated.
- **Translations as language files** – the UI languages Italian, Spanish and French are no longer compiled in but plain JSON files (`MeshcomWebDesk/Languages/*.json`, English text → translation). Anyone can **add a language or improve a translation** without touching code: drop a `xx.json` into `<DataPath>/languages/` (Docker: `./data/languages/`) – a file with the code of a built-in language overrides single strings, a new code adds a new language that appears in **Settings → Language** (a "Reload language files" button avoids a restart). Missing strings fall back to English. `python scripts/check_translations.py` reports missing/unused texts and `--write xx` creates a template; see `docs/translations.md`.
- **Firmware 4.40a: `--mheard` window** – the node now answers `--mheard` with `[MH] key=value` lines instead of an ASCII table. The MHeard popup (Telnet console and Command Helper) understands the new format (the old table still works) and shows the new columns **Age**, **Role** (N = node, R = relay), **NB** (bidirectional neighbours) and **GW**.
- **Mailbox custody notice (store-and-forward, firmware 4.40a)** – a `:stoNNN` text from a mailbox node is no longer shown as a chat message: it appears in the monitor as `STO` and the matching outgoing DM gets a **📬** tick ("held by mailbox node X – delivered once the station is reachable"). The real ACK still turns it into ✓✓.
- **Map: role, neighbours, gateway (firmware 4.40a)** – after a `--mheard` answer (MHeard popup) the map popup of the listed stations shows **📡 Relay / Node**, the number of **bidirectional neighbours** and a **GW** badge for gateways. ext-udp does not carry this data, so it is refreshed whenever you open the MHeard popup.

### Improvements
- **Translations completed** – the Italian and Spanish dictionaries were missing about 560 texts and French about 220 (mostly newer features such as KISS, Weather API, Web view, watchlist groups, Settings hints); all are translated now. Texts with values ("3 min ago", "Close tab "X"?", …) were not translatable at all and now use placeholders (`TF`), several hard-coded German/English labels (MH list, Command Helper, Reload) are localised too.
- **Doc links** – the node-connection guide link now points to an existing language (de/en/es/it, everything else → en) instead of a 404 for French.
- **Monitor: long texts no longer cut off** – long messages in the monitor wrap onto further lines instead of ending with an ellipsis.

### Bugfixes
- **Message retries (firmware 4.35u+)** – the node re-sends an unacknowledged DM up to 3×, encoding the attempt in bits 10–11 of the `msg_id`. These copies are now recognised as the same message (the dedup key ignores those bits) instead of relying on the text fallback alone.
- **ACK ticks**: an ACK is now applied to the newest unacknowledged message with that sequence number (numbers wrap at 999 and are reused) instead of the oldest.
- **KISS with SSID > 15**: messages are sent over ext-udp instead of failing on the AX.25 SSID limit.

---

## [1.15.2] – released

### Features
- **Web view**: new **🌐 Web** tab next to the map embedding configurable web pages in a sandboxed iframe (Settings → 🌐 Web).
- **Watchlist: groups**: the Watchlist can now follow groups (toast, alert tone, optional voice announcement); own messages are ignored and the voice announcement is throttled per group.

### Bugfixes
- **KISS without "Additional Nodes"**: KISS/TCP can be enabled directly under Settings → Connection (incl. node password); the primary node profile is created automatically.
- **Node switcher for a single node**: the node bar in the chat and the NET Console node selector only appear when more than one node is enabled.
- **MH list, map and chat tabs empty after adding the first node**: the carry-over from the legacy single-node state now always runs, merges without duplicates and recovers data of deleted/re-created node profiles.

## [1.15.1] – released

### Features
- **New hardware IDs**: added `hw_id` 58 (HELTEC-E213 / Heltec Vision Master E213), 59 (ESP32-LORAPRS-E22) and 60 (ESP32-LORAPRS-RA01) to the hardware name lookup; documented the previously-added `hw_id` 61 (T-WATCH-S3) in the README table.
- **Live Map: neighbour count & beacon comment**: for stations heard over KISS/TCP the map popup now shows the `/N` neighbour count (`👥 N`) and the operator's beacon comment (machine extensions like `/B= /A= /R=` stripped). A well-meshed station (`/N` ≥ 6) gets a slightly larger marker dot; new legend entry.

### Bugfixes
- **Monitor layout on narrow windows / phones**: a KISS position row's APRS comment was wrapped character-by-character into a tall thin column. Monitor rows now wrap cleanly and the comment sits on its own line, truncated with an ellipsis (full text in the tooltip). Regression from v1.15.0.
- **Absurd air pressure for some weather stations**: a heard station's ext-udp telemetry carries the barometric *altitude* under the `qfe` key, not the pressure, so BME680-equipped nodes showed values like `191 hPa`. WebDesk now ignores `qfe` for heard stations and range-checks every pressure to a plausible 540–1080 hPa. Upstream firmware bug.
- **Chat ACK/delivery tooltip not localized**: the message delivery-state tooltip was hardcoded in German; now localized with ES/FR/IT strings added. The status bar for a KISS-primary node also shows ext-udp before KISS now.

---

## [1.15.0] – released

### Features
- **KISS/TCP as an optional per-node add-on**: each node can *additionally* be connected over its KISS/TCP interface (TCP port 8001; requires a MeshCom firmware with the KISS/TCP interface), on top of the always-on ext-udp base – a checkbox per node in Settings. Over KISS the monitor gains the full APRS position comment, the digipeater path, the `/R=` relay-node list, the `/N` neighbour count and RSSI/SNR per frame. Sending works over KISS with a per-send delivery result from the node's `0xF0` frame. Optional HMAC-SHA256 auth (`--kiss auth on`) reuses the node password.
- **KISS hub**: WebDesk holds the single KISS connection to a node and re-serves it as its own KISS/TCP listener, so Dire Wolf, YAAC, APRSdroid etc. connect through WebDesk instead of competing for the node's one slot. Port, target node and localhost/LAN binding configurable under Settings → KISS Hub.
- **SrcInfo frame (KISS `0x20`)**: origin callsigns with an SSID above 15 (`-16`…`-99`, clamped to `-15` in the AX.25 address field) are restored from a separate frame and used as the real sender for display and as the reply addressee.
- **KISS and ext-udp as separate status indicators** for a KISS-primary node; a yellow ext-udp dot flags when the node's own position / telemetry / firmware data is not arriving.
- **Per-node password** feeding NET Console (HMAC) and KISS auth; the unused TLS certificate fingerprint field was removed.
- **KISS console commands** `--kiss`, `--kiss tx`, `--kiss meta` in the Console Command Helper.

### Bugfixes
- Bare `ackNNN` / `rejNNN` acknowledgements are now recognised, so delivery checkmarks are set for ACKs from standard APRS clients (e.g. PinPoint).
- MeshCom time-sync broadcasts are kept out of the chat over KISS (monitor only), matching ext-udp.
- A hub client's ACK is no longer misattributed to WebDesk's own outgoing messages that share a base callsign.
- KISS info fields decode as UTF-8 with a Latin-1 fallback (Dire Wolf / PinPoint send CP1252).
- A KISS connect timeout shows as a clean "node unreachable" status; auth detection no longer stalls on a quiet no-auth node.

---

## [1.14.3] – released

### Features
- **Custom gateway sources**: the *Gateway Source* setting now offers the Italian dashboard (`meshcom.dig-italia.it`) as a built-in preset alongside OE and DL, plus an "OE + DL" and a "None" option. Below it, an arbitrary number of additional dashboards can be added by URL (each must point to a MeshCom dashboard `rakgw.html` page); callsigns from every enabled source are merged. Source changes take effect immediately instead of after the next 15-minute refresh.

### Bugfixes
- **Gateway list empty (OE/DL)**: the MeshCom dashboards moved their gateway page from `gateways.html` to `rakgw.html`; the OE URL had started returning 404, so the default gateway highlight list was empty. All preset URLs updated.
- **Map popup/tooltip text localized**: marker popups and tooltips on the live map showed hardcoded German strings regardless of the selected UI language.
- **Own-position altitude unit localized**: the altitude unit next to the own position in the MH window was not translated.
- **Quick text insertion**: picking a quick text now inserts it at the cursor position instead of overwriting the input field.
- **MH header spacing**: added the missing space between the station count and its label in the MH monitor header.

---

## [1.14.2] – released

### Features
- **New hardware ID**: added `hw_id` 61 (T-WATCH-S3) to the hardware name lookup.
- **New console command `--setlog on/off`**: shows incoming LoRa packets (Message/Position/Hey/ACK) in an overview, without needing to enable the other debug outputs; available in the Console Command Helper's Debug group.
- **Hardware-type icons**: the hardware badge in the MH monitor and the multi-node switcher buttons now shows a small icon for hardware with a distinctive form factor (⌚ T-WATCH-S3, ⌨️ T-DECK family, 🔌 T-ETH-ELITE); generic LoRa boards stay text-only. Hidden on small screens in the node switcher to preserve the existing mobile layout.

---

## [1.14.1] – released

### Features
- **Map connection view rework**: relay links are now aggregated per hop segment instead of per full path, using a shared LoRa signal-quality scale (RSSI/SNR) across the map, MH list and chat monitor. Adds a time filter (15 min/1 h/6 h/all) and a minimum-usage filter for relay segments, direction arrows, a focus mode that dims unrelated links while a marker popup is open, and diff-based marker updates so open popups survive live updates.
- **Ping round-trip time**: when a direct `ping`/`--ping`/`>ping` gets a `Pong!` reply from the same station within 5 minutes, the chat bubble now shows the round-trip time (⏱ ms/s) next to the reply text.
- **ACK round-trip time**: tracked alongside the Pong round-trip and shown on the Pong reply itself.
- **UDP receive diagnostics popover**: a ❓ button next to the UDP status badge (visible once it falls back to yellow after 15 minutes without a new packet) shows the node's local IP addresses and a troubleshooting checklist.
- **Battery level icon**: the static 🔋 emoji is now a small fill-proportional, colour-coded icon reflecting the charge percentage; used consistently in the chat status bar, node switcher, monitor list, MH table and callsign popup, and now shown for every configured node instead of only the primary one.

### Bugfixes
- Service worker no longer throws an invalid `Response` on a failed fetch, which could leave the browser tab hanging after resuming from sleep.
- Fixed a Chrome-only circuit crash (`removeChild`/`insertBefore` on `null`) when switching chat tabs or sending a message, caused by Chrome's automatic page translation rewriting the DOM behind Blazor's back; the app now opts out via `translate="no"`.
- Hardened the Blazor circuit against render-batch backlog on slower connections (`MaxBufferedUnacknowledgedRenderBatches` raised from the default).

---

## [1.14.0] – released

### Features
- **Appearance settings with themes**: New 🎨 *Appearance* section in Settings: choose between four built-in themes (**MeshCom Dark** – the classic look and default, **Midnight (OLED)**, **Light**, **High Contrast**) or create your own. Custom themes are edited in a grouped colour editor (backgrounds, accents, buttons, text, links, status colours, messages, monitor), applied live as a preview and saved under a name. Themes can be exported/imported as `.mctheme.json` files so users can share them. The entire UI stylesheet was refactored to semantic CSS custom properties; the default theme renders identical to previous releases.
- **Native extudp telemetry**: Selected telemetry values (temp, humidity, pressure, temp 2, QNH, gas resistance, CO2) can now be sent directly to the node as a native `"type":"tele"` UDP telegram – written straight into the node's sensor variables and flowing into its next position beacon – instead of only as chat text. Sends happen on value change rather than on a fixed schedule, using the fixed field names the node's firmware expects. IT/ES/FR translations added for the extudp telemetry UI.
- **Extudp telemetry firmware-capability check**: After each send, WebDesk now watches for the node's own next telemetry echo and compares it against the values just sent. If no matching echo arrives within 20 s (older firmware without this feature, and nodes with real sensor hardware installed, look identical from here), extudp telemetry is disabled automatically and persisted, with the confirmation status (✅/⚠️/ℹ️) shown live next to the toggle in Settings.
- **Additional nodes can be disabled**: Each node under *Additional Nodes* now has an enable/disable checkbox so a node can be taken out of active use (registration, sending, chat switcher, telnet console) temporarily without deleting its configuration. The primary node's connection fields (callsign, device/listen IP + port) are now read-only there and mirror the *Connection* section above, so they no longer need to be entered twice; promoting a different node to primary moves its connection data up into that section instead.
- **NET Console**: Button to open a node's default web interface directly.
- **Toolbar**: *Connect* button shown when disconnected, not just *Disconnect*.

### Bugfixes
- **Own beacons shown in the monitor**: Own position/telemetry beacons now appear in the monitor, not just other stations'.
- **Telemetry mapping**: Duplicate `WeatherRole` assignments are cleared automatically; skipped extudp sends are now logged with a reason instead of failing silently; `TelemetryExtUdpEnabled` and the extudp slot are persisted correctly on save.
- **Spectrum chart**: No longer accumulates points from previous scans.
- **Light theme contrast**: Fixed for own-message callsigns and chips (Console Command Helper etc.).

---

## [1.13.3] – released

### Features
- **Calendar Beacon – multiple announcement lead times**: The separate "days before" / "hours before" fields are replaced by a comma-separated lead-time list per entry, e.g. `3d, 24h, 2h` (units: `d` = days, `h` = hours, `m` = minutes; no unit = hours). Any number of advance announcements per event; existing settings migrate automatically and slot keys stay compatible (no duplicate sends after the update). (#13, #14)
- **Calendar Beacon – next-transmission preview**: The settings page shows date and time of the next scheduled transmission for each entry, in addition to the next event date.
- **Weather API – optional request/response logging**: New *Log requests* toggle logs the masked request URL and raw provider response, matching the existing QRZ / MQTT / AI logging pattern.
- **Console Command Helper – via node remembered**: The last-sent via node callsign is remembered.

### Bugfixes
- **Calendar Beacon – missing group warns**: Active entries without a target group were silently skipped; the settings page and the log now show a clear warning.
- **Stale weather data no longer rebroadcast**: Telemetry sending checks the observation timestamp and skips outdated values; Weather Underground empty responses (HTTP 204) produce a clear error instead of a JSON parse failure; hanging requests fail fast (30 s timeout).
- **iPad detection via touch heuristic**: Clipboard copy and TTS skip now also work on iPads that report themselves as macOS.
- **Bot help prefix shortened**: `Befehle/Commands:` reduced to `Cmds:` to save LoRa payload space.
- **Settings backup – CalendarBeacons null-guard on restore**: A backup file containing `"CalendarBeacons": null` no longer aborts the import.
- **Release packaging**: Linux/macOS tarballs now extract into a `MeshcomWebDesk/` folder instead of flat into the current directory (install instructions updated).

---

## [1.13.2] – released

### Features
- **`>` as alternative bot command prefix**: Bot commands can now be triggered with `>` in addition to `--` (e.g. `>ping`, `>help`). The `>` prefix is the native syntax introduced in MeshCom firmware 4.35p.
- **Bot replies use `>` syntax**: Help output, unknown-command messages and license notices now display `>help`, `>ping` etc. instead of `--help` / `--ping`.
- **Via-routing in monitor (firmware 4.35p)**: Newer firmware encodes the via-routing path inside the `dst` JSON field (e.g. `"DB0FRI-12,DH1FR-2"`). The monitor now parses this and renders it as `→ DB0FRI-12 ⟶ DH1FR-2`, mirroring the existing relay-hop display for the source path.
- **Via-routing on outgoing messages**: When the local node echoes back a sent message, the via-path reported in the echo is applied retroactively to the already-displayed TX row in the monitor.
- **Console Command Helper – `via` and `viadebug`**: `--via on/off` (enable/disable via-routing) and `--viadebug on/off` (via-routing debug output) are now available as Console Command Helper entries. The via-node callsign can be set directly in the helper via a text field (`--via OE1KBC-24` / `--via NONE`).

---

## [1.13.1] – released

### Bugfixes
- **Fix node echo not recognized with relay path in dst**: Newer firmware versions include via-nodes in the `dst` field of the echo packet (e.g. `"DB0KH-11,DH1FR-99"` instead of `"DH1FR-99"`), causing the node-echo timeout warning to fire on every send. Only the final callsign is now used for matching.
- **Fix ANSI / C1 control chars in NET Console output**: Firmware v4.35p routes Serial debug output through the NET Console, including ANSI escape sequences and raw C1 control bytes (0x9B etc.). The strip regex now covers the full C1 range (U+0080–U+009F).
- **Fix MQTT send payload with mixed-case `text` key**: `TryGetProperty("text")` was case-sensitive and silently dropped payloads using `"Text"` or `"TEXT"`. The lookup now uses `OrdinalIgnoreCase`.
- **Fix Python 3 missing from Docker image / HamQSL endpoint in prop.py**: `python3` was missing from the runtime image; added and symlinked as `python`. `prop.py` switches from the deprecated `solar.php` endpoint (returns a GIF) to `solarxml.php`.

---

## [1.11.0] – in development (dev)

### Features
- **Weather API (AWEKAS & Weather Underground)**: External weather data can now be fetched from [AWEKAS](https://www.awekas.at) or [Weather Underground](https://www.wunderground.com) and written directly to the telemetry JSON file. Provider, station ID, API key, and poll interval are configurable in Settings → 🌤️ Weather API. A simulation mode (no API key required) generates realistic test data for offline testing.
- **Weather API – License model**: Real provider access (AWEKAS / Weather Underground) requires a personal license key tied to the station callsign incl. SSID. Without a license, only the simulation mode is available and the poll interval is fixed at 24 hours. License keys are issued upon a coffee donation (☕ Buy me a Coffee / 💳 PayPal). The license request button pre-fills an e-mail with callsign, provider and donation reference.
- **Weather API – Bot command**: New bot command `--weather` reports the current provider, last received values, license status and timestamp.
- **New-message divider line**: Unread messages in chat tabs are now visually separated from already-read messages by a horizontal divider line (*"Neue Nachrichten"* / *"New messages"*), making it easy to identify where new messages start after returning to a tab.
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
- **Donate buttons**: Buy me a Coffee and PayPal donation buttons are now shown everywhere (About page, Welcome dialog, Weather API license section).

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
- **Fix Weather API key not decrypted on load**: `WeatherApi.ApiKey` was encrypted on save but not decrypted when loading settings, causing false authentication failures.
- **Fix Weather API settings not persisted**: The entire `WeatherApi` block was missing from `SettingsService`, so provider/key/interval were reset to defaults on every restart.

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