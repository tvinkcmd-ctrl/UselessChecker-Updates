using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UselessChecker
{
    // Кастомный вертикальный скроллбар: тонкий трек, неоновый градиентный ползунок,
    // свечение при наведении/перетаскивании. Прячет нативный скроллбар целевого контрола
    // и перехватывает колесо мыши через IMessageFilter. Публичный API (BindTo) не изменён.
    public class CyberVScrollBar : Control, IMessageFilter
    {
        private Control _target;
        private bool _isDragging;
        private int _dragOffset;
        private bool _isHovered;
        private System.Windows.Forms.Timer _scrollTimer;

        public Color ThumbColor { get; set; } = CyberPalette.AccentRedDeep;
        public Color TrackColor { get; set; } = Color.FromArgb(20, 20, 24);

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        private const int SB_VERT = 1;
        private const int WM_MOUSEWHEEL = 0x020A;

        public CyberVScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Width = 8;
            DoubleBuffered = true;
        }

        public void BindTo(Control target)
        {
            if (_target != null) Application.RemoveMessageFilter(this);
            _target = target;
            if (_target == null) return;

            Application.AddMessageFilter(this);
            _target.Layout += (s, e) => UpdateScroll();
            _target.ControlAdded += (s, e) => UpdateScroll();
            _target.ControlRemoved += (s, e) => UpdateScroll();
            _target.SizeChanged += (s, e) => UpdateScroll();
            if (_target is ScrollableControl sc) sc.Scroll += (s, e) => UpdateScroll();
            _target.Disposed += (s, e) => Application.RemoveMessageFilter(this);

            if (_scrollTimer != null) { _scrollTimer.Stop(); _scrollTimer.Dispose(); }
            _scrollTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _scrollTimer.Tick += (s, e) =>
            {
                if (_target == null || _target.IsDisposed) { _scrollTimer.Stop(); _scrollTimer.Dispose(); return; }
                HideNativeScrollBar();
                UpdateScroll();
            };
            _scrollTimer.Start();
        }

        private void HideNativeScrollBar()
        {
            if (_target != null && _target.IsHandleCreated)
                ShowScrollBar(_target.Handle, SB_VERT, false);
        }

        private void UpdateScroll() => Invalidate();

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        private int GetScrollY()
        {
            if (_target is ListBox lb) return lb.TopIndex * lb.ItemHeight;
            if (_target is ScrollableControl sc) return -sc.AutoScrollPosition.Y;
            return 0;
        }

        private int GetTargetVirtualHeight()
        {
            if (_target == null) return 0;
            if (_target is ListBox lb) return lb.Items.Count * lb.ItemHeight;
            if (_target is ScrollableControl sc) return sc.DisplayRectangle.Height;
            int maxBottom = 0;
            foreach (Control ctrl in _target.Controls)
                if (ctrl.Visible && ctrl.Bottom > maxBottom) maxBottom = ctrl.Bottom;
            return maxBottom + _target.Padding.Bottom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_target == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Трек с лёгкой вертикальной глубиной.
            using (var tb = new LinearGradientBrush(ClientRectangle, TrackColor, Color.FromArgb(14, 14, 17), 90f))
                g.FillRectangle(tb, ClientRectangle);

            int totalHeight = GetTargetVirtualHeight();
            int viewHeight = _target.Height;
            if (totalHeight <= viewHeight) return;

            int scrollY = GetScrollY();
            float ratio = (float)viewHeight / totalHeight;
            int thumbHeight = (int)(Height * ratio);
            if (thumbHeight < 24) thumbHeight = 24;
            float trackSpace = Height - thumbHeight;
            float maxScroll = totalHeight - viewHeight;
            float thumbY = maxScroll > 0 ? (scrollY / maxScroll) * trackSpace : 0;
            var thumbRect = new Rectangle(1, (int)thumbY, Width - 2, thumbHeight);

            bool active = _isDragging || _isHovered;

            // Неоновое свечение вокруг ползунка при активности.
            if (active)
            {
                int[] al = { 26, 14 };
                for (int i = 0; i < al.Length; i++)
                {
                    var gr = Rectangle.Inflate(thumbRect, i + 1, 0);
                    using var gp = Round(gr, 4);
                    using var pen = new Pen(CyberPalette.Alpha(CyberPalette.AccentNeon, al[i]), 1f);
                    g.DrawPath(pen, gp);
                }
            }

            // Ползунок: градиент тёмно-красный -> неон (при активности — ярче).
            using (var path = Round(thumbRect, 4))
            {
                var top = active ? CyberPalette.AccentNeon : ThumbColor;
                var bot = active ? CyberPalette.Mix(CyberPalette.AccentNeon, Color.White, 0.15f) : CyberPalette.AccentNeon;
                using (var fb = new LinearGradientBrush(thumbRect, top, bot, 90f))
                    g.FillPath(fb, path);
                // Верхний блик ползунка.
                g.SetClip(path);
                using (var hl = new Pen(CyberPalette.Alpha(Color.White, 40), 1f))
                    g.DrawLine(hl, thumbRect.X + 2, thumbRect.Y + 1, thumbRect.Right - 2, thumbRect.Y + 1);
                g.ResetClip();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_target == null) return;
            int totalHeight = GetTargetVirtualHeight();
            int viewHeight = _target.Height;
            if (totalHeight <= viewHeight) return;

            float ratio = (float)viewHeight / totalHeight;
            int thumbHeight = (int)(Height * ratio);
            if (thumbHeight < 24) thumbHeight = 24;
            float trackSpace = Height - thumbHeight;
            float maxScroll = totalHeight - viewHeight;
            int scrollY = GetScrollY();
            float thumbY = maxScroll > 0 ? (scrollY / maxScroll) * trackSpace : 0;

            if (e.Y >= thumbY && e.Y <= thumbY + thumbHeight)
            {
                _isDragging = true;
                _dragOffset = e.Y - (int)thumbY;
                Capture = true;
            }
            else
            {
                int targetY = (int)((float)e.Y / Height * totalHeight) - (viewHeight / 2);
                ScrollTo(targetY);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isDragging || _target == null) return;
            int totalHeight = GetTargetVirtualHeight();
            int viewHeight = _target.Height;
            float ratio = (float)viewHeight / totalHeight;
            int thumbHeight = (int)(Height * ratio);
            if (thumbHeight < 24) thumbHeight = 24;
            float trackSpace = Height - thumbHeight;
            float newThumbY = e.Y - _dragOffset;
            if (newThumbY < 0) newThumbY = 0;
            if (newThumbY > trackSpace) newThumbY = trackSpace;
            float percentage = trackSpace > 0 ? newThumbY / trackSpace : 0;
            ScrollTo((int)(percentage * (totalHeight - viewHeight)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            Capture = false;
            Invalidate();
        }

        private void ScrollTo(int y)
        {
            if (_target == null) return;
            int totalHeight = GetTargetVirtualHeight();
            int viewHeight = _target.Height;
            int maxScroll = totalHeight - viewHeight;
            if (y < 0) y = 0;
            if (y > maxScroll) y = maxScroll;
            if (_target is ListBox lb)
            {
                if (lb.ItemHeight > 0)
                {
                    int index = y / lb.ItemHeight;
                    if (index >= lb.Items.Count) index = lb.Items.Count - 1;
                    if (index < 0) index = 0;
                    lb.TopIndex = index;
                }
            }
            else if (_target is ScrollableControl sc)
            {
                sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, y);
            }
            Invalidate();
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL && _target != null && !_target.IsDisposed && _target.Visible)
            {
                Control ctrl = Control.FromHandle(m.HWnd);
                if (ctrl != null && IsDescendantOf(_target, ctrl))
                {
                    short delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    ScrollByDelta(delta);
                    return true;
                }
            }
            return false;
        }

        private static bool IsDescendantOf(Control parent, Control child)
        {
            Control current = child;
            while (current != null) { if (current == parent) return true; current = current.Parent; }
            return false;
        }

        private void ScrollByDelta(int delta)
        {
            if (_target == null) return;
            int totalHeight = GetTargetVirtualHeight();
            int viewHeight = _target.Height;
            if (totalHeight <= viewHeight) return;
            if (_target is ListBox lb)
            {
                int scrollY = lb.TopIndex * lb.ItemHeight;
                int lines = (delta / 120) * 3;
                ScrollTo(scrollY - lines * lb.ItemHeight);
            }
            else if (_target is ScrollableControl sc)
            {
                int scrollY = -sc.AutoScrollPosition.Y;
                int amount = (delta / 120) * 45;
                ScrollTo(scrollY - amount);
            }
        }

        private static GraphicsPath Round(Rectangle r, int rad)
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.RemoveMessageFilter(this);
                if (_scrollTimer != null) { _scrollTimer.Stop(); _scrollTimer.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}