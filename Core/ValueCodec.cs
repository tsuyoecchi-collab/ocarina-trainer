using System.Buffers.Binary;
using System.Globalization;

namespace PhykroTrainer.Core;

public enum ValueKind
{
    Byte,
    Int16,
    Int32,
    Int64,
    Float,
    Double
}

/// <summary>Convierte valores entre texto y bytes, y los compara según su tipo.</summary>
public static class ValueCodec
{
    public static int Size(ValueKind kind) => kind switch
    {
        ValueKind.Byte => 1,
        ValueKind.Int16 => 2,
        ValueKind.Int32 => 4,
        ValueKind.Int64 => 8,
        ValueKind.Float => 4,
        ValueKind.Double => 8,
        _ => 4
    };

    public static string ShortLabel(ValueKind kind) => kind switch
    {
        ValueKind.Byte => "Byte",
        ValueKind.Int16 => "Int16",
        ValueKind.Int32 => "Int32",
        ValueKind.Int64 => "Int64",
        ValueKind.Float => "Float",
        ValueKind.Double => "Double",
        _ => "Int32"
    };

    public static string LongLabel(ValueKind kind) => kind switch
    {
        ValueKind.Byte => "Entero 8 bits (0-255)",
        ValueKind.Int16 => "Entero 16 bits",
        ValueKind.Int32 => "Entero 32 bits",
        ValueKind.Int64 => "Entero 64 bits",
        ValueKind.Float => "Decimal 32 bits (float)",
        ValueKind.Double => "Decimal 64 bits (double)",
        _ => "Entero 32 bits"
    };

    public static bool TryParse(ValueKind kind, string? text, out byte[] bytes, out string error)
    {
        bytes = new byte[Size(kind)];
        error = "";

        string value = (text ?? "").Trim();
        if (value.Length == 0)
        {
            error = "Falta el valor.";
            return false;
        }

        try
        {
            switch (kind)
            {
                case ValueKind.Byte:
                {
                    long number = ParseInteger(value, 0, byte.MaxValue);
                    bytes[0] = (byte)number;
                    break;
                }
                case ValueKind.Int16:
                    BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)ParseInteger(value, short.MinValue, short.MaxValue));
                    break;
                case ValueKind.Int32:
                    BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)ParseInteger(value, int.MinValue, int.MaxValue));
                    break;
                case ValueKind.Int64:
                    BinaryPrimitives.WriteInt64LittleEndian(bytes, ParseInteger(value, long.MinValue, long.MaxValue));
                    break;
                case ValueKind.Float:
                    BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)ParseDecimal(value));
                    break;
                case ValueKind.Double:
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes, ParseDecimal(value));
                    break;
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            error = $"'{value}' no es un {ShortLabel(kind)} válido.";
            return false;
        }
    }

    public static string Format(ValueKind kind, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size(kind))
            return "—";

        return kind switch
        {
            ValueKind.Byte => bytes[0].ToString(CultureInfo.InvariantCulture),
            ValueKind.Int16 => BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            ValueKind.Int32 => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            ValueKind.Int64 => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            ValueKind.Float => FormatFloat(BinaryPrimitives.ReadSingleLittleEndian(bytes)),
            ValueKind.Double => FormatFloat(BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
            _ => "—"
        };
    }

    public static byte[] EncodeInt(ValueKind kind, long value)
    {
        byte[] bytes = new byte[Size(kind)];
        switch (kind)
        {
            case ValueKind.Byte:
                bytes[0] = unchecked((byte)value);
                break;
            case ValueKind.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(bytes, unchecked((short)value));
                break;
            case ValueKind.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(bytes, unchecked((int)value));
                break;
            case ValueKind.Int64:
                BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
                break;
            case ValueKind.Float:
                BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
                break;
            case ValueKind.Double:
                BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
                break;
        }

        return bytes;
    }

    public static byte[] EncodeFloat(float value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }

    public static float DecodeFloat(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : 0f;

    /// <summary>Compara numéricamente dos valores del mismo tipo. Devuelve &lt;0, 0 o &gt;0.</summary>
    public static int Compare(ValueKind kind, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return kind switch
        {
            ValueKind.Byte => a[0].CompareTo(b[0]),
            ValueKind.Int16 => BinaryPrimitives.ReadInt16LittleEndian(a).CompareTo(BinaryPrimitives.ReadInt16LittleEndian(b)),
            ValueKind.Int32 => BinaryPrimitives.ReadInt32LittleEndian(a).CompareTo(BinaryPrimitives.ReadInt32LittleEndian(b)),
            ValueKind.Int64 => BinaryPrimitives.ReadInt64LittleEndian(a).CompareTo(BinaryPrimitives.ReadInt64LittleEndian(b)),
            ValueKind.Float => BinaryPrimitives.ReadSingleLittleEndian(a).CompareTo(BinaryPrimitives.ReadSingleLittleEndian(b)),
            ValueKind.Double => BinaryPrimitives.ReadDoubleLittleEndian(a).CompareTo(BinaryPrimitives.ReadDoubleLittleEndian(b)),
            _ => 0
        };
    }

    private static string FormatFloat(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNegativeInfinity(value)) return "-∞";

        string text = value.ToString("0.######", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    private static long ParseInteger(string text, long min, long max)
    {
        bool negative = text.StartsWith('-');
        string body = negative ? text[1..] : text;
        if (body.Length == 0)
            throw new FormatException();

        long value;
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            ulong raw = Convert.ToUInt64(body[2..], 16);
            value = unchecked((long)raw);
        }
        else
        {
            value = long.Parse(body, NumberStyles.None, CultureInfo.InvariantCulture);
            if (negative) value = -value;
        }

        if (value < min || value > max)
            throw new OverflowException();

        return value;
    }

    private static double ParseDecimal(string text)
    {
        string normalized = text.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return value;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return value;
        throw new FormatException();
    }
}
