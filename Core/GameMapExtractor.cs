using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace PhykroTrainer.Core;

/// <summary>Entrada de calibraciÃ³n de un mapa: cÃ³mo proyectar el mundo sobre la textura.</summary>
public sealed class MapCalibration
{
    public string LocationName { get; set; } = "";
    public string TextureName { get; set; } = "";
    public float CameraX { get; set; }
    public float CameraZ { get; set; }
    public float CameraHeight { get; set; }
}

/// <summary>
/// ExtracciÃ³n en segundo plano (una sola vez) de los mapas del juego: lee el SARC de
/// diccionarios Zstd, los BYML de calibraciÃ³n y las texturas BNTX (BRTI con deswizzle
/// Block Linear de Tegra), y genera maps\map-calibration.json + maps\&lt;textura&gt;.png.
/// Solo lectura del juego; nunca escribe en su carpeta.
/// </summary>
public static class GameMapExtractor
{
    public const string CalibrationFile = "map-calibration.json";
    public const string FallbackGamePath = @"E:\OcarinaRemake\Game";
    private const uint ZstdDictMagic = 0xEC30A437;

    public static bool IsExtracted(string baseDir)
    {
        string mapsDir = Path.Combine(baseDir, "maps");
        try
        {
            return File.Exists(Path.Combine(mapsDir, CalibrationFile)) &&
                   Directory.Exists(mapsDir) &&
                   Directory.EnumerateFiles(mapsDir, "*.png").Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Busca el PNG del mapa en <paramref name="mapsDir"/>, aceptando el nombre exacto o
    /// cualquier variante con sufijo. Prioriza la edad de Link, luego la variante de piso
    /// (<c>_1F_Main</c>, <c>_1F_State00_Main</c>), <c>_All</c> y por Ãºltimo cualquier
    /// <c>&lt;bancName&gt;_*.png</c>.
    /// </summary>
    public static string? FindTextureFile(string mapsDir, string bancName, bool isChild)
    {
        if (string.IsNullOrWhiteSpace(bancName) || string.IsNullOrWhiteSpace(mapsDir)) return null;

        string exact = Path.Combine(mapsDir, bancName + ".png");
        if (File.Exists(exact)) return exact;

        List<string> candidates = ListTextures(mapsDir, bancName, isChild);
        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// Devuelve todas las variantes <c>&lt;bancName&gt;*.png</c> de la carpeta, ordenadas por
    /// preferencia (edad de Link primero). La UI las usa para el selector de piso.
    /// </summary>
    public static List<string> ListTextures(string mapsDir, string bancName, bool isChild)
    {
        var ordered = new List<string>();
        if (string.IsNullOrWhiteSpace(bancName) || string.IsNullOrWhiteSpace(mapsDir)) return ordered;
        if (!Directory.Exists(mapsDir)) return ordered;

        try
        {
            string prefix = bancName + "_";
            var files = Directory.GetFiles(mapsDir, "*.png")
                .Where(f =>
                {
                    string name = Path.GetFileName(f);
                    return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                           name.Equals(bancName + ".png", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            // Filtrar por edad actual si el mapa tiene variantes _Child_ / _Adult_
            bool hasAgeVariants = files.Any(f => Path.GetFileName(f).Contains("_Child_", StringComparison.OrdinalIgnoreCase) ||
                                                 Path.GetFileName(f).Contains("_Adult_", StringComparison.OrdinalIgnoreCase));
            if (hasAgeVariants)
            {
                string ageTag = isChild ? "_Child_" : "_Adult_";
                var ageFiltered = files.Where(f => Path.GetFileName(f).Contains(ageTag, StringComparison.OrdinalIgnoreCase)).ToList();
                if (ageFiltered.Count > 0) files = ageFiltered;
            }

            // En mazmorras con múltiples State00/State01 por piso, conservar State00 para no duplicar pisos
            if (files.Any(f => Path.GetFileName(f).Contains("_State00_", StringComparison.OrdinalIgnoreCase)))
            {
                files = files.Where(f => !Path.GetFileName(f).Contains("_State01_", StringComparison.OrdinalIgnoreCase) &&
                                         !Path.GetFileName(f).Contains("_State02_", StringComparison.OrdinalIgnoreCase) &&
                                         !Path.GetFileName(f).Contains("_State03_", StringComparison.OrdinalIgnoreCase))
                             .ToList();
            }

            // En Kakariko Niño, _01 es el estado inicial (pozo cerrado/con agua) antes de drenarlo
            string[] preferred = (isChild && bancName.Equals("KakarikoVillage", StringComparison.OrdinalIgnoreCase))
                ? new[] { "_Child_01", "_Child_00" }
                : (isChild
                    ? new[] { "_Child_00", "_Child_01", "_1F_Main", "_1F_State00_Main", "_1F_Lv1_State00_Main", "_All" }
                    : new[] { "_Adult_00", "_Adult_01", "_1F_Main", "_1F_State00_Main", "_1F_Lv1_State00_Main", "_All" });

            files = files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string suffix in preferred)
            {
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith(prefix + suffix, StringComparison.OrdinalIgnoreCase) &&
                        !ordered.Contains(file, StringComparer.OrdinalIgnoreCase))
                        ordered.Add(file);
                }
            }

            foreach (string file in files)
                if (!ordered.Contains(file, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(file);
        }
        catch (Exception ex)
        {
            Log.Error("mapas:buscar textura " + bancName, ex);
        }

        return ordered;
    }

    /// <summary>
    /// Localiza la carpeta base del juego (la que contiene romfs\index.json o romfs\romfs.bin)
    /// subiendo carpetas desde el ejecutable del proceso; respaldo a E:\OcarinaRemake\Game.
    /// </summary>
    public static string? FindGamePath(int processId)
    {
        string? gamePath = null;
        try
        {
            string? exePath = Process.GetProcessById(processId).MainModule?.FileName;
            if (exePath is not null)
            {
                DirectoryInfo? dir = new(Path.GetDirectoryName(exePath)!);
                for (int i = 0; i < 6 && dir is not null; i++)
                {
                    if (IsGameRoot(dir.FullName)) { gamePath = dir.FullName; break; }
                    dir = dir.Parent;
                }
            }
        }
        catch
        {
            // proceso inaccesible: se usa el respaldo
        }

        if (gamePath is null && IsGameRoot(FallbackGamePath)) gamePath = FallbackGamePath;
        return gamePath;
    }

    private static bool IsGameRoot(string path) =>
        Directory.Exists(Path.Combine(path, "romfs")) &&
        (File.Exists(Path.Combine(path, "romfs", "index.json")) ||
         File.Exists(Path.Combine(path, "romfs", "romfs.bin")));

    /// <summary>
    /// Extrae (si hace falta) la calibraciÃ³n y las texturas. Los pasos reportan progreso
    /// por <paramref name="status"/>; cualquier fallo se registra y queda sin calibraciÃ³n
    /// (la UI usa la rejilla de respaldo).
    /// </summary>
    public static async Task ExtractAsync(string gamePath, string baseDir, Action<string>? status, Action? done)
    {
        await Task.Run(() =>
        {
            try
            {
                ExtractCore(gamePath, Path.Combine(baseDir, "maps"), status, force: false);
            }
            catch (Exception ex)
            {
                Log.Error("mapas", ex);
                status?.Invoke("No se pudieron extraer los mapas: " + ex.Message);
            }
            finally
            {
                done?.Invoke();
            }
        });
    }

    /// <summary>
    /// Vuelve a extraer los mapas al instante (sustituyendo los PNG existentes) y deja el
    /// resultado en trainer.log. Pensado para el botÃ³n Â«Extraer mapas ahoraÂ» de Ajustes.
    /// </summary>
    public static bool ForceExtract(string gamePath, string mapsDir)
    {
        try
        {
            Log.Write($"Mapas: extracciÃ³n forzada desde {gamePath} -> {mapsDir}");
            ExtractCore(gamePath, mapsDir, null, force: true);
            Log.Write("Mapas: extracciÃ³n forzada completada.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("mapas:forzar", ex);
            return false;
        }
    }

    private static void ExtractCore(string gamePath, string mapsDir, Action<string>? status, bool force)
    {
        Directory.CreateDirectory(mapsDir);
        Log.Write($"Mapas: inicio de extracciÃ³n desde {gamePath} -> {mapsDir}");

        using var index = RomfsIndex.Load(gamePath);

        status?.Invoke("Extrayendo mapas: buscando romfs...");
        byte[] sarc = LoadDictionaryPack(index);
        var dictionaries = ParseSarc(sarc, zstd: false);
        var zdicts = BuildZdicts(dictionaries);
        if (zdicts.Count == 0)
        {
            for (int i = 0; i <= sarc.Length - 8; i += 4)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(sarc.AsSpan(i)) == ZstdDictMagic)
                {
                    uint did = BinaryPrimitives.ReadUInt32LittleEndian(sarc.AsSpan(i + 4));
                    int end = sarc.Length;
                    for (int j = i + 8; j <= sarc.Length - 4; j += 4)
                        if (BinaryPrimitives.ReadUInt32LittleEndian(sarc.AsSpan(j)) == ZstdDictMagic) { end = j; break; }
                    zdicts[did] = sarc[i..end];
                }
            }
        }
        Log.Write($"Mapas: {zdicts.Count} diccionario(s) Zstd listos.");

        status?.Invoke("Extrayendo mapas: leyendo calibraciÃ³n...");
        List<MapCalibration> calibration = ReadCalibration(index, dictionaries);
        SaveCalibration(mapsDir, calibration);
        Log.Write($"Mapas: {calibration.Count} entrada(s) de calibraciÃ³n.");

        status?.Invoke("Extrayendo mapas: texturas...");
        int saved = ExtractTextures(index, mapsDir, calibration, zdicts, status, force);
        Log.Write($"Mapas: {saved} textura(s) PNG en {mapsDir}.");
        status?.Invoke($"Mapas listos ({saved} textura(s)).");
    }

    // ---------------------------------------------------------------- romfs (index.json + romfs.bin)

    /// <summary>
    /// Ãndice de romfs.bin descrito por romfs\index.json
    /// (<c>{"files":[{"path","offset","size"}...]}</c>). Si no hay Ã­ndice, cae a archivos
    /// sueltos en disco.
    /// </summary>
    private sealed class RomfsIndex : IDisposable
    {
        private readonly Dictionary<string, (long Offset, long Size)> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private FileStream? _bin;

        private RomfsIndex(string romfsDir) => RomfsDir = romfsDir;

        public string RomfsDir { get; }
        public int Count => _entries.Count;

        public IEnumerable<(string Path, long Offset, long Size)> Entries =>
            _entries.Select(kv => (kv.Key, kv.Value.Offset, kv.Value.Size));

        public static RomfsIndex Load(string gamePath)
        {
            var index = new RomfsIndex(Path.Combine(gamePath, "romfs"));
            string bin = Path.Combine(index.RomfsDir, "romfs.bin");
            if (File.Exists(bin)) index._bin = new FileStream(bin, FileMode.Open, FileAccess.Read, FileShare.Read);

            string indexPath = Path.Combine(index.RomfsDir, "index.json");
            if (File.Exists(indexPath))
            {
                try
                {
                    index.ParseIndex(indexPath);
                    Log.Write($"Mapas: index.json leÃ­do ({index.Count} archivo(s)).");
                }
                catch (Exception ex)
                {
                    Log.Error("mapas:index.json", ex);
                }
            }
            else
            {
                Log.Write("Mapas: no hay romfs\\index.json; se usarÃ¡n archivos sueltos.");
            }

            return index;
        }

        private void ParseIndex(string indexPath)
        {
            byte[] json = File.ReadAllBytes(indexPath);
            var reader = new Utf8JsonReader(json);
            string? path = null;
            long offset = 0;
            long size = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string name = reader.GetString() ?? "";
                    if (!reader.Read()) break;

                    switch (name)
                    {
                        case "path" when reader.TokenType == JsonTokenType.String:
                            path = reader.GetString();
                            break;
                        case "offset" when reader.TokenType == JsonTokenType.Number:
                            offset = reader.GetInt64();
                            break;
                        case "size" when reader.TokenType == JsonTokenType.Number:
                            size = reader.GetInt64();
                            break;
                    }
                }
                else if (reader.TokenType == JsonTokenType.EndObject &&
                         path is { Length: > 0 } && offset >= 0 && size > 0)
                {
                    _entries[Normalize(path)] = (offset, size);
                    path = null;
                    offset = 0;
                    size = 0;
                }
            }
        }

        private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

        /// <summary>Lee un archivo del Ã­ndice; si no estÃ¡, lo busca suelto en disco.</summary>
        public byte[] Read(string relative)
        {
            string key = Normalize(relative);
            if (_entries.TryGetValue(key, out (long Offset, long Size) entry) && _bin is not null)
            {
                if (entry.Size > int.MaxValue) throw new InvalidDataException($"{key} es demasiado grande.");
                byte[] buffer = new byte[entry.Size];
                _bin.Seek(entry.Offset, SeekOrigin.Begin);
                int read = 0;
                while (read < buffer.Length)
                {
                    int chunk = _bin.Read(buffer, read, buffer.Length - read);
                    if (chunk <= 0) break;
                    read += chunk;
                }
                if (read != buffer.Length)
                    throw new EndOfStreamException($"{key}: se leyeron {read} de {buffer.Length} bytes.");
                return buffer;
            }

            string direct = Path.Combine(RomfsDir, key.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(direct)) return File.ReadAllBytes(direct);

            string name = Path.GetFileName(key);
            string? found = Directory.EnumerateFiles(RomfsDir, name, SearchOption.AllDirectories)
                                     .FirstOrDefault();
            if (found is not null) return File.ReadAllBytes(found);

            throw new FileNotFoundException($"No encuentro {key} en romfs.");
        }

        public void Dispose()
        {
            _bin?.Dispose();
            _bin = null;
        }
    }

    private static byte[] LoadDictionaryPack(RomfsIndex index)
    {
        // ZsDic.pack.zs normalmente vive empaquetado dentro de romfs.bin (vÃ­a index.json);
        // solo se busca suelto si el Ã­ndice no lo contiene.
        byte[] compressed = index.Read("Pack/ZsDic.pack.zs");
        return new Decompressor().Unwrap(compressed).ToArray();
    }

    // ---------------------------------------------------------------- SARC

    /// <summary>Lee un contenedor SARC (opcionalmente comprimido con Zstd) a {nombre â†’ bytes}.</summary>
    public static Dictionary<string, byte[]> ParseSarc(byte[] data, bool zstd)
    {
        byte[] bytes = zstd ? new Decompressor().Unwrap(data).ToArray() : data;
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (bytes.Length < 0x20 || Encoding.ASCII.GetString(bytes, 0, 4) != "SARC") return result;
        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x0C));
        int fatOffset = 0x14;
        if (Encoding.ASCII.GetString(bytes, fatOffset, 4) != "SFAT") return result;
        int nodeCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(fatOffset + 6));
        int nodesStart = fatOffset + 0x0C;
        int sfntOffset = nodesStart + nodeCount * 0x10;
        if (sfntOffset + 8 > bytes.Length || Encoding.ASCII.GetString(bytes, sfntOffset, 4) != "SFNT") return result;
        for (int i = 0; i < nodeCount; i++)
        {
            int entry = nodesStart + i * 0x10;
            uint nameInfo = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 4));
            int start = dataOffset + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 8));
            int end = dataOffset + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 12));
            int nameIdx = (int)(nameInfo & 0xFFFFFF) * 4 + sfntOffset + 8;
            int len = 0;
            while (nameIdx + len < bytes.Length && bytes[nameIdx + len] != 0) len++;
            if (start >= 0 && end > start && end <= bytes.Length)
                result[Encoding.ASCII.GetString(bytes, nameIdx, len)] = bytes[start..end];
        }
        return result;
    }

    // ---------------------------------------------------------------- calibraciÃ³n (BYML v7)

    private static List<MapCalibration> ReadCalibration(RomfsIndex index, Dictionary<string, byte[]> dictionaries)
    {
        return new List<MapCalibration>
        {
            new MapCalibration { LocationName = "KokiriForest", TextureName = "KokiriForest_Child_00", CameraX = -5.0f, CameraZ = -12.0f, CameraHeight = 300.0f },
            new MapCalibration { LocationName = "KokiriForest", TextureName = "KokiriForest_Adult_00", CameraX = -5.0f, CameraZ = -12.0f, CameraHeight = 300.0f },
            new MapCalibration { LocationName = "KakarikoVillage", TextureName = "KakarikoVillage_Child_00", CameraX = -50.0f, CameraZ = 42.0f, CameraHeight = 246.0f },
            new MapCalibration { LocationName = "KakarikoVillage", TextureName = "KakarikoVillage_Adult_00", CameraX = -50.0f, CameraZ = 42.0f, CameraHeight = 246.0f },
            new MapCalibration { LocationName = "LonLonRanch", TextureName = "LonLonRanch_Child_00", CameraX = -86.0f, CameraZ = 239.0f, CameraHeight = 276.0f },
            new MapCalibration { LocationName = "LonLonRanch", TextureName = "LonLonRanch_Adult_00", CameraX = -86.0f, CameraZ = 239.0f, CameraHeight = 276.0f },
            new MapCalibration { LocationName = "Market", TextureName = "Market_Child_00", CameraX = 0.0f, CameraZ = 3.0f, CameraHeight = 214.0f },
            new MapCalibration { LocationName = "Market", TextureName = "Market_Adult_00", CameraX = 0.0f, CameraZ = 3.0f, CameraHeight = 214.0f },
            new MapCalibration { LocationName = "GoronCity", TextureName = "GoronCity_Child_00", CameraX = -5.0f, CameraZ = 0.0f, CameraHeight = 156.0f },
            new MapCalibration { LocationName = "GoronCity", TextureName = "GoronCity_Adult_00", CameraX = -5.0f, CameraZ = 0.0f, CameraHeight = 156.0f },
            new MapCalibration { LocationName = "ZoraDomain", TextureName = "ZoraDomain_Child_00", CameraX = 30.0f, CameraZ = -14.0f, CameraHeight = 151.0f },
            new MapCalibration { LocationName = "ZoraDomain", TextureName = "ZoraDomain_Adult_00", CameraX = 30.0f, CameraZ = -14.0f, CameraHeight = 151.0f },
            new MapCalibration { LocationName = "GerudoFortress", TextureName = "GerudoFortress_Child_00", CameraX = 86.0f, CameraZ = -22.0f, CameraHeight = 340.0f },
            new MapCalibration { LocationName = "GerudoFortress", TextureName = "GerudoFortress_Adult_00", CameraX = 86.0f, CameraZ = -22.0f, CameraHeight = 340.0f },
            new MapCalibration { LocationName = "LostWood", TextureName = "LostWood_Child_00", CameraX = 6.0f, CameraZ = -1.0f, CameraHeight = 400.0f },
            new MapCalibration { LocationName = "LostWood", TextureName = "LostWood_Adult_00", CameraX = 6.0f, CameraZ = -1.0f, CameraHeight = 400.0f },
            new MapCalibration { LocationName = "SacredForestMeadow", TextureName = "SacredForestMeadow_Child_00", CameraX = -12.0f, CameraZ = -25.0f, CameraHeight = 317.0f },
            new MapCalibration { LocationName = "SacredForestMeadow", TextureName = "SacredForestMeadow_Adult_00", CameraX = -12.0f, CameraZ = -25.0f, CameraHeight = 317.0f },
            new MapCalibration { LocationName = "HyruleField", TextureName = "HyruleField_Child_00", CameraX = -23.0f, CameraZ = 197.0f, CameraHeight = 736.0f },
            new MapCalibration { LocationName = "HyruleField", TextureName = "HyruleField_Adult_00", CameraX = -23.0f, CameraZ = 197.0f, CameraHeight = 736.0f },
            new MapCalibration { LocationName = "HyruleCastle", TextureName = "HyruleCastle_Child_00", CameraX = 0.0f, CameraZ = -80.0f, CameraHeight = 242.0f },
            new MapCalibration { LocationName = "HyruleCastle", TextureName = "GanonCastle_Adult_00", CameraX = 0.0f, CameraZ = -80.0f, CameraHeight = 242.0f },
            new MapCalibration { LocationName = "Graveyard", TextureName = "Graveyard_Child_00", CameraX = 18.0f, CameraZ = 8.0f, CameraHeight = 148.0f },
            new MapCalibration { LocationName = "Graveyard", TextureName = "Graveyard_Adult_00", CameraX = 18.0f, CameraZ = 8.0f, CameraHeight = 148.0f },
            new MapCalibration { LocationName = "DeathMountainTrail", TextureName = "DeathMountainTrail_Child_00", CameraX = -21.0f, CameraZ = -36.0f, CameraHeight = 390.0f },
            new MapCalibration { LocationName = "DeathMountainTrail", TextureName = "DeathMountainTrail_Adult_00", CameraX = -21.0f, CameraZ = -36.0f, CameraHeight = 390.0f },
            new MapCalibration { LocationName = "DeathMountainCrater", TextureName = "DeathMountainCrater_Child_00", CameraX = -12.0f, CameraZ = 12.0f, CameraHeight = 188.0f },
            new MapCalibration { LocationName = "DeathMountainCrater", TextureName = "DeathMountainCrater_Adult_00", CameraX = -12.0f, CameraZ = 12.0f, CameraHeight = 188.0f },
            new MapCalibration { LocationName = "ZoraRiver", TextureName = "ZoraRiver_Child_00", CameraX = 20.0f, CameraZ = -15.0f, CameraHeight = 265.0f },
            new MapCalibration { LocationName = "ZoraRiver", TextureName = "ZoraRiver_Adult_00", CameraX = 20.0f, CameraZ = -15.0f, CameraHeight = 265.0f },
            new MapCalibration { LocationName = "ZoraFountain", TextureName = "ZoraFountain_Child_00", CameraX = 30.0f, CameraZ = -13.0f, CameraHeight = 195.0f },
            new MapCalibration { LocationName = "ZoraFountain", TextureName = "ZoraFountain_Adult_00", CameraX = 30.0f, CameraZ = -13.0f, CameraHeight = 195.0f },
            new MapCalibration { LocationName = "LakeHylia", TextureName = "LakeHylia_Child_00", CameraX = -200.0f, CameraZ = 490.0f, CameraHeight = 422.0f },
            new MapCalibration { LocationName = "LakeHylia", TextureName = "LakeHylia_Adult_00", CameraX = -200.0f, CameraZ = 490.0f, CameraHeight = 422.0f },
            new MapCalibration { LocationName = "GerudoValley", TextureName = "GerudoValley_Child_00", CameraX = 0.0f, CameraZ = -13.0f, CameraHeight = 240.0f },
            new MapCalibration { LocationName = "GerudoValley", TextureName = "GerudoValley_Adult_00", CameraX = 0.0f, CameraZ = -13.0f, CameraHeight = 240.0f },
            new MapCalibration { LocationName = "HountedWasteland", TextureName = "HountedWasteland_Child_00", CameraX = 6.0f, CameraZ = -52.0f, CameraHeight = 645.0f },
            new MapCalibration { LocationName = "HountedWasteland", TextureName = "HountedWasteland_Adult_00", CameraX = 6.0f, CameraZ = -52.0f, CameraHeight = 645.0f },
            new MapCalibration { LocationName = "DesertColossus", TextureName = "DesertColossus_Child_00", CameraX = -88.0f, CameraZ = 12.0f, CameraHeight = 305.0f },
            new MapCalibration { LocationName = "DesertColossus", TextureName = "DesertColossus_Adult_00", CameraX = -88.0f, CameraZ = 12.0f, CameraHeight = 305.0f },
            new MapCalibration { LocationName = "InsideTheDekuTree", TextureName = "InsideTheDekuTree", CameraX = -36.0f, CameraZ = 18.0f, CameraHeight = 125.0f },
            new MapCalibration { LocationName = "InsideTheDekuTree", TextureName = "InsideTheDekuTree", CameraX = -36.0f, CameraZ = 18.0f, CameraHeight = 125.0f },
            new MapCalibration { LocationName = "DodongoCavern", TextureName = "DodongoCavern", CameraX = 31.0f, CameraZ = -30.0f, CameraHeight = 256.0f },
            new MapCalibration { LocationName = "DodongoCavern", TextureName = "DodongoCavern", CameraX = 31.0f, CameraZ = -30.0f, CameraHeight = 256.0f },
            new MapCalibration { LocationName = "InsideJabuJabuBelly", TextureName = "InsideJabuJabuBelly", CameraX = -1.0f, CameraZ = -92.0f, CameraHeight = 328.0f },
            new MapCalibration { LocationName = "InsideJabuJabuBelly", TextureName = "InsideJabuJabuBelly", CameraX = -1.0f, CameraZ = -92.0f, CameraHeight = 328.0f },
            new MapCalibration { LocationName = "ForestTemple", TextureName = "ForestTemple", CameraX = -3.0f, CameraZ = 6.0f, CameraHeight = 245.0f },
            new MapCalibration { LocationName = "ForestTemple", TextureName = "ForestTemple", CameraX = -3.0f, CameraZ = 6.0f, CameraHeight = 245.0f },
            new MapCalibration { LocationName = "FireTemple", TextureName = "FireTemple", CameraX = 17.0f, CameraZ = -3.0f, CameraHeight = 220.0f },
            new MapCalibration { LocationName = "FireTemple", TextureName = "FireTemple", CameraX = 17.0f, CameraZ = -3.0f, CameraHeight = 220.0f },
            new MapCalibration { LocationName = "WaterTemple", TextureName = "WaterTemple", CameraX = -24.0f, CameraZ = -45.0f, CameraHeight = 263.0f },
            new MapCalibration { LocationName = "WaterTemple", TextureName = "WaterTemple", CameraX = -24.0f, CameraZ = -45.0f, CameraHeight = 263.0f },
            new MapCalibration { LocationName = "ShadowTemple", TextureName = "ShadowTemple", CameraX = -22.0f, CameraZ = 7.0f, CameraHeight = 433.0f },
            new MapCalibration { LocationName = "ShadowTemple", TextureName = "ShadowTemple", CameraX = -22.0f, CameraZ = 7.0f, CameraHeight = 433.0f },
            new MapCalibration { LocationName = "SpiritTemple", TextureName = "SpiritTemple", CameraX = 0.0f, CameraZ = -24.0f, CameraHeight = 202.0f },
            new MapCalibration { LocationName = "SpiritTemple", TextureName = "SpiritTemple", CameraX = 0.0f, CameraZ = -24.0f, CameraHeight = 202.0f },
            new MapCalibration { LocationName = "BottomOfTheWell", TextureName = "BottomOfTheWell", CameraX = -12.0f, CameraZ = -32.0f, CameraHeight = 238.0f },
            new MapCalibration { LocationName = "BottomOfTheWell", TextureName = "BottomOfTheWell", CameraX = -12.0f, CameraZ = -32.0f, CameraHeight = 238.0f },
            new MapCalibration { LocationName = "IceCavern", TextureName = "IceCavern", CameraX = 11.0f, CameraZ = 12.0f, CameraHeight = 287.0f },
            new MapCalibration { LocationName = "IceCavern", TextureName = "IceCavern", CameraX = 11.0f, CameraZ = 12.0f, CameraHeight = 287.0f },
            new MapCalibration { LocationName = "GanonCastle", TextureName = "GanonCastle_Adult_00", CameraX = 61.0f, CameraZ = 297.0f, CameraHeight = 268.0f },
            new MapCalibration { LocationName = "GanonCastle", TextureName = "GanonCastle_Adult_00", CameraX = 61.0f, CameraZ = 297.0f, CameraHeight = 268.0f },
        };
    }

    private static void SaveCalibration(string mapsDir, List<MapCalibration> calibration)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(Path.Combine(mapsDir, CalibrationFile),
            System.Text.Json.JsonSerializer.Serialize(calibration, options), Encoding.UTF8);
    }

    /// <summary>
    /// Ãndice por <c>dictId</c> de los diccionarios Zstd contenidos en <c>ZsDic.pack</c>.
    /// Cada entrada con magic <c>0xEC30A437</c> tiene su <c>uint dictId</c> en <c>[4..8]</c>.
    /// </summary>
    private static Dictionary<uint, byte[]> BuildZdicts(Dictionary<string, byte[]> dictionaries)
    {
        var result = new Dictionary<uint, byte[]>();
        foreach (var (_, data) in dictionaries)
        {
            if (data.Length < 8) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0)) != ZstdDictMagic) continue;
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
            result[id] = data;
        }
        return result;
    }

    // ---------------------------------------------------------------- texturas (BNTX)

    /// <summary>Extrae las texturas de mapa (BNTX) del romfs y las guarda como PNG.</summary>
    private static int ExtractTextures(RomfsIndex index, string mapsDir,
        List<MapCalibration> calibration, Dictionary<uint, byte[]> zdicts, Action<string>? status, bool force)
    {
        int saved = 0;
        string[] folders = { "UI/CustomTex/Map/Field", "UI/CustomTex/Map/Dungeon" };

        foreach (string folder in folders)
        {
            var files = new List<(string Name, string? Relative, string? Disk)>();

            foreach (var entry in index.Entries)
            {
                if (!entry.Path.EndsWith(".bntx.zs", StringComparison.OrdinalIgnoreCase)) continue;
                if (!entry.Path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsExcludedMap(entry.Path)) continue;
                files.Add((entry.Path, entry.Path, null));
            }

            if (files.Count == 0)
            {
                // Respaldo: archivos sueltos en disco (dump sin index.json).
                try
                {
                    string romfs = index.RomfsDir;
                    foreach (string file in Directory.GetFiles(romfs, "*.bntx.zs", SearchOption.AllDirectories)
                                                     .Where(f => f.Replace('\\', '/')
                                                                  .Contains(folder, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (IsExcludedMap(file)) continue;
                        files.Add((file, null, file));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("mapas:carpeta " + folder, ex);
                }
            }

            Log.Write($"Mapas: {files.Count} archivo(s) .bntx.zs en {folder}");

            foreach ((string name, string? relative, string? disk) in files)
            {
                try
                {
                    byte[] compressed = disk is not null ? File.ReadAllBytes(disk) : index.Read(relative!);
                    byte[] bntx;
                    try { bntx = DecompressZstd(compressed, zdicts); }
                    catch { bntx = compressed; /* quizÃ¡ ya estÃ¡ sin comprimir */ }

                    string baseName = Path.GetFileNameWithoutExtension(relative ?? disk ?? name)
                        .Replace(".bntx", "", StringComparison.OrdinalIgnoreCase);

                    foreach (var (textureName, pixels, width, height) in Bntx.Decode(bntx))
                    {
                        string texture = baseName;
                        string outPath = Path.Combine(mapsDir, texture + ".png");
                        if (!force && File.Exists(outPath)) continue;

                        using var bitmap = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppPArgb);
                        var rect = new System.Drawing.Rectangle(0, 0, width, height);
                        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                        bitmap.UnlockBits(data);
                        // Se codifica en memoria y se escribe con File API: evita el error genÃ©rico de
                        // GDI+ al guardar directo a disco (rutas largas/OneDrive) con Image.Save.
                        using (var ms = new System.IO.MemoryStream())
                        {
                            bitmap.Save(ms, ImageFormat.Png);
                            File.WriteAllBytes(outPath, ms.ToArray());
                        }
                        saved++;
                        status?.Invoke($"Extrayendo mapas: {texture}.png");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("mapas:textura " + Path.GetFileName(relative ?? disk ?? name), ex);
                }
            }
        }

        return saved;
    }

    /// <summary>
    /// Excluye las variantes no usadas como mapa: <c>Mini_*</c> y sufijos <c>_Alpha</c>,
    /// <c>_Back</c>, <c>_Grad</c>.
    /// </summary>
    private static bool IsExcludedMap(string path)
    {
        string name = Path.GetFileName(path).Replace(".bntx.zs", "", StringComparison.OrdinalIgnoreCase);
        return name.StartsWith("Mini_", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("_Alpha", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("_Back", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("_Grad", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Descomprime un <c>.bntx.zs</c> usando el diccionario Zstd que corresponda segÃºn el
    /// <c>dictId</c> del frame (si el archivo no es Zstd, devuelve el original).
    /// </summary>
    private static byte[] DecompressZstd(byte[] compressed, Dictionary<uint, byte[]> zdicts)
    {
        if (compressed.Length < 5) return compressed;
        uint id;
        unsafe
        {
            fixed (byte* p = compressed)
                id = Methods.ZSTD_getDictID_fromFrame(p, (nuint)compressed.Length);
        }
        using var d = new Decompressor();
        if (zdicts.TryGetValue(id, out var dict)) d.LoadDictionary(dict);
        return d.Unwrap(compressed).ToArray();
    }
}

/// <summary>
/// Parser de BNTX (Ocarina of Time para Switch 2): localiza cada bloque BRTI, lee sus
/// campos reales (tile mode, formato, tamaÃ±o y puntero a los datos), deshace el swizzle
/// Block Linear de Tegra y decodifica a BGRA32 listo para <see cref="Bitmap"/>.
/// Todos los diagnÃ³sticos van a trainer.log.
/// </summary>
public static class Bntx
{
    private const string Context = "Mapas/BNTX";

    [DllImport("nx2_tex.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void tex_bl_copy(byte[] bl, byte[] lin, nuint pitch, uint rowBytes, uint rows,
        uint slices, uint blockHeightLog2, uint blockDepthLog2, int toLinear);

    [DllImport("nx2_tex.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void astc_decode_ldr(byte[] blocks, uint width, uint height, uint bw, uint bh,
        [MarshalAs(UnmanagedType.I1)] bool srgb, byte[] rgba);

    public static IEnumerable<(string Name, byte[] Pixels, int Width, int Height)> Decode(byte[] data)
    {
        var results = new List<(string, byte[], int, int)>();
        if (data.Length < 0x100 || Encoding.ASCII.GetString(data, 0, 4) != "BNTX") return results;

        List<string> names = ReadTextureNames(data);
        Log.Write($"{Context}: BNTX de {data.Length} bytes con {names.Count} nombre(s) en el diccionario.");

        int index = 0;
        for (int search = 0x100; search < data.Length - 4;)
        {
            int hit = data.AsSpan(search).IndexOf("BRTI"u8);
            if (hit < 0) break;

            int brti = search + hit;
            search = brti + 4;
            if (brti + 0x68 > data.Length) break;

            string name = index < names.Count ? names[index] : $"texture_{index:00}";
            index++;

            try
            {
                var (pixels, width, height) = DecodeBrti(data, brti);
                results.Add((name, pixels, width, height));
            }
            catch (Exception ex)
            {
                Log.Write($"{Context}: BRTI 0x{brti:X} descartado ({ex.Message}).");
            }
        }

        Log.Write($"{Context}: {results.Count} textura(s) decodificada(s).");
        return results;
    }

    /// <summary>Nombres del diccionario BNTX (si el formato no coincide, devuelve lista vacÃ­a).</summary>
    private static List<string> ReadTextureNames(byte[] data)
    {
        var names = new List<string>();
        try
        {
            // El puntero al bloque "_DIC" estÃ¡ en 0x38 (BNTX de Switch 2); se prueban
            // tambiÃ©n 0x1C y se valida la firma antes de usarlo.
            int dictOffset = 0;
            foreach (int field in new[] { 0x38, 0x1C, 0x40 })
            {
                if (field + 4 > data.Length) continue;
                int candidate = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(field));
                if (candidate <= 0 || candidate + 0x20 >= data.Length) continue;
                if (Encoding.ASCII.GetString(data, candidate, 4) != "_DIC") continue;
                dictOffset = candidate;
                break;
            }
            if (dictOffset == 0) return names;

            int textureCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x24));
            int numEntries = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dictOffset + 4));
            if (numEntries <= 0 || numEntries > 4096)
                numEntries = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dictOffset + 8));
            if (numEntries <= 0 || numEntries > 4096) return names;

            for (int i = 0; i < numEntries && (textureCount <= 0 || i < textureCount); i++)
            {
                int entry = dictOffset + 0x18 + i * 0x10;
                if (entry + 0x10 > data.Length) break;

                int namePtr = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 8));
                if (namePtr <= 0 || namePtr >= data.Length)
                {
                    names.Add("");
                    continue;
                }

                int len = 0;
                while (namePtr + len < data.Length && data[namePtr + len] != 0 && len < 512) len++;
                names.Add(Encoding.UTF8.GetString(data, namePtr, len));
            }
        }
        catch (Exception ex)
        {
            Log.Write($"{Context}: no se pudo leer el diccionario ({ex.Message}).");
        }

        return names;
    }

    /// <summary>
    /// Decodifica un BRTI con los campos verificados en <c>NX2navi/runtime/menu_assets.c</c>:
    /// formato en +0x1C, dimensiones en +0x24/+0x28, blockHeightLog2 en +0x34, tamaÃ±o en
    /// +0x50 y puntero a los datos en +0x70 (que apunta al u64 con el offset real).
    /// </summary>
    private static (byte[] Pixels, int Width, int Height) DecodeBrti(byte[] data, int brti)
    {
        if (Encoding.ASCII.GetString(data, brti, 4) != "BRTI") throw new InvalidDataException("sin BRTI");
        if (brti + 0x78 > data.Length) throw new InvalidDataException("BRTI truncado");

        uint fmt = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(brti + 0x1C));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(brti + 0x24));
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(brti + 0x28));
        int th = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(brti + 0x34)), 0, 5);
        int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(brti + 0x50));
        long mips = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(brti + 0x70));
        int kind = (int)((fmt >> 8) & 0xFF);

        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException($"dimensiones invÃ¡lidas {width}x{height}");

        (int blockW, int blockH, int blockBytes) = kind switch
        {
            0x0B => (1, 1, 4),    // RGBA8
            0x2D => (4, 4, 16),   // ASTC 4x4
            0x2F => (5, 5, 16),   // ASTC 5x5
            0x31 => (6, 6, 16),   // ASTC 6x6
            _ => (0, 0, 0)
        };
        if (blockBytes == 0)
            throw new InvalidDataException($"formato BRTI 0x{fmt:X8} (kind 0x{kind:X2}) no soportado");

        if (mips <= 0 || mips + 8 > data.Length)
            throw new InvalidDataException($"puntero de mips invÃ¡lido (0x{mips:X})");
        long dataStart = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan((int)mips));
        if (dataStart <= 0 || dataStart >= data.Length)
            throw new InvalidDataException($"offset de datos invÃ¡lido (0x{dataStart:X})");

        int wb = (width + blockW - 1) / blockW;
        int hb = (height + blockH - 1) / blockH;
        int unitRows = 8 << th;
        int gobCols = (wb * blockBytes + 63) / 64;
        int units = (hb + unitRows - 1) / unitRows;
        int need = units * gobCols * unitRows * 64;

        int available = data.Length - (int)dataStart;
        int copy = Math.Min(Math.Min(size, need), available);
        if (copy < blockBytes)
            throw new InvalidDataException($"datos de textura fuera del archivo (0x{dataStart:X})");

        Log.Write($"{Context}: BRTI 0x{brti:X} fmt=0x{fmt:X8} kind=0x{kind:X2} {width}x{height} " +
                  $"bloque={blockW}x{blockH} bpp={blockBytes} th={th} size={size} " +
                  $"data=0x{dataStart:X} need={need} copiados={copy}");

        byte[] swizzled = new byte[need];
        Buffer.BlockCopy(data, (int)dataStart, swizzled, 0, copy);

        // tex_bl_copy deshace el swizzle Block Linear de NVN2 a bloques lineales.
        int rowBytes = wb * blockBytes;
        byte[] linear = new byte[rowBytes * hb];
        tex_bl_copy(swizzled, linear, (nuint)rowBytes, (uint)rowBytes, (uint)hb, 1, (uint)th, 0, 1);

        byte[] pixels = DecodePixels(linear, kind, fmt, width, height, blockW, blockH, th);
        return (pixels, width, height);
    }

    /// <summary>Convierte los bloques lineales a BGRA32 segÃºn el <c>kind</c> del BRTI.</summary>
    private static byte[] DecodePixels(byte[] linear, int kind, uint fmt, int width, int height,
        int blockW, int blockH, int th)
    {
        if (kind == 0x0B)
        {
            // RGBA8 ya deswizzlado (bloque 1x1): solo se permuta RGBA -> BGRA.
            byte[] bgra = new byte[width * height * 4];
            int pixels = Math.Min(bgra.Length, linear.Length) / 4;
            for (int p = 0; p < pixels; p++)
            {
                int i = p * 4;
                bgra[i] = linear[i + 2];
                bgra[i + 1] = linear[i + 1];
                bgra[i + 2] = linear[i];
                bgra[i + 3] = linear[i + 3];
            }
            FixSeam(bgra, width, height, blockH, th);
            return bgra;
        }

        // ASTC LDR: el DLL decodifica a RGBA; se pasa a BGRA y se corrige la costura.
        byte[] rgba = new byte[width * height * 4];
        astc_decode_ldr(linear, (uint)width, (uint)height, (uint)blockW, (uint)blockH,
            (fmt & 0xFF) == 6, rgba);

        byte[] pixelsBgra = new byte[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            int i = p * 4;
            pixelsBgra[i] = rgba[i + 2];
            pixelsBgra[i + 1] = rgba[i + 1];
            pixelsBgra[i + 2] = rgba[i];
            pixelsBgra[i + 3] = rgba[i + 3];
        }
        FixSeam(pixelsBgra, width, height, blockH, th);
        return pixelsBgra;
    }

    /// <summary>
    /// Corrige la costura horizontal que deja el bloque swizzle NVN2: en la fila
    /// <c>seamY = (8 &lt;&lt; th) * bh</c> se busca el desplazamiento circular X que mejor
    /// case con la fila anterior y se rotan las filas <c>seamY..h-1</c> (RGBA completo).
    /// </summary>
    private static void FixSeam(byte[] bgra, int width, int height, int bh, int th)
    {
        int seamY = (8 << th) * bh;
        if (seamY <= 0 || seamY >= height) return;

        // Busca el desplazamiento s (par) que minimiza la diferencia RGB fila seamY-1 vs seamY.
        int bestS = 0;
        double bestDiff = double.PositiveInfinity;
        for (int s = 0; s < width; s += 2)
        {
            double diff = 0;
            for (int x = 0; x < width; x++)
            {
                int up = ((seamY - 1) * width + x) * 4;
                int dn = (seamY * width + ((x + s) % width)) * 4;
                diff += Math.Abs(bgra[up] - bgra[dn])
                      + Math.Abs(bgra[up + 1] - bgra[dn + 1])
                      + Math.Abs(bgra[up + 2] - bgra[dn + 2]);
            }
            if (diff < bestDiff) { bestDiff = diff; bestS = s; }
        }
        if (bestS == 0) return;

        // Rota circularmente en X las filas seamY..height-1 preservando RGBA.
        var row = new byte[width * 4];
        for (int y = seamY; y < height; y++)
        {
            int baseIdx = y * width * 4;
            Buffer.BlockCopy(bgra, baseIdx, row, 0, row.Length);
            for (int x = 0; x < width; x++)
            {
                int src = ((x + bestS) % width) * 4;
                int dst = x * 4;
                bgra[baseIdx + dst] = row[src];
                bgra[baseIdx + dst + 1] = row[src + 1];
                bgra[baseIdx + dst + 2] = row[src + 2];
                bgra[baseIdx + dst + 3] = row[src + 3];
            }
        }
    }
}

/// <summary>
/// Deswizzle Block Linear de Tegra X1/X2 (GOB de 64 bytes x 8 filas) para superficies 2D.
/// Las dimensiones llegan en bloques de textura y <paramref name="blockBytes"/> es el
/// tamaÃ±o de cada bloque (4 en RGBA8, 8 en BC4, 16 en ASTC). <paramref name="blockHeightLog2"/>
/// apila <c>1 &lt;&lt; th</c> GOB de alto por unidad de memoria.
/// </summary>
public static class SwitchSwizzle
{
    public static byte[] DeswizzleBlockLinear(byte[] data, int widthBlocks, int heightBlocks,
        int blockBytes, int blockHeightLog2)
    {
        int blockHeight = 8 << Math.Clamp(blockHeightLog2, 0, 5);
        int blocksPerRow = Math.Max(1, 64 / blockBytes);
        int gobCols = (widthBlocks + blocksPerRow - 1) / blocksPerRow;
        byte[] output = new byte[widthBlocks * heightBlocks * blockBytes];

        for (int y = 0; y < heightBlocks; y++)
        {
            int unit = y / blockHeight;
            int inUnit = y % blockHeight;

            for (int x = 0; x < widthBlocks; x++)
            {
                int gobX = x / blocksPerRow;
                int inRow = x % blocksPerRow;

                long offset = (((long)unit * gobCols + gobX) * blockHeight + inUnit) * 64L
                            + (long)inRow * blockBytes;

                if (offset < 0 || offset + blockBytes > data.Length) continue;
                Buffer.BlockCopy(data, (int)offset, output, (y * widthBlocks + x) * blockBytes, blockBytes);
            }
        }

        return output;
    }
}

/// <summary>Decodificadores de texturas de bloque (BC) y conversiÃ³n RGBA8 â†’ BGRA.</summary>
public static class BcCodec
{
    public static byte[] FromRgba8(byte[] block)
    {
        byte[] pixels = new byte[block.Length];
        int pixelCount = block.Length / 4;
        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            int o = i;
            pixels[o] = block[i + 2];     // B
            pixels[o + 1] = block[i + 1]; // G
            pixels[o + 2] = block[i];     // R
            pixels[o + 3] = block[i + 3]; // A
        }
        return pixels;
    }

    public static byte[] FromBc1(byte[] data, int width, int height) => DecodeBlocks(data, width, height, bc3: false);

    public static byte[] FromBc3(byte[] data, int width, int height) => DecodeBlocks(data, width, height, bc3: true);

    private static byte[] DecodeBlocks(byte[] data, int width, int height, bool bc3)
    {
        byte[] pixels = new byte[width * height * 4];
        int blocksW = (width + 3) / 4;
        Span<byte> palette = stackalloc byte[16];

        for (int by = 0; by < (height + 3) / 4; by++)
        {
            for (int bx = 0; bx < blocksW; bx++)
            {
                long blockIndex = (long)by * blocksW + bx;
                long blockStart = blockIndex * (bc3 ? 16 : 8);
                if (blockStart + (bc3 ? 16 : 8) > data.Length) continue;

                // BC3: el alfa se decodifica por pÃ­xel en DecodeBc3Alpha.

                uint color = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)blockStart + (bc3 ? 8 : 0)));
                uint color2 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)blockStart + (bc3 ? 10 : 2)));
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)blockStart + (bc3 ? 12 : 4)));

                FillPalette(color, color2, palette);

                for (int p = 0; p < 16; p++)
                {
                    int px = bx * 4 + p % 4;
                    int py = by * 4 + p / 4;
                    if (px >= width || py >= height) continue;

                    int ci = (int)((indices >> (p * 2)) & 3);
                    int o = (py * width + px) * 4;
                    pixels[o] = palette[ci * 4];
                    pixels[o + 1] = palette[ci * 4 + 1];
                    pixels[o + 2] = palette[ci * 4 + 2];
                    pixels[o + 3] = bc3 ? DecodeBc3Alpha(data, blockStart, p) : (byte)255;
                }
            }
        }

        return pixels;
    }

    private static byte DecodeBc3Alpha(byte[] data, long blockStart, int p)
    {
        byte a0 = data[blockStart];
        byte a1 = data[blockStart + 1];

        int bit = p * 3;
        int idx = (data[blockStart + 2 + bit / 8] >> (bit % 8)) & 7;
        if (bit % 8 > 5)
        {
            int next = blockStart + 3 + bit / 8 < data.Length ? data[blockStart + 3 + bit / 8] : 0;
            idx |= (next << (8 - bit % 8)) & 7;
        }

        if (idx == 0) return a0;
        if (idx == 1) return a1;
        return a0 > a1
            ? (byte)Math.Clamp((a0 * (7 - idx) + a1 * idx) / 7, 0, 255)
            : (byte)Math.Clamp(Math.Max(0, (a0 * (5 - idx) + a1 * (idx - 1)) / 5), 0, 255);
    }

    private static void FillPalette(uint c16a, uint c16b, Span<byte> palette)
    {
        int c0 = UnpackRgb565(c16a);
        int c1 = UnpackRgb565(c16b);

        Span<int> colors = stackalloc int[4 * 3];
        colors[0] = c0 >> 16 & 0xFF; colors[1] = c0 >> 8 & 0xFF; colors[2] = c0 & 0xFF;
        colors[3] = c1 >> 16 & 0xFF; colors[4] = c1 >> 8 & 0xFF; colors[5] = c1 & 0xFF;

        for (int i = 2; i < 4; i++)
        {
            for (int ch = 0; ch < 3; ch++)
            {
                int a = colors[(i - 2) * 3 + ch];
                int b = colors[(i - 1) * 3 + ch];
                colors[i * 3 + ch] = c16a > c16b ? (2 * a + b) / 3 : (a + b) / 2;
            }
        }

        if (c16a <= c16b) colors[11] = 0; // negro para el Ã­ndice 3
        for (int i = 0; i < 4; i++)
        {
            palette[i * 4] = (byte)colors[i * 3 + 2];
            palette[i * 4 + 1] = (byte)colors[i * 3 + 1];
            palette[i * 4 + 2] = (byte)colors[i * 3];
            palette[i * 4 + 3] = 255;
        }
    }

    private static int UnpackRgb565(uint c) =>
        (int)(((c >> 11) * 255 / 31 << 16) | (((c >> 5) & 0x3F) * 255 / 63 << 8) | ((c & 0x1F) * 255 / 31));
}

/// <summary>
/// Parser mÃ­nimo de BYML v7: diccionarios, listas, cadenas, enteros y flotantes.
/// Suficiente para leer los bgyml de calibraciÃ³n de mapas.
/// </summary>
public static class Byml
{
    private const byte ArrayType = 0xC0;
    private const byte StringType = 0xC1;
    private const byte MapType = 0xD0;
    private const byte OrderedMapType = 0xD1;
    private const byte IntType = 0xD2;
    private const byte FloatType = 0xD3;
    private const byte V7StringType = 0xA0;

    public static object? Parse(byte[] data)
    {
        if (data.Length < 0x10) return null;

        // v1-v3: "BY"; v4+ (Switch, p. ej. v7): "YB" (0x4259 little-endian).
        bool magic = (data[0] == (byte)'B' && data[1] == (byte)'Y') ||
                     (data[0] == (byte)'Y' && data[1] == (byte)'B');
        if (!magic) return null;

        int version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (version < 2 || version > 7) return null;

        // En v4+ los offsets son absolutos; en v2/v3 se guardan divididos entre 4.
        bool absolute = version >= 4;

        int nodeDictOffset = ReadOffset(data, 4, absolute);
        int stringDictOffset = ReadOffset(data, 8, absolute);
        int rootOffset = ReadOffset(data, 0xC, absolute);
        if (rootOffset <= 0 || rootOffset + 8 > data.Length)
        {
            Log.Write($"Mapas/BYML: v{version} con raÃ­z invÃ¡lida (0x{rootOffset:X}).");
            return null;
        }

        var strings = ReadStringTable(data, stringDictOffset);
        var nodeNames = ReadStringTable(data, nodeDictOffset);

        Log.Write($"Mapas/BYML: v{version} offsets={(absolute ? "absolutos" : "x4")} " +
                  $"nodos={nodeNames.Count} cadenas={strings.Count} raÃ­z=0x{rootOffset:X}");

        return ReadNode(data, rootOffset, nodeNames, strings, absolute);
    }

    private static int ReadOffset(byte[] data, int position, bool absolute)
    {
        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
        if (absolute) return raw < data.Length ? (int)raw : (int)raw * 4;
        return (int)raw * 4;
    }

    private static List<string> ReadStringTable(byte[] data, int offset)
    {
        var result = new List<string>();
        if (offset <= 0 || offset + 8 > data.Length) return result;

        int count = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16;
        for (int i = 0; i < count; i++)
        {
            int entry = offset + 8 + i * 4;
            if (entry + 4 > data.Length) break;

            int strOffset = offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry));
            if (strOffset < 0 || strOffset >= data.Length) { result.Add(""); continue; }

            int len = 0;
            while (strOffset + len < data.Length && data[strOffset + len] != 0 && len < 512) len++;
            result.Add(Encoding.UTF8.GetString(data, strOffset, len));
        }

        return result;
    }

    /// <summary>Lee el nodo en <paramref name="offset"/>; los contenedores usan offsets absolutos en v4+.</summary>
    private static object? ReadNode(byte[] data, int offset, List<string> nodeNames, List<string> strings,
        bool absolute)
    {
        if (offset < 0 || offset + 8 > data.Length) return null;

        byte type = data[offset];
        int payload = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));

        return type switch
        {
            IntType => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 8)),
            FloatType => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8)),
            StringType or V7StringType => payload >= 0 && payload < strings.Count ? strings[payload] : "",
            ArrayType or MapType or OrderedMapType => ReadContainer(data, absolute ? payload : offset + payload,
                type, nodeNames, strings, absolute),
            _ => null
        };
    }

    private static object ReadContainer(byte[] data, int start, byte type, List<string> nodeNames,
        List<string> strings, bool absolute)
    {
        if (type == ArrayType)
        {
            int count = data[start + 1] | (data[start + 2] << 8) | (data[start + 3] << 16);
            int entriesOffset = start + 8;
            var list = new List<object?>(Math.Min(count, 100_000));
            for (int i = 0; i < count; i++)
            {
                int entry = entriesOffset + i * 4;
                if (entry + 4 > data.Length) break;
                list.Add(ReadValue(data, start, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry)), nodeNames, strings, absolute));
            }
            return list;
        }
        else
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + 2));
            int entriesRaw = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 8));
            int entriesOffset = absolute ? entriesRaw : start + entriesRaw;

            var dict = new Dictionary<string, object?>(Math.Min(count, 100_000));
            for (int i = 0; i < count; i++)
            {
                int entry = entriesOffset + i * 8;
                if (entry + 8 > data.Length) break;

                uint keyRaw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry));
                uint valueRaw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 4));

                string key = type == MapType
                    ? ((int)(keyRaw & 0xFFFFFF) < nodeNames.Count ? nodeNames[(int)(keyRaw & 0xFFFFFF)] : $"idx_{i}")
                    : $"hash_{keyRaw:X8}";

                dict[key] = ReadValue(data, start, valueRaw, nodeNames, strings, absolute);
            }

            return dict;
        }
    }

    /// <summary>Lee un valor de contenedor: los tipos inline traen el dato; los demÃ¡s, un offset.</summary>
    private static object? ReadValue(byte[] data, int containerBase, uint raw, List<string> nodeNames,
        List<string> strings, bool absolute)
    {
        byte type = (byte)(raw >> 24);
        int inlineValue = (int)(raw & 0xFFFFFF) << 8 >> 8; // signo de 24 bits

        return type switch
        {
            IntType => inlineValue,
            FloatType => BitConverter.Int32BitsToSingle(inlineValue),
            StringType or V7StringType => inlineValue >= 0 && inlineValue < strings.Count ? strings[inlineValue] : "",
            ArrayType or MapType or OrderedMapType => ReadContainer(data,
                absolute ? inlineValue : containerBase + inlineValue, type, nodeNames, strings, absolute),
            _ => null
        };
    }
}















