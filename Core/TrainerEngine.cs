using System.Buffers.Binary;
using System.Linq;

namespace PhykroTrainer.Core;

/// <summary>
/// Motor del trainer: resuelve direcciones, escribe valores congelados y guarda
/// los valores originales para poder restaurarlos.
/// </summary>
public sealed class TrainerEngine : IDisposable
{
    private readonly Dictionary<string, long> _staticAddressCache = new();
    private readonly Dictionary<string, OriginalValue> _originals = new();
    private readonly Dictionary<string, string> _lastCapacity = new();
    private bool _disposed;

    public GameMemory? Memory { get; private set; }
    public TrainerProfile? Profile { get; private set; }
    public event Action<string>? Message;

    public IReadOnlyDictionary<string, OriginalValue> Originals => _originals;

    public readonly record struct OriginalValue(long Address, byte[] Bytes);

    public void Attach(GameMemory memory, TrainerProfile profile)
    {
        Memory?.Dispose();
        Memory = memory;
        Profile = profile;
        _staticAddressCache.Clear();
        _originals.Clear();
    }

    public void Detach()
    {
        Memory?.Dispose();
        Memory = null;
        _staticAddressCache.Clear();
        _originals.Clear();
    }

    public bool IsConnected => Memory is { IsAlive: true };

    public bool TryResolve(CheatTarget target, out long address, out string error)
    {
        address = 0;
        error = "Sin conexión.";

        GameMemory? memory = Memory;
        if (memory is null || !memory.IsAlive) return false;

        if (string.IsNullOrWhiteSpace(target.Spec))
        {
            error = "Falta la dirección.";
            return false;
        }

        if (!AddressSpec.TryParse(target.Spec, out AddressSpec spec, out error)) return false;

        if (!spec.IsPointerChain && _staticAddressCache.TryGetValue(target.Spec, out long cached))
        {
            address = cached;
            return true;
        }

        if (!spec.TryResolve(memory, out address, out error)) return false;

        if (!spec.IsPointerChain) _staticAddressCache[target.Spec] = address;
        return true;
    }

    public bool RefreshCurrent(CheatTarget target)
    {
        SyncMax(target);

        if (!TryResolve(target, out long address, out string error))
        {
            target.CurrentDisplay = "—";
            if (target.Status != error) target.Status = error;
            return false;
        }

        byte[] buffer = new byte[ValueCodec.Size(target.Kind)];
        if (!Memory!.TryRead(address, buffer))
        {
            const string message = "No se pudo leer.";
            target.CurrentDisplay = "—";
            if (target.Status != message) target.Status = message;
            return false;
        }

        target.CurrentDisplay = ValueCodec.Format(target.Kind, buffer);
        if (target.Status.StartsWith("Error", StringComparison.Ordinal) ||
            target.Status.StartsWith("No ", StringComparison.Ordinal) ||
            target.Status.StartsWith("Sin ", StringComparison.Ordinal) ||
            target.Status.StartsWith("Falta", StringComparison.Ordinal))
        {
            target.Status = "";
        }

        return true;
    }

    /// <summary>Escribe el valor del truco una vez. Guarda el original la primera vez.</summary>
    public bool Apply(CheatTarget target, bool captureOriginal = true, bool quiet = false)
    {
        SyncMax(target);

        if (!TryResolve(target, out long address, out string error))
        {
            SetStatus(target, error);
            return false;
        }

        string valueText = ClampToMax(target, out bool clamped, out string maxText);
        if (!ValueCodec.TryParse(target.Kind, valueText, out byte[] bytes, out error))
        {
            SetStatus(target, error);
            return false;
        }

        if (captureOriginal)
        {
            string key = $"{target.Id}:{address:X}";
            if (!_originals.ContainsKey(key))
            {
                byte[] original = new byte[bytes.Length];
                if (Memory!.TryRead(address, original))
                    _originals[key] = new OriginalValue(address, original);
            }
        }

        if (!Memory!.TryWrite(address, bytes))
        {
            SetStatus(target, "No se pudo escribir.");
            return false;
        }

        target.CurrentDisplay = ValueCodec.Format(target.Kind, bytes);
        if (target.Status.Length != 0) target.Status = "";
        if (clamped) target.Status = $"Limitado al máximo {maxText}.";
        if (!quiet) Message?.Invoke($"{target.Name}: {target.CurrentDisplay}");
        return true;
    }

    /// <summary>
    /// Si el truco tiene una dirección de máximo (MaxSpec), lee la capacidad real que guarda
    /// el juego y actualiza «Max» solo. Si el truco está congelado y su valor estaba al tope
    /// (o por encima) del máximo anterior, lo sube al nuevo máximo. Devuelve true si Max cambió.
    /// </summary>
    public bool SyncMax(CheatTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.MaxSpec)) return false;
        if (Memory is null || !Memory.IsAlive) return false;
        if (!AddressSpec.TryParse(target.MaxSpec, out AddressSpec spec, out _)) return false;
        if (!spec.TryResolve(Memory, out long address, out _)) return false;

        byte[] buffer = new byte[ValueCodec.Size(target.Kind)];
        if (!Memory.TryRead(address, buffer)) return false;

        string maxText = ValueCodec.Format(target.Kind, buffer);
        if (string.Equals(target.Max, maxText, StringComparison.Ordinal)) return false;

        string previous = _lastCapacity.TryGetValue(target.Id, out string? old) ? old : "";
        _lastCapacity[target.Id] = maxText;
        target.Max = maxText;

        // Si limitamos y el valor quedó por encima del nuevo máximo (p. ej. el máximo baja
        // de 500 a 200 y el valor era 500), se baja al máximo aunque no esté congelado.
        if (target.LimitToMax && ValueExceedsMax(target, maxText))
            target.Value = maxText;
        else if (target.Frozen && TargetWasFull(target, previous, maxText))
            target.Value = maxText;

        if (target.Status.Length == 0)
            target.Status = $"Máximo del juego: {maxText}.";
        return true;
    }

    private static bool ValueExceedsMax(CheatTarget target, string maxText)
    {
        if (!ValueCodec.TryParse(target.Kind, target.Value, out byte[] value, out _) ||
            !ValueCodec.TryParse(target.Kind, maxText, out byte[] maxBytes, out _))
        {
            return false;
        }

        if (maxBytes.All(b => b == 0)) return false;
        return ValueCodec.Compare(target.Kind, value, maxBytes) > 0;
    }

    private static bool TargetWasFull(CheatTarget target, string previous, string maxText)
    {
        // Sin registro previo: se considera a tope cuando el valor actual ya supera o iguala
        // el nuevo máximo (p. ej. 39 bombas con máximo 30 → se recorta a 30). Si el valor es
        // menor (5 semillas con máximo 50), no se fuerza a llenar.
        if (previous.Length == 0)
        {
            if (!ValueCodec.TryParse(target.Kind, target.Value, out byte[] value, out _) ||
                !ValueCodec.TryParse(target.Kind, maxText, out byte[] newMax, out _))
            {
                return false;
            }

            return ValueCodec.Compare(target.Kind, value, newMax) >= 0;
        }

        if (!ValueCodec.TryParse(target.Kind, target.Value, out byte[] current, out _) ||
            !ValueCodec.TryParse(target.Kind, previous, out byte[] oldMax, out _))
        {
            return false;
        }

        return ValueCodec.Compare(target.Kind, current, oldMax) >= 0;
    }

    /// <summary>Devuelve el texto de valor que hay que escribir, recortado al tope «Máximo» del truco
    /// si se ha rellenado y lo supera.
    /// </summary>
    private static string ClampToMax(CheatTarget target, out bool clamped, out string maxText)
    {
        clamped = false;
        string value = target.Value;
        maxText = (target.Max ?? "").Trim();
        if (!target.LimitToMax || maxText.Length == 0) return value;

        if (!ValueCodec.TryParse(target.Kind, value, out byte[] valueBytes, out _)) return value;
        if (!ValueCodec.TryParse(target.Kind, maxText, out byte[] maxBytes, out _)) return value;
        if (maxBytes.All(b => b == 0)) return value;
        if (ValueCodec.Compare(target.Kind, valueBytes, maxBytes) <= 0) return value;

        clamped = true;
        return ValueCodec.Format(target.Kind, maxBytes);
    }

    /// <summary>Escribe todos los trucos activos con «Congelar» marcado.</summary>
    public void FreezeTick()
    {
        TrainerProfile? profile = Profile;
        if (profile is null || Memory is null || !Memory.IsAlive) return;

        foreach (CheatTarget target in profile.Targets)
        {
            if (!target.Enabled || !target.Frozen) continue;

            SyncMax(target);

            if (!TryResolve(target, out long address, out string error) ||
                !ValueCodec.TryParse(target.Kind, ClampToMax(target, out _, out _),
                    out byte[] bytes, out error))
            {
                if (target.Status != error) target.Status = error;
                continue;
            }

            string key = $"{target.Id}:{address:X}";
            if (!_originals.ContainsKey(key))
            {
                byte[] original = new byte[bytes.Length];
                if (Memory.TryRead(address, original))
                    _originals[key] = new OriginalValue(address, original);
            }

            if (Memory.TryWrite(address, bytes))
            {
                if (target.Status.Length != 0) target.Status = "";
            }
            else if (target.Status != "No se pudo escribir.")
            {
                target.Status = "No se pudo escribir.";
            }
        }
    }

    public int RestoreAll()
    {
        if (Memory is null || !Memory.IsAlive || _originals.Count == 0) return 0;

        int restored = 0;
        foreach (OriginalValue original in _originals.Values)
        {
            if (Memory.TryWrite(original.Address, original.Bytes)) restored++;
        }

        _originals.Clear();
        foreach (CheatTarget target in Profile?.Targets ?? new List<CheatTarget>())
        {
            target.Frozen = false;
            if (target.Status.StartsWith("No ", StringComparison.Ordinal)) target.Status = "";
        }

        Message?.Invoke($"Restaurados {restored} valores originales.");
        return restored;
    }

    public void ToggleFreeze(CheatTarget target)
    {
        target.Enabled = true;
        target.Frozen = !target.Frozen;

        if (target.Frozen) Apply(target, quiet: true);
        Message?.Invoke($"{target.Name}: {(target.Frozen ? "congelado ON" : "congelado OFF")}");
    }

    // ----- Coordenadas y waypoints -----

    public bool TryReadCoordinate(string specText, out float value, out string error)
    {
        value = 0;
        error = "Sin conexión.";
        if (Memory is null || !Memory.IsAlive) return false;
        if (string.IsNullOrWhiteSpace(specText))
        {
            error = "Falta configurar la dirección.";
            return false;
        }

        if (!AddressSpec.TryParse(specText, out AddressSpec spec, out error)) return false;
        if (!spec.TryResolve(Memory, out long address, out error)) return false;

        byte[] buffer = new byte[4];
        if (!Memory.TryRead(address, buffer))
        {
            error = "No se pudo leer la coordenada.";
            return false;
        }

        value = BinaryPrimitives.ReadSingleLittleEndian(buffer);
        return true;
    }

    public bool TryReadPosition(out float x, out float y, out float z, out string error)
    {
        x = y = z = 0;
        error = "";
        TrainerProfile? profile = Profile;
        if (profile is null)
        {
            error = "Sin perfil.";
            return false;
        }

        if (!TryReadCoordinate(profile.CoordX, out x, out error)) return false;
        if (!TryReadCoordinate(profile.CoordY, out y, out error)) return false;
        if (!TryReadCoordinate(profile.CoordZ, out z, out error)) return false;
        return true;
    }

    public bool TryWritePosition(double x, double y, double z, out string error)
    {
        error = "Sin conexión.";
        TrainerProfile? profile = Profile;
        if (profile is null || Memory is null || !Memory.IsAlive) return false;

        string[] specs = { profile.CoordX, profile.CoordY, profile.CoordZ };
        double[] values = { x, y, z };

        for (int i = 0; i < 3; i++)
        {
            if (string.IsNullOrWhiteSpace(specs[i]))
            {
                error = "Faltan las direcciones X/Y/Z en la pestaña Waypoints.";
                return false;
            }

            if (!AddressSpec.TryParse(specs[i], out AddressSpec spec, out error) ||
                !spec.TryResolve(Memory, out long address, out error))
            {
                return false;
            }

            byte[] bytes = ValueCodec.EncodeFloat((float)values[i]);
            if (!Memory.TryWrite(address, bytes))
            {
                error = "No se pudo escribir la posición.";
                return false;
            }
        }

        Message?.Invoke("Teletransporte aplicado.");
        return true;
    }

    public void SetStatus(CheatTarget target, string status) => target.Status = status;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Memory?.Dispose();
        Memory = null;
    }
}
