# Phykro Trainer

Trainer externo con menú de trucos y escáner de memoria para juegos de **un solo jugador** en Windows x64 (probado con `ocarina.exe`). No inyecta DLLs: lee y escribe memoria de forma externa. Incluye mapa interactivo con **361 coleccionables**, guía de estadísticas y teletransporte por waypoints.

> **Uso responsable:** solo para juegos offline y copias que poseas. Haz copia de seguridad de tus partidas antes de modificar la memoria.

---

## Funciones principales

- **Coleccionables (361 elementos):** detecta en tiempo real Skulltulas Doradas (100), Piezas de Corazón (36), Frijoles Mágicos (73) y Cofres del Tesoro (152) mediante banderas de partida (`Game Data Flags`).
- **Escáner de memoria y trucos:** buscador de direcciones por tipo (`Int32`, `Float`, etc.) con congelación de valores cada 50 ms y plantillas predefinidas para rupias y munición en *Ocarina Remake* (direcciones probadas como `0x1ADD3E134` para rupias y consumibles Deku/Bombas).
- **Estadísticas y JSON editable:** tabla de solo lectura sincronizada con `estadisticas.json` (33 registros de partida y contadores de combate/movimiento personalizables sin recompilar).
- **Waypoints y atajos globales:** guardado de coordenadas (X, Y, Z) para teletransporte rápido y atajos personalizables (F9, F10, Ctrl+Alt+D, etc.).
- **Módulos de juego:** arquitectura modular (`IGameModule` en `Core/Games/`) para adaptar el trainer a otros títulos.

## Compilación y diagnóstico

Requisitos: Windows 10/11 x64 y .NET SDK 10.

```powershell
cd ocarina-trainer
dotnet build -c Release
dotnet run -c Release -- --selftest
dotnet run -c Release -- --probe ocarina.exe
.\build.ps1 # Publica un único .exe autocontenido en dist\
```

*Nota: Para ver el documento completo con la guía detallada de direcciones, plantillas y solución de problemas, consulta el archivo fuente en el repositorio.*