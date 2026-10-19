// Ui.cs — visual moderno (cantos arredondados, estilo macOS / One UI)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ColorMatch
{
    public static class Uxtheme
    {
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr h, string app, string id);

        // tira o tema do Windows do controle: aí BackColor/ForeColor escuros valem (usado nos combos)
        public static void Escuro(Control c)
        {
            try { SetWindowTheme(c.Handle, "", ""); } catch { }
        }
    }

    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(10, 12, 17);
        public static readonly Color Card = Color.FromArgb(19, 23, 30);
        public static readonly Color Card2 = Color.FromArgb(25, 31, 40);
        public static readonly Color Sunken = Color.FromArgb(13, 16, 21);
        public static readonly Color Border = Color.FromArgb(38, 45, 58);
        public static readonly Color Text = Color.FromArgb(237, 241, 247);
        public static readonly Color Muted = Color.FromArgb(139, 150, 168);
        public static readonly Color Accent = Color.FromArgb(92, 138, 255);
        public static readonly Color Good = Color.FromArgb(92, 205, 148);
        public static readonly Color Warn = Color.FromArgb(255, 186, 92);
        public static readonly Color Danger = Color.FromArgb(255, 105, 105);

        public static Font F(float size)
        {
            return new Font("Segoe UI", size, FontStyle.Regular);
        }
        public static Font B(float size)
        {
            return new Font("Segoe UI", size, FontStyle.Bold);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad < 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (var p = Round(r, rad))
            using (var pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }
    }

    // cartão com cantos arredondados + divisórias opcionais
    public class Card : Panel
    {
        public float Radius = 18f;
        public Color FillColor = Theme.Card;
        public Color LineColor = Theme.Border;
        public bool ShowBorder = true;
        public bool Dragging = false;
        public System.Collections.Generic.List<int> Dividers = new System.Collections.Generic.List<int>();

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.Fill(g, r, Radius, FillColor);
            if (ShowBorder || Dragging)
                Theme.Stroke(g, r, Radius, Dragging ? Theme.Accent : LineColor, Dragging ? 1.8f : 1f);
            foreach (int y in Dividers)
            {
                using (var pen = new Pen(Theme.Border, 1f))
                    g.DrawLine(pen, 20, y, Width - 20, y);
            }
            base.OnPaint(e);   // deixa os handlers de Paint (curva, etc.) desenharem por cima
        }
    }

    // botão plano arredondado, com hover e variante "accent"
    public class ModernButton : Button
    {
        public bool Accent;
        public float Radius = 10f;
        bool _hover, _down;

        public ModernButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.F(9f);
            Cursor = Cursors.Hand;
            Size = new Size(110, 32);
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            Color fill, line = Color.Empty;
            Color txt = Enabled ? Theme.Text : Theme.Muted;

            if (Accent)
            {
                fill = _hover ? Theme.Mix(Theme.Accent, Color.White, 0.10) : Theme.Accent;
                if (_down) fill = Theme.Mix(Theme.Accent, Color.Black, 0.10);
            }
            else
            {
                fill = _hover ? Theme.Card2 : Theme.Mix(Theme.Card, Color.White, 0.035);
                if (_down) fill = Theme.Card;
                line = Theme.Border;
            }
            if (!Enabled) { fill = Theme.Card; line = Theme.Border; }

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.Fill(g, r, Radius, fill);
            if (line != Color.Empty) Theme.Stroke(g, r, Radius, line, 1f);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(2, 0, Width - 4, Height), txt,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // caixa de seleção no visual do app (texto à esquerda + seta), abre um menu escuro
    public class SelectBox : Button
    {
        bool _hover, _down;

        public SelectBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.F(8.75f);
            Cursor = Cursors.Hand;
            Size = new Size(352, 28);
            UseVisualStyleBackColor = false;
            TextAlign = ContentAlignment.MiddleLeft;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            Color fill = _hover ? Theme.Card2 : Theme.Sunken;
            if (_down) fill = Theme.Card;

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.Fill(g, r, 8f, fill);
            Theme.Stroke(g, r, 8f, Theme.Border, 1f);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(10, 0, Width - 34, Height),
                Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            using (var p = new Pen(Theme.Muted, 1.6f))
            {
                int cx = Width - 15, cy = Height / 2 - 1;
                g.DrawLines(p, new Point[] {
                    new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
            }
        }
    }

    // cores do menu suspenso no tema escuro
    public class MenuCores : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card2; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card2; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card2; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card2; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.Card; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Card; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Card; } }
        public override Color CheckBackground { get { return Theme.Accent; } }
        public override Color CheckSelectedBackground { get { return Theme.Accent; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    // slider arredondado (trilho + preenchimento accent + bolinha)
    public class ModernSlider : Control
    {
        int _min, _max = 100, _val;
        bool _drag;
        public event EventHandler ValueChanged;

        public ModernSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Card;
            Height = 28;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Minimum
        {
            get { return _min; }
            set { _min = value; if (_val < _min) { _val = _min; Notify(); } Invalidate(); }
        }
        public int Maximum
        {
            get { return _max; }
            set { _max = value; if (_val > _max) { _val = _max; Notify(); } Invalidate(); }
        }
        public int Value
        {
            get { return _val; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v == _val) return;
                _val = v;
                Invalidate();
                Notify();
            }
        }

        void Notify()
        {
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float pad = 9f;
            float w = Width - pad * 2f;
            float cy = Height / 2f;
            double t = (_max > _min) ? (double)(_val - _min) / (_max - _min) : 0;
            float x = pad + (float)(w * t);

            Theme.Fill(g, new RectangleF(pad, cy - 3f, w, 6f), 3f, Theme.Mix(Theme.Card, Color.White, 0.10));
            if (x > pad + 1)
                Theme.Fill(g, new RectangleF(pad, cy - 3f, x - pad, 6f), 3f,
                    ContainsFocus ? Theme.Mix(Theme.Accent, Color.White, 0.10) : Theme.Accent);

            using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(sh, x - 8f, cy - 7f, 16f, 16f);
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, x - 8f, cy - 8f, 16f, 16f);
        }

        void FromMouse(int mx)
        {
            float pad = 9f;
            float w = Width - pad * 2f;
            if (w <= 0) return;
            double t = (mx - pad) / w;
            if (t < 0) t = 0; if (t > 1) t = 1;
            Value = _min + (int)Math.Round(t * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e) { _drag = true; Focus(); FromMouse(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag) FromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k)
        {
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down) return true;
            return base.IsInputKey(k);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = _val - 1;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = _val + 1;
            base.OnKeyDown(e);
        }
    }

    // "chip" com o valor (cantinho arredondado)
    public class Chip : Control
    {
        string _txt = "";

        public override string Text
        {
            get { return _txt; }
            set { _txt = value == null ? "" : value; Invalidate(); }
        }

        public Chip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
            Font = Theme.B(9.25f);
            Size = new Size(56, 24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.Fill(g, r, 7f, Theme.Sunken);
            Theme.Stroke(g, r, 7f, Theme.Border, 1f);
            TextRenderer.DrawText(g, _txt, Font, new Rectangle(0, 0, Width, Height), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // switch estilo One UI (pílula + bolinha), com rótulo à direita
    public class ModernToggle : Control
    {
        bool _checked;
        string _caption = "";
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value == null ? "" : value; Invalidate(); }
        }

        public ModernToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            Font = Theme.F(9f);
            Height = 30;
            Width = 230;
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float h = 20f, w = 38f, y = (Height - h) / 2f;
            var pill = new RectangleF(0.5f, y + 0.5f, w - 1f, h - 1f);
            Theme.Fill(g, pill, (h - 1f) / 2f, _checked ? Theme.Accent : Theme.Mix(Theme.Card, Color.White, 0.12));
            if (!_checked) Theme.Stroke(g, pill, (h - 1f) / 2f, Theme.Border, 1f);

            float kx = _checked ? (w - h + 3f) : 3f;
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, y + 3f, h - 6f, h - 6f);

            TextRenderer.DrawText(g, _caption, Font,
                new Rectangle((int)w + 8, 0, Width - (int)w - 10, Height),
                _checked ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // botão da barra de título (minimizar / fechar / sair)
    public class WinButton : Control
    {
        public int Kind;   // 0 = minimizar, 1 = fechar, 2 = sair
        bool _hover;

        public WinButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            Size = new Size(30, 30);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (_hover)
            {
                Color bg = Kind == 1 ? Color.FromArgb(220, 70, 70) : Theme.Card2;
                Theme.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8f, bg);
            }

            Color fg = (_hover && Kind == 1) ? Color.White : Theme.Muted;
            using (var pen = new Pen(fg, 1.4f))
            {
                float cx = Width / 2f, cy = Height / 2f;
                if (Kind == 0)
                    g.DrawLine(pen, cx - 5f, cy + 1.5f, cx + 5f, cy + 1.5f);
                else if (Kind == 1)
                {
                    g.DrawLine(pen, cx - 4f, cy - 4f, cx + 4f, cy + 4f);
                    g.DrawLine(pen, cx + 4f, cy - 4f, cx - 4f, cy + 4f);
                }
                else
                {
                    g.DrawArc(pen, cx - 4.5f, cy - 4.5f, 9f, 9f, -60f, 300f);
                    g.DrawLine(pen, cx, cy - 7f, cx, cy - 2f);
                }
            }
        }
    }
}
