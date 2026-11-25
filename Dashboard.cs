using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

internal sealed class Dashboard : Form
{
    internal static readonly Color Canvas = Color.FromArgb(18, 26, 24);
    internal static readonly Color Mint = Color.FromArgb(131, 240, 193);
    private static readonly Color Page = Color.FromArgb(16, 20, 20), Card = Color.FromArgb(26, 34, 32),
        ButtonColor = Color.FromArgb(39, 52, 48), Ink = Color.FromArgb(237, 245, 241),
        Muted = Color.FromArgb(167, 185, 178);
    private const int HotKeyId = 1;
    private static readonly string[] Actions = { "Disabled", "Left click", "Right click", "Middle click", "Undo (Ctrl+Z)", "Redo (Ctrl+Y)", "Hold Space" };
    private readonly PointerEngine engine = new PointerEngine();
    private List<Tablet> tablets = new List<Tablet>();
    private readonly Timer timer = new Timer { Interval = 32 };
    private DeskSelect device, monitor, rotation, tip, barrel, second;
    private TrackBar speed, curve;
    private Label status, modeHelp, speedText, pressure, live, curveText, hotkeyText, secondLabel;
    private readonly Button[] modes = new Button[3];
    private readonly CropField[] crop = new CropField[4];
    private TabletView preview;
    private NotifyIcon tray;
    private bool initialized, loading, hotkey, canRead, pendingScan, dirty;
    private string startupError;
    private Settings settings;
    private int displayedCount = -1;
    private bool displayedRange;

    internal Dashboard()
    {
        settings = Settings.Load();
        Text = "Tablet Desk";
        Icon = SystemIcons.Application;
        BackColor = Page;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(910, 648);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        SuspendLayout();
        Build();
        ResumeLayout(false);
    }

    private static Label LabelAt(Control parent, string value, int x, int y, int width, int size = 10,
        bool bold = false, bool muted = false)
    {
        var label = new Label { Text = value, Location = new Point(x, y), Size = new Size(width, size * 2 + 5),
            ForeColor = muted ? Muted : Ink, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            UseMnemonic = false };
        parent.Controls.Add(label);
        return label;
    }

    private static Panel CardAt(Control parent, int x, int y, int width, int height)
    {
        var card = new Panel { BackColor = Card, Location = new Point(x, y), Size = new Size(width, height) };
        parent.Controls.Add(card);
        return card;
    }

    private static Button ButtonAt(Control parent, string text, int x, int y, int width, int height, Action click)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height),
            FlatStyle = FlatStyle.Flat, BackColor = ButtonColor, ForeColor = Ink, Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = Color.FromArgb(63, 82, 74);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 82, 73);
        button.Click += delegate { click(); };
        parent.Controls.Add(button);
        return button;
    }

    private static DeskSelect ComboAt(Control parent, int x, int y, int width, string name, params string[] options)
    {
        var combo = new DeskSelect { Location = new Point(x, y), Width = width, AccessibleName = name };
        combo.Items.AddRange(options);
        parent.Controls.Add(combo);
        return combo;
    }

    private static TrackBar TrackAt(Control parent, int x, int y, int width, int min, int max, int step, string name)
    {
        var track = new TrackBar { Location = new Point(x, y), Size = new Size(width, 28), AutoSize = false,
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, SmallChange = step, LargeChange = step * 4,
            BackColor = Card, AccessibleName = name };
        parent.Controls.Add(track);
        return track;
    }

    private void Build()
    {
        LabelAt(this, "Tablet Desk", 25, 12, 295, 25, true);
        status = LabelAt(this, "Looking for drawing tablets…", 27, 61, 392, 10, false, true);
        device = ComboAt(this, 493, 26, 286, "Drawing tablet");
        ButtonAt(this, "Refresh", 790, 24, 94, 33, Scan);
        device.SelectedIndexChanged += delegate { if (!loading) SelectTablet(); };

        var pointer = CardAt(this, 26, 104, 434, 195);
        LabelAt(pointer, "Pointer mode", 17, 12, 230, 14, true);
        modes[0] = ButtonAt(pointer, "Windows pen", 18, 49, 112, 38, () => SetMode(PointerMode.WindowsPen));
        modes[1] = ButtonAt(pointer, "Relative mouse", 138, 49, 139, 38, () => SetMode(PointerMode.Relative));
        modes[2] = ButtonAt(pointer, "Absolute mouse", 285, 49, 131, 38, () => SetMode(PointerMode.Absolute));
        modeHelp = LabelAt(pointer, "", 18, 99, 399, 10, false, true);
        modeHelp.Height = 45;
        LabelAt(pointer, "Mouse speed", 18, 154, 109);
        speed = TrackAt(pointer, 123, 150, 244, 20, 300, 5, "Relative mouse speed");
        speedText = LabelAt(pointer, "", 370, 154, 57, 10, true);

        var mapping = CardAt(this, 26, 313, 434, 171);
        LabelAt(mapping, "Mapping", 17, 11, 230, 14, true);
        LabelAt(mapping, "Display", 18, 49, 222, 9, false, true);
        LabelAt(mapping, "Rotation", 283, 49, 131, 9, false, true);
        monitor = ComboAt(mapping, 18, 72, 247, "Display for absolute mouse mode");
        rotation = ComboAt(mapping, 283, 72, 133, "Rotation", "0°", "90°", "180°", "270°");
        LabelAt(mapping, "Tablet area crop (%)  ·  Left / Top / Right / Bottom", 18, 108, 380, 9, false, true);
        string[] edges = { "Left", "Top", "Right", "Bottom" };
        for (int i = 0; i < crop.Length; i++)
        {
            var item = new CropField(edges[i]) { Location = new Point(18 + i * 101, 132), Width = 95 };
            mapping.Controls.Add(item);
            crop[i] = item;
            item.ValueChanged += delegate { ReadControls(true); };
        }

        var bindings = CardAt(this, 26, 498, 434, 106);
        LabelAt(bindings, "Pen actions", 17, 9, 230, 14, true);
        LabelAt(bindings, "Tip", 18, 45, 129, 9, false, true);
        LabelAt(bindings, "Barrel button", 153, 45, 129, 9, false, true);
        secondLabel = LabelAt(bindings, "Second button", 288, 45, 131, 9, false, true);
        tip = ComboAt(bindings, 18, 68, 128, "Pen tip action", Actions);
        barrel = ComboAt(bindings, 153, 68, 128, "Barrel button action", Actions);
        second = ComboAt(bindings, 288, 68, 128, "Second pen button action", Actions);

        var testing = CardAt(this, 476, 104, 408, 500);
        LabelAt(testing, "Pen preview", 16, 12, 243, 14, true);
        ButtonAt(testing, "Clear", 318, 13, 72, 31, () => preview.Clear());
        live = LabelAt(testing, "Waiting for pen input", 17, 51, 373, 10, false, true);
        preview = new TabletView { Location = new Point(17, 84), Size = new Size(373, 244) };
        testing.Controls.Add(preview);
        LabelAt(testing, "Pressure", 17, 345, 130, 11, true);
        pressure = LabelAt(testing, "0%", 222, 346, 168, 10, false, true);
        pressure.TextAlign = ContentAlignment.TopRight;
        LabelAt(testing, "Preview pressure curve", 17, 383, 242, 10);
        curveText = LabelAt(testing, "Linear", 280, 383, 110, 10, true);
        curveText.TextAlign = ContentAlignment.TopRight;
        curve = TrackAt(testing, 16, 408, 374, -10, 10, 1, "Preview pressure curve, soft to firm");
        LabelAt(testing, "Soft", 17, 442, 116, 9, false, true);
        var firm = LabelAt(testing, "Firm", 275, 442, 115, 9, false, true);
        firm.TextAlign = ContentAlignment.TopRight;
        LabelAt(testing, "This curve applies to the preview.", 17, 468, 373, 9, false, true);

        hotkeyText = LabelAt(this, "Ctrl + Alt + F10  ·  Toggle relative mouse", 27, 617, 590, 10, false, true);
        ButtonAt(this, "Minimize to tray", 746, 610, 138, 31, HideToTray);
        speed.ValueChanged += delegate { ReadControls(false); };
        curve.ValueChanged += delegate { ReadControls(false); };
        monitor.SelectedIndexChanged += delegate { ReadControls(false); };
        rotation.SelectedIndexChanged += delegate { ReadControls(true); };
        tip.SelectedIndexChanged += delegate { ReadControls(true); };
        barrel.SelectedIndexChanged += delegate { ReadControls(true); };
        second.SelectedIndexChanged += delegate { ReadControls(true); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (initialized) return;
        initialized = true;
        loading = true;
        FillMonitors();
        speed.Value = Limit(settings.Speed, 20, 300);
        curve.Value = Limit(settings.Curve, -10, 10);
        rotation.SelectedIndex = Limit(settings.Rotation, 0, 3);
        tip.SelectedIndex = Limit(settings.Tip, 0, Actions.Length - 1);
        barrel.SelectedIndex = Limit(settings.Barrel, 0, Actions.Length - 1);
        second.SelectedIndex = Limit(settings.Secondary, 0, Actions.Length - 1);
        for (int i = 0; i < crop.Length; i++) crop[i].Value = Limit(settings.Crop[i], 0, 90);
        crop[2].Value = Math.Min(crop[2].Value, 95 - crop[0].Value);
        crop[3].Value = Math.Min(crop[3].Value, 95 - crop[1].Value);
        loading = false;
        try { PointerEngine.Register(Handle); canRead = true; }
        catch (Win32Exception error) { startupError = "Windows pen input could not be opened. " + error.Message; }
        hotkey = Native.RegisterHotKey(Handle, HotKeyId, 0x4003, (uint)Keys.F10);
        if (!hotkey) hotkeyText.Text = "Shortcut unavailable · Switch modes in the app or tray";
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Tablet Desk", null, delegate { RestoreFromTray(); });
        menu.Items.Add("Toggle relative mouse", null, delegate { ToggleRelative(); });
        menu.Items.Add("Exit", null, delegate { Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "Tablet Desk", ContextMenuStrip = menu };
        tray.DoubleClick += delegate { RestoreFromTray(); };
        Scan();
        ReadControls(false);
        dirty = false;
        timer.Tick += delegate { UpdateLive(); };
        timer.Start();
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
    }

    private void FillMonitors()
    {
        monitor.Items.Clear();
        monitor.Items.Add("All displays");
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            monitor.Items.Add(string.Format("Display {0}  ·  {1} × {2}{3}", i + 1, screen.Bounds.Width,
                screen.Bounds.Height, screen.Primary ? " (primary)" : ""));
        }
        int selected = Array.FindIndex(screens, s => s.DeviceName == settings.Display);
        monitor.SelectedIndex = selected + 1;
    }

    private void DisplaysChanged(object sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke((Action)delegate
        {
            loading = true;
            FillMonitors();
            loading = false;
            ReadControls(false);
        });
    }

    private void ReadControls(bool reset)
    {
        if (loading || !initialized) return;
        if (reset) engine.Reset();
        int[] edges = crop.Select(n => n.Value).ToArray();
        for (int i = 0; i < edges.Length; i++)
        {
            int other = (i + 2) % edges.Length;
            crop[i].Maximum = Math.Min(90, 95 - edges[other]);
        }
        engine.Area = new RectangleF(edges[0] / 100f, edges[1] / 100f,
            (100 - edges[0] - edges[2]) / 100f, (100 - edges[1] - edges[3]) / 100f);
        preview.Area = engine.Area;
        engine.Rotation = rotation.SelectedIndex * 90;
        engine.Speed = speed.Value;
        engine.Tip = (PenAction)tip.SelectedIndex;
        engine.Barrel = (PenAction)barrel.SelectedIndex;
        engine.Secondary = engine.Eraser = (PenAction)second.SelectedIndex;
        preview.PressurePower = Math.Pow(2, curve.Value / 8.0);
        speedText.Text = speed.Value + "%";
        curveText.Text = curve.Value == 0 ? "Linear" : (curve.Value < 0 ? "Soft " : "Firm ") + Math.Abs(curve.Value);
        var screens = Screen.AllScreens;
        int display = monitor.SelectedIndex - 1;
        engine.Output = display >= 0 && display < screens.Length ? screens[display].Bounds : SystemInformation.VirtualScreen;
        settings.Display = display >= 0 && display < screens.Length ? screens[display].DeviceName : "";
        settings.Speed = speed.Value;
        settings.Curve = curve.Value;
        settings.Rotation = rotation.SelectedIndex;
        settings.Tip = tip.SelectedIndex;
        settings.Barrel = barrel.SelectedIndex;
        settings.Secondary = second.SelectedIndex;
        settings.Crop = edges;
        dirty = true;
        preview.Invalidate();
    }

    private void Scan()
    {
        pendingScan = false;
        var next = Tablet.Scan();
        Tablet oldTablet = engine.Tablet;
        string path = oldTablet == null ? settings.Device : oldTablet.Path;
        int selected = next.FindIndex(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (selected < 0 && next.Count > 0) selected = 0;
        loading = true;
        device.Items.Clear();
        foreach (var tablet in next) device.Items.Add(tablet.Name);
        if (next.Count == 0) device.Items.Add("No drawing tablet detected");
        device.SelectedIndex = Math.Max(0, selected);
        device.Enabled = next.Count > 0;
        loading = false;
        var old = tablets;
        tablets = next;
        SelectTablet();
        foreach (var tablet in old) tablet.Dispose();
    }

    private void SelectTablet()
    {
        Tablet old = engine.Tablet;
        Tablet current = device.SelectedIndex >= 0 && device.SelectedIndex < tablets.Count ? tablets[device.SelectedIndex] : null;
        if (old == null || current == null || old.Handle != current.Handle)
        {
            SetMode(PointerMode.WindowsPen);
            preview.Clear();
            engine.LastSample = new PenSample();
            engine.ReportCount = 0;
            displayedCount = -1;
        }
        engine.Tablet = current;
        engine.OtherTablets = Math.Max(0, tablets.Count - 1);
        if (current != null && settings.Device != current.Path)
        {
            settings.Device = current.Path;
            dirty = true;
        }
        RefreshMode();
    }

    private void SetMode(PointerMode mode)
    {
        if (mode != PointerMode.WindowsPen && (!canRead || engine.Tablet == null)) return;
        try { engine.SetMode(mode); }
        catch (Win32Exception error)
        {
            MessageBox.Show(this, error.Message, "Unable to start mouse mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshMode();
    }

    private void RefreshMode()
    {
        var tablet = engine.Tablet;
        bool available = canRead && tablet != null;
        bool mouse = engine.Mode != PointerMode.WindowsPen;
        for (int i = 0; i < modes.Length; i++)
        {
            bool active = i == (int)engine.Mode;
            modes[i].Enabled = i == 0 || available;
            modes[i].BackColor = active ? Mint : ButtonColor;
            modes[i].ForeColor = active ? Canvas : Ink;
            modes[i].FlatAppearance.MouseOverBackColor = active ? Mint : Color.FromArgb(62, 82, 73);
        }
        status.Text = startupError ?? (tablet == null ? "Connect a standard HID pen tablet to begin."
            : tablet.Name + " connected  ·  " + (mouse ? "Mouse mode active" : "Windows pen active"));
        status.ForeColor = available ? Mint : Muted;
        modeHelp.Text = engine.Mode == PointerMode.Relative ? "Hover to move, tap to click, lift to reposition the pen."
            : engine.Mode == PointerMode.Absolute ? "Map the tablet area to a display and send mouse input."
            : "Use Windows pen input and pressure in supported drawing apps.";
        speed.Enabled = engine.Mode == PointerMode.Relative;
        monitor.Enabled = engine.Mode == PointerMode.Absolute;
        rotation.Enabled = mouse;
        foreach (var edge in crop) edge.Enabled = mouse;
        tip.Enabled = mouse;
        barrel.Enabled = mouse && tablet != null && tablet.HasBarrel;
        second.Enabled = mouse && tablet != null && (tablet.HasSecondary || tablet.HasEraser);
        secondLabel.Text = tablet != null && !tablet.HasSecondary && tablet.HasEraser
            ? (tablet.S640 ? "Second / eraser" : "Eraser") : "Second button";
        curve.Enabled = tablet != null && tablet.HasPressure;
        if (tray != null)
            tray.Text = "Tablet Desk · " + (mouse ? modes[(int)engine.Mode].Text : "Windows pen");
    }

    private void UpdateLive()
    {
        if (pendingScan) Scan();
        engine.Tick();
        var sample = engine.LastSample;
        if (engine.ReportCount == displayedCount && sample.InRange == displayedRange) return;
        displayedCount = engine.ReportCount;
        displayedRange = sample.InRange;
        if (Visible)
        {
            preview.UpdateSample(sample);
            pressure.Text = engine.Tablet == null || !engine.Tablet.HasPressure ? "Unavailable" :
                string.Format("{0:0}%  ·  {1:0}% preview", sample.Pressure * 100, Math.Pow(sample.Pressure, preview.PressurePower) * 100);
            live.Text = displayedCount == 0 ? (engine.Tablet == null ? "No pen connected" : "Waiting for pen input")
                : (sample.InRange ? sample.Tip ? "Tip down" : "Hovering" : "Pen lifted") +
                (sample.Barrel ? "  ·  Barrel button" : "") + (sample.Secondary || sample.Eraser ? "  ·  Second / eraser" : "");
        }
    }

    private void ToggleRelative()
    {
        SetMode(engine.Mode == PointerMode.WindowsPen ? PointerMode.Relative : PointerMode.WindowsPen);
    }

    private void HideToTray()
    {
        tray.Visible = true;
        Hide();
        preview.UpdateSample(new PenSample());
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        displayedCount = -1;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0xff && engine != null) engine.ReadRaw(message.LParam);
        if (message.Msg == 0xfe) pendingScan = true;
        if (message.Msg == 0x312 && message.WParam.ToInt32() == HotKeyId) ToggleRelative();
        base.WndProc(ref message);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        timer.Stop();
        timer.Dispose();
        if (hotkey) Native.UnregisterHotKey(Handle, HotKeyId);
        engine.Dispose();
        foreach (var tablet in tablets) tablet.Dispose();
        if (tray != null)
        {
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
        }
        if (dirty) settings.Save();
        base.OnFormClosed(e);
    }

    private static int Limit(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
}

public sealed class Settings
{
    public string Device = "", Display = "";
    public int Speed = 100, Curve, Rotation, Tip = 1, Barrel = 2, Secondary = 3;
    public int[] Crop = { 0, 0, 0, 0 };
    private static readonly string DirectoryName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabletDesk");
    private static readonly string Filename = Path.Combine(DirectoryName, "Settings.xml");
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Settings));

    internal static Settings Load()
    {
        try
        {
            using (var file = File.OpenRead(Filename))
            {
                var settings = (Settings)Serializer.Deserialize(file);
                if (settings.Crop == null || settings.Crop.Length != 4) settings.Crop = new int[4];
                return settings;
            }
        }
        catch (IOException) { return new Settings(); }
        catch (UnauthorizedAccessException) { return new Settings(); }
        catch (InvalidOperationException) { return new Settings(); }
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryName);
            string temp = Filename + ".tmp";
            using (var file = File.Create(temp)) Serializer.Serialize(file, this);
            if (File.Exists(Filename)) File.Replace(temp, Filename, null);
            else File.Move(temp, Filename);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

