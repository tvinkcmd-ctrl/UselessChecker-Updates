using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UselessChecker
{
    // Аим-тренер (пасхалка по тройному клику в логотип). Геймплей нетронут; добавлены живой
    // ambient на старте/геймовере, свечение мишеней, виньетка и Dispose для игрового таймера
    // (в оригинале таймер не диспозился при CloseGame → утечка). Конструктор принимает Form1.
    public class CyberAimGamePanel : Panel
    {
        private readonly Form1 _parent;
        private readonly System.Windows.Forms.Timer _gameTimer;
        private readonly System.Windows.Forms.Timer _idleTimer;
        private readonly List<AimTarget> _targets = new List<AimTarget>();
        private readonly List<VisualEffect> _effects = new List<VisualEffect>();
        private readonly Random _rand = new Random();
        private int _score, _highScore, _hits, _shots, _lives = 3;
        private int _spawnTimer, _spawnInterval = 45;
        private bool _isStarted, _isGameOver;
        private CyberButton _closeBtn, _actionBtn;

        public CyberAimGamePanel(Form1 parent)
        {
            _parent = parent;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _gameTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTimer.Tick += GameLoop_Tick;
            _idleTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _idleTimer.Tick += (s, e) => { if (Visible && !_isStarted) Invalidate(); };

            InitializeUI();
        }

        private void InitializeUI()
        {
            _closeBtn = new CyberButton
            {
                Text = "✕",
                Size = new Size(40, 40),
                AccentColor = CyberPalette.AccentNeon,
                CustomBaseColor = CyberPalette.CardBg,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = CyberPalette.TextSecondary
            };
            _closeBtn.Click += (s, e) => { _gameTimer.Stop(); _idleTimer.Stop(); _parent.CloseGame(); };
            Controls.Add(_closeBtn);

            _actionBtn = new CyberButton
            {
                Text = "НАЧАТЬ ТРЕНИРОВКУ АИМА",
                Size = new Size(240, 45),
                AccentColor = CyberPalette.AccentEmerald,
                CustomBaseColor = CyberPalette.CardBg,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                ForeColor = Color.White
            };
            _actionBtn.Click += (s, e) => StartGame();
            Controls.Add(_actionBtn);
        }

        private void ManageIdle() { if (Visible && !_isStarted) _idleTimer.Start(); else _idleTimer.Stop(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); ManageIdle(); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_closeBtn != null) _closeBtn.Location = new Point(Width - 55, 15);
            if (_actionBtn != null) _actionBtn.Location = new Point((Width - _actionBtn.Width) / 2, Height / 2 + 120);
        }

        private void StartGame()
        {
            _score = 0; _hits = 0; _shots = 0; _lives = 3; _spawnInterval = 45;
            _targets.Clear(); _effects.Clear();
            _isStarted = true; _isGameOver = false; _spawnTimer = 0;
            _actionBtn.Visible = false;
            _gameTimer.Start();
            ManageIdle();
            Focus();
            Invalidate();
        }

        private void GameOver()
        {
            _gameTimer.Stop();
            _isGameOver = true;
            if (_score > _highScore) _highScore = _score;
            _actionBtn.Text = "ПОПРОБОВАТЬ СНОВА";
            _actionBtn.Visible = true;
            _actionBtn.BringToFront();
            ManageIdle();
            Invalidate();
        }

        private void GameLoop_Tick(object sender, EventArgs e)
        {
            if (++_spawnTimer >= _spawnInterval)
            {
                _spawnTimer = 0;
                if (_score > 1000 && _spawnInterval > 25) _spawnInterval--;
                _targets.Add(new AimTarget
                {
                    Position = new PointF(_rand.Next(60, Width - 60), _rand.Next(100, Height - 100)),
                    MaxRadius = _rand.Next(18, 28),
                    CurrentRadius = 0f,
                    GrowthRate = (float)(_rand.NextDouble() * 0.7 + 0.6),
                    IsShrinking = false,
                    Color = _rand.Next(0, 4) == 0 ? CyberPalette.AccentCyan : CyberPalette.AccentNeon
                });
            }
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var t = _targets[i];
                if (!t.IsShrinking) { t.CurrentRadius += t.GrowthRate; if (t.CurrentRadius >= t.MaxRadius) { t.CurrentRadius = t.MaxRadius; t.IsShrinking = true; } }
                else
                {
                    t.CurrentRadius -= t.GrowthRate;
                    if (t.CurrentRadius <= 0)
                    {
                        _targets.RemoveAt(i); _lives--; _shots++;
                        if (_lives <= 0) { GameOver(); return; }
                    }
                }
            }
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var eff = _effects[i];
                eff.Radius += eff.Speed; eff.Alpha -= 10;
                if (eff.Alpha <= 0) _effects.RemoveAt(i);
            }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_isStarted || _isGameOver || _closeBtn.Bounds.Contains(e.Location)) return;
            _shots++;
            bool hit = false;
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var t = _targets[i];
                float dx = e.X - t.Position.X, dy = e.Y - t.Position.Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= t.CurrentRadius + 6)
                {
                    _targets.RemoveAt(i); _hits++;
                    _score += (t.Color == CyberPalette.AccentCyan ? 200 : 100) + (_hits / 5) * 15;
                    _effects.Add(new VisualEffect { Position = t.Position, Radius = t.CurrentRadius, MaxRadius = t.CurrentRadius * 2.2f, Speed = 2f, Color = t.Color, Alpha = 220 });
                    hit = true; break;
                }
            }
            if (!hit)
            {
                _score = Math.Max(0, _score - 40);
                _effects.Add(new VisualEffect { Position = new PointF(e.X, e.Y), Radius = 1, MaxRadius = 12, Speed = 1.2f, Color = CyberPalette.AccentNeon, Alpha = 180 });
            }
            Invalidate();
        }

        private void DrawAmbient(Graphics g)
        {
            double t = DateTime.Now.TimeOfDay.TotalMilliseconds;
            float scanY = (float)((t / 12.0) % Height);
            using (var sb = new LinearGradientBrush(new Rectangle(0, (int)scanY - 24, Width, 48),
                       Color.Transparent, CyberPalette.Alpha(CyberPalette.AccentNeon, 22), 90f))
                g.FillRectangle(sb, 0, (int)scanY - 24, Width, 24);
            using (var sb2 = new LinearGradientBrush(new Rectangle(0, (int)scanY, Width, 48),
                       CyberPalette.Alpha(CyberPalette.AccentNeon, 22), Color.Transparent, 90f))
                g.FillRectangle(sb2, 0, (int)scanY, Width, 24);
            float pulse = (float)(0.5 + 0.5 * Math.Sin(t / 300.0));
            float cx = Width / 2f, cy = Height / 2f - 20;
            for (int i = 0; i < 3; i++)
            {
                float rad = 50 + i * 40 + pulse * 12;
                int a = 30 - i * 8; if (a <= 0) continue;
                using var pen = new Pen(CyberPalette.Alpha(CyberPalette.AccentCyan, a), 1f);
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

            using (var gridPen = new Pen(Color.FromArgb(20, 20, 24), 1))
            {
                for (int x = 0; x < Width; x += 40) g.DrawLine(gridPen, x, 0, x, Height);
                for (int y = 0; y < Height; y += 40) g.DrawLine(gridPen, 0, y, Width, y);
            }
            // Виньетка.
            using (var vb = new PathGradientBrush(new[] { new PointF(0, 0), new PointF(Width, 0), new PointF(Width, Height), new PointF(0, Height) }))
            { vb.CenterColor = Color.Transparent; vb.SurroundColors = new[] { CyberPalette.Alpha(Color.Black, 90) }; g.FillRectangle(vb, 0, 0, Width, Height); }
            using (var borderPen = new Pen(CyberPalette.BorderColor, 1f)) g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            if (!_isStarted)
            {
                DrawAmbient(g);
                DrawCentered(g, "ИНТЕРАКТИВНЫЙ ТРЕНИРОВЩИК АИМА", new Font("Segoe UI", 18, FontStyle.Bold), CyberPalette.AccentNeon, Height / 2 - 80);
                DrawCentered(g, "Разминайте рефлексы и точность наведения прямо в чекере перед каткой в CS 2!\n\nПравила:\nБыстро кликайте по возникающим мишеням, пока они не сжались в ноль.\nКаждый пропуск мишени или холостой выстрел отнимает единицу здоровья.", new Font("Segoe UI", 10), CyberPalette.TextSecondary, Height / 2 - 30);
                return;
            }
            if (_isGameOver)
            {
                DrawAmbient(g);
                DrawCentered(g, "ТРЕНИРОВКА ЗАВЕРШЕНА", new Font("Segoe UI", 20, FontStyle.Bold), CyberPalette.AccentNeon, Height / 2 - 110);
                float accuracy = _shots > 0 ? ((float)_hits / _shots) * 100 : 0;
                DrawCentered(g, $"Набранные очки: {_score}\nЛичный рекорд сессии: {_highScore}\nПопаданий: {_hits}\nВсего выстрелов: {_shots}\nТочность кликов: {accuracy:0.0}%", new Font("Segoe UI", 11), CyberPalette.TextPrimary, Height / 2 - 50);
                return;
            }

            foreach (var eff in _effects)
                using (var pen = new Pen(CyberPalette.Alpha(eff.Color, (int)eff.Alpha), 1.8f))
                    g.DrawEllipse(pen, eff.Position.X - eff.Radius, eff.Position.Y - eff.Radius, eff.Radius * 2, eff.Radius * 2);

            foreach (var t in _targets)
            {
                // Внешнее свечение мишени.
                using (var glow = new Pen(CyberPalette.Alpha(t.Color, 40), 6f))
                    g.DrawEllipse(glow, t.Position.X - t.CurrentRadius, t.Position.Y - t.CurrentRadius, t.CurrentRadius * 2, t.CurrentRadius * 2);
                using (var pen = new Pen(t.Color, 2f))
                    g.DrawEllipse(pen, t.Position.X - t.CurrentRadius, t.Position.Y - t.CurrentRadius, t.CurrentRadius * 2, t.CurrentRadius * 2);
                float core = t.CurrentRadius * 0.35f;
                if (core > 0.5f)
                    using (var b = new SolidBrush(CyberPalette.Alpha(t.Color, 140)))
                        g.FillEllipse(b, t.Position.X - core, t.Position.Y - core, core * 2, core * 2);
            }

            float acc = _shots > 0 ? ((float)_hits / _shots) * 100 : 0;
            TextRenderer.DrawText(g, $"ОЧКИ: {_score:N0}   |   РЕКОРД: {_highScore:N0}   |   ТОЧНОСТЬ: {acc:0}%",
                new Font("Segoe UI Semibold", 10f, FontStyle.Bold), new Point(25, 22), CyberPalette.TextPrimary);
            for (int l = 0; l < 3; l++)
            {
                var lr = new Rectangle(Width - 180 + l * 26, 23, 16, 16);
                using (var b = new SolidBrush(l < _lives ? CyberPalette.AccentNeon : Color.FromArgb(40, 40, 45))) g.FillRectangle(b, lr);
                using (var p = new Pen(CyberPalette.BorderColor, 1f)) g.DrawRectangle(p, lr);
            }
        }

        private void DrawCentered(Graphics g, string text, Font font, Color color, int y)
        {
            using var brush = new SolidBrush(color);
            var size = g.MeasureString(text, font, Width - 100);
            g.DrawString(text, font, brush, new RectangleF((Width - size.Width) / 2, y, size.Width, size.Height),
                new StringFormat { Alignment = StringAlignment.Center });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gameTimer?.Stop(); _gameTimer?.Dispose();
                _idleTimer?.Stop(); _idleTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private class AimTarget { public PointF Position { get; set; } public float MaxRadius { get; set; } public float CurrentRadius { get; set; } public float GrowthRate { get; set; } public bool IsShrinking { get; set; } public Color Color { get; set; } }
        private class VisualEffect { public PointF Position { get; set; } public float Radius { get; set; } public float MaxRadius { get; set; } public float Speed { get; set; } public Color Color { get; set; } public float Alpha { get; set; } }
    }
}