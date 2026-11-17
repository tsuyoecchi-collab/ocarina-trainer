# AGENTS.md — ocarina-trainer

## 1. Ahorro de Tokens (Cero Verborrea)
- **Ve directo a la acción:** Prohibido meditar en exceso, explicar antes de editar o resumir después.
- **Chat ultra corto:** Máximo 3–5 líneas por respuesta. Muestra código solo si el usuario pide un plan.
- **Bitácora en `MEMORY.md`:** Máximo 3 viñetas de una línea por turno (`Archivo`, `Cambio en 10 palabras`, `Pendiente`).

## 2. Proveedores y Roles
- **Gemini (Google / Modo IA):** Asistente interactivo sin acceso directo al disco; diseña, diagnostica y entrega bloques de **PowerShell** autocontenidos (usando siempre `System.IO.File::WriteAllText` con `UTF8Encoding($true)` para preservar acentos), compila con `dotnet publish` y ejecuta `git commit`/`push` cuando el usuario lo ordena.
- **Proveedor único por sesión (agentes locales):** Usar solo modelos del proveedor activo (por defecto **Gonka**). Prohibido usar OpenRouter o modelos oficiales reservados para GitHub.
- **En Gonka:** `MiniMaxAI/MiniMax-M2.7` ejecuta cambios, compila y actualiza `MEMORY.md`. Usar `deepseek-ai/DeepSeek-V4-Flash-0731` **solo** cuando el usuario pida planificar sin editar archivos ni usar terminal.
- **Respaldo OpenCode (fuera de Gonka):** Modelo grande `opencode/deepseek-v4.1-flash-free` y modelo pequeño `opencode/glm-5.3-flash-free`.

## 3. Áreas de Propiedad y Bloqueos
- **A. UI:** `UI/` (`Theme.cs`, `Ui.cs`, `MainForm.cs`, `StatsPage.cs`, `CollectiblesPage.cs`, `ScannerPage.cs`, `WaypointsPage.cs`, `GuidePage.cs`, `SettingsPage.cs`; excepto `CheatsPage.cs` si lo usa C)
- **B. Motor:** `Core/` (`GameMemory.cs`, `TrainerEngine.cs`, `Scanner.cs`, `GameFlagsReader.cs`, `GameMapExtractor.cs`, `StatsCatalog.cs`, `SelfTest.cs`)
- **C. Juegos:** `Core/Games/`, `UI/CheatsPage.cs`
- **D. Config y Datos:** `estadisticas.json`, `trainer-config.json`, `coleccionables.json`
- **E. Docs:** `README.md`, `MEMORY.md`, `AGENTS.md`
- **F. Tools:** `tools/ValueFinder/`, `gen_coleccionables_all.py`
- **G. Build:** `build.ps1`, `nx2_tex.dll`, `dist/` (único)
- **Un archivo, un modelo:** Marca `EN USO: <archivo> — <modelo> — <hora>` en `MEMORY.md` antes de editar y bórralo al terminar. No toques áreas ajenas; anota peticiones en «Pendientes» de `MEMORY.md`. Ediciones quirúrgicas únicamente; usa `edit`/`write` (nunca `Set-Content` de PS) para cuidar UTF-8.

## 4. Flujo, Seguridad y Reglas Críticas
- **Turno:** Lee `MEMORY.md` (y `README.md` si aplica) -> marca `EN USO` -> edita -> verifica `dotnet build -c Release` y `dotnet run -c Release -- --selftest` (exit 0) -> actualiza `MEMORY.md` y libera `EN USO`.
- **Nuevos significados de estadísticas:** **PROHIBIDO editar código, compilar o publicar.** Actualiza solo `MEMORY.md` y `dist/estadisticas.json` (y copia junto al `.exe` si existe). Aplica al reconectar.
- **Build (`dist/`):** Solo Modelo Build corre `.\build.ps1` tras build final y borra solo residuos (`trainer.log`, `selftest.txt`, `probe.txt`). **Regla /dis:** NUNCA borrar `estadisticas.json`, `trainer-config.json` ni `coleccionables.json` salvo que exista versión nueva. No editar `dist/LEEME.md`.
- **Aislamiento y Privacidad:** Prohibido salir de `ocarina-trainer/`, leer `.env`/credenciales o ejecutar `git push`/`pull`/`gh` sin instrucción explícita del usuario.

## 5. Arquitectura y Comandos (`ocarina-trainer`)
- **Stack:** WinForms C# (.NET 10, x64) memoria externa sin DLL; mutex `Local\Phykro.Trainer` (`--elevated` lo omite); idioma Español; tema pergamino cálido/pizarra estilo OoT Remake (`Theme.cs`, `Ui.cs` con checkboxes `20×20` `ReadOnly` de clic único).
- **Comandos:** `dotnet build -c Release` | `dotnet run -c Release -- --selftest` (exit 0/1) | `dotnet run -c Release -- --probe ocarina.exe` (solo lectura).
- **Módulos:** `Core/` (memoria, escáner, códecs, congelación, `TrainerEngine.ClampToMax`/`SyncMax`, `GameFlagsReader` para banderas de partida, `GameMapExtractor` con `nx2_tex.dll` + `AstcSharp` para texturas BRTI/ASTC/BC4). `Core/Games/` (`IGameModule`, `OcarinaModule`, `GenericModule`). `UI/CollectiblesPage.cs` (mapa en vivo + catálogo de 361 coleccionables: 100 Skulltulas, 36 Piezas de Corazón, 73 Frijoles Mágicos, 152 Cofres). Restaura originales al salir (`RestoreOnExit`).
- **ValueFinder (`tools/ValueFinder`):** Excluido del `.csproj` principal (`dotnet build .\tools\ValueFinder\ValueFinder.csproj -c Release`). Enlaza archivos concretos de `Core` con `<Compile Link>`. `write` exige `--confirm`; sesión en `%TEMP%\phykro-find.bin`.

## 6. Direcciones Probadas y Atajos (Ocarina 1.1.1 / NX2navi 0.10.1)
- **Base y Mapa/Flags:** `MOD_MAIN_BASE` fijo `0x7000000000`; gestor de banderas `0x7004C7E180` (`+0x10` count, `+0x18` entries `0x18` B con bit 0 en `+0x16`, `+0x20` tabla MurmurHash3 semilla 0); posición Link (X, Y, Z Float) `0x7004E371D0,0x480,0x31C`.
- **Atajos:** `RegisterHotKey` (`Ctrl+Alt+R` ocupado en este PC; rupias usa `Ctrl+Alt+D`).
- **Rupias y Capacidad (`MaxSpec`):** Rupias `0x1ADD3E134` (Int32, espejo `0x1ADD3E138` automático, máx `0x1ADD3E364`=500). Capacidad bolsa (clase `0x7004C20718`): bombas `0x1ADD3E2C4`=40 (30 en partida vieja), semillas `0x1ADD3E314`=50. `0x1ADD3E2EC`=30 y `0x1ADD3E33C`=30 son palos/nueces sin desambiguar (no enlazar).
- **Munición (Int32, espejo `+4` NO sincroniza):** semillas `0x1ADD3EA44` (máx 50), palos `0x1ADD3EA6C` (máx 30), nueces `0x1ADD3EA1C` (máx 30), bombas `0x1ADD3E83C` (máx 40), bombchus `0x1ADD3E8DC` (máx 40), bulbo `0x1ADD3F00C` (sin tope). Plantillas en `UI/CheatsPage.cs` (`AddKnownCheat`, `AddAmmoCheats`).