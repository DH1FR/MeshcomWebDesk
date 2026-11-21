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

> ⚠️ **Firmware 4.40a.10 o posterior:** la IP de destino UDP **ya no puede ser una dirección broadcast/multicast** (`255.255.255.255`, el broadcast de la propia subred, `224.x.x.x`, `0.0.0.0`, la IP del propio nodo). El nodo registra `[EXT] target … refused` y no envía nada. Introduce siempre la **IP concreta del PC de WebDesk** (`--extudpip 192.168.1.100`).

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

## Comprobación de la conexión y asistente de configuración

En **Ajustes → 🔌 Conexión** hay dos botones:

- **🔍 Comprobar ajustes** comprueba los valores actuales (también los no guardados) e indica qué no encaja y qué cambiar: formato de direcciones IP y puertos, si **Listen IP** pertenece a este PC (error frecuente: la IP del nodo en lugar de `0.0.0.0`), si el socket UDP está abierto, si el nodo es accesible, si llegan paquetes y si **indicativo y SSID** coinciden con el nodo.
- **🧙 Asistente de configuración** te guía paso a paso (nodo WiFi, hub Bluetooth en este PC o en otro equipo) Ya en el primer paso eliges *Configurar el nodo automáticamente* (preseleccionado; WebDesk configura ext UDP y la IP de destino mediante la consola de red; si está desactivada, el asistente te guía para activarla en la interfaz web del nodo) o *Configurar el nodo manualmente* (comandos para copiar). Los valores solo se introducen al hacer clic en *Aplicar al formulario*; después aún debes guardar.

Ninguno cambia **nada automáticamente** ni envía **nada al nodo**. En **Nodos adicionales** cada nodo tiene su propio botón de comprobación (🔍) y asistente (🧙).

Si el **estado UDP de la barra de estado está en rojo** o amarillo, al hacer clic se abre la comprobación (rojo: directamente en los ajustes con el motivo concreto del error).

**Primer inicio:** Si todavía no hay ningún nodo configurado (sin lista de nodos y sin datos de un nodo pocos segundos después del inicio; también tras borrar todos los nodos) el chat muestra arriba un banner con *Iniciar configuración*. Abre los ajustes con el asistente. *Más tarde* lo oculta solo hasta la próxima carga, *No volver a mostrar* de forma permanente. Tras el primer guardado ya no aparece.

**Buscar un nodo en la red:** El asistente tiene dos botones. *Buscar nodo* resuelve el indicativo como nombre de host (el firmware lo usa como nombre DHCP/mDNS) y muestra las IP que ya han enviado paquetes. *Buscar en la red* consulta, a petición, las direcciones de tu red local /24 en busca de un servidor web de MeshCom (puerto 80, título de página «INDICATIVO - Meshcom»). Solo encuentra nodos con el servidor web activado; un nodo solo con ext-UDP no se anuncia por sí mismo. Al hacer clic en un resultado solo se copia la IP en el asistente.

**Leer y corregir ajustes en el nodo (consola de red):** Si la consola de red del nodo está activada (puerto 2323, contraseña HMAC), la comprobación muestra abajo la sección *Ajustes del nodo mediante la consola de red*. *Leer del nodo* informa de ext UDP, la IP de destino y el indicativo del nodo. Si algo difiere, *Corregir en el nodo …* ofrece los comandos `--extudp on` y `--extudpip <IP de este PC>`; no se envía nada hasta que confirmes, y solo estos dos comandos. El indicativo del nodo nunca se modifica; en su lugar *Usar el indicativo … en WebDesk* copia el valor del nodo al formulario. Si la consola de red no es accesible, la comprobación indica que debe estar activada en el nodo (interfaz web: IP Network Settings → net console, o `--netconsole on`; contraseña con `--passwd`).

**Comparación automática de todos los ajustes del nodo:** Si la consola de red es accesible y se conoce la contraseña del nodo, la comprobación lee el nodo por sí sola en cada ejecución (solo lectura, `--info`) y compara lo que WebDesk tiene configurado: **indicativo/SSID**, **ext UDP** (activado) e **IP de destino** (la IP de este PC), la **dirección IP del nodo** (frente a Device IP) y, con KISS activado, **KISS/TCP, TX, RxMeta y Auth**. Además muestra si `--nopmother` retiene mensajes directos ajenos. La sección bajo la comprobación lo muestra todo en una tabla (*En el nodo* / *Esperado*). La corrección solo ofrece `--extudp on`, `--extudpip`, `--kiss on`, `--kiss tx on` y `--kiss meta on`.

**Activar KISS en el nodo:** Si KISS/TCP está activado en *Conexión* (o en un nodo adicional), allí aparece el botón *Activar KISS en el nodo mediante la consola de red*. Lee el nodo y ofrece `--kiss on`, `--kiss tx on` y `--kiss meta on` – no se envía nada hasta que confirmes. Solo los nodos ESP32 con el firmware adecuado ofrecen KISS.

**Adoptar el indicativo en cualquiera de los dos lados:** Si el indicativo difiere hay dos botones. *Usar el indicativo … en WebDesk* introduce el valor del nodo en el formulario (después guarda). *Cambiar el indicativo en el nodo a …* ajusta el nodo al indicativo de WebDesk (`--setcall`, mediante la consola de red). Esto solo ocurre tras un clic y una confirmación; la advertencia señala que cambia la identidad del nodo en la malla y que el nodo puede reiniciarse. Solo se envía este único comando.

**Notar enseguida un SSID incorrecto:** Una discrepancia también se nota sin consola de red: si WebDesk envía un mensaje y el nodo lo devuelve con otro indicativo, la barra de estado muestra la insignia de aviso **⚠️ ¿Indicativo?** (un clic abre la comprobación) y la comprobación indica el indicativo que el nodo usa realmente. WebDesk no cambia nada automáticamente.

**Indicativo y SSID:** WebDesk reconoce sus propios paquetes por el indicativo. Si el SSID difiere del nodo, los paquetes llegan, pero el estado UDP sigue en amarillo. La comprobación compara con lo que informa el propio nodo (posición, telemetría, eco de mensajes o `--info` mediante la consola de red) e indica el valor correcto.

---

## Hub Bluetooth (MeshCom UDP Bluetooth Hub)

Los nodos sin WiFi (p. ej. Heltec T114) se pueden conectar mediante el **MeshCom UDP Bluetooth Hub**. El hub se presenta ante WebDesk como un nodo con ext-UDP. Si se ejecuta en el mismo PC, hub y WebDesk no deben usar el mismo puerto:

| Ajuste de WebDesk | Valor |
|---|---|
| Device IP | `127.0.0.1` (o la IP del equipo del hub) |
| Device Port | `1798` (el «UDP port» del hub) |
| Listen IP / Listen Port | `127.0.0.1` o `0.0.0.0` / `1799` |
| Indicativo | **el del nodo**, con SSID |

En el hub: conecta el nodo por Bluetooth (desconecta antes la app del móvil), «WebDesk» = `127.0.0.1:1799`. La comprobación reconoce el funcionamiento con hub, verifica si algo escucha en el puerto del hub y avisa de conflictos de puertos. WebDesk no puede distinguir **dos nodos con la misma IP** (p. ej. dos hubs en un PC), porque asigna los paquetes por su IP de origen.

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
