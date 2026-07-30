using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Стеклянный прогресс-бар: вдавленный полупрозрачный трек + градиентное заполнение
    // с верхним бликом и мягким свечением правого края (без жёсткого неона).
    public class CyberProgressBar : Control
    {
        private int _val;
        public int Value
        {
            get => _val;
            set { _val = value < 0 ? 0 : value > 100 ? 100 : value; Invalidate(); }
        }
        public Color AccentColor { get; set; } = CyberPalette.AccentNeon;

        public CyberProgressBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = 6;

            // Вдавленный трек: тёмная полупрозрачная впадина + внутренний верхний shadow.
            using (var track = CyberPalette.Round(rect, radius))
            {
                using (var tb = new SolidBrush(CyberPalette.Alpha(Color.Black, 95))) g.FillPath(tb, track);
                g.SetClip(track);
                using (var sh = new LinearGradientBrush(rect, CyberPalette.Alpha(Color.Black, 70), Color.Transparent, 90f))
                    g.FillRectangle(sh, rect.X, rect.Y, rect.Width, rect.Height / 2);
                g.ResetClip();
                using (var bp = new Pen(CyberPalette.Alpha(Color.White, 12), 1f)) g.DrawPath(bp, track);
            }

            int fillW = (int)(rect.Width * (_val / 100f));
            if (fillW > 0)
            {
                var fr = new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, fillW - 2), rect.Height - 2);
                using var fp = CyberPalette.Round(fr, Math.Max(2, radius - 1));
                using (var fb = new LinearGradientBrush(fr, CyberPalette.AccentRedDeep, AccentColor, 0f))
                    g.FillPath(fb, fp);
                g.SetClip(fp);
                // Верхний блик заполнения.
                using (var hl = new LinearGradientBrush(fr, CyberPalette.Alpha(Color.White, 60), Color.Transparent, 90f))
                    g.FillRectangle(hl, fr.X, fr.Y, fr.Width, fr.Height / 2);
                // Мягкое свечение правого края.
                if (fillW > radius * 2)
                    using (var glow = new LinearGradientBrush(
                        new Rectangle(fr.Right - 16, fr.Y, 16, fr.Height),
                        CyberPalette.Alpha(CyberPalette.AccentGlow, 110), Color.Transparent, 0f))
                        g.FillRectangle(glow, fr.Right - 16, fr.Y, 16, fr.Height);
                g.ResetClip();
            }
        }
    }
}