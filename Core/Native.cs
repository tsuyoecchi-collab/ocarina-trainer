using System.Runtime.InteropServices;

namespace PhykroTrainer.Core;

/// <summary>Llamadas a la API de Windows que necesita el trainer.</summary>
internal static class Native
{
    // Acceso a procesos
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessVmOperation = 0x0008;
    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessVmWrite = 0x0020;

    // Regiones de memoria
    public const uint MemCommit = 0x1000;
    public const uint MemImage = 0x1000000;
    public const uint MemMapped = 0x40000;

    public const uint PageNoAccess = 0x01;
    public const uint PageReadOnly = 0x02;
    public const uint PageReadWrite = 0x04;
    public const uint PageWriteCopy = 0x08;
    public const uint PageExecute = 0x10;
    public const uint PageExecuteRead = 0x20;
    public const uint PageExecuteReadWrite = 0x40;
    public const uint PageExecuteWriteCopy = 0x80;
    public const uint PageGuard = 0x100;

    public const uint MemReserve = 0x2000;

    // Hotkeys
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public const int WmHotkey = 0x0312;
    public const int DwmwaUseImmersiveDarkMode = 20;

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation64
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public uint Alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer,
        IntPtr size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer,
        IntPtr size, out IntPtr bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address,
        out MemoryBasicInformation64 information, IntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAlloc(IntPtr address, IntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFree(IntPtr address, IntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
