// App.cs — ColorMatch: interface gráfica + aplicação de calibração
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ColorMatch
{
    public enum PatternKind { Gray50, White, Black, Ramp, Bars }

    static class Program
    {
        // versão publicada (SemVer): 1.0.x = correções, 1.x.0 = recursos novos
        public const string Versao = "1.5.0";

        [STAThread]
        static void Main(string[] args)
        {
            if (args == null) args = new string[0];
            Func<string, bool> pedido = t => args.Any(a => a.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

            // diagnóstico: qualquer falha inesperada fica registrada e visível
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { ProfileStore.Log("ERRO FATAL: " + e.ExceptionObject); } catch { }
                try
                {
                    MessageBox.Show("O ColorMatch encontrou um erro inesperado.\n\n" + e.ExceptionObject +
                        "\n\nLog: " + ProfileStore.LogPath, "ColorMatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };
            try
            {
                ProfileStore.Log("inicio v" + Versao + " — " + Environment.OSVersion.VersionString +
                    " — CLR " + Environment.Version + " — " + (Environment.Is64BitProcess ? "64" : "32") + " bits");
            }
            catch { }

            // temas que acompanham o app (ex.: "Mac (nativo)") entram na lista de perfis
            BuiltinThemes.Seed();

            // /aplicar: aplica o perfil salvo e sai. Não usa o mutex — pode rodar
            // mesmo com o app já aberto (é só uma escrita de DDC + curva).
            if (pedido("/aplicar") || pedido("/apply"))
            {
                try { ApplyStartup(); } catch (Exception e) { ProfileStore.Log("ERRO /aplicar: " + e.Message); }
                return;
            }

            bool criado;
            using (var mutex = new Mutex(true, "ColorMatch_SingleInstance", out criado))
            {
                if (!criado)
                {
                    // já existe uma instância viva: pede para ela mostrar a janela (ou parar)
                    Sinalizar(pedido("/parar") || pedido("/stop") ? "Stop" : "Show");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                {
                    try { ProfileStore.Log("ERRO UI: " + e.Exception); } catch { }
                    MessageBox.Show(e.Exception.Message, "Erro ColorMatch");
                };

                bool invisivel = pedido("/invisivel") || pedido("/invisible") || pedido("/silent");
                bool bandeja = invisivel || pedido("/min") || pedido("/tray");
                if (pedido("/dump"))
                    MainForm.DumpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colormatch_dump.txt");
                if (pedido("/osd")) MainForm.OsdNoInicio = true;

                Application.Run(new MainForm(bandeja, invisivel));
            }
        }

        // avisa a instância que já está rodando (nome do evento = "ColorMatch_" + sufixo)
        // a instância pode ainda estar iniciando (DDC lento): tenta por até ~10 s
        static void Sinalizar(string sufixo)
        {
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting("ColorMatch_" + sufixo))
                    { ev.Set(); return; }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(250);
                }
                catch (Exception e)
                {
                    ProfileStore.Log("Sinalizar(" + sufixo + "): " + e.Message);
                    return;
                }
            }
            ProfileStore.Log("Sinalizar(" + sufixo + "): instância não respondeu em 10 s");
        }

        // aplica o perfil salvo sem abrir janela (usado no login)
        public static void ApplyStartup()
        {
            var prof = ProfileStore.Load();
            var displays = Win.EnumerateDisplays();
            Win.AttachRects(displays);
            prof.MapLive(displays, true);
            foreach (var ds in displays)
            {
                var c = ds.Cal != null ? ds.Cal : prof.GetByMonitorId(ds.MonitorId, ds.DeviceName);
                Win.AttachPhysical(ds);
                try
                {
                    Win.ApplyExtras(ds.PhysicalHandle, c.Extras);
                    Win.WriteVCP(ds.PhysicalHandle, Win.VCP_BRIGHTNESS, (uint)c.Brightness);
                    Win.WriteVCP(ds.PhysicalHandle, Win.VCP_CONTRAST, (uint)c.Contrast);
                    Win.WriteVCP(ds.PhysicalHandle, Win.VCP_RED_GAIN, (uint)c.RedGain);
                    Win.WriteVCP(ds.PhysicalHandle, Win.VCP_GREEN_GAIN, (uint)c.GreenGain);
                    Win.WriteVCP(ds.PhysicalHandle, Win.VCP_BLUE_GAIN, (uint)c.BlueGain);
                    if (c.Saturation >= 0) Win.WriteVCP(ds.PhysicalHandle, 0x8A, (uint)c.Saturation);
                    bool rampOk = Win.WriteRamp(ds.DeviceName, c.Ramp());
                    ProfileStore.Log("aplicado: " + ds.DeviceName + " (" + ds.FriendlyName + ")" +
                        (rampOk ? " LUT ok" : " FALHA LUT"));
                }
                catch (Exception e) { ProfileStore.Log("erro ao aplicar " + ds.DeviceName + ": " + e.Message); }
            }
        }

        public static void ApplyAll(Profile prof, List<DisplayScreen> displays, bool curves)
        {
            prof.MapLive(displays, true);
            foreach (var ds in displays)
            {
                var c = ds.Cal != null ? ds.Cal : prof.GetByMonitorId(ds.MonitorId, ds.DeviceName);
                Win.ApplyExtras(ds.PhysicalHandle, c.Extras);
                Win.WriteVCP(ds.PhysicalHandle, Win.VCP_BRIGHTNESS, (uint)c.Brightness);
                Win.WriteVCP(ds.PhysicalHandle, Win.VCP_CONTRAST, (uint)c.Contrast);
                Win.WriteVCP(ds.PhysicalHandle, Win.VCP_RED_GAIN, (uint)c.RedGain);
                Win.WriteVCP(ds.PhysicalHandle, Win.VCP_GREEN_GAIN, (uint)c.GreenGain);
                Win.WriteVCP(ds.PhysicalHandle, Win.VCP_BLUE_GAIN, (uint)c.BlueGain);
                if (c.Saturation >= 0) Win.WriteVCP(ds.PhysicalHandle, 0x8A, (uint)c.Saturation);
                if (curves)
                    Win.WriteRamp(ds.DeviceName, c.Ramp());
            }
        }
    }

    // ---------------- Form de teste visual (tela cheia ou faixa) ----------------
    public class PatternForm : Form
    {
        PatternKind _kind;
        string _title;
        static List<PatternForm> _abertos = new List<PatternForm>();

        public static void CloseAll()
        {
            var copia = _abertos.ToArray();
            _abertos.Clear();
            foreach (var f in copia) { try { f.Close(); } catch { } }
        }

        public PatternForm(DisplayScreen ds, PatternKind kind, string title)
            : this(new Rectangle(ds.X, ds.Y, ds.W, ds.H), kind, title) { }

        public PatternForm(Rectangle bounds, PatternKind kind, string title)
        {
            _kind = kind; _title = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            KeyPreview = true;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Click += (s, e) => Close();
            FormClosed += (s, e) => { try { _abertos.Remove(this); } catch { } };
            Shown += (s, e) => { TopMost = false; _abertos.Add(this); Activate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            switch (_kind)
            {
                case PatternKind.Gray50: g.FillRectangle(new SolidBrush(Color.FromArgb(128, 128, 128)), r); break;
                case PatternKind.White: g.FillRectangle(Brushes.White, r); break;
                case PatternKind.Black: g.FillRectangle(Brushes.Black, r); break;
                case PatternKind.Ramp:
                    {
                        int steps = 17;
                        for (int i = 0; i < steps; i++)
                        {
                            int v = (int)Math.Round(255.0 * i / (steps - 1));
                            var b = new SolidBrush(Color.FromArgb(v, v, v));
                            int w = r.Width / steps + 1;
                            g.FillRectangle(b, r.Width / steps * i, 0, w, r.Height);
                        }
                    } break;
                case PatternKind.Bars:
                    {
                        Color[] cs = { Color.White, Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 255), Color.FromArgb(0, 255, 0), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(0, 0, 255), Color.Black };
                        int w = r.Width / cs.Length + 1;
                        for (int i = 0; i < cs.Length; i++) g.FillRectangle(new SolidBrush(cs[i]), r.Width / cs.Length * i, 0, w, r.Height);
                    } break;
            }
            var f = new Font("Segoe UI", 10, FontStyle.Bold);
            var s = "ESC fecha  •  " + _title;
            var size = g.MeasureString(s, f);
            var pos = new PointF(r.Width - size.Width - 24, r.Height - size.Height - 16);
            g.DrawString(s, f, Brushes.Black, pos.X + 1, pos.Y + 1);
            g.DrawString(s, f, Brushes.White, pos.X, pos.Y);
        }
    }

    // identificador de telas: número gigante na tela
    public class IdentifyForm : Form
    {
        string _label;
        public IdentifyForm(DisplayScreen ds, string label)
        {
            _label = label;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(ds.X, ds.Y, ds.W, ds.H);
            BackColor = Color.FromArgb(20, 60, 90);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Click += (s, e) => Close();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = ClientRectangle;
            var fNum = new Font("Segoe UI", (int)(r.Height / 280.0 * 96), FontStyle.Bold, GraphicsUnit.Point);
            var sz = g.MeasureString(_label, fNum);
            g.DrawString(_label, fNum, Brushes.White, (r.Width - sz.Width) / 2f, (r.Height - sz.Height) / 2f);
            var f = new Font("Segoe UI", 14, FontStyle.Regular);
            var s = "Identificar tela - clique para fechar";
            var s2 = g.MeasureString(s, f);
            g.DrawString(s, f, Brushes.White, (r.Width - s2.Width) / 2f, (r.Height - sz.Height) / 2f + sz.Height + 12);
        }
    }

    // ---------------- painel de um monitor ----------------
    public class MonitorPanel : Card
    {
        public DisplayScreen Display;
        public MonitorCal Cal;
        public Profile ProfileRef;
        public MonitorEntry Ent;      // o que o banco de dados sabe deste monitor
        public bool Ativa = true;      // a tela está ligada/presente agora

        Dictionary<string, ModernSlider> _tb = new Dictionary<string, ModernSlider>();
        Dictionary<string, Control> _lb = new Dictionary<string, Control>();
        Card _curvePrev;
        Label _st;
        bool _syncSliders;
        SelectBox _presetSel;
        string _presetNome = "";
        Label _lblTitle;
        Label _lblDev;
        MainForm _owner;

        // handle DDC do monitor (pega na hora se ainda não tem)
        IntPtr H()
        {
            if (Display.PhysicalHandle == IntPtr.Zero) Win.AttachPhysical(Display);
            return Display.PhysicalHandle;
        }

        public string StatusTexto { get { return _st == null ? "" : _st.Text; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // alça de arrasto (3 bolinhas) no canto do cabeçalho
            using (var b = new SolidBrush(Theme.Border))
                for (int i = 0; i < 3; i++)
                    e.Graphics.FillEllipse(b, 9, 30 + i * 6, 3, 3);
        }

        public string MarcaTexto()
        {
            if (!string.IsNullOrEmpty(Display.Brand)) return Display.Brand;
            if (!string.IsNullOrEmpty(Display.FriendlyName)) return Display.FriendlyName;
            return Display.DeviceName;
        }

        string ModeloTexto()
        {
            string m = Display.FriendlyName;
            if (string.IsNullOrEmpty(m)) m = Display.DeviceName;
            return m + "  ·  " + (Display.Primary ? "principal" : "secundário");
        }

        public void RefreshHeader()
        {
            if (_lblTitle != null) _lblTitle.Text = MarcaTexto();
            if (_lblDev != null) _lblDev.Text = ModeloTexto();
        }

        const int WW = 520;
        const int HH = 746;

        public MonitorPanel(DisplayScreen ds, MonitorCal cal, Profile profile, MainForm owner)
        {
            Display = ds; Cal = cal; ProfileRef = profile; _owner = owner;
            Width = WW; Height = HH;
            Radius = 20f;
            FillColor = Theme.Card;
            BackColor = Theme.Bg;
            Margin = new Padding(6);
            DoubleBuffered = true;
            Dividers.Add(74);
            Dividers.Add(318);
            Dividers.Add(648);
            Build();
            // arrastar pelo cabeçalho do cartão troca a ordem (fica salva no perfil)
            LigarArrasto(this);
            if (_lblTitle != null) { LigarArrasto(_lblTitle); _lblTitle.Cursor = Cursors.SizeAll; }
            if (_lblDev != null) { LigarArrasto(_lblDev); _lblDev.Cursor = Cursors.SizeAll; }
            // NADA de DDC aqui: a janela precisa aparecer na hora. A leitura/checagem
            // do DDC é feita depois, em segundo plano (IniciarSondagemDdc).
            SyncSliders();
            ApplyCurve();
        }

        // chamado em segundo plano: adota o DDC na primeira vez ou repõe o do perfil
        public void SondarDdc()
        {
            AdotarOuReporDdc();
        }

        public void StatusLendo()
        {
            if (_st != null) _st.Text = "verificando DDC do monitor...";
        }

        public string AvisoDdc = "";

        public void MostrarAvisoDdc()
        {
            if (_st != null && !string.IsNullOrEmpty(AvisoDdc)) _st.Text = AvisoDdc;
        }

        public void StatusPronto()
        {
            ApplyCurve();
        }

        // predefinição aplicada só neste monitor (muda só a curva de gamma)
        public string PresetAtual { get { return _presetNome; } }

        public void AplicarPredefinicao(double curva, string nome)
        {
            Cal.Gamma = curva;
            SyncSliders();
            _presetNome = nome == null ? "" : nome;
            if (_presetSel != null)
                _presetSel.Text = _presetNome == "" ? "— (personalizado)" : _presetNome;
            ApplyCurve();
        }

        // perfil ICC importado: curva + ganhos calculados do perfil, só neste monitor
        public void AplicarIcc(IccPerfil icc)
        {
            if (icc == null) return;
            Cal.Gamma = icc.Gamma;
            Cal.BrightMult = icc.BrightMult;
            Cal.BlackLift = icc.BlackLift;
            Cal.GainR = icc.GainR;
            Cal.GainG = icc.GainG;
            Cal.GainB = icc.GainB;
            SyncSliders();
            _presetNome = "ICC: " + icc.Nome;
            if (_presetSel != null) _presetSel.Text = _presetNome;
            ApplyCurve();
        }

        // aplica de novo a LUT com os valores atuais (usado pelo filtro de cor)
        public void AplicarLut() { ApplyCurve(); }
        public void Edicao() { if (_owner != null) _owner.EditouPerfil(this); }
        public void AvisarMudanca() { if (_owner != null) _owner.ProfileChanged(); }

        // saturação do monitor via DDC/CI (VCP 0x8A), quando o monitor suporta
        public bool SaturacaoDisponivel(out int valor, out int max)
        {
            uint cur, mx;
            bool ok = Win.ReadVCP(H(), 0x8A, out cur, out mx);
            valor = (int)cur;
            max = (int)mx;
            return ok && mx > 0;
        }

        public void DefinirSaturacao(int v)
        {
            if (_owner != null) _owner.EditouPerfil(this);
            Cal.Saturation = v;
            Win.WriteVCP(H(), 0x8A, (uint)v);
            if (_st != null) _st.Text = "Saturação (DDC): " + v + ".";
            if (_owner != null) _owner.ProfileChanged();
        }

        void PresetCustom()
        {
            _presetNome = "";
            if (_presetSel != null) _presetSel.Text = "— (personalizado)";
        }

        public void LimparPreset() { PresetCustom(); }

        static readonly byte[] CodigosPrincipais = {
            Win.VCP_BRIGHTNESS, Win.VCP_CONTRAST, Win.VCP_RED_GAIN, Win.VCP_GREEN_GAIN, Win.VCP_BLUE_GAIN };
        static readonly string[] ChavesPrincipais = { "bright", "contrast", "red", "green", "blue" };

        // o monitor diz (via DDC/CI) o que ele tem: liga/desliga cada controle principal
        public void AplicarSuporte(MonitorEntry e)
        {
            Ent = e;
            bool conhecido = e != null && e.Vcps != null && e.Vcps.Count > 0;
            for (int i = 0; i < CodigosPrincipais.Length; i++)
            {
                string k = ChavesPrincipais[i];
                bool sup = !conhecido || e.Suporta(CodigosPrincipais[i]);
                if (_tb.ContainsKey("ddc_" + k)) _tb["ddc_" + k].Enabled = sup;
                if (_lb.ContainsKey("n_" + k)) _lb["n_" + k].ForeColor = sup ? Theme.Text : Theme.Muted;
                if (_lb.ContainsKey("v_" + k))
                {
                    if (sup) { if (_tb.ContainsKey("ddc_" + k)) _lb["v_" + k].Text = _tb["ddc_" + k].Value.ToString(); }
                    else _lb["v_" + k].Text = "—";
                }
            }
        }

        // ---- arrastar o cartão para trocar de lugar (a ordem fica salva no perfil) ----
        Point _p0;
        bool _pressionado;
        public bool Arrastando;

        void MdDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _p0 = Control.MousePosition;
            _pressionado = true;
        }
        void MdMove(object s, MouseEventArgs e)
        {
            if (!_pressionado || _owner == null) return;
            // se o botão já foi solto em outro lugar, não começa arrasto nenhum
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { _pressionado = false; return; }
            var p = Control.MousePosition;
            if (!Arrastando)
            {
                if (Math.Abs(p.X - _p0.X) < 8 && Math.Abs(p.Y - _p0.Y) < 8) return;
                Arrastando = true;
                Dragging = true;
                Cursor = Cursors.SizeAll;
                _owner.IniciarArrasto(this);
            }
            _owner.ArrastarPainel(this);
        }
        void MdUp(object s, MouseEventArgs e)
        {
            if (!_pressionado) return;
            _pressionado = false;
            if (!Arrastando) return;
            Arrastando = false;
            Dragging = false;
            Cursor = Cursors.Default;
            if (_owner != null) _owner.SoltarPainel(this);
        }
        void LigarArrasto(Control c)
        {
            c.MouseDown += new MouseEventHandler(MdDown);
            c.MouseMove += new MouseEventHandler(MdMove);
            c.MouseUp += new MouseEventHandler(MdUp);
            c.MouseCaptureChanged += (s, e) => { if (!Arrastando) _pressionado = false; };
        }

        Label MkLabel(string text, int x, int y, int w, Font f, Color c)
        {
            var l = new Label();
            l.Text = text; l.Location = new Point(x, y);
            l.Size = new Size(w, 20);
            l.BackColor = Theme.Card;
            l.ForeColor = c;
            l.Font = f;
            l.AutoSize = false;
            Controls.Add(l); return l;
        }

        Chip MkChip(int x, int y, int w)
        {
            var c = new Chip();
            c.Location = new Point(x, y); c.Size = new Size(w, 26);
            c.BackColor = Theme.Card;
            Controls.Add(c); return c;
        }

        ModernButton MkButton(string text, int x, int y, int w, int h, bool accent)
        {
            var b = new ModernButton();
            b.Text = text; b.Location = new Point(x, y); b.Size = new Size(w, h);
            b.Accent = accent; b.BackColor = Theme.Card;
            b.Font = Theme.F(8.75f);
            Controls.Add(b); return b;
        }

        ModernSlider MkTrack(string key, int x, int y, int min, int max, int val, EventHandler changed, int width)
        {
            var t = new ModernSlider();
            t.Minimum = min; t.Maximum = max;
            t.Value = Math.Max(min, Math.Min(max, val));
            t.Size = new Size(width, 28);
            t.Location = new Point(x, y);
            t.BackColor = Theme.Card;
            if (changed != null) t.ValueChanged += changed;
            Controls.Add(t);
            _tb[key] = t; return t;
        }

        void Section(string text, int y)
        {
            MkLabel(text, 20, y, 256, Theme.B(8.25f), Theme.Muted);
        }

        void Section(string text, int y, int w)
        {
            MkLabel(text, 20, y, w, Theme.B(8.25f), Theme.Muted);
        }

        void Build()
        {
            // ---- cabeçalho do cartão: marca em destaque, codinome embaixo ----
            _lblTitle = MkLabel(MarcaTexto(), 20, 12, 340, Theme.B(14f), Theme.Text);
            _lblDev = MkLabel(ModeloTexto(), 20, 41, 360, Theme.F(9f), Theme.Muted);
            var identify = MkButton("Identificar", 396, 16, 104, 30, false);
            identify.Click += (s, e) => _owner.ShowIdentify(Display);

            // ---- DDC / OSD interno ----
            Section("AJUSTE DO MONITOR", 86, 132);
            var btZero = MkButton("Zerar DDC", 158, 80, 92, 28, false);
            btZero.Click += (s, e) =>
            {
                if (_owner != null) _owner.EditouPerfil(this);
                Cal.Contrast = 50; Cal.RedGain = 50; Cal.GreenGain = 50; Cal.BlueGain = 50;
                SyncSliders(); PushDdcAll(); _owner.ProfileChanged();
                _st.Text = "DDC neutro.";
            };
            var btDdc = MkButton("Testar DDC", 256, 80, 92, 28, false);
            btDdc.Click += (s, e) => TestDdc();
            var btOsd = MkButton("OSD completo", 354, 80, 146, 28, false);
            btOsd.Click += (s, e) =>
            {
                if (_owner != null) _owner.AbrirOsd(this);
            };

            string[] names = { "Brilho", "Contraste", "Ganho R", "Ganho G", "Ganho B" };
            Byte[] codes = { Win.VCP_BRIGHTNESS, Win.VCP_CONTRAST, Win.VCP_RED_GAIN, Win.VCP_GREEN_GAIN, Win.VCP_BLUE_GAIN };
            string[] keys = { "bright", "contrast", "red", "green", "blue" };
            int y = 116;
            for (int i = 0; i < 5; i++)
            {
                string k = keys[i]; byte code = codes[i];
                _lb["n_" + k] = MkLabel(names[i], 20, y + 5, 86, Theme.F(9f), Theme.Text);
                var t = MkTrack("ddc_" + k, 110, y, 0, 100, GetCal(keys[i]), (s, e) => { OnDdcChange(code, k); }, 246);
                _lb["v_" + k] = MkChip(366, y + 2, 60);
                _lb["v_" + k].Text = t.Value.ToString();
                y += 40;
            }

            // ---- curva ----
            Section("CURVA DE COR · LUT DO WINDOWS", 330);

            // predefinição DESTE monitor: a lista abre aqui e aplica só neste cartão
            MkLabel("Predefinição", 20, 359, 86, Theme.F(9f), Theme.Text);
            _presetSel = new SelectBox();
            _presetSel.Location = new Point(110, 354);
            _presetSel.Size = new Size(250, 28);
            _presetSel.BackColor = Theme.Card;
            _presetSel.Text = "— (personalizado)";
            _presetSel.Click += (s, e) => { if (_owner != null) _owner.AbrirMenuPredefinicoes(this, _presetSel); };
            Controls.Add(_presetSel);

            int y2 = 402;

            MkLabel("Gamma", 20, y2 + 5, 86, Theme.F(9f), Theme.Text);
            MkTrack("g_gamma", 110, y2, 40, 260, (int)(Cal.Gamma * 100), (s, e) => { if (_syncSliders) return; Cal.Gamma = _tb["g_gamma"].Value / 100.0; if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); ApplyCurve(); }, 208);
            _lb["g_gamma"] = MkChip(326, y2 + 2, 58);

            MkLabel("Brilho", 20, y2 + 45, 86, Theme.F(9f), Theme.Text);
            MkTrack("g_bright", 110, y2 + 40, 80, 122, (int)(Cal.BrightMult * 100), (s, e) => { if (_syncSliders) return; Cal.BrightMult = _tb["g_bright"].Value / 100.0; if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); ApplyCurve(); }, 208);
            _lb["g_bright"] = MkChip(326, y2 + 42, 58);

            MkLabel("Preto", 20, y2 + 85, 86, Theme.F(9f), Theme.Text);
            MkTrack("g_black", 110, y2 + 80, 0, 80, (int)(Cal.BlackLift * 1000), (s, e) => { if (_syncSliders) return; Cal.BlackLift = _tb["g_black"].Value / 1000.0; if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); ApplyCurve(); }, 208);
            _lb["g_black"] = MkChip(326, y2 + 82, 58);

            MkLabel("Realce R", 20, y2 + 125, 86, Theme.F(9f), Theme.Text);
            MkTrack("g_r", 110, y2 + 120, 88, 112, (int)(Cal.GainR * 100), (s, e) => { if (_syncSliders) return; Cal.GainR = _tb["g_r"].Value / 100.0; if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); ApplyCurve(); }, 208);
            _lb["g_r"] = MkChip(326, y2 + 122, 58);

            MkLabel("Realce G", 20, y2 + 165, 86, Theme.F(9f), Theme.Text);
            MkTrack("g_g", 110, y2 + 160, 88, 112, (int)(Cal.GainG * 100), (s, e) => { if (_syncSliders) return; Cal.GainG = _tb["g_g"].Value / 100.0; if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); ApplyCurve(); }, 208);
            _lb["g_g"] = MkChip(326, y2 + 162, 58);

            MkLabel("Realce B", 20, y2 + 205, 86, Theme.F(9f), Theme.Text);
            MkTrack("g_b", 110, y2 + 200, 88, 112, (int)(Cal.GainB * 100), (s, e) => { if (_syncSliders) return; Cal.GainB = _tb["g_b"].Value / 100.0; if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); ApplyCurve(); }, 208);
            _lb["g_b"] = MkChip(326, y2 + 202, 58);

            _curvePrev = new Card();
            _curvePrev.Location = new Point(398, y2);
            _curvePrev.Size = new Size(100, 100);
            _curvePrev.Radius = 12f;
            _curvePrev.FillColor = Theme.Sunken;
            _curvePrev.ShowBorder = false;
            _curvePrev.BackColor = Theme.Card;
            _curvePrev.Paint += CurvePrevPaint;
            Controls.Add(_curvePrev);

            var neutral = MkButton("Curva zerada", 398, y2 + 108, 100, 28, false);
            neutral.Font = Theme.F(8.5f);
            neutral.Click += (s, e) => { if (_owner != null) _owner.EditouPerfil(this); PresetCustom(); Cal.Gamma = 1.0; Cal.BrightMult = 1.0; Cal.BlackLift = 0; Cal.GainR = Cal.GainG = Cal.GainB = 1.0; SyncSliders(); ApplyCurve(); _owner.ProfileChanged(); };

            // filtro de cor (temperatura/matiz na LUT; saturação por DDC se o monitor tiver)
            var filtro = MkButton("Filtro de cor", 398, y2 + 146, 100, 28, false);
            filtro.Font = Theme.F(8.5f);
            filtro.Click += (s, e) => { if (_owner != null) _owner.AbrirFiltro(this); };

            // ---- testes visuais ----
            Section("PADRÕES DE TESTE · ESC FECHA", 662);
            string[] pn = { "50% cinza", "Branco", "Preto", "Rampa", "Barras" };
            PatternKind[] pk = { PatternKind.Gray50, PatternKind.White, PatternKind.Black, PatternKind.Ramp, PatternKind.Bars };
            for (int i = 0; i < pk.Length; i++)
            {
                var k = pk[i]; string n = pn[i];
                var b = MkButton(n, 20 + i * 97, 686, 90, 30, false);
                b.Font = Theme.F(8.5f);
                b.Click += (s, e) => { var f = new PatternForm(Display, k, Display.ToString()); f.Show(); };
            }

            _st = MkLabel("", 20, 726, 480, Theme.F(8.5f), Theme.Good);
        }

        int GetCal(string key)
        {
            switch (key)
            {
                case "bright": return Cal.Brightness;
                case "contrast": return Cal.Contrast;
                case "red": return Cal.RedGain;
                case "green": return Cal.GreenGain;
                default: return Cal.BlueGain;
            }
        }

        void OnDdcChange(byte code, string key)
        {
            if (_syncSliders) return;
            int v = _tb["ddc_" + key].Value;
            _lb["v_" + key].Text = v.ToString();
            switch (key)
            {
                case "bright": Cal.Brightness = v; break;
                case "contrast": Cal.Contrast = v; break;
                case "red": Cal.RedGain = v; break;
                case "green": Cal.GreenGain = v; break;
                case "blue": Cal.BlueGain = v; break;
            }
            if (_owner != null) _owner.EditouPerfil(this);
            var h = H();
            uint mv = (uint)Math.Max(1, v);
            Win.WriteVCP(h, code, mv);
            _owner.ProfileChanged();
        }

        void TestDdc()
        {
            var h = H();
            if (h == IntPtr.Zero) { _st.Text = "DDC: monitor sem handle fisico."; return; }
            uint cur, mx;
            if (!Win.ReadVCP(h, Win.VCP_BRIGHTNESS, out cur, out mx))
            { _st.Text = "DDC: o monitor nao respondeu a leitura (DDC/CI desligado no OSD?)."; return; }
            uint alvo = cur > 10 ? cur - 5 : cur + 5;
            Win.WriteVCP(h, Win.VCP_BRIGHTNESS, alvo);
            System.Threading.Thread.Sleep(600);
            uint v2, m2;
            Win.ReadVCP(h, Win.VCP_BRIGHTNESS, out v2, out m2);
            Win.WriteVCP(h, Win.VCP_BRIGHTNESS, cur);
            if (v2 == alvo)
                _st.Text = "DDC OK: brilho " + cur + " para " + v2 + " e voltou para " + cur + " (o monitor obedece).";
            else
                _st.Text = "DDC sem efeito: escrevi " + alvo + " mas o monitor leu " + v2 +
                    " (HDR ligado no monitor? ou DDC/CI desligado no OSD?)";
        }

        void PushDdcAll()
        {
            var h = H();
            Win.WriteVCP(h, Win.VCP_BRIGHTNESS, (uint)Cal.Brightness);
            Win.WriteVCP(h, Win.VCP_CONTRAST, (uint)Cal.Contrast);
            Win.WriteVCP(h, Win.VCP_RED_GAIN, (uint)Cal.RedGain);
            Win.WriteVCP(h, Win.VCP_GREEN_GAIN, (uint)Cal.GreenGain);
            Win.WriteVCP(h, Win.VCP_BLUE_GAIN, (uint)Cal.BlueGain);
            if (Cal.Saturation >= 0) Win.WriteVCP(h, 0x8A, (uint)Cal.Saturation);
        }

        // extras do OSD (preset de cor, níveis de preto...) — lento (lê antes de
        // escrever), então só roda ao aplicar um perfil, não a cada slider
        void PushExtras()
        {
            Win.ApplyExtras(H(), Cal.Extras);
        }

        void ApplyCurve()
        {
            var ramp = Cal.Ramp();
            bool ok = Win.WriteRamp(Display.DeviceName, ramp);
            var got = Win.ReadRamp(Display.DeviceName);
            if (!ok || got == null) { _st.Text = "Falha na aplicação da LUT."; ProfileStore.Log("FALHA LUT " + Display.DeviceName); }
            else { _st.Text = "LUT aplicada e confirmada."; }
            _curvePrev.Invalidate();
            _owner.ProfileChanged();
            UpdateValueLabels();
        }

        void UpdateValueLabels()
        {
            _lb["g_gamma"].Text = Cal.Gamma.ToString("0.00");
            _lb["g_bright"].Text = ((int)Math.Round(Cal.BrightMult * 100)).ToString() + "%";
            _lb["g_black"].Text = ((int)Math.Round(Cal.BlackLift * 1000)).ToString();
            _lb["g_r"].Text = ((int)Math.Round(Cal.GainR * 100)).ToString() + "%";
            _lb["g_g"].Text = ((int)Math.Round(Cal.GainG * 100)).ToString() + "%";
            _lb["g_b"].Text = ((int)Math.Round(Cal.GainB * 100)).ToString() + "%";
        }

        // ---- verificação / reaplicação silenciosa (usada pelo vigia da janela principal) ----
        // Compara a LUT que está viva no Windows com a que o perfil pede. Tolerância ~0.15%
        public bool CurveMatchesCal()
        {
            var want = Cal.Ramp();
            var got = Win.ReadRamp(Display.DeviceName);
            if (got == null || got.Length < 768) return false;
            for (int i = 0; i < 768; i++)
                if (Math.Abs((int)(ushort)want[i] - (int)(ushort)got[i]) > 96) return false;
            return true;
        }

        // devolve true se a cor está garantida; "estavaOk" diz se já estava certa (ou se foi reposta agora)
        public bool ReapplyCurveQuiet(out bool estavaOk)
        {
            estavaOk = CurveMatchesCal();
            if (estavaOk) return true;
            return Win.WriteRamp(Display.DeviceName,
                Cal.Ramp());
        }

        public void CopyCalFrom(MonitorCal c)
        {
            Cal.MonitorId = c.MonitorId; Cal.DeviceName = c.DeviceName; Cal.FriendlyName = c.FriendlyName;
            Cal.Brightness = c.Brightness; Cal.Contrast = c.Contrast;
            Cal.RedGain = c.RedGain; Cal.GreenGain = c.GreenGain; Cal.BlueGain = c.BlueGain;
            Cal.Gamma = c.Gamma; Cal.BrightMult = c.BrightMult; Cal.BlackLift = c.BlackLift;
            Cal.GainR = c.GainR; Cal.GainG = c.GainG; Cal.GainB = c.GainB;
        }

        public void RefreshFromHardware(bool readSlDdc)
        {
            if (readSlDdc)
                try { AdotarOuReporDdc(); } catch { }
            SyncSliders();
            ApplyCurve();
        }

        // Ao abrir o painel: o PERFIL é a verdade.
        //  - entrada nova (monitor nunca visto): adota o que está no monitor
        //  - entrada já calibrada: se o monitor está diferente do perfil, repõe
        public void AdotarOuReporDdc()
        {
            var h = H();
            if (h == IntPtr.Zero) return;
            uint cur, mx;
            AvisoDdc = "";

            if (Cal.Nova)
            {
                if (Win.ReadVCP(h, Win.VCP_BRIGHTNESS, out cur, out mx)) Cal.Brightness = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_CONTRAST, out cur, out mx)) Cal.Contrast = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_RED_GAIN, out cur, out mx)) Cal.RedGain = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_GREEN_GAIN, out cur, out mx)) Cal.GreenGain = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_BLUE_GAIN, out cur, out mx)) Cal.BlueGain = (int)cur;
                Cal.Nova = false;
                AvisoDdc = "Monitor novo: valores DDC atuais adotados no perfil.";
                ProfileStore.Log("monitor novo no perfil: " + Display.DeviceName + " adotou DDC " +
                    Cal.Brightness + "/" + Cal.Contrast + "/" + Cal.RedGain + "/" + Cal.GreenGain + "/" + Cal.BlueGain);
                return;
            }

            var dif = "";
            if (Win.ReadVCP(h, Win.VCP_BRIGHTNESS, out cur, out mx) && (int)cur != Cal.Brightness)
            { Win.WriteVCP(h, Win.VCP_BRIGHTNESS, (uint)Cal.Brightness); dif += " brilho=" + cur + "->" + Cal.Brightness; }
            if (Win.ReadVCP(h, Win.VCP_CONTRAST, out cur, out mx) && (int)cur != Cal.Contrast)
            { Win.WriteVCP(h, Win.VCP_CONTRAST, (uint)Cal.Contrast); dif += " contraste=" + cur + "->" + Cal.Contrast; }
            if (Win.ReadVCP(h, Win.VCP_RED_GAIN, out cur, out mx) && (int)cur != Cal.RedGain)
            { Win.WriteVCP(h, Win.VCP_RED_GAIN, (uint)Cal.RedGain); dif += " R=" + cur + "->" + Cal.RedGain; }
            if (Win.ReadVCP(h, Win.VCP_GREEN_GAIN, out cur, out mx) && (int)cur != Cal.GreenGain)
            { Win.WriteVCP(h, Win.VCP_GREEN_GAIN, (uint)Cal.GreenGain); dif += " G=" + cur + "->" + Cal.GreenGain; }
            if (Win.ReadVCP(h, Win.VCP_BLUE_GAIN, out cur, out mx) && (int)cur != Cal.BlueGain)
            { Win.WriteVCP(h, Win.VCP_BLUE_GAIN, (uint)Cal.BlueGain); dif += " B=" + cur + "->" + Cal.BlueGain; }
            if (Cal.Saturation >= 0 && Win.ReadVCP(h, 0x8A, out cur, out mx) && (int)cur != Cal.Saturation)
            { Win.WriteVCP(h, 0x8A, (uint)Cal.Saturation); dif += " sat=" + cur + "->" + Cal.Saturation; }
            if (dif != "")
            {
                ProfileStore.Log("DDC do monitor estava diferente do perfil — corrigido em " + Display.DeviceName + ":" + dif);
                AvisoDdc = "DDC do monitor corrigido para o do perfil." + dif;
            }
        }

        // leitura explícita do hardware (botão "Ler atual"): traz o que está no monitor para os sliders
        public void AttachOrRead()
        {
            uint cur, mx;
            var h = H();
            if (h != IntPtr.Zero)
            {
                if (Win.ReadVCP(h, Win.VCP_BRIGHTNESS, out cur, out mx)) Cal.Brightness = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_CONTRAST, out cur, out mx)) Cal.Contrast = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_RED_GAIN, out cur, out mx)) Cal.RedGain = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_GREEN_GAIN, out cur, out mx)) Cal.GreenGain = (int)cur;
                if (Win.ReadVCP(h, Win.VCP_BLUE_GAIN, out cur, out mx)) Cal.BlueGain = (int)cur;
                Cal.Nova = false;
            }
            var ramp = Win.ReadRamp(Display.DeviceName);
            _st.Text = ramp == null ? "LUT analisada: não leu rampa." : ("LUT atual: " + DescribeRamp(ramp));
        }

        string DescribeRamp(short[] r)
        {
            if (r == null) return "?";
            double d0 = (ushort)r[300] / 65535.0;      // meio da rampa do verde
            double d1 = (ushort)r[0] / 65535.0;        // nível do preto
            if (Math.Abs(d0 - 0.1725) < 0.0025 && d1 == 0) return "identidade (sem ajustes)";
            return "meio nível " + d0.ToString("0.000") + " · preto " + d1.ToString("0.000");
        }

        public void SyncSliders()
        {
            _syncSliders = true;
            try
            {
                _tb["ddc_bright"].Value = Cal.Brightness;
                _tb["ddc_contrast"].Value = Cal.Contrast;
                _tb["ddc_red"].Value = Cal.RedGain;
                _tb["ddc_green"].Value = Cal.GreenGain;
                _tb["ddc_blue"].Value = Cal.BlueGain;
                _lb["v_bright"].Text = Cal.Brightness.ToString();
                _lb["v_contrast"].Text = Cal.Contrast.ToString();
                _lb["v_red"].Text = Cal.RedGain.ToString();
                _lb["v_green"].Text = Cal.GreenGain.ToString();
                _lb["v_blue"].Text = Cal.BlueGain.ToString();
                _tb["g_gamma"].Value = (int)Math.Max(40, Math.Min(260, Cal.Gamma * 100));
                _tb["g_bright"].Value = (int)Math.Max(80, Math.Min(122, Cal.BrightMult * 100));
                _tb["g_black"].Value = (int)Math.Max(0, Math.Min(80, Cal.BlackLift * 1000));
                _tb["g_r"].Value = (int)Math.Max(88, Math.Min(112, Cal.GainR * 100));
                _tb["g_g"].Value = (int)Math.Max(88, Math.Min(112, Cal.GainG * 100));
                _tb["g_b"].Value = (int)Math.Max(88, Math.Min(112, Cal.GainB * 100));
            }
            finally { _syncSliders = false; }
            _curvePrev.Invalidate();
            UpdateValueLabels();
        }

        void CurvePrevPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(7, 7, _curvePrev.Width - 14, _curvePrev.Height - 14);

            var st = g.Save();
            using (var clip = Theme.Round(new RectangleF(1, 1, _curvePrev.Width - 2, _curvePrev.Height - 2), 11f))
                g.SetClip(clip, CombineMode.Intersect);

            using (var pen = new Pen(Theme.Mix(Theme.Sunken, Color.White, 0.07), 1f))
            {
                for (int i = 1; i < 4; i++)
                {
                    g.DrawLine(pen, r.X, r.Y + r.Height * i / 4, r.Right, r.Y + r.Height * i / 4);
                    g.DrawLine(pen, r.X + r.Width * i / 4, r.Y, r.X + r.Width * i / 4, r.Bottom);
                }
            }

            var ramp = Cal.Ramp();
            Pen[] pens = { new Pen(Color.FromArgb(235, 110, 110), 1.6f),
                           new Pen(Color.FromArgb(120, 225, 130), 1.6f),
                           new Pen(Color.FromArgb(120, 150, 245), 1.6f) };
            for (int ch = 0; ch < 3; ch++)
            {
                for (int i = 1; i < 256; i++)
                {
                    // a rampa é short[] com sinal, mas os valores são de 0..65535 (o cast devolve o valor real)
                    double v0 = (ushort)ramp[ch * 256 + i - 1] / 65535.0;
                    double v1 = (ushort)ramp[ch * 256 + i] / 65535.0;
                    float x0 = r.X + (i - 1) / 255f * r.Width;
                    float x1 = r.X + i / 255f * r.Width;
                    float y0 = (float)(r.Bottom - v0 * r.Height);
                    float y1 = (float)(r.Bottom - v1 * r.Height);
                    g.DrawLine(pens[ch], x0, y0, x1, y1);
                }
            }
            foreach (var p in pens) p.Dispose();
            g.Restore(st);
        }
    }

    // ---------------- janela principal ----------------
    public class MainForm : Form
    {
        Profile prof;
        List<DisplayScreen> displays;
        List<MonitorPanel> panels = new List<MonitorPanel>();
        SelectBox selRef;
        SelectBox selPerfil;
        int _refIdx = -1;
        ModernToggle chkAuto;
        NotifyIcon tray;
        Label _status;
        Label _statusAuto;
        System.Windows.Forms.Timer _watch;
        System.Windows.Forms.Timer _settle;
        int _settleLeft;
        ModernToggle chkTray;
        Panel _header;
        FlowLayoutPanel _flow;
        FlowLayoutPanel _bar;
        bool _startHidden;
        System.Windows.Forms.Timer _probeTimer;
        bool _invisible;
        DateTime _profStamp = DateTime.MinValue;
        EventWaitHandle _evShow, _evStop;
        System.Windows.Forms.Timer _saveT;
        public static string DumpPath;
        public static bool OsdNoInicio;

        public MainForm() : this(false, false) { }

        public MainForm(bool startHidden) : this(startHidden, false) { }

        public MainForm(bool startHidden, bool invisible)
        {
            _startHidden = startHidden;
            _invisible = invisible;
            Text = "ColorMatch " + Program.Versao;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1160, 916);
            MinimumSize = new Size(1030, 780);
            BackColor = Theme.Bg;
            Font = Theme.F(9f);
            KeyPreview = true;
            DoubleBuffered = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) PatternForm.CloseAll(); };
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            if (_startHidden) ShowInTaskbar = false;
            Load += MainForm_Load;
            Shown += (s, e) =>
            {
                // re-aplica após a janela aparecer (DWM pode ter resetado a LUT na inicialização)
                var t = new System.Windows.Forms.Timer();
                t.Interval = 1500;
                t.Tick += (x, y) => { t.Stop(); try { ReapplyAllQuiet("janela exibida"); } catch { } };
                t.Start();

                // DDC é lento em alguns monitores: só depois da janela visível, em segundo plano
                var tp = new System.Windows.Forms.Timer();
                tp.Interval = 1200;
                tp.Tick += (x, y) => { tp.Stop(); try { IniciarSondagemDdc(); } catch { } };
                tp.Start();
                _probeTimer = tp;
            };
        }

        // ---- janela sem borda: cantos redondos (DWM) + arrastar/redimensionar ----
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2;                 // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref round, 4);
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                int border = 0x00382C25;       // COLORREF ~ Theme.Border
                DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal)   // WM_NCHITTEST
            {
                long lp = m.LParam.ToInt64();
                int sx = unchecked((short)(lp & 0xFFFF));
                int sy = unchecked((short)((lp >> 16) & 0xFFFF));
                Point p = PointToClient(new Point(sx, sy));
                int g = 7;
                bool l = p.X <= g, r = p.X >= ClientSize.Width - g, t = p.Y <= g, b = p.Y >= ClientSize.Height - g;
                if (t && l) { m.Result = (IntPtr)13; return; }
                if (t && r) { m.Result = (IntPtr)14; return; }
                if (b && l) { m.Result = (IntPtr)16; return; }
                if (b && r) { m.Result = (IntPtr)17; return; }
                if (l) { m.Result = (IntPtr)10; return; }
                if (r) { m.Result = (IntPtr)11; return; }
                if (t) { m.Result = (IntPtr)12; return; }
                if (b) { m.Result = (IntPtr)15; return; }
            }
            base.WndProc(ref m);
        }

        void BuildHeader()
        {
            var h = new Panel();
            h.Dock = DockStyle.None;
            h.Bounds = new Rectangle(0, 0, ClientSize.Width, 60);
            h.BackColor = Theme.Bg;
            _header = h;
            Controls.Add(h);

            var ico = new PictureBox();
            ico.Size = new Size(28, 28);
            ico.Location = new Point(20, 16);
            ico.SizeMode = PictureBoxSizeMode.Zoom;
            ico.BackColor = Theme.Bg;
            try { if (Icon != null) ico.Image = new Icon(Icon, 64, 64).ToBitmap(); } catch { }
            h.Controls.Add(ico);

            var titulo = new Label();
            titulo.Text = "ColorMatch";
            titulo.AutoSize = false;
            titulo.Location = new Point(58, 12);
            titulo.Size = new Size(220, 22);
            titulo.Font = Theme.B(11.5f);
            titulo.ForeColor = Theme.Text;
            titulo.BackColor = Theme.Bg;
            h.Controls.Add(titulo);

            var sub = new Label();
            sub.Text = "todas as telas com a mesma cor  ·  v" + Program.Versao;
            sub.AutoSize = false;
            sub.Location = new Point(60, 34);
            sub.Size = new Size(360, 18);
            sub.Font = Theme.F(8.25f);
            sub.ForeColor = Theme.Muted;
            sub.BackColor = Theme.Bg;
            h.Controls.Add(sub);

            _statusAuto = new Label();
            _statusAuto.Text = "cor ok";
            _statusAuto.AutoSize = false;
            _statusAuto.TextAlign = ContentAlignment.MiddleRight;
            _statusAuto.Size = new Size(200, 18);
            _statusAuto.Font = Theme.F(8.25f);
            _statusAuto.ForeColor = Theme.Good;
            _statusAuto.BackColor = Theme.Bg;
            h.Controls.Add(_statusAuto);

            var sair = new WinButton();
            sair.Kind = 2;
            h.Controls.Add(sair);

            var min = new WinButton();
            min.Kind = 0;
            h.Controls.Add(min);

            var fechar = new WinButton();
            fechar.Kind = 1;
            h.Controls.Add(fechar);

            MouseEventHandler arrastar = (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
            };
            h.MouseDown += arrastar;
            titulo.MouseDown += arrastar;
            sub.MouseDown += arrastar;
            ico.MouseDown += arrastar;

            fechar.Click += (s, e) => Close();
            min.Click += (s, e) => { WindowState = FormWindowState.Minimized; };
            sair.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Sair do ColorMatch?\n\nA cor deixa de ser vigiada até você abrir o app de novo.",
                    "Sair", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    FecharDeVez();
            };

            EventHandler posicionar = (s, e) =>
            {
                fechar.Location = new Point(h.Width - 46, 15);
                min.Location = new Point(h.Width - 82, 15);
                sair.Location = new Point(h.Width - 118, 15);
                _statusAuto.Location = new Point(h.Width - 118 - 210, 21);
            };
            h.Resize += posicionar;
            posicionar(null, EventArgs.Empty);
        }

        // posiciona cabeçalho / barra de comandos / área dos cartões na mão (docking do WinForms empilha errado)
        void ArrumarLayout()
        {
            if (_header == null || _bar == null || _flow == null) return;
            int w = ClientSize.Width;
            _header.Bounds = new Rectangle(0, 0, w, 60);
            _bar.Location = new Point(0, 60);
            _bar.Width = w;
            _bar.Height = _bar.GetPreferredSize(new Size(w, 0)).Height;
            if (_status != null)
            {
                _status.Bounds = new Rectangle(16, 60 + _bar.Height + 2, Math.Max(100, w - 32), 22);
                _status.BringToFront();
            }
            int top = 60 + _bar.Height + 24;
            _flow.Bounds = new Rectangle(0, top, w, Math.Max(0, ClientSize.Height - top));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ArrumarLayout();
        }

        // janela fantasma: inicializa escondida (Load/handle rodam) e nunca aparece até alguém pedir
        bool _forceShow;
        bool _jaIniciou;
        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && !_forceShow)
            {
                if (!_jaIniciou)
                {
                    _jaIniciou = true;
                    Opacity = 0;                 // invisível de verdade, sem piscar
                    ShowInTaskbar = false;
                    base.SetVisibleCore(true);   // força o WinForms a criar o handle e disparar o Load
                    base.SetVisibleCore(false);  // esconde na mesma hora
                    Opacity = 1;
                }
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        void MainForm_Load(object sender, EventArgs ee)
        {
            prof = ProfileStore.Load();
            displays = Win.EnumerateDisplays();
            Win.AttachRects(displays);
            prof.MapLive(displays, true);      // casa cada tela com a entrada do perfil (precisamos disto p/ saber a ordem salva)
            // ordem: a que o usuário salvou arrastando; se não houver, a posição física (esquerda -> direita)
            bool temOrdem = false;
            foreach (var d in displays) if (d.Cal != null && d.Cal.Ordem >= 0) temOrdem = true;
            if (temOrdem)
            {
                displays.Sort(delegate(DisplayScreen a, DisplayScreen b)
                {
                    int oa = (a.Cal != null && a.Cal.Ordem >= 0) ? a.Cal.Ordem : 9999;
                    int ob = (b.Cal != null && b.Cal.Ordem >= 0) ? b.Cal.Ordem : 9999;
                    if (oa != ob) return oa.CompareTo(ob);
                    return a.X.CompareTo(b.X);
                });
            }
            else
            {
                displays.Sort(delegate(DisplayScreen a, DisplayScreen b)
                {
                    if (a.X != b.X) return a.X.CompareTo(b.X);
                    return a.Y.CompareTo(b.Y);
                });
            }

            // ---- cabeçalho próprio da janela ----
            BuildHeader();

            // ---- barra de comandos ----
            var tb = new FlowLayoutPanel();
            tb.AutoSize = false;
            tb.BackColor = Theme.Bg; tb.Padding = new Padding(16, 6, 16, 8);
            tb.FlowDirection = FlowDirection.LeftToRight; tb.WrapContents = true;
            Controls.Add(tb);
            _bar = tb;

            Func<string, int, ModernButton> btn = (t, w) =>
            {
                var b = new ModernButton();
                b.Text = t; b.Width = w; b.Height = 34;
                b.BackColor = Theme.Bg;
                b.Margin = new Padding(3, 4, 3, 4);
                return b;
            };

            // separador fino entre os grupos da barra
            Func<Control> sep = () =>
            {
                var p = new Panel();
                p.Width = 1; p.Height = 22;
                p.BackColor = Theme.Border;
                p.Margin = new Padding(9, 10, 9, 4);
                return p;
            };

            // quebra de linha explícita: mantém cada grupo inteiro na mesma linha
            Func<Control> quebra = () =>
            {
                var p = new Panel();
                p.Width = 4000; p.Height = 0;
                p.Margin = new Padding(0);
                p.BackColor = Theme.Bg;
                return p;
            };

            // grupo 1: perfil (lista de perfis salvos + ações)
            var lblPerfil = new Label();
            lblPerfil.Text = "Perfil";
            lblPerfil.AutoSize = false;
            lblPerfil.Size = new Size(52, 34);
            lblPerfil.TextAlign = ContentAlignment.MiddleLeft;
            lblPerfil.Font = Theme.F(8.75f);
            lblPerfil.ForeColor = Theme.Muted;
            lblPerfil.BackColor = Theme.Bg;
            lblPerfil.Margin = new Padding(3, 4, 2, 4);
            tb.Controls.Add(lblPerfil);

            selPerfil = new SelectBox();
            selPerfil.Width = 250; selPerfil.Height = 34;
            selPerfil.Margin = new Padding(0, 4, 6, 4);
            selPerfil.Click += (s, e) => AbrirMenuPerfis();
            tb.Controls.Add(selPerfil);

            var bNovo = btn("+ Novo perfil", 128);
            bNovo.Click += (s, e) => SalvarComoNovoPerfil();
            tb.Controls.Add(bNovo);

            tb.Controls.Add(sep());

            var bSave = btn("Salvar perfil", 124);
            bSave.Accent = true;
            bSave.Click += (s, e) => { EditouPerfil(null); SaveProfile(true); };
            tb.Controls.Add(bSave);

            var bRestore = btn("Ler do monitor", 140);
            bRestore.Click += (s, e) => { ReloadFromHardware(); };
            tb.Controls.Add(bRestore);

            var bApplySaved = btn("Reaplicar perfil", 148);
            bApplySaved.Click += (s, e) => { ApplySavedNow(); };
            tb.Controls.Add(bApplySaved);

            tb.Controls.Add(quebra());

            // grupo 2: igualar os monitores (copiar o DDC de um para os outros)
            var lblRef = new Label();
            lblRef.Text = "Copiar DDC de";
            lblRef.AutoSize = false;
            lblRef.Size = new Size(104, 34);
            lblRef.TextAlign = ContentAlignment.MiddleLeft;
            lblRef.Font = Theme.F(8.75f);
            lblRef.ForeColor = Theme.Muted;
            lblRef.BackColor = Theme.Bg;
            lblRef.Margin = new Padding(3, 4, 2, 4);
            tb.Controls.Add(lblRef);

            selRef = new SelectBox();
            selRef.Width = 232; selRef.Height = 34;
            selRef.Margin = new Padding(0, 4, 6, 4);
            selRef.Click += (s, e) => AbrirRefMenu();
            tb.Controls.Add(selRef);

            var bEqualize = btn("Copiar p/ os outros", 172);
            bEqualize.Click += (s, e) => EqualizeDdc();
            tb.Controls.Add(bEqualize);

            tb.Controls.Add(sep());

            // grupo 3: padrões de teste
            var bCmp = btn("Cinza nos dois", 132);
            bCmp.Click += (s, e) => ShowCompare(PatternKind.Gray50);
            tb.Controls.Add(bCmp);

            var bFechar = btn("Fechar testes", 124);
            bFechar.Click += (s, e) => PatternForm.CloseAll();
            tb.Controls.Add(bFechar);

            tb.Controls.Add(quebra());

            // grupo 4: sistema
            chkAuto = new ModernToggle();
            chkAuto.Caption = "Iniciar com o Windows";
            chkAuto.Width = 216;
            chkAuto.BackColor = Theme.Bg;
            chkAuto.Margin = new Padding(3, 8, 6, 4);
            chkAuto.CheckedChanged += (s, e) =>
            {
                prof.AutoStart = chkAuto.Checked;
                try { ProfileStore.Save(prof); } catch { }
                SetAutoStart(chkAuto.Checked);
            };
            chkAuto.Checked = IsAutoStart() || prof.AutoStart;
            tb.Controls.Add(chkAuto);

            chkTray = new ModernToggle();
            chkTray.Caption = "Rodar invisível";
            chkTray.Width = 188;
            chkTray.BackColor = Theme.Bg;
            chkTray.Margin = new Padding(3, 8, 9, 4);
            chkTray.Checked = prof.InvisibleMode;
            chkTray.CheckedChanged += (s, e) => SetInvisibleMode(chkTray.Checked);
            tb.Controls.Add(chkTray);

            _status = new Label();
            _status.Text = "Escolha o monitor de referência e use \"Copiar p/ os outros\". \"Salvar perfil\" guarda a calibração.";
            _status.AutoSize = false;
            _status.Size = new Size(560, 22);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Font = Theme.F(8.5f);
            _status.ForeColor = Theme.Muted;
            _status.BackColor = Theme.Bg;
            _status.Margin = new Padding(9, 11, 3, 4);
            Controls.Add(_status);

            _refIdx = displays.FindIndex(d => d.Primary);
            if (_refIdx < 0 && displays.Count > 0) _refIdx = 0;
            AtualizarRefTexto(false);
            AtualizarPerfilTexto(false);
            _status.Text = "Cada cartão ajusta o seu monitor. \"Perfil\" (topo) aplica nos dois. \"Predefinição\" (no cartão) muda só aquele monitor.";

            // ---- vigia: garante que a cor não se perca ----
            // Verifica a cada minuto se a LUT continua viva no Windows; se algo apagou
            // (jogo em tela cheia, mudança de resolução, repouso, driver), repõe na hora.
            _watch = new System.Windows.Forms.Timer();
            _watch.Interval = 60000;
            _watch.Tick += (s, e) => ReapplyAllQuiet("verificação automática");
            _watch.Start();

            // depois de trocar modo (Win+P), ligar monitor ou logar, o driver reseta a LUT alguns
            // segundos depois — então recheca varias vezes nesses primeiros 20s
            _settle = new System.Windows.Forms.Timer();
            _settle.Interval = 1000;
            _settle.Tick += (s, e) =>
            {
                if (_settleLeft <= 0) { _settle.Stop(); return; }
                _settleLeft--;
                ReapplyAllQuiet("ajuste pós-mudança de tela");
            };
            _settleLeft = 20;
            _settle.Start();
            try
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => QueueReapply("mudou a resolução");
                Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
                {
                    if (e.Mode == Microsoft.Win32.PowerModes.Resume) QueueReapply("voltou do repouso");
                };
                Microsoft.Win32.SystemEvents.SessionSwitch += (s, e) =>
                {
                    if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) QueueReapply("tela desbloqueada");
                };
            }
            catch (Exception ex) { ProfileStore.Log("vigia: eventos de sistema indisponiveis (" + ex.Message + ")"); }

            // painel de monitores
            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.None;
            flow.AutoScroll = true;
            flow.BackColor = Theme.Bg;
            flow.Padding = new Padding(12, 6, 12, 18);
            Controls.Add(flow);
            _flow = flow;

            prof.MapLive(displays, true);      // casa cada tela com a entrada certa do perfil
            foreach (var ds in displays)
            {
                try
                {
                    var cal = ds.Cal != null ? ds.Cal : prof.GetByMonitorId(ds.MonitorId, ds.DeviceName);
                    var mp = new MonitorPanel(ds, cal, prof, this);
                    // usa só o que o banco já sabe (sem DDC agora); o resto vem em segundo plano
                    var ent = MonitorDb.PorChave(Profile.HardwareKey(ds.MonitorId));
                    if (ent != null) mp.AplicarSuporte(ent);
                    panels.Add(mp);
                    flow.Controls.Add(mp);
                }
                catch (Exception ex) { ProfileStore.Log("ERRO CriarPainel " + ds.DeviceName + ": " + ex); }
            }

            if (displays.Count == 0 || panels.Count == 0)
            {
                ProfileStore.Log("AVISO: displays=" + displays.Count + " paineis=" + panels.Count);
                _status.Text = "Nenhum painel de monitor foi criado (monitores detectados: " + displays.Count +
                    "). Detalhes em " + ProfileStore.LogPath;
            }

            ReordenarPaineis();
            AjustarTamanhoJanela();
            ArrumarLayout();

            // tray
            tray = new NotifyIcon();
            tray.Text = "ColorMatch";
            tray.Icon = System.Drawing.SystemIcons.Shield;
            tray.Visible = !InvisibleAgora();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir janela", null, (s, e) => MostrarJanela());
            menu.Items.Add("Reaplicar perfil salvo agora", null, (s, e) => ApplySavedNow());
            menu.Items.Add("Aplicar e sair (aplicação única)", null, (s, e) =>
            {
                try { Program.ApplyStartup(); } catch { }
                tray.Visible = false; Application.Exit();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Esconder ícone (modo invisível)", null, (s, e) => SetInvisibleMode(true));
            menu.Items.Add("Sair (paro de vigiar a cor até abrir de novo)", null, (s, e) => FecharDeVez());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => MostrarJanela();

            // ---- controle remoto: outra execução do exe pede "mostrar" ou "parar" ----
            StartIpc();
            _remapear = true;    // primeira verificação já realinha as telas (DISPLAY1/DISPLAY2)
            _profStamp = System.IO.File.GetLastWriteTimeUtc(ProfileStore.ProfilePath);
            ProfileStore.Log("modo: " + (tray.Visible ? "bandeja" : "invisível (sem ícone)") +
                (_startHidden ? ", iniciado escondido" : ", janela aberta"));

            if (!string.IsNullOrEmpty(DumpPath))
            {
                var td = new System.Windows.Forms.Timer();
                td.Interval = 3000;
                td.Tick += (s, e) =>
                {
                    td.Stop();
                    string txt;
                    try { txt = Diagnostico(); }
                    catch (Exception ex) { txt = "erro: " + ex; }
                    try { System.IO.File.WriteAllText(DumpPath, txt); } catch { }
                    FecharDeVez();
                };
                td.Start();
            }
            else if (OsdNoInicio)
            {
                // /osd: abre o OSD completo de cada monitor (atalho rápido, e serve p/ testar)
                var tl = new System.Windows.Forms.Timer();
                tl.Interval = 1200;
                tl.Tick += (s, e) =>
                {
                    tl.Stop();
                    foreach (var p in panels.ToArray())
                        try { AbrirOsd(p); } catch (Exception ex) { ProfileStore.Log("OSD: " + ex.Message); }
                };
                tl.Start();
            }
        }

        // DDC em segundo plano: a janela já está visível; cada monitor é verificado
        // sem travar a interface (alguns monitores levam segundos em cada leitura).
        void IniciarSondagemDdc()
        {
            if (panels.Count == 0) return;
            ProfileStore.Log("sondagem DDC em segundo plano: " + panels.Count + " monitor(es)");
            foreach (var mp in panels.ToArray())
            {
                var alvo = mp;
                try { alvo.StatusLendo(); } catch { }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Win.AttachPhysical(alvo.Display);
                        alvo.SondarDdc();
                    }
                    catch (Exception ex)
                    {
                        ProfileStore.Log("sondagem DDC " + alvo.Display.DeviceName + ": " + ex.Message);
                    }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            try
                            {
                                alvo.SyncSliders();
                                var ent = MonitorDb.PorChave(Profile.HardwareKey(alvo.Display.MonitorId));
                                if (ent != null) alvo.AplicarSuporte(ent);
                                alvo.MostrarAvisoDdc();
                                if (string.IsNullOrEmpty(alvo.AvisoDdc)) alvo.StatusPronto();
                            }
                            catch { }
                        });
                    }
                    catch { }
                });
            }
        }

        public string Diagnostico()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("janela=" + Bounds + "  paineis=" + panels.Count + "  displays=" + (displays == null ? -1 : displays.Count));
            int i = 0;
            foreach (var p in panels)
            {
                i++;
                sb.AppendLine("PAINEL " + i + " ativa=" + p.Ativa);
                sb.AppendLine("   display.dev=" + p.Display.DeviceName + " primary=" + p.Display.Primary +
                    " x=" + p.Display.X + " hmon=" + p.Display.HMonitor + " phys=" + p.Display.PhysicalHandle);
                sb.AppendLine("   display.id=" + p.Display.MonitorId);
                sb.AppendLine("   display.brand=" + p.Display.Brand + " nome=" + p.Display.FriendlyName);
                sb.AppendLine("   cal.id=" + p.Cal.MonitorId + " cal.dev=" + p.Cal.DeviceName + " cal.nome=" + p.Cal.FriendlyName);
                sb.AppendLine("   ddc=" + p.Cal.Brightness + "/" + p.Cal.Contrast + "/" + p.Cal.RedGain + "/" + p.Cal.GreenGain + "/" + p.Cal.BlueGain);
                sb.AppendLine("   curva=" + p.Cal.Gamma + "/" + p.Cal.BrightMult + "/" + p.Cal.BlackLift + "/" + p.Cal.GainR + "/" + p.Cal.GainG + "/" + p.Cal.GainB);
                sb.AppendLine("   status=" + p.StatusTexto);
                sb.AppendLine("   luts ok=" + p.CurveMatchesCal());
            }
            foreach (var c in AllTexts(this)) sb.AppendLine("   [" + c + "]");
            return sb.ToString();
        }

        static List<string> AllTexts(Control raiz)
        {
            var l = new List<string>();
            foreach (Control c in raiz.Controls)
            {
                var ct = c as Chip;
                if (ct != null) l.Add(ct.Text);
                else if (c is Label && c.Text.Length > 0) l.Add(c.Text);
                l.AddRange(AllTexts(c));
            }
            return l;
        }

        bool InvisibleAgora()
        {
            return _invisible || (prof != null && prof.InvisibleMode);
        }

        // liga/desliga o modo invisível agora e grava no perfil (vale para o próximo login também)
        void SetInvisibleMode(bool on)
        {
            prof.InvisibleMode = on;
            _invisible = on;
            try { ProfileStore.Save(prof); } catch { }
            if (tray != null) tray.Visible = !on;
            if (chkTray != null && chkTray.Checked != on) chkTray.Checked = on;
            SetAutoStart(chkAuto != null && chkAuto.Checked);   // reescreve com /invisivel ou /min
            _status.Text = on
                ? "Rodando em segundo plano, sem ícone. Abra de novo pelo atalho ColorMatch na área de trabalho."
                : "Ícone de volta na bandeja (perto do relógio).";
        }

        void StartIpc()
        {
            try
            {
                _evShow = new EventWaitHandle(false, EventResetMode.AutoReset, "ColorMatch_Show");
                _evStop = new EventWaitHandle(false, EventResetMode.AutoReset, "ColorMatch_Stop");
                var th = new Thread(() =>
                {
                    var aguardar = new WaitHandle[] { _evShow, _evStop };
                    while (true)
                    {
                        int i;
                        try { i = WaitHandle.WaitAny(aguardar); } catch { return; }
                        if (i == 1) { try { BeginInvoke(new Action(FecharDeVez)); } catch { } return; }
                        try { BeginInvoke(new Action(MostrarJanela)); } catch { }
                    }
                });
                th.IsBackground = true; th.Start();
            }
            catch (Exception ex) { ProfileStore.Log("ipc: " + ex.Message); }
        }

        void MostrarJanela()
        {
            _forceShow = true;
            ShowInTaskbar = true;
            Opacity = 1;
            WindowState = FormWindowState.Normal;
            Show(); Activate(); BringToFront();
        }

        void FecharDeVez()
        {
            if (_watch != null) _watch.Stop();
            if (tray != null) tray.Visible = false;
            Application.Exit();
        }

        // Avisa que o perfil mudou de verdade (sliders, OSD, extras...).
        // Grava sozinho ~0,9s depois da última mexida, para não escrever o .xml
        // a cada tique de slider. Assim "no perfil" no OSD é verdade e a
        // calibração/ajustes sobrevivem a reiniciar o PC.
        public void ProfileChanged()
        {
            if (_saveT == null)
            {
                _saveT = new System.Windows.Forms.Timer();
                _saveT.Interval = 900;
                _saveT.Tick += (s, e) =>
                {
                    _saveT.Stop();
                    try { SaveProfile(false); } catch { }
                };
            }
            _saveT.Stop();
            _saveT.Start();
        }

        public void ShowIdentify(DisplayScreen ds)
        {
            var f = new IdentifyForm(ds, (displays.IndexOf(ds) + 1).ToString());
            f.Show();
        }

        void ReloadFromHardware()
        {
            EditouPerfil(null);
            bool cur = prof.UseSoftwareCurve;
            foreach (var p in panels)
            {
                p.Cal.MonitorId = p.Display.MonitorId;
                p.Cal.DeviceName = p.Display.DeviceName;
                p.Cal.FriendlyName = p.Display.FriendlyName;
                p.AttachOrRead();
                p.SyncSliders();
            }
            MessageBox.Show("Valores lidos do hardware (DDC) e LUT atual aplicados nos sliders.");
        }

        static void CallPrivate(Control p, string name)
        {
            var m = p.GetType().GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m != null) m.Invoke(p, null);
        }

        // monta o menu escuro com a lista de monitores para o "Copiar DDC de"
        void AbrirRefMenu()
        {
            if (displays == null || displays.Count == 0) return;
            var m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new MenuCores());
            m.ShowImageMargin = false;
            m.BackColor = Theme.Card2;
            m.ForeColor = Theme.Text;
            m.Font = Theme.F(9.5f);
            m.Padding = new Padding(4);
            m.MinimumSize = new Size(selRef.Width, 0);
            for (int i = 0; i < displays.Count; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(displays[i].ToString());
                it.BackColor = Theme.Card2;
                it.ForeColor = Theme.Text;
                it.Padding = new Padding(8, 5, 8, 5);
                if (idx == _refIdx) it.Font = new Font(m.Font, FontStyle.Bold);
                it.Click += (s, e) => { _refIdx = idx; AtualizarRefTexto(); };
                m.Items.Add(it);
            }
            m.Show(selRef, new Point(0, selRef.Height + 2));
        }

        void AtualizarRefTexto()
        {
            AtualizarRefTexto(true);
        }

        void AtualizarRefTexto(bool explicar)
        {
            if (selRef == null || displays == null || _refIdx < 0 || _refIdx >= displays.Count) return;
            selRef.Text = displays[_refIdx].ToString();
            if (explicar && _status != null)
                _status.Text = "Copiar DDC: pega brilho, contraste e ganhos de " + displays[_refIdx].ToString() +
                    " e aplica nos outros monitores.";
        }

        void EqualizeDdc()
        {
            EditouPerfil(null);
            int refIdx = _refIdx;
            if (refIdx < 0 || displays == null || refIdx >= displays.Count) return;
            var refDs = displays[refIdx];
            var refPanel = panels.FirstOrDefault(p => p.Display == refDs);
            if (refPanel == null) return;
            var rc = refPanel.Cal;
            int n = 0;
            foreach (var p in panels)
            {
                if (p.Display == refDs) continue;
                p.Cal.Brightness = rc.Brightness;
                p.Cal.Contrast = rc.Contrast;
                p.Cal.RedGain = rc.RedGain;
                p.Cal.GreenGain = rc.GreenGain;
                p.Cal.BlueGain = rc.BlueGain;
                p.SyncSliders();
                CallPrivate(p, "PushDdcAll");
                n++;
            }
            SaveProfile(false);
            if (_status != null)
                _status.Text = "DDC de " + refDs.ToString() + " copiado para " + n + " monitor(es).";
        }

        // ---------- perfis: lista, criação, aplicação ----------

        void AtualizarPerfilTexto(bool mexerStatus)
        {
            if (selPerfil == null) return;
            string nome = prof == null ? "" : prof.Name;
            if (string.IsNullOrEmpty(nome) || (string.IsNullOrEmpty(prof.Id) && nome == "Perfil"))
                nome = "Calibração atual";
            selPerfil.Text = nome;
            if (mexerStatus && _status != null)
                _status.Text = string.IsNullOrEmpty(prof.Id)
                    ? "Perfil de trabalho \"" + nome + "\" — edições ficam só aqui até você usar \"+ Novo perfil\"."
                    : "Perfil \"" + nome + "\" ativo — as edições são salvas nele.";
        }

        class PerfilMac
        {
            public string Nome;
            public double Curva;
            public string Detalhe;
            public PerfilMac(string n, double c, string d) { Nome = n; Curva = c; Detalhe = d; }
        }

        // perfis que acompanham o macOS (a curva é o que dá para reproduzir via LUT;
        // primárias/white point não são representáveis numa LUT por canal).
        // Curva = gama do perfil / 2.2 (a mesma conta do Icc.cs): γ1.8 -> 0.82 (escurece
        // os meios-tons), γ2.2 -> 1.00 (identidade).
        static readonly PerfilMac[] MacPerfis = new PerfilMac[]
        {
            new PerfilMac("sRGB IEC61966-2.1",    1.00, "gamma efetivo 2.2"),
            new PerfilMac("Display P3",            1.00, "gamma 2.2; primárias P3 não reproduzíveis"),
            new PerfilMac("Adobe RGB (1998)",      1.00, "gamma 2.2"),
            new PerfilMac("Apple RGB",             0.82, "γ1.8 compensado para sRGB"),
            new PerfilMac("Generic RGB",           0.82, "γ1.8 compensado para sRGB"),
            new PerfilMac("Generic Gray 2.2",      1.00, "gamma efetivo 2.2"),
            new PerfilMac("Generic Gray",          0.82, "γ1.8 compensado para sRGB"),
            new PerfilMac("ProPhoto RGB",          0.82, "γ1.8 compensado para sRGB"),
            new PerfilMac("LG FULL HD (macOS)",    0.89, "ICC γ1.96 · compensa p/ sRGB"),
        };

        void AbrirMenuPerfis()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new MenuCores());
            m.ShowImageMargin = false;
            m.BackColor = Theme.Card2;
            m.ForeColor = Theme.Text;
            m.Font = Theme.F(9.5f);
            m.Padding = new Padding(4);
            m.MinimumSize = new Size(selPerfil.Width + 40, 0);

            Func<string, ToolStripMenuItem> item = (txt) =>
            {
                var it = new ToolStripMenuItem(txt);
                it.BackColor = Theme.Card2;
                it.ForeColor = Theme.Text;
                it.Padding = new Padding(8, 5, 8, 5);
                return it;
            };

            var atual = item("Calibração atual" + (string.IsNullOrEmpty(prof.Id) ? "   (editando)" : ""));
            if (string.IsNullOrEmpty(prof.Id)) atual.Font = new Font(m.Font, FontStyle.Bold);
            atual.Click += (s, e) =>
            {
                prof.Id = ""; prof.Name = "Calibração atual";
                SaveProfile(false);
                AtualizarPerfilTexto(true);
            };
            m.Items.Add(atual);
            m.Items.Add(new ToolStripSeparator());

            var perfis = ProfileLibrary.List();
            var cab = item("PERFIS SALVOS");
            cab.Enabled = false;
            cab.ForeColor = Theme.Muted;
            m.Items.Add(cab);
            if (perfis.Count == 0)
            {
                var vazio = item("(nenhum ainda — use \"+ Novo perfil\")");
                vazio.Enabled = false;
                vazio.ForeColor = Theme.Muted;
                m.Items.Add(vazio);
            }
            foreach (var p in perfis)
            {
                var it = item(p.Name);
                if (p.Id == prof.Id) it.Font = new Font(m.Font, FontStyle.Bold);
                var alvo = p;
                it.Click += (s, e) => AplicarPerfil(alvo);
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());

            var dica = item("Predefinições: use a lista \"Predefinição\" dentro de cada cartão");
            dica.Enabled = false;
            dica.ForeColor = Theme.Muted;
            m.Items.Add(dica);
            m.Items.Add(new ToolStripSeparator());

            var salvar = item("+ Salvar como novo perfil...");
            salvar.Click += (s, e) => SalvarComoNovoPerfil();
            m.Items.Add(salvar);

            if (!string.IsNullOrEmpty(prof.Id))
            {
                var ren = item("Renomear perfil...");
                ren.Click += (s, e) => RenomearPerfil();
                m.Items.Add(ren);

                var del = item("Excluir perfil...");
                del.ForeColor = Theme.Danger;
                del.Click += (s, e) => ExcluirPerfil();
                m.Items.Add(del);
            }

            m.Show(selPerfil, new Point(0, selPerfil.Height + 2));
        }

        void AplicarPerfil(Profile src)
        {
            if (src == null) return;
            int n = 0, faltando = 0;
            foreach (var p in panels)
            {
                var c = src.Match(p.Display.MonitorId, p.Display.DeviceName);
                if (c == null) { faltando++; continue; }
                p.Cal.CopyValuesFrom(c);
                p.SyncSliders();
                p.LimparPreset();
                CallPrivate(p, "PushExtras");
                CallPrivate(p, "PushDdcAll");
                CallPrivate(p, "ApplyCurve");
                n++;
            }
            prof.Id = src.Id;
            prof.Name = src.Name;
            prof.Editavel = src.Editavel;
            prof.UseSoftwareCurve = src.UseSoftwareCurve;
            SaveProfile(false);
            AtualizarPerfilTexto(true);
            _status.Text = "Perfil \"" + src.Name + "\" aplicado em " + n + " monitor(es)" +
                (faltando > 0 ? " (" + faltando + " sem entrada no perfil)" : "") + ".";
            ProfileStore.Log("perfil da lista aplicado: " + src.Name + " (" + n + " monitor(es))");
        }

        // menu de predefinições de UM monitor (aberto pelo botão "Predefinição" do cartão)
        public void AbrirMenuPredefinicoes(MonitorPanel mp, Control ancora)
        {
            var m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new MenuCores());
            m.ShowImageMargin = false;
            m.BackColor = Theme.Card2;
            m.ForeColor = Theme.Text;
            m.Font = Theme.F(9.5f);
            m.Padding = new Padding(4);

            Func<string, ToolStripMenuItem> item = (txt) =>
            {
                var it = new ToolStripMenuItem(txt);
                it.BackColor = Theme.Card2;
                it.ForeColor = Theme.Text;
                it.Padding = new Padding(8, 5, 8, 5);
                return it;
            };

            var cab = item("PREDEFINIÇÃO · " + mp.MarcaTexto());
            cab.Enabled = false;
            cab.ForeColor = Theme.Muted;
            m.Items.Add(cab);

            foreach (var pm in MacPerfis)
            {
                var it = item(pm.Nome + "   ·   " + pm.Detalhe);
                if (pm.Nome == mp.PresetAtual) it.Font = new Font(m.Font, FontStyle.Bold);
                var alvo = pm;
                var painel = mp;
                it.Click += (s, e) => AplicarMacEm(painel, alvo);
                m.Items.Add(it);
            }

            m.Items.Add(new ToolStripSeparator());

            var cabIcc = item("PERFIS ICC IMPORTADOS");
            cabIcc.Enabled = false;
            cabIcc.ForeColor = Theme.Muted;
            m.Items.Add(cabIcc);

            var iccs = Icc.Listar();
            if (iccs.Count == 0)
            {
                var vazio = item("(nenhum — use \"Importar perfil ICC\")");
                vazio.Enabled = false;
                vazio.ForeColor = Theme.Muted;
                m.Items.Add(vazio);
            }
            foreach (var ic in iccs)
            {
                var it = item(ic.Nome + "   ·   " + ic.Detalhe);
                if (("ICC: " + ic.Nome) == mp.PresetAtual) it.Font = new Font(m.Font, FontStyle.Bold);
                var perfilIcc = ic;
                var painel = mp;
                it.Click += (s, e) => AplicarIccEm(painel, perfilIcc);
                m.Items.Add(it);
            }
            var importar = item("Importar perfil ICC...");
            importar.Click += (s, e) => ImportarIcc(mp);
            m.Items.Add(importar);

            m.Show(ancora, new Point(0, ancora.Height + 2));
        }

        void AplicarIccEm(MonitorPanel mp, IccPerfil icc)
        {
            if (mp == null || icc == null || !icc.Ok) return;

            // vira perfil de trabalho: não altera um perfil salvo na lista
            prof.Id = "";
            prof.Name = "Calibração atual";
            prof.Editavel = false;
            mp.AplicarIcc(icc);
            SaveProfile(false);
            AtualizarPerfilTexto(true);
            _status.Text = "Perfil ICC \"" + icc.Nome + "\" aplicado só no " + mp.MarcaTexto() + ".";
            ProfileStore.Log("ICC " + icc.Nome + " aplicado em " + mp.Display.DeviceName +
                " (gama " + icc.Gamma + ")");
        }

        void ImportarIcc(MonitorPanel mp)
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Importar perfil ICC";
                d.Filter = "Perfis ICC (*.icc;*.icm)|*.icc;*.icm|Todos os arquivos (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string destino = Icc.Importar(d.FileName);
                    var icc = Icc.Ler(destino);
                    if (icc == null || !icc.Ok)
                    {
                        MessageBox.Show("Não consegui ler este perfil ICC.\n" +
                            (icc == null ? "" : icc.Erro), "ColorMatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    AplicarIccEm(mp, icc);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Falha ao importar o ICC:\n" + ex.Message,
                        "ColorMatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void AplicarMacEm(MonitorPanel mp, PerfilMac pm)
        {
            if (mp == null || pm == null) return;

            // vira perfil de trabalho: não altera um perfil salvo na lista
            prof.Id = "";
            prof.Name = "Calibração atual";
            prof.Editavel = false;
            mp.AplicarPredefinicao(pm.Curva, pm.Nome);
            SaveProfile(false);
            AtualizarPerfilTexto(true);
            _status.Text = "Predefinição \"" + pm.Nome + "\" aplicada só no " + mp.MarcaTexto() + ".";
            ProfileStore.Log("predefinição " + pm.Nome + " aplicada em " + mp.Display.DeviceName);
        }

        void SalvarComoNovoPerfil()
        {
            string sugestao = prof.Name;
            if (string.IsNullOrEmpty(sugestao) || sugestao == "Perfil" || sugestao == "Calibração atual")
                sugestao = "Meu perfil";
            if (!string.IsNullOrEmpty(prof.Id)) sugestao = prof.Name + " (cópia)";
            string nome = PromptForm.Ask(this, "Novo perfil", "Nome do perfil:", sugestao);
            if (nome == null) return;

            Profile existente = null;
            foreach (var p in ProfileLibrary.List())
                if (string.Equals(p.Name, nome, StringComparison.CurrentCultureIgnoreCase)) { existente = p; break; }

            if (existente != null &&
                MessageBox.Show("Já existe um perfil \"" + nome + "\". Substituir?", "ColorMatch",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            var novo = prof.Clone();
            novo.Name = nome;
            novo.Id = existente != null ? existente.Id : Guid.NewGuid().ToString("N");
            novo.Editavel = existente != null ? existente.Editavel : false;
            ProfileLibrary.Save(novo);
            prof.Id = novo.Id;
            prof.Name = novo.Name;
            SaveProfile(false);
            AtualizarPerfilTexto(true);
            _status.Text = "Perfil \"" + nome + "\" salvo e adicionado à lista.";
            ProfileStore.Log("perfil criado: " + nome);
        }

        void RenomearPerfil()
        {
            if (string.IsNullOrEmpty(prof.Id))
            {
                MessageBox.Show("Este perfil ainda não está na lista. Use \"+ Novo perfil\" para salvá-lo primeiro.");
                return;
            }
            string nome = PromptForm.Ask(this, "Renomear perfil", "Novo nome:", prof.Name);
            if (nome == null) return;
            prof.Name = nome;
            ProfileLibrary.Save(prof);
            SaveProfile(false);
            AtualizarPerfilTexto(true);
            _status.Text = "Perfil renomeado para \"" + nome + "\".";
        }

        void ExcluirPerfil()
        {
            if (string.IsNullOrEmpty(prof.Id))
            {
                MessageBox.Show("O perfil de trabalho ainda não está na lista (nada para excluir).");
                return;
            }
            if (MessageBox.Show("Excluir o perfil \"" + prof.Name + "\" da lista?\n\nA calibração atual NÃO é alterada.",
                "ColorMatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var p = ProfileLibrary.FindById(prof.Id);
            ProfileLibrary.Delete(p != null ? p : prof);
            prof.Id = "";
            prof.Name = "Calibração atual";
            SaveProfile(false);
            AtualizarPerfilTexto(true);
            _status.Text = "Perfil removido da lista. A calibração atual continua aplicada.";
            ProfileStore.Log("perfil excluído da lista");
        }

        // faixa de cinza na juncao das duas telas, com o app ainda acessivel
        internal void ShowCompare(PatternKind kind)
        {
            PatternForm.CloseAll();
            if (displays.Count < 2)
            {
                MessageBox.Show("Preciso de dois monitores ligados para comparar lado a lado.");
                return;
            }
            var ord = displays.OrderBy(d => d.X).ToList();
            var esq = ord[0];
            var dir = ord[ord.Count - 1];

            // sobe a janela do app para o canto, liberando a faixa de baixo
            Location = new Point(esq.X + 24, esq.Y + 24);

            int menorH = Math.Min(esq.H, dir.H);
            int faixaH = Math.Max(140, menorH * 32 / 100);
            int faixaW = Math.Max(160, Math.Min(esq.W, dir.W) * 30 / 100);

            var rEsq = new Rectangle(esq.X + esq.W - faixaW, esq.Y + esq.H - faixaH - 80, faixaW, faixaH);
            var rDir = new Rectangle(dir.X, dir.Y + dir.H - faixaH - 80, faixaW, faixaH);
            new PatternForm(rEsq, kind, esq.ToString()).Show();
            new PatternForm(rDir, kind, dir.ToString()).Show();
            _status.Text = "Faixas de cinza abertas na juncao das telas. Compare a linha entre elas e ajuste os sliders.";
        }

        void SaveProfile(bool talk)
        {
            foreach (var p in panels)
            {
                p.Cal.FriendlyName = p.Display.FriendlyName;
            }
            ProfileStore.Save(prof);
            // original protegido NUNCA é sobrescrito; só cópias de trabalho (Editavel) sincronizam
            if (!string.IsNullOrEmpty(prof.Id) && prof.Editavel)
                try { ProfileLibrary.Save(prof); } catch { }
            _profStamp = System.IO.File.GetLastWriteTimeUtc(ProfileStore.ProfilePath);
            if (talk) MessageBox.Show("Perfil salvo em: " + ProfileStore.ProfilePath);
        }

        // chamado antes de qualquer edição do usuário: se o perfil ativo é um original
        // (ou se veio de uma predefinição), cria automaticamente uma cópia e passa a
        // editar a cópia — o original nunca é sobrescrito.
        public void EditouPerfil(MonitorPanel origem)
        {
            try
            {
                if (!string.IsNullOrEmpty(prof.Id) && prof.Editavel) return;   // já é cópia

                Profile original = null;
                string nomeBase = null;
                if (!string.IsNullOrEmpty(prof.Id))
                {
                    original = ProfileLibrary.FindById(prof.Id);
                    if (original == null) return;
                    nomeBase = original.Name;
                }
                else
                {
                    MonitorPanel o = origem;
                    if (o == null)
                        foreach (var p in panels)
                            if (!string.IsNullOrEmpty(p.PresetAtual)) { o = p; break; }
                    if (o == null || string.IsNullOrEmpty(o.PresetAtual)) return;
                    nomeBase = o.PresetAtual;
                    if (nomeBase.StartsWith("ICC: ")) nomeBase = nomeBase.Substring(5);
                }

                var copia = prof.Clone();
                copia.Id = Guid.NewGuid().ToString("N");
                copia.Name = NomeCopia(nomeBase);
                copia.Editavel = true;
                ProfileLibrary.Save(copia);

                prof.Id = copia.Id;
                prof.Name = copia.Name;
                prof.Editavel = true;
                SaveProfile(false);
                AtualizarPerfilTexto(true);
                _status.Text = "Edição criou a cópia \"" + copia.Name + "\". " +
                    (original != null ? "O original \"" + original.Name + "\" ficou intacto." : "A predefinição ficou intacta.");
                ProfileStore.Log("edição: cópia \"" + copia.Name + "\" criada" +
                    (original != null ? " de \"" + original.Name + "\"" : " a partir da predefinição"));
            }
            catch (Exception ex) { ProfileStore.Log("EditouPerfil: " + ex.Message); }
        }

        string NomeCopia(string baseNome)
        {
            string nome = baseNome + " (editado)";
            if (ProfileLibrary.FindByName(nome) == null) return nome;
            for (int i = 2; i < 1000; i++)
            {
                string n = baseNome + " (editado " + i + ")";
                if (ProfileLibrary.FindByName(n) == null) return n;
            }
            return baseNome + " (editado " + DateTime.Now.ToString("HHmmss") + ")";
        }

        // se o .xml mudou no disco (por ex. você salvou outro perfil), o vigia passa a usar o novo
        void RecarregarPerfilSeMudou()
        {
            try
            {
                var st = System.IO.File.GetLastWriteTimeUtc(ProfileStore.ProfilePath);
                if (st == _profStamp) return;
                _profStamp = st;
                var saved = ProfileStore.Load();
                var atuais = new List<DisplayScreen>();
                foreach (var p in panels) atuais.Add(p.Display);
                saved.MapLive(atuais, false);      // só casa o que existe; não inventa entrada nova
                foreach (var p in panels)
                {
                    if (p.Display.Cal == null) continue;
                    p.CopyCalFrom(p.Display.Cal);
                    p.SyncSliders();
                }
                if (chkTray != null) chkTray.Checked = saved.InvisibleMode;
                prof.Name = saved.Name;
                prof.Id = saved.Id;
                prof.Editavel = saved.Editavel;
                prof.UseSoftwareCurve = saved.UseSoftwareCurve;
                prof.InvisibleMode = saved.InvisibleMode;
                prof.AutoStart = saved.AutoStart;
                AtualizarPerfilTexto(false);
                ProfileStore.Log("vigia: perfil mudou no disco — recarregado");
            }
            catch (Exception ex) { ProfileStore.Log("vigia: falha ao recarregar perfil (" + ex.Message + ")"); }
        }

        // aplica (DDC + curva) o perfil que está no arquivo .xml agora mesmo, na mão
        void ApplySavedNow()
        {
            var saved = ProfileStore.Load();
            foreach (var p in panels)
            {
                var c = saved.GetByMonitorId(p.Display.MonitorId, p.Display.DeviceName);
                p.CopyCalFrom(c);
                p.SyncSliders();
                CallPrivate(p, "PushExtras");
                CallPrivate(p, "PushDdcAll");
                CallPrivate(p, "ApplyCurve");
            }
            _profStamp = System.IO.File.GetLastWriteTimeUtc(ProfileStore.ProfilePath);
            ProfileStore.Log("perfil salvo reaplicado manualmente");
            _status.Text = "Perfil salvo reaplicado agora (" + DateTime.Now.ToString("HH:mm:ss") + ").";
            ReapplyAllQuiet("reaplicação manual");
        }

        // chamado pelos eventos de sistema (resolução, repouso, desbloqueio) — sempre na thread da janela
        void QueueReapply(string motivo)
        {
            try
            {
                _remapear = true;   // topologia pode ter mudado: realinha DISPLAY1/DISPLAY2
                _settleLeft = 8; if (_settle != null) _settle.Start();
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => ReapplyAllQuiet(motivo)));
            }
            catch { }
        }

        // se o Win+P / hotplug trocou os nomes (DISPLAY1 vira DISPLAY2), realinha cada painel pelo MonitorId
        DateTime _ultimoRemap = DateTime.MinValue;
        bool _remapear;

        // acha a tela viva que corresponde a uma tela antiga (sem repetir tela já usada por outro painel)
        static DisplayScreen AcharTela(DisplayScreen refr, List<DisplayScreen> atual, List<DisplayScreen> usados)
        {
            if (!string.IsNullOrEmpty(refr.MonitorId))
                foreach (var d in atual)
                    if (!usados.Contains(d) && d.MonitorId == refr.MonitorId) return d;

            string k = Profile.HardwareKey(refr.MonitorId);
            if (k != "")
            {
                DisplayScreen achou = null; int n = 0;
                foreach (var d in atual)
                    if (!usados.Contains(d) && Profile.HardwareKey(d.MonitorId) == k) { achou = d; n++; }
                if (n == 1) return achou;
            }

            foreach (var d in atual)
                if (!usados.Contains(d) && d.DeviceName == refr.DeviceName) return d;

            return null;
        }

        // ---- arrastar cartões para reordenar (ordem salva no perfil) ----
        MonitorPanel _arrastando;

        public void IniciarArrasto(MonitorPanel p) { _arrastando = p; }

        public void ArrastarPainel(MonitorPanel p)
        {
            if (_arrastando != p || _flow == null) return;
            Point tela = Control.MousePosition;
            for (int i = 0; i < _flow.Controls.Count; i++)
            {
                var c = _flow.Controls[i];
                if (c == p) continue;
                var r = c.RectangleToScreen(c.ClientRectangle);
                if (tela.X < r.Left || tela.X > r.Right || tela.Y < r.Top || tela.Y > r.Bottom) continue;
                int atual = _flow.Controls.GetChildIndex(p);
                if (atual == i) return;
                _flow.Controls.SetChildIndex(p, i);
                _flow.PerformLayout();
                return;
            }
        }

        public void SoltarPainel(MonitorPanel p)
        {
            if (_arrastando != p) { _arrastando = null; return; }
            _arrastando = null;
            if (_flow == null) return;

            var nova = new List<MonitorPanel>();
            foreach (Control c in _flow.Controls)
            {
                var mp = c as MonitorPanel;
                if (mp != null) nova.Add(mp);
            }

            // nada mudou? não escreve nada no perfil
            bool mudou = false;
            for (int i = 0; i < nova.Count; i++)
                if (nova[i].Cal != null && nova[i].Cal.Ordem != i) mudou = true;
            if (!mudou) { _status.Text = ""; return; }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < nova.Count; i++)
            {
                nova[i].Cal.Ordem = i;
                sb.Append((i > 0 ? " -> " : "") + nova[i].MarcaTexto());
            }
            if (nova.Count == panels.Count) panels = nova;
            try
            {
                ProfileStore.Save(prof);
                _profStamp = System.IO.File.GetLastWriteTimeUtc(ProfileStore.ProfilePath);
            }
            catch { }
            ProfileStore.Log("ordem dos cartões salva: " + sb);
            _status.Text = "ordem salva.";
        }

        public void AbrirFiltro(MonitorPanel p)
        {
            if (p == null) return;
            try
            {
                using (var f = new FilterForm(p)) f.ShowDialog(this);
            }
            catch (Exception ex) { ProfileStore.Log("filtro: erro " + ex); }
        }

        public void AbrirOsd(MonitorPanel p)
        {
            if (p == null) return;
            try
            {
                var ja = OsdForm.AcharAberto(p.Display);
                if (ja != null) { ja.Activate(); return; }
                var ent = MonitorDb.Obter(p.Display, true, false);
                p.AplicarSuporte(ent);
                var f = new OsdForm(p.Display, p.Cal, ent, this);
                f.Show();
                ProfileStore.Log("OSD: janela aberta para " + (ent == null ? "?" : ent.Nome));
            }
            catch (Exception ex) { ProfileStore.Log("OSD: erro " + ex); }
        }

        // o OSD descobriu/confirmou controles: atualiza os cartões
        public void AtualizarSuportes()
        {
            foreach (var p in panels.ToArray())
            {
                try
                {
                    if (p.Display == null) continue;
                    p.AplicarSuporte(MonitorDb.PorChave(Profile.HardwareKey(p.Display.MonitorId)));
                }
                catch { }
            }
        }

        void AtualizarMapeamentoDeTelas()
        {
            try
            {
                var atual = Win.EnumerateDisplays();
                if (atual.Count == 0) return;
                Win.AttachRects(atual);
                prof.MapLive(atual, true);      // casa cada tela com a entrada do perfil (e conserta MonitorId/DeviceName)

                var usados = new List<DisplayScreen>();
                foreach (var p in panels) p.Ativa = false;
                foreach (var p in panels)
                {
                    var d = AcharTela(p.Display, atual, usados);
                    if (d == null) continue;    // monitor desligado / fora no momento
                    usados.Add(d);
                    p.Ativa = true;
                    if (p.Display.DeviceName != d.DeviceName)
                        ProfileStore.Log("remapeado: " + p.Cal.FriendlyName + " " + p.Display.DeviceName + " -> " + d.DeviceName);
                    p.Display = d;
                    if (d.PhysicalHandle == IntPtr.Zero) Win.AttachPhysical(d);
                    p.RefreshHeader();
                    if (d.Cal != null && d.Cal != p.Cal) { p.Cal = d.Cal; p.SyncSliders(); }
                    var entCache = MonitorDb.PorChave(Profile.HardwareKey(d.MonitorId));
                    if (entCache != null) p.AplicarSuporte(entCache);
                }

                // monitor ligado depois que o app abriu: ganha painel próprio (UI + vigia)
                foreach (var d in atual)
                {
                    if (usados.Contains(d)) continue;
                    Win.AttachPhysical(d);
                    var cal = d.Cal != null ? d.Cal : prof.GetByMonitorId(d.MonitorId, d.DeviceName);
                    var mp = new MonitorPanel(d, cal, prof, this);
                    var entNovo = MonitorDb.PorChave(Profile.HardwareKey(d.MonitorId));
                    if (entNovo != null) mp.AplicarSuporte(entNovo);
                    panels.Add(mp);
                    if (_flow != null) _flow.Controls.Add(mp);
                    ProfileStore.Log("painel criado para monitor que ligou depois: " + d.DeviceName + " (" + d.FriendlyName + ")");
                }

                displays = atual;
                ReordenarPaineis();
                if (_layoutN != panels.Count) AjustarTamanhoJanela();
            }
            catch (Exception ex) { ProfileStore.Log("remapear: " + ex.Message); }
        }

        // cartões na ordem que o usuário salvou (arrastando); se não houver, ordem física
        void ReordenarPaineis()
        {
            if (_flow == null || panels.Count < 2) return;
            var ord = new List<MonitorPanel>(panels);
            ord.Sort(delegate(MonitorPanel a, MonitorPanel b)
            {
                int oa = a.Cal != null ? a.Cal.Ordem : -1;
                int ob = b.Cal != null ? b.Cal.Ordem : -1;
                if ((oa >= 0 || ob >= 0) && oa != ob)
                {
                    if (oa < 0) oa = 9999;
                    if (ob < 0) ob = 9999;
                    return oa.CompareTo(ob);
                }
                if (a.Display.X != b.Display.X) return a.Display.X.CompareTo(b.Display.X);
                return a.Display.Y.CompareTo(b.Display.Y);
            });
            _flow.SuspendLayout();
            for (int i = 0; i < ord.Count; i++)
            {
                if (_flow.Controls.Contains(ord[i])) _flow.Controls.SetChildIndex(ord[i], i);
                panels[i] = ord[i];
            }
            _flow.ResumeLayout();
        }

        // com N telas a janela cresce o quanto der; se não couber, o miolo rola
        int _layoutN = -1;
        void AjustarTamanhoJanela()
        {
            if (_bar == null || _flow == null || panels.Count == 0) return;
            var wa = Screen.FromControl(this).WorkingArea;
            const int cardW = 532, cardH = 758;
            int cols = panels.Count;
            int maxW = wa.Width - 60;
            if (24 + cols * cardW + 24 > maxW) cols = Math.Max(1, (maxW - 48) / cardW);
            int rows = (panels.Count + cols - 1) / cols;
            int needW = 48 + cols * cardW;
            int needH = 60 + _bar.GetPreferredSize(new Size(needW, 0)).Height + 38 + rows * cardH;
            if (needW > wa.Width - 20) needW = wa.Width - 20;
            if (needH > wa.Height - 20) needH = wa.Height - 20;
            if (needW < 1030) needW = 1030;
            if (needH < 780) needH = 780;
            ClientSize = new Size(needW, needH);
            _layoutN = panels.Count;
            ProfileStore.Log("layout: " + panels.Count + " tela(s) -> " + cols + " coluna(s), janela " + needW + "x" + needH);
        }

        void AplicarVerificando(out int perdidas, out bool falhou)
        {
            perdidas = 0; falhou = false;
            foreach (var p in panels)
            {
                if (!p.Ativa) continue;
                try
                {
                    bool estava;
                    bool ok = p.ReapplyCurveQuiet(out estava);
                    if (!estava) perdidas++;
                    if (!ok) falhou = true;
                }
                catch { falhou = true; }
            }
        }

        // verifica a LUT de cada monitor e repõe só se alguma coisa apagou. Não mexe no que o usuário não pediu.
        void ReapplyAllQuiet(string motivo)
        {
            if (panels.Count == 0) return;
            RecarregarPerfilSeMudou();
            if (_remapear) { AtualizarMapeamentoDeTelas(); _remapear = false; }

            int perdidas; bool falhou;
            AplicarVerificando(out perdidas, out falhou);

            // escrever falhou? pode ser que o DISPLAYn mudou de nome (Win+P/hotplug) — reenumera e tenta 1x
            if (falhou && (DateTime.Now - _ultimoRemap).TotalSeconds > 300)
            {
                AtualizarMapeamentoDeTelas();
                _ultimoRemap = DateTime.Now;
                int p2; bool f2;
                AplicarVerificando(out p2, out f2);
                perdidas += p2; falhou = f2;
            }
            string hora = DateTime.Now.ToString("HH:mm:ss");
            if (perdidas > 0)
            {
                _statusAuto.Text = "cor reposta · " + hora;
                _statusAuto.ForeColor = Theme.Warn;
                ProfileStore.Log("vigia: LUT perdida em " + perdidas + " tela(s) por '" + motivo + "' — reposta");
            }
            else if (falhou)
            {
                _statusAuto.Text = "vigia falhou · " + hora;
                _statusAuto.ForeColor = Theme.Danger;
            }
            else
            {
                _statusAuto.Text = "cor ok · " + hora;
                _statusAuto.ForeColor = Theme.Good;
            }

            if (tray != null) tray.Text = "ColorMatch — cor ativa (verificado " + hora + ")";
        }

        bool IsAutoStart()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    var v = k == null ? null : (string)k.GetValue("ColorMatch");
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (on)
                    {
                        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        string arg = (prof != null && prof.InvisibleMode) ? " /invisivel" : " /min";
                        k.SetValue("ColorMatch", "\"" + exe + "\"" + arg);
                    }
                    else k.DeleteValue("ColorMatch", false);
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && WindowState != FormWindowState.Minimized)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                return;
            }
            // grava qualquer mudança que ainda estava esperando o tempo do "salvar sozinho"
            try { if (_saveT != null && _saveT.Enabled) { _saveT.Stop(); SaveProfile(false); } } catch { }
            tray.Visible = false;
            base.OnFormClosing(e);
        }
    }
}
