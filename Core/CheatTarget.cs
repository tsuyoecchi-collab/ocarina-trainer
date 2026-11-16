using System.ComponentModel;
using System.Text.Json.Serialization;

namespace PhykroTrainer.Core;

/// <summary>Un truco: una dirección del juego, un valor y un atajo de teclado.</summary>
public sealed class CheatTarget : INotifyPropertyChanged
{
    private string _name = "";
    private ValueKind _kind = ValueKind.Int32;
    private string _spec = "";
    private string _value = "";
    private string _max = "";
    private string _maxSpec = "";
    private string _hotkey = "";
    private bool _enabled = true;
    private bool _frozen;
    private bool _limitToMax = true;
    private string _note = "";
    private string _currentDisplay = "—";
    private string _status = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public ValueKind Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value)) Notify(nameof(KindLabel));
        }
    }

    public string Spec
    {
        get => _spec;
        set => Set(ref _spec, value);
    }

    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public string Hotkey
    {
        get => _hotkey;
        set => Set(ref _hotkey, value);
    }

    /// <summary>
    /// Tope opcional. Si se rellena, ningún valor escrito o congelado superará esta cifra:
    /// sirve para no poner munición o rupias por encima de lo que admite el juego.
    /// Vacío = sin límite.
    /// </summary>
    public string Max
    {
        get => _max;
        set => Set(ref _max, value);
    }

    /// <summary>
    /// Dirección opcional donde el juego guarda la capacidad real (el máximo) de este
    /// objeto. Si se rellena, el trainer lee esa dirección constantemente y actualiza
    /// «Max» solo. Además, si el truco está congelado y el valor estaba al tope (o por
    /// encima) del máximo anterior, se sube al nuevo máximo.
    /// </summary>
    public string MaxSpec
    {
        get => _maxSpec;
        set => Set(ref _maxSpec, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public bool Frozen
    {
        get => _frozen;
        set => Set(ref _frozen, value);
    }

    /// <summary>
    /// Si está marcado (Sí) y hay un «Máximo» mayor que 0, las escrituras se recortan
    /// siempre a ese máximo: el valor nunca puede superarlo. «No» permite escribir
    /// libremente por encima del máximo.
    /// </summary>
    public bool LimitToMax
    {
        get => _limitToMax;
        set => Set(ref _limitToMax, value);
    }

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    [JsonIgnore]
    public string KindLabel => ValueCodec.ShortLabel(Kind);

    [JsonIgnore]
    public string CurrentDisplay
    {
        get => _currentDisplay;
        set => Set(ref _currentDisplay, value);
    }

    [JsonIgnore]
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CheatTarget Clone(string? newName = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = newName ?? Name,
        Kind = Kind,
        Spec = Spec,
        Value = Value,
        Max = Max,
        MaxSpec = MaxSpec,
        Hotkey = "",
        Enabled = Enabled,
        Frozen = false,
        LimitToMax = LimitToMax,
        Note = Note
    };

    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(property);
        return true;
    }

    private void Notify(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>Posición guardada para el teletransporte.</summary>
public sealed class Waypoint
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string CreatedText => Created.ToString("dd/MM/yy HH:mm");
}

/// <summary>Configuración de un juego concreto (un perfil por proceso).</summary>
public sealed class TrainerProfile
{
    public string ProcessName { get; set; } = GameModules.Default.DefaultProcessName;
    public int FreezeIntervalMs { get; set; } = 50;
    public bool AutoAttach { get; set; } = true;
    public bool RestoreOnExit { get; set; } = true;
    public string HotkeyToggleWindow { get; set; } = "Ctrl+Alt+T";
    public string HotkeySaveWaypoint { get; set; } = "F9";
    public string HotkeyTeleport { get; set; } = "F10";
    public string GamePath { get; set; } = @"E:\OcarinaRemake\Game";
    public string CoordX { get; set; } = "";
    public string CoordY { get; set; } = "";
    public string CoordZ { get; set; } = "";
    public List<CheatTarget> Targets { get; set; } = new();
    public List<Waypoint> Waypoints { get; set; } = new();

    public void Normalize()
    {
        Targets ??= new List<CheatTarget>();
        Waypoints ??= new List<Waypoint>();
        FreezeIntervalMs = Math.Clamp(FreezeIntervalMs, 10, 2000);
        if (string.IsNullOrWhiteSpace(ProcessName)) ProcessName = GameModules.Default.DefaultProcessName;
        if (string.IsNullOrWhiteSpace(GamePath)) GamePath = @"E:\OcarinaRemake\Game";
    }
}

public sealed class ConfigFile
{
    public int Version { get; set; } = 1;
    public string LastProcess { get; set; } = GameModules.Default.DefaultProcessName;
    public List<TrainerProfile> Profiles { get; set; } = new();

    public TrainerProfile GetProfile(string processName)
    {
        Profiles ??= new List<TrainerProfile>();
        string name = string.IsNullOrWhiteSpace(processName) ? GameModules.Default.DefaultProcessName : processName.Trim();

        TrainerProfile? profile = Profiles.FirstOrDefault(p => ProcessNames.Match(p.ProcessName, name));

        if (profile is null)
        {
            profile = new TrainerProfile { ProcessName = name };
            Profiles.Add(profile);
        }
        else
        {
            profile.ProcessName = name;
        }

        profile.Normalize();
        return profile;
    }

    public void Normalize()
    {
        Profiles ??= new List<TrainerProfile>();
        foreach (TrainerProfile profile in Profiles) profile.Normalize();
        if (string.IsNullOrWhiteSpace(LastProcess)) LastProcess = GameModules.Default.DefaultProcessName;
    }
}
