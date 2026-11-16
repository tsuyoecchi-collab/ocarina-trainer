using System.Diagnostics;
using System.Globalization;
using System.Text;
using PhykroTrainer.Core;

namespace PhykroTrainer.Tools;

/// <summary>
/// Herramienta de apoyo (solo lectura) para localizar valores con el mismo motor
/// del trainer. Guarda los candidatos entre pasos para poder filtrar varias veces.
/// </summary>
internal static class Program
{
    private const string Magic = "PFND1";
    private static readonly string DefaultSession =
        Path.Combine(Path.GetTempPath(), "phykro-find.bin");

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            if (args.Length == 0) return Usage();

            return args[0].ToLowerInvariant() switch
            {
                "find" => Find(args),
                "next" => Next(args),
                "show" => Show(args),
                "test" => Test(args),
                "dump" => Dump(args),
                "write" => WriteValue(args),
                "watch" => Watch(args),
                "poke" => Poke(args),
                "ptrs" => Ptrs(args),
                "sig" => Sig(args),
                "str" => StrFind(args),
                "pat" => PatternFind(args),
                "pages" => Pages(args),
                "snapshot" => Snapshot(args),
                _ => Usage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("PhykroFinder · búsqueda de valores de solo lectura");
        Console.WriteLine();
        Console.WriteLine("  find <proceso> <tipo> <valor> [--area fast|module|writable|all]");
        Console.WriteLine("       [--session archivo] [--unaligned] [--limit N]");
        Console.WriteLine("  next <proceso> <tipo> <valor|-> <exact|greater|less|notequal|");
        Console.WriteLine("       increased|decreased|changed|unchanged> --session archivo [--limit N]");
        Console.WriteLine("  show <sesión> [--limit N]");
        Console.WriteLine("  test <proceso> <tipo> <dirección>");
        Console.WriteLine("  dump <proceso> <dirección> [--before N] [--after N]");
        Console.WriteLine("  write <proceso> <tipo> <dirección> <valor> --confirm");
        Console.WriteLine("  watch <sesión> [--process nombre] [--seconds N] [--ms N]");
        Console.WriteLine("  poke <proceso> <tipo> <sesión> <índice> <valor> [--seconds N] [--ms N] [--restore]");
        Console.WriteLine("  ptrs <proceso> <dirección> [--range N] [--from 0x…] [--size 0x…] [--limit N]");
        Console.WriteLine("  sig <proceso> [--min N] [--max N] [--class 0x…] [--from 0x…] [--size 0x…] [--limit N]");
        Console.WriteLine("  snapshot <proceso> <archivo> [--from 0x…] [--size 0x…]");
        Console.WriteLine("  str <proceso> <texto> [--utf16] [--limit N] [--context N]");
        Console.WriteLine("  pat <proceso> <hex> [--limit N] [--context N]");
        Console.WriteLine("  pages <proceso> <archivo> [--page 0x1000]");
        Console.WriteLine();
        Console.WriteLine("Tipos: byte, int16, int32, int64, float, double");
        Console.WriteLine("Ejemplo: find ocarina int32 500 --area fast");
        return 1;
    }

    private static int Find(string[] args)
    {
        if (args.Length < 4) return Usage();

        string processName = args[1];
        ValueKind kind = ParseKind(args[2]);
        string valueText = args[3];
        ScanArea area = ParseArea(Option(args, "--area") ?? "fast");
        bool aligned = !Has(args, "--unaligned");
        string sessionPath = Option(args, "--session") ?? DefaultSession;
        int limit = ParseInt(Option(args, "--limit"), 40);

        if (!ValueCodec.TryParse(kind, valueText, out byte[] target, out string error))
            throw new ArgumentException(error);

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, area);

        long estimate = Scanner.EstimateBytes(memory, area);
        Console.WriteLine($"Área: {area} · {estimate / (1024.0 * 1024.0):N0} MB · " +
                          $"buscando {valueText} ({kind}{(aligned ? ", alineado" : "")})…");

        var watch = Stopwatch.StartNew();
        ScanSession session = Scanner.FirstScan(memory, kind, ScanMode.Exact, target, aligned,
            area, null, CancellationToken.None);
        watch.Stop();

        Save(sessionPath, session);
        Console.WriteLine($"Primera búsqueda: {session.Count:N0} candidato(s) en {watch.ElapsedMilliseconds:N0} ms.");
        PrintCandidates(memory, session, limit);

        if (session.Count == 0)
        {
            Console.WriteLine("No hay coincidencias. Prueba otro tipo (int16, byte, float) u otra área.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Ahora cambia el valor en el juego (gasta o gana) y ejecuta:");
            Console.WriteLine($"  next {processName} {KindName(kind)} <valor nuevo> exact --session \"{sessionPath}\"");
        }

        return 0;
    }

    private static int Next(string[] args)
    {
        if (args.Length < 5) return Usage();

        string processName = args[1];
        ValueKind kind = ParseKind(args[2]);
        string valueText = args[3];
        ScanMode mode = ParseMode(args[4]);
        string sessionPath = Option(args, "--session") ?? DefaultSession;
        int limit = ParseInt(Option(args, "--limit"), 40);

        ScanSession previous = Load(sessionPath);
        if (previous.Kind != kind)
            throw new ArgumentException($"La sesión es de tipo {previous.Kind}, no {kind}.");

        byte[]? target = null;
        if (Scanner.NeedsValue(mode))
        {
            if (valueText is "-" or "")
                throw new ArgumentException("Ese modo necesita un valor.");
            if (!ValueCodec.TryParse(kind, valueText, out target, out string error))
                throw new ArgumentException(error);
        }

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);
        Console.WriteLine($"Filtrando {previous.Count:N0} candidato(s) con {ModeName(mode)}" +
                          (target is null ? "…" : $" = {valueText}…"));

        var watch = Stopwatch.StartNew();
        ScanSession next = Scanner.NextScan(memory, previous, mode, target, null, CancellationToken.None);
        watch.Stop();

        Save(sessionPath, next);
        Console.WriteLine($"Filtro: {next.Count:N0} candidato(s) en {watch.ElapsedMilliseconds:N0} ms.");
        PrintCandidates(memory, next, limit);

        if (next.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine("Cambia otra vez el valor y repite el filtro para reducirlo más:");
            Console.WriteLine($"  next {processName} {KindName(kind)} <valor nuevo> exact --session \"{sessionPath}\"");
        }
        else if (next.Count == 1)
        {
            Console.WriteLine();
            Console.WriteLine("¡Candidato único! Compruébalo con:");
            Console.WriteLine($"  test {processName} {KindName(kind)} \"{Describe(memory, next.Addresses[0])}\"");
        }

        return 0;
    }

    private static int Show(string[] args)
    {
        if (args.Length < 2) return Usage();

        string sessionPath = args[1];
        int limit = ParseInt(Option(args, "--limit"), 40);
        ScanSession session = Load(sessionPath);

        Console.WriteLine($"Sesión: {sessionPath}");
        Console.WriteLine($"Tipo: {KindName(session.Kind)} · candidatos: {session.Count:N0}");
        try
        {
            using GameMemory memory = Attach(Option(args, "--process") ?? "ocarina");
            PrintHeader(memory, null);
            PrintCandidates(memory, session, limit);
        }
        catch (Exception ex)
        {
            Console.WriteLine("(Sin proceso para leer los valores actuales: " + ex.Message + ")");
        }

        return 0;
    }

    private static int Test(string[] args)
    {
        if (args.Length < 4) return Usage();

        string processName = args[1];
        ValueKind kind = ParseKind(args[2]);
        string specText = args[3];

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);

        if (!AddressSpec.TryParse(specText, out AddressSpec spec, out string error))
            throw new ArgumentException(error);
        if (!spec.TryResolve(memory, out long address, out error))
            throw new ArgumentException(error);

        byte[] buffer = new byte[ValueCodec.Size(kind)];
        if (!memory.TryRead(address, buffer))
            throw new ArgumentException("La dirección no se puede leer.");

        Console.WriteLine($"Dirección: {spec}");
        Console.WriteLine($"Resuelta:  0x{address:X}  ({Describe(memory, address)})");
        Console.WriteLine($"Valor:     {ValueCodec.Format(kind, buffer)}");
        return 0;
    }

    // ---------------------------------------------------------------- utilidades

    private static int Dump(string[] args)
    {
        if (args.Length < 3) return Usage();

        string processName = args[1];
        string specText = args[2];
        int before = ParseInt(Option(args, "--before"), 64);
        int after = ParseInt(Option(args, "--after"), 96);

        using GameMemory memory = Attach(processName);
        if (!AddressSpec.TryParse(specText, out AddressSpec spec, out string error) ||
            !spec.TryResolve(memory, out long address, out error))
            throw new ArgumentException(error);

        Console.WriteLine($"Proceso: {memory.ProcessName} (PID {memory.ProcessId})");
        Console.WriteLine($"Centro: 0x{address:X}  ({Describe(memory, address)})");
        Console.WriteLine("Offset      Dirección            Int32        Float        Hex");
        Console.WriteLine("----------------------------------------------------------------");

        long start = (address - before) & ~3L;
        long end = (address + after) & ~3L;
        for (long cursor = start; cursor <= end; cursor += 4)
        {
            byte[] buffer = new byte[4];
            if (!memory.TryRead(cursor, 4, buffer))
            {
                Console.WriteLine($"+{cursor - address,6:+#;-#;0}  0x{cursor:X}  (no legible)");
                continue;
            }

            int intValue = BitConverter.ToInt32(buffer, 0);
            float floatValue = BitConverter.ToSingle(buffer, 0);
            string marker = cursor == address ? " <==" : "";
            Console.WriteLine($"+{cursor - address,6:+#;-#;0}  0x{cursor:X}  {intValue,12}  {floatValue,10:0.###}  " +
                              $"{Convert.ToHexString(buffer)}{marker}");
        }

        return 0;
    }

    private static int WriteValue(string[] args)
    {
        if (args.Length < 5) return Usage();
        if (!Has(args, "--confirm"))
            throw new ArgumentException("Escribir memoria requiere añadir --confirm.");

        string processName = args[1];
        ValueKind kind = ParseKind(args[2]);
        string specText = args[3];
        string valueText = args[4];

        using GameMemory memory = Attach(processName);
        if (!AddressSpec.TryParse(specText, out AddressSpec spec, out string error) ||
            !spec.TryResolve(memory, out long address, out error))
            throw new ArgumentException(error);
        if (!ValueCodec.TryParse(kind, valueText, out byte[] bytes, out error))
            throw new ArgumentException(error);

        byte[] original = new byte[bytes.Length];
        bool read = memory.TryRead(address, original);
        string oldText = read ? ValueCodec.Format(kind, original) : "(ilegible)";

        if (!memory.TryWrite(address, bytes))
            throw new InvalidOperationException("No se pudo escribir la dirección.");

        byte[] check = new byte[bytes.Length];
        memory.TryRead(address, check);
        Console.WriteLine($"Dirección: {spec}");
        Console.WriteLine($"Resuelta:  0x{address:X}  ({Describe(memory, address)})");
        Console.WriteLine($"Antes:     {oldText}{(read ? $"  ({Convert.ToHexString(original)})" : "")}");
        Console.WriteLine($"Ahora:     {ValueCodec.Format(kind, check)}  ({Convert.ToHexString(check)})");
        return 0;
    }

    private static int Watch(string[] args)
    {
        if (args.Length < 2) return Usage();

        string sessionPath = args[1];
        ScanSession session = Load(sessionPath);
        if (session.Count == 0) throw new ArgumentException("La sesión no tiene candidatos.");
        string processName = Option(args, "--process") ?? "ocarina";
        int seconds = ParseInt(Option(args, "--seconds"), 10);
        int interval = ParseInt(Option(args, "--ms"), 250);

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);
        Console.WriteLine($"Vigilando {session.Count} dirección(es) durante {seconds} s cada {interval} ms…");
        Sample(memory, session, null, null, seconds * 1000 / interval, interval);
        return 0;
    }

    private static int Poke(string[] args)
    {
        if (args.Length < 6) return Usage();

        string processName = args[1];
        ValueKind kind = ParseKind(args[2]);
        string sessionPath = args[3];
        if (!int.TryParse(args[4], out int index) || index < 0)
            throw new ArgumentException("El índice debe ser un número.");
        string valueText = args[5];
        int seconds = ParseInt(Option(args, "--seconds"), 3);
        int interval = ParseInt(Option(args, "--ms"), 200);

        ScanSession session = Load(sessionPath);
        if (session.Kind != kind)
            throw new ArgumentException($"La sesión es de tipo {session.Kind}, no {kind}.");
        if (index >= session.Count) throw new ArgumentException("Índice fuera de la sesión.");
        if (!ValueCodec.TryParse(kind, valueText, out byte[] bytes, out string error))
            throw new ArgumentException(error);

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);

        long address = session.Addresses[index];
        int size = ValueCodec.Size(kind);
        byte[] original = new byte[size];
        bool read = memory.TryRead(address, original);

        Console.WriteLine($"Escribiendo {valueText} en el candidato #{index} 0x{address:X} " +
                          $"(antes: {(read ? ValueCodec.Format(kind, original) : "?")}), y observando {seconds} s…");
        if (!memory.TryWrite(address, bytes))
            throw new InvalidOperationException("No se pudo escribir la dirección.");

        Sample(memory, session, address, original, seconds * 1000 / interval, interval);

        if (Has(args, "--restore") && read)
        {
            memory.TryWrite(address, original);
            Console.WriteLine($"Restaurado 0x{address:X} a {ValueCodec.Format(kind, original)}.");
        }

        return 0;
    }

    // Muestrea todas las direcciones de la sesión; imprime solo los cambios, marcando la dirección escrita.
    private static void Sample(GameMemory memory, ScanSession session, long? written, byte[]? original, int samples,
                               int interval)
    {
        int size = session.ValueSize;
        byte[] buffer = new byte[size];
        string[] last = new string[session.Count];
        Array.Fill(last, "");
        int shown = Math.Min(session.Count, 16);

        for (int sample = 0; sample <= samples; sample++)
        {
            var changes = new List<string>();
            for (int i = 0; i < session.Count; i++)
            {
                long address = session.Addresses[i];
                string text = memory.TryRead(address, size, buffer)
                    ? ValueCodec.Format(session.Kind, buffer)
                    : "?";
                if (text == last[i]) continue;
                string mark = written == address ? " *" : "";
                changes.Add($"0x{address:X}{mark} {last[i]} -> {text}");
                last[i] = text;
            }

            if (changes.Count > 0)
                Console.WriteLine($"[{sample * (long)interval,5} ms] " + string.Join(" | ", changes));

            if (sample < samples) Thread.Sleep(interval);
        }
    }

    // Busca punteros (valores de 8 bytes) que apunten a la dirección dada o a su objeto.
    // Con --range N acepta valores v con 0 <= direccion - v <= N (puntero a la base del objeto).
    private static int Ptrs(string[] args)
    {
        if (args.Length < 3) return Usage();

        string processName = args[1];
        if (!TryParseHex(args[2], out long target))
            throw new ArgumentException($"Dirección no válida: {args[2]}");

        long range = TryParseHex(Option(args, "--range") ?? "0", out long r) ? r : 0;
        int limit = ParseInt(Option(args, "--limit"), 200);

        long scanFrom = 0, scanSize = 0;
        if (Option(args, "--from") is { } fromText)
        {
            if (!TryParseHex(fromText, out scanFrom)) throw new ArgumentException("--from no válido.");
            if (!TryParseHex(Option(args, "--size") ?? "0", out scanSize) || scanSize <= 0)
                throw new ArgumentException("Con --from hace falta --size.");
        }

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);
        Console.WriteLine(range == 0
            ? $"Buscando punteros exactos a 0x{target:X}…"
            : $"Buscando punteros a 0x{target:X} con desplazamiento 0..0x{range:X}…");

        List<MemoryRegion> regions = scanSize > 0
            ? [new MemoryRegion(scanFrom, scanSize, 0, 0)]
            : memory.GetRegions(true);

        long scanned = regions.Sum(x => x.Size);
        Console.WriteLine($"Regiones: {regions.Count:N0} · {scanned / (1024.0 * 1024.0):N0} MB");

        var hits = new List<(long At, long Value)>();
        var watch = Stopwatch.StartNew();
        foreach (MemoryRegion region in regions)
        {
            long start = (region.Base + 7) & ~7L;
            long end = region.Base + region.Size;
            ScanPointers(memory, start, end, target, range, hits, limit);
        }
        watch.Stop();

        hits.Sort((a, b) => a.At.CompareTo(b.At));
        Console.WriteLine($"Punteros encontrados: {hits.Count:N0} en {watch.ElapsedMilliseconds:N0} ms.");
        foreach ((long at, long value) in hits.Take(limit))
        {
            long offset = target - value;
            Console.WriteLine($"  en 0x{at:X} -> 0x{value:X}  (objeto +0x{offset:X})  [{Describe(memory, at)}]");
        }
        if (hits.Count > limit)
            Console.WriteLine($"  … y {hits.Count - limit:N0} más. Usa --limit N para ver más.");

        return 0;
    }

    private static void ScanPointers(GameMemory memory, long start, long end, long target, long range,
        List<(long At, long Value)> hits, int limit)
    {
        const int chunk = 1 << 20;
        byte[] buffer = new byte[chunk];
        for (long cursor = start; cursor + 8 <= end; cursor += chunk)
        {
            int size = (int)Math.Min(chunk, end - cursor);
            if (!memory.TryRead(cursor, size, buffer))
            {
                if (size > 64 * 1024)
                {
                    int half = (size / 2) & ~7;
                    ScanPointers(memory, cursor, cursor + half, target, range, hits, limit);
                    ScanPointers(memory, cursor + half, cursor + size, target, range, hits, limit);
                }
                continue;
            }

            for (int offset = 0; offset + 8 <= size; offset += 8)
            {
                long value = BitConverter.ToInt64(buffer, offset);
                if (value < 0x10000 || value > 0x7FFF_FFFF_FFFF) continue;

                bool match = range == 0
                    ? value == target
                    : value <= target && target - value <= range;
                if (!match) continue;

                hits.Add((cursor + offset, value));
                if (hits.Count >= limit * 20) return;
            }
        }
    }

    // Busca el registro de guardado que contiene un par de int32 iguales (valor + espejo).
    // Cada registro mide 0x28 bytes: puntero de clase, tamaño 8, flag 1, tres ceros,
    // el valor, su copia y un cero. Devuelve la dirección del valor (registro + 0x1C).
    private static int Sig(string[] args)
    {
        if (args.Length < 2) return Usage();

        string processName = args[1];
        int min = ParseInt(Option(args, "--min"), 0);
        int max = ParseInt(Option(args, "--max"), 9999);
        long? onlyClass = TryParseHex(Option(args, "--class") ?? "", out long cls) ? cls : null;
        int limit = ParseInt(Option(args, "--limit"), 200);
        bool anyMirror = Has(args, "--any-mirror");

        long scanFrom = 0x100000000, scanSize = 0x380000000;   // bloque de pila y montón del invitado
        if (Option(args, "--from") is { } fromText)
        {
            if (!TryParseHex(fromText, out scanFrom)) throw new ArgumentException("--from no válido.");
            if (!TryParseHex(Option(args, "--size") ?? "0", out scanSize) || scanSize <= 0)
                throw new ArgumentException("Con --from hace falta --size.");
        }
        long scanTo = scanFrom + scanSize;

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);

        var regions = new List<MemoryRegion>();
        foreach (MemoryRegion region in memory.GetRegions(true))
        {
            long start = Math.Max(region.Base, scanFrom);
            long end = Math.Min(region.Base + region.Size, scanTo);
            if (end - start >= 0x30) regions.Add(new MemoryRegion(start, end - start, region.Protect, region.Type));
        }

        Console.WriteLine($"Buscando registros con par de valores {min}..{max} " +
                          $"en {regions.Count:N0} región(es)…");

        var hits = new List<(long ValueAddress, int Value, long Class)>();
        var watch = Stopwatch.StartNew();
        foreach (MemoryRegion region in regions)
        {
            long cursor = (region.Base + 7) & ~7L;
            long end = region.Base + region.Size;
            const int chunk = 1 << 20;
            byte[] buffer = new byte[chunk + 0x40];

            while (cursor + 0x30 <= end)
            {
                int size = (int)Math.Min(chunk, end - cursor);
                if (!memory.TryRead(cursor, size, buffer))
                {
                    cursor += 0x1000;   // salta la página ilegible
                    continue;
                }

                for (int off = 0; off + 0x30 <= size; off += 8)
                {
                    long c = BitConverter.ToInt64(buffer, off);
                    if (onlyClass is { } want ? c != want : (c < 0x7000000000 || c >= 0x7008000000)) continue;

                    long p = cursor + off;
                    if (BitConverter.ToInt32(buffer, off + 0x08) != 8) continue;
                    if (BitConverter.ToInt32(buffer, off + 0x0C) != 1) continue;
                    if (BitConverter.ToInt32(buffer, off + 0x10) != 0) continue;
                    if (BitConverter.ToInt32(buffer, off + 0x14) != 0) continue;
                    if (BitConverter.ToInt32(buffer, off + 0x18) != 0) continue;
                    if (BitConverter.ToInt32(buffer, off + 0x24) != 0) continue;

                    int value = BitConverter.ToInt32(buffer, off + 0x1C);
                    if (value < min || value > max) continue;
                    if (!anyMirror && BitConverter.ToInt32(buffer, off + 0x20) != value) continue;

                    long next = BitConverter.ToInt64(buffer, off + 0x28);
                    if (next < 0x7000000000 || next >= 0x7008000000) continue;

                    hits.Add((p + 0x1C, value, c));
                    if (hits.Count >= limit * 20) break;
                }

                if (hits.Count >= limit * 20) break;
                cursor += size;
            }
        }
        watch.Stop();

        Console.WriteLine($"Candidatos: {hits.Count:N0} en {watch.ElapsedMilliseconds:N0} ms.");
        foreach ((long valueAddress, int value, long c) in hits.Take(limit))
            Console.WriteLine($"  0x{valueAddress:X}  valor {value,6}  clase 0x{c:X}");
        if (hits.Count > limit)
            Console.WriteLine($"  … y {hits.Count - limit:N0} más. Usa --limit N para ver más.");

        return 0;
    }

    private static int Snapshot(string[] args)
    {
        if (args.Length < 3) return Usage();

        string processName = args[1];
        string path = args[2];
        long from = 0x100000000;
        long size = 0x380000000;
        if (Option(args, "--from") is { } fromText && !TryParseHex(fromText, out from))
            throw new ArgumentException("--from no válido.");
        if (Option(args, "--size") is { } sizeText && (!TryParseHex(sizeText, out size) || size <= 0))
            throw new ArgumentException("--size no válido.");
        if (size > int.MaxValue)
            throw new ArgumentException("Para snapshot usa un rango de menos de 2 GB.");

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);
        Console.WriteLine($"Volcando 0x{from:X} + 0x{size:X}…");

        // Se lee región por región: leer bloques fijos falla cuando el bloque cruza el
        // límite entre dos regiones y se perdería memoria válida (quedaba a cero).
        byte[] data = new byte[size];
        byte[] chunk = new byte[64 * 1024];
        long end = from + size;
        long copied = 0;

        foreach (MemoryRegion region in memory.GetRegions(writableOnly: false))
        {
            long regionStart = Math.Max(region.Base, from);
            long regionEnd = Math.Min(region.Base + region.Size, end);
            if (regionEnd <= regionStart) continue;

            for (long pos = regionStart; pos < regionEnd; pos += chunk.Length)
            {
                int length = (int)Math.Min(chunk.Length, regionEnd - pos);
                if (!memory.TryRead(pos, length, chunk)) continue;
                Buffer.BlockCopy(chunk, 0, data, (int)(pos - from), length);
                copied += length;
            }
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, data);
        File.WriteAllText(path + ".txt", $"base=0x{from:X}\nsize=0x{size:X}\n");
        Console.WriteLine($"Snapshot guardado: {path} (base 0x{from:X}, {size:N0} bytes; " +
                          $"legibles {copied:N0}, huecos a cero {size - copied:N0}).");
        return 0;
    }

    // Busca una cadena (ASCII o UTF-16LE) en la memoria escribible.
    private static int StrFind(string[] args)
    {
        if (args.Length < 3) return Usage();

        string processName = args[1];
        string text = args[2];
        if (text.Length < 2) throw new ArgumentException("El texto debe tener al menos 2 caracteres.");

        bool utf16 = Has(args, "--utf16");
        byte[] needle = utf16 ? Encoding.Unicode.GetBytes(text) : Encoding.ASCII.GetBytes(text);

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);
        Console.WriteLine($"Buscando \"{text}\" ({(utf16 ? "UTF-16" : "ASCII")}, {needle.Length} bytes) en memoria escribible…");
        return SearchBytes(memory, needle, args);
    }

    // Busca un patrón de bytes escrito en hexadecimal (\"D2 CB 81 3B 03 00 00 00\" o \"d2cb813b03000000\").
    private static int PatternFind(string[] args)
    {
        if (args.Length < 3) return Usage();

        string processName = args[1];
        string raw = args[2].Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                              .Replace(" ", "").Replace("-", "").Replace(",", "");
        if (raw.Length < 4 || raw.Length % 2 != 0 || !raw.All(Uri.IsHexDigit))
            throw new ArgumentException("El patrón debe ser hexadecimal y de longitud par.");

        byte[] needle = Convert.FromHexString(raw);

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);
        Console.WriteLine($"Buscando patrón {string.Join(' ', needle.Select(b => b.ToString("X2")))} ({needle.Length} bytes)…");
        return SearchBytes(memory, needle, args);
    }

    // Núcleo de búsqueda de bytes: recorre las regiones escribibles por bloques con
    // arrastre, para encontrar patrones que crucen el límite de un bloque.
    private static int SearchBytes(GameMemory memory, byte[] needle, string[] args)
    {
        int limit = ParseInt(Option(args, "--limit"), 100);
        int context = ParseInt(Option(args, "--context"), 0);

        const int ChunkSize = 1 << 20;
        int carryMax = needle.Length - 1;
        byte[] buffer = new byte[carryMax + ChunkSize];
        byte[] staging = new byte[ChunkSize];
        var hits = new List<long>();

        foreach (MemoryRegion region in memory.GetRegions(writableOnly: true))
        {
            if (hits.Count >= limit) break;

            long pos = region.Base;
            long end = region.Base + region.Size;
            int prefix = 0;

            while (pos < end && hits.Count < limit)
            {
                int length = (int)Math.Min(ChunkSize, end - pos);
                if (!memory.TryRead(pos, length, staging))
                {
                    // Al no poder leer el bloque, se descarta el arrastre anterior.
                    pos += length;
                    prefix = 0;
                    continue;
                }

                Buffer.BlockCopy(staging, 0, buffer, prefix, length);

                int total = prefix + length;
                int searchFrom = 0;
                while (searchFrom <= total - needle.Length)
                {
                    int idx = new ReadOnlySpan<byte>(buffer, searchFrom, total - searchFrom).IndexOf(needle);
                    if (idx < 0) break;

                    int start = searchFrom + idx;
                    if (start >= prefix)
                    {
                        hits.Add(pos - prefix + start);
                        if (hits.Count >= limit) break;
                    }

                    searchFrom = start + 1;
                }

                prefix = Math.Min(carryMax, total);
                Buffer.BlockCopy(buffer, total - prefix, buffer, 0, prefix);
                pos += length;
            }
        }

        Console.WriteLine($"Coincidencias: {hits.Count}" + (hits.Count >= limit ? " (límite alcanzado)" : ""));
        foreach (long address in hits)
        {
            if (context <= 0)
            {
                Console.WriteLine($"  0x{address:X}");
                continue;
            }

            byte[] around = new byte[context * 2 + needle.Length];
            if (!memory.TryRead(address - context, around))
            {
                Console.WriteLine($"  0x{address:X}  (no se pudo leer el contexto)");
                continue;
            }

            string before = string.Join(' ', around[..context].Select(b => b.ToString("X2")));
            string body = string.Join(' ', around[context..(context + needle.Length)].Select(b => b.ToString("X2")));
            string after = string.Join(' ', around[(context + needle.Length)..].Select(b => b.ToString("X2")));
            Console.WriteLine($"  0x{address:X}  {before} [{body}] {after}");
        }

        return 0;
    }

    // Huella cada página de memoria escribible (FNV-1a de 64 bits). Comparar dos
    // listas localiza qué páginas cambian sin guardar gigabytes de volcado.
    private static int Pages(string[] args)
    {
        if (args.Length < 3) return Usage();

        string processName = args[1];
        string path = args[2];
        int pageSize = 0x1000;
        if (Option(args, "--page") is { } pageText)
        {
            if (!TryParseHex(pageText, out long page) || page <= 0 || page > 0x100000)
                throw new ArgumentException("--page no válido.");
            pageSize = (int)page;
        }

        using GameMemory memory = Attach(processName);
        PrintHeader(memory, null);

        int pagesPerChunk = Math.Max(1, (4 << 20) / pageSize);
        byte[] chunk = new byte[pagesPerChunk * pageSize];
        long pageCount = 0;

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path, false, Encoding.ASCII);
        writer.WriteLine($"# base=0x0 page=0x{pageSize:X}");

        foreach (MemoryRegion region in memory.GetRegions(writableOnly: true))
        {
            long pos = region.Base & ~(long)(pageSize - 1);
            long end = region.Base + region.Size;

            while (pos < end)
            {
                int count = (int)Math.Min(pagesPerChunk, (end - pos + pageSize - 1) / pageSize);
                int length = count * pageSize;
                if (!memory.TryRead(pos, length, chunk))
                {
                    pos += length;
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    ulong hash = Hash(chunk.AsSpan(i * pageSize, pageSize));
                    writer.WriteLine($"0x{pos + (long)i * pageSize:X} 0x{hash:X16}");
                    pageCount++;
                }

                pos += length;
            }
        }

        writer.Flush();
        Console.WriteLine($"Páginas huelladas: {pageCount:N0} -> {path}");
        return 0;
    }

    private static ulong Hash(ReadOnlySpan<byte> data)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static GameMemory Attach(string name)
    {
        string bare = Path.GetFileNameWithoutExtension(name);
        Process? process = Process.GetProcessesByName(bare)
            .OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero)
            .FirstOrDefault();

        if (process is null)
            throw new InvalidOperationException($"No hay ningún proceso «{bare}» en ejecución.");

        GameMemory memory = GameMemory.Attach(process.Id);
        if (!memory.IsAlive)
        {
            memory.Dispose();
            throw new InvalidOperationException("No se pudo abrir el proceso.");
        }

        return memory;
    }

    private static void PrintHeader(GameMemory memory, ScanArea? area)
    {
        Console.WriteLine($"Proceso: {memory.ProcessName} (PID {memory.ProcessId}) · " +
                          $"{(memory.Is64Bit ? "x64" : "x86")} · escritura: {memory.HasWriteAccess}");
        if (memory.MainModule is { } module)
            Console.WriteLine($"Módulo: {module.Name} · base 0x{module.Base:X} · " +
                              $"{module.Size / (1024.0 * 1024.0):N0} MB");
        if (area is not null) Console.WriteLine($"Área: {area}");
    }

    private static void PrintCandidates(GameMemory memory, ScanSession session, int limit)
    {
        if (session.Count == 0)
        {
            Console.WriteLine("  (sin candidatos)");
            return;
        }

        int shown = Math.Min(limit, session.Count);
        for (int i = 0; i < shown; i++)
        {
            long address = session.Addresses[i];
            Console.WriteLine($"  0x{address:X}  {Describe(memory, address),-32}  {session.ValueText(i)}");
        }

        if (session.Count > shown)
            Console.WriteLine($"  … y {session.Count - shown:N0} más. Usa --limit N para ver más.");
    }

    private static string Describe(GameMemory memory, long address)
    {
        return AddressSpec.Describe(address, memory.MainModule);
    }

    private static string KindName(ValueKind kind) => kind.ToString().ToLowerInvariant();

    private static string ModeName(ScanMode mode) => mode.ToString().ToLowerInvariant();

    private static ValueKind ParseKind(string text) => text.ToLowerInvariant() switch
    {
        "byte" => ValueKind.Byte,
        "int16" or "short" => ValueKind.Int16,
        "int32" or "int" => ValueKind.Int32,
        "int64" or "long" => ValueKind.Int64,
        "float" => ValueKind.Float,
        "double" => ValueKind.Double,
        _ => throw new ArgumentException($"Tipo no válido: {text}")
    };

    private static ScanMode ParseMode(string text) => text.ToLowerInvariant() switch
    {
        "exact" or "igual" => ScanMode.Exact,
        "notequal" or "distinto" => ScanMode.NotEqual,
        "greater" or "mayor" => ScanMode.Greater,
        "less" or "menor" => ScanMode.Less,
        "increased" or "aumento" => ScanMode.Increased,
        "decreased" or "disminuyo" => ScanMode.Decreased,
        "changed" or "cambio" => ScanMode.Changed,
        "unchanged" or "sincambios" => ScanMode.Unchanged,
        _ => throw new ArgumentException($"Comparación no válida: {text}")
    };

    private static ScanArea ParseArea(string text) => text.ToLowerInvariant() switch
    {
        "fast" or "rapida" => ScanArea.Fast,
        "module" or "modulo" => ScanArea.MainModule,
        "writable" or "escribible" => ScanArea.Writable,
        "all" or "todo" => ScanArea.All,
        _ => throw new ArgumentException($"Área no válida: {text}")
    };

    private static string? Option(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static bool Has(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool TryParseHex(string text, out long value)
    {
        value = 0;
        string body = text.Trim();
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) body = body[2..];
        if (body.Length == 0 || !body.All(Uri.IsHexDigit)) return false;
        return long.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, out int value) && value >= 0 ? value : fallback;

    private static void Save(string path, ScanSession session)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Magic);
        writer.Write((int)session.Kind);
        writer.Write(session.ValueSize);
        writer.Write(session.Count);
        foreach (long address in session.Addresses) writer.Write(address);
        writer.Write(session.Values);
    }

    private static ScanSession Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("No existe la sesión de búsqueda.", path);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        if (reader.ReadString() != Magic) throw new InvalidDataException("Archivo de sesión no válido.");
        ValueKind kind = (ValueKind)reader.ReadInt32();
        int valueSize = reader.ReadInt32();
        int count = reader.ReadInt32();

        if (valueSize != ValueCodec.Size(kind) || count < 0 || count > Scanner.MaxResults)
            throw new InvalidDataException("Cabecera de sesión no válida.");

        long[] addresses = new long[count];
        for (int i = 0; i < count; i++) addresses[i] = reader.ReadInt64();

        byte[] values = reader.ReadBytes(count * valueSize);
        if (values.Length != count * valueSize)
            throw new InvalidDataException("La sesión está incompleta.");

        return new ScanSession(kind, addresses, values, (byte[])values.Clone());
    }
}
