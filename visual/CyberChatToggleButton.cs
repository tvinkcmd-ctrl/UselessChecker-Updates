using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Кнопка вызова ассистента в title bar. Hover-фон рисуется вручную (без блик/тень-полос,
    // чтобы на маленьком размере не было рельс): скруглённая акцентная заливка + рамка + glow.
    // Иконка чата — скруглённый пузырь с хвостиком и тремя точками, без эмодзи.
    public class CyberChatToggleButton : Control
    {
        private bool _isHovered;
        private float _hover;
        private readonly System.Windows.Forms.Timer _anim;

        public CyberChatToggleButton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            _anim = new System.Windows.Forms.Timer { Interval = 16 };
            _anim.Tick += (s, e) =>
            {
                if (_isHovered && _hover < 1f) _hover = Math.Min(1f, _hover + 0.14f);
                else if (!_isHovered && _hover > 0f) _hover = Math.Max(0f, _hover - 0.12f);
                else { _anim.Stop(); return; }
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; _anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _anim.Start(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var bgRect = new Rectangle(5, 5, Width - 10, Height - 10);
            if (_hover > 0.01f)
            {
                // Наружный glow.
                using (var glow = new Pen(CyberPalette.Alpha(CyberPalette.AccentNeon, (int)(40 * _hover)), 1f))
                    g.DrawPath(glow, CyberPalette.Round(Rectangle.Inflate(bgRect, 2, 2), 11));
                // Скруглённая акцентная заливка (плотная, без рельс).
                using (var fill = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentNeon, (int)(48 * _hover))))
                    g.FillPath(fill, CyberPalette.Round(bgRect, 9));
                using (var border = new Pen(CyberPalette.Alpha(CyberPalette.AccentGlow, (int)(90 * _hover)), 1f))
                    g.DrawPath(border, CyberPalette.Round(bgRect, 9));
            }

            var iconColor = CyberPalette.Mix(CyberPalette.TextSecondary, CyberPalette.AccentGlow, _hover);
            float cx = Width / 2f, cy = Height / 2f;
            var bubble = new Rectangle((int)cx - 11, (int)cy - 8, 22, 15);
            using (var bpath = CyberPalette.Round(bubble, 5))
            using (var pen = new Pen(iconColor, 2f))
                g.DrawPath(pen, bpath);
            using (var pen = new Pen(iconColor, 2f))
            {
                g.DrawLine(pen, cx - 5, cy + 6, cx - 9, cy + 11);
                g.DrawLine(pen, cx - 9, cy + 11, cx - 9, cy + 6);
            }
            using (var db = new SolidBrush(iconColor))
            {
                g.FillEllipse(db, cx - 6, cy - 2, 3, 3);
                g.FillEllipse(db, cx - 1.5f, cy - 2, 3, 3);
                g.FillEllipse(db, cx + 3, cy - 2, 3, 3);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim?.Stop(); _anim?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}