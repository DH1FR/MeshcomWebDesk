# 🔌 Nodo MeshCom: Configuración de conexión para WebDesk

Esta guía explica qué ajustes deben configurarse en el **nodo MeshCom** y en **WebDesk** para establecer la comunicación.

---

## Requisito previo: Misma red

El nodo y el PC con WebDesk deben ser accesibles en la **misma red local (LAN/Wi-Fi)**.

> 📋 **Nota sobre los valores de ejemplo:** Todas las direcciones IP, puertos e indicativos de esta guía son **solo ejemplos**. En tu red las direcciones pueden ser completamente distintas (p.ej. `10.0.0.x`, `172.16.x.x` u otra subred). Ajusta todos los valores según tu propia configuración de red.

```
Nodo MeshCom  ◄──── UDP 1799 ────►  PC WebDesk
192.168.1.60                         192.168.1.100
(IP de ejemplo)                      (IP de ejemplo)
```

---

## Paso 1 – Configurar el firmware del nodo (EXTUDP)

WebDesk se comunica mediante el **protocolo EXTUDP JSON**. Esto debe habilitarse en el nodo.

> ℹ️ Las rutas exactas del menú dependen de la versión del firmware.  
> Compatible con **MeshCom Firmware v4.35+**

| Ajuste en el menú del nodo | Valor |
|---------------------------|-------|
| **Habilitar EXTUDP** | ✅ Activado |
| **IP de destino UDP** | Dirección IP del PC WebDesk (p.ej. `192.168.1.100`) |
| **Puerto UDP de destino** | `1799` |

> 💡 **Consejo:** Encuentra la IP del PC WebDesk con `ipconfig` (Windows) o `ip a` / `ifconfig` (Linux/macOS).

---

## Paso 2 – Configuración de WebDesk (`/settings` → 🔌 Conexión)

| Campo | Valor recomendado | Significado |
|-------|------------------|-------------|
| **Listen IP** | `0.0.0.0` | Escucha en todas las interfaces de red |
| **Listen Port** | `1799` | Puerto UDP en el que WebDesk recibe mensajes entrantes |
| **Device IP** | `192.168.1.60` | Dirección IP del nodo MeshCom |
| **Device Port** | `1799` | Puerto UDP del nodo para mensajes salientes |
| **Indicativo propio** | p.ej. `EA1XYZ-2` | Tu indicativo con SSID |

> ⚠️ **Listen Port** y **Device Port** deben coincidir con la configuración EXTUDP del nodo (por defecto: `1799`).

---

## Paso 3 – Verificar el firewall

Asegúrate de que el **puerto UDP 1799** **no esté bloqueado** en el PC WebDesk.

**Firewall de Windows (una vez, como Administrador):**
```powershell
netsh advfirewall firewall add rule name="MeshCom WebDesk UDP 1799" protocol=UDP dir=in localport=1799 action=allow
```

**Linux (ufw):**
```bash
sudo ufw allow 1799/udp
```

---

## Filtro del Monitor

El **panel del monitor** (área inferior) tiene un campo de búsqueda 🔍 en la barra de título.

- Escribe un **indicativo** o un **fragmento de texto** para filtrar las filas al instante
- Busca en: `De`, `Para`, `texto del mensaje`, `datos brutos`
- **No distingue mayúsculas/minúsculas**, la posición en el texto no importa
- El contador cambia a `X / Y Entradas` cuando hay un filtro activo
- El botón **×** borra el filtro

---

## Indicador de estado en la barra de estado

| Símbolo | Significado |
|---------|-------------|
| 🔴 UDP **Sin socket** | El puerto UDP no pudo vincularse (p.ej. puerto ya en uso) |
| 🟡 UDP **Esperando señal** | Socket activo, pero aún no se ha recibido ningún paquete del nodo |
| 🟢 UDP **Recibiendo** | Se ha recibido al menos un paquete UDP del nodo |

> ℹ️ Como UDP no tiene conexión, no existe un estado clásico de "conectado". Verde significa que los datos realmente están llegando.

---

## Problemas comunes

| Síntoma | Posible causa |
|---------|--------------|
| Estado: 🔴 Sin socket | Puerto UDP ya en uso o error de permisos |
| Estado: 🟡 Esperando señal | El nodo no envía a la IP correcta / puerto incorrecto |
| Sin mensajes entrantes | El firewall bloquea UDP 1799 |
| Puede enviar pero no recibir | Listen IP incorrecto (p.ej. IP específica en lugar de `0.0.0.0`) |
| Puede recibir pero no enviar | Device IP o Device Port incorrectos |
| Los mensajes llegan duplicados | El nodo está configurado con dos IPs de destino |

---

## Ejemplo: Configuración completa

```
IP del nodo:      192.168.1.60
IP del PC WebDesk: 192.168.1.100

--- Firmware del nodo ---
EXTUDP:           Habilitado
IP de destino UDP: 192.168.1.100  ← PC WebDesk
Puerto UDP:       1799

--- WebDesk appsettings / página de Configuración ---
ListenIp:         0.0.0.0
ListenPort:       1799
DeviceIp:         192.168.1.60   ← Nodo
DevicePort:       1799
MyCallsign:       EA1XYZ-2
```

---

## Enlaces adicionales

- 🏠 [Proyecto MeshCom](https://icssw.org/meshcom/)
- 💾 [MeshCom Firmware en GitHub](https://github.com/icssw-org/MeshCom-Firmware)
- 🔗 [Repositorio MeshCom WebDesk](https://github.com/DH1FR/MeshcomWebDesk)
- 📖 [Integración de telemetría con Home Assistant](homeassistant-telemetry.md)
