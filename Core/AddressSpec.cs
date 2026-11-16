using System.Globalization;
using System.Text;

namespace PhykroTrainer.Core;

/// <summary>
/// Dirección de un valor dentro del juego. Formatos aceptados:
///   0x7FF7A2B01234                     dirección absoluta
///   ocarina.exe+0x1A2B3C               base del módulo + desplazamiento
///   ocarina.exe+0x1A2B3C,0x10,0x8      cadena de punteros (offsets en hexadecimal)
/// </summary>
public sealed class AddressSpec
{
    public string Text { get; private init; } = "";
    public string? ModuleName { get; private init; }
    public long Offset { get; private init; }
    public long[] Chain { get; private init; } = Array.Empty<long>();
    public bool IsAbsolute => ModuleName is null;
    public bool IsPointerChain => Chain.Length > 0;

    public static bool TryParse(string? text, out AddressSpec spec, out string error)
    {
        spec = new AddressSpec();
        error = "";

        string raw = (text ?? "").Trim();
        if (raw.Length == 0)
        {
            error = "Falta la dirección.";
            return false;
        }

        string[] parts = raw.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Dirección vacía.";
            return false;
        }

        string first = parts[0];
        string? module = null;
        long offset;

        int plus = first.LastIndexOf('+');
        if (plus >= 0)
        {
            module = first[..plus].Trim();
            if (module.Length == 0)
            {
                error = "Escribe el nombre del módulo antes del «+» (ejemplo: ocarina.exe+0x1A2B3C).";
                return false;
            }

            string offsetText = first[(plus + 1)..].Trim();
            if (!TryParseHex(offsetText, out offset))
            {
                error = $"Desplazamiento no válido: '{offsetText}'.";
                return false;
            }
        }
        else
        {
            if (!TryParseHex(first, out offset))
            {
                error = "Dirección no válida. Usa 0x…, modulo.exe+0x… o una cadena con comas.";
                return false;
            }

            // Un número corto sin 0x casi siempre es un despiste: exigimos 0x para
            // direcciones absolutas pequeñas, pero aceptamos las largas al pegarlas
            // desde Cheat Engine (p. ej. 7FF7A2B01234).
            bool hasPrefix = first.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!hasPrefix && first.Length < 9)
            {
                error = "Para una dirección absoluta escribe 0x delante (ejemplo: 0x7FF7A2B01234).";
                return false;
            }
        }

        var chain = new List<long>();
        for (int i = 1; i < parts.Length; i++)
        {
            if (!TryParseHex(parts[i], out long step))
            {
                error = $"Offset de puntero no válido: '{parts[i]}'.";
                return false;
            }

            chain.Add(step);
        }

        spec = new AddressSpec
        {
            Text = raw,
            ModuleName = module,
            Offset = offset,
            Chain = chain.ToArray()
        };

        return true;
    }

    /// <summary>Resuelve la dirección final donde vive el valor.</summary>
    public bool TryResolve(GameMemory memory, out long address, out string error)
    {
        address = 0;
        error = "";

        long current;
        if (ModuleName is null)
        {
            current = Offset;
        }
        else
        {
            if (memory.FindModule(ModuleName) is not { } module)
            {
                error = $"No encuentro el módulo «{ModuleName}» en el proceso.";
                return false;
            }

            current = unchecked(module.Base + Offset);
        }

        if (current <= 0)
        {
            error = "Dirección resultante no válida.";
            return false;
        }

        for (int i = 0; i < Chain.Length; i++)
        {
            byte[] pointer = new byte[memory.PointerSize];
            if (!memory.TryRead(current, pointer))
            {
                error = $"No se puede leer el puntero en 0x{current:X}.";
                return false;
            }

            long next = memory.PointerSize == 8
                ? unchecked((long)BitConverter.ToUInt64(pointer, 0))
                : BitConverter.ToUInt32(pointer, 0);

            if (next <= 0 || next > 0x7FFF_FFFF_FFFF)
            {
                error = "Puntero nulo o fuera de rango.";
                return false;
            }

            current = unchecked(next + Chain[i]);
        }

        if (current <= 0)
        {
            error = "Dirección final no válida.";
            return false;
        }

        address = current;
        return true;
    }

    /// <summary>
    /// Describe una dirección para que Cheat Engine la acepte. Si cae dentro del
    /// módulo principal se guarda como módulo+offset (más estable entre partidas).
    /// </summary>
    public static string Describe(long address, GameModule? mainModule)
    {
        if (mainModule is { } module &&
            address >= module.Base && address < module.Base + module.Size)
        {
            long relative = address - module.Base;
            return $"{module.Name}+0x{relative:X}";
        }

        return $"0x{address:X}";
    }

    private static bool TryParseHex(string text, out long value)
    {
        value = 0;
        string body = text.Trim();
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) body = body[2..];
        if (body.Length == 0) return false;

        if (!body.All(Uri.IsHexDigit)) return false;
        return long.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(ModuleName is null ? $"0x{Offset:X}" : $"{ModuleName}+0x{Offset:X}");
        foreach (long offset in Chain) builder.Append($",0x{offset:X}");
        return builder.ToString();
    }
}
