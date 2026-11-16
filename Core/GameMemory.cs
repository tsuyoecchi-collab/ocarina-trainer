using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhykroTrainer.Core;

public readonly record struct GameModule(string Name, long Base, long Size, string FileName);

public readonly record struct MemoryRegion(long Base, long Size, uint Protect, uint Type)
{
    public bool IsWritable =>
        (Protect == Native.PageReadWrite ||
         Protect == Native.PageWriteCopy ||
         Protect == Native.PageExecuteReadWrite ||
         Protect == Native.PageExecuteWriteCopy);

    public bool IsImage => Type == Native.MemImage;
}

/// <summary>Proceso del juego abierto para lectura y escritura de memoria.</summary>
public sealed class GameMemory : IDisposable
{
    private readonly IntPtr _handle;
    private List<GameModule>? _modules;
    private bool _disposed;

    public Process Process { get; }
    public int ProcessId => Process.Id;
    public string ProcessName { get; }
    public bool Is64Bit { get; }
    public int PointerSize => Is64Bit ? 8 : 4;
    public GameModule? MainModule { get; private set; }
    public bool HasWriteAccess { get; }

    public bool IsAlive
    {
        get
        {
            try { return !_disposed && _handle != IntPtr.Zero && !Process.HasExited; }
            catch { return false; }
        }
    }

    private GameMemory(Process process, IntPtr handle, bool is64Bit, bool writeAccess)
    {
        Process = process;
        ProcessName = process.ProcessName;
        _handle = handle;
        Is64Bit = is64Bit;
        HasWriteAccess = writeAccess;

        try
        {
            ProcessModule? main = process.MainModule;
            if (main is not null)
            {
                MainModule = new GameModule(main.ModuleName, main.BaseAddress.ToInt64(),
                    main.ModuleMemorySize, main.FileName ?? main.ModuleName);
            }
        }
        catch
        {
            // Algunos procesos no permiten enumerar módulos; se resolverá más tarde.
        }
    }

    public static GameMemory Attach(int processId)
    {
        Process process = Process.GetProcessById(processId);

        uint access = Native.ProcessQueryInformation | Native.ProcessVmOperation |
                      Native.ProcessVmRead | Native.ProcessVmWrite;

        IntPtr handle = Native.OpenProcess(access, false, processId);
        bool writeAccess = handle != IntPtr.Zero;

        if (handle == IntPtr.Zero)
        {
            // Reintento solo con lectura para al menos poder usar el escáner.
            handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, processId);
        }

        if (handle == IntPtr.Zero)
        {
            int code = Marshal.GetLastWin32Error();
            throw new Win32Exception(code,
                code == 5
                    ? "Windows denegó el acceso al proceso. Ejecuta el trainer como administrador " +
                      "(si el juego está elevado) y comprueba que no sea un juego con anticheat."
                    : "No se pudo abrir el proceso del juego.");
        }

        bool is64Bit = true;
        try
        {
            if (Native.IsWow64Process(handle, out bool wow64)) is64Bit = !wow64;
        }
        catch
        {
            is64Bit = Environment.Is64BitOperatingSystem;
        }

        return new GameMemory(process, handle, is64Bit, writeAccess);
    }

    public IReadOnlyList<GameModule> Modules
    {
        get
        {
            if (_modules is null)
            {
                _modules = new List<GameModule>();
                try
                {
                    foreach (ProcessModule module in Process.Modules)
                    {
                        _modules.Add(new GameModule(module.ModuleName, module.BaseAddress.ToInt64(),
                            module.ModuleMemorySize, module.FileName ?? module.ModuleName));
                    }
                }
                catch
                {
                    if (MainModule is GameModule main) _modules.Add(main);
                }
            }

            return _modules;
        }
    }

    public GameModule? FindModule(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return MainModule;

        foreach (GameModule module in Modules)
        {
            if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(module.FileName), name, StringComparison.OrdinalIgnoreCase))
            {
                return module;
            }
        }

        return null;
    }

    public bool TryRead(long address, byte[] buffer)
    {
        if (_disposed || _handle == IntPtr.Zero || address <= 0 || buffer.Length == 0) return false;

        return Native.ReadProcessMemory(_handle, (IntPtr)address, buffer, buffer.Length, out IntPtr read) &&
               read.ToInt64() == buffer.Length;
    }

    public bool TryWrite(long address, byte[] buffer)
    {
        if (_disposed || _handle == IntPtr.Zero || address <= 0 || buffer.Length == 0) return false;

        return Native.WriteProcessMemory(_handle, (IntPtr)address, buffer, buffer.Length, out IntPtr written) &&
               written.ToInt64() == buffer.Length;
    }

    /// <summary>Enumera las regiones de memoria legibles (y opcionalmente solo escribibles).</summary>
    public List<MemoryRegion> GetRegions(bool writableOnly)
    {
        var regions = new List<MemoryRegion>();
        if (_disposed || _handle == IntPtr.Zero) return regions;

        const long maxAddress = 0x7FFF_FFFF_FFFF;
        long address = 0;
        int infoSize = Marshal.SizeOf<Native.MemoryBasicInformation64>();

        while (address < maxAddress)
        {
            if (Native.VirtualQueryEx(_handle, (IntPtr)address, out Native.MemoryBasicInformation64 info,
                    infoSize) == IntPtr.Zero)
            {
                break;
            }

            long baseAddress = unchecked((long)info.BaseAddress);
            long size = unchecked((long)info.RegionSize);
            if (size <= 0) break;

            uint protection = info.Protect & 0xFF;
            bool readable = info.State == Native.MemCommit &&
                            (info.Protect & Native.PageGuard) == 0 &&
                            protection is not (Native.PageNoAccess or Native.PageExecute);

            if (readable && (!writableOnly || IsWritable(protection)))
            {
                regions.Add(new MemoryRegion(baseAddress, size, protection, info.Type));
            }

            long next = baseAddress + size;
            if (next <= address) next = address + 0x1000;
            address = next;
        }

        return regions;
    }

    /// <summary>
    /// Lee muchos valores dispersos. Agrupa direcciones cercanas en una sola lectura
    /// para que el escaneo siguiente sea rápido.
    /// </summary>
    public int ReadMany(IReadOnlyList<long> addresses, int size, byte[] destination, bool[]? valid = null)
    {
        int count = addresses.Count;
        int copied = 0;
        var buffer = new byte[70_000];
        var single = new byte[Math.Max(1, size)];

        int i = 0;
        while (i < count)
        {
            long start = addresses[i];
            int j = i + 1;
            while (j < count && addresses[j] - start < 60_000) j++;

            long end = addresses[j - 1] + size;
            int span = unchecked((int)(end - start));
            bool groupOk = span > 0 && span <= buffer.Length && TryRead(start, span, buffer);

            for (int k = i; k < j; k++)
            {
                if (valid is not null) valid[k] = false;

                if (groupOk)
                {
                    int offset = checked((int)(addresses[k] - start));
                    Buffer.BlockCopy(buffer, offset, destination, k * size, size);
                    if (valid is not null) valid[k] = true;
                }
                else if (TryRead(addresses[k], single))
                {
                    Buffer.BlockCopy(single, 0, destination, k * size, size);
                    if (valid is not null) valid[k] = true;
                }

                copied++;
            }

            i = j;
        }

        return copied;
    }

    public bool TryRead(long address, int length, byte[] destination)
    {
        if (_disposed || _handle == IntPtr.Zero || address <= 0 || length <= 0 ||
            length > destination.Length)
        {
            return false;
        }

        return Native.ReadProcessMemory(_handle, (IntPtr)address, destination, length, out IntPtr read) &&
               read.ToInt64() == length;
    }

    private static bool IsWritable(uint protection) =>
        protection is Native.PageReadWrite or Native.PageWriteCopy
            or Native.PageExecuteReadWrite or Native.PageExecuteWriteCopy;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero) Native.CloseHandle(_handle);
    }
}
