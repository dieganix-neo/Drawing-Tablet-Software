using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

internal struct PenSample
{
    internal double X, Y, Pressure;
    internal bool InRange, Tip, Barrel, Secondary, Eraser;
}

internal sealed class Tablet : IDisposable
{
    internal readonly IntPtr Handle;
    internal readonly string Path, Name;
    internal readonly bool S640;
    internal readonly ushort Usage;
    private IntPtr preparsed;
    private Native.HidData[] data;
    private readonly Dictionary<byte, Layout> layouts = new Dictionary<byte, Layout>();
    private PenSample previous;
    internal double Aspect { get; private set; }

    private sealed class Layout
    {
        internal Axis X, Y, Pressure;
        internal int Range = -1, Tip = -1, Barrel = -1, Secondary = -1, Eraser = -1;
    }

    private sealed class Axis
    {
        internal readonly int Index;
        private readonly Native.ControlCaps cap;

        internal Axis(int index, Native.ControlCaps cap) { Index = index; this.cap = cap; }
        internal double PhysicalSize { get { return (double)cap.PhysicalMax - cap.PhysicalMin; } }

        internal double Normalize(uint value)
        {
            double signed = value;
            if (cap.LogicalMin < 0 && cap.Bits > 0 && cap.Bits <= 32)
            {
                long span = 1L << cap.Bits;
                long packed = value & (span - 1);
                signed = packed >= span / 2 ? packed - span : packed;
            }
            return Math.Max(0, Math.Min(1, (signed - cap.LogicalMin) / ((double)cap.LogicalMax - cap.LogicalMin)));
        }
    }

    internal bool HasAxes { get { return layouts.Values.Any(l => l.X != null && l.Y != null); } }
    internal bool HasPressure { get { return layouts.Values.Any(l => l.Pressure != null); } }
    internal bool HasBarrel { get { return layouts.Values.Any(l => l.Barrel >= 0); } }
    internal bool HasSecondary { get { return layouts.Values.Any(l => l.Secondary >= 0); } }
    internal bool HasEraser { get { return layouts.Values.Any(l => l.Eraser >= 0); } }
    private Tablet(IntPtr handle, string path, ushort usage)
    {
        Handle = handle;
        Path = path;
        Usage = usage;
        string product = Native.ProductName(path);
        S640 = path.IndexOf("VID_28BD&PID_0937", StringComparison.OrdinalIgnoreCase) >= 0;
        if (S640) product = "UGEE S640";
        Name = string.IsNullOrWhiteSpace(product) ? "HID drawing tablet" : product;
    }

    internal static List<Tablet> Scan()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf(typeof(Native.RawList));
        var result = new List<Tablet>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (Native.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count == 0) return result;
            var devices = new Native.RawList[count];
            uint filled = Native.GetRawInputDeviceList(devices, ref count, size);
            if (filled == uint.MaxValue) continue;
            IntPtr info = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < filled; i++)
                {
                    var device = devices[i];
                    if (device.Type != Native.Hid) continue;
                    uint bytes = 32;
                    Marshal.WriteInt32(info, 32);
                    if (Native.GetRawInputDeviceInfo(device.Device, Native.DeviceInfo, info, ref bytes) == uint.MaxValue) continue;
                    ushort page = (ushort)Marshal.ReadInt16(info, 20);
                    ushort usage = (ushort)Marshal.ReadInt16(info, 22);
                    if (page != 0x0d || (usage != 1 && usage != 2 && usage != 3 && usage != 0x20)) continue;
                    string path = GetPath(device.Device);
                    if (path.Length == 0) continue;
                    var tablet = new Tablet(device.Device, path, usage);
                    if (tablet.ReadDescriptor()) result.Add(tablet);
                    else tablet.Dispose();
                }
            }
            finally { Marshal.FreeHGlobal(info); }
            return result;
        }
        return result;
    }

    private static string GetPath(IntPtr handle)
    {
        uint chars = 0;
        if (Native.GetRawInputDeviceInfo(handle, Native.DeviceName, IntPtr.Zero, ref chars) == uint.MaxValue || chars == 0) return "";
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)chars * 2));
        try
        {
            return Native.GetRawInputDeviceInfo(handle, Native.DeviceName, buffer, ref chars) == uint.MaxValue
                ? "" : Marshal.PtrToStringUni(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private bool ReadDescriptor()
    {
        uint bytes = 0;
        if (Native.GetRawInputDeviceInfo(Handle, Native.PreparsedData, IntPtr.Zero, ref bytes) == uint.MaxValue || bytes == 0) return false;
        preparsed = Marshal.AllocHGlobal(checked((int)bytes));
        if (Native.GetRawInputDeviceInfo(Handle, Native.PreparsedData, preparsed, ref bytes) == uint.MaxValue) return false;
        Native.Caps caps;
        if (Native.HidP_GetCaps(preparsed, out caps) != Native.Success || caps.DataIndices == 0) return false;
        data = new Native.HidData[caps.DataIndices];
        var values = new Native.ControlCaps[caps.Values];
        var buttons = new Native.ControlCaps[caps.Buttons];
        ushort count = caps.Values;
        if (count != 0 && Native.HidP_GetValueCaps(0, values, ref count, preparsed) == Native.Success)
            for (int i = 0; i < count; i++) AddControls(values[i], false);
        count = caps.Buttons;
        if (count != 0 && Native.HidP_GetButtonCaps(0, buttons, ref count, preparsed) == Native.Success)
            for (int i = 0; i < count; i++) AddControls(buttons[i], true);
        var layout = layouts.Values.FirstOrDefault(l => l.X != null && l.Y != null);
        if (layout == null) return false;
        Aspect = layout.X.PhysicalSize > 0 && layout.Y.PhysicalSize > 0
            ? Math.Max(0.25, Math.Min(4, layout.X.PhysicalSize / layout.Y.PhysicalSize)) : 1.6;
        return true;
    }

    private void AddControls(Native.ControlCaps cap, bool button)
    {
        Layout layout;
        if (!layouts.TryGetValue(cap.Report, out layout)) layouts[cap.Report] = layout = new Layout();
        int last = cap.IsRange != 0 ? cap.UsageMax : cap.Usage;
        for (int usage = cap.Usage; usage <= last; usage++)
        {
            int index = cap.Index + usage - cap.Usage;
            if (cap.IsRange != 0 && index > cap.IndexMax) break;
            if (!button && cap.LogicalMax > cap.LogicalMin)
            {
                if (cap.Page == 1 && usage == 0x30 && cap.IsAbsolute != 0) layout.X = new Axis(index, cap);
                if (cap.Page == 1 && usage == 0x31 && cap.IsAbsolute != 0) layout.Y = new Axis(index, cap);
                if (cap.Page == 0x0d && usage == 0x30) layout.Pressure = new Axis(index, cap);
            }
            if (cap.Page != 0x0d) continue;
            switch (usage)
            {
                case 0x32: layout.Range = index; break;
                case 0x42: layout.Tip = index; break;
                case 0x44: layout.Barrel = index; break;
                case 0x45: layout.Eraser = index; break;
                case 0x5A: layout.Secondary = index; break;
            }
        }
    }

    internal bool Decode(IntPtr report, uint bytes, out PenSample sample)
    {
        sample = previous;
        Layout layout;
        if (bytes == 0 || !layouts.TryGetValue(Marshal.ReadByte(report), out layout)) return false;
        uint count = (uint)data.Length;
        if (Native.HidP_GetData(0, data, ref count, preparsed, report, bytes) != Native.Success) return false;
        if (layout.Range >= 0) sample.InRange = false;
        if (layout.Tip >= 0) sample.Tip = false;
        if (layout.Barrel >= 0) sample.Barrel = false;
        if (layout.Secondary >= 0) sample.Secondary = false;
        if (layout.Eraser >= 0) sample.Eraser = false;
        bool x = false, y = false;
        for (int i = 0; i < count; i++)
        {
            var item = data[i];
            if (layout.X != null && item.Index == layout.X.Index) { sample.X = layout.X.Normalize(item.Value); x = true; }
            if (layout.Y != null && item.Index == layout.Y.Index) { sample.Y = layout.Y.Normalize(item.Value); y = true; }
            if (layout.Pressure != null && item.Index == layout.Pressure.Index) sample.Pressure = layout.Pressure.Normalize(item.Value);
            if (item.Index == layout.Range) sample.InRange = item.Value != 0;
            if (item.Index == layout.Tip) sample.Tip = item.Value != 0;
            if (item.Index == layout.Barrel) sample.Barrel = item.Value != 0;
            if (item.Index == layout.Secondary) sample.Secondary = item.Value != 0;
            if (item.Index == layout.Eraser) sample.Eraser = item.Value != 0;
        }
        if (layout.Tip < 0 && layout.Pressure != null) sample.Tip = sample.Pressure > 0;
        if (layout.Range < 0 && x && y) sample.InRange = true;
        if (sample.Tip || sample.Eraser) sample.InRange = true;
        if (!sample.InRange) sample = new PenSample { X = sample.X, Y = sample.Y };
        previous = sample;
        return x && y || layout.Tip >= 0 || layout.Range >= 0 || layout.Barrel >= 0 || layout.Secondary >= 0 || layout.Eraser >= 0;
    }

    public void Dispose()
    {
        if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
        preparsed = IntPtr.Zero;
    }
}
