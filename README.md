# Phykro Trainer

Trainer externo con menú de trucos para juegos de **un solo jugador** en Windows x64.
Pensado para `ocarina.exe` (TU juego single-player), pero funciona con cualquier proceso
x64 al que tengas acceso.

No inyecta DLLs: lee y escribe la memoria del proceso desde fuera, como Cheat Engine.
Incluye escáner de memoria propio, trucos con congelación de valores, atajos globales,
teletransporte por waypoints y guía integrada.

> **Uso responsable:** solo para juegos offline y copias que poseas. No lo uses en juegos
> online, con anticheat ni para saltarte protecciones. Modificar la memoria puede colgar el
> juego: haz copia de tus partidas antes de probar.

---

## Inicio rápido

1. Abre el juego.
2. Ejecuta `Phykro-Trainer.exe`.
3. El trainer detecta `ocarina` automáticamente y se conecta. Si no lo hace, elige el
   proceso en la lista y pulsa **Conectar** (o **Actualizar** si acabas de abrir el juego).
4. Si el juego se ejecuta como administrador, abre el trainer con
   **Ajustes → Reiniciar como administrador**.

La barra superior muestra `Conectado · lectura y escritura activas` cuando todo está listo.

## Coleccionables y Mapa en vivo (361 elementos)

La pestaña **Coleccionables** integra el mapa real del juego decodificado desde `.bntx`/BRTI (`GameMapExtractor` + `nx2_tex.dll` + `AstcSharp`) junto con seguimiento en tiempo real mediante banderas de partida (`GameFlagsReader` sobre `0x7004C7E180`):

- **361 coleccionables mapeados (`coleccionables.json`):** 100 Skulltulas Doradas, 36 Piezas de Corazón, 73 Frijoles Mágicos y 152 Cofres del Tesoro por zona, época y piso.
- **Radar en vivo:** lee las coordenadas X/Y/Z de Link (`0x7004E371D0,0x480,0x31C`) y dibuja su posición y orientación sobre el mapa.
- **Interfaz estilo OoT Remake:** paleta de pergamino cálido y bordes dorados (`Theme.cs`) con checkboxes `20×20` de clic único (`Ui.cs`).

## Encontrar una dirección (ejemplo: rupias)

No hay direcciones mágicas: cada partida/versión puede tener las suyas. El flujo es el
mismo que en Cheat Engine, pero integrado:

1. En el juego, apunta un valor exacto (por ejemplo, **42 rupias**).
2. En **Escáner**, elige:
   - **Tipo:** Entero 32 bits (`Int32`) para dinero, vida, munición y contadores.
   - **Comparación:** Valor exacto.
   - **Valor:** `42`.
   - **Área:** Memoria pequeña ≤ 4 MB (recomendado) o Módulo del juego.
3. Pulsa **Buscar**. Saldrán muchísimas coincidencias; es normal.
4. Vuelve al juego, gasta o gana rupias (por ejemplo, hasta **50**).
5. En el trainer, pon `50` y busca de nuevo con **Valor exacto**; o elige **Aumentó** /
   **Disminuyó** según lo que haya pasado y pulsa **Buscar** para filtrar.
6. Repite el paso 4–5 hasta quedarte con 1–5 direcciones.
7. Selecciona las filas (Ctrl o Mayús para varias) y pulsa **Añadir a trucos**.
8. En **Trucos**, escribe el valor deseado (por ejemplo `9999`), marca **Activo** y
   **Congelar**, y el trainer lo escribirá cada 50 ms.

Para la vida usa el mismo método: busca el valor visible, recibe daño y filtra con
**Disminuyó** hasta aislar la dirección; luego congélala en el máximo.

## Rupias en Ocarina Remake (probado)

Esta versión del juego (`ocarina.exe` con runtime NX2navi 0.10.1, Ocarina of Time 1.1.1)
guarda el monedero real en una dirección fija de la memoria:

| Dato | Valor |
|---|---|
| Dirección | `0x1ADD3E134` (Entero 32 bits, little-endian) |
| Copia/espejo | `0x1ADD3E138` (la sincroniza el propio juego; no hace falta tocarla) |
| Valor probado | 500 rupias |

Se comprobó cerrando y volviendo a abrir el juego (PID y base del módulo nuevos): la
dirección sigue siendo la misma y contiene las rupias de la partida cargada.

Para tenerlo listo sin escanear:

1. En **Trucos**, pulsa **Plantillas ▾**.
2. Elige **Rupias · 0x1ADD3E134 · atajo Ctrl+Alt+D**.
3. Ajusta **Valor** (por ejemplo `500`) y pulsa **Aplicar ahora**.
4. Con **Ctrl+Alt+D** activas/desactivas **Congelar**: mientras esté activo, el trainer
   reescribe el valor cada 50 ms (las rupias no bajan al gastar).

La plantilla ya trae **Máximo** `500` y la dirección del máximo (`0x1ADD3E364`): aunque
escribas `999`, el trainer lo recorta a la capacidad real del monedero y la sigue sola si
consigues una bolsa mayor (ver más abajo).

El espejo `0x1ADD3E138` no se toca: cuando cambian las rupias, el juego actualiza ambas
posiciones. Si una actualización del juego cambia la dirección, vuelve a localizarla con
el procedimiento del apartado anterior y edita el truco (doble clic en la fila).

## Munición infinita en Ocarina Remake (probado)

Los seis consumibles también tienen dirección fija (todas Int32):

| Objeto | Dirección | Valor de la sesión |
|---|---|---|
| Semillas Deku | `0x1ADD3EA44` | 27 |
| Palos Deku | `0x1ADD3EA6C` | 30 |
| Nuez Deku | `0x1ADD3EA1C` | 29 |
| Bombas | `0x1ADD3E83C` | 39 |
| Bombchu | `0x1ADD3E8DC` | 38 |
| Bulbo Mercader | `0x1ADD3F00C` | 23 |

A diferencia de las rupias, el espejo de `+4` **no** se sincroniza solo: el juego lee el
primer valor. Por eso el truco escribe únicamente esa dirección.

1. En **Trucos**, pulsa **Plantillas ▾**.
2. Elige **Munición infinita · 6 tipos**.
3. Se añaden los seis trucos con **Congelar** activo en el valor que tengan en ese momento:
   no bajan al usarlos. Para dejar de congelar, desmarca **Congelar** o cierra el trainer.

Las plantillas incluyen el **Máximo** de cada objeto (30 palos/nueces, 40 bombas/bombchu,
50 semillas, 500 rupias), así que congelar en `999` no supera la capacidad real. Bombas,
semillas, palos y nueces llevan además la **dirección del máximo** puesta: el trainer lee la
capacidad del juego y, si consigues una bolsa mayor, sube el máximo y el stock solos.
El bulbo del mercader no tiene tope porque su límite depende del comercio.

Se comprobó en vivo escribiendo valores más bajos en memoria: el trainer los devolvió a su
valor congelado en menos de un segundo. Si el juego se actualiza y deja de funcionar,
vuelve a localizar cada dirección con el Escáner y edita el truco (doble clic en la fila).

### Si no aparece nada

- Prueba **Módulo del juego** o **Toda la memoria escribible** (más lento).
- Cambia el **Tipo**: `Int16`, `Float` o `Byte`.
- En barras (corazones, energía) el valor interno puede no coincidir con el número que ves.
- Usa **Nueva búsqueda** para empezar de cero cuando cambies de valor objetivo.

### Direcciones que sobreviven al reinicio

- Las direcciones absolutas (`0x...`) pueden cambiar al cerrar el juego.
- Si la dirección cae dentro del módulo, el trainer la guarda como
  `ocarina.exe+0x1A2B3C`, que suele seguir sirviendo (el ejecutable tiene base fija).
- Para valores del montón (heap), pega una cadena de punteros de Cheat Engine:
  `ocarina.exe+0x1A2B3C,0x10,0x8`
  El trainer resuelve cada salto y escribe el valor final.

## Pantalla Estadísticas

La barra lateral incluye una página **Estadísticas**:
una tabla de solo lectura con los contadores y capacidades que declara el módulo del juego.
No hay botones de escribir: el trainer nunca modifica esos valores. La tabla se refresca
sola mientras estás conectado, y si un valor no parece real (negativo o por encima del
máximo) o la dirección ya no se lee, muestra `—`. La columna **Actualizado** registra la última vez que cambió cada valor (null = nunca). Un checkbox en la cabecera permite mostrar/ocultar la columna **Valor anterior**.

**Las estadísticas se cargan de un JSON editable, no del código.** La primera vez el
trainer crea junto al ejecutable el archivo `estadisticas.json` con 33 entradas: las 33
registros de la tabla de estadísticas del juego (como «Valor 1» … «Valor 33») y la
Skulltulas doradas ya etiquetada. Puedes renombrar, añadir, quitar o corregir direcciones,
tipos y máximos en ese archivo y **reconectar** para que se apliquen **sin recompilar**.

Formato de cada entrada:

```json
[
  { "Nombre": "Valor 1", "Direccion": "0x1ADEB54F0", "Tipo": "Int32", "Maximo": 0, "DireccionMaximo": "", "Nota": "" }
]
```

- `Nombre`: texto que se muestra en la columna «Dato».
- `Direccion`: mismas sintaxis que un truco (`0x...` absoluta, `ocarina.exe+0x...` o cadena de punteros).
- `Tipo`: `Byte`, `Int16`, `Int32`, `Int64`, `Float` o `Double`.
- `Maximo`: opcional (0 = sin tope). Si un valor lo supera o es negativo, se muestra `—`.
- `DireccionMaximo`: opcional. Dirección de memoria donde el juego guarda la capacidad real
  de este dato (p. ej. `0x1ADD3E364` para las rupias, `0x1ADD3E2C4` para las bombas).
  Si se rellena, la columna **Máximo** muestra el valor leído de memoria en vez del número
  fijo de `Maximo`. Estilo `TrainerEngine.SyncMax`: el trainer la lee cada tick y adapta
  el máximo mostrado. Vacía = usar `Maximo` fijo. Permite enlazar un máximo a otra dirección
  **sin recompilar**.
- `Nota`: texto que aparece en la columna «Detalle».

Si el archivo falta o se estropea, el trainer lo regenera con los valores por defecto. Si está
en `%APPDATA%\PhykroTrainer`, puedes abrir la carpeta desde **Ajustes → Abrir carpeta de datos**.

## Trucos

- **Activo**: el truco se tiene en cuenta.
- **Congelar**: se reescribe el valor continuamente (cada 50 ms por defecto).
- **Máximo**: tope opcional. Si se rellena, **Aplicar ahora** y **Congelar** nunca escriben
  por encima de esa cifra; el estado muestra «Limitado al máximo …». Deja la celda vacía para
  no limitar. El valor que lee el juego no se modifica: solo se recorta lo que escribe el trainer.
- **Dir. máximo**: dirección opcional donde el juego guarda la capacidad real de ese objeto
  (p. ej. `0x1ADD3E2C4` para el saco de bombas, `0x1ADD3E314` para las semillas,
  `0x1ADD3E364` para las rupías). El trainer la lee constantemente y actualiza la columna
  **Máximo** solo. Si el truco está **congelado** y el valor estaba al tope (o por encima)
  del máximo anterior, al subir la bolsa en el juego el stock se sube también al nuevo
  máximo. Ejemplo: máximo 30 bombas → el trainer recorta a 30; consigues el saco de 40 →
  el trainer pone Máximo 40 y el stock en 40.
- **Aplicar ahora**: escribe el valor una vez.
- **Leer**: muestra el valor actual en la columna **Actual**.
- **Plantillas ▾**: crea un truco con nombre, tipo y valor típicos; solo falta pegar la
  dirección encontrada. La sección **Ocarina 1.1.1 · direcciones probadas** crea el truco
  de rupias y los seis de munición ya completos, con dirección, valores, topes y, cuando se
  conoce, la dirección del máximo automática.
- **Restaurar originales**: devuelve a memoria los valores que había antes de la primera
  escritura de esta sesión.

### Límites del juego (Ocarina 1.1.1)

Estos son los topes usados por las plantillas y por la columna **Máximo**:

| Objeto | Máximo |
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
consigues una bolsa mayor, sube el **Máximo** del truco para aprovecharla. Nunca escribas
por encima de la capacidad: el contador puede desbordarse o mostrarse mal.

En la plantilla de **Munición infinita**, bombas, semillas, palos y nueces (y las rupias)
llevan la dirección del máximo puesta: el trainer actualiza el tope y el stock solo. Los
sacos de bombchus no tienen dirección de máximo confirmada en esta versión del
juego; añádeles una en **Trucos → Editar → Dir. máximo** si la localizas, o edita a mano
la columna **Máximo** (por ejemplo, `40` mientras use bolsas pequeñas).


Cada truco puede tener su **atajo** (F1, Ctrl+Alt+2, Shift+F5…). Al pulsarlo se activa o
desactiva **Congelar**. Los atajos son globales: funcionan con el juego en primer plano.

## Módulos de juego

El motor de memoria y el escáner son genéricos: no saben de ningún juego concreto. Lo que
depende de un juego (nombre del proceso, direcciones y plantillas de trucos) vive en un
**módulo** dentro de `Core/Games/`:

- `GameModule.cs` define `IGameModule`, `CheatPreset` y `CheatPresetGroup`.
- `OcarinaModule.cs` contiene las direcciones verificadas de Ocarina 1.1.1.
- `GameModules.cs` registra los módulos y elige el que corresponde al proceso del perfil.

Al pulsar **Plantillas ▾**, la sección «direcciones probadas» se genera desde el módulo del
juego activo, no con direcciones fijas en la interfaz. Para soportar otro juego basta con
crear otro `IGameModule` (proceso + plantillas) y registrarlo en `GameModules`; el escáner,
el motor de congelación y la interfaz no cambian.

## Waypoints y teletransporte

1. Encuentra tres `Float` que cambien al moverte (normalmente X, Y y Z, seguidos en memoria).
   Usa el Escáner con **Cambió** o **Disminuyó/Aumentó** mientras te mueves.
2. Pega las tres direcciones en **Waypoints → X / Y / Z** y pulsa **Guardar direcciones**.
3. Muévete a un lugar, ponle nombre y pulsa **Guardar posición** (o **F9**).
4. Selecciona el waypoint y pulsa **Teletransportar** (o **F10**).

Si el juego usa otra escala, el teletransporte puede dejarte en otro sitio: ajusta las
coordenadas o busca los valores que use internamente el juego.

## Atajos por defecto

| Atajo | Acción |
|---|---|
| `Ctrl+Alt+T` | Ocultar / mostrar el trainer |
| `Ctrl+Alt+D` | Congelar / descongelar rupias (truco de Ocarina) |
| `F9` | Guardar waypoint en la posición actual |
| `F10` | Teletransportar al waypoint seleccionado |

Se pueden cambiar en **Ajustes**, y cada truco tiene su propio atajo en **Trucos**.

## Archivos y datos

El trainer guarda todo junto al ejecutable:

- `trainer-config.json`: perfiles por juego, trucos, waypoints y atajos.
- `trainer.log`: registro de errores y conexiones.
- `selftest.txt` / `probe.txt`: informes de las pruebas de diagnóstico.

Si la carpeta del ejecutable no es escribible, usa
`%APPDATA%\PhykroTrainer`. Puedes abrir la carpeta desde **Ajustes → Abrir carpeta de datos**.

## Compilar desde el código

Requisitos: Windows 10/11 x64 y .NET SDK 10.

```powershell
cd ocarina-trainer

# Compilar
dotnet build -c Release

# Pruebas internas (sin tocar ningún juego)
dotnet run -c Release -- --selftest

# Diagnóstico de solo lectura sobre el juego en ejecución
dotnet run -c Release -- --probe ocarina.exe

# Publicar un único .exe autocontenido en dist\
.\build.ps1
```

`build.ps1` compila, ejecuta el selftest y publica
`dist\Phykro-Trainer.exe` (autocontenido: no hace falta instalar .NET en el equipo destino).

## Solución de problemas

- **No conecta**: abre el juego antes, pulsa **Actualizar** y elige el proceso.
- **Solo lectura**: el juego está elevado; reinicia el trainer como administrador.
- **No escribe / no cambia nada**: la dirección puede ser una copia del valor. Vuelve a
  escanear y prueba otra coincidencia; en barras usa el valor interno real.
- **El juego se cuelga**: cierra el juego, abre el trainer y pulsa **Restaurar originales**
  (o reinicia el juego). Los valores solo viven en memoria; no se modifica ningún archivo.
- **Un atajo no funciona**: otra aplicación lo tiene ocupado. Elige otra combinación en
  **Ajustes** o en el truco.

## Límites conocidos

- Solo procesos x64 (el escáner y las direcciones asumen 8 bytes de puntero).
- No incluye *pointer scan* automático: las cadenas de punteros se pegan a mano.
- No hay firma de código: Windows SmartScreen puede pedir confirmación la primera vez.
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