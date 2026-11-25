using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

internal sealed class DeskSelect : Button
{
    internal static readonly Color Fill = Color.FromArgb(32, 44, 40);
    internal static readonly Color Border = Color.FromArgb(69, 89, 80);
    internal static readonly Color TextColor = Color.FromArgb(236, 245, 240);
    internal static readonly Color Dim = Color.FromArgb(145, 166, 156);
    private static readonly Color Highlight = Color.FromArgb(51, 80, 67);
    private const TextFormatFlags TextFlags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
    internal readonly System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
    internal event EventHandler SelectedIndexChanged;
    private readonly ContextMenuStrip menu;
    private int selectedIndex = -1;

    internal DeskSelect()
    {
        Height = 28;
        BackColor = Fill;
        ForeColor = TextColor;
        AccessibleRole = AccessibleRole.ButtonDropDown;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        menu = new ContextMenuStrip { BackColor = Fill, ForeColor = TextColor, ShowImageMargin = false,
            Renderer = new SelectRenderer(), MaximumSize = new Size(0, 260) };
        menu.Closed += delegate { Invalidate(); };
    }

    internal int SelectedIndex
    {
        get { return selectedIndex; }
        set
        {
            if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("value");
            Text = value < 0 ? "" : Items[value];
            AccessibleDescription = Text;
            if (selectedIndex == value) return;
            selectedIndex = value;
            Invalidate();
            var changed = SelectedIndexChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || Items.Count == 0 || menu.Visible) return;
        foreach (ToolStripItem item in new System.Collections.Generic.List<ToolStripItem>(ItemsInMenu())) item.Dispose();
        menu.Items.Clear();
        ToolStripMenuItem selected = null;
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(Items[i]) { BackColor = Fill, ForeColor = TextColor,
                AutoSize = false, Size = new Size(Width - 2, 27), DisplayStyle = ToolStripItemDisplayStyle.Text };
            item.Click += delegate { SelectedIndex = index; };
            menu.Items.Add(item);
            if (index == SelectedIndex) selected = item;
        }
        menu.Show(this, new Point(0, Height), ToolStripDropDownDirection.BelowRight);
        if (selected != null) selected.Select();
        Invalidate();
    }

    private System.Collections.Generic.IEnumerable<ToolStripItem> ItemsInMenu()
    {
        foreach (ToolStripItem item in menu.Items) yield return item;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if ((keys == Keys.F4 || keys == (Keys.Alt | Keys.Down)) && Enabled)
        {
            PerformClick();
            return true;
        }
        if ((keys == Keys.Up || keys == Keys.Down || keys == Keys.Home || keys == Keys.End) && Items.Count > 0 && !menu.Visible)
        {
            if (keys == Keys.Home) SelectedIndex = 0;
            else if (keys == Keys.End) SelectedIndex = Items.Count - 1;
            else SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, SelectedIndex + (keys == Keys.Up ? -1 : 1)));
            return true;
        }
        return base.ProcessCmdKey(ref message, keys);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        Color line = (Focused || menu.Visible) && Enabled ? Dashboard.Mint : Border;
        using (var fill = new SolidBrush(Enabled ? Fill : Dashboard.Canvas))
        using (var outline = new Pen(line))
        using (var arrow = new Pen(Enabled ? Dashboard.Mint : Dim, 1.35f))
        {
            graphics.FillRectangle(fill, ClientRectangle);
            graphics.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
            int x = Width - 14, y = Height / 2;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawLines(arrow, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
        }
        TextRenderer.DrawText(graphics, Text, Font, new Rectangle(8, 0, Math.Max(0, Width - 28), Height),
            Enabled ? TextColor : Dim, TextFlags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }

    private sealed class SelectRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(Fill)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            using (var brush = new SolidBrush(Highlight)) e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }
}

internal sealed class CropField : UserControl
{
    private readonly TextBox input;
    private int value, maximum = 90;
    internal event EventHandler ValueChanged;

    internal CropField(string edge)
    {
        Height = 30;
        MinimumSize = new Size(75, 28);
        BackColor = DeskSelect.Fill;
        AccessibleName = edge + " crop";
        TabStop = false;
        input = new TextBox { BorderStyle = BorderStyle.None, BackColor = DeskSelect.Fill,
            ForeColor = DeskSelect.TextColor, TextAlign = HorizontalAlignment.Center, Text = "0", MaxLength = 2,
            AccessibleName = edge + " crop percentage" };
        Controls.Add(input);
        var minus = StepButton("−", "Decrease " + edge.ToLowerInvariant() + " crop", -1);
        var plus = StepButton("+", "Increase " + edge.ToLowerInvariant() + " crop", 1);
        Resize += delegate
        {
            input.SetBounds(24, Math.Max(4, (Height - input.PreferredHeight) / 2),
                Math.Max(20, Width - 48), input.PreferredHeight);
            minus.SetBounds(1, 1, 23, Height - 2);
            plus.SetBounds(Width - 24, 1, 23, Height - 2);
        };
        input.Validated += delegate { Commit(); };
        input.Enter += delegate { input.SelectAll(); Invalidate(); };
        input.Leave += delegate { Invalidate(); };
        input.KeyPress += delegate(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };
        input.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter)
            {
                Commit();
                if (e.KeyCode != Keys.Enter) Value += e.KeyCode == Keys.Up ? 1 : -1;
                input.SelectAll();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }

    internal int Value
    {
        get { return value; }
        set
        {
            int next = Math.Max(0, Math.Min(maximum, value));
            input.Text = next.ToString(CultureInfo.InvariantCulture);
            if (this.value == next) return;
            this.value = next;
            var changed = ValueChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }
    }

    internal int Maximum
    {
        get { return maximum; }
        set { maximum = value; if (this.value > maximum) Value = maximum; }
    }

    private void Commit()
    {
        int next;
        Value = int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out next) ? next : Value;
    }

    private Button StepButton(string text, string name, int direction)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            ForeColor = Dashboard.Mint, BackColor = DeskSelect.Fill, Cursor = Cursors.Hand,
            Margin = Padding.Empty, Padding = Padding.Empty, AccessibleName = name };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 80, 68);
        button.Click += delegate { Commit(); Value += direction; };
        Controls.Add(button);
        return button;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var outline = new Pen(ContainsFocus ? Dashboard.Mint : DeskSelect.Border))
            e.Graphics.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
    }
}

