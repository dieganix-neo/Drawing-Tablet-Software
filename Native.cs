using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class Native
{
    internal const uint Hid = 2, DeviceName = 0x20000007, DeviceInfo = 0x2000000b,
        PreparsedData = 0x20000005, Input = 0x10000003;
    internal const int Success = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawList { public IntPtr Device; public uint Type; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Registration
    {
        public ushort Page, Usage;
        public uint Flags;
        public IntPtr Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Header { public uint Type, Size; public IntPtr Device, Code; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct Caps
    {
        [FieldOffset(4)] public ushort ReportSize;
        [FieldOffset(46)] public ushort Buttons;
        [FieldOffset(48)] public ushort Values;
        [FieldOffset(50)] public ushort DataIndices;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct ControlCaps
    {
        [FieldOffset(0)] public ushort Page;
        [FieldOffset(2)] public byte Report;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(18)] public ushort Bits;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(48)] public int PhysicalMin;
        [FieldOffset(52)] public int PhysicalMax;
        [FieldOffset(56)] public ushort Usage;
        [FieldOffset(58)] public ushort UsageMax;
        [FieldOffset(68)] public ushort Index;
        [FieldOffset(70)] public ushort IndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidData { public ushort Index, Reserved; public uint Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Key { public ushort Code, Scan; public uint Flags, Time; public UIntPtr Extra; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public Mouse Mouse;
        [FieldOffset(0)] public Key Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent { public uint Type; public InputData Data; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HookEvent { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }

    internal delegate IntPtr Hook(int code, IntPtr message, IntPtr details);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputDeviceList([Out] RawList[] devices, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr parsed, out Caps caps);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetValueCaps(int type, [Out] ControlCaps[] values, ref ushort count, IntPtr parsed);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetButtonCaps(int type, [Out] ControlCaps[] buttons, ref ushort count, IntPtr parsed);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetData(int type, [Out] HidData[] data, ref uint count, IntPtr parsed, IntPtr report, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetProductString(Microsoft.Win32.SafeHandles.SafeFileHandle device, [Out] byte[] text, uint length);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr details);
    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, InputEvent[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static readonly InputEvent[] mouseEvent = new InputEvent[1];
    private static readonly int inputSize = Marshal.SizeOf(typeof(InputEvent));
    private static readonly UIntPtr marker = new UIntPtr(0x5450444b);

    internal static void EnableDpi()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
        catch (EntryPointNotFoundException) { }
        SetProcessDPIAware();
    }

    internal static IntPtr InstallMouseHook(Hook callback)
    {
        IntPtr handle = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (handle == IntPtr.Zero) throw new Win32Exception();
        return handle;
    }

    internal static void MouseInput(uint flags, int x = 0, int y = 0)
    {
        mouseEvent[0].Data.Mouse = new Mouse { X = x, Y = y, Flags = flags, Extra = marker };
        SendInput(1, mouseEvent, inputSize);
    }

    internal static void KeyChord(params ushort[] keys)
    {
        var events = new InputEvent[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            events[i].Type = events[events.Length - 1 - i].Type = 1;
            events[i].Data.Key = new Key { Code = keys[i], Extra = marker };
            events[events.Length - 1 - i].Data.Key = new Key { Code = keys[i], Flags = 2, Extra = marker };
        }
        SendInput((uint)events.Length, events, inputSize);
    }

    internal static void KeyState(ushort key, bool down)
    {
        var item = new InputEvent { Type = 1 };
        item.Data.Key = new Key { Code = key, Flags = down ? 0u : 2u, Extra = marker };
        SendInput(1, new[] { item }, inputSize);
    }

    internal static string ProductName(string path)
    {
        using (var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            var text = new byte[512];
            if (handle.IsInvalid || !HidD_GetProductString(handle, text, (uint)text.Length)) return "";
            return Encoding.Unicode.GetString(text).TrimEnd('\0').Trim();
        }
    }
}
