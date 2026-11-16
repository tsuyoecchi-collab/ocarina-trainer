using System.Collections.Concurrent;
using System.Diagnostics;

namespace PhykroTrainer.Core;

public enum ScanMode
{
    Exact,
    NotEqual,
    Greater,
    Less,
    Increased,
    Decreased,
    Changed,
    Unchanged
}

public enum ScanArea
{
    Fast,
    MainModule,
    Writable,
    All
}

public readonly record struct ScanProgress(long BytesScanned, long BytesTotal, int Found, bool Truncated,
    bool Filtering = false);

/// <summary>Resultado de un escaneo: direcciones y valores actuales y previos.</summary>
public sealed class ScanSession
{
    public ValueKind Kind { get; }
    public long[] Addresses { get; }
    public byte[] Values { get; }
    public byte[] Previous { get; }
    public bool Truncated { get; }
    public int Count => Addresses.Length;
    public int ValueSize { get; }

    public ScanSession(ValueKind kind, long[] addresses, byte[] values, byte[] previous, bool truncated = false)
    {
        Kind = kind;
        Addresses = addresses;
        Values = values;
        Previous = previous;
        Truncated = truncated;
        ValueSize = ValueCodec.Size(kind);
    }

    public string ValueText(int index) =>
        ValueCodec.Format(Kind, Values.AsSpan(index * ValueSize, ValueSize));

    public string PreviousText(int index) =>
        ValueCodec.Format(Kind, Previous.AsSpan(index * ValueSize, ValueSize));
}

public static class Scanner
{
    public const int MaxResults = 2_000_000;
    private const int ChunkSize = 1 << 20;
    private const long FastRegionLimit = 4L * 1024 * 1024;

    public static string ModeLabel(ScanMode mode) => mode switch
    {
        ScanMode.Exact => "Valor exacto",
        ScanMode.NotEqual => "Distinto de",
        ScanMode.Greater => "Mayor que",
        ScanMode.Less => "Menor que",
        ScanMode.Increased => "Aumentó",
        ScanMode.Decreased => "Disminuyó",
        ScanMode.Changed => "Cambió",
        ScanMode.Unchanged => "Sin cambios",
        _ => mode.ToString()
    };

    public static string AreaLabel(ScanArea area) => area switch
    {
        ScanArea.Fast => "Memoria pequeña ≤ 4 MB (recomendado)",
        ScanArea.MainModule => "Módulo del juego (rápido)",
        ScanArea.Writable => "Toda la memoria escribible (lento)",
        ScanArea.All => "Todo el proceso (muy lento)",
        _ => area.ToString()
    };

    public static bool IsFirstScanMode(ScanMode mode) =>
        mode is ScanMode.Exact or ScanMode.NotEqual or ScanMode.Greater or ScanMode.Less;

    public static bool NeedsValue(ScanMode mode) =>
        mode is ScanMode.Exact or ScanMode.NotEqual or ScanMode.Greater or ScanMode.Less;

    public static List<MemoryRegion> SelectRegions(GameMemory memory, ScanArea area)
    {
        switch (area)
        {
            case ScanArea.MainModule:
                GameModule? module = memory.FindModule(null);
                return module is null
                    ? new List<MemoryRegion>()
                    : new List<MemoryRegion>
                    {
                        new(module.Value.Base, module.Value.Size, Native.PageReadWrite, Native.MemImage)
                    };

            case ScanArea.Fast:
                return memory.GetRegions(writableOnly: true)
                    .Where(r => r.Size <= FastRegionLimit)
                    .ToList();

            case ScanArea.Writable:
                return memory.GetRegions(writableOnly: true);

            default:
                return memory.GetRegions(writableOnly: false);
        }
    }

    public static long EstimateBytes(GameMemory memory, ScanArea area)
    {
        try { return SelectRegions(memory, area).Sum(r => r.Size); }
        catch { return 0; }
    }

    public static ScanSession FirstScan(GameMemory memory, ValueKind kind, ScanMode mode, byte[]? target,
        bool aligned, ScanArea area, IProgress<ScanProgress>? progress, CancellationToken token,
        IReadOnlyList<MemoryRegion>? regionsOverride = null)
    {
        if (!IsFirstScanMode(mode))
            throw new ArgumentException("Esa comparación necesita un escaneo previo o un valor de referencia.", nameof(mode));
        if (NeedsValue(mode) && target is null)
            throw new ArgumentException("Falta el valor a buscar.", nameof(target));

        int size = ValueCodec.Size(kind);
        int step = aligned ? size : 1;
        List<MemoryRegion> regions = regionsOverride?.ToList() ?? SelectRegions(memory, area);
        long total = regions.Sum(r => r.Size);

        var chunks = new List<(long Address, int Size)>();
        foreach (MemoryRegion region in regions)
        {
            long offset = 0;
            while (offset < region.Size)
            {
                int length = (int)Math.Min(ChunkSize, region.Size - offset);
                if (length >= size) chunks.Add((region.Base + offset, length));
                offset += length;
            }
        }

        var bag = new ConcurrentBag<(List<long> Addresses, byte[] Values)>();
        long scanned = 0;
        int found = 0;
        int limitReached = 0;
        long lastReport = 0;
        var stopwatch = Stopwatch.StartNew();

        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.ForEach(chunks, options, chunk =>
        {
            if (Volatile.Read(ref limitReached) != 0) return;

            var addresses = new List<long>();
            var values = new FlatBuffer(size);
            byte[] buffer = new byte[chunk.Size];

            if (memory.TryRead(chunk.Address, chunk.Size, buffer))
            {
                int first = aligned ? (int)((size - (chunk.Address % size)) % size) : 0;
                int limit = chunk.Size - size;

                for (int position = first; position <= limit; position += step)
                {
                    if ((position & 0xFFFF) == 0 && Volatile.Read(ref limitReached) != 0) break;

                    ReadOnlySpan<byte> candidate = new(buffer, position, size);
                    bool match;

                    if (mode == ScanMode.Exact)
                    {
                        match = candidate.SequenceEqual(target);
                    }
                    else
                    {
                        int comparison = ValueCodec.Compare(kind, candidate, target);
                        match = mode switch
                        {
                            ScanMode.NotEqual => comparison != 0,
                            ScanMode.Greater => comparison > 0,
                            ScanMode.Less => comparison < 0,
                            _ => false
                        };
                    }

                    if (!match) continue;

                    addresses.Add(chunk.Address + position);
                    values.Append(candidate);

                    if (Interlocked.Increment(ref found) >= MaxResults)
                    {
                        Volatile.Write(ref limitReached, 1);
                        break;
                    }
                }
            }

            if (addresses.Count > 0) bag.Add((addresses, values.ToArray()));

            long done = Interlocked.Add(ref scanned, chunk.Size);
            if (progress is not null && stopwatch.ElapsedMilliseconds - Interlocked.Read(ref lastReport) >= 150)
            {
                Interlocked.Exchange(ref lastReport, stopwatch.ElapsedMilliseconds);
                progress.Report(new ScanProgress(done, total, Volatile.Read(ref found), false));
            }
        });

        int count = bag.Sum(item => item.Addresses.Count);
        if (count > MaxResults) count = MaxResults;

        long[] allAddresses = new long[count];
        byte[] allValues = new byte[count * size];
        int index = 0;

        foreach ((List<long> addresses, byte[] values) in bag)
        {
            for (int i = 0; i < addresses.Count && index < count; i++, index++)
            {
                allAddresses[index] = addresses[i];
                Buffer.BlockCopy(values, i * size, allValues, index * size, size);
            }
        }

        // Los resultados llegan desordenados al escanear en paralelo: se ordenan.
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Array.Sort(allAddresses, order);
        byte[] sortedValues = new byte[count * size];
        for (int i = 0; i < count; i++)
            Buffer.BlockCopy(allValues, order[i] * size, sortedValues, i * size, size);

        bool truncated = Volatile.Read(ref limitReached) != 0 || count >= MaxResults;
        progress?.Report(new ScanProgress(total, total, count, truncated));

        return new ScanSession(kind, allAddresses, sortedValues, (byte[])sortedValues.Clone(), truncated);
    }

    public static ScanSession NextScan(GameMemory memory, ScanSession previous, ScanMode mode, byte[]? target,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        if (IsFirstScanMode(mode) && target is null)
            throw new ArgumentException("Falta el valor a buscar.", nameof(target));

        int count = previous.Count;
        int size = previous.ValueSize;
        if (count == 0)
        {
            return new ScanSession(previous.Kind, Array.Empty<long>(), Array.Empty<byte>(), Array.Empty<byte>());
        }

        byte[] current = new byte[count * size];
        bool[] valid = new bool[count];
        memory.ReadMany(previous.Addresses, size, current, valid);

        var addresses = new List<long>(count);
        var values = new FlatBuffer(size);
        var previousValues = new FlatBuffer(size);
        var stopwatch = Stopwatch.StartNew();
        long lastReport = 0;

        for (int i = 0; i < count; i++)
        {
            if ((i & 0x3FFF) == 0)
            {
                token.ThrowIfCancellationRequested();
                if (progress is not null && stopwatch.ElapsedMilliseconds - lastReport >= 120)
                {
                    lastReport = stopwatch.ElapsedMilliseconds;
                    progress.Report(new ScanProgress(i, count, addresses.Count, false, Filtering: true));
                }
            }

            if (!valid[i]) continue;

            ReadOnlySpan<byte> now = current.AsSpan(i * size, size);
            ReadOnlySpan<byte> before = previous.Values.AsSpan(i * size, size);
            bool equal = now.SequenceEqual(before);

            bool keep = mode switch
            {
                ScanMode.Exact => now.SequenceEqual(target),
                ScanMode.NotEqual => !now.SequenceEqual(target),
                ScanMode.Greater => ValueCodec.Compare(previous.Kind, now, target) > 0,
                ScanMode.Less => ValueCodec.Compare(previous.Kind, now, target) < 0,
                ScanMode.Increased => ValueCodec.Compare(previous.Kind, now, before) > 0,
                ScanMode.Decreased => ValueCodec.Compare(previous.Kind, now, before) < 0,
                ScanMode.Changed => !equal,
                ScanMode.Unchanged => equal,
                _ => false
            };

            if (!keep) continue;

            addresses.Add(previous.Addresses[i]);
            values.Append(now);
            previousValues.Append(before);
        }

        progress?.Report(new ScanProgress(count, count, addresses.Count, false, Filtering: true));

        return new ScanSession(previous.Kind, addresses.ToArray(), values.ToArray(), previousValues.ToArray());
    }

    /// <summary>Buffer de bytes que crece solo, para no crear un array por resultado.</summary>
    private sealed class FlatBuffer
    {
        private byte[] _data;
        private int _length;

        public FlatBuffer(int size)
        {
            _data = new byte[Math.Max(size * 64, 1024)];
        }

        public void Append(ReadOnlySpan<byte> value)
        {
            Ensure(value.Length);
            value.CopyTo(_data.AsSpan(_length));
            _length += value.Length;
        }

        private void Ensure(int extra)
        {
            if (_length + extra <= _data.Length) return;
            int newSize = Math.Max(_data.Length * 2, _length + extra + 1024);
            Array.Resize(ref _data, newSize);
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[_length];
            Buffer.BlockCopy(_data, 0, result, 0, _length);
            return result;
        }
    }
}
