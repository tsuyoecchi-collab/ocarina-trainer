using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhykroTrainer.Core;

/// <summary>
/// Entrada de «estadisticas.json» tal como la edita el usuario (nombres en español).
/// El entrenador la mapea a <see cref="GameStat"/>.
/// </summary>
public sealed class GameStatDto
{
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ValueKind Tipo { get; set; } = ValueKind.Int32;
    public int Maximo { get; set; }
    public string Nota { get; set; } = "";
    /// <summary>Dirección opcional donde el juego guarda el máximo real de este dato (p. ej.
    /// la capacidad de la bolsa). Si se rellena, el trainer lee esa dirección y muestra el
    /// valor en la columna «Máximo» en vez del número fijo de «Maximo». Vacía = sin máximo
    /// leído de memoria (se usa «Maximo» como tope fijo). Mismas sintaxis que un truco.</summary>
    public string DireccionMaximo { get; set; } = "";
}

/// <summary>
/// Carga las estadísticas mostradas en la página «Estadísticas» desde un archivo JSON
/// editable junto al ejecutable (o en %APPDATA%). Si el archivo no existe o está roto, se
/// genera uno por defecto con los 33 registros de la tabla de estadísticas del juego y la
/// Skulltula etiquetada. Así el usuario cambia nombres, direcciones, tipos o máximos sin
/// recompilar: basta editar el JSON y volver a conectar.
/// </summary>
public static class StatsCatalog
{
    public static string StatsPath => Path.Combine(Paths.DataDir, "estadisticas.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static IReadOnlyList<GameStat>? _cache;

    /// <summary>Últimas estadísticas cargadas. Llamar a <see cref="Reload"/> para releer el archivo.</summary>
    public static IReadOnlyList<GameStat> Current => _cache ??= Load();

    /// <summary>Recarga desde el archivo (al conectar o al cambiar de perfil).</summary>
    public static void Reload() => _cache = Load();

    /// <summary>Expone los valores por defecto para pruebas (no lee el archivo).</summary>
    public static IReadOnlyList<GameStat> BuildDefaultsForTest() => BuildDefaults();

    private static IReadOnlyList<GameStat> Load()
    {
        try
        {
            if (File.Exists(StatsPath))
            {
                List<GameStatDto>? file = JsonSerializer.Deserialize<List<GameStatDto>>(File.ReadAllText(StatsPath), Options);
                if (file is { Count: > 0 })
                {
                    var list = file.Select(Map).Where(s => s.Spec.Length > 0).ToList();
                    if (list.Count > 0) return list;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("cargar estadisticas.json", ex);
        }

        var defaults = BuildDefaults();
        TryWriteDefaults(defaults);
        return defaults;
    }

    private static void TryWriteDefaults(List<GameStat> stats)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            var dtos = stats.Select(s => new GameStatDto
            {
                Nombre = s.Name,
                Direccion = s.Spec,
                Tipo = s.Kind,
                Maximo = s.Max,
                DireccionMaximo = s.MaxSpec,
                Nota = s.Note
            }).ToList();
            File.WriteAllText(StatsPath, JsonSerializer.Serialize(dtos, Options));
        }
        catch (Exception ex)
        {
            Log.Error("crear estadisticas.json", ex);
        }
    }

    internal static GameStat Map(GameStatDto d) => new()
    {
        Name = string.IsNullOrWhiteSpace(d.Nombre) ? "—" : d.Nombre.Trim(),
        Spec = (d.Direccion ?? "").Trim(),
        Kind = d.Tipo,
        Max = Math.Max(0, d.Maximo),
        MaxSpec = (d.DireccionMaximo ?? "").Trim(),
        Note = d.Nota ?? ""
    };

    /// <summary>
    /// Defecto: los 33 registros de la tabla de estadísticas (clase 0x04C20E98, stride 0x30,
    /// valor en +0x20). El 8.º registro es la Skulltula dorada verificada.
    /// </summary>
    private static List<GameStat> BuildDefaults()
    {
        const long tableBase = 0x1ADEB54D0;
        const int stride = 0x30;
        const int valueOffset = 0x20;
        const long skullRecord = 0x1ADEB5620; // 8.º registro = Skulltulas

        var list = new List<GameStat>(33);
        for (int i = 0; i < 33; i++)
        {
            long record = tableBase + (long)i * stride;
            bool skull = record == skullRecord;
            bool swordAttacks = i == 21;
            list.Add(new GameStat
            {
                Name = skull ? "Skulltulas doradas" : (swordAttacks ? "Ataques con espada" : $"Valor {i + 1}"),
                Spec = "0x" + (record + valueOffset).ToString("X"),
                Kind = ValueKind.Int32,
                Max = skull ? 100 : 0,
                MaxSpec = "",
                Note = skull
                    ? "Total de la partida, no por zona (tabla de estadísticas)."
                    : (swordAttacks
                        ? "Contador de ataques con espada (tabla de estadísticas del juego)."
                        : $"Registro {i + 1} de la tabla de estadísticas del juego.")
            });
        }
        return list;
    }
}
