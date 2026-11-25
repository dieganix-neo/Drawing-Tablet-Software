using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class TabletView : Control
{
    private PenSample sample;
    private readonly List<Stroke> strokes = new List<Stroke>();
    private PointF? last;
    internal RectangleF Area = new RectangleF(0, 0, 1, 1);
    internal double PressurePower = 1;
    private struct Stroke { internal PointF From, To; internal float Width; }

    internal TabletView()
    {
        BackColor = Dashboard.Canvas;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.ResizeRedraw, true);
        AccessibleName = "Live pen position and pressure preview";
    }

    internal void UpdateSample(PenSample value)
    {
        sample = value;
        var point = new PointF((float)value.X, (float)value.Y);
        if (value.InRange && value.Tip)
        {
            float width = (float)(1.6 + 8 * Math.Pow(value.Pressure, PressurePower));
            if (strokes.Count >= 2200) strokes.RemoveRange(0, 300);
            if (!last.HasValue || last.Value != point)
                strokes.Add(new Stroke { From = last ?? point, To = point, Width = width });
            last = point;
        }
        else last = null;
        Invalidate();
    }

    internal void Clear()
    {
        strokes.Clear();
        last = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var field = new RectangleF(16, 16, Math.Max(1, Width - 32), Math.Max(1, Height - 32));
        for (int x = 0; x <= 16; x++)
            for (int y = 0; y <= 10; y++)
                using (var dot = new SolidBrush(Color.FromArgb(53, 70, 64)))
                    g.FillEllipse(dot, field.X + field.Width * x / 16, field.Y + field.Height * y / 10, 2, 2);
        var crop = new RectangleF(field.X + field.Width * Area.X, field.Y + field.Height * Area.Y,
            field.Width * Area.Width, field.Height * Area.Height);
        using (var frame = new Pen(Color.FromArgb(73, 119, 102)) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(frame, crop.X, crop.Y, crop.Width, crop.Height);
        using (var ink = new Pen(Dashboard.Mint) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var fill = new SolidBrush(Dashboard.Mint))
        {
            foreach (var stroke in strokes)
            {
                ink.Width = stroke.Width;
                var p = Map(field, stroke.From);
                if (stroke.From == stroke.To) g.FillEllipse(fill, p.X - ink.Width / 2, p.Y - ink.Width / 2, ink.Width, ink.Width);
                else g.DrawLine(ink, p, Map(field, stroke.To));
            }
            if (sample.InRange)
            {
                var p = Map(field, new PointF((float)sample.X, (float)sample.Y));
                ink.Width = 1.25f;
                g.DrawEllipse(ink, p.X - 5, p.Y - 5, 10, 10);
            }
        }
    }

    private static PointF Map(RectangleF field, PointF point)
    {
        return new PointF(field.X + field.Width * point.X, field.Y + field.Height * point.Y);
    }
}
