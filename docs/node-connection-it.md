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
