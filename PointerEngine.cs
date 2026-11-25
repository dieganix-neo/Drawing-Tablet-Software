using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal enum PointerMode { WindowsPen, Relative, Absolute }
internal enum PenAction { None, LeftClick, RightClick, MiddleClick, Undo, Redo, Space }

internal sealed class PointerEngine : IDisposable
{
    internal PointerMode Mode { get; private set; }
    internal Tablet Tablet;
    internal int Speed = 100, Rotation;
    internal int OtherTablets;
    internal RectangleF Area = new RectangleF(0, 0, 1, 1);
    internal Rectangle Output = SystemInformation.VirtualScreen;
    internal PenAction Tip = PenAction.LeftClick, Barrel = PenAction.RightClick,
        Secondary = PenAction.MiddleClick, Eraser = PenAction.RightClick;
    internal PenSample LastSample;
    internal int LastReportTime, ReportCount;
    private readonly Native.Hook hookDelegate;
    private IntPtr hook;
    private bool anchored;
    private double lastX, lastY, carryX, carryY;
    private PenSample previous;
    private int buttons;
    private bool spaceDown;
    private const int ReportTimeout = 250;

    internal PointerEngine() { hookDelegate = FilterMouse; }

    internal void SetMode(PointerMode mode)
    {
        Reset();
        if (mode != PointerMode.WindowsPen && hook == IntPtr.Zero) hook = Native.InstallMouseHook(hookDelegate);
        if (mode == PointerMode.WindowsPen && hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
        Mode = mode;
    }

    internal static void Register(IntPtr window)
    {
        var usage = new ushort[] { 1, 2, 3, 0x20 };
        var registrations = new Native.Registration[usage.Length];
        for (int i = 0; i < usage.Length; i++)
            registrations[i] = new Native.Registration { Page = 0x0d, Usage = usage[i], Flags = 0x2100, Window = window };
        if (!Native.RegisterRawInputDevices(registrations, (uint)registrations.Length,
            (uint)Marshal.SizeOf(typeof(Native.Registration)))) throw new Win32Exception();
    }

    private IntPtr FilterMouse(int code, IntPtr message, IntPtr details)
    {
        if (code >= 0 && Mode != PointerMode.WindowsPen && Tablet != null && ReportCount != 0 &&
            (OtherTablets == 0 || unchecked(Environment.TickCount - LastReportTime) < ReportTimeout && LastSample.InRange))
        {
            var e = (Native.HookEvent)Marshal.PtrToStructure(details, typeof(Native.HookEvent));
            if ((e.Extra.ToUInt64() & 0xffffff80UL) == 0xff515700UL) return new IntPtr(1);
        }
        return Native.CallNextHookEx(hook, code, message, details);
    }

    internal unsafe void ReadRaw(IntPtr input)
    {
        if (Tablet == null) return;
        uint headerSize = (uint)sizeof(Native.Header);
        uint bytes = 0;
        if (Native.GetRawInputData(input, Native.Input, IntPtr.Zero, ref bytes, headerSize) == uint.MaxValue ||
            bytes < headerSize + 8 || bytes > 65536) return;
        byte* buffer = stackalloc byte[(int)bytes];
        uint result = Native.GetRawInputData(input, Native.Input, new IntPtr(buffer), ref bytes, headerSize);
        if (result == uint.MaxValue || result < headerSize + 8) return;
        var header = (Native.Header*)buffer;
        if (header->Type != Native.Hid || header->Device != Tablet.Handle) return;
        var payload = (uint*)(buffer + headerSize);
        uint reportSize = payload[0], count = payload[1];
        if (reportSize == 0 || count > (result - headerSize - 8) / reportSize) return;
        byte* report = buffer + headerSize + 8;
        for (uint i = 0; i < count; i++, report += reportSize)
        {
            PenSample sample;
            if (Tablet.Decode(new IntPtr(report), reportSize, out sample)) Process(sample, Environment.TickCount);
        }
    }

    internal void Process(PenSample sample, int now)
    {
        int elapsed = unchecked(now - LastReportTime);
        LastReportTime = now;
        LastSample = sample;
        ReportCount++;
        if (Mode == PointerMode.WindowsPen) return;
        if (elapsed > ReportTimeout)
        {
            anchored = false;
            carryX = carryY = 0;
        }
        if (!sample.InRange)
        {
            Reset();
            return;
        }
        Move(sample);
        int wanted = 0;
        bool wantSpace = false;
        bool eraser = sample.Eraser && (Tablet != null && Tablet.S640 || sample.Tip || sample.Pressure > 0);
        bool hadEraser = previous.Eraser && (Tablet != null && Tablet.S640 || previous.Tip || previous.Pressure > 0);
        Collect(sample.Tip && !sample.Eraser, previous.Tip && !previous.Eraser, Tip, ref wanted, ref wantSpace);
        Collect(sample.Barrel, previous.Barrel, Barrel, ref wanted, ref wantSpace);
        Collect(sample.Secondary, previous.Secondary, Secondary, ref wanted, ref wantSpace);
        Collect(eraser, hadEraser, Eraser, ref wanted, ref wantSpace);
        SetButtons(wanted, wantSpace);
        previous = sample;
    }

    private void Move(PenSample sample)
    {
        double x = Clamp((sample.X - Area.X) / Area.Width);
        double y = Clamp((sample.Y - Area.Y) / Area.Height);
        double width = Area.Width, height = Area.Height / (Tablet == null ? 1.6 : Tablet.Aspect);
        double oldX = x;
        switch (Rotation)
        {
            case 90: x = 1 - y; y = oldX; break;
            case 180: x = 1 - x; y = 1 - y; break;
            case 270: x = y; y = 1 - oldX; break;
        }
        if (Rotation == 90 || Rotation == 270)
        {
            double oldWidth = width;
            width = height;
            height = oldWidth;
        }
        if (Mode == PointerMode.Relative && anchored)
        {
            double scale = 1300 * Speed / 100.0;
            carryX += (x - lastX) * width * scale;
            carryY += (y - lastY) * height * scale;
            int dx = (int)Math.Round(carryX), dy = (int)Math.Round(carryY);
            carryX -= dx;
            carryY -= dy;
            if (dx != 0 || dy != 0) Native.MouseInput(1, dx, dy);
        }
        if (Mode == PointerMode.Absolute)
        {
            Point target = new Point(Output.X + (int)Math.Round(x * (Output.Width - 1)),
                Output.Y + (int)Math.Round(y * (Output.Height - 1)));
            Rectangle desktop = SystemInformation.VirtualScreen;
            if (desktop.Width > 0 && desktop.Height > 0)
                Native.MouseInput(0xc001, (int)((target.X - desktop.X + 0.5) * 65536 / desktop.Width),
                    (int)((target.Y - desktop.Y + 0.5) * 65536 / desktop.Height));
        }
        lastX = x;
        lastY = y;
        anchored = true;
    }

    private static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }

    private static void Collect(bool down, bool wasDown, PenAction action, ref int buttons, ref bool space)
    {
        if (!down) return;
        switch (action)
        {
            case PenAction.LeftClick: buttons |= 1; break;
            case PenAction.RightClick: buttons |= 2; break;
            case PenAction.MiddleClick: buttons |= 4; break;
            case PenAction.Undo: if (!wasDown) Native.KeyChord(0x11, 0x5a); break;
            case PenAction.Redo: if (!wasDown) Native.KeyChord(0x11, 0x59); break;
            case PenAction.Space: space = true; break;
        }
    }

    private void SetButtons(int wanted, bool wantSpace)
    {
        int changed = buttons ^ wanted;
        if ((changed & 1) != 0) Native.MouseInput((wanted & 1) != 0 ? 2u : 4u);
        if ((changed & 2) != 0) Native.MouseInput((wanted & 2) != 0 ? 8u : 16u);
        if ((changed & 4) != 0) Native.MouseInput((wanted & 4) != 0 ? 32u : 64u);
        if (spaceDown != wantSpace) Native.KeyState(0x20, wantSpace);
        buttons = wanted;
        spaceDown = wantSpace;
    }

    internal void Tick()
    {
        if (LastSample.InRange && unchecked(Environment.TickCount - LastReportTime) > ReportTimeout)
        {
            LastSample.InRange = false;
            LastSample.Pressure = 0;
            Reset();
        }
    }

    internal void Reset()
    {
        SetButtons(0, false);
        anchored = false;
        previous = new PenSample();
        carryX = carryY = 0;
    }

    public void Dispose() { SetMode(PointerMode.WindowsPen); }
}
