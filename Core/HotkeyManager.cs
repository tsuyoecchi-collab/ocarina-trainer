using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhykroTrainer.Core;

public readonly record struct HotkeySpec(Keys Key, uint Modifiers);

/// <summary>Atajos globales con RegisterHotKey (funcionan aunque el juego tenga el foco).</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int FirstId = 0xB100;
    private readonly Dictionary<int, Action> _actions = new();
    private IntPtr _window;
    private int _nextId = FirstId;

    public List<string> Errors { get; } = new();

    public void SetWindow(IntPtr window) => _window = window;

    public void Clear()
    {
        if (_window != IntPtr.Zero)
        {
            foreach (int id in _actions.Keys) Native.UnregisterHotKey(_window, id);
        }

        _actions.Clear();
        Errors.Clear();
        _nextId = FirstId;
    }

    public bool Register(string? text, Action action)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (_window == IntPtr.Zero) return false;

        if (!TryParse(text, out HotkeySpec spec, out string error))
        {
            Errors.Add($"{text}: {error}");
            return false;
        }

        int id = _nextId++;
        if (!Native.RegisterHotKey(_window, id, spec.Modifiers | Native.ModNoRepeat, (uint)spec.Key))
        {
            int code = Marshal.GetLastWin32Error();
            Errors.Add($"{Format(spec)}: Windows no lo permitió (código {code}); puede estar ocupado por otro programa.");
            return false;
        }

        _actions[id] = action;
        return true;
    }

    /// <summary>Devuelve true si el id correspondía a un atajo nuestro.</summary>
    public bool Handle(int id)
    {
        if (!_actions.TryGetValue(id, out Action? action)) return false;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("atajo global", ex);
        }

        return true;
    }

    public static bool TryParse(string? text, out HotkeySpec spec, out string error)
    {
        spec = default;
        error = "";

        string raw = (text ?? "").Trim();
        if (raw.Length == 0)
        {
            error = "Atajo vacío.";
            return false;
        }

        string[] tokens = raw.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = "Atajo vacío.";
            return false;
        }

        uint modifiers = 0;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Native.ModControl;
                    break;
                case "alt":
                    modifiers |= Native.ModAlt;
                    break;
                case "shift":
                    modifiers |= Native.ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= Native.ModWin;
                    break;
                default:
                    error = $"Modificador desconocido: «{tokens[i]}».";
                    return false;
            }
        }

        if (!TryParseKey(tokens[^1], out Keys key))
        {
            error = $"Tecla desconocida: «{tokens[^1]}».";
            return false;
        }

        bool isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
        if (modifiers == 0 && !isFunctionKey && key is not (Keys.Pause or Keys.PrintScreen or Keys.Scroll))
        {
            error = "Usa Ctrl, Alt o Shift junto a esa tecla (o una tecla F1–F24).";
            return false;
        }

        spec = new HotkeySpec(key, modifiers);
        return true;
    }

    public static string Format(HotkeySpec spec)
    {
        var parts = new List<string>();
        if ((spec.Modifiers & Native.ModControl) != 0) parts.Add("Ctrl");
        if ((spec.Modifiers & Native.ModAlt) != 0) parts.Add("Alt");
        if ((spec.Modifiers & Native.ModShift) != 0) parts.Add("Shift");
        if ((spec.Modifiers & Native.ModWin) != 0) parts.Add("Win");
        parts.Add(KeyLabel(spec.Key));
        return string.Join("+", parts);
    }

    public static string KeyLabel(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return "Num" + (int)(key - Keys.NumPad0);
        return key switch
        {
            Keys.Oemplus => "+",
            Keys.OemMinus => "-",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            _ => key.ToString()
        };
    }

    public static bool TryParseKey(string text, out Keys key)
    {
        key = Keys.None;
        string token = text.Trim();
        if (token.Length == 0) return false;

        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = Keys.A + (c - 'A');
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = Keys.D0 + (c - '0');
                return true;
            }
        }

        if (token.Length == 2 && (token[0] is 'F' or 'f') && char.IsDigit(token[1]))
        {
            int number = token[1] - '0';
            if (number is >= 1 and <= 9)
            {
                key = Keys.F1 + (number - 1);
                return true;
            }
        }

        if (Enum.TryParse(token, ignoreCase: true, out Keys parsed) && IsUsableKey(parsed))
        {
            key = parsed;
            return true;
        }

        string normalized = token.ToLowerInvariant();
        key = normalized switch
        {
            "space" or "espacio" => Keys.Space,
            "tab" => Keys.Tab,
            "enter" or "intro" or "return" => Keys.Enter,
            "esc" or "escape" => Keys.Escape,
            "backspace" or "retroceso" => Keys.Back,
            "ins" or "insert" => Keys.Insert,
            "del" or "supr" or "delete" => Keys.Delete,
            "home" or "inicio" => Keys.Home,
            "end" or "fin" => Keys.End,
            "pgup" or "pageup" or "repag" => Keys.PageUp,
            "pgdn" or "pagedown" or "avpag" => Keys.PageDown,
            "up" or "arriba" => Keys.Up,
            "down" or "abajo" => Keys.Down,
            "left" or "izquierda" => Keys.Left,
            "right" or "derecha" => Keys.Right,
            "prtsc" or "printscreen" or "imprpant" => Keys.PrintScreen,
            "pause" or "pausa" => Keys.Pause,
            _ => Keys.None
        };

        return key != Keys.None;
    }

    private static bool IsUsableKey(Keys key) =>
        key is not (Keys.None or Keys.Control or Keys.ControlKey or Keys.Shift or Keys.ShiftKey or
            Keys.Menu or Keys.LWin or Keys.RWin or Keys.LControlKey or Keys.RControlKey or
            Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu);

    public void Dispose() => Clear();
}
