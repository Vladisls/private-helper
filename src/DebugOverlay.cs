using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CAHelper
{
    /// Click-through overlay that draws what the Farm Tracker sees. Created DPI-aware so it uses the same
    /// real screen pixels as the captures.
    sealed class DebugOverlay : OverlayForm
    {
        public struct Box { public Rectangle R; public Color C; public string Label; }
        public struct Seg { public Point A, B; public Color C; }
        public struct Note { public Point P; public string Text; public Color C; }
        public readonly List<Box> Boxes = new List<Box>();
        public readonly List<Seg> Lines = new List<Seg>();
        public readonly List<Note> Tags = new List<Note>();
        public Rectangle Virt;
        readonly Font font = new Font("Segoe UI", 9f, FontStyle.Bold), small = new Font("Segoe UI", 8f, FontStyle.Bold);
        static readonly Color Key = Color.FromArgb(255, 0, 255);

        DebugOverlay() { BackColor = Key; TransparencyKey = Key; }

        public static DebugOverlay Create()
        {
            using (new DpiAware())
            {
                var o = new DebugOverlay { Virt = PartyOcr.PhysicalVirtualScreen() };
                o.Bounds = o.Virt;
                o.ShowQuiet();
                return o;
            }
        }

        public void Clear() { Boxes.Clear(); Lines.Clear(); Tags.Clear(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            int ox = -Virt.X, oy = -Virt.Y;
            foreach (var l in Lines) using (var p = new Pen(l.C, 1)) g.DrawLine(p, l.A.X + ox, l.A.Y + oy, l.B.X + ox, l.B.Y + oy);
            foreach (var b in Boxes)
            {
                var r = b.R; r.Offset(ox, oy);
                using (var p = new Pen(b.C, 2)) g.DrawRectangle(p, r);
                if (!string.IsNullOrEmpty(b.Label))
                {
                    var size = g.MeasureString(b.Label, font);
                    var lr = new RectangleF(r.X, Math.Max(0, r.Y - size.Height - 2), size.Width + 6, size.Height + 2);
                    using (var bg = new SolidBrush(Color.FromArgb(20, 20, 20))) g.FillRectangle(bg, lr);
                    using (var br = new SolidBrush(b.C)) g.DrawString(b.Label, font, br, lr.X + 3, lr.Y + 1);
                }
            }
            foreach (var t in Tags)
            {
                var size = g.MeasureString(t.Text, small);
                var lr = new RectangleF(t.P.X + ox, t.P.Y + oy, size.Width + 2, size.Height);
                using (var bg = new SolidBrush(Color.FromArgb(20, 20, 20))) g.FillRectangle(bg, lr);
                using (var br = new SolidBrush(t.C)) g.DrawString(t.Text, small, br, lr.X + 1, lr.Y);
            }
        }
    }
}
