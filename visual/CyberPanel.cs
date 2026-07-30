using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Основа всего интерфейса — матовое стекло. Фон рисуется через CyberPalette.DrawGlassSurface
    // полупрозрачно, поэтому ambient-пятна окна просвечивают сквозь панель. Публичный API
    // (FillColor/BorderColor/GlowColor/GlowOnHover) сохранён, чтобы UI и логика не ломались.
    public class CyberPanel : Panel
    {
        public Color BorderColor { get; set; } = CyberPalette.BorderColor;
        public Color FillColor { get; set; } = CyberPalette.CardBg;
        public Color GlowColor { get; set; } = CyberPalette.AccentNeon;
        public bool GlowOnHover { get; set; } = true;
        public int CornerRadius { get; set; } = 16;

        private float _hover;
        private bool _isHovered;
        private float _rippleR, _rippleA;
        private Point _rippleC;
        private readonly System.Windows.Forms.Timer _anim, _ripple;

        public CyberPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; // важно: прозрачность до ambient-фона окна

            _anim = new System.Windows.Forms.Timer { Interval = 16 };
            _anim.Tick += (s, e) =>
            {
                if (_isHovered && _hover < 1f) _hover = Math.Min(1f, _hover + 0.08f);
                else if (!_isHovered && _hover > 0f) _hover = Math.Max(0f, _hover - 0.08f);
                else { _anim.Stop(); return; }
                Invalidate();
            };
            _ripple = new System.Windows.Forms.Timer { Interval = 16 };
            _ripple.Tick += (s, e) =>
            {
                _rippleR += 3f; _rippleA -= 12f;
                if (_rippleA <= 0) { _rippleA = 0; _ripple.Stop(); }
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e) { if (GlowOnHover) { _isHovered = true; _anim.Start(); } base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { if (GlowOnHover) { _isHovered = false; _anim.Start(); } base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _rippleC = e.Location; _rippleR = 2f; _rippleA = 95f; _ripple.Start(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            CyberPalette.DrawGlassSurface(g, rect, CornerRadius, FillColor, _hover, GlowColor);

            if (_rippleA > 0)
            {
                using var path = CyberPalette.Round(rect, CornerRadius);
                g.SetClip(path);
                using var rb = new SolidBrush(CyberPalette.Alpha(GlowColor, (int)_rippleA));
                g.FillEllipse(rb, _rippleC.X - _rippleR, _rippleC.Y - _rippleR, _rippleR * 2, _rippleR * 2);
                g.ResetClip();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim?.Stop(); _anim?.Dispose(); _ripple?.Stop(); _ripple?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}