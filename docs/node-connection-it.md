# 🔌 Nodo MeshCom: Impostazioni di connessione per WebDesk

Questa guida spiega quali impostazioni devono essere configurate sul **nodo MeshCom** e in **WebDesk** per stabilire la comunicazione.

---

## Prerequisito: Stessa rete

Il nodo e il PC con WebDesk devono essere raggiungibili nella **stessa rete locale (LAN/Wi-Fi)**.

> 📋 **Nota sui valori di esempio:** Tutti gli indirizzi IP, le porte e i nominativi in questa guida sono **solo esempi**. Nella tua rete gli indirizzi possono essere completamente diversi (es. `10.0.0.x`, `172.16.x.x` o un'altra subnet). Adatta tutti i valori alla tua configurazione di rete.

```
Nodo MeshCom  ◄──── UDP 1799 ────►  PC WebDesk
192.168.1.60                         192.168.1.100
(IP di esempio)                      (IP di esempio)
```

---

## Passo 1 – Configurare il firmware del nodo (EXTUDP)

WebDesk comunica tramite il **protocollo EXTUDP JSON**. Questo deve essere abilitato sul nodo.

> ℹ️ I percorsi esatti del menu dipendono dalla versione del firmware.  
> Compatibile con **MeshCom Firmware v4.35+**

| Impostazione nel menu del nodo | Valore |
|-------------------------------|--------|
| **Abilitare EXTUDP** | ✅ Attivo |
| **IP di destinazione UDP** | Indirizzo IP del PC WebDesk (es. `192.168.1.100`) |
| **Porta UDP di destinazione** | `1799` |

> 💡 **Suggerimento:** Trovate l'IP del PC WebDesk con `ipconfig` (Windows) o `ip a` / `ifconfig` (Linux/macOS).

> ⚠️ **Firmware 4.40a.10 e successivi:** l'IP di destinazione UDP **non può più essere un indirizzo broadcast/multicast** (`255.255.255.255`, il broadcast della sottorete del nodo, `224.x.x.x`, `0.0.0.0`, l'IP del nodo stesso). Il nodo registra `[EXT] target … refused` e non invia nulla. Inserisci sempre l'**IP specifico del PC WebDesk** (`--extudpip 192.168.1.100`).

---

## Passo 2 – Impostazioni WebDesk (`/settings` → 🔌 Connessione)

| Campo | Valore consigliato | Significato |
|-------|-------------------|-------------|
| **Listen IP** | `0.0.0.0` | Ascolta su tutte le interfacce di rete |
| **Listen Port** | `1799` | Porta UDP su cui WebDesk riceve i messaggi in arrivo |
| **Device IP** | `192.168.1.60` | Indirizzo IP del nodo MeshCom |
| **Device Port** | `1799` | Porta UDP del nodo per i messaggi in uscita |
| **Nominativo** | es. `IZ0XYZ-2` | Il tuo nominativo con SSID |

> ⚠️ **Listen Port** e **Device Port** devono corrispondere alla configurazione EXTUDP del nodo (default: `1799`).

---

## Passo 3 – Verificare il firewall

Assicurati che la **porta UDP 1799** **non sia bloccata** sul PC WebDesk.

**Windows Firewall (una volta, come Amministratore):**
```powershell
netsh advfirewall firewall add rule name="MeshCom WebDesk UDP 1799" protocol=UDP dir=in localport=1799 action=allow
```

**Linux (ufw):**
```bash
sudo ufw allow 1799/udp
```

---

## Controllo della connessione e assistente di configurazione

In **Impostazioni → 🔌 Connessione** ci sono due pulsanti:

- **🔍 Controlla impostazioni** controlla i valori attuali (anche quelli non salvati) e indica cosa non va e cosa modificare: formato di indirizzi IP e porte, se **Listen IP** appartiene a questo PC (errore frequente: l'IP del nodo invece di `0.0.0.0`), se il socket UDP è aperto, se il nodo è raggiungibile, se arrivano pacchetti e se **nominativo e SSID** corrispondono al nodo.
- **🧙 Assistente di configurazione** ti guida passo per passo (nodo WiFi, hub Bluetooth su questo PC o su un altro computer) Già nel primo passo scegli *Configura il nodo automaticamente* (preselezionato; WebDesk imposta ext UDP e l'IP di destinazione tramite la console di rete; se è disattivata, l'assistente ti guida ad attivarla nell'interfaccia web del nodo) oppure *Configura il nodo manualmente* (comandi da copiare). I valori vengono inseriti solo dopo il clic su *Applica al modulo*; poi devi ancora salvare.

Nessuno dei due cambia **nulla automaticamente** né invia **nulla al nodo**. Sotto **Nodi aggiuntivi** ogni nodo ha il proprio pulsante di controllo (🔍) e il proprio assistente (🧙).

Se lo **stato UDP nella barra di stato è rosso** o giallo, un clic apre il controllo (rosso: direttamente nelle impostazioni con il motivo concreto dell'errore).

**Primo avvio:** Se non è ancora configurato alcun nodo (nessuna lista di nodi e nessun dato da un nodo pochi secondi dopo l'avvio; anche dopo aver eliminato tutti i nodi) la chat mostra in alto un banner con *Avvia configurazione*. Apre le impostazioni con l'assistente. *Più tardi* lo nasconde solo fino al prossimo caricamento, *Non mostrare più* in modo permanente. Dopo il primo salvataggio non compare più.

**Cercare un nodo in rete:** L'assistente ha due pulsanti. *Cerca nodo* risolve il nominativo come nome host (il firmware lo usa come nome DHCP/mDNS) e mostra gli IP che hanno già inviato pacchetti. *Scansiona la rete* interroga, su richiesta, gli indirizzi della tua rete locale /24 alla ricerca di un server web MeshCom (porta 80, titolo pagina «NOMINATIVO - Meshcom»). Trova solo nodi con il server web attivo; un nodo con solo ext-UDP non si annuncia da solo. Cliccando su un risultato l'IP viene solo copiato nell'assistente.

**Leggere e correggere le impostazioni sul nodo (console di rete):** Se la console di rete del nodo è attivata (porta 2323, password HMAC), il controllo mostra sotto la sezione *Impostazioni del nodo tramite la console di rete*. *Leggi dal nodo* segnala ext UDP, l'IP di destinazione e il nominativo del nodo. Se qualcosa differisce, *Correggi sul nodo …* propone i comandi `--extudp on` e `--extudpip <IP di questo PC>`; non viene inviato nulla finché non confermi, e solo questi due comandi. Il nominativo sul nodo non viene mai modificato; invece *Usa il nominativo … in WebDesk* copia il valore del nodo nel modulo. Se la console di rete non è raggiungibile, il controllo indica che deve essere attivata sul nodo (interfaccia web: IP Network Settings → net console, oppure `--netconsole on`; password con `--passwd`).

**Confronto automatico di tutte le impostazioni del nodo:** Se la console di rete è raggiungibile e la password del nodo è nota, il controllo legge il nodo da sé a ogni esecuzione (solo lettura, `--info`) e confronta ciò che WebDesk ha configurato: **nominativo/SSID**, **ext UDP** (attivo) e **IP di destinazione** (l'IP di questo PC), l'**indirizzo IP del nodo** (rispetto alla Device IP) e, con KISS attivo, **KISS/TCP, TX, RxMeta e Auth**. Mostra inoltre se `--nopmother` trattiene i messaggi diretti altrui. La sezione sotto il controllo mostra tutto in una tabella (*Sul nodo* / *Atteso*). La correzione propone solo `--extudp on`, `--extudpip`, `--kiss on`, `--kiss tx on` e `--kiss meta on`.

**Attivare KISS sul nodo:** Se KISS/TCP è attivato in *Connessione* (o in un nodo aggiuntivo), lì compare il pulsante *Attiva KISS sul nodo tramite la console di rete*. Legge il nodo e propone `--kiss on`, `--kiss tx on` e `--kiss meta on` – non viene inviato nulla finché non confermi. Solo i nodi ESP32 con firmware adatto offrono KISS.

**Adottare il nominativo da entrambi i lati:** Se il nominativo differisce ci sono due pulsanti. *Usa il nominativo … in WebDesk* inserisce il valore del nodo nel modulo (poi salva). *Cambia il nominativo sul nodo in …* imposta il nodo sul nominativo di WebDesk (`--setcall`, tramite la console di rete). Succede solo dopo un clic e una conferma; l'avviso segnala che cambia l'identità del nodo nella mesh e che il nodo può riavviarsi. Viene inviato solo questo comando.

**Accorgersi subito di un SSID errato:** Una discrepanza si nota anche senza console di rete: se WebDesk invia un messaggio e il nodo lo restituisce con un altro nominativo, la barra di stato mostra il badge di avviso **⚠️ Nominativo?** (un clic apre il controllo) e il controllo indica il nominativo che il nodo usa davvero. WebDesk non cambia nulla automaticamente.

**Nominativo e SSID:** WebDesk riconosce i propri pacchetti dal nominativo. Se l'SSID differisce dal nodo, i pacchetti arrivano ma lo stato UDP resta giallo. Il controllo confronta con ciò che il nodo stesso segnala (posizione, telemetria, eco dei messaggi o `--info` tramite la console di rete) e indica il valore corretto.

---

## Hub Bluetooth (MeshCom UDP Bluetooth Hub)

I nodi senza WiFi (ad es. Heltec T114) si possono collegare tramite il [**MeshCom UDP Bluetooth Hub**](https://github.com/DH1FR/MeshComUDPBluetoothHub). L'hub si presenta a WebDesk come un nodo con ext-UDP. Se gira sullo stesso PC, hub e WebDesk non devono usare la stessa porta:

| Impostazione WebDesk | Valore |
|---|---|
| Device IP | `127.0.0.1` (o l'IP del computer dell'hub) |
| Device Port | `1798` (l'«UDP port» dell'hub) |
| Listen IP / Listen Port | `127.0.0.1` oppure `0.0.0.0` / `1799` |
| Nominativo | **quello del nodo**, con SSID |

Nell'hub: collega il nodo via Bluetooth (disconnetti prima l'app del telefono), «WebDesk» = `127.0.0.1:1799`. Il controllo riconosce il funzionamento con hub, verifica se qualcosa è in ascolto sulla porta dell'hub e avvisa dei conflitti di porta. WebDesk non può distinguere **due nodi con lo stesso IP** (ad es. due hub su un PC), perché assegna i pacchetti in base all'IP di origine. Con l'hub attuale il controllo confronta inoltre la sua **porta UDP** (= Device Port) e la **porta a cui invia** (= Listen Port) con le impostazioni di WebDesk e verifica che WebDesk ascolti sull'indirizzo a cui l'hub invia (un hub che invia a `127.0.0.1` non raggiunge WebDesk se questo ascolta solo sull'indirizzo LAN).

---

## Filtro Monitor

Il **riquadro monitor** (area inferiore) ha un campo di ricerca 🔍 nella barra del titolo.

- Digita un **nominativo** o un **frammento di testo** per filtrare le righe immediatamente
- Ricerca in: `Da`, `A`, `testo del messaggio`, `dati grezzi`
- **Non distingue maiuscole/minuscole**, la posizione nel testo non ha importanza
- Il contatore passa a `X / Y Voci` quando un filtro è attivo
- Il pulsante **×** cancella il filtro

---

## Indicatore di stato nella barra di stato

| Simbolo | Significato |
|---------|-------------|
| 🔴 UDP **Nessun socket** | La porta UDP non è stata associata (es. porta già occupata) |
| 🟡 UDP **Attesa segnale** | Socket attivo, ma nessun pacchetto ricevuto dal nodo |
| 🟢 UDP **Ricezione OK** | Almeno un pacchetto UDP è stato ricevuto dal nodo |

> ℹ️ Poiché UDP è senza connessione, non esiste un classico stato "connesso". Verde significa che i dati arrivano effettivamente.

---

## Problemi comuni

| Sintomo | Possibile causa |
|---------|----------------|
| Stato: 🔴 Nessun socket | Porta UDP già occupata o errore di permesso |
| Stato: 🟡 Attesa segnale | Il nodo non invia all'IP corretto / porta errata |
| Nessun messaggio in arrivo | Il firewall blocca UDP 1799 |
| Può inviare ma non ricevere | Listen IP errato (es. IP specifico invece di `0.0.0.0`) |
| Può ricevere ma non inviare | Device IP o Device Port errati |
| I messaggi arrivano doppi | Il nodo è configurato con due IP di destinazione |

---

## Esempio: Configurazione completa

```
IP Nodo:          192.168.1.60
IP PC WebDesk:    192.168.1.100

--- Firmware del nodo ---
EXTUDP:           Abilitato
IP di destinazione UDP: 192.168.1.100  ← PC WebDesk
Porta UDP:        1799

--- WebDesk appsettings / pagina Impostazioni ---
ListenIp:         0.0.0.0
ListenPort:       1799
DeviceIp:         192.168.1.60   ← Nodo
DevicePort:       1799
MyCallsign:       IZ0XYZ-2
```

---

## Link utili

- 🏠 [Progetto MeshCom](https://icssw.org/meshcom/)
- 💾 [MeshCom Firmware su GitHub](https://github.com/icssw-org/MeshCom-Firmware)
- 🔗 [Repository MeshCom WebDesk](https://github.com/DH1FR/MeshcomWebDesk)
- 📖 [Integrazione telemetria Home Assistant](homeassistant-telemetry.md)
