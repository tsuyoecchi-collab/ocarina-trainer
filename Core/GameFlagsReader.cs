using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace PhykroTrainer.Core;

/// <summary>
/// Lector de solo lectura de las banderas de partida (Game Data Flags) del juego.
/// Estructura (todas las direcciones absolutas, proceso x64):
///   0x7004C7E180            puntero -> slot
///   slot                    puntero -> manager
///   manager+0x10            uint32 count (número de entradas)
///   manager+0x18            puntero -> entradas (cada una de 0x18 bytes;
///                           el estado vive en entry+0x16 y el bit 0 indica «conseguida»)
///   manager+0x20            puntero -> tabla hash de {uint32 hash, uint32 index} (8 bytes por fila)
///   manager+0x2c            uint32 tamaño de la tabla
/// El hash de cada nombre se calcula con MurmurHash3 x86_32 (semilla 0).
/// </summary>
public static class GameFlagsReader
{
    public const ulong FlagManagerPointer = 0x7004C7E180;

    public const int OffCount = 0x10;
    public const int OffEntries = 0x18;
    public const int OffTable = 0x20;
    public const int OffTableSize = 0x2c;
    public const int EntrySize = 0x18;
    public const int OffEntryState = 0x16;
    public const int TableRowSize = 8;

    /// <summary>MurmurHash3 x86_32 sobre los bytes UTF-8 del nombre (semilla 0 por defecto).</summary>
    public static uint Murmur3(string name, uint seed = 0)
    {
        byte[] data = Encoding.UTF8.GetBytes(name ?? "");
        int length = data.Length;
        uint h = seed;
        int i = 0;

        while (i + 4 <= length)
        {
            uint k = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i));
            k *= 0xcc9e2d51;
            k = BitOperations.RotateLeft(k, 15);
            k *= 0x1b873593;

            h ^= k;
            h = BitOperations.RotateLeft(h, 13);
            h = h * 5 + 0xe6546b64;
            i += 4;
        }

        uint tail = 0;
        int remaining = length - i;
        if (remaining > 0)
        {
            for (int b = remaining - 1; b >= 0; b--)
                tail = (tail << 8) | data[i + b];
            tail *= 0xcc9e2d51;
            tail = BitOperations.RotateLeft(tail, 15);
            tail *= 0x1b873593;
            h ^= tail;
        }

        h ^= (uint)length;
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }

    /// <summary>
    /// Resuelve la dirección del manager: puntero en <see cref="FlagManagerPointer"/> -> slot,
    /// y dentro del slot el puntero al manager. Devuelve false si la cadena no es legible.
    /// </summary>
    public static bool TryResolveManager(GameMemory memory, out long manager, out string error)
    {
        manager = 0;
        error = "";

        if (!TryReadPointer(memory, (long)FlagManagerPointer, out long slot, ref error) ||
            !TryReadPointer(memory, slot, out manager, ref error))
        {
            return false;
        }

        if (manager <= 0)
        {
            error = "Puntero del manager nulo.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Busca un hash en la tabla del manager y devuelve el estado de la bandera:
    /// true si el bit 0 del byte de estado está activo, false si no, null si no se
    /// encuentra el hash o la memoria no es legible.
    /// </summary>
    public static bool? ReadFlagByHash(GameMemory memory, uint hash)
    {
        if (!TryResolveManager(memory, out long manager, out _)) return null;
        return ReadFlagFromManager(memory, manager, hash);
    }

    /// <summary>Variante interna para pruebas: lee la tabla desde un manager ya resuelto.</summary>
    internal static bool? ReadFlagFromManager(GameMemory memory, long manager, uint hash)
    {
        byte[] header = new byte[OffTableSize + 4];
        if (!memory.TryRead(manager, header)) return null;

        long tableAddress = (long)BitConverter.ToUInt64(header, OffTable);
        int tableSize = BitConverter.ToInt32(header, OffTableSize);
        if (tableAddress <= 0 || tableSize <= 0 || tableSize > 1_000_000) return null;

        byte[] table = new byte[tableSize * TableRowSize];
        if (!memory.TryRead(tableAddress, table)) return null;

        for (int row = 0; row < tableSize; row++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(row * TableRowSize)) != hash) continue;

            int index = BitConverter.ToInt32(table, row * TableRowSize + 4);
            byte[] countBuffer = new byte[4];
            if (!memory.TryRead(manager + OffCount, countBuffer)) return null;
            int count = BitConverter.ToInt32(countBuffer, 0);
            if (index < 0 || index >= count) return null;

            byte[] entriesPointer = new byte[memory.PointerSize];
            if (!memory.TryRead(manager + OffEntries, entriesPointer)) return null;
            long entriesAddress = memory.PointerSize == 8
                ? unchecked((long)BitConverter.ToUInt64(entriesPointer, 0))
                : BitConverter.ToUInt32(entriesPointer, 0);
            if (entriesAddress <= 0) return null;

            byte[] state = new byte[1];
            if (!memory.TryRead(entriesAddress + index * EntrySize + OffEntryState, state)) return null;

            return (state[0] & 1) == 1;
        }

        return null;
    }

    /// <summary>
    /// Lee una máscara de bits: bit 0 = el menos significativo del dword en <paramref name="address"/>.
    /// null si la memoria no es legible.
    /// </summary>
    public static bool? ReadBit(GameMemory memory, ulong address, int bitIndex)
    {
        if (memory is null || address == 0 || bitIndex < 0 || bitIndex > 31) return null;

        byte[] buffer = new byte[4];
        if (!memory.TryRead((long)address, buffer)) return null;

        return (BinaryPrimitives.ReadInt32LittleEndian(buffer) >> bitIndex & 1) == 1;
    }

    /// <summary>
    /// Lee una cadena (máx. 63 bytes, cortada en «\0») de la sección 16 del manager:
    /// sec = manager+0x600; tabla hash {uint32 hash, uint32 index} en sec+0x20 de tamaño
    /// sec+0x2c; entradas en sec+0x18 con stride 0x108 y puntero al texto en entry+0x68.
    /// </summary>
    public static string? ReadText64(GameMemory memory, string name)
    {
        if (!TryResolveManager(memory, out long manager, out _)) return null;
        return ReadTextFromSection(memory, manager, name);
    }

    /// <summary>Variante interna para pruebas: lee la sección 16 desde un manager ya resuelto.</summary>
    internal static string? ReadTextFromSection(GameMemory memory, long manager, string name)
    {
        long sec = manager + 0x600;

        byte[] header = new byte[0x30];
        if (!memory.TryRead(sec, header)) return null;

        long tableAddress = (long)BitConverter.ToUInt64(header, 0x20);
        long entriesPointer = (long)BitConverter.ToUInt64(header, 0x18);
        int tableSize = BitConverter.ToInt32(header, 0x2c);
        if (tableAddress <= 0 || entriesPointer <= 0 || tableSize <= 0 || tableSize > 1_000_000) return null;

        uint hash = Murmur3(name);
        byte[] table = new byte[tableSize * TableRowSize];
        if (!memory.TryRead(tableAddress, table)) return null;

        for (int row = 0; row < tableSize; row++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(row * TableRowSize)) != hash) continue;

            int index = BitConverter.ToInt32(table, row * TableRowSize + 4);
            byte[] countBuffer = new byte[4];
            if (!memory.TryRead(sec + 0x10, countBuffer)) return null;
            int count = BitConverter.ToInt32(countBuffer, 0);
            if (index < 0 || index >= count) return null;

            byte[] textPtr = new byte[memory.PointerSize];
            if (!memory.TryRead(entriesPointer + index * 0x108 + 0x68, textPtr)) return null;
            long textAddress = memory.PointerSize == 8
                ? unchecked((long)BitConverter.ToUInt64(textPtr, 0))
                : BitConverter.ToUInt32(textPtr, 0);
            if (textAddress <= 0) return null;

            byte[] text = new byte[64];
            if (!memory.TryRead(textAddress, text)) return null;

            int length = Array.IndexOf(text, (byte)0);
            if (length < 0) length = 63;
            return Encoding.UTF8.GetString(text, 0, length);
        }

        return null;
    }

    private static bool TryReadPointer(GameMemory memory, long address, out long pointer, ref string error)
    {
        pointer = 0;
        byte[] buffer = new byte[memory.PointerSize];
        if (!memory.TryRead(address, buffer))
        {
            error = $"No se puede leer el puntero en 0x{address:X}.";
            return false;
        }

        pointer = memory.PointerSize == 8
            ? unchecked((long)BitConverter.ToUInt64(buffer, 0))
            : BitConverter.ToUInt32(buffer, 0);

        if (pointer <= 0 || pointer > 0x7FFF_FFFF_FFFF)
        {
            error = "Puntero nulo o fuera de rango.";
            return false;
        }

        return true;
    }

    public static HashSet<ulong> ReadBool64Set(GameMemory memory)
    {
        var active = new HashSet<ulong>();
        if (!TryResolveManager(memory, out long manager, out _)) return active;

        byte[] header = new byte[0x10];
        if (!memory.TryRead(manager + 0xC58, header)) return active;

        int count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0));
        long ptr = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)));
        if (ptr <= 0 || count <= 0 || count > 100_000) return active;

        byte[] buf = new byte[count * 16];
        if (!memory.TryRead(ptr, buf)) return active;

        for (int i = 0; i < count; i++)
        {
            int off = i * 16;
            if ((buf[off + 12] & 1) != 0)
                active.Add(BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(off)));
        }
        return active;
    }
}

