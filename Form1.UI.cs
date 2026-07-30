// Визуальная часть Form1 (partial). ФОН: богатый винный ambient с красными заревами (как в
// «мясной» версии) рисуется ОДИН РАЗ в кэш-Bitmap (RebuildBgCache), а окно и каждый контейнер
// берут из кэша свой срез одним DrawImage (DrawBgSlice). Поэтому: (1) фон живой и цветной,
// (2) при сворачивании нет ни белых дыр, ни красных X-швов — PathGradient живёт только в
// оффскрине, а не в промежуточном кадре, и прозрачных контролов, выпрашивающих фон, нет.
// Модалки/intro по-прежнему используют плоский PaintDeepBg (им кэш окна не нужен).
#pragma warning disable CS8618, CS8625, CS8601, CS8602, CS8603, CS8604, CS8600, CS8629
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UselessChecker
{
    public partial class Form1 : Form
    {
        // Тёмная подложка контейнеров (видна доли секунды до первого Paint и вне среза кэша).
        private static readonly Color BgTop = Color.FromArgb(17, 12, 19);
        private static readonly Color BgBot = Color.FromArgb(9, 9, 12);

        private bool _chatWasOpen;     // было ли окно чата открыто при сворачивании
        private Bitmap _bgCache;       // кэш богатого ambient-фона окна

        #region Win32: перетаскивание формы без рамки
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private void EnableFormDrag(Control control)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
        }
        #endregion

        #region Фон: кэш ambient + прорези для контейнеров
        // Мягкое радиальное зарево (только для оффскрин-кэша — в горячем пути не используется).
        private static void DrawBlob(Graphics g, float cx, float cy, float rx, float ry, Color core)
        {
            using var p = new GraphicsPath();
            p.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
            using var b = new PathGradientBrush(p)
            {
                CenterColor = core,
                SurroundColors = new[] { Color.Transparent },
                CenterPoint = new PointF(cx, cy)
            };
            g.FillPath(b, p);
        }

        // Богатый винный ambient (воспроизводит «мясной» фон): тёплый градиент + красное зарево
        // справа-сверху + бордо слева-снизу + лёгкое бордо справа-по-центру + виньетка.
        private static void DrawAmbientInto(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float w = r.Width, h = r.Height;
            using (var bg = new LinearGradientBrush(r, Color.FromArgb(22, 11, 16), Color.FromArgb(10, 8, 11), 135f))
                g.FillRectangle(bg, r);
            DrawBlob(g, w * 0.86f, h * 0.06f, w * 0.54f, h * 0.66f, CyberPalette.Alpha(CyberPalette.AccentNeon, 92));
            DrawBlob(g, w * 0.03f, h * 0.97f, w * 0.48f, h * 0.66f, CyberPalette.Alpha(CyberPalette.AccentRedDeep, 110));
            DrawBlob(g, w * 0.98f, h * 0.50f, w * 0.32f, h * 0.52f, CyberPalette.Alpha(CyberPalette.AccentRedDeep, 50));
            DrawBlob(g, w * 0.50f, h * 0.50f, w * 0.40f, h * 0.46f, CyberPalette.Alpha(Color.FromArgb(40, 16, 22), 34));
            using (var vig = new PathGradientBrush(new[] { new PointF(0, 0), new PointF(w, 0), new PointF(w, h), new PointF(0, h) }))
            {
                vig.CenterColor = Color.Transparent;
                vig.SurroundColors = new[] { CyberPalette.Alpha(Color.Black, 120) };
                g.FillRectangle(vig, r);
            }
        }

        // Перестройка кэша при ресайзе/первом показе.
        private void RebuildBgCache()
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            var old = _bgCache;
            _bgCache = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var g = Graphics.FromImage(_bgCache)) DrawAmbientInto(g, new Rectangle(0, 0, _bgCache.Width, _bgCache.Height));
            old?.Dispose();
        }

        // Контейнер рисует свой кусок общего кэша (прорезь) -> единое полотно без швов.
        private void DrawBgSlice(Graphics g, Control c)
        {
            if (_bgCache == null) { PaintDeepBg(g, c.ClientRectangle); return; }
            Point tl = this.PointToClient(c.PointToScreen(Point.Empty));
            var src = new Rectangle(tl.X, tl.Y, c.ClientRectangle.Width, c.ClientRectangle.Height);
            var bounds = new Rectangle(0, 0, _bgCache.Width, _bgCache.Height);
            var isect = Rectangle.Intersect(src, bounds);
            if (isect.IsEmpty) { PaintDeepBg(g, c.ClientRectangle); return; }
            var dest = new Rectangle(isect.X - src.X, isect.Y - src.Y, isect.Width, isect.Height);
            g.DrawImage(_bgCache, dest, isect, GraphicsUnit.Pixel);
        }

        private void PaintFlatBg(object s, PaintEventArgs e) => DrawBgSlice(e.Graphics, (Control)s);

        // Фон главного окна = кэш целиком.
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) { base.OnPaintBackground(e); return; }
            if (_bgCache == null || _bgCache.Width != ClientSize.Width || _bgCache.Height != ClientSize.Height) RebuildBgCache();
            if (_bgCache != null) e.Graphics.DrawImage(_bgCache, 0, 0);
            else base.OnPaintBackground(e);
        }

        // Плоский безопасный фон для модалок/intro (у них свой Form, кэш окна недоступен).
        private static void PaintDeepBg(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new LinearGradientBrush(r, BgTop, BgBot, 135f)) g.FillRectangle(bg, r);
            int e2 = Math.Max(40, (int)(Math.Min(r.Width, r.Height) * 0.22f));
            using (var t = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, e2), CyberPalette.Alpha(Color.Black, 70), Color.Transparent, 90f)) g.FillRectangle(t, r.X, r.Y, r.Width, e2);
            using (var b = new LinearGradientBrush(new Rectangle(r.X, r.Bottom - e2, r.Width, e2), Color.Transparent, CyberPalette.Alpha(Color.Black, 80), 90f)) g.FillRectangle(b, r.X, r.Bottom - e2, r.Width, e2);
            using (var l = new LinearGradientBrush(new Rectangle(r.X, r.Y, e2, r.Height), CyberPalette.Alpha(Color.Black, 60), Color.Transparent, 0f)) g.FillRectangle(l, r.X, r.Y, e2, r.Height);
            using (var rr = new LinearGradientBrush(new Rectangle(r.Right - e2, r.Y, e2, r.Height), Color.Transparent, CyberPalette.Alpha(Color.Black, 60), 0f)) g.FillRectangle(rr, r.Right - e2, r.Y, e2, r.Height);
        }

        // Сворачивание: прячем чат (страховка) + перестраиваем кэш под новый размер.
        private void OnFormResize(object s, EventArgs e)
        {
            RebuildBgCache();
            if (WindowState == FormWindowState.Minimized)
            {
                if (_chatOverlay != null) { _chatWasOpen = _chatOverlay.Visible; _chatOverlay.Visible = false; }
            }
            else if (WindowState == FormWindowState.Normal || WindowState == FormWindowState.Maximized)
            {
                if (_chatOverlay != null && _chatWasOpen) { _chatOverlay.Visible = true; _chatOverlay.BringToFront(); }
                Invalidate(true);
                Update();
            }
        }
        #endregion

        #region Вспомогательные фабрики
        private void SetControlDoubleBuffered(Control c) =>
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(c, true, null);

        private void AddCustomScrollbar(Panel parent, Control target, int x, int y, int width, int height)
        {
            var scrollbar = new CyberVScrollBar
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                ThumbColor = CyberPalette.AccentRedDeep,
                TrackColor = Color.FromArgb(20, 20, 24)
            };
            parent.Controls.Add(scrollbar);
            scrollbar.BindTo(target);
            scrollbar.BringToFront();
        }

        private FlowLayoutPanel CreateScrollableFlow(Panel parent, int x, int y, int width, int height, FlowDirection direction = FlowDirection.TopDown, bool wrapContents = false)
        {
            Panel wrapper = new Panel { Location = new Point(x, y), Size = new Size(width, height), BackColor = BgBot };
            SetControlDoubleBuffered(wrapper);
            wrapper.Paint += PaintFlatBg;
            parent.Controls.Add(wrapper);
            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Location = new Point(0, 0),
                Size = new Size(width + 25, height),
                FlowDirection = direction,
                WrapContents = wrapContents,
                AutoScroll = true,
                BackColor = BgBot
            };
            flow.HorizontalScroll.Maximum = 0;
            flow.HorizontalScroll.Visible = false;
            SetControlDoubleBuffered(flow);
            flow.Paint += PaintFlatBg;
            wrapper.Controls.Add(flow);
            AddCustomScrollbar(parent, flow, x + width + 4, y, 8, height);
            return flow;
        }
        #endregion

        #region Кастомное модальное сообщение (скруглённые углы через прозрачный ключ)
        private async Task ShowCustomMessageBoxAsync(string text, string title = "Уведомление системы", string icon = "Info")
        {
            var msgForm = new Form
            {
                Text = title,
                Size = new Size(500, 260),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(1, 2, 3),
                TransparencyKey = Color.FromArgb(1, 2, 3),
                FormBorderStyle = FormBorderStyle.None,
                TopMost = true,
                ShowInTaskbar = false
            };
            SetControlDoubleBuffered(msgForm);
            var panel = new Panel { Size = new Size(500, 260), BackColor = Color.Transparent, Dock = DockStyle.Fill };
            msgForm.Controls.Add(panel);

            Color accentColor = CyberPalette.AccentNeon;
            string indicatorText = "СИСТЕМНОЕ УВЕДОМЛЕНИЕ";
            if (icon == "Error") { accentColor = CyberPalette.AccentRedDeep; indicatorText = "ОШИБКА ВЫПОЛНЕНИЯ"; }
            else if (icon == "Warning") { accentColor = CyberPalette.AccentRedMuted; indicatorText = "ВНИМАНИЕ"; }
            else if (icon == "Success") { accentColor = CyberPalette.AccentEmerald; indicatorText = "УСПЕШНО"; }

            panel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(1, 2, 3));
                using var path = CyberPalette.Round(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), 14);
                g.SetClip(path);
                PaintDeepBg(g, panel.ClientRectangle);
                g.ResetClip();
                using (var border = new Pen(CyberPalette.Alpha(accentColor, 150), 1.2f)) g.DrawPath(border, path);
                using (var hl = new LinearGradientBrush(new Rectangle(0, 0, panel.Width, 2), accentColor, Color.Transparent, 90f))
                    g.FillRectangle(hl, 0, 0, panel.Width, 2);
            };
            panel.Controls.Add(new Label { Text = indicatorText, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = accentColor, Size = new Size(450, 30), Location = new Point(25, 20), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
            panel.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Size = new Size(450, 30), Location = new Point(25, 50), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
            panel.Controls.Add(new Label { Text = text, Font = new Font("Segoe UI", 9.5f), ForeColor = CyberPalette.TextSecondary, Size = new Size(450, 110), Location = new Point(25, 85), TextAlign = ContentAlignment.TopLeft, BackColor = Color.Transparent });

            var btn = new CyberButton { Text = "ОК", Size = new Size(140, 35), Location = new Point(335, 205), AccentColor = accentColor, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary };
            btn.Click += (s, e) => { msgForm.Close(); msgForm.Dispose(); };
            panel.Controls.Add(btn);
            msgForm.AcceptButton = btn;
            msgForm.Show();
            for (int i = 0; i <= 100; i += 10) { msgForm.Opacity = i / 100.0; await Task.Delay(8); }
            while (msgForm.Visible) await Task.Delay(50);
        }
        #endregion

        #region Экран первоначальной загрузки (скруглённый)
        private async Task ShowIntroScreenAsync()
        {
            var introForm = new Form
            {
                Text = "Инициализация...",
                Size = new Size(600, 380),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(1, 2, 3),
                TransparencyKey = Color.FromArgb(1, 2, 3),
                FormBorderStyle = FormBorderStyle.None,
                TopMost = true,
                ShowInTaskbar = false
            };
            SetControlDoubleBuffered(introForm);
            var panel = new Panel { Size = new Size(600, 380), BackColor = Color.Transparent, Dock = DockStyle.Fill };
            introForm.Controls.Add(panel);
            panel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(1, 2, 3));
                using var path = CyberPalette.Round(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), 16);
                g.SetClip(path);
                PaintDeepBg(g, panel.ClientRectangle);
                g.ResetClip();
                using (var border = new Pen(CyberPalette.Alpha(Color.White, 26), 1f)) g.DrawPath(border, path);
                using (var hl = new LinearGradientBrush(new Rectangle(0, 0, panel.Width, 2), CyberPalette.AccentNeon, Color.Transparent, 0f))
                    g.FillRectangle(hl, 0, 0, panel.Width, 2);
            };
            panel.Controls.Add(new Label { Text = "Useless Checker", Font = new Font("Segoe UI Semibold", 24, FontStyle.Bold), ForeColor = CyberPalette.AccentNeon, Size = new Size(500, 50), Location = new Point(50, 90), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
            panel.Controls.Add(new Label { Text = "Система диагностики и анализа процессов | Версия 3.0.7", Font = new Font("Segoe UI", 9, FontStyle.Regular), ForeColor = CyberPalette.TextSecondary, Size = new Size(500, 25), Location = new Point(50, 142), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
            var hexLabel = new Label { Text = "Инициализация модулей...", Font = new Font("Segoe UI", 9, FontStyle.Regular), ForeColor = CyberPalette.AccentCyan, Size = new Size(500, 25), Location = new Point(50, 185), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            panel.Controls.Add(hexLabel);
            var statusLabel = new Label { Text = "Связь с сервером...", Font = new Font("Segoe UI", 10), ForeColor = CyberPalette.TextPrimary, Size = new Size(500, 30), Location = new Point(50, 230), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            panel.Controls.Add(statusLabel);
            var loadBar = new CyberProgressBar { Size = new Size(400, 8), Location = new Point(100, 280), AccentColor = CyberPalette.AccentNeon, Value = 0 };
            panel.Controls.Add(loadBar);

            introForm.Show();
            for (int i = 0; i <= 100; i += 10) { introForm.Opacity = i / 100.0; await Task.Delay(8); }

            string[] statuses = { "Подключение модулей...", "Тестирование адресных пространств...", "Проверка активных отладчиков...", "Сканирование файловых баз...", "Готовность системного ядра..." };
            for (int k = 0; k < statuses.Length; k++)
            {
                statusLabel.Text = statuses[k];
                hexLabel.Text = $"Загрузка компонентов ({k * 20}%)";
                int stepLimit = (k + 1) * 20;
                while (loadBar.Value < stepLimit) { loadBar.Value += 2; await Task.Delay(15); }
            }
            statusLabel.Text = "Компоненты успешно загружены";
            statusLabel.ForeColor = CyberPalette.AccentEmerald;
            hexLabel.Text = "Готов к работе";
            hexLabel.ForeColor = CyberPalette.AccentEmerald;
            await Task.Delay(400);
            for (int i = 100; i >= 0; i -= 10) { introForm.Opacity = i / 100.0; await Task.Delay(8); }
            introForm.Close();
            introForm.Dispose();
            for (int i = 0; i <= 100; i += 10) { Opacity = i / 100.0; await Task.Delay(8); }
        }
        #endregion

        #region Сборка основного UI
        private void InitializeDesignUI()
        {
            Text = "Useless Checker v3.0.7";
            Size = new Size(1100, 750);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgBot;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
            SetControlDoubleBuffered(this);
            this.Resize += OnFormResize;
            RebuildBgCache();

            var titleBar = new Panel { Size = new Size(1100, 45), BackColor = BgTop, Dock = DockStyle.Top };
            SetControlDoubleBuffered(titleBar);
            titleBar.Paint += PaintFlatBg;
            var titleTextLabel = new Label { Text = "Useless Checker | Forensic Diagnostic Utility", Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), ForeColor = CyberPalette.TextSecondary, Size = new Size(500, 45), Location = new Point(20, 0), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            titleBar.Controls.Add(titleTextLabel);
            EnableFormDrag(titleBar);
            EnableFormDrag(titleTextLabel);

            var minBtn = new Button { Text = "—", Size = new Size(50, 45), Location = new Point(1000, 0), BackColor = Color.Transparent, ForeColor = CyberPalette.TextSecondary, Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
            minBtn.FlatAppearance.BorderSize = 0; minBtn.Cursor = Cursors.Hand;
            minBtn.Click += (s, e) => WindowState = FormWindowState.Minimized;
            minBtn.MouseEnter += (s, e) => { minBtn.BackColor = CyberPalette.Alpha(Color.White, 14); minBtn.ForeColor = CyberPalette.TextPrimary; };
            minBtn.MouseLeave += (s, e) => { minBtn.BackColor = Color.Transparent; minBtn.ForeColor = CyberPalette.TextSecondary; };
            titleBar.Controls.Add(minBtn);

            var closeBtn = new Button { Text = "✕", Size = new Size(50, 45), Location = new Point(1050, 0), BackColor = Color.Transparent, ForeColor = CyberPalette.TextSecondary, Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
            closeBtn.FlatAppearance.BorderSize = 0; closeBtn.Cursor = Cursors.Hand;
            closeBtn.Click += (s, e) => Close();
            closeBtn.MouseEnter += (s, e) => { closeBtn.BackColor = CyberPalette.AccentRedDeep; closeBtn.ForeColor = Color.White; };
            closeBtn.MouseLeave += (s, e) => { closeBtn.BackColor = Color.Transparent; closeBtn.ForeColor = CyberPalette.TextSecondary; };
            titleBar.Controls.Add(closeBtn);
            Controls.Add(titleBar);

            var header = new Panel { Size = new Size(1100, 110), BackColor = BgTop, Location = new Point(0, 45) };
            SetControlDoubleBuffered(header);
            header.Paint += (s, e) =>
            {
                DrawBgSlice(e.Graphics, header);
                using var sep = new LinearGradientBrush(new Rectangle(0, header.Height - 2, header.Width, 2), CyberPalette.AccentNeon, Color.Transparent, 0f);
                e.Graphics.FillRectangle(sep, 0, header.Height - 2, header.Width, 2);
            };
            var headerTitle = new Label { Text = "Useless Checker", Font = new Font("Segoe UI", 26, FontStyle.Bold), ForeColor = CyberPalette.AccentNeon, Size = new Size(520, 44), Location = Point.Empty, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            headerTitle.Click += (s, e) =>
            {
                _logoClickCount++;
                if (_logoClickCount >= 3) { _logoClickCount = 0; LaunchEasterEggGame(); }
            };
            header.Controls.Add(headerTitle);
            header.Controls.Add(new Label { Text = "ИНСТРУМЕНТ КОМПЛЕКСНОЙ ДИАГНОСТИКИ ФАЙЛОВОЙ СИСТЕМЫ И АКТИВНЫХ ПРОЦЕССОВ", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = CyberPalette.TextSecondary, Size = new Size(800, 20), Location = new Point(28, 70), BackColor = Color.Transparent });
            EnableFormDrag(header);
            Controls.Add(header);

            var sidebar = new FlowLayoutPanel { Size = new Size(250, 595), Location = new Point(0, 155), BackColor = BgTop, FlowDirection = FlowDirection.TopDown, Padding = new Padding(15, 12, 15, 0) };
            SetControlDoubleBuffered(sidebar);
            sidebar.Paint += (s, e) =>
            {
                DrawBgSlice(e.Graphics, sidebar);
                using var pen = new Pen(CyberPalette.Alpha(Color.White, 16), 1f);
                e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
                using var glow = new LinearGradientBrush(new Rectangle(sidebar.Width - 8, 0, 8, sidebar.Height), Color.Transparent, CyberPalette.Alpha(CyberPalette.AccentNeon, 24), 0f);
                e.Graphics.FillRectangle(glow, sidebar.Width - 8, 0, 8, sidebar.Height);
            };

            _isFlappyTabVisible = (new Random().Next(1, 101) <= 15);
            var tabList = new List<(string Text, string Name, Action<int> Action)>
            {
                ("Программы", "programs", idx => SwitchTab("programs", idx)),
                ("Папки", "folders", idx => SwitchTab("folders", idx)),
                ("Дополнительные функции", "additional", idx => SwitchTab("additional", idx)),
                ("Steam сессии", "steam", idx => SwitchTab("steam", idx)),
                ("Гайд по проверке", "guide", idx => SwitchTab("guide", idx)),
                ("Данные ПК", "pcinfo", idx => SwitchTab("pcinfo", idx))
            };
            if (_isFlappyTabVisible) tabList.Add(("Мини-игра", "flappy", idx => SwitchTab("flappy", idx)));
            tabList.Add(("Контакты", "contacts", idx => SwitchTab("contacts", idx)));

            for (int i = 0; i < tabList.Count; i++)
            {
                var tab = tabList[i];
                int currentIndex = i;
                var btn = new CyberButton
                {
                    Text = tab.Text,
                    Size = new Size(220, 40),
                    CustomBaseColor = CyberPalette.CardBg,
                    Active = currentIndex == 0,
                    ForeColor = currentIndex == 0 ? CyberPalette.TextPrimary : CyberPalette.TextSecondary,
                    Font = new Font("Segoe UI Semibold", 9.5f, currentIndex == 0 ? FontStyle.Bold : FontStyle.Regular),
                    AccentColor = CyberPalette.AccentNeon,
                    Margin = new Padding(0, 0, 0, 8)
                };
                btn.Click += (s, e) => tab.Action(currentIndex);
                sidebar.Controls.Add(btn);
                _sidebarButtons.Add(btn);
            }
            Controls.Add(sidebar);

            _mainContent = new Panel { Size = new Size(850, 595), Location = new Point(250, 155), BackColor = BgBot };
            SetControlDoubleBuffered(_mainContent);
            _mainContent.Paint += PaintFlatBg;
            _viewport = new Panel { Size = new Size(810, 555), Location = new Point(20, 20), BackColor = BgBot };
            SetControlDoubleBuffered(_viewport);
            _viewport.Paint += PaintFlatBg;
            _mainContent.Controls.Add(_viewport);
            Controls.Add(_mainContent);

            _programsPanel = CreateSubPanel("Программы");
            _foldersPanel = CreateSubPanel("Папки");
            _additionalPanel = CreateSubPanel("Дополнительные функции");
            _steamPanel = CreateSubPanel("Steam сессии");
            _guidePanel = CreateSubPanel("Гайд-Проверка");
            _pcInfoPanel = CreateSubPanel("Данные ПК");
            if (_isFlappyTabVisible) _flappyPanel = CreateSubPanel("Мини-игра");
            _contactsPanel = CreateSubPanel("Контакты");

            PopulateProgramsTab();
            PopulateFoldersTab();
            PopulateAdditionalTab();
            PopulateSteamTab();
            PopulateGuideTab();
            if (_isFlappyTabVisible) PopulateFlappyTab();
            PopulateContactsTab();
            SwitchTab("programs", 0);
        }

        private Panel CreateSubPanel(string title)
        {
            var panel = new Panel { Size = new Size(810, 555), Location = new Point(0, 0), BackColor = BgBot, Visible = false };
            SetControlDoubleBuffered(panel);
            panel.Paint += PaintFlatBg;
            panel.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, AutoSize = true, Location = new Point(5, 5), BackColor = Color.Transparent });
            _viewport?.Controls.Add(panel);
            return panel;
        }

        private async void SwitchTab(string name, int index)
        {
            if (_isTransitioning) return;
            Panel targetPanel = name switch
            {
                "programs" => _programsPanel,
                "folders" => _foldersPanel,
                "additional" => _additionalPanel,
                "steam" => _steamPanel,
                "guide" => _guidePanel,
                "pcinfo" => _pcInfoPanel,
                "flappy" => _flappyPanel,
                "contacts" => _contactsPanel,
                _ => null
            };
            if (targetPanel == null) return;

            for (int i = 0; i < _sidebarButtons.Count; i++)
            {
                _sidebarButtons[i].Active = i == index;
                _sidebarButtons[i].ForeColor = i == index ? CyberPalette.TextPrimary : CyberPalette.TextSecondary;
                _sidebarButtons[i].Font = new Font("Segoe UI", 10, i == index ? FontStyle.Bold : FontStyle.Regular);
            }

            if (name == "pcinfo") RenderPCInfoUI();
            if (name == "additional") Task.Run(async () => await UpdateRegistryStatusesAsync());
            if (_isFlappyTabVisible) { if (name == "flappy") _flappyGameControl?.OnTabShown(); else _flappyGameControl?.OnTabHidden(); }

            if (_activePanel == null)
            {
                _activePanel = targetPanel;
                _activePanel.Location = new Point(0, 0);
                _activePanel.Visible = true;
                _currentTab = index;
            }
            else if (_activePanel != targetPanel)
            {
                _isTransitioning = true;
                _activePanel.Visible = false;
                bool slideDown = index > _currentTab;
                int span = 555;
                targetPanel.Location = new Point(0, slideDown ? span : -span);
                targetPanel.Visible = true;
                targetPanel.BringToFront();
                const int duration = 220;
                var startTime = DateTime.UtcNow;
                while (true)
                {
                    double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    float t = (float)(elapsed / duration);
                    if (t >= 1f) break;
                    float ease = 1f - (float)Math.Pow(1f - t, 3);
                    int newY = (int)((slideDown ? span : -span) * (1f - ease));
                    if (!targetPanel.IsDisposed) targetPanel.Location = new Point(0, newY);
                    await Task.Delay(12);
                }
                if (!targetPanel.IsDisposed) targetPanel.Location = new Point(0, 0);
                _activePanel = targetPanel;
                _currentTab = index;
                _isTransitioning = false;
            }

            _chatOverlay?.BringToFront();
            _chatToggleBtn?.BringToFront();
        }
        #endregion

        #region ИИ-ассистент и пасхалка
        private void InitializeChatOverlay()
        {
            try
            {
                _chatOverlay = new CyberChatOverlay { Location = new Point(this.Width - 395, 45), Size = new Size(380, 530), Visible = false, BackColor = BgBot };
                this.Controls.Add(_chatOverlay);
                _chatOverlay.BringToFront();
                _chatToggleBtn = new CyberChatToggleButton { Location = new Point(945, 0), Size = new Size(50, 45) };
                _chatToggleBtn.Click += (s, e) =>
                {
                    _chatOverlay.Visible = !_chatOverlay.Visible;
                    if (_chatOverlay.Visible) { _chatOverlay.BringToFront(); _chatOverlay.FocusInput(); }
                };
                foreach (Control ctrl in this.Controls)
                {
                    if (ctrl.Height == 45 && ctrl.Dock == DockStyle.Top) { ctrl.Controls.Add(_chatToggleBtn); _chatToggleBtn.BringToFront(); break; }
                }
            }
            catch (Exception ex) { Logger.Error("InitializeChatOverlay", ex); }
        }

        private void LaunchEasterEggGame()
        {
            if (_gamePanel != null) return;
            _gamePanel = new CyberAimGamePanel(this) { Location = new Point(0, 45), Size = new Size(1100, 705), BackColor = BgBot };
            this.Controls.Add(_gamePanel);
            _gamePanel.BringToFront();
            _gamePanel.Focus();
        }

        public void CloseGame()
        {
            if (_gamePanel != null) { this.Controls.Remove(_gamePanel); _gamePanel.Dispose(); _gamePanel = null; }
        }
        #endregion

        #region Раздел: Программы
        private void PopulateProgramsTab()
        {
            if (_programsPanel == null) return;
            var flow = CreateScrollableFlow(_programsPanel, 5, 55, 790, 480, FlowDirection.LeftToRight, true);
            var programs = new DiagnosticToolInfo[]
            {
                new DiagnosticToolInfo { Number = "01", Name = "LastActivityView", Description = "Отслеживание истории запущенных процессов", LocalPath = Path.Combine(ToolsPath, "LastActivityView.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/LastActivityView.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/LastActivityView.exe", IsZip = false },
                new DiagnosticToolInfo { Number = "02", Name = "Process Hacker", Description = "Инструмент инспектирования памяти и процессов", LocalPath = Path.Combine(ToolsPath, "ProcessHacker.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/ProcessHacker.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/ProcessHacker.exe", IsZip = false },
                new DiagnosticToolInfo { Number = "03", Name = "ShellBag Analyzer", Description = "Парсер следов открытия папок в системе", LocalPath = Path.Combine(ToolsPath, "shellbag_analyzer_cleaner.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/shellbag_analyzer_cleaner.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/shellbag_analyzer_cleaner.exe", IsZip = false },
                new DiagnosticToolInfo { Number = "04", Name = "Everything", Description = "Портативная версия Everything (быстрый поиск файлов)", LocalPath = Path.Combine(ToolsPath, "Everything.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/everything.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/everything.exe", IsZip = false },
                new DiagnosticToolInfo { Number = "05", Name = "ExecutedProgramsList", Description = "Лог выполнения программ и обращений", LocalPath = Path.Combine(ToolsPath, "ExecutedProgramsList.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/ExecutedProgramsList.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/ExecutedProgramsList.exe", IsZip = false },
                new DiagnosticToolInfo { Number = "06", Name = "BrowserDownloadsView", Description = "Инспектор сессий загрузки в веб-обозревателях", LocalPath = Path.Combine(ToolsPath, "BrowserDownloadsView.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/BrowserDownloadsView.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/BrowserDownloadsView.exe", IsZip = false },
                new DiagnosticToolInfo { Number = "07", Name = "JournalTrace", Description = "Парсинг системного журнала изменений NTFS USN", LocalPath = Path.Combine(ToolsPath, "JournalTrace.exe"), DownloadUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/JournalTrace.exe", FallbackUrl = "https://github.com/tvinkcmd-ctrl/UselessChecker-Updates/releases/download/www/JournalTrace.exe", IsZip = false }
            };

            foreach (var p in programs)
            {
                var card = new CyberPanel { Size = new Size(375, 104), Margin = new Padding(0, 0, 10, 15), CornerRadius = 16 };
                card.Controls.Add(new Label { Text = p.Number, Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = CyberPalette.AccentRedMuted, Size = new Size(42, 38), Location = new Point(15, 15), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
                card.Controls.Add(new Label { Text = p.Name, Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Size = new Size(210, 25), Location = new Point(60, 16), BackColor = Color.Transparent });
                card.Controls.Add(new Label { Text = p.Description, Font = new Font("Segoe UI", 9), ForeColor = CyberPalette.TextSecondary, Size = new Size(210, 45), Location = new Point(60, 42), BackColor = Color.Transparent });
                var btn = new CyberButton { Text = "Запустить", Size = new Size(90, 32), Location = new Point(272, 36), AccentColor = CyberPalette.AccentNeon, Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Tag = p };
                btn.Click += async (s, e) =>
                {
                    var triggerBtn = (CyberButton)s;
                    var tool = (DiagnosticToolInfo)triggerBtn.Tag;
                    triggerBtn.Enabled = false;
                    triggerBtn.Text = "Загрузка...";
                    try
                    {
                        if (!File.Exists(tool.LocalPath))
                        {
                            bool success = await DownloadAndExtractToolAsync(tool.DownloadUrl, tool.FallbackUrl, tool.LocalPath, tool.IsZip);
                            if (!success) { _ = ShowCustomMessageBoxAsync($"Не удалось скачать компонент {tool.Name}.\nПроверьте соединение с Интернетом или брандмауэр Windows.", "Ошибка сети", "Error"); return; }
                        }
                        Process.Start(new ProcessStartInfo(tool.LocalPath) { UseShellExecute = true });
                    }
                    catch (Exception ex) { _ = ShowCustomMessageBoxAsync($"Не удалось выполнить запуск:\n{ex.Message}", "Ошибка запуска", "Error"); }
                    finally { triggerBtn.Enabled = true; triggerBtn.Text = "Запустить"; }
                };
                card.Controls.Add(btn);
                flow.Controls.Add(card);
            }
        }
        #endregion

        #region Раздел: Папки и встроенный сканер
        private void PopulateFoldersTab()
        {
            if (_foldersPanel == null) return;
            var leftWrapper = new Panel { Location = new Point(5, 55), Size = new Size(260, 480), BackColor = Color.Transparent };
            _foldersPanel.Controls.Add(leftWrapper);
            var folderFlow = new FlowLayoutPanel { Location = new Point(0, 0), Size = new Size(245, 480), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
            folderFlow.HorizontalScroll.Maximum = 0;
            folderFlow.HorizontalScroll.Visible = false;
            leftWrapper.Controls.Add(folderFlow);
            AddCustomScrollbar(_foldersPanel, folderFlow, 250, 55, 8, 480);

            var folders = new (string Name, string Path)[]
            {
                ("Recent Files", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent")),
                ("Prefetch", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch")),
                ("Crash Dumps", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps")),
                ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
                ("Program Data", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
                ("Startup Config", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup")),
                ("Temp Storage", Path.GetTempPath())
            };
            foreach (var f in folders)
            {
                var btn = new CyberButton { Text = f.Name, Size = new Size(215, 42), CustomBaseColor = CyberPalette.CardBg, ForeColor = CyberPalette.TextSecondary, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), AccentColor = CyberPalette.AccentCyan, Tag = f.Path, Margin = new Padding(0, 0, 0, 10) };
                btn.Click += (s, e) =>
                {
                    string p = ((CyberButton)s).Tag.ToString();
                    if (Directory.Exists(p)) Process.Start("explorer.exe", p);
                    else _ = ShowCustomMessageBoxAsync("Указанный каталог не найден в файловой системе.", "Ошибка пути", "Error");
                };
                folderFlow.Controls.Add(btn);
            }
            var openAllBtn = new CyberButton { Text = "Открыть все папки", Size = new Size(215, 42), CustomBaseColor = CyberPalette.CardBg, ForeColor = CyberPalette.TextPrimary, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), AccentColor = CyberPalette.AccentEmerald, Margin = new Padding(0, 0, 0, 10) };
            openAllBtn.Click += (s, e) => { foreach (var f in folders) if (Directory.Exists(f.Path)) Process.Start("explorer.exe", f.Path); };
            folderFlow.Controls.Add(openAllBtn);

            var scanFrame = new CyberPanel { Location = new Point(275, 55), Size = new Size(520, 480), BorderColor = CyberPalette.BorderColor, FillColor = CyberPalette.PanelBg, CornerRadius = 20 };
            SetControlDoubleBuffered(scanFrame);
            _foldersPanel.Controls.Add(scanFrame);

            _pnlScanIdle = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = true };
            SetControlDoubleBuffered(_pnlScanIdle);
            scanFrame.Controls.Add(_pnlScanIdle);
            _pnlScanIdle.Controls.Add(new Label { Text = "СИСТЕМА АНАЛИЗА УГРОЗ", Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Location = new Point(30, 90), Size = new Size(460, 35), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
            var startBtn = new CyberButton { Text = "Запустить сканирование системы", Size = new Size(460, 58), Location = new Point(30, 160), CustomBaseColor = Color.FromArgb(43, 14, 20), ForeColor = Color.White, Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), AccentColor = CyberPalette.AccentNeon };
            startBtn.Click += async (s, e) => await StartEmbedCheatScanAsync();
            _pnlScanIdle.Controls.Add(startBtn);
            _pnlScanIdle.Controls.Add(new Label { Text = "Инструмент проводит глубокий сигнатурный анализ локальных директорий, кэша выполнения, реестра и временных файлов.\n\nВы можете свободно переключаться на другие вкладки программы и пользоваться всем функционалом во время выполнения сканирования в фоновом режиме.", Font = new Font("Segoe UI", 9.5f), ForeColor = CyberPalette.TextSecondary, Location = new Point(30, 244), Size = new Size(460, 150), TextAlign = ContentAlignment.TopCenter, BackColor = Color.Transparent });

            _pnlScanRunning = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            SetControlDoubleBuffered(_pnlScanRunning);
            scanFrame.Controls.Add(_pnlScanRunning);
            _pnlScanRunning.Controls.Add(new Label { Text = "Криминалистический поиск угроз...", Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), ForeColor = CyberPalette.AccentNeon, Location = new Point(30, 25), Size = new Size(460, 25), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
            _embedProgressBar = new CyberProgressBar { AccentColor = CyberPalette.AccentNeon, Location = new Point(30, 65), Size = new Size(460, 15) };
            _pnlScanRunning.Controls.Add(_embedProgressBar);
            _embedScanPercent = new Label { Text = "0%", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = CyberPalette.AccentCyan, Location = new Point(30, 95), Size = new Size(80, 25), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _pnlScanRunning.Controls.Add(_embedScanPercent);
            _embedScanStatus = new Label { Text = "Инициализация...", Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Location = new Point(30, 130), Size = new Size(460, 22), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _pnlScanRunning.Controls.Add(_embedScanStatus);
            _embedScanPath = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = CyberPalette.TextSecondary, Location = new Point(30, 155), Size = new Size(460, 35), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, AutoEllipsis = true };
            _pnlScanRunning.Controls.Add(_embedScanPath);
            _embedFilesCount = new Label { Text = "Файлов: 0", Font = new Font("Segoe UI", 9.5f), ForeColor = CyberPalette.TextSecondary, Location = new Point(30, 205), Size = new Size(460, 20), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _pnlScanRunning.Controls.Add(_embedFilesCount);
            _embedCheatsCount = new Label { Text = "Угрозы: 0", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = CyberPalette.AccentNeon, Location = new Point(30, 230), Size = new Size(460, 20), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _pnlScanRunning.Controls.Add(_embedCheatsCount);
            _embedPathsCount = new Label { Text = "Маршруты: 0/0", Font = new Font("Segoe UI", 9.5f), ForeColor = CyberPalette.TextSecondary, Location = new Point(30, 255), Size = new Size(460, 20), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _pnlScanRunning.Controls.Add(_embedPathsCount);
            var cancelBtn = new CyberButton { Text = "Прервать сканирование", Size = new Size(200, 38), Location = new Point(30, 305), AccentColor = CyberPalette.AccentNeon, Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary };
            cancelBtn.Click += (s, e) => { _scanCts?.Cancel(); cancelBtn.Enabled = false; cancelBtn.Text = "Прерывание..."; };
            _pnlScanRunning.Controls.Add(cancelBtn);

            _pnlScanResults = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            SetControlDoubleBuffered(_pnlScanResults);
            scanFrame.Controls.Add(_pnlScanResults);
            _embedResultsHeader = new Label { Text = "Результаты сканирования", Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), ForeColor = CyberPalette.AccentNeon, Location = new Point(20, 15), Size = new Size(480, 25), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _pnlScanResults.Controls.Add(_embedResultsHeader);
            var listWrapper = new Panel { Location = new Point(20, 50), Size = new Size(460, 270), BackColor = CyberPalette.Alpha(CyberPalette.Background, 150) };
            listWrapper.Paint += (s, e) => { using var pen = new Pen(CyberPalette.Alpha(Color.White, 16), 1f); e.Graphics.DrawRectangle(pen, 0, 0, listWrapper.Width - 1, listWrapper.Height - 1); };
            _pnlScanResults.Controls.Add(listWrapper);
            _embedResultsListBox = new ListBox { Location = new Point(0, 0), Size = new Size(485, 270), BackColor = CyberPalette.CardBg, ForeColor = CyberPalette.TextPrimary, Font = new Font("Segoe UI", 9), BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24 };
            _embedResultsListBox.DrawItem += (sender, a) =>
            {
                if (a.Index < 0) return;
                a.DrawBackground();
                bool isSelected = (a.State & DrawItemState.Selected) == DrawItemState.Selected;
                Color bg = isSelected ? CyberPalette.CardHover : CyberPalette.CardBg;
                Color fg = isSelected ? CyberPalette.AccentNeon : CyberPalette.TextPrimary;
                using (var brush = new SolidBrush(bg)) a.Graphics.FillRectangle(brush, a.Bounds);
                string text = _embedResultsListBox.Items[a.Index].ToString();
                TextRenderer.DrawText(a.Graphics, text, a.Font, a.Bounds, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using var pen = new Pen(CyberPalette.BorderColor, 1);
                a.Graphics.DrawLine(pen, a.Bounds.Left, a.Bounds.Bottom - 1, a.Bounds.Right, a.Bounds.Bottom - 1);
            };
            listWrapper.Controls.Add(_embedResultsListBox);
            _embedResultsScrollbar = new CyberVScrollBar { Location = new Point(485, 50), Size = new Size(8, 270), ThumbColor = CyberPalette.AccentRedDeep, TrackColor = Color.FromArgb(20, 20, 24) };
            _pnlScanResults.Controls.Add(_embedResultsScrollbar);
            _embedResultsScrollbar.BindTo(_embedResultsListBox);
            var embedLocateBtn = new CyberButton { Text = "Перейти к файлу", Size = new Size(160, 35), Location = new Point(20, 335), AccentColor = CyberPalette.AccentCyan, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary };
            embedLocateBtn.Click += (s, e) =>
            {
                if (_embedResultsListBox.SelectedItem != null)
                {
                    string rawItem = _embedResultsListBox.SelectedItem.ToString();
                    int index = rawItem.IndexOf(": ");
                    if (index != -1)
                    {
                        string cleanPath = rawItem.Substring(index + 2).Trim();
                        if (File.Exists(cleanPath)) try { Process.Start("explorer.exe", $"/select,\"{cleanPath}\""); } catch (Exception ex) { Logger.Error("LocateFileExplorerEmbed", ex); }
                    }
                }
                else _ = ShowCustomMessageBoxAsync("Пожалуйста, выберите файл из списка для локализации.", "Элемент не выбран", "Warning");
            };
            _pnlScanResults.Controls.Add(embedLocateBtn);
            var embedResetBtn = new CyberButton { Text = "Сброс", Size = new Size(110, 35), Location = new Point(370, 335), AccentColor = CyberPalette.BorderHover, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary };
            embedResetBtn.Click += (s, e) => { _pnlScanResults.Visible = false; _pnlScanIdle.Visible = true; };
            _pnlScanResults.Controls.Add(embedResetBtn);
        }
        #endregion

        #region Раздел: Steam сессии
        private void PopulateSteamTab()
        {
            if (_steamPanel == null) return;
            var btn = new CyberButton { Text = "Сканировать аккаунты", Size = new Size(220, 45), Location = new Point(5, 55), AccentColor = CyberPalette.AccentCyan, ForeColor = CyberPalette.TextPrimary, Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold) };
            btn.Click += async (s, e) => await FindSteamAccountsAsync();
            _steamPanel.Controls.Add(btn);
        }
        #endregion

        #region Раздел: Данные ПК
        private void RenderLoadingPCInfoUI()
        {
            if (_pcInfoPanel == null) return;
            _pcInfoPanel.Controls.Clear();
            _pcInfoPanel.Controls.Add(new Label { Text = "Данные ПК", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, AutoSize = true, Location = new Point(5, 5), BackColor = Color.Transparent });
            _pcInfoPanel.Controls.Add(new Label { Text = "Сбор информации о системе. Пожалуйста, подождите...", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = CyberPalette.AccentCyan, Size = new Size(700, 30), Location = new Point(20, 100), BackColor = Color.Transparent });
        }

        private void RenderPCInfoUI()
        {
            if (_pcInfoPanel == null) return;
            _pcInfoPanel.Controls.Clear();
            _pcInfoPanel.Controls.Add(new Label { Text = "Данные ПК", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, AutoSize = true, Location = new Point(5, 5), BackColor = Color.Transparent });
            if (_cachedPCInfo == null)
            {
                RenderLoadingPCInfoUI();
                Task.Run(async () =>
                {
                    _cachedPCInfo = await RetrievePCInfoDataAsync();
                    if (this.IsHandleCreated) this.BeginInvoke(new Action(() => RenderPCInfoUI()));
                });
                return;
            }
            var refreshBtn = new CyberButton { Text = "Обновить данные", Size = new Size(160, 36), Location = new Point(640, 5), AccentColor = CyberPalette.AccentNeon, Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary };
            refreshBtn.Click += (s, e) => { _cachedPCInfo = null; RenderPCInfoUI(); };
            _pcInfoPanel.Controls.Add(refreshBtn);

            var flow = CreateScrollableFlow(_pcInfoPanel, 5, 55, 790, 480, FlowDirection.TopDown, false);
            _uptimeValueLabel = AddDiagnosticCard(flow, "Время непрерывной работы (Uptime)", _cachedPCInfo.Uptime, CyberPalette.AccentEmerald);
            AddDiagnosticCard(flow, "Центральный процессор (CPU)", _cachedPCInfo.Cpu, CyberPalette.AccentCyan);
            AddDiagnosticCard(flow, "Оперативная память (RAM)", _cachedPCInfo.Ram, CyberPalette.AccentNeon);
            AddDiagnosticCard(flow, "Видеоадаптер (GPU)", _cachedPCInfo.Gpu, CyberPalette.AccentEmerald);
            AddDiagnosticCard(flow, "Операционная система (OS)", _cachedPCInfo.Os, CyberPalette.AccentCyan);
            AddDiagnosticCard(flow, "Среда виртуализации (VM)", _cachedPCInfo.VmStatus, _cachedPCInfo.VmColor);
            AddDiagnosticCard(flow, "Системная плата", _cachedPCInfo.Motherboard, CyberPalette.TextPrimary);
            AddDiagnosticCard(flow, "Аппаратные интерфейсы (DMA)", _cachedPCInfo.DmaStatus, _cachedPCInfo.DmaColor);
            AddDiagnosticCard(flow, "Активные процессы записи", _cachedPCInfo.RecordersStatus, _cachedPCInfo.RecordersColor);
        }

        private Label AddDiagnosticCard(FlowLayoutPanel flow, string title, string value, Color valColor)
        {
            var card = new CyberPanel { Size = new Size(750, 78), Margin = new Padding(0, 0, 0, 15), CornerRadius = 16 };
            card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold), ForeColor = CyberPalette.TextSecondary, Size = new Size(400, 20), Location = new Point(20, 14), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
            var label = new Label { Text = value ?? "Данные отсутствуют", Font = new Font("Segoe UI", 11f, FontStyle.Regular), ForeColor = valColor, Size = new Size(710, 26), Location = new Point(20, 38), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, BackColor = Color.Transparent };
            card.Controls.Add(label);
            flow.Controls.Add(card);
            return label;
        }
        #endregion

        #region Раздел: Дополнительные функции
        private void PopulateAdditionalTab()
        {
            if (_additionalPanel == null) return;
            var flow = CreateScrollableFlow(_additionalPanel, 5, 55, 790, 480, FlowDirection.TopDown, false);
            flow.Controls.Add(new Label { Text = "Мониторинг реестра запущен в фоновом режиме. Изменения проверяются автоматически каждые 2.5 сек.\nПеренаправление автоматически фокусирует Редактор реестра на выбранный раздел. Путь также копируется в буфер.", Font = new Font("Segoe UI", 9, FontStyle.Regular), ForeColor = CyberPalette.AccentCyan, Size = new Size(770, 50), BackColor = CyberPalette.Alpha(CyberPalette.AccentCyan, 16), Padding = new Padding(15, 0, 15, 0), TextAlign = ContentAlignment.MiddleLeft });

            var regPaths = new (string Name, string Path)[]
            {
                ("Исключения Windows Defender", @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Exclusions"),
                ("Совместимость приложений (AppCompatFlags)", @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store"),
                ("История запусков Explorer (AppSwitched)", @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched"),
                ("Журнал использования функций (ShowJumpView)", @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\ShowJumpView"),
                ("Трассировка активности фоновых служб (BAM)", @"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\bam\State\UserSettings")
            };
            foreach (var reg in regPaths)
            {
                var rowPanel = new CyberPanel { Size = new Size(770, 46), Margin = new Padding(0, 6, 0, 0), CornerRadius = 12 };
                rowPanel.Controls.Add(new Label { Text = reg.Name, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Location = new Point(15, 0), Size = new Size(380, 46), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
                var statusLabel = new Label { Text = "Инициализация...", Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), ForeColor = CyberPalette.TextSecondary, Location = new Point(410, 0), Size = new Size(220, 46), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
                rowPanel.Controls.Add(statusLabel);
                var navBtn = new CyberButton { Text = "Открыть", Size = new Size(110, 30), Location = new Point(645, 8), AccentColor = CyberPalette.AccentNeon, ForeColor = CyberPalette.TextPrimary, Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), Tag = reg.Path };
                navBtn.Click += (s, e) => { if (s is CyberButton b && b.Tag is string p) NavigateRegistryKey(p); };
                rowPanel.Controls.Add(navBtn);
                flow.Controls.Add(rowPanel);
                _monitoredRegistryPaths.Add((reg.Path, statusLabel));
            }

            var dnsPanel = new CyberPanel { Size = new Size(770, 118), Margin = new Padding(0, 12, 0, 0), BorderColor = CyberPalette.BorderColor, CornerRadius = 16 };
            dnsPanel.Controls.Add(new Label { Text = "АНАЛИЗ СИСТЕМНОГО DNS-КЭША", Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), ForeColor = CyberPalette.AccentCyan, Size = new Size(300, 20), Location = new Point(15, 12), BackColor = Color.Transparent });
            dnsPanel.Controls.Add(new Label { Text = "Позволяет извлечь историю обращений к серверам авторизации и хостам разработчиков запрещенного софта (Midnight, Xone, Neverlose и др.). Следы остаются активными в памяти Windows даже после удаления файлов чита.", Font = new Font("Segoe UI", 8.5f), ForeColor = CyberPalette.TextSecondary, Size = new Size(540, 46), Location = new Point(15, 34), BackColor = Color.Transparent });
            var dnsStatusLabel = new Label { Text = "Статус: Ожидание запуска", Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), ForeColor = CyberPalette.TextSecondary, Size = new Size(350, 20), Location = new Point(15, 86), BackColor = Color.Transparent };
            dnsPanel.Controls.Add(dnsStatusLabel);
            var dnsScanBtn = new CyberButton { Text = "Проверить DNS-кэш", Size = new Size(170, 36), Location = new Point(580, 41), AccentColor = CyberPalette.AccentCyan, ForeColor = CyberPalette.TextPrimary, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold) };
            dnsScanBtn.Click += async (s, e) => await RunDnsCacheScanAsync(dnsStatusLabel, dnsScanBtn);
            dnsPanel.Controls.Add(dnsScanBtn);
            flow.Controls.Add(dnsPanel);
            flow.Controls.Add(new Panel { Size = new Size(770, 1), BackColor = CyberPalette.Alpha(Color.White, 14), Margin = new Padding(0, 15, 0, 8) });

            var dataUsageBtn = new CyberButton { Text = "Статистика сетевого трафика", Size = new Size(770, 42), CustomBaseColor = CyberPalette.CardBg, ForeColor = CyberPalette.TextPrimary, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), AccentColor = CyberPalette.AccentEmerald, Margin = new Padding(0, 4, 0, 0) };
            dataUsageBtn.Click += (s, e) => Process.Start(new ProcessStartInfo("ms-settings:datausage") { UseShellExecute = true });
            flow.Controls.Add(dataUsageBtn);
            flow.Controls.Add(new Label { Text = "Запуск системного компонента мониторинга сетевой активности приложений. Позволяет анализировать нежелательную фоновую сетевую нагрузку.", Font = new Font("Segoe UI", 8.5f), ForeColor = CyberPalette.TextSecondary, Size = new Size(770, 22), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 0, 0) });
        }
        #endregion

        #region Раздел: Гайд-Проверка
        private void PopulateGuideTab()
        {
            if (_guidePanel == null) return;
            _guidePanel.Controls.Clear();
            _guidePanel.Controls.Add(new Label { Text = "Гайд-Проверка", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, AutoSize = true, Location = new Point(20, 15), BackColor = Color.Transparent });
            var guideFlow = CreateScrollableFlow(_guidePanel, 20, 65, 770, 480, FlowDirection.TopDown, false);

            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 1. Использование данных", new[] {
                "• Справа снизу нажмите на значок сети → \"Параметры сети и интернета\"",
                "• Перейдите в раздел \"Использование данных\"",
                "• Проверьте список на наличие подозрительных приложений"
            }, null));

            string everythingText = "SelectFire | Victoria | Desolver | Iniuria | Doras | EzGlobal | Zapped | NinjaWare | BetterGo | RyzeXTR | Menthol | Sector | Dork | Maze | Singlelady | Terrority | Mirium | Hydrawebz | OXIDE | Azrah | Alphen | 2324_CS2_Wh | Kaban.exe | kaban | Euclid | VRedux | FECURITY | loader.exe | loader | Aimmy | Ekknod | TKazer | AimStar | SoftHub | PlagueCheat | WareWare | Invertable | OmniAim | Repa | BebraRange | MemeSence | Aspyxia | SmokeyCheats | SmufrWrecker | Sunsum | EKKNOD | TKAZER | SkeetHook | Furios | Pussycat | Haunted | Iccluded | Sakara | INSTINST | MixSoft | W1NNER | Primordial Jessica | Wurst | skillclient | Flux | Huzuni | WWE | LiquidBounce | Zeus | Nexus | Akrien | Clicker | Aristois | SALHACK2.05 | kamiblue | Pyro | Kyprak | hvii | Summit | KamiBlue | EaZyb1337 | Arisois | GameSense | xulu | Kompl | Xone | ExtrimHack | EZfrags | Midnight | REKTWARE | MUTINY | hack | cheat | Yeahnot | KlarWare | bhop | Aimware | Skeet | Aurora | LeagueMode | Nixware | Unreal | VRedux | Fatality | OneTap | ev0lve | Eternity | Z0rhack | Stickrpg | Demonside.us | BunnyHop | AviraSAMOWARE | ExLoader | FURIOS | Skeet | Avira | Neverlose | NixWare | ESPdX | BoBerHook | Legendware | EGHack | FATALITY | nixware.cc | HAUNTEDPROJECT | Osiris | SLOWLYB1 | RusherClient | Akrienb3 | external | RAGE | .ahk | PhoenixHack | OBR | OneByteRadar | EZinjector | Reborn | Keter | Osiris | Breakthrough | luno | interium | underical | enternal";
            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 2. Проверка через Everything", new[] {
                "• Запустите программу Everything",
                "• Вставьте поисковый запрос в строку поиска"
            }, new[] { ("» Поисковый запрос:", everythingText) }));

            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 3. Проверка через LastActivityView", new[] {
                "• Просмотрите список запущенных программ",
                "• Обратите внимание на дату и время запуска"
            }, null));

            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 4. Проверка через ShellBag Analyzer", new[] {
                "• Нажмите кнопку \"Анализ\" (внизу по центру)",
                "• В правом верхнем углу выберите \"Показать все\"",
                "• Перейдите в раздел \"Удалённые папки\"",
                "• Проверьте дату удаления подозрительных файлов"
            }, null));

            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 5. Проверка папок", new[] {
                "AppData (Win+R → appdata):", "  └─ Roaming: ищем Xone, Naim, Aimmy", "  └─ Local: аналогичная проверка", "",
                "Prefetch:", "  └─ Поиск подозрительных записей", "", "ProgramData:", "  └─ Поиск подозрительных папок", "",
                "CrashDumps:", "  └─ Проверка вылетов приложений"
            }, null));

            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 6. Проверка браузера", new[] {
                "• Проверьте историю загрузок", "• Проверьте историю поиска (чит, cheat, xone, midnight)",
                "• Проверьте сайты читов на наличие аккаунтов", "• Проверьте почты для восстановления (gmail, yandex, mail)"
            }, null));

            string everythingPhSteam = "Midnight | Snmpapi.pdb | Inetmib1.pdb | Xone";
            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 7. ProcessHacker (Steam)", new[] {
                "• Найдите процесс steam.exe в списке", "• Дважды кликните → Properties → Memory",
                "• Кнопка \"strings\": значение 6, все галочки", "• Кнопка \"Filter\": последний пункт", "• Введите поисковые запросы поочерёдно"
            }, new[] { ("» Поисковые запросы:", everythingPhSteam) }));

            string everythingPhCs2 = "##unload_popup|XONE|Dexterion|Aimmy|Whiskey Mod|t.me/|Spectator List|INTERIUM|Enigma|Luno|pastehook/|NAIM|plague|##temp_button_add|##Removals|ZRK 1.4|tim_apple|extrimhack|S1mpleInternal|compkiller|Cryptic|Osiris|Ekknod|##watermark|undetek|Necrum|neoxa7|Shark Gui|ExHack|aimware";
            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 8. ProcessHacker (CS2)", new[] {
                "• Найдите процесс cs2.exe (в ветке steam.exe)", "• Дважды кликните → Properties → Memory",
                "• Кнопка \"strings\": значение 4", "• Кнопка \"Filter\": последний пункт", "• Введите поисковые запросы"
            }, new[] { ("» Поисковые запросы:", everythingPhCs2) }));

            string browserSites = "doomxtf.com|axios-macro.com|midnight.im|xone.fun|blast.hk|yougame.biz|jestkii|wh-satano|cheatcsgo|interium|r8cheats|ezcheats|exloader|cs-elect.ru|extrimhack|neverlose.cc|gamesense|legendware|nixware|phoenix-hack|rf-cheats|anyx.gg|hackvshack.net|ezyhack|unknowncheats|cheater.ninja|insanitycheats.com|cheater.fun|100cheats.ru|undetek.com|cheater.world|zelenka.guru/tags/cs2-cheat|procheats|hells-hack.com|clickhack.ru|procheat.pro|420cheats.com|cs2-cheat|wh-satano.ru|up-game.pro|millex.xyz|boohack.ru|elitehacks.ru|cheatcsgo.ru|box-cheat.ru|novamacro|predator.systems|mvploader|securecheats|darkaim|invision.gg|elitepvpers.com|privatecheatz|cosmocheats|skycheats.com|capefactory.io|rockpapershotgun.com|en1gma.tech|lunocs2.ru|abyss.gg|ezcs.ru|kitchenhack.ru|ezyhack.ru|extrimhack.ru|dhjcheats.com|aimcop.ru|novamacro.xyz|promacro.ru|promacro.store|botmek.ru|topmacro.ru|ggmacro.ru|aimstar|myhacks.store|interium.ooo|nixware.cc|arayas-cheats.com|x-cheats.com|r8cheats.guru|gamebreaker.ru|cheatside.ru|shadowcheat.pro|h4ck.shop";
            string emails = "@mail.ru | @gmail.com | @yandex.ru";
            guideFlow.Controls.Add(CreateGuideSectionPanel("▸ 9. ProcessHacker (Браузер)", new[] {
                "• Найдите процессы: chrome.exe, mozilla.exe, yandex.exe", "• Дважды кликните → Properties → Memory",
                "• Кнопка \"strings\": значение 6, все галочки", "• Кнопка \"Filter\": последний пункт", "• Введите поисковые запросы"
            }, new[] { ("» Сайты для поиска:", browserSites), ("» Почты для поиска (вводить по очереди):", emails) }));
        }

        private Panel CreateGuideSectionPanel(string title, string[] content, (string Label, string Text)[] copyItems)
        {
            var pnl = new CyberPanel { Size = new Size(740, 100), Margin = new Padding(0, 0, 0, 12), Padding = new Padding(20, 15, 20, 15), BorderColor = CyberPalette.BorderColor, CornerRadius = 16 };
            pnl.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), ForeColor = CyberPalette.AccentRedMuted, Size = new Size(700, 28), Location = new Point(15, 10), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
            int y = 42;
            foreach (var line in content)
            {
                if (string.IsNullOrEmpty(line)) { y += 5; continue; }
                pnl.Controls.Add(new Label { Text = line, Font = new Font("Segoe UI", 9), ForeColor = CyberPalette.TextSecondary, Size = new Size(680, 22), Location = new Point(20, y), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
                y += 24;
            }
            if (copyItems != null && copyItems.Length > 0)
            {
                y += 8;
                foreach (var item in copyItems)
                {
                    pnl.Controls.Add(new Label { Text = item.Label, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = CyberPalette.AccentCyan, Size = new Size(680, 18), Location = new Point(20, y), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
                    y += 22;
                    var copyBtn = new CyberButton { Text = "Копировать", Size = new Size(140, 30), Location = new Point(20, y), AccentColor = CyberPalette.AccentCyan, Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary, Tag = item.Text };
                    copyBtn.Click += (s, e) => { if (s is CyberButton b && b.Tag != null) { Clipboard.SetText(b.Tag.ToString()); _ = ShowCustomMessageBoxAsync("Текст скопирован в буфер обмена.", "Готово", "Success"); } };
                    pnl.Controls.Add(copyBtn);
                    y += 38;
                }
            }
            pnl.Height = y + 15;
            return pnl;
        }
        #endregion

        #region Раздел: Мини-игра (Flappy)
        private void PopulateFlappyTab()
        {
            if (_flappyPanel == null) return;
            _flappyGameControl = new CyberFlappyPanel { Location = new Point(5, 55), Size = new Size(790, 480), BackColor = Color.Transparent };
            _flappyPanel.Controls.Add(_flappyGameControl);
        }
        #endregion

        #region Раздел: Контакты
        private void PopulateContactsTab()
        {
            if (_contactsPanel == null) return;
            var flow = new FlowLayoutPanel { Size = new Size(810, 480), Location = new Point(5, 55), FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = false, BackColor = Color.Transparent };
            _contactsPanel.Controls.Add(flow);
            flow.Controls.Add(new CyberContactCard { Platform = "Telegram", Title = "Telegram-канал", Subtitle = "@armagedon1337", ButtonText = "Открыть канал", Url = "https://t.me/armagedon1337", BrandColor = Color.FromArgb(0, 136, 204), Size = new Size(360, 210), Margin = new Padding(0, 0, 25, 0) });
            flow.Controls.Add(new CyberContactCard { Platform = "Discord", Title = "Сообщество Discord", Subtitle = "discord.gg/yx6YxJJYXz", ButtonText = "Присоединиться", Url = "https://discord.gg/yx6YxJJYXz", BrandColor = Color.FromArgb(88, 101, 242), Size = new Size(360, 210), Margin = new Padding(0, 0, 25, 0) });
        }
        #endregion
    }
}
#pragma warning restore CS8618, CS8625, CS8601, CS8602, CS8603, CS8604, CS8600, CS8629