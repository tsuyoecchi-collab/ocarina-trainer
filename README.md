# Phykro Trainer

Trainer externo con men├║ de trucos para juegos de **un solo jugador** en Windows x64.
Pensado para `ocarina.exe` (TU juego single-player), pero funciona con cualquier proceso
x64 al que tengas acceso.

No inyecta DLLs: lee y escribe la memoria del proceso desde fuera, como Cheat Engine.
Incluye esc├íner de memoria propio, trucos con congelaci├│n de valores, atajos globales,
teletransporte por waypoints y gu├¡a integrada.

> **Uso responsable:** solo para juegos offline y copias que poseas. No lo uses en juegos
> online, con anticheat ni para saltarte protecciones. Modificar la memoria puede colgar el
> juego: haz copia de tus partidas antes de probar.

---

## Inicio r├ípido

1. Abre el juego.
2. Ejecuta `Phykro-Trainer.exe`.
3. El trainer detecta `ocarina` autom├íticamente y se conecta. Si no lo hace, elige el
   proceso en la lista y pulsa **Conectar** (o **Actualizar** si acabas de abrir el juego).
4. Si el juego se ejecuta como administrador, abre el trainer con
   **Ajustes ÔåÆ Reiniciar como administrador**.

La barra superior muestra `Conectado ┬À lectura y escritura activas` cuando todo est├í listo.

## Encontrar una direcci├│n (ejemplo: rupias)

No hay direcciones m├ígicas: cada partida/versi├│n puede tener las suyas. El flujo es el
mismo que en Cheat Engine, pero integrado:

1. En el juego, apunta un valor exacto (por ejemplo, **42 rupias**).
2. En **Esc├íner**, elige:
   - **Tipo:** Entero 32 bits (`Int32`) para dinero, vida, munici├│n y contadores.
   - **Comparaci├│n:** Valor exacto.
   - **Valor:** `42`.
   - **├ürea:** Memoria peque├▒a Ôëñ 4 MB (recomendado) o M├│dulo del juego.
3. Pulsa **Buscar**. Saldr├ín much├¡simas coincidencias; es normal.
4. Vuelve al juego, gasta o gana rupias (por ejemplo, hasta **50**).
5. En el trainer, pon `50` y busca de nuevo con **Valor exacto**; o elige **Aument├│** /
   **Disminuy├│** seg├║n lo que haya pasado y pulsa **Buscar** para filtrar.
6. Repite el paso 4ÔÇô5 hasta quedarte con 1ÔÇô5 direcciones.
7. Selecciona las filas (Ctrl o May├║s para varias) y pulsa **A├▒adir a trucos**.
8. En **Trucos**, escribe el valor deseado (por ejemplo `9999`), marca **Activo** y
   **Congelar**, y el trainer lo escribir├í cada 50 ms.

Para la vida usa el mismo m├®todo: busca el valor visible, recibe da├▒o y filtra con
**Disminuy├│** hasta aislar la direcci├│n; luego cong├®lala en el m├íximo.

## Rupias en Ocarina Remake (probado)

Esta versi├│n del juego (`ocarina.exe` con runtime NX2navi 0.10.1, Ocarina of Time 1.1.1)
guarda el monedero real en una direcci├│n fija de la memoria:

| Dato | Valor |
|---|---|
| Direcci├│n | `0x1ADD3E134` (Entero 32 bits, little-endian) |
| Copia/espejo | `0x1ADD3E138` (la sincroniza el propio juego; no hace falta tocarla) |
| Valor probado | 500 rupias |

Se comprob├│ cerrando y volviendo a abrir el juego (PID y base del m├│dulo nuevos): la
direcci├│n sigue siendo la misma y contiene las rupias de la partida cargada.

Para tenerlo listo sin escanear:

1. En **Trucos**, pulsa **Plantillas Ôû¥**.
2. Elige **Rupias ┬À 0x1ADD3E134 ┬À atajo Ctrl+Alt+D**.
3. Ajusta **Valor** (por ejemplo `500`) y pulsa **Aplicar ahora**.
4. Con **Ctrl+Alt+D** activas/desactivas **Congelar**: mientras est├® activo, el trainer
   reescribe el valor cada 50 ms (las rupias no bajan al gastar).

La plantilla ya trae **M├íximo** `500` y la direcci├│n del m├íximo (`0x1ADD3E364`): aunque
escribas `999`, el trainer lo recorta a la capacidad real del monedero y la sigue sola si
consigues una bolsa mayor (ver m├ís abajo).

El espejo `0x1ADD3E138` no se toca: cuando cambian las rupias, el juego actualiza ambas
posiciones. Si una actualizaci├│n del juego cambia la direcci├│n, vuelve a localizarla con
el procedimiento del apartado anterior y edita el truco (doble clic en la fila).

## Munici├│n infinita en Ocarina Remake (probado)

Los seis consumibles tambi├®n tienen direcci├│n fija (todas Int32):

| Objeto | Direcci├│n | Valor de la sesi├│n |
|---|---|---|
| Semillas Deku | `0x1ADD3EA44` | 27 |
| Palos Deku | `0x1ADD3EA6C` | 30 |
| Nuez Deku | `0x1ADD3EA1C` | 29 |
| Bombas | `0x1ADD3E83C` | 39 |
| Bombchu | `0x1ADD3E8DC` | 38 |
| Bulbo Mercader | `0x1ADD3F00C` | 23 |

A diferencia de las rupias, el espejo de `+4` **no** se sincroniza solo: el juego lee el
primer valor. Por eso el truco escribe ├║nicamente esa direcci├│n.

1. En **Trucos**, pulsa **Plantillas Ôû¥**.
2. Elige **Munici├│n infinita ┬À 6 tipos**.
3. Se a├▒aden los seis trucos con **Congelar** activo en el valor que tengan en ese momento:
   no bajan al usarlos. Para dejar de congelar, desmarca **Congelar** o cierra el trainer.

Las plantillas incluyen el **M├íximo** de cada objeto (30 palos/nueces, 40 bombas/bombchu,
50 semillas, 500 rupias), as├¡ que congelar en `999` no supera la capacidad real. Bombas,
semillas, palos y nueces llevan adem├ís la **direcci├│n del m├íximo** puesta: el trainer lee la
capacidad del juego y, si consigues una bolsa mayor, sube el m├íximo y el stock solos.
El bulbo del mercader no tiene tope porque su l├¡mite depende del comercio.

Se comprob├│ en vivo escribiendo valores m├ís bajos en memoria: el trainer los devolvi├│ a su
valor congelado en menos de un segundo. Si el juego se actualiza y deja de funcionar,
vuelve a localizar cada direcci├│n con el Esc├íner y edita el truco (doble clic en la fila).

### Si no aparece nada

- Prueba **M├│dulo del juego** o **Toda la memoria escribible** (m├ís lento).
- Cambia el **Tipo**: `Int16`, `Float` o `Byte`.
- En barras (corazones, energ├¡a) el valor interno puede no coincidir con el n├║mero que ves.
- Usa **Nueva b├║squeda** para empezar de cero cuando cambies de valor objetivo.

### Direcciones que sobreviven al reinicio

- Las direcciones absolutas (`0x...`) pueden cambiar al cerrar el juego.
- Si la direcci├│n cae dentro del m├│dulo, el trainer la guarda como
  `ocarina.exe+0x1A2B3C`, que suele seguir sirviendo (el ejecutable tiene base fija).
- Para valores del mont├│n (heap), pega una cadena de punteros de Cheat Engine:
  `ocarina.exe+0x1A2B3C,0x10,0x8`
  El trainer resuelve cada salto y escribe el valor final.

## Pantalla Estad├¡sticas

La barra lateral incluye una p├ígina **Estad├¡sticas**:
una tabla de solo lectura con los contadores y capacidades que declara el m├│dulo del juego.
No hay botones de escribir: el trainer nunca modifica esos valores. La tabla se refresca
sola mientras est├ís conectado, y si un valor no parece real (negativo o por encima del
m├íximo) o la direcci├│n ya no se lee, muestra `ÔÇö`. La columna **Actualizado** registra la ├║ltima vez que cambi├│ cada valor (null = nunca). Un checkbox en la cabecera permite mostrar/ocultar la columna **Valor anterior**.

**Las estad├¡sticas se cargan de un JSON editable, no del c├│digo.** La primera vez el
trainer crea junto al ejecutable el archivo `estadisticas.json` con 33 entradas: las 33
registros de la tabla de estad├¡sticas del juego (como ┬½Valor 1┬╗ ÔÇª ┬½Valor 33┬╗) y la
Skulltulas doradas ya etiquetada. Puedes renombrar, a├▒adir, quitar o corregir direcciones,
tipos y m├íximos en ese archivo y **reconectar** para que se apliquen **sin recompilar**.

Formato de cada entrada:

```json
[
  { "Nombre": "Valor 1", "Direccion": "0x1ADEB54F0", "Tipo": "Int32", "Maximo": 0, "DireccionMaximo": "", "Nota": "" }
]
```

- `Nombre`: texto que se muestra en la columna ┬½Dato┬╗.
- `Direccion`: mismas sintaxis que un truco (`0x...` absoluta, `ocarina.exe+0x...` o cadena de punteros).
- `Tipo`: `Byte`, `Int16`, `Int32`, `Int64`, `Float` o `Double`.
- `Maximo`: opcional (0 = sin tope). Si un valor lo supera o es negativo, se muestra `ÔÇö`.
- `DireccionMaximo`: opcional. Direcci├│n de memoria donde el juego guarda la capacidad real
  de este dato (p. ej. `0x1ADD3E364` para las rupias, `0x1ADD3E2C4` para las bombas).
  Si se rellena, la columna **M├íximo** muestra el valor le├¡do de memoria en vez del n├║mero
  fijo de `Maximo`. Estilo `TrainerEngine.SyncMax`: el trainer la lee cada tick y adapta
  el m├íximo mostrado. Vac├¡a = usar `Maximo` fijo. Permite enlazar un m├íximo a otra direcci├│n
  **sin recompilar**.
- `Nota`: texto que aparece en la columna ┬½Detalle┬╗.

Si el archivo falta o se estropea, el trainer lo regenera con los valores por defecto. Si est├í
en `%APPDATA%\PhykroTrainer`, puedes abrir la carpeta desde **Ajustes ÔåÆ Abrir carpeta de datos**.

## Trucos

- **Activo**: el truco se tiene en cuenta.
- **Congelar**: se reescribe el valor continuamente (cada 50 ms por defecto).
- **M├íximo**: tope opcional. Si se rellena, **Aplicar ahora** y **Congelar** nunca escriben
  por encima de esa cifra; el estado muestra ┬½Limitado al m├íximo ÔÇª┬╗. Deja la celda vac├¡a para
  no limitar. El valor que lee el juego no se modifica: solo se recorta lo que escribe el trainer.
- **Dir. m├íximo**: direcci├│n opcional donde el juego guarda la capacidad real de ese objeto
  (p. ej. `0x1ADD3E2C4` para el saco de bombas, `0x1ADD3E314` para las semillas,
  `0x1ADD3E364` para las rup├¡as). El trainer la lee constantemente y actualiza la columna
  **M├íximo** solo. Si el truco est├í **congelado** y el valor estaba al tope (o por encima)
  del m├íximo anterior, al subir la bolsa en el juego el stock se sube tambi├®n al nuevo
  m├íximo. Ejemplo: m├íximo 30 bombas ÔåÆ el trainer recorta a 30; consigues el saco de 40 ÔåÆ
  el trainer pone M├íximo 40 y el stock en 40.
- **Aplicar ahora**: escribe el valor una vez.
- **Leer**: muestra el valor actual en la columna **Actual**.
- **Plantillas Ôû¥**: crea un truco con nombre, tipo y valor t├¡picos; solo falta pegar la
  direcci├│n encontrada. La secci├│n **Ocarina 1.1.1 ┬À direcciones probadas** crea el truco
  de rupias y los seis de munici├│n ya completos, con direcci├│n, valores, topes y, cuando se
  conoce, la direcci├│n del m├íximo autom├ítica.
- **Restaurar originales**: devuelve a memoria los valores que hab├¡a antes de la primera
  escritura de esta sesi├│n.

### L├¡mites del juego (Ocarina 1.1.1)

Estos son los topes usados por las plantillas y por la columna **M├íximo**:

| Objeto | M├íximo |
|---|---|
| Rupias | 500 |
| Semillas Deku | 50 |
| Palos Deku | 30 |
| Nuez Deku | 30 |
| Bombas | 40 |
| Bombchu | 40 |
| Bulbo Mercader | sin tope |

El juego guarda la capacidad de la bolsa en memoria, junto a los consumibles
(`0x1ADD3E2C4` = 40 bombas, `0x1ADD3E314` = 50 semillas, `0x1ADD3E364` = 500 rupias,
`0x1ADD3E2EC` = 30 nueces y `0x1ADD3E33C` = 30 palos). Si
consigues una bolsa mayor, sube el **M├íximo** del truco para aprovecharla. Nunca escribas
por encima de la capacidad: el contador puede desbordarse o mostrarse mal.

En la plantilla de **Munici├│n infinita**, bombas, semillas, palos y nueces (y las rupias)
llevan la direcci├│n del m├íximo puesta: el trainer actualiza el tope y el stock solo. Los
sacos de bombchus no tienen direcci├│n de m├íximo confirmada en esta versi├│n del
juego; a├▒├ídeles una en **Trucos ÔåÆ Editar ÔåÆ Dir. m├íximo** si la localizas, o edita a mano
la columna **M├íximo** (por ejemplo, `40` mientras use bolsas peque├▒as).


Cada truco puede tener su **atajo** (F1, Ctrl+Alt+2, Shift+F5ÔÇª). Al pulsarlo se activa o
desactiva **Congelar**. Los atajos son globales: funcionan con el juego en primer plano.

## M├│dulos de juego

El motor de memoria y el esc├íner son gen├®ricos: no saben de ning├║n juego concreto. Lo que
depende de un juego (nombre del proceso, direcciones y plantillas de trucos) vive en un
**m├│dulo** dentro de `Core/Games/`:

- `GameModule.cs` define `IGameModule`, `CheatPreset` y `CheatPresetGroup`.
- `OcarinaModule.cs` contiene las direcciones verificadas de Ocarina 1.1.1.
- `GameModules.cs` registra los m├│dulos y elige el que corresponde al proceso del perfil.

Al pulsar **Plantillas Ôû¥**, la secci├│n ┬½direcciones probadas┬╗ se genera desde el m├│dulo del
juego activo, no con direcciones fijas en la interfaz. Para soportar otro juego basta con
crear otro `IGameModule` (proceso + plantillas) y registrarlo en `GameModules`; el esc├íner,
el motor de congelaci├│n y la interfaz no cambian.

## Waypoints y teletransporte

1. Encuentra tres `Float` que cambien al moverte (normalmente X, Y y Z, seguidos en memoria).
   Usa el Esc├íner con **Cambi├│** o **Disminuy├│/Aument├│** mientras te mueves.
2. Pega las tres direcciones en **Waypoints ÔåÆ X / Y / Z** y pulsa **Guardar direcciones**.
3. Mu├®vete a un lugar, ponle nombre y pulsa **Guardar posici├│n** (o **F9**).
4. Selecciona el waypoint y pulsa **Teletransportar** (o **F10**).

Si el juego usa otra escala, el teletransporte puede dejarte en otro sitio: ajusta las
coordenadas o busca los valores que use internamente el juego.

## Atajos por defecto

| Atajo | Acci├│n |
|---|---|
| `Ctrl+Alt+T` | Ocultar / mostrar el trainer |
| `Ctrl+Alt+D` | Congelar / descongelar rupias (truco de Ocarina) |
| `F9` | Guardar waypoint en la posici├│n actual |
| `F10` | Teletransportar al waypoint seleccionado |

Se pueden cambiar en **Ajustes**, y cada truco tiene su propio atajo en **Trucos**.

## Archivos y datos

El trainer guarda todo junto al ejecutable:

- `trainer-config.json`: perfiles por juego, trucos, waypoints y atajos.
- `trainer.log`: registro de errores y conexiones.
- `selftest.txt` / `probe.txt`: informes de las pruebas de diagn├│stico.

Si la carpeta del ejecutable no es escribible, usa
`%APPDATA%\PhykroTrainer`. Puedes abrir la carpeta desde **Ajustes ÔåÆ Abrir carpeta de datos**.

## Compilar desde el c├│digo

Requisitos: Windows 10/11 x64 y .NET SDK 10.

```powershell
cd ocarina-trainer

# Compilar
dotnet build -c Release

# Pruebas internas (sin tocar ning├║n juego)
dotnet run -c Release -- --selftest

# Diagn├│stico de solo lectura sobre el juego en ejecuci├│n
dotnet run -c Release -- --probe ocarina.exe

# Publicar un ├║nico .exe autocontenido en dist\
.\build.ps1
```

`build.ps1` compila, ejecuta el selftest y publica
`dist\Phykro-Trainer.exe` (autocontenido: no hace falta instalar .NET en el equipo destino).

## Soluci├│n de problemas

- **No conecta**: abre el juego antes, pulsa **Actualizar** y elige el proceso.
- **Solo lectura**: el juego est├í elevado; reinicia el trainer como administrador.
- **No escribe / no cambia nada**: la direcci├│n puede ser una copia del valor. Vuelve a
  escanear y prueba otra coincidencia; en barras usa el valor interno real.
- **El juego se cuelga**: cierra el juego, abre el trainer y pulsa **Restaurar originales**
  (o reinicia el juego). Los valores solo viven en memoria; no se modifica ning├║n archivo.
- **Un atajo no funciona**: otra aplicaci├│n lo tiene ocupado. Elige otra combinaci├│n en
  **Ajustes** o en el truco.

## L├¡mites conocidos

- Solo procesos x64 (el esc├íner y las direcciones asumen 8 bytes de puntero).
- No incluye *pointer scan* autom├ítico: las cadenas de punteros se pegan a mano.
- No hay firma de c├│digo: Windows SmartScreen puede pedir confirmaci├│n la primera vez.
- Pensado para un jugador. Sin soporte para anticheat ni juegos online.
## Arquitectura interna

- `Core/GameMemory.cs` y `TrainerEngine.cs`: lectura/escritura externa x64 sin inyección de DLL, congelación por tick (50 ms), `ClampToMax` y `SyncMax`.
- `Core/GameFlagsReader.cs`: resolución de banderas de partida (*Game Data Flags*) mediante tabla hash MurmurHash3 (semilla 0).
- `Core/GameMapExtractor.cs`: decodificación de texturas BRTI/ASTC/BC4 de escenarios y calabozos apoyada en `nx2_tex.dll` y `AstcSharp`.
- `UI/`: pantallas independientes (`CollectiblesPage`, `CheatsPage`, `ScannerPage`, `StatsPage`, `WaypointsPage`, `GuidePage`, `SettingsPage`) sobre `Theme.cs` y `Ui.cs`.

## Próximas mejoras (Hoja de ruta)

- **Assets visuales personalizados:** integrar marcos dorados y fondos de pizarra grabada en tarjetas, paneles y botones principales.
- **Catálogo completo de Estadísticas:** identificar y etiquetar los 23 valores restantes de la tabla de estadísticas (`0x1ADEB54D0..0x1ADEB5AD0`).
- **Direcciones de capacidad faltantes:** confirmar las direcciones de máximo (`MaxSpec`) para sacos de bombchus, palos Deku y nueces Deku.
- **Versionado semántico y Releases:** publicar versiones etiquetadas (`v1.2.0`+) en GitHub con binarios autocontenidos y notas de cambio.
- **Ampliación de coleccionables:** incorporar botellas, canciones, medallones y mejoras de equipo al rastreador en vivo.