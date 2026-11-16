using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace PhykroTrainer.Core;

/// <summary>
/// Pruebas internas que no tocan ningún juego: memoria propia, códecs, escáner y
/// cadenas de punteros. Se lanzan con «--selftest».
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        var lines = new List<string>();
        bool ok = true;

        void Check(string name, bool condition, string detail = "")
        {
            lines.Add($"{(condition ? "OK   " : "FALLO")} · {name}{(detail.Length > 0 ? " → " + detail : "")}");
            if (!condition) ok = false;
        }

        try
        {
            // --- Códecs ---
            Check("Int32 exacto", RoundTrip(ValueKind.Int32, "123456", "123456"));
            Check("Int32 hexadecimal", RoundTrip(ValueKind.Int32, "0x10", "16"));
            Check("Byte máximo", RoundTrip(ValueKind.Byte, "255", "255"));
            Check("Int16 negativo", RoundTrip(ValueKind.Int16, "-32768", "-32768"));
            Check("Float 3.5", RoundTrip(ValueKind.Float, "3.5", "3.5"));
            Check("Double negativo", RoundTrip(ValueKind.Double, "-1.25", "-1.25"));
            Check("Int64 grande", RoundTrip(ValueKind.Int64, "9000000000", "9000000000"));
            Check("Valor fuera de rango rechazado",
                !ValueCodec.TryParse(ValueKind.Byte, "999", out _, out _));

            // --- Direcciones ---
            Check("Dirección absoluta",
                AddressSpec.TryParse("0x7FF6A2B01234", out AddressSpec absolute, out _) &&
                absolute.IsAbsolute && absolute.Offset == 0x7FF6A2B01234);
            Check("Módulo + offset",
                AddressSpec.TryParse("ocarina.exe+0x1A2B3C", out AddressSpec module, out _) &&
                module.ModuleName == "ocarina.exe" && module.Offset == 0x1A2B3C);
            Check("Cadena de punteros",
                AddressSpec.TryParse("ocarina.exe+0x1A2B3C,0x10,0x8", out AddressSpec chain, out _) &&
                chain.Chain.Length == 2 && chain.Chain[0] == 0x10 && chain.Chain[1] == 0x8);
            Check("Dirección corta sin 0x rechazada",
                !AddressSpec.TryParse("1234", out _, out _));

            // --- Catálogo de estadísticas (JSON editable) ---
            Check("Estadísticas por defecto: 33 registros",
                StatsCatalog.BuildDefaultsForTest().Count == 33,
                $"{StatsCatalog.BuildDefaultsForTest().Count}");
            Check("La Skulltula tiene dirección y tope",
                StatsCatalog.BuildDefaultsForTest().Any(s => s.Name.Contains("Skulltula") &&
                                                             s.Spec.Length > 0 && s.Max == 100));
            foreach (GameStat def in StatsCatalog.BuildDefaultsForTest())
                Check("Formato de dirección del defecto",
                    AddressSpec.TryParse(def.Spec, out _, out _), def.Name + " → " + def.Spec);

            // --- MaxSpec (DireccionMaximo en JSON) ---
            Check("Todos los defectos tienen MaxSpec vacía",
                StatsCatalog.BuildDefaultsForTest().All(s => s.MaxSpec.Length == 0),
                $"{StatsCatalog.BuildDefaultsForTest().Count(s => s.MaxSpec.Length > 0)}");
            Check("Skulltula MaxSpec vacía",
                StatsCatalog.BuildDefaultsForTest().Any(s => s.Name.Contains("Skulltula") && s.MaxSpec.Length == 0));
            Check("Mapeo DireccionMaximo→MaxSpec (0x1ADD3E364)",
                StatsCatalog.Map(new GameStatDto { DireccionMaximo = "0x1ADD3E364" }).MaxSpec == "0x1ADD3E364");
            Check("Map: Dto sin DireccionMaximo da MaxSpec vacía",
                StatsCatalog.Map(new GameStatDto()).MaxSpec.Length == 0);

            // --- Murmur3 (GameFlagsReader) ---
            Check("Murmur3 cadena vacía", GameFlagsReader.Murmur3("") == 0);
            Check("Murmur3 «hello»", GameFlagsReader.Murmur3("hello") == 0x248bfa47);
            Check("Murmur3 «foo»", GameFlagsReader.Murmur3("foo") == 0xf6a5c420);


            // --- Extracción de mapas (GameMapExtractor / BNTX) ---
            Check("SARC inválido devuelve vacío", GameMapExtractor.ParseSarc(new byte[16], zstd: false).Count == 0);
            Check("BYML inválido devuelve null", Byml.Parse(new byte[16]) is null);
            byte[] rgba = { 10, 20, 30, 40, 50, 60, 70, 80 };
            byte[] bgra = BcCodec.FromRgba8(rgba);
            Check("RGBA8 → BGRA", bgra[0] == 30 && bgra[1] == 20 && bgra[2] == 10 && bgra[3] == 40 &&
                                  bgra[4] == 70 && bgra[5] == 60 && bgra[6] == 50 && bgra[7] == 80);

            // --- Memoria y escáner sobre nuestro propio proceso ---
            const int regionSize = 0x10000;
            IntPtr allocation = Native.VirtualAlloc(IntPtr.Zero, regionSize,
                Native.MemCommit | Native.MemReserve, Native.PageReadWrite);
            if (allocation == IntPtr.Zero)
            {
                Check("VirtualAlloc", false, "sin memoria");
            }
            else
            {
                try
                {
                    // Limpiar y sembrar valores conocidos.
                    byte[] zero = new byte[regionSize];
                    Marshal.Copy(zero, 0, allocation, zero.Length);

                    long baseAddress = allocation.ToInt64();
                    Marshal.WriteInt32(allocation + 0x100, 123456);
                    Marshal.Copy(ValueCodec.EncodeFloat(3.5f), 0, allocation + 0x200, 4);
                    Marshal.WriteInt64(allocation + 0x300, baseAddress + 0x400);
                    Marshal.WriteInt32(allocation + 0x408, 777);

                    using (GameMemory memory = GameMemory.Attach(Environment.ProcessId))
                    {
                        Check("Conectar al propio proceso", memory.IsAlive);
                        Check("Proceso x64", memory.PointerSize == 8);
                        Check("Módulo principal detectado", memory.MainModule is not null);
                        Check("Regiones escribibles enumeradas", memory.GetRegions(true).Count > 0);

                        var region = new List<MemoryRegion>
                        {
                            new(baseAddress, regionSize, Native.PageReadWrite, 0)
                        };

                        ValueCodec.TryParse(ValueKind.Int32, "123456", out byte[] target, out _);
                        ScanSession first = Scanner.FirstScan(memory, ValueKind.Int32, ScanMode.Exact, target,
                            aligned: true, ScanArea.Writable, null, CancellationToken.None, region);

                        Check("Escaneo exacto Int32", first.Count == 1, $"{first.Count} resultado(s)");
                        Check("Dirección encontrada", first.Count == 1 && first.Addresses[0] == baseAddress + 0x100,
                            first.Count == 1 ? $"0x{first.Addresses[0]:X}" : "—");

                        if (first.Count == 1)
                        {
                            Check("Lectura del valor", memory.TryRead(first.Addresses[0], new byte[4]) &&
                                                    memory.TryRead(first.Addresses[0], 4, new byte[4]));

                            byte[] updated = ValueCodec.EncodeInt(ValueKind.Int32, 123457);
                            Check("Escritura del valor", memory.TryWrite(first.Addresses[0], updated));

                            ScanSession increased = Scanner.NextScan(memory, first, ScanMode.Increased, null, null,
                                CancellationToken.None);
                            Check("Siguiente escaneo: aumentó", increased.Count == 1, $"{increased.Count} resultado(s)");

                            ScanSession unchanged = Scanner.NextScan(memory, increased, ScanMode.Unchanged, null, null,
                                CancellationToken.None);
                            Check("Siguiente escaneo: sin cambios", unchanged.Count == 1, $"{unchanged.Count} resultado(s)");

                            ValueCodec.TryParse(ValueKind.Int32, "123457", out byte[] exactTarget, out _);
                            ScanSession exact = Scanner.NextScan(memory, unchanged, ScanMode.Exact, exactTarget, null,
                                CancellationToken.None);
                            Check("Siguiente escaneo: valor exacto", exact.Count == 1, $"{exact.Count} resultado(s)");

                            // Restaurar el 123456 para la prueba de punteros.
                            memory.TryWrite(first.Addresses[0], ValueCodec.EncodeInt(ValueKind.Int32, 123456));
                        }

                        ValueCodec.TryParse(ValueKind.Float, "3.5", out byte[] floatTarget, out _);
                        ScanSession floats = Scanner.FirstScan(memory, ValueKind.Float, ScanMode.Exact, floatTarget,
                            aligned: true, ScanArea.Writable, null, CancellationToken.None, region);
                        Check("Escaneo exacto Float", floats.Count == 1, $"{floats.Count} resultado(s)");

                        string pointerSpec = $"0x{baseAddress + 0x300:X},0x8";
                        bool parsedPointer = AddressSpec.TryParse(pointerSpec, out AddressSpec spec, out string pointerError);
                        Check("Cadena de punteros se resuelve",
                            parsedPointer && spec.TryResolve(memory, out long pointed, out pointerError) &&
                            pointed == baseAddress + 0x408,
                            pointerError);

                        if (parsedPointer && spec.TryResolve(memory, out long pointedAddress, out _))
                        {
                            byte[] value = new byte[4];
                            memory.TryRead(pointedAddress, value);
                            Check("Valor a través del puntero", BinaryPrimitives.ReadInt32LittleEndian(value) == 777);
                        }

                        // --- Tope «Máximo» del motor ---
                        long cappedAddress = baseAddress + 0x500;
                        Marshal.WriteInt32((IntPtr)cappedAddress, 7);

                        var capped = new CheatTarget
                        {
                            Name = "Prueba de tope",
                            Kind = ValueKind.Int32,
                            Spec = $"0x{cappedAddress:X}",
                            Value = "999",
                            Max = "30"
                        };

                        var engine = new TrainerEngine();
                        engine.Attach(memory, new TrainerProfile { Targets = { capped } });
                        bool applied = engine.Apply(capped);

                        byte[] cappedBytes = new byte[4];
                        memory.TryRead(cappedAddress, cappedBytes);
                        int cappedValue = BinaryPrimitives.ReadInt32LittleEndian(cappedBytes);
                        Check("Valor por encima del máximo se recorta", applied && cappedValue == 30,
                            $"{cappedValue}");
                        Check("Estado de valor limitado", capped.Status.Contains("Limitado al máximo 30", StringComparison.Ordinal),
                            capped.Status);

                        capped.Value = "25";
                        engine.Apply(capped);
                        memory.TryRead(cappedAddress, cappedBytes);
                        Check("Valor bajo el máximo se escribe entero",
                            BinaryPrimitives.ReadInt32LittleEndian(cappedBytes) == 25);

                        capped.Value = "999";
                        capped.Enabled = true;
                        capped.Frozen = true;
                        engine.FreezeTick();
                        memory.TryRead(cappedAddress, cappedBytes);
                        Check("Congelar también respeta el máximo",
                            BinaryPrimitives.ReadInt32LittleEndian(cappedBytes) == 30);

                        Check("Restaurar devuelve el original tras el tope",
                            engine.RestoreAll() > 0 &&
                            (memory.TryRead(cappedAddress, cappedBytes),
                             BinaryPrimitives.ReadInt32LittleEndian(cappedBytes) == 7).Item2);

                        // --- Máximo automático leído del juego (MaxSpec) ---
                        long capAddress = baseAddress + 0x520;     // «capacidad» que guarda el juego
                        Marshal.WriteInt32((IntPtr)capAddress, 30);
                        long stockAddress = baseAddress + 0x524;
                        Marshal.WriteInt32((IntPtr)stockAddress, 39);

                        var auto = new CheatTarget
                        {
                            Name = "Auto bombas",
                            Kind = ValueKind.Int32,
                            Spec = $"0x{stockAddress:X}",
                            Value = "39",
                            Max = "40",
                            MaxSpec = $"0x{capAddress:X}",
                            Enabled = true,
                            Frozen = true
                        };

                        var autoEngine = new TrainerEngine();
                        autoEngine.Attach(memory, new TrainerProfile { Targets = { auto } });
                        bool synced = autoEngine.SyncMax(auto);
                        Check("El máximo se lee de la memoria del juego", synced && auto.Max == "30",
                            $"Max={auto.Max}");
                        Check("El stock se pone al tope del juego", auto.Value == "30",
                            $"Valor={auto.Value}");
                        autoEngine.FreezeTick();
                        memory.TryRead(stockAddress, cappedBytes);
                        Check("El stock escrito respeta el máximo del juego",
                            BinaryPrimitives.ReadInt32LittleEndian(cappedBytes) == 30);

                        Marshal.WriteInt32((IntPtr)capAddress, 40); // la bolsa crece a 40
                        autoEngine.SyncMax(auto);
                        Check("Si sube la capacidad, máximo y stock la siguen",
                            auto.Max == "40" && auto.Value == "40",
                            $"Max={auto.Max} Valor={auto.Value}");
                        autoEngine.FreezeTick();
                        memory.TryRead(stockAddress, cappedBytes);
                        Check("El stock se escribe al nuevo máximo",
                            BinaryPrimitives.ReadInt32LittleEndian(cappedBytes) == 40);

                        long underAddress = baseAddress + 0x528;
                        Marshal.WriteInt32((IntPtr)underAddress, 5);
                        var under = new CheatTarget
                        {
                            Name = "Auto bajo",
                            Kind = ValueKind.Int32,
                            Spec = $"0x{underAddress:X}",
                            Value = "5",
                            Max = "50",
                            MaxSpec = $"0x{capAddress:X}",
                            Enabled = true,
                            Frozen = true
                        };
                        var underEngine = new TrainerEngine();
                        underEngine.Attach(memory, new TrainerProfile { Targets = { under } });
                        underEngine.SyncMax(under);
                        Check("Un valor bajo no se fuerza al máximo",
                            under.Max == "40" && under.Value == "5",
                            $"Max={under.Max} Valor={under.Value}");

                        // --- GameFlagsReader: tabla hash y máscaras de bits (estructuras sintéticas) ---
                        Marshal.WriteInt32(allocation + 0x610, 1);                    // count
                        Marshal.WriteInt64(allocation + 0x618, baseAddress + 0x700); // entradas
                        Marshal.WriteInt64(allocation + 0x620, baseAddress + 0x800); // tabla hash
                        Marshal.WriteInt32(allocation + 0x62c, 1);                   // tamaño de tabla
                        Marshal.WriteInt32(allocation + 0x800, unchecked((int)0xdeadbeef));
                        Marshal.WriteInt32(allocation + 0x804, 0);

                        Marshal.WriteByte(allocation + 0x716, 1);
                        Check("Bandera por hash: activa",
                            GameFlagsReader.ReadFlagFromManager(memory, baseAddress + 0x600, 0xdeadbeef) == true);
                        Marshal.WriteByte(allocation + 0x716, 0);
                        Check("Bandera por hash: inactiva",
                            GameFlagsReader.ReadFlagFromManager(memory, baseAddress + 0x600, 0xdeadbeef) == false);
                        Check("Hash ausente devuelve null",
                            GameFlagsReader.ReadFlagFromManager(memory, baseAddress + 0x600, 0x12345678) is null);

                        Marshal.WriteInt32(allocation + 0x900, 0b0101);
                        Check("ReadBit activo",
                            GameFlagsReader.ReadBit(memory, (ulong)(baseAddress + 0x900), 0) == true &&
                            GameFlagsReader.ReadBit(memory, (ulong)(baseAddress + 0x900), 2) == true);
                        Check("ReadBit inactivo",
                            GameFlagsReader.ReadBit(memory, (ulong)(baseAddress + 0x900), 1) == false &&
                            GameFlagsReader.ReadBit(memory, (ulong)(baseAddress + 0x900), 31) == false);
                        Check("ReadBit índice inválido devuelve null",
                            GameFlagsReader.ReadBit(memory, (ulong)(baseAddress + 0x900), 32) is null);

                        // --- GameFlagsReader: sección 16 (ReadText64) ---
                        long managerText = baseAddress + 0x4000;
                        long secText = managerText + 0x600;
                        Marshal.WriteInt32((IntPtr)(secText + 0x10), 1);                          // count
                        Marshal.WriteInt64((IntPtr)(secText + 0x18), baseAddress + 0x4B00);      // entradas
                        Marshal.WriteInt64((IntPtr)(secText + 0x20), baseAddress + 0x4D00);      // tabla hash
                        Marshal.WriteInt32((IntPtr)(secText + 0x2c), 1);                         // tamaño tabla
                        Marshal.WriteInt32((IntPtr)(baseAddress + 0x4D00),
                            unchecked((int)GameFlagsReader.Murmur3("Sequence_CurrentBanc")));
                        Marshal.WriteInt32((IntPtr)(baseAddress + 0x4D04), 0);                   // índice 0
                        Marshal.WriteInt64((IntPtr)(baseAddress + 0x4B00 + 0x68), baseAddress + 0x4E00); // textPtr
                        byte[] textBytes = Encoding.ASCII.GetBytes("vr_SPRT\0");
                        Marshal.Copy(textBytes, 0, allocation + 0x4E00, textBytes.Length);

                        Check("ReadText64 lee la cadena",
                            GameFlagsReader.ReadTextFromSection(memory, managerText, "Sequence_CurrentBanc") == "vr_SPRT");
                        Check("ReadText64 hash ausente devuelve null",
                            GameFlagsReader.ReadTextFromSection(memory, managerText, "OtroNombre") is null);

                        GameModule main = memory.MainModule!.Value;
                        string relative = AddressSpec.Describe(main.Base + 0x100, main);
                        Check("Dirección relativa al módulo", relative.Contains("+0x100"), relative);

                        bool moduleParsed = AddressSpec.TryParse($"{main.Name}+0x1000", out AddressSpec moduleSpec, out _);
                        Check("Módulo + offset se resuelve",
                            moduleParsed && moduleSpec.TryResolve(memory, out long moduleAddress, out _) &&
                            moduleAddress == main.Base + 0x1000);

                        // Escape de la propia región (sin escribir en el juego).
                        byte[] header = new byte[2];
                        Check("Lectura del encabezado del módulo",
                            memory.TryRead(main.Base, header) && header[0] == (byte)'M' && header[1] == (byte)'Z');
                    }
                }
                finally
                {
                    Native.VirtualFree(allocation, 0, 0x8000); // MEM_RELEASE
                }
            }
        }
        catch (Exception ex)
        {
            ok = false;
            lines.Add("FALLO · excepción: " + ex);
            Log.Error("selftest", ex);
        }

        lines.Add("");
        lines.Add(ok ? "RESULTADO: TODO CORRECTO" : "RESULTADO: HAY FALLOS");

        string report = string.Join(Environment.NewLine, lines);
        try
        {
            File.WriteAllText(Path.Combine(Paths.DataDir, "selftest.txt"), report, Encoding.UTF8);
        }
        catch
        {
            // sin informe no se puede hacer más
        }

        Log.Write("Selftest: " + (ok ? "correcto" : "con fallos"));
        return ok ? 0 : 1;
    }

    /// <summary>Diagnóstico de solo lectura sobre un proceso real (--probe ocarina.exe).</summary>
    public static int Probe(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase));
        string name = index >= 0 && index + 1 < args.Length ? args[index + 1] : "ocarina.exe";
        bool dumpTable = index + 2 < args.Length && args[index + 1].Equals("--table", StringComparison.OrdinalIgnoreCase);
        if (dumpTable) name = index + 2 < args.Length ? args[index + 2] : "ocarina.exe";
        string bareName = Path.GetFileNameWithoutExtension(name);

        var lines = new List<string>();
        int exitCode = 0;

        try
        {
            Process? process = Process.GetProcessesByName(bareName)
                .OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero)
                .FirstOrDefault();

            if (process is null)
            {
                lines.Add($"No hay ningún proceso «{bareName}» en ejecución.");
                exitCode = 2;
            }
            else
            {
                using GameMemory memory = GameMemory.Attach(process.Id);
                lines.Add($"Proceso: {memory.ProcessName} (PID {memory.ProcessId})");
                lines.Add($"x64: {memory.Is64Bit} · acceso de escritura: {memory.HasWriteAccess}");
                lines.Add($"Módulo: {memory.MainModule}");

                List<MemoryRegion> writable = memory.GetRegions(true);
                List<MemoryRegion> all = memory.GetRegions(false);
                lines.Add($"Regiones legibles: {all.Count} ({all.Sum(r => r.Size) / (1024.0 * 1024.0):N0} MB)");
                lines.Add($"Regiones escribibles: {writable.Count} ({writable.Sum(r => r.Size) / (1024.0 * 1024.0):N0} MB)");
                foreach (var group in all.GroupBy(r => r.Type))
                {
                    lines.Add($"  Tipo 0x{group.Key:X}: {group.Count()} regiones, " +
                              $"{group.Sum(r => r.Size) / (1024.0 * 1024.0):N0} MB");
                }

                lines.Add("  Regiones más grandes:");
                foreach (MemoryRegion big in all.OrderByDescending(r => r.Size).Take(8))
                {
                    lines.Add($"    0x{big.Base:X}  {big.Size / (1024.0 * 1024.0):N0} MB  " +
                              $"tipo 0x{big.Type:X}  protección 0x{big.Protect:X}");
                }

                foreach (long limitMb in new long[] { 1, 8, 32, 128, 512 })
                {
                    long limit = limitMb * 1024 * 1024;
                    var small = writable.Where(r => r.Size <= limit).ToList();
                    lines.Add($"  Escribibles ≤ {limitMb} MB: {small.Count:N0} regiones, " +
                              $"{small.Sum(r => r.Size) / (1024.0 * 1024.0):N0} MB");
                }

                GameModule? main = memory.MainModule;
                if (main is not null)
                {
                    byte[] header = new byte[2];
                    bool headerOk = memory.TryRead(main.Value.Base, header);
                    lines.Add($"Cabecera del módulo: {(headerOk ? Encoding.ASCII.GetString(header) : "ilegible")}");
                }

                // --- Tabla de estadísticas: volcado de los 33 registros ---
                if (dumpTable)
                {
                    const long tableBase = 0x1ADEB54D0;
                    const int stride = 0x30;
                    const int rowCount = 33;
                    const int fieldCount = 12; // 0x30 / 4
                    const long skullRow = 0x1ADEB5620; // 8.º registro (índice 7)

                    lines.Add("");
                    lines.Add($"=== VOLCADO TABLA ESTADÍSTICAS (solo lectura) ===");
                    lines.Add($"Base: 0x{tableBase:X}  Filas: {rowCount}  Stride: 0x{stride:X} ({stride})");
                    lines.Add("");

                    byte[] tableBuffer = new byte[rowCount * stride];
                    bool tableRead = memory.TryRead(tableBase, tableBuffer);
                    lines.Add($"Lectura de {rowCount * stride} bytes (@ 0x{tableBase:X}): {(tableRead ? "OK" : "FALLO")}");

                    if (tableRead)
                    {
                        // Encabezados de columna
                        var offsetHeaders = new List<string> { "Fila" };
                        for (int o = 0; o < fieldCount; o++) offsetHeaders.Add($"+0x{o * 4:X2}");
                        offsetHeaders.Add("Valor en +0x20");
                        lines.Add(string.Join("  ", offsetHeaders));

                        for (int row = 0; row < rowCount; row++)
                        {
                            long rowBase = row * stride;
                            var cells = new List<string> { $"[{row:D2}]" };

                            for (int field = 0; field < fieldCount; field++)
                            {
                                int cellOffset = (int)(rowBase + field * 4);
                                int value = BitConverter.ToInt32(tableBuffer, cellOffset);
                                cells.Add($"0x{value:X8}");
                            }

                            // Valor en +0x20 como referencia
                            int valueAt20 = BitConverter.ToInt32(tableBuffer, (int)(rowBase + 0x20));
                            cells.Add($"→ {valueAt20}");

                            lines.Add(string.Join("  ", cells));

                            // Señalar la fila de Skulltulas
                            if (tableBase + rowBase == skullRow)
                            {
                                lines.Add($"  *** SKULLTULA (推测) — valor=0x{valueAt20:X} ({valueAt20}) ***");
                            }
                        }

                        // Buscar en la tabla valores que coincidan con los máximos conocidos de consumibles
                        lines.Add("");
                        lines.Add("=== BÚSQUEDA DE OFFSETS DE MÁXIMO ===");
                        // Known max values from the game: bombs=40(0x28), seeds=50(0x32), rupees=500(0x1F4)
                        var knownMaxes = new (string name, int value, int row)[]
                        {
                            ("rupias (max 500)", 500, -1),
                            ("bombas (max 40)", 40, -1),
                            ("semillas (max 50)", 50, -1),
                        };

                        for (int row = 0; row < rowCount; row++)
                        {
                            long rowBase = row * stride;
                            for (int field = 0; field < fieldCount; field++)
                            {
                                int cellOffset = (int)(rowBase + field * 4);
                                int value = BitConverter.ToInt32(tableBuffer, cellOffset);
                                foreach (var km in knownMaxes)
                                {
                                    if (value == km.value)
                                    {
                                        long absoluteAddr = tableBase + cellOffset;
                                        lines.Add($"  Posible máximo: {km.name} — fila [{row}], offset +0x{field * 4:X2}, valor={value}, dirección tabla=0x{absoluteAddr:X}");
                                    }
                                }
                            }
                        }
                    }
                }

                var progressTotal = new Stopwatch();
                progressTotal.Start();
                ScanSession scan = Scanner.FirstScan(memory, ValueKind.Int32, ScanMode.Exact,
                    ValueCodec.EncodeInt(ValueKind.Int32, 0x7F3A9C21), aligned: true,
                    ScanArea.MainModule, null, CancellationToken.None);
                progressTotal.Stop();

                lines.Add($"Escaneo de prueba del módulo: {scan.Count} coincidencias en {progressTotal.ElapsedMilliseconds} ms");

                ScanSession next = Scanner.NextScan(memory, scan, ScanMode.Unchanged, null, null, CancellationToken.None);
                lines.Add($"Siguiente escaneo: {next.Count} coincidencias");

                List<MemoryRegion> fast = Scanner.SelectRegions(memory, ScanArea.Fast);
                lines.Add($"Área rápida: {fast.Count:N0} regiones, " +
                          $"{fast.Sum(r => r.Size) / (1024.0 * 1024.0):N0} MB");

                var fastWatch = Stopwatch.StartNew();
                ScanSession fastScan = Scanner.FirstScan(memory, ValueKind.Int32, ScanMode.Exact,
                    ValueCodec.EncodeInt(ValueKind.Int32, 0x7F3A9C21), aligned: true, ScanArea.Fast, null,
                    CancellationToken.None);
                fastWatch.Stop();
                lines.Add($"Escaneo de área rápida: {fastScan.Count} coincidencias en {fastWatch.ElapsedMilliseconds} ms");
            }
        }
        catch (Exception ex)
        {
            lines.Add("ERROR: " + ex.Message);
            Log.Error("probe", ex);
            exitCode = 1;
        }

        string report = string.Join(Environment.NewLine, lines);
        try
        {
            File.WriteAllText(Path.Combine(Paths.DataDir, "probe.txt"), report, Encoding.UTF8);
        }
        catch
        {
            // ignorar
        }

        Console.WriteLine(report);
        return exitCode;
    }

    private static bool RoundTrip(ValueKind kind, string input, string expected)
    {
        if (!ValueCodec.TryParse(kind, input, out byte[] bytes, out _)) return false;
        return ValueCodec.Format(kind, bytes) == expected;
    }
}
