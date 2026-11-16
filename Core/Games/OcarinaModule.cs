namespace PhykroTrainer.Core;

/// <summary>
/// Módulo para Ocarina of Time Remake (runtime NX2navi). Direcciones verificadas en la
/// versión 1.1.1: si el juego se actualiza, vuelve a localizarlas con el Escáner y edita
/// este archivo. Todo lo específico del juego vive aquí.
/// </summary>
public sealed class OcarinaModule : IGameModule
{
    public string Id => "ocarina-oot-remake";
    public string DisplayName => "Ocarina of Time Remake";
    public string DefaultProcessName => "ocarina.exe";
    public IReadOnlyList<string> ProcessAliases { get; } = new[] { "ocarina.exe", "ocarina" };

    public IReadOnlyList<CheatPresetGroup> PresetGroups { get; } = BuildPresets();

    /// <summary>
    /// Contadores de la partida, cargados desde «estadisticas.json» (se genera solo junto
    /// al ejecutable la primera vez). Se leen cada pocos cientos de milisegundos y solo se
    /// muestran: el juego los recalcula desde sus propios datos, así que escribirlos no sirve.
    /// </summary>
    public IReadOnlyList<GameStat> Stats => StatsCatalog.Current;

    private static IReadOnlyList<CheatPresetGroup> BuildPresets()
    {
        var rupees = new CheatPreset
        {
            Name = "Rupias",
            Spec = "0x1ADD3E134",
            Value = "500",
            Max = "500",
            MaxSpec = "0x1ADD3E364",
            Hotkey = "Ctrl+Alt+D",
            Note = "Monedero real del juego (int32). El espejo 0x1ADD3E138 lo sincroniza el " +
                   "propio juego. Si tras una actualización del juego deja de funcionar, vuelve " +
                   "a buscarlo con el Escáner."
        };

        (string Name, string Spec, int Fallback, string Max, string MaxSpec)[] ammo =
        {
            ("Semillas Deku", "0x1ADD3EA44", 50, "50", "0x1ADD3E314"),
            ("Palos Deku", "0x1ADD3EA6C", 30, "30", "0x1ADD3E33C"),
            ("Nuez Deku", "0x1ADD3EA1C", 40, "30", "0x1ADD3E2EC"),
            ("Bombas", "0x1ADD3E83C", 40, "40", "0x1ADD3E2C4"),
            ("Bombchu", "0x1ADD3E8DC", 50, "40", ""),
            ("Bulbo Mercader", "0x1ADD3F00C", 30, "", "")
        };

        var ammoPresets = ammo.Select(a => new CheatPreset
        {
            Name = a.Name,
            Spec = a.Spec,
            Value = a.Fallback.ToString(),
            Max = a.Max,
            MaxSpec = a.MaxSpec,
            Note = (a.MaxSpec.Length > 0
                ? "Consumible de Ocarina 1.1.1. El máximo se lee del juego automáticamente: " +
                  "si consigues una bolsa mayor, la columna «Máximo» y el stock se actualizan solos. "
                : "Consumible de Ocarina 1.1.1. Congelado en el valor que tenía al añadirlo: " +
                  "no baja al usarlo. ")
                + "El espejo de +4 no se toca. «Máximo» evita pasarse de la capacidad que da el juego.",
            FreezeByDefault = true,
            CaptureCurrentValue = true
        }).ToList();

        return new[]
        {
            new CheatPresetGroup
            {
                Name = "Rupias · 0x1ADD3E134 · atajo Ctrl+Alt+D",
                Description = "Añade el truco de rupias ya configurado. El atajo congela/descongela el valor.",
                Cheats = new[] { rupees }
            },
            new CheatPresetGroup
            {
                Name = "Munición infinita · 6 tipos (semillas, palos, bombas…)",
                Description = "Añade y congela semillas, palos, nueces, bombas, bombchus y bulbos en su valor actual.",
                Cheats = ammoPresets
            }
        };
    }
}
