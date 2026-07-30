using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UselessChecker
{
    // Оверлей ассистента БЕЗ мерцания. Причина мерцания была в прозрачных промежуточных
    // панелях + дорогом фоне из радиальных пятен + отсутствии double buffering на детях.
    // Чинится так: фон оверлея = дешёвый градиент (без PathGradient-пятен), ВСЕ промежуточные
    // панели непрозрачные и double-buffered, поле ввода без системной рамки (BorderStyle.None)
    // со своей обводкой, краснеющей в фокусе. Логика (Groq/Cloudflare, кулдаун, история,
    // пузыри через FillColor) перенесена один-в-один — это не Steam API, не трогаем.
    public class CyberChatOverlay : CyberPanel
    {
        private const string GroqApiKey = "by_cloudflare";
        private static readonly string[] GroqModels = { "groq/compound", "groq/compound-mini", "llama-3.1-8b-instant", "llama3-8b-8192" };
        private const string Endpoint = "https://useless-asistent.tvinkcmd.workers.dev/openai/v1/chat/completions";
        private const int CooldownDuration = 60;

        private Panel _headerPanel;
        private FlowLayoutPanel _messageFlow;
        private Panel _inputPanel;
        private TextBox _inputBox;
        private CyberButton _sendButton;
        private readonly List<Tuple<string, string>> _history = new List<Tuple<string, string>>();
        private static readonly HttpClient ChatHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        private System.Windows.Forms.Timer _cooldownTimer;
        private System.Windows.Forms.Timer _pulseTimer;
        private int _cooldownSecondsRemaining;
        private bool _inputFocused;

        public CyberChatOverlay()
        {
            DoubleBuffered = true;
            GlowOnHover = false;
            CornerRadius = 18;
            InitializeUI();
        }

        private static void Buf(Control c) =>
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(c, true, null);

        // Дешёвый фон оверлея: один градиент, без радиальных пятен — не мерцает при частой
        // перерисовке потока сообщений. Живость даёт header (пульс-точка, перерисовывается локально).
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using var bg = new LinearGradientBrush(r, CyberPalette.Background, CyberPalette.BackgroundWarm, 90f);
            g.FillRectangle(bg, r);
        }

        // Только стеклянная рамка поверх фона.
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CyberPalette.Round(rect, CornerRadius);
            g.SetClip(path);
            int[] al = { 40, 22, 10 };
            for (int i = 0; i < al.Length; i++)
            {
                var inner = new Rectangle(rect.X + i + 1, rect.Y + i + 1, rect.Width - (i + 1) * 2, rect.Height - (i + 1) * 2);
                using var ip = CyberPalette.Round(inner, Math.Max(1, CornerRadius - i - 1));
                using var pen = new Pen(CyberPalette.Alpha(CyberPalette.AccentNeon, al[i]), 1f);
                g.DrawPath(pen, ip);
            }
            g.ResetClip();
            using (var border = new Pen(CyberPalette.Alpha(Color.White, 30), 1f)) g.DrawPath(border, path);
            using (var topHi = new Pen(CyberPalette.Alpha(Color.White, 50), 1f))
                g.DrawLine(topHi, rect.X + CornerRadius, rect.Y, rect.Right - CornerRadius, rect.Y);
        }

        private void InitializeUI()
        {
            // Header: НЕпрозрачный + буфер -> не мерцает; свой градиент рисуется в Paint поверх.
            _headerPanel = new Panel { Location = new Point(0, 0), Size = new Size(380, 44), BackColor = CyberPalette.CardBg };
            Buf(_headerPanel);
            _headerPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new LinearGradientBrush(_headerPanel.ClientRectangle,
                    CyberPalette.Alpha(CyberPalette.CardBgTop, 230), CyberPalette.Alpha(CyberPalette.CardBg, 230), 90f))
                    g.FillRectangle(bg, _headerPanel.ClientRectangle);
                using (var hl = new Pen(CyberPalette.Alpha(Color.White, 18), 1f)) g.DrawLine(hl, 0, 0, _headerPanel.Width, 0);
                using (var sep = new LinearGradientBrush(new Rectangle(0, _headerPanel.Height - 2, _headerPanel.Width, 2),
                    CyberPalette.AccentNeon, Color.Transparent, 0f))
                    g.FillRectangle(sep, 0, _headerPanel.Height - 2, _headerPanel.Width, 2);
                float beat = (float)(0.45 + 0.55 * Math.Sin(DateTime.Now.TimeOfDay.TotalMilliseconds / 300.0));
                using (var glow = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentEmerald, (int)(44 * beat))))
                    g.FillEllipse(glow, 12, 16, 14, 14);
                using (var dot = new SolidBrush(CyberPalette.Alpha(CyberPalette.AccentEmerald, (int)(120 + 120 * beat))))
                    g.FillEllipse(dot, 15, 19, 8, 8);
            };
            _headerPanel.Controls.Add(new Label
            {
                Text = "Useless asistente",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = CyberPalette.AccentNeon,
                Location = new Point(34, 0), Size = new Size(190, 44),
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
            });
            var closeBtn = new CyberButton
            {
                Text = "✕", Size = new Size(28, 28), Location = new Point(342, 8),
                AccentColor = CyberPalette.AccentRedDeep,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = CyberPalette.TextSecondary
            };
            closeBtn.Click += (s, e) => Visible = false;
            _headerPanel.Controls.Add(closeBtn);
            Controls.Add(_headerPanel);

            _pulseTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _pulseTimer.Tick += (s, e) => { if (Visible) _headerPanel.Invalidate(); };

            // Контейнер и поток: НЕпрозрачные (Background) + буфер -> скролл не дёргает фон оверлея.
            var messageContainer = new Panel { Location = new Point(0, 44), Size = new Size(380, 436), BackColor = CyberPalette.Background };
            Buf(messageContainer);
            _messageFlow = new FlowLayoutPanel
            {
                Location = new Point(0, 0), Size = new Size(400, 436),
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                BackColor = CyberPalette.Background, Padding = new Padding(10, 6, 5, 10)
            };
            Buf(_messageFlow);
            messageContainer.Controls.Add(_messageFlow);
            var scrollBar = new CyberVScrollBar { Location = new Point(368, 0), Size = new Size(10, 436) };
            messageContainer.Controls.Add(scrollBar);
            scrollBar.BindTo(_messageFlow);
            Controls.Add(messageContainer);

            // Input: НЕпрозрачный + буфер; поле без системной рамки, своя обводка с фокус-glow.
            _inputPanel = new Panel { Location = new Point(0, 480), Size = new Size(380, 50), BackColor = CyberPalette.CardBg };
            Buf(_inputPanel);
            _inputPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new LinearGradientBrush(_inputPanel.ClientRectangle,
                    CyberPalette.Alpha(CyberPalette.CardBg, 230), CyberPalette.Alpha(CyberPalette.CardBgTop, 230), 90f))
                    g.FillRectangle(bg, _inputPanel.ClientRectangle);
                using (var sep = new LinearGradientBrush(new Rectangle(0, 0, _inputPanel.Width, 2),
                    CyberPalette.AccentNeon, Color.Transparent, 0f))
                    g.FillRectangle(sep, 0, 0, _inputPanel.Width, 2);
                var fr = new Rectangle(10, 9, 272, 31);
                using var rp = CyberPalette.Round(fr, 9);
                using (var fill = new SolidBrush(CyberPalette.Alpha(CyberPalette.Background, 230))) g.FillPath(fill, rp);
                var frameCol = _inputFocused ? CyberPalette.AccentNeon : CyberPalette.Alpha(Color.White, 26);
                using (var pen = new Pen(frameCol, _inputFocused ? 1.4f : 1f)) g.DrawPath(pen, rp);
                if (_inputFocused)
                    using (var glow = new Pen(CyberPalette.Alpha(CyberPalette.AccentNeon, 60), 1f))
                        g.DrawPath(glow, CyberPalette.Round(Rectangle.Inflate(fr, 1, 1), 10));
            };
            _inputBox = new TextBox
            {
                BackColor = CyberPalette.Background, ForeColor = CyberPalette.TextPrimary,
                Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.None, // НЕ FixedSingle!
                Location = new Point(20, 18), Size = new Size(252, 20)
            };
            _inputBox.GotFocus += (s, e) => { _inputFocused = true; _inputPanel.Invalidate(); };
            _inputBox.LostFocus += (s, e) => { _inputFocused = false; _inputPanel.Invalidate(); };
            _inputBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendMessage(); } };
            _inputPanel.Controls.Add(_inputBox);
            _sendButton = new CyberButton
            {
                Text = "Отправить", Size = new Size(82, 31), Location = new Point(288, 9),
                AccentColor = CyberPalette.AccentNeon,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold), ForeColor = CyberPalette.TextPrimary
            };
            _sendButton.Click += (s, e) => SendMessage();
            _inputPanel.Controls.Add(_sendButton);
            Controls.Add(_inputPanel);

            _cooldownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _cooldownTimer.Tick += CooldownTimer_Tick;

            AddMessageBubble("Приветствую. Я Useless asistente. Задайте любой технический вопрос по диагностике системы или поиску следов софта.", "assistant");
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _pulseTimer.Start(); else _pulseTimer.Stop();
        }

        public void FocusInput()
        {
            if (_inputBox != null && _inputBox.CanFocus && _cooldownSecondsRemaining <= 0) _inputBox.Focus();
        }

        private static string CleanMarkdown(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string o = Regex.Replace(input, @"\*\*(.*?)\*\*", "$1");
            o = Regex.Replace(o, @"\*(.*?)\*", "$1");
            return Regex.Replace(o, @"`(.*?)`", "$1");
        }

        private void AddMessageBubble(string text, string sender)
        {
            string cleaned = CleanMarkdown(text);
            var msgPanel = new Panel { Width = 345, BackColor = Color.Transparent, Margin = new Padding(3, 4, 3, 4) };
            bool isUser = sender == "user", isSystem = sender == "system";
            // Фон пузыря = FillColor (CyberPanel рисует именно его; BackColor раньше сливал пузыри).
            var bubbleBg = isSystem ? Color.FromArgb(30, 30, 35) : isUser ? Color.FromArgb(43, 14, 20) : CyberPalette.CardBg;
            var bubbleBorder = isSystem ? CyberPalette.TextDark : isUser ? CyberPalette.AccentNeon : CyberPalette.BorderColor;
            var textColor = isSystem ? CyberPalette.AccentCyan : isUser ? Color.White : CyberPalette.TextPrimary;

            var textLabel = new Label
            {
                Text = cleaned, ForeColor = textColor, Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.Transparent, MaximumSize = new Size(260, 0), AutoSize = true, Location = new Point(12, 10)
            };
            Size size = TextRenderer.MeasureText(cleaned, textLabel.Font, new Size(260, int.MaxValue), TextFormatFlags.WordBreak);
            textLabel.Size = new Size(260, size.Height);

            var bubblePanel = new CyberPanel
            {
                FillColor = bubbleBg, BorderColor = bubbleBorder,
                GlowOnHover = false, CornerRadius = 14,
                Size = new Size(284, size.Height + 20),
                Location = new Point(isUser ? 61 : 5, 0)
            };
            bubblePanel.Controls.Add(textLabel);
            msgPanel.Height = bubblePanel.Height;
            msgPanel.Controls.Add(bubblePanel);
            _messageFlow.Controls.Add(msgPanel);
            _messageFlow.ScrollControlIntoView(msgPanel);
        }

        private void CooldownTimer_Tick(object sender, EventArgs e)
        {
            _cooldownSecondsRemaining--;
            if (_cooldownSecondsRemaining <= 0)
            {
                _cooldownTimer.Stop();
                _inputBox.Enabled = true; _inputBox.BackColor = CyberPalette.Background; _inputBox.ForeColor = CyberPalette.TextPrimary; _inputBox.Text = "";
                _sendButton.Enabled = true; _sendButton.Text = "Отправить"; _sendButton.AccentColor = CyberPalette.AccentNeon;
                _inputBox.Focus();
            }
            else _inputBox.Text = $"Таймаут запросов: {_cooldownSecondsRemaining} сек.";
        }

        private void StartInputCooldown()
        {
            _cooldownSecondsRemaining = CooldownDuration;
            _inputBox.Enabled = false; _inputBox.BackColor = Color.FromArgb(24, 24, 28); _inputBox.ForeColor = CyberPalette.AccentRedMuted;
            _inputBox.Text = $"Таймаут запросов: {_cooldownSecondsRemaining} сек.";
            _sendButton.Enabled = false; _sendButton.AccentColor = CyberPalette.BorderColor;
            _cooldownTimer.Start();
        }

        private async void SendMessage()
        {
            if (_cooldownSecondsRemaining > 0) return;
            string prompt = _inputBox.Text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;
            if (string.IsNullOrEmpty(GroqApiKey) || GroqApiKey == "YOUR_GROQ_API_KEY_HERE")
            { AddMessageBubble("Ошибка: API Ключ не настроен. Пожалуйста, откройте исходный код утилиты и укажите ваш Groq API Ключ.", "system"); return; }

            _inputBox.Clear(); _sendButton.Enabled = false; _inputBox.Enabled = false;
            AddMessageBubble(prompt, "user");

            var typingPanel = new Panel { Width = 345, Height = 30, BackColor = Color.Transparent, Margin = new Padding(3) };
            typingPanel.Controls.Add(new Label { Text = "Useless asistente анализирует...", ForeColor = CyberPalette.TextSecondary, Font = new Font("Segoe UI", 8.5f, FontStyle.Italic), Location = new Point(10, 5), AutoSize = true });
            _messageFlow.Controls.Add(typingPanel); _messageFlow.ScrollControlIntoView(typingPanel);

            string response = await Task.Run(() => CallAiApiAsync(prompt, _history));
            _messageFlow.Controls.Remove(typingPanel); typingPanel.Dispose();
            AddMessageBubble(response, "assistant");
            _history.Add(Tuple.Create("user", prompt)); _history.Add(Tuple.Create("assistant", response));
            if (_history.Count > 16) _history.RemoveRange(0, 2);
            StartInputCooldown();
        }

        private async Task<string> CallAiApiAsync(string prompt, List<Tuple<string, string>> history)
        {
            if (string.IsNullOrEmpty(GroqApiKey) || GroqApiKey == "YOUR_GROQ_API_KEY_HERE")
                return "Ошибка: API Ключ не настроен. Пожалуйста, откройте исходный код утилиты и укажите ваш Groq API Ключ.";
            var messages = new List<object>
            {
                new { role = "system", content = "You are 'Useless asistente', an expert artificial intelligence system specialized in PC operating systems, registry, DNS caches, memory traces, network activity and forensic analysis. You are integrated directly into a diagnostic utility. Provide concise, direct, technically accurate, and brief answers. Speak Russian if the user speaks Russian." }
            };
            foreach (var turn in history) messages.Add(new { role = turn.Item1, content = turn.Item2 });
            messages.Add(new { role = "user", content = prompt });

            foreach (string model in GroqModels)
            {
                try
                {
                    string json = JsonSerializer.Serialize(new { model, messages, temperature = 0.6 });
                    using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                    using var response = await ChatHttpClient.SendAsync(request);
                    string body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(body);
                        var choices = doc.RootElement.GetProperty("choices");
                        if (choices.GetArrayLength() > 0) return choices[0].GetProperty("message").GetProperty("content").GetString();
                    }
                    else Logger.Info($"Модель {model} вернула статус {response.StatusCode}. Переход к резервному варианту...");
                }
                catch (Exception ex) { Logger.Error($"Исключение при обращении к модели {model}", ex); }
            }
            return "Попробуйте позже";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cooldownTimer?.Stop(); _cooldownTimer?.Dispose();
                _pulseTimer?.Stop(); _pulseTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}