using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Премиальная кнопка-плитка. Визуал переписан с нуля против «рельс 2012»:
    // плотный собственный градиент (не полупрозрачный серый), объём через мягкий внутренний
    // свет сверху (без нижней чёрной полосы), стеклянный край 1px, наружное цветное свечение
    // на hover/active и светящаяся левая полоса-индикатор у активной вкладки.
    public class CyberButton : Control, IButtonControl
    {
        private float _hover;
        private bool _isHovered;
        private bool _active;
        private DialogResult _dialogResult;
        private float _rippleR, _rippleA;
        private Point _rippleC;
        private readonly System.Windows.Forms.Timer _anim, _ripple;

        public Color AccentColor { get; set; } = CyberPalette.AccentNeon;
        public Color CustomBaseColor { get; set; } = CyberPalette.CardBg;
        public int CornerRadius { get; set; } = 12;

        public bool Active
        {
            get => _active;
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        public CyberButton()
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
            _ripple = new System.Windows.Forms.Timer { Interval = 16 };
            _ripple.Tick += (s, e) =>
            {
                _rippleR += 3f; _rippleA -= 16f;
                if (_rippleA <= 0) { _rippleA = 0; _ripple.Stop(); }
                Invalidate();
            };
        }

        public DialogResult DialogResult { get => _dialogResult; set => _dialogResult = value; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; _anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _anim.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _rippleC = e.Location; _rippleR = 2f; _rippleA = 150f; _ripple.Start(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float eff = _active ? 1f : _hover;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = CornerRadius;
            using var path = CyberPalette.Round(rect, radius);

            // 1) Наружное цветное свечение ПОД кнопкой (читается как «кнопка светится»).
            if (eff > 0.01f)
            {
                int[] al = { 30, 18, 9 };
                for (int i = 0; i < al.Length; i++)
                {
                    var outer = Rectangle.Inflate(rect, i + 2, i + 2);
                    using var op = CyberPalette.Round(outer, radius + i + 2);
                    using var pen = new Pen(CyberPalette.Alpha(AccentColor, (int)(al[i] * eff)), 1f);
                    g.DrawPath(pen, op);
                }
            }

            // 2) Плотный собственный градиент: верх осветлён, низ = база. НЕ полупрозрачный.
            var topCol = CyberPalette.Mix(CustomBaseColor, CyberPalette.CardBgTop, 0.65f);
            var botCol = CyberPalette.Mix(CustomBaseColor, Color.Black, 0.10f);
            using (var bg = new LinearGradientBrush(rect, topCol, botCol, 90f))
                g.FillPath(bg, path);

            g.SetClip(path);

            // 3) Мягкий внутренний свет сверху-центр (объём без рельс).
            using (var hl = new PathGradientBrush(CyberPalette.Round(
                new Rectangle(rect.X + rect.Width / 4, rect.Y - rect.Height / 2, rect.Width / 2, rect.Height), radius)))
            {
                hl.CenterColor = CyberPalette.Alpha(Color.White, 26);
                hl.SurroundColors = new[] { Color.Transparent };
                g.FillPath(hl, CyberPalette.Round(rect, radius));
            }

            // 4) Тёплый акцентный подъём при hover/active.
            if (eff > 0.01f)
                using (var warm = new LinearGradientBrush(rect,
                    CyberPalette.Alpha(AccentColor, (int)(eff * 46)),
                    CyberPalette.Alpha(AccentColor, (int)(eff * 14)), 90f))
                    g.FillRectangle(warm, rect);

            // 5) Ripple.
            if (_rippleA > 0)
                using (var rb = new SolidBrush(CyberPalette.Alpha(AccentColor, (int)_rippleA)))
                    g.FillEllipse(rb, _rippleC.X - _rippleR, _rippleC.Y - _rippleR, _rippleR * 2, _rippleR * 2);

            // 6) Светящаяся левая полоса-индикатор у активной вкладки.
            if (_active)
            {
                var bar = new Rectangle(rect.X + 2, rect.Y + 6, 3, rect.Height - 12);
                using (var glow = new LinearGradientBrush(new Rectangle(bar.X, bar.Y, 14, bar.Height),
                    CyberPalette.Alpha(AccentColor, 120), Color.Transparent, 0f))
                    g.FillRectangle(glow, bar.X, bar.Y, 14, bar.Height);
                using (var bb = new LinearGradientBrush(bar, CyberPalette.AccentGlow, AccentColor, 90f))
                    g.FillRectangle(bb, bar);
            }
            g.ResetClip();

            // 7) Рамка-фаска + стеклянный верхний край 1px (без нижней полосы-рельсы).
            using (var border = new Pen(CyberPalette.Mix(CyberPalette.BorderColor, AccentColor, eff), 1f + 0.4f * eff))
                g.DrawPath(border, path);
            using (var topHi = new Pen(CyberPalette.Alpha(Color.White, 46 + (int)(eff * 24)), 1f))
                g.DrawLine(topHi, rect.X + radius, rect.Y, rect.Right - radius, rect.Y);

            // 8) Текст: белый на active/hover, иначе основной.
            Color txt = eff > 0.5f ? Color.White
                : CyberPalette.Mix(CyberPalette.TextPrimary, Color.White, _hover * 0.3f);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, txt,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}