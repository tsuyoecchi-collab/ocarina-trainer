namespace PhykroTrainer.Core;

/// <summary>Módulo para juegos sin datos conocidos: solo habilita el motor y el escáner.</summary>
public sealed class GenericModule : IGameModule
{
    public string Id => "generic";
    public string DisplayName => "Genérico";
    public string DefaultProcessName => "";
    public IReadOnlyList<string> ProcessAliases { get; } = Array.Empty<string>();
    public IReadOnlyList<CheatPresetGroup> PresetGroups { get; } = Array.Empty<CheatPresetGroup>();
    public IReadOnlyList<GameStat> Stats { get; } = Array.Empty<GameStat>();
}

/// <summary>
/// Registro de módulos de juego. El motor de memoria y el escáner son genéricos; aquí se
/// elige qué módulo (proceso + trucos conocidos) corresponde al perfil activo.
/// </summary>
public static class GameModules
{
    private static readonly GenericModule Generic = new();

    private static readonly IGameModule[] Modules =
    {
        new OcarinaModule()
    };

    /// <summary>Módulo por defecto: el juego para el que se preparó el trainer.</summary>
    public static IGameModule Default => Modules[0];

    public static IReadOnlyList<IGameModule> All => Modules;

    /// <summary>
    /// Devuelve el módulo que corresponde al nombre de proceso. Si no coincide con ninguno,
    /// usa el genérico (sin plantillas) para no ofrecer direcciones de otro juego.
    /// </summary>
    public static IGameModule Resolve(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return Default;

        foreach (IGameModule module in Modules)
        {
            if (ProcessNames.Match(module.DefaultProcessName, processName)) return module;
            foreach (string alias in module.ProcessAliases)
            {
                if (ProcessNames.Match(alias, processName)) return module;
            }
        }

        return Generic;
    }
}
