using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Кибер-симулятор полёта дрона. Геймплей нетронут; переписан визуал (виньетка, неоновые
    // барьеры со свечением, трейл дрона, живой ambient на старте/геймовере) и добавлен Dispose
    // для таймеров (в оригинале таймер игры утекал). Публичный API: OnTabShown/OnTabHidden.
    public class CyberFlappyPanel : Panel
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _idleTimer; // ambient-анимация вне игры
        private readonly List<FlappyBarrier> _barriers = new List<FlappyBarrier>();
        private readonly LinkedList<PointF> _trail = new LinkedList<PointF>(); // затухающий след дрона
        private readonly Random _random = new Random();
        private float _birdY, _birdV;
        private const float Gravity = 0.40f;
        private const float JumpImpulse = -7.2f;
        private const float DroneRadius = 14f;
        private const float BarrierWidth = 65f;
        private const float BarrierGap = 155f;
        private const float BarrierSpeed = 3.5f;
        private const int SpawnIntervalTicks = 80;
        private const float DroneX = 160f;
        private int _score, _highScore, _spawnTicks;
        private bool _isPlaying, _isGameOver;
        private CyberButton _btnAction;

        public CyberFlappyPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
         ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

            _timer = new System.Windows.Forms.Timer { Interval = 20 };
            _timer.Tick += GameUpdateLoop;

            _idleTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _idleTimer.Tick += (s, e) => { if (Visible && !_isPlaying) Invalidate(); };

            InitializeControls();
        }

        private void InitializeControls()
        {
            _btnAction = new CyberButton
            {
                Text = "ЗАПУСТИТЬ ДРОН",
                Size = new Size(220, 45),
                AccentColor = CyberPalette.AccentEmerald,
                CustomBaseColor = CyberPalette.CardBg,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                ForeColor = Color.White
            };
            _btnAction.Click += (s, e) => StartGame();
            Controls.Add(_btnAction);
        }

        // Управление ambient-таймером: живёт только когда панель видима и игра не идёт.
        private void ManageIdle()
        {
            if (Visible && !_isPlaying) _idleTimer.Start();
            else _idleTimer.Stop();
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); ManageIdle(); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_btnAction != null)
                _btnAction.Location = new Point((Width - _btnAction.Width) / 2, Height / 2 + 50);
        }

        public void OnTabShown() { Focus(); ManageIdle(); }
        public void OnTabHidden()
        {
            if (_isPlaying && !_isGameOver)
            {
                _timer.Stop();
                _isPlaying = false;
                _btnAction.Text = "ПРОДОЛЖИТЬ СИМУЛЯЦИЮ";
                _btnAction.Visible = true;
                Invalidate();
            }
            ManageIdle();
        }

        private void StartGame()
        {
            _score = 0; _birdY = Height / 2; _birdV = 0f;
            _barriers.Clear(); _trail.Clear(); _spawnTicks = 0;
            _isPlaying = true; _isGameOver = false;
            _btnAction.Visible = false;
            SpawnBarrier();
            _timer.Start();
            ManageIdle();
            Focus();
            Invalidate();
        }

        private void GameOver()
        {
            _timer.Stop();
            _isGameOver = true; _isPlaying = false;
            if (_score > _highScore) _highScore = _score;
            _btnAction.Text = "ИНИЦИАЛИЗИРОВАТЬ ЗАНОВО";
            _btnAction.Visible = true;
            _btnAction.BringToFront();
            ManageIdle();
            Invalidate();
        }

        private void Jump() { if (_isPlaying && !_isGameOver) _birdV = JumpImpulse; }

        private void SpawnBarrier()
        {
            float padding = BarrierGap / 2 + 40f;
            float gapY = (float)(_random.NextDouble() * (Height - 2 * padding) + padding);
            _barriers.Add(new FlappyBarrier { X = Width, GapY = gapY, Passed = false });
        }

        private void GameUpdateLoop(object sender, EventArgs e)
        {
            if (!_isPlaying || _isGameOver) return;
            _birdV += Gravity; _birdY += _birdV;
            if (_birdY - DroneRadius < 0) { _birdY = DroneRadius; _birdV = 0; }
            if (_birdY + DroneRadius > Height) { GameOver(); return; }

            // Трейл: храним последние позиции, старые выталкиваем.
            _trail.AddLast(new PointF(DroneX, _birdY));
            while (_trail.Count > 14) _trail.RemoveFirst();

            if (++_spawnTicks >= SpawnIntervalTicks) { _spawnTicks = 0; SpawnBarrier(); }

            for (int i = _barriers.Count - 1; i >= 0; i--)
            {
                var bar = _barriers[i];
                bar.X -= BarrierSpeed;
                if (CheckCollision(DroneX, _birdY, DroneRadius, bar)) { GameOver(); return; }
                if (!bar.Passed && bar.X + BarrierWidth < DroneX) { bar.Passed = true; _score += 100; }
                if (bar.X + BarrierWidth < 0) _barriers.RemoveAt(i);
            }
            Invalidate();
        }

        private bool CheckCollision(float bx, float by, float br, FlappyBarrier bar)
        {
            float rh1 = bar.GapY - BarrierGap / 2;
            float ry2 = bar.GapY + BarrierGap / 2;
            return Intersects(bx, by, br, bar.X, 0, BarrierWidth, rh1) ||
                   Intersects(bx, by, br, bar.X, ry2, BarrierWidth, Height - ry2);
        }
        private static bool Intersects(float cx, float cy, float r, float rx, float ry, float rw, float rh)
        {
            float dx = cx - Math.Max(rx, Math.Min(cx, rx + rw));
            float dy = cy - Math.Max(ry, Math.Min(cy, ry + rh));
            return dx * dx + dy * dy < r * r;
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { Jump(); e.Handled = true; } }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Jump(); Focus(); } }

        // Радиальная виньетка для глубины сцены.
        private void DrawVignette(Graphics g)
        {
            var r = new Rectangle(0, 0, Width, Height);
            using var b = new PathGradientBrush(new[] {
                new PointF(0, 0), new PointF(Width, 0), new PointF(Width, Height), new PointF(0, Height) });
            b.CenterColor = Color.Transparent;
            b.SurroundColors = new[] { CyberPalette.Alpha(Color.Black, 90) };
            g.FillRectangle(b, r);
        }

        // Живой ambient: бегущая сканирующая линия + пульсирующие кольца (старт/геймовер).
        private void DrawAmbient(Graphics g)
        {
            double t = DateTime.Now.TimeOfDay.TotalMilliseconds;
            float scanY = (float)((t / 14.0) % Height);
            using (var sb = new LinearGradientBrush(new Rectangle(0, (int)scanY - 20, Width, 40),
                       Color.Transparent, CyberPalette.Alpha(CyberPalette.AccentCyan, 26), 90f))
                g.FillRectangle(sb, 0, (int)scanY - 20, Width, 20);
            using (var sb2 = new LinearGradientBrush(new Rectangle(0, (int)scanY, Width, 40),
                       CyberPalette.Alpha(CyberPalette.AccentCyan, 26), Color.Transparent, 90f))
                g.FillRectangle(sb2, 0, (int)scanY, Width, 20);

            float pulse = (float)(0.5 + 0.5 * Math.Sin(t / 350.0));
            float cx = Width / 2f, cy = Height / 2f - 30;
            for (int i = 0; i < 3; i++)
            {
                float rad = 60 + i * 46 + pulse * 10;
                int a = (int)(26 - i * 7);
                if (a <= 0) continue;
                using var pen = new Pen(CyberPalette.Alpha(CyberPalette.AccentNeon, a), 1f);
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Фоновая сетка.
            using (var gridPen = new Pen(Color.FromArgb(20, 20, 24), 1))
            {
                for (int x = 0; x < Width; x += 40) g.DrawLine(gridPen, x, 0, x, Height);
                for (int y = 0; y < Height; y += 40) g.DrawLine(gridPen, 0, y, Width, y);
            }
            DrawVignette(g);
            using (var borderPen = new Pen(CyberPalette.BorderColor, 1f))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            if (!_isPlaying)
            {
                DrawAmbient(g);
                if (!_isGameOver && _score == 0)
                {
                    DrawCentered(g, "ИНТЕРАКТИВНЫЙ ПОЛЕТНЫЙ СИМУЛЯТОР", new Font("Segoe UI", 16, FontStyle.Bold), CyberPalette.AccentNeon, Height / 2 - 100);
                    DrawCentered(g, "Управляйте разведывательным кибер-дроном, маневрируя между силовыми полями!\n\nИнструкции:\n• Нажимайте ПРОБЕЛ или кликайте левой кнопкой мыши для удержания высоты.\n• Каждое пройденное препятствие приносит 100 очков.", new Font("Segoe UI", 9.5f), CyberPalette.TextSecondary, Height / 2 - 50);
                }
                else
                {
                    DrawCentered(g, "СИМУЛЯЦИЯ ПРЕРВАНА", new Font("Segoe UI", 18, FontStyle.Bold), CyberPalette.AccentNeon, Height / 2 - 110);
                    DrawCentered(g, $"Ваш счет: {_score:N0} очков\nРекорд сессии: {_highScore:N0} очков", new Font("Segoe UI Semibold", 11, FontStyle.Bold), CyberPalette.TextPrimary, Height / 2 - 60);
                }
                return;
            }

            // Барьеры: заливка + неоновая кромка + внешнее свечение.
            foreach (var bar in _barriers)
            {
                var top = new RectangleF(bar.X, 0, BarrierWidth, bar.GapY - BarrierGap / 2);
                var btm = new RectangleF(bar.X, bar.GapY + BarrierGap / 2, BarrierWidth, Height - (bar.GapY + BarrierGap / 2));
                using (var bb = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentRedDeep, 40)))
                { g.FillRectangle(bb, top); g.FillRectangle(bb, btm); }
                // Свечение кромок.
                using (var glow = new Pen(CyberPalette.Alpha(CyberPalette.AccentNeon, 50), 5f))
                { g.DrawLine(glow, bar.X, top.Bottom, bar.X + BarrierWidth, top.Bottom); g.DrawLine(glow, bar.X, btm.Top, bar.X + BarrierWidth, btm.Top); }
                using (var edge = new Pen(CyberPalette.AccentRedDeep, 1.5f))
                { g.DrawRectangle(edge, top.X, top.Y, top.Width, top.Height); g.DrawRectangle(edge, btm.X, btm.Y, btm.Width, btm.Height); }
                using (var neon = new Pen(CyberPalette.AccentCyan, 2.5f))
                { g.DrawLine(neon, bar.X, top.Bottom, bar.X + BarrierWidth, top.Bottom); g.DrawLine(neon, bar.X, btm.Top, bar.X + BarrierWidth, btm.Top); }
            }

            // Трейл дрона (затухающие кольца).
            int idx = 0;
            foreach (var p in _trail)
            {
                float a = (idx++ / (float)_trail.Count) * 70f;
                float rr = DroneRadius * (0.3f + 0.5f * idx / _trail.Count);
                using var tb = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentCyan, (int)a));
                g.FillEllipse(tb, p.X - rr, p.Y - rr, rr * 2, rr * 2);
            }

            // Дрон.
            float rot = Math.Max(-35f, Math.Min(45f, _birdV * 4.5f));
            g.TranslateTransform(DroneX, _birdY);
            g.RotateTransform(rot);
            float pulse = 1f + 0.15f * (float)Math.Sin(DateTime.Now.Millisecond * 0.04f);
            float flame = DroneRadius * 1.6f * pulse;
            using (var plume = new GraphicsPath())
            {
                plume.AddPolygon(new[] { new PointF(-DroneRadius * 0.5f, -4f), new PointF(-DroneRadius * 0.5f - flame, 0), new PointF(-DroneRadius * 0.5f, 4f) });
                using var pb = new PathGradientBrush(plume) { CenterColor = CyberPalette.Alpha(CyberPalette.AccentNeon, 240), SurroundColors = new[] { Color.Transparent } };
                g.FillPath(pb, plume);
            }
            using (var wing = new Pen(CyberPalette.TextSecondary, 2f)) { g.DrawLine(wing, -2f, -2f, -5f, -16f); g.DrawLine(wing, -2f, 2f, -5f, 16f); }
            using (var rb = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentCyan, 90)))
            using (var rp = new Pen(CyberPalette.AccentCyan, 1.5f))
            { g.FillEllipse(rb, -9f, -20f, 8f, 8f); g.DrawEllipse(rp, -9f, -20f, 8f, 8f); g.FillEllipse(rb, -9f, 12f, 8f, 8f); g.DrawEllipse(rp, -9f, 12f, 8f, 8f); }
            using (var hb = new SolidBrush(Color.FromArgb(32, 32, 38)))
            using (var hp = new Pen(CyberPalette.TextPrimary, 1.8f))
            { var hull = new[] { new PointF(DroneRadius * 1.3f, 0), new PointF(0, -DroneRadius * 0.65f), new PointF(-DroneRadius * 0.7f, 0), new PointF(0, DroneRadius * 0.65f) }; g.FillPolygon(hb, hull); g.DrawPolygon(hp, hull); }
            using (var cb = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentCyan, 190)))
            { var canopy = new[] { new PointF(DroneRadius * 0.85f, 0), new PointF(0, -DroneRadius * 0.35f), new PointF(-DroneRadius * 0.25f, 0), new PointF(0, DroneRadius * 0.35f) }; g.FillPolygon(cb, canopy); }
            using (var lr = new Pen(CyberPalette.Alpha(Color.White, 220), 1f)) g.DrawLine(lr, 1f, -2f, DroneRadius * 0.4f, -1f);
            g.ResetTransform();

            TextRenderer.DrawText(g, $"ТЕКУЩИЙ СЧЕТ: {_score:N0}   |   РЕКОРД СЕССИИ: {_highScore:N0}",
                new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), new Point(20, 20), CyberPalette.TextPrimary);
        }

        private void DrawCentered(Graphics g, string text, Font font, Color color, int y)
        {
            using var brush = new SolidBrush(color);
            var size = g.MeasureString(text, font, Width - 80);
            g.DrawString(text, font, brush, new RectangleF((Width - size.Width) / 2, y, size.Width, size.Height),
                new StringFormat { Alignment = StringAlignment.Center });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Stop(); _timer?.Dispose();
                _idleTimer?.Stop(); _idleTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private class FlappyBarrier { public float X { get; set; } public float GapY { get; set; } public bool Passed { get; set; } }
    }
}