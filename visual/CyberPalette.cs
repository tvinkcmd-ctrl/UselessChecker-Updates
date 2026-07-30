using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace UselessChecker
{
    // Палитра Modern Glass + движок отрисовки. Исправлено против «полосы посередине»:
    // DrawGlassSurface больше не кладёт блик и тень двумя половинными прямоугольниками
    // (шов на стыке). Объём = один плавный градиент на всю высоту (ColorBlend), блик живёт
    // только в верхней трети, тень — только в нижней, середина ровная -> шва нет ни на
    // карточках, ни на мелких кнопках.
    public static class CyberPalette
    {
        public static readonly Color Background     = Color.FromArgb(10, 10, 14);
        public static readonly Color BackgroundWarm = Color.FromArgb(15, 11, 17);
        public static readonly Color PanelBg        = Color.FromArgb(20, 20, 26);
        public static readonly Color CardBg         = Color.FromArgb(26, 26, 33);
        public static readonly Color CardBgTop      = Color.FromArgb(34, 34, 42);
        public static readonly Color CardHover      = Color.FromArgb(40, 32, 38);

        public static readonly Color AccentNeon     = Color.FromArgb(224, 38, 64);
        public static readonly Color AccentRedDeep  = Color.FromArgb(150, 14, 34);
        public static readonly Color AccentRedMuted = Color.FromArgb(176, 86, 96);
        public static readonly Color AccentGlow     = Color.FromArgb(255, 96, 116);
        public static readonly Color AccentCyan     = Color.FromArgb(96, 150, 205);
        public static readonly Color AccentEmerald  = Color.FromArgb(86, 168, 120);

        public static readonly Color TextPrimary    = Color.FromArgb(245, 243, 246);
        public static readonly Color TextSecondary  = Color.FromArgb(150, 150, 162);
        public static readonly Color TextDark       = Color.FromArgb(104, 104, 114);
        public static readonly Color BorderColor    = Color.FromArgb(48, 48, 56);
        public static readonly Color BorderHover    = Color.FromArgb(110, 70, 82);

        public static Color Alpha(Color c, int a) => Color.FromArgb(Clamp(a), c.R, c.G, c.B);

        public static Color Mix(Color a, Color b, float t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void DrawBlob(Graphics g, float cx, float cy, float rx, float ry, Color core)
        {
            var rect = new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2);
            using var path = new GraphicsPath();
            path.AddEllipse(rect);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = core,
                SurroundColors = new[] { Color.Transparent },
                CenterPoint = new PointF(cx, cy)
            };
            g.FillPath(brush, path);
        }

        // Живой фон зоны (главное окно, модалки). Без изменений — пятен на весь экран, швов нет.
        public static void DrawAmbientBackground(Graphics g, Rectangle rect)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float w = rect.Width, h = rect.Height;

            using (var bg = new LinearGradientBrush(rect, Background, BackgroundWarm, 90f))
                g.FillRectangle(bg, rect);

            DrawBlob(g, w * 0.82f, h * 0.16f, w * 0.42f, h * 0.55f, Alpha(AccentNeon, 40));
            DrawBlob(g, w * 0.12f, h * 0.92f, w * 0.40f, h * 0.52f, Alpha(AccentRedDeep, 46));
            DrawBlob(g, w * 0.30f, h * 0.46f, w * 0.34f, h * 0.44f, Alpha(Color.FromArgb(38, 50, 70), 30));

            using (var vig = new PathGradientBrush(new[] {
                new PointF(0, 0), new PointF(w, 0), new PointF(w, h), new PointF(0, h) }))
            {
                vig.CenterColor = Color.Transparent;
                vig.SurroundColors = new[] { Alpha(Color.Black, 110) };
                g.FillRectangle(vig, rect);
            }
        }

        // Плотный материал-стекло БЕЗ горизонтального шва. Объём — один ColorBlend-градиент
        // на всю высоту; блик и тень ограничены третями и заливаются ТОЛЬКО своими третями
        // (не полным rect, как раньше — именно заливка полным rect половинной кистью и давала полосу).
        public static void DrawGlassSurface(Graphics g, Rectangle rect, int radius, Color tint, float hover, Color accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var path = Round(rect, radius);

            // 1) Плотная тонировка (читается на любом фоне).
            int baseAlpha = 205 + (int)(hover * 40);
            using (var fill = new SolidBrush(Alpha(tint, baseAlpha)))
                g.FillPath(fill, path);

            g.SetClip(path);

            // 2) Объём одним плавным градиентом: светлее сверху -> база в середине -> темнее снизу.
            using (var vol = new LinearGradientBrush(rect, Color.White, Color.Black, 90f))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { Alpha(Color.White, 16), Alpha(tint, 0), Alpha(Color.Black, 30) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                vol.InterpolationColors = blend;
                g.FillRectangle(vol, rect);
            }

            // 3) Тёплый акцентный подъём при hover/active (плавно, без шва).
            if (hover > 0.01f)
                using (var warm = new LinearGradientBrush(rect,
                    Alpha(accent, (int)(hover * 42)), Alpha(accent, (int)(hover * 12)), 90f))
                    g.FillRectangle(warm, rect);

            // 4) Верхний блик — ТОЛЬКО верхняя треть, гаснет до середины.
            int hiH = Math.Max(2, (int)(rect.Height * 0.35f));
            var hiRect = new Rectangle(rect.X, rect.Y, rect.Width, hiH);
            using (var hl = new LinearGradientBrush(hiRect, Alpha(Color.White, 30), Color.Transparent, 90f))
                g.FillRectangle(hl, hiRect);

            // 5) Нижняя тень — ТОЛЬКО нижняя треть, начинается прозрачной у середины.
            int shH = Math.Max(2, (int)(rect.Height * 0.35f));
            var shRect = new Rectangle(rect.X, rect.Bottom - shH, rect.Width, shH);
            using (var sh = new LinearGradientBrush(shRect, Color.Transparent, Alpha(Color.Black, 42), 90f))
                g.FillRectangle(sh, shRect);

            // 6) Внутреннее акцентное свечение при hover/active.
            if (hover > 0.01f)
            {
                int[] al = { 60, 34, 16 };
                for (int i = 0; i < al.Length; i++)
                {
                    var inner = new Rectangle(rect.X + i + 1, rect.Y + i + 1, rect.Width - (i + 1) * 2, rect.Height - (i + 1) * 2);
                    using var ip = Round(inner, Math.Max(1, radius - i - 1));
                    using var pen = new Pen(Alpha(accent, (int)(al[i] * hover)), 1f);
                    g.DrawPath(pen, ip);
                }
            }
            g.ResetClip();

            // 7) Светлый контур + яркий блик по верхней кромке 1px (без нижней полосы).
            using (var border = new Pen(Alpha(Color.White, 30 + (int)(hover * 22)), 1f))
                g.DrawPath(border, path);
            using (var topHi = new Pen(Alpha(Color.White, 60), 1f))
                g.DrawLine(topHi, rect.X + radius, rect.Y, rect.Right - radius, rect.Y);
        }
    }
}