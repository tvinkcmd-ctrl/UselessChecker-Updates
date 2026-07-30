using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Премиальная карточка контакта в стиле Modern Glass: стеклянная поверхность,
    // бренд-свечение по периметру при наведении, живой ambient-пульс вокруг иконки
    // платформы (карточка «дышит» даже без курсора), векторные логотипы Telegram/Discord
    // (полигоны, без эмодзи), стеклянная кнопка действия внутри. Публичные свойства
    // (Platform/Title/Subtitle/ButtonText/Url/BrandColor) сохранены — UI их задаёт как раньше.
    public class CyberContactCard : Control
    {
        private bool _isHovered;
        private float _hover;
        private readonly System.Windows.Forms.Timer _anim;
        private readonly System.Windows.Forms.Timer _pulse; // ambient-пульс иконки
        private float _rippleR;
        private Point _rippleC;
        private float _rippleA;
        private readonly System.Windows.Forms.Timer _ripple;

        public string Platform { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ButtonText { get; set; }
        public string Url { get; set; }
        public Color BrandColor { get; set; }

        public CyberContactCard()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;

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
                _rippleR += 3.5f; _rippleA -= 12f;
                if (_rippleA <= 0) { _rippleA = 0; _ripple.Stop(); }
                Invalidate();
            };
            _pulse = new System.Windows.Forms.Timer { Interval = 45 };
            _pulse.Tick += (s, e) => { if (Visible) Invalidate(); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _pulse.Start(); else _pulse.Stop();
        }
        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; _anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _anim.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _rippleC = e.Location; _rippleR = 2f; _rippleA = 120f; _ripple.Start(); }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            try { if (!string.IsNullOrEmpty(Url)) Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Error($"Failed to open URL: {Url}", ex); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = 18;

            // Стеклянная поверхность с бренд-акцентом на hover.
            CyberPalette.DrawGlassSurface(g, rect, radius, CyberPalette.CardBg, _hover, BrandColor);

            // Ripple по клику.
            if (_rippleA > 0)
            {
                using var path = CyberPalette.Round(rect, radius);
                g.SetClip(path);
                using var rb = new SolidBrush(CyberPalette.Alpha(BrandColor, (int)_rippleA));
                g.FillEllipse(rb, _rippleC.X - _rippleR, _rippleC.Y - _rippleR, _rippleR * 2, _rippleR * 2);
                g.ResetClip();
            }

            // Иконка платформы: живой пульс-ореол бренд-цветом + стеклянный круг.
            int iconSize = 64, iconX = 25, iconY = (Height - iconSize) / 2 - 14;
            float beat = (float)(0.5 + 0.5 * Math.Sin(DateTime.Now.TimeOfDay.TotalMilliseconds / 420.0));
            int haloA = (int)(26 + 30 * beat) + (int)(_hover * 30);
            using (var halo = new SolidBrush(CyberPalette.Alpha(BrandColor, haloA)))
                g.FillEllipse(halo, iconX - 6, iconY - 6, iconSize + 12, iconSize + 12);
            var iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);
            using (var iconPath = CyberPalette.Round(iconRect, iconSize / 2))
            {
                using (var ib = new LinearGradientBrush(iconRect,
                    CyberPalette.Mix(BrandColor, Color.White, 0.12f), BrandColor, 90f))
                    g.FillPath(ib, iconPath);
                using var ip = new Pen(CyberPalette.Alpha(Color.White, 60), 1f);
                g.DrawPath(ip, iconPath);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Platform == "Telegram")
            {
                using var iconPen = new Pen(Color.White, 2.5f) { LineJoin = LineJoin.Round };
                float cx = iconX + iconSize / 2f, cy = iconY + iconSize / 2f;
                g.DrawPolygon(iconPen, new[] {
                    new PointF(cx - 14, cy - 2), new PointF(cx + 16, cy - 14), new PointF(cx + 4, cy + 14),
                    new PointF(cx - 2, cy + 4), new PointF(cx - 14, cy - 2), new PointF(cx + 16, cy - 14),
                    new PointF(cx - 2, cy + 4), new PointF(cx - 6, cy + 10), new PointF(cx - 4, cy + 4)
                });
            }
            else if (Platform == "Discord")
            {
                float cx = iconX + iconSize / 2f, cy = iconY + iconSize / 2f;
                using (var wb = new SolidBrush(Color.White)) g.FillEllipse(wb, cx - 18, cy - 12, 36, 24);
                using (var eb = new SolidBrush(BrandColor)) { g.FillEllipse(eb, cx - 9, cy - 4, 6, 6); g.FillEllipse(eb, cx + 3, cy - 4, 6, 6); }
            }

            // Тексты: крупный контрастный заголовок + приглушённый подзаголовок.
            int textX = iconX + iconSize + 22, textY = iconY + 8;
            using (var tf = new Font("Segoe UI Semibold", 14f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, tf, new Rectangle(textX, textY, Width - textX - 20, 30),
                    Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var sf = new Font("Segoe UI", 9.5f))
                TextRenderer.DrawText(g, Subtitle, sf, new Rectangle(textX, textY + 30, Width - textX - 20, 22),
                    CyberPalette.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Стеклянная кнопка действия внутри карточки.
            int btnW = Width - 50, btnH = 38, btnX = 25, btnY = Height - 58;
            var btnRect = new Rectangle(btnX, btnY, btnW, btnH);
            CyberPalette.DrawGlassSurface(g, btnRect, 10, Color.FromArgb(20, 20, 24), _hover, BrandColor);
            using (var bf = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, ButtonText, bf, btnRect,
                    CyberPalette.Mix(CyberPalette.TextSecondary, Color.White, _hover),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim?.Stop(); _anim?.Dispose();
                _ripple?.Stop(); _ripple?.Dispose();
                _pulse?.Stop(); _pulse?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}