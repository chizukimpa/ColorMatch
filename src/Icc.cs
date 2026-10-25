// Icc.cs — lê perfis ICC de monitor e transforma em predefinições do ColorMatch.
//
// O que dá para reproduzir numa LUT por canal é a CURVA (TRC) do perfil. A ideia é
// a mesma que o macOS faz ao exibir conteúdo sRGB num monitor cujo perfil diz que a
// curva é outra (ex.: gama 1.96): o sinal enviado é invTRC(sRGB_linear(p)).
// As primárias (gamut) não são reproduzíveis por LUT — só a curva e os ganhos.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ColorMatch
{
    public class IccPerfil
    {
        public string Nome = "";
        public string Caminho = "";
        public string Detalhe = "";
        public double Gamma = 1.0;
        public double BrightMult = 1.0;
        public double BlackLift = 0.0;
        public double GainR = 1.0, GainG = 1.0, GainB = 1.0;
        public bool TemVcgt = false;
        public string Erro = "";

        public bool Ok { get { return Erro == ""; } }
    }

    public static class Icc
    {
        public static string Dir
        {
            get
            {
                string p = Path.Combine(ProfileStore.Dir, "icc");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        public static List<IccPerfil> Listar()
        {
            var l = new List<IccPerfil>();
            try
            {
                foreach (var f in Directory.GetFiles(Dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".icc" && ext != ".icm") continue;
                    var p = Ler(f);
                    if (p != null && p.Ok) l.Add(p);
                }
            }
            catch { }
            l.Sort(delegate(IccPerfil a, IccPerfil b)
            {
                return string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase);
            });
            return l;
        }

        // copia para a pasta do app (sobrescreve se já existir um com o mesmo nome)
        public static string Importar(string origem)
        {
            var p = Ler(origem);
            if (p == null || !p.Ok)
                throw new Exception(p == null ? "Perfil ICC inválido." : p.Erro);
            string nome = NomeArquivo(p.Nome);
            string destino = Path.Combine(Dir, nome + ".icc");
            File.Copy(origem, destino, true);
            return destino;
        }

        static string NomeArquivo(string s)
        {
            if (string.IsNullOrEmpty(s)) s = "Perfil ICC";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
            s = s.Trim();
            while (s.IndexOf("  ") >= 0) s = s.Replace("  ", " ");
            if (s.Length > 80) s = s.Substring(0, 80).Trim();
            return s == "" ? "Perfil ICC" : s;
        }

        // ---------------- leitura ----------------

        public static IccPerfil Ler(string caminho)
        {
            var icc = new IccPerfil();
            icc.Caminho = caminho;
            try
            {
                byte[] d = File.ReadAllBytes(caminho);
                if (d.Length < 132 || d[36] != 'a' || d[37] != 'c' || d[38] != 's' || d[39] != 'p')
                { icc.Erro = "não é um perfil ICC."; return icc; }

                icc.Nome = Descricao(d);
                if (string.IsNullOrEmpty(icc.Nome)) icc.Nome = Path.GetFileNameWithoutExtension(caminho);

                var tags = Tags(d);
                if (!tags.ContainsKey("rTRC") || !tags.ContainsKey("gTRC") || !tags.ContainsKey("bTRC"))
                {
                    if (!tags.ContainsKey("kTRC")) { icc.Erro = "perfil sem curva de tons (TRC)."; return icc; }
                }
                bool cinza = !tags.ContainsKey("rTRC");
                Trc tr, tg, tb;
                if (cinza)
                {
                    tr = tg = tb = Trc.Ler(d, tags["kTRC"][0]);
                }
                else
                {
                    tr = Trc.Ler(d, tags["rTRC"][0]);
                    tg = Trc.Ler(d, tags["gTRC"][0]);
                    tb = Trc.Ler(d, tags["bTRC"][0]);
                }
                if (tr == null || tg == null || tb == null) { icc.Erro = "curva de tons não suportada."; return icc; }

                // gama aparente do perfil (informativo, ex.: 1.96)
                double gy = tg.Eval(0.5);
                double gamaPerfil = (gy > 0 && gy < 1) ? Math.Log(gy) / Math.Log(0.5) : 0;

                // curva que o macOS manda para a tela: invTRC(sRGB_linear(p))
                double[] fr = new double[256], fg = new double[256], fb = new double[256];
                for (int i = 0; i < 256; i++)
                {
                    double p = i / 255.0;
                    double lin = SrgbParaLinear(p);
                    fr[i] = tr.Inv(lin);
                    fg[i] = tg.Inv(lin);
                    fb[i] = tb.Inv(lin);
                }

                double black = fg[0];
                double bright = fg[255];
                if (bright < 0.05) bright = 1.0;
                double mid = (fg[128] - black) / Math.Max(0.05, bright - black);
                if (mid <= 0.001) mid = 0.5;
                double gammaApp = Math.Log(0.5) / Math.Log(mid);
                if (double.IsNaN(gammaApp) || double.IsInfinity(gammaApp)) gammaApp = 1.0;

                icc.Gamma = Arred(Clamp(gammaApp, 0.40, 2.60), 2);
                icc.BrightMult = Arred(Clamp(bright, 0.85, 1.15), 2);
                icc.BlackLift = Arred(Clamp(black, 0.0, 0.05), 3);

                double refg = fg[191];
                if (refg > 0.02)
                {
                    icc.GainR = Arred(Clamp(fr[191] / refg, 0.85, 1.15), 2);
                    icc.GainG = 1.0;
                    icc.GainB = Arred(Clamp(fb[191] / refg, 0.85, 1.15), 2);
                }

                icc.TemVcgt = tags.ContainsKey("vcgt");

                string det = "γ" + gamaPerfil.ToString("0.00", CultureInfo.InvariantCulture);
                if (icc.TemVcgt) det += " · vcgt ignorado";
                icc.Detalhe = det;
            }
            catch (Exception e)
            {
                icc.Erro = e.Message;
            }
            return icc;
        }

        static double Arred(double v, int casas)
        {
            double f = Math.Pow(10, casas);
            return Math.Round(v * f) / f;
        }

        static double Clamp(double v, double a, double b)
        {
            if (v < a) return a;
            if (v > b) return b;
            return v;
        }

        // sRGB (codificado) -> luz linear
        static double SrgbParaLinear(double p)
        {
            if (p <= 0.04045) return p / 12.92;
            return Math.Pow((p + 0.055) / 1.055, 2.4);
        }

        static Dictionary<string, int[]> Tags(byte[] d)
        {
            var t = new Dictionary<string, int[]>(StringComparer.Ordinal);
            int n = BE32(d, 128);
            for (int i = 0; i < n; i++)
            {
                int o = 132 + i * 12;
                if (o + 12 > d.Length) break;
                string sig = "" + (char)d[o] + (char)d[o + 1] + (char)d[o + 2] + (char)d[o + 3];
                int off = BE32(d, o + 4);
                int len = BE32(d, o + 8);
                if (off > 0 && off < d.Length) t[sig] = new int[] { off, len };
            }
            return t;
        }

        static string Descricao(byte[] d)
        {
            var tags = Tags(d);
            if (!tags.ContainsKey("desc")) return "";
            int o = tags["desc"][0];
            string sig = "" + (char)d[o] + (char)d[o + 1] + (char)d[o + 2] + (char)d[o + 3];
            if (sig == "desc")
            {
                int len = BE32(d, o + 8);
                if (len <= 0 || len > 512) return "";
                return Encoding.ASCII.GetString(d, o + 12, len).TrimEnd('\0', ' ');
            }
            if (sig == "mluc")
            {
                int cnt = BE32(d, o + 8);
                int rec = BE32(d, o + 12);
                string best = "";
                for (int i = 0; i < cnt && i < 16; i++)
                {
                    int b = o + 16 + i * rec;
                    if (b + 12 > d.Length) break;
                    string lang = "" + (char)d[b] + (char)d[b + 1];
                    int ln = BE32(d, b + 4);
                    int of = BE32(d, b + 8);
                    if (ln <= 0 || of + ln > d.Length) continue;
                    string s = Encoding.BigEndianUnicode.GetString(d, o + of, ln).TrimEnd('\0');
                    if (lang == "en" || best == "") best = s;
                    if (lang == "en") break;
                }
                return best;
            }
            return "";
        }

        static int BE32(byte[] d, int o)
        {
            return (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
        }

        static int BE16(byte[] d, int o)
        {
            return (d[o] << 8) | d[o + 1];
        }

        static double S15(byte[] d, int o)
        {
            return BE32(d, o) / 65536.0;
        }

        // ---------------- curva de tons (TRC) ----------------

        class Trc
        {
            int _tipo;               // 0 = curv, 1..5 = para tipo 0..4
            double _g;
            double[] _tab;
            double _a, _b, _c, _d, _e, _f;

            public static Trc Ler(byte[] d, int o)
            {
                if (o + 12 > d.Length) return null;
                string sig = "" + (char)d[o] + (char)d[o + 1] + (char)d[o + 2] + (char)d[o + 3];
                var t = new Trc();
                if (sig == "curv")
                {
                    int cnt = BE32(d, o + 8);
                    if (cnt == 0) { t._tipo = 0; t._g = 1.0; t._tab = null; return t; }
                    if (cnt == 1)
                    {
                        t._tipo = 0;
                        t._g = BE16(d, o + 12) / 256.0;
                        t._tab = null;
                        return t;
                    }
                    if (o + 12 + cnt * 2 > d.Length) return null;
                    t._tipo = 9;
                    t._tab = new double[cnt];
                    for (int i = 0; i < cnt; i++)
                        t._tab[i] = BE16(d, o + 12 + i * 2) / 65535.0;
                    return t;
                }
                if (sig == "para")
                {
                    int tp = BE16(d, o + 8);
                    if (tp < 0 || tp > 4) return null;
                    t._tipo = tp + 1;
                    t._g = S15(d, o + 12);
                    t._a = S15(d, o + 16);
                    t._b = S15(d, o + 20);
                    t._c = S15(d, o + 24);
                    t._d = S15(d, o + 28);
                    t._e = S15(d, o + 32);
                    t._f = S15(d, o + 36);
                    return t;
                }
                return null;
            }

            public double Eval(double x)
            {
                if (x <= 0) return 0;
                if (x >= 1) return 1;
                if (_tipo == 0) return Math.Pow(x, _g);
                if (_tipo == 9)
                {
                    double pos = x * (_tab.Length - 1);
                    int i = (int)pos;
                    if (i >= _tab.Length - 1) return _tab[_tab.Length - 1];
                    double fr = pos - i;
                    return _tab[i] * (1 - fr) + _tab[i + 1] * fr;
                }
                if (_tipo == 1) return Math.Pow(Clamp(x, 0, 1), _g);
                if (_tipo == 2) return Math.Pow(Clamp(_a * x + _b, 0, 1), _g);
                if (_tipo == 3) return Math.Pow(Clamp(_a * x + _b, 0, 1), _g) + _c;
                if (_tipo == 4)
                {
                    if (x >= _d) return Math.Pow(Clamp(_a * x + _b, 0, 1), _g);
                    return _c * x;
                }
                // tipo 4 do ICC (para tipo 4): e na sobra, f na reta
                if (x >= _d) return Math.Pow(Clamp(_a * x + _b, 0, 1), _g) + _e;
                return _c * x + _f;
            }

            public double Inv(double y)
            {
                if (y <= 0) return 0;
                if (y >= 1) return 1;
                double lo = 0, hi = 1;
                for (int i = 0; i < 50; i++)
                {
                    double mid = (lo + hi) / 2;
                    if (Eval(mid) < y) lo = mid; else hi = mid;
                }
                return (lo + hi) / 2;
            }
        }
    }
}
