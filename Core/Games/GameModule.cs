namespace PhykroTrainer.Core;

/// <summary>
/// Plantilla de un truco que aporta un módulo de juego. El motor y el escáner no saben
/// de juegos: cada módulo declara aquí sus direcciones conocidas.
/// </summary>
public sealed class CheatPreset
{
    public required string Name { get; init; }
    public ValueKind Kind { get; init; } = ValueKind.Int32;
    public required string Spec { get; init; }
    public string Value { get; init; } = "";
    public string Max { get; init; } = "";
    public string MaxSpec { get; init; } = "";
    public string Hotkey { get; init; } = "";
    public string Note { get; init; } = "";

    /// <summary>Se añade con «Congelar» activo (p. ej. munición infinita).</summary>
    public bool FreezeByDefault { get; init; }

    /// <summary>Al añadirlo se lee el valor actual del juego en vez de usar «Value».</summary>
    public bool CaptureCurrentValue { get; init; }
}

/// <summary>Conjunto de trucos que se añaden de una sola vez desde una entrada de menú.</summary>
public sealed class CheatPresetGroup
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required IReadOnlyList<CheatPreset> Cheats { get; init; }
}

/// <summary>
/// Dato de progreso de la partida que aporta un módulo de juego. Es de solo lectura: la
/// barra lateral lo muestra y lo refresca, pero no se escribe ni se puede congelar, porque
/// el juego recalcula estos contadores desde sus propios datos internos.
/// </summary>
public sealed class GameStat
{
    /// <summary>Nombre corto que se ve en la barra lateral.</summary>
    public required string Name { get; init; }

    /// <summary>Dirección del contador (mismas sintaxis que un truco).</summary>
    public required string Spec { get; init; }

    public ValueKind Kind { get; init; } = ValueKind.Int32;

    /// <summary>Máximo conocido del juego; 0 = sin tope.</summary>
    public int Max { get; init; }

    /// <summary>Dirección opcional donde el juego guarda la capacidad real de este dato
    /// (p. ej. la capacidad de la bolsa). Si tiene valor, la UI lee esa dirección y muestra
    /// el máximo leído en vez del固定 «Max». Vacía = usar «Max» fijo. Estilo TrainerEngine.SyncMax.</summary>
    public string MaxSpec { get; init; } = "";

    /// <summary>Aclaración que se muestra bajo el valor (por ejemplo, si es total o por zona).</summary>
    public string Note { get; init; } = "";
}

/// <summary>
/// Módulo de un juego concreto: aísla el proceso, las direcciones y las plantillas de
/// trucos para que el resto del trainer siga siendo genérico y reutilizable. Para añadir
/// otro juego basta con una nueva implementación y registrarla en <see cref="GameModules"/>.
/// </summary>
public interface IGameModule
{
    /// <summary>Identificador estable (no se muestra al usuario).</summary>
    string Id { get; }

    /// <summary>Nombre visible del juego.</summary>
    string DisplayName { get; }

    /// <summary>Proceso por defecto cuando aún no hay perfil guardado.</summary>
    string DefaultProcessName { get; }

    /// <summary>Otros nombres de proceso que se consideran este mismo juego.</summary>
    IReadOnlyList<string> ProcessAliases { get; }

    /// <summary>Trucos conocidos, agrupados como aparecen en «Plantillas».</summary>
    IReadOnlyList<CheatPresetGroup> PresetGroups { get; }

    /// <summary>Contadores de progreso de solo lectura que se muestran en la barra lateral.</summary>
    IReadOnlyList<GameStat> Stats { get; }
}
