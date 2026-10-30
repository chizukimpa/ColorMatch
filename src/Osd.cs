// Osd.cs — ColorMatch: janela "OSD do monitor".
// Lista TODOS os controles DDC/CI que aquele monitor responde (nome MCCS, código,
// valor atual, máximo) e deixa mexer em cada um. O que o usuário muda fica no perfil.
// Os códigos que não respondem são escondidos e o banco de monitores se autocorrige.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ColorMatch
{
    public class OsdForm : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        static List<OsdForm> _abertos = new List<OsdForm>();
        public static void CloseAll()
        {
            var copia = _abertos.ToArray();
            _abertos.Clear();
            foreach (var f in copia) { try { f.Close(); } catch { } }
        }
        public static OsdForm AcharAberto(DisplayScreen ds)
        {
            foreach (var f in _abertos)
                if (ds != null && f._ds != null && f._ds.DeviceName == ds.DeviceName) return f;
            return null;
        }

        class Linha
        {
            public byte Code;
            public Label Nome;
            public Chip Valor;
            public ModernSlider Slider;
            public ModernButton Botao;
            public SelectBox Sel;
            public uint SelValor;
            public Label Nota;
            public bool Visivel = true;
            public bool Confirmado;
        }

        class ItemVcp
        {
            public uint Valor;
            public string Texto;
            public ItemVcp(uint v, string t) { Valor = v; Texto = t; }
            public override string ToString() { return Texto; }
        }
        // valores possíveis de um controle discreto, do capabilities do monitor


        DisplayScreen _ds;
        MonitorCal _cal;
        MonitorEntry _ent;
        MainForm _dono;

        Panel _lista;
        Label _status;
        Label _lblSub;
        System.Windows.Forms.Timer _tick;
        List<byte> _fila = new List<byte>();
        List<Linha> _linhas = new List<Linha>();
        Dictionary<byte, Linha> _porCodigo = new Dictionary<byte, Linha>();
        bool _carregando;
        bool _bancoMudou;
        int _restantes;

        const int LINHA = 40;

        // ordem dos "principais" no topo da lista
        static readonly byte[] Principais = new byte[] {
            0x10, 0x12, 0x16, 0x18, 0x1A, 0x6C, 0x6E, 0x70, 0x87, 0x8A, 0x90, 0x62,
            0x8D, 0x60, 0x14, 0x0C, 0x72, 0xDB, 0xD6, 0x86, 0xAA, 0xCC, 0xC0 };

        public OsdForm(DisplayScreen ds, MonitorCal cal, MonitorEntry ent, MainForm dono)
        {
            _ds = ds; _cal = cal; _ent = ent; _dono = dono;

            Text = "OSD do monitor";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            Font = Theme.F(9f);
            KeyPreview = true;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int altura = 720;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (altura > wa.Height - 60) altura = wa.Height - 60;
            ClientSize = new Size(730, altura);
            int k = _abertos.Count;
            Location = new Point(wa.Left + (wa.Width - Width) / 2 + k * 34,
                                 wa.Top + (wa.Height - Height) / 2 + k * 34);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            Build();
            _abertos.Add(this);
            Carregar();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
                int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                int border = 0x00382C25; DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)   // redimensionar puxando a borda
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

        Label MkLabel(string t, int x, int y, int w, Font f, Color c, Control pai)
        {
            var l = new Label();
            l.Text = t; l.Location = new Point(x, y); l.Size = new Size(w, 18);
            l.Font = f; l.ForeColor = c;
            l.BackColor = (pai == _lista) ? Theme.Card : Theme.Bg;
            l.AutoSize = false;
            pai.Controls.Add(l); return l;
        }

        ModernButton MkButton(string t, int x, int y, int w, int h, Control pai, bool accent)
        {
            var b = new ModernButton();
            b.Text = t; b.Size = new Size(w, h); b.Location = new Point(x, y);
            b.BackColor = (pai == _lista) ? Theme.Card : Theme.Bg;
            b.Accent = accent;
            pai.Controls.Add(b); return b;
        }

        void Build()
        {
            // ---- cabeçalho (arrasta a janela) ----
            var hdr = new Panel();
            hdr.Location = new Point(0, 0);
            hdr.Size = new Size(ClientSize.Width, 64);
            hdr.BackColor = Theme.Bg;
            Controls.Add(hdr);
            hdr.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
            };

            string nome = _ent != null ? _ent.Nome : (_ds == null ? "Monitor" : _ds.ToString());
            MkLabel(nome, 20, 10, 520, Theme.B(12.5f), Theme.Text, hdr);

            string sub = _ent == null ? "" : (_ent.Tipo == "" ? "" : _ent.Tipo.ToUpper() + " · ");
            if (_ent != null)
                sub += (_ent.Polegadas > 0 ? _ent.Polegadas.ToString("0.#") + "\" · " : "") +
                       (_ent.Mccs == "" ? "" : "MCCS " + _ent.Mccs + " · ") +
                       (_ent.Ano > 0 ? _ent.Ano + " · " : "") +
                       (_ent.Serial == "" ? "" : "sn " + _ent.Serial + " · ");
            if (_ds != null) sub += _ds.DeviceName;
            _lblSub = MkLabel(sub, 21, 34, 650, Theme.F(8.25f), Theme.Muted, hdr);

            var fechar = new WinButton();
            fechar.Kind = 1;
            fechar.Location = new Point(hdr.Width - 42, 14);
            hdr.Controls.Add(fechar);
            fechar.Click += (s, e) => Close();

            // ---- barra de ações ----
            var btLer = MkButton("Ler tudo de novo", 20, 74, 152, 32, this, false);
            btLer.Click += (s, e) => RecarregarTudo();

            var btRepor = MkButton("Repor valores do perfil", 180, 74, 186, 32, this, false);
            btRepor.Click += (s, e) => ReporDoPerfil();

            var btZero = MkButton("Zerar extras do perfil", 374, 74, 176, 32, this, false);
            btZero.Click += (s, e) =>
            {
                _cal.Extras.Clear();
                if (_dono != null) _dono.ProfileChanged();
                _status.Text = "extras apagados do perfil.";
                _status.ForeColor = Theme.Muted;
            };

            _status = MkLabel("lendo o monitor…", 560, 82, 160, Theme.F(8.25f), Theme.Muted, this);

            // ---- lista de controles ----
            _lista = new Panel();
            _lista.Location = new Point(14, 116);
            _lista.Size = new Size(ClientSize.Width - 28, ClientSize.Height - 130);
            _lista.BackColor = Theme.Card;
            _lista.AutoScroll = true;
            _lista.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_lista);

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 45;
            _tick.Tick += (s, e) => LerProximo();

            ConstruirLinhas();
        }

        // relê do zero, inclusive os códigos que já tinham falhado
        void RecarregarTudo()
        {
            if (_tick != null) _tick.Stop();
            if (_ent != null) _ent.Ausentes = new List<string>();
            _lista.Controls.Clear();
            _linhas.Clear();
            _porCodigo.Clear();
            ConstruirLinhas();
            Carregar();
        }

        // principais primeiro (na ordem da lista), depois o resto em ordem de código
        List<byte> OrdemDosCodigos()
        {
            var todos = new List<byte>();
            if (_ent != null && _ent.Vcps != null)
                foreach (var s in _ent.Vcps)
                {
                    byte c;
                    if (byte.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out c))
                        if (!todos.Contains(c)) todos.Add(c);
                }
            // soma os candidatos: é assim que o monitor novo é descoberto (o que não responder some)
            foreach (var c in Win.VcpCandidatos)
                if (!todos.Contains(c) && !(_ent != null && _ent.Ausente(c))) todos.Add(c);
            todos.Sort();

            var fim = new List<byte>(todos);
            var ini = new List<byte>();
            foreach (var c in Principais)
                if (fim.Remove(c)) ini.Add(c);
            ini.AddRange(fim);
            return ini;
        }

        void ConstruirLinhas()
        {
            foreach (var code in OrdemDosCodigos())
            {
                bool leitura = Win.VcpEhLeitura(code);
                bool acao = Win.VcpEhAcao(code);
                var vals = Win.CapValores(_ent != null ? _ent.Capacidades : "", code);

                var l = new Linha();
                l.Code = code;
                l.Nome = MkLabel(Win.VcpNome(code) + "   0x" + code.ToString("X2"),
                    16, 0, 232, Theme.F(8.75f), Theme.Text, _lista);

                if (acao)
                {
                    // valor tabelado de 1 clique (restaurar padrão etc.)
                    l.Valor = new Chip();
                    l.Valor.Size = new Size(80, 26);
                    l.Valor.BackColor = Theme.Card;
                    l.Valor.Text = "—";
                    _lista.Controls.Add(l.Valor);

                    l.Botao = MkButton("Executar", 352, 0, 96, 28, _lista, false);
                    l.Botao.Font = Theme.F(8.25f);
                    var cc = code;
                    l.Botao.Click += (s, e) => ExecutarAcao(cc);
                }
                else if (Win.VcpEhEnum(code) || vals.Count >= 2)
                {
                    // valor discreto: combo com o que o monitor declarou (entrada, preset, mudo, energia...)
                    if (vals.Count >= 2)
                    {
                        var sb = new SelectBox();
                        sb.Location = new Point(256, 0);
                        sb.Size = new Size(352, 28);
                        sb.BackColor = Theme.Card;
                        sb.Text = "…";
                        _lista.Controls.Add(sb);
                        l.Sel = sb;
                        var cc = code;
                        var ll = l;
                        sb.Click += (s, e) => AbrirMenuSel(ll, cc, vals);
                    }
                    else l.Nota = MkLabel("ajuste no OSD do monitor", 352, 0, 240, Theme.F(8.25f), Theme.Muted, _lista);
                }
                else if (leitura)
                {
                    l.Valor = new Chip();
                    l.Valor.Size = new Size(80, 26);
                    l.Valor.BackColor = Theme.Card;
                    l.Valor.Text = "…";
                    _lista.Controls.Add(l.Valor);
                    l.Nota = MkLabel("somente leitura", 352, 0, 200, Theme.F(8.25f), Theme.Muted, _lista);
                }
                else
                {
                    l.Valor = new Chip();
                    l.Valor.Size = new Size(80, 26);
                    l.Valor.BackColor = Theme.Card;
                    l.Valor.Text = "…";
                    _lista.Controls.Add(l.Valor);

                    l.Slider = new ModernSlider();
                    l.Slider.Size = new Size(256, 28);
                    l.Slider.Minimum = 0;
                    l.Slider.Maximum = 100;
                    l.Slider.BackColor = Theme.Card;
                    l.Slider.Enabled = false;
                    _lista.Controls.Add(l.Slider);
                    var cc = code;
                    var ll = l;
                    l.Slider.ValueChanged += (s, e) =>
                    {
                        if (_carregando) return;
                        Escrever(cc, ll.Slider.Value);
                    };
                }

                // o perfil já manda algum valor pra este código?
                int g = _cal.GetExtra(code);
                if (g >= 0 && l.Valor != null) l.Valor.Text = g.ToString();

                _linhas.Add(l);
                _porCodigo[code] = l;
            }

            Relayout();
        }

        void Relayout()
        {
            int y = 6;
            foreach (var l in _linhas)
            {
                if (!l.Visivel) continue;
                if (l.Nome != null) l.Nome.Location = new Point(16, y + 11);
                if (l.Valor != null) l.Valor.Location = new Point(256, y + 7);
                if (l.Sel != null) l.Sel.Location = new Point(256, y + 6);
                if (l.Slider != null) l.Slider.Location = new Point(352, y + 5);
                if (l.Botao != null) l.Botao.Location = new Point(352, y + 5);
                if (l.Nota != null) l.Nota.Location = new Point(352, y + 11);
                y += LINHA;
            }
        }

        void EsconderLinha(Linha l)
        {
            l.Visivel = false;
            if (l.Nome != null) l.Nome.Visible = false;
            if (l.Valor != null) l.Valor.Visible = false;
            if (l.Sel != null) l.Sel.Visible = false;
            if (l.Slider != null) l.Slider.Visible = false;
            if (l.Botao != null) l.Botao.Visible = false;
            if (l.Nota != null) l.Nota.Visible = false;
        }

        // ---------- leitura em fila (um por tick, não trava a janela) ----------
        public void Carregar()
        {
            _fila.Clear();
            foreach (var l in _linhas) if (l.Visivel) _fila.Add(l.Code);
            _restantes = _fila.Count;
            _status.ForeColor = Theme.Muted;
            _status.Text = "lendo " + _fila.Count + "…";
            if (_fila.Count == 0) { _status.Text = "sem controles."; return; }
            if (_tick != null && !_tick.Enabled) _tick.Start();
        }

        void LerProximo()
        {
            if (_fila.Count == 0) { Terminou(); return; }
            byte code = _fila[0];
            _fila.RemoveAt(0);
            _restantes = _fila.Count;

            Linha l;
            _porCodigo.TryGetValue(code, out l);

            uint cur = 0, mx = 0;
            var h = HandleDdc();
            bool ok = h != IntPtr.Zero && Win.ReadVCP(h, code, out cur, out mx);

            if (!ok)
            {
                // escrita-só (restaurar padrão, auto ajuste...) não responde leitura: fica na lista
                if (l != null && l.Botao != null) l.Confirmado = true;
                else if (l != null) EsconderLinha(l);
            }
            else if (l != null)
            {
                l.Confirmado = true;
                _carregando = true;
                string amig = Win.VcpValorNome(code, cur);
                if (l.Valor != null)
                {
                    l.Valor.Text = amig != null ? amig : cur.ToString();
                    l.Valor.Font = Theme.B(l.Valor.Text.Length > 9 ? 8f : 9.25f);
                }
                if (l.Sel != null)
                {
                    l.SelValor = cur;
                    l.Sel.Text = amig != null ? amig : cur.ToString();
                }
                if (l.Slider != null)
                {
                    if (mx > 0 && mx <= 4096)
                    {
                        l.Slider.Maximum = (int)mx;
                        if (cur <= mx) l.Slider.Value = (int)cur;
                        l.Slider.Enabled = true;
                    }
                    else l.Slider.Enabled = false;
                }
                _carregando = false;
            }

            if (_restantes > 0) _status.Text = "lendo " + _restantes + "…";
            else Terminou();
        }

        void Terminou()
        {
            if (_tick != null) _tick.Stop();
            Relayout();

            // o banco aprende: o que respondeu fica, o que não respondeu fica marcado
            if (_ent != null)
            {
                var respondeu = new List<byte>();
                var ausentes = new List<byte>();
                foreach (var l in _linhas)
                {
                    if (l.Confirmado) respondeu.Add(l.Code);
                    else ausentes.Add(l.Code);
                }
                // se quase nada respondeu, pode ser monitor dormindo/OSD aberto: não grava
                if (respondeu.Count >= 3)
                {
                    var antes = _ent.Vcps == null ? 0 : _ent.Vcps.Count;
                    _ent.Vcps = new List<string>();
                    respondeu.Sort();
                    foreach (var c in respondeu) _ent.Vcps.Add(c.ToString("X2"));
                    _ent.Ausentes = new List<string>();
                    foreach (var c in ausentes) _ent.Ausentes.Add(c.ToString("X2"));
                    if (antes != _ent.Vcps.Count || _bancoMudou)
                    {
                        MonitorDb.Salvar();
                        ProfileStore.Log("OSD " + _ent.Nome + ": " + _ent.Vcps.Count + " controles confirmados");
                    }
                    if (_lblSub != null)
                        _lblSub.Text = (_ent.Tipo == "" ? "" : _ent.Tipo.ToUpper() + " · ") +
                            (_ent.Polegadas > 0 ? _ent.Polegadas.ToString("0.#") + "\" · " : "") +
                            (_ent.Mccs == "" ? "" : "MCCS " + _ent.Mccs + " · ") +
                            (_ent.Ano > 0 ? _ent.Ano + " · " : "") +
                            (_ent.Serial == "" ? "" : "sn " + _ent.Serial + " · ") +
                            (_ds != null ? _ds.DeviceName : "");
                }
            }
            _status.Text = "pronto.";
            _status.ForeColor = Theme.Good;
            if (_dono != null) { try { _dono.AtualizarSuportes(); } catch { } }
        }

        IntPtr HandleDdc()
        {
            if (_ds == null) return IntPtr.Zero;
            if (_ds.PhysicalHandle == IntPtr.Zero) Win.AttachPhysical(_ds);
            return _ds.PhysicalHandle;
        }

        void Escrever(byte code, int valor)
        {
            var h = HandleDdc();
            if (h == IntPtr.Zero) { _status.Text = "sem handle DDC."; return; }
            bool ok = Win.WriteVCP(h, code, (uint)valor);
            if (ok) _cal.SetExtra(code, valor);
            _bancoMudou = true;
            Linha l;
            if (_porCodigo.TryGetValue(code, out l))
            {
                if (l.Valor != null)
                {
                    l.Valor.Text = valor.ToString();
                    l.Valor.Font = Theme.B(l.Valor.Text.Length > 9 ? 8f : 9.25f);
                }
                if (l.Sel != null)
                {
                    string nm = Win.VcpValorNome(code, (uint)valor);
                    l.SelValor = (uint)valor;
                    l.Sel.Text = nm != null ? nm : valor.ToString();
                }
            }
            _status.Text = ok ? ("0x" + code.ToString("X2") + " = " + valor + " · no perfil")
                              : ("0x" + code.ToString("X2") + ": o monitor recusou.");
            _status.ForeColor = ok ? Theme.Good : Theme.Warn;
            if (_dono != null) _dono.ProfileChanged();
        }

        // menu escuro com os valores que o monitor aceita para aquele código
        void AbrirMenuSel(Linha l, byte code, List<uint> vals)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Theme.Card2;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;
            menu.Font = Theme.F(9f);
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuCores());

            foreach (var v in vals)
            {
                string nm = Win.VcpValorNome(code, v);
                var mi = new ToolStripMenuItem((v == l.SelValor ? "• " : "   ") +
                    (nm != null ? nm : ("valor " + v)) + "    (" + v + ")");
                mi.ForeColor = v == l.SelValor ? Theme.Accent : Theme.Text;
                mi.Tag = v;
                var val = v;
                mi.Click += (s, e) => EscolherEnum(code, val);
                menu.Items.Add(mi);
            }
            // deixa o menu com a mesma largura da caixinha, sem cortar texto
            int altItens = 0;
            foreach (ToolStripItem it in menu.Items) altItens += it.GetPreferredSize(Size.Empty).Height;
            menu.AutoSize = false;
            menu.Size = new Size(Math.Max(240, l.Sel.Width), altItens + 10);
            menu.Show(l.Sel, new Point(0, l.Sel.Height + 4));
        }

        void EscolherEnum(byte code, uint valor)
        {
            if (Win.VcpPerigoso(code))
            {
                var r = MessageBox.Show(
                    "Mudar \"" + Win.VcpNome(code) + "\" pode deixar esta tela sem imagem\n" +
                    "até você mexer no botão do próprio monitor.\n\nContinuar?",
                    "OSD do monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            Escrever(code, (int)valor);
            // confere no monitor o que ele realmente aceitou
            LerUm(code);
        }

        // relê só um código (depois de mexer num valor discreto)
        void LerUm(byte code)
        {
            uint cur = 0, mx = 0;
            var h = HandleDdc();
            if (h == IntPtr.Zero || !Win.ReadVCP(h, code, out cur, out mx)) return;
            Linha l;
            if (!_porCodigo.TryGetValue(code, out l)) return;

            bool antes = _carregando;
            _carregando = true;
            string amig = Win.VcpValorNome(code, cur);
            if (l.Sel != null) { l.SelValor = cur; l.Sel.Text = amig != null ? amig : cur.ToString(); }
            if (l.Valor != null)
            {
                l.Valor.Text = amig != null ? amig : cur.ToString();
                l.Valor.Font = Theme.B(l.Valor.Text.Length > 9 ? 8f : 9.25f);
            }
            if (l.Slider != null && mx > 0 && mx <= 4096)
            {
                l.Slider.Maximum = (int)mx;
                if (cur <= mx) l.Slider.Value = (int)cur;
            }
            _carregando = antes;
        }

        void ExecutarAcao(byte code)
        {
            var h = HandleDdc();
            if (h == IntPtr.Zero) return;
            Win.WriteVCP(h, code, 1);
            _status.Text = "executado: " + Win.VcpNome(code);
            _status.ForeColor = Theme.Warn;
            System.Threading.Thread.Sleep(500);
            Carregar();
        }

        // reescreve no monitor o que o perfil manda (os 5 principais + extras guardados)
        public void ReporDoPerfil()
        {
            var h = HandleDdc();
            if (h == IntPtr.Zero) return;
            Win.WriteVCP(h, Win.VCP_BRIGHTNESS, (uint)_cal.Brightness);
            Win.WriteVCP(h, Win.VCP_CONTRAST, (uint)_cal.Contrast);
            Win.WriteVCP(h, Win.VCP_RED_GAIN, (uint)_cal.RedGain);
            Win.WriteVCP(h, Win.VCP_GREEN_GAIN, (uint)_cal.GreenGain);
            Win.WriteVCP(h, Win.VCP_BLUE_GAIN, (uint)_cal.BlueGain);
            int n = Win.ApplyExtras(h, _cal.Extras);
            _status.Text = "perfil reposto (" + (5 + n) + " valores).";
            _status.ForeColor = Theme.Good;
            Carregar();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _abertos.Remove(this);
            if (_tick != null) _tick.Stop();
            base.OnFormClosed(e);
        }
    }
}
