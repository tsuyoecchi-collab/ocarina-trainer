# MEMORY.md — ocarina-trainer

## 1. Archivos EN USO
- (Ninguno)

## 2. Reglas Inamovibles y Publicación
- **Nuevos significados de estadísticas (CRÍTICO):** Cuando el usuario etiquete un valor de la tabla, **PROHIBIDO editar código, compilar o publicar**. Solo actualiza este `MEMORY.md` y `dist/estadisticas.json` (y la copia junto al `.exe` si existe), manteniendo las notas sin el texto `"(tabla de estadísticas del juego)"`.
- **Publicación (`dist/`):** Solo el Modelo Build ejecuta `.\build.ps1` tras un build final y limpia únicamente residuos (`trainer.log`, `selftest.txt`, `probe.txt`). `build.ps1` ya respalda y restaura `estadisticas.json` y `trainer-config.json` salvo que exista versión nueva.
- **Formato:** Usar herramientas `edit`/`write` (nunca `Set-Content` de PowerShell) para conservar acentos UTF-8.

## 3. Catálogo Verificado de Estadísticas (Ocarina 1.1.1 — Tabla `0x1ADEB54D0..0x1ADEB5AD0`)
- **Ya etiquetadas (10 de 33):**
  - Valor 6 (`0x1ADEB55E0`): **Enemigos derrotados**
  - Valor 10 (`0x1ADEB56A0`): **Elechos cortados**
  - Valor 11 (`0x1ADEB56D0`): **Cantidad de Saltos**
  - Valor 19 (`0x1ADEB5850`): **Cantidad de Volteretas**
  - Valor 20 (`0x1ADEB5880`): **Cantidad de Rupias Recolectadas**
  - Valor 22 (`0x1ADEB58E0`): **Ataques con espada** (también en `StatsCatalog.cs`)
  - Valor 23 (`0x1ADEB5910`): **Distancia Caminada/Nadada**
  - Valor 25 (`0x1ADEB5970`): **Distancia de Buceo**
  - Valor 28 (`0x1ADEB5A00`): **Ataques con la Resortera de las Hadas**
  - Valor 30 (`0x1ADEB5A60`): **Apuntar a Enemigos**
- **Sin etiquetar:** Valores 1–5, 7–9, 12–18, 21, 24, 26–27, 29, 31–33 (no tocar hasta confirmación del usuario).

## 4. Estado Actual de los Módulos
- **Motor y Catálogo (`Core/`):** `StatsCatalog.cs` (`Map` es `internal` para `SelfTest.cs`, soporta `DireccionMaximo` -> `MaxSpec`). `OcarinaModule.cs` enlazado a `StatsCatalog.Current` (33 entradas). `selftest` en verde (exit 0).
- **Interfaz (`UI/`):** `Theme.cs` y `Ui.cs` con paleta estilo menú OoT Remake (pergamino oscuro cálido, marcos dorados, barras superior e inferior texturizadas, checkboxes `20x20` de pizarra grabada con clic único determinista); `MainForm.cs` sin tarjeta lateral PROGRESO; `StatsPage.cs` es tabla solo lectura con columna `Actualizado` ordenada descendentemente y refrescada en `OnUiTick`; `CollectiblesPage.cs` muestra 361 coleccionables (Skulltulas, Piezas de Corazón, Frijoles Mágicos y Cofres) con badges HUD y pines en el mapa.

## 5. Decisiones Inamovibles / Direcciones
- **MOD_MAIN_BASE fijo:** `0x7000000000`.
- **Gestor de banderas (Game Data Flags):** puntero `0x7004C7E180` -> `manager` (`+0x10` count, `+0x18` entries de `0x18` bytes con estado `& 1` en `+0x16`, `+0x20` tabla `{uint32 hash, uint32 index}` usando MurmurHash3 semilla 0).
- **Posición de Link (solo lectura para mapa):** `0x7004E371D0,0x480,0x31C` (X, Y, Z Float).
- **Banderas iniciales confirmadas (hash hex -> nombre):**
  - Piezas Corazón: `1657d7d8` (Skull Kid), `1d1793e9` (Vasija Goron), `5a66513c` (Carrera Dampé), `5ff7aa30` (Skull Kid 2), `6b8fc751` (Scrub Montaña), `a666baa8` (Techo Kakariko), `e41e1597` (Scrub Campo Hyrule), `f1891f6e` (Pesca), `f2fab510` (Perro Richard).
  - Botellas/Canciones: `1106b082` (Botella), `03463ab6` (Big Poe), `170b5df2` (Carta Ruto), `53db16a6` (Fuego Azul), `01c20002` (Zelda), `d3f8bfcc` (Saria), `2b39dd4f` (Tormentas).

## 6. Pendientes para otros modelos
- **Modelo Build:** Limpiar `dist/trainer.log` pendiente en la próxima publicación oficial.

## 7. Registro de Cambios Recientes (Máx. 3 viñetas por turno)
- **`Theme.cs` / `Ui.cs` / `MainForm.cs`:** rediseño completo estilo menú OoT Remake (pergamino cálido, bordes dorados, barras superior/inferior de textura y botones de encabezado), reparación de codificación UTF-8 en `.cs` y checkboxes `20x20` de pizarra/marco dorado con `ReadOnly = true` + `CellMouseClick` para evitar modos de edición nativos rotos.
- **`gen_coleccionables_all.py` / `CollectiblesPage.cs`:** ampliado `coleccionables.json` a **361 entradas** (100 Skulltulas Doradas + 36 Piezas de Corazón + 73 Frijoles Mágicos + 152 Cofres del Tesoro) y añadido badge/icono de **Cofres** en el HUD y el mapa.
- **Publicado** `dist\Phykro-Trainer.exe` (commits `20f0373`, `5ff12f6`, `ab11ceb`); build 0 errores y sincronizado en `origin/main`.