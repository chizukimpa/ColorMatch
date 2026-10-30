// Profiles.cs — ColorMatch: modelo de calibração por monitor + persistência XML
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace ColorMatch
{
    public class VcpValor
    {
        public byte Codigo;
        public int Valor;
        public VcpValor() { }
        public VcpValor(byte c, int v) { Codigo = c; Valor = v; }
    }

    public class MonitorCal
    {
        public string MonitorId = "";   // MONITOR\AOCB501\{...}\0002
        public string DeviceName = "";  // \\.\DISPLAY1
        public string FriendlyName = "";

        // posição do cartão no layout (arrastar e soltar) — -1 = ainda não definida
        public int Ordem = -1;

        // DDC/CI (0..100)
        public int Brightness = 50;
        public int Contrast = 50;
        public int RedGain = 50;
        public int GreenGain = 50;
        public int BlueGain = 50;

        // Curva LUT
        public double Gamma = 1.0;      // >1 clareia meio-tom (2.2 = correção padrão)
        public double BrightMult = 1.0; // multiplicador de luminância
        public double BlackLift = 0.0;  // levanta preto 0..0.08
        public double GainR = 1.0;
        public double GainG = 1.0;
        public double GainB = 1.0;

        // filtro de cor (aplicado em cima dos ganhos calibrados)
        public double Temp = 0.0;       // -1 frio (azul) .. +1 quente (vermelho)
        public double Tint = 0.0;       // -1 verde .. +1 magenta
        public int Saturation = -1;     // DDC VCP 0x8A; -1 = não aplicado / sem suporte

        // modo de tela (0 = não mexer)
        public int Largura = 0;
        public int Altura = 0;
        public int TaxaHz = 0;

        [System.Xml.Serialization.XmlIgnore]
        public double GainREfetivo { get { return GainR * (1.0 + 0.15 * Temp + 0.04 * Tint); } }
        [System.Xml.Serialization.XmlIgnore]
        public double GainGEfetivo { get { return GainG * (1.0 - 0.12 * Tint); } }
        [System.Xml.Serialization.XmlIgnore]
        public double GainBEffetivo { get { return GainB * (1.0 - 0.15 * Temp + 0.04 * Tint); } }

        public short[] Ramp()
        {
            return Win.BuildRamp(Gamma, BrightMult, BlackLift, GainREfetivo, GainGEfetivo, GainBEffetivo);
        }

        // outros controles do OSD (DDC/CI) que o usuário mexeu: código VCP -> valor
        public List<VcpValor> Extras = new List<VcpValor>();

        public int GetExtra(byte code)
        {
            if (Extras == null) return -1;
            foreach (var v in Extras) if (v.Codigo == code) return v.Valor;
            return -1;
        }

        // copia só os valores de calibração (não mexe em MonitorId/DeviceName/Ordem)
        public void CopyValuesFrom(MonitorCal c)
        {
            if (c == null) return;
            Brightness = c.Brightness; Contrast = c.Contrast;
            RedGain = c.RedGain; GreenGain = c.GreenGain; BlueGain = c.BlueGain;
            Gamma = c.Gamma; BrightMult = c.BrightMult; BlackLift = c.BlackLift;
            GainR = c.GainR; GainG = c.GainG; GainB = c.GainB;
            Temp = c.Temp; Tint = c.Tint; Saturation = c.Saturation;
            Largura = c.Largura; Altura = c.Altura; TaxaHz = c.TaxaHz;
            Extras = new List<VcpValor>();
            if (c.Extras != null)
                foreach (var v in c.Extras) Extras.Add(new VcpValor(v.Codigo, v.Valor));
        }

        public void SetExtra(byte code, int valor)
        {
            if (Extras == null) Extras = new List<VcpValor>();
            foreach (var v in Extras) if (v.Codigo == code) { v.Valor = valor; return; }
            Extras.Add(new VcpValor(code, valor));
        }

        [System.Xml.Serialization.XmlIgnore]
        public bool Nova = false;      // entrada criada agora (monitor nunca calibrado): adota o DDC que está nele
    }

    public class Profile
    {
        public string Name = "Perfil";
        public string Id = "";               // "" = perfil de trabalho (profile.xml); senão é um perfil da biblioteca
        public bool Editavel = false;        // false = original protegido (editar cria cópia); true = cópia de trabalho
        public bool UseSoftwareCurve = true;
        public bool InvisibleMode = false;   // sem ícone na bandeja (rodar 100% invisível)
        public bool AutoStart = false;       // inicia junto com o Windows (repara a entrada do registro se sumir)
        public List<MonitorCal> Monitors = new List<MonitorCal>();

        // cópia independente (usada em "salvar como novo perfil")
        public Profile Clone()
        {
            var n = new Profile();
            n.Name = Name; n.Id = Id; n.UseSoftwareCurve = UseSoftwareCurve;
            n.InvisibleMode = InvisibleMode; n.AutoStart = AutoStart; n.Editavel = Editavel;
            n.Monitors = new List<MonitorCal>();
            foreach (var m in Monitors)
            {
                var c = new MonitorCal();
                c.MonitorId = m.MonitorId; c.DeviceName = m.DeviceName; c.FriendlyName = m.FriendlyName;
                c.Ordem = m.Ordem;
                c.CopyValuesFrom(m);
                n.Monitors.Add(c);
            }
            return n;
        }

        // procura a entrada deste monitor SEM criar uma nova (Match != GetByMonitorId)
        public MonitorCal Match(string monitorId, string deviceName)
        {
            if (!string.IsNullOrEmpty(monitorId))
                foreach (var m in Monitors)
                    if (m.MonitorId == monitorId) return m;

            string k = HardwareKey(monitorId);
            if (k != "")
            {
                MonitorCal achou = null; int n = 0;
                foreach (var m in Monitors)
                    if (HardwareKey(m.MonitorId) == k) { achou = m; n++; }
                if (n == 1) return achou;
            }

            if (!string.IsNullOrEmpty(deviceName))
                foreach (var m in Monitors)
                    if (m.DeviceName == deviceName) return m;

            return null;
        }

        // chave estável do monitor, sem o sufixo de instância ("MONITOR\AOCB501\{...}\0002" -> "MONITOR\AOCB501\{...}")
        public static string HardwareKey(string monitorId)
        {
            if (string.IsNullOrEmpty(monitorId)) return "";
            int i = monitorId.LastIndexOf('\\');
            if (i > 0 && monitorId.Length - i == 5)
            {
                bool soDigitos = true;
                for (int k = i + 1; k < monitorId.Length; k++)
                    if (!char.IsDigit(monitorId[k])) { soDigitos = false; break; }
                if (soDigitos) return monitorId.Substring(0, i);
            }
            return monitorId;
        }

        // casa as telas vivas com as entradas do perfil SEM roubar a entrada do vizinho.
        // DISPLAY1/DISPLAY2 trocam de nome a cada Win+P, então a ordem é:
        //   1) MonitorId exato  2) chave de hardware (sobrevive à troca de DISPLAYn)
        //   3) DeviceName  4) entrada nova
        public void MapLive(List<DisplayScreen> live, bool criarNovas)
        {
            var usados = new List<MonitorCal>();
            foreach (var d in live) d.Cal = null;

            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var d in live)
                {
                    if (d.Cal != null) continue;
                    MonitorCal m = null;
                    if (pass == 0 && !string.IsNullOrEmpty(d.MonitorId))
                        foreach (var x in Monitors)
                            if (!usados.Contains(x) && x.MonitorId == d.MonitorId) { m = x; break; }
                    if (pass == 1)
                    {
                        string k = HardwareKey(d.MonitorId);
                        if (k != "")
                        {
                            MonitorCal achou = null; int n = 0;
                            foreach (var x in Monitors)
                                if (!usados.Contains(x) && HardwareKey(x.MonitorId) == k) { achou = x; n++; }
                            if (n == 1) m = achou;
                        }
                    }
                    if (pass == 2 && !string.IsNullOrEmpty(d.DeviceName))
                        foreach (var x in Monitors)
                            if (!usados.Contains(x) && x.DeviceName == d.DeviceName) { m = x; break; }

                    if (m == null) continue;
                    if (!string.IsNullOrEmpty(d.MonitorId)) m.MonitorId = d.MonitorId;
                    if (!string.IsNullOrEmpty(d.DeviceName)) m.DeviceName = d.DeviceName;
                    if (string.IsNullOrEmpty(m.FriendlyName)) m.FriendlyName = d.FriendlyName;
                    d.Cal = m;
                    usados.Add(m);
                }
            }

            if (!criarNovas) return;
            foreach (var d in live)
            {
                if (d.Cal != null) continue;
                var n = new MonitorCal();
                n.MonitorId = d.MonitorId; n.DeviceName = d.DeviceName; n.FriendlyName = d.FriendlyName;
                n.Nova = true;
                Monitors.Add(n);
                d.Cal = n;
            }
        }

        public MonitorCal GetByMonitorId(string monitorId, string deviceName)
        {
            if (!string.IsNullOrEmpty(monitorId))
                foreach (var m in Monitors)
                    if (m.MonitorId == monitorId) return m;

            string k = HardwareKey(monitorId);
            if (k != "")
            {
                MonitorCal achou = null; int n = 0;
                foreach (var m in Monitors)
                    if (HardwareKey(m.MonitorId) == k) { achou = m; n++; }
                if (n == 1) return achou;
            }

            if (!string.IsNullOrEmpty(deviceName))
                foreach (var m in Monitors)
                    if (m.DeviceName == deviceName) return m;

            var novo = new MonitorCal();
            novo.MonitorId = monitorId; novo.DeviceName = deviceName;
            novo.Nova = true;
            Monitors.Add(novo);
            return novo;
        }
    }

    public static class ProfileStore
    {
        public static string Dir
        {
            get
            {
                string p = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "ColorMatch");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }
        public static string ProfilePath { get { return Path.Combine(Dir, "profile.xml"); } }
        public static string ProfilesDir
        {
            get
            {
                string p = Path.Combine(Dir, "perfis");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }
        public static string LogPath { get { return Path.Combine(Dir, "apply.log"); } }

        static XmlSerializer Ser = new XmlSerializer(typeof(Profile));

        public static Profile Load()
        {
            try
            {
                if (!File.Exists(ProfilePath)) return new Profile();
                using (var fs = File.OpenRead(ProfilePath))
                    return (Profile)Ser.Deserialize(fs);
            }
            catch { return new Profile(); }
        }

        public static void Save(Profile p)
        {
            using (var fs = File.Create(ProfilePath))
                Ser.Serialize(fs, p);
        }

        public static Profile LoadFrom(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                    return (Profile)Ser.Deserialize(fs);
            }
            catch { return null; }
        }

        public static void SaveTo(string path, Profile p)
        {
            using (var fs = File.Create(path))
                Ser.Serialize(fs, p);
        }

        public static void Log(string s)
        {
            try { File.AppendAllText(LogPath, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + s + "\r\n"); } catch { }
        }
    }

    // biblioteca de perfis salvos pelo usuário (%APPDATA%\ColorMatch\perfis\*.xml)
    public static class ProfileLibrary
    {
        public static List<Profile> List()
        {
            var l = new List<Profile>();
            try
            {
                foreach (var f in Directory.GetFiles(ProfileStore.ProfilesDir, "*.xml"))
                {
                    var p = ProfileStore.LoadFrom(f);
                    if (p == null) continue;
                    if (string.IsNullOrEmpty(p.Id)) p.Id = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrEmpty(p.Name)) p.Name = "Perfil";
                    l.Add(p);
                }
            }
            catch { }
            l.Sort(delegate(Profile a, Profile b)
            {
                return string.Compare(a.Name, b.Name, System.StringComparison.CurrentCultureIgnoreCase);
            });
            return l;
        }

        public static Profile FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in List()) if (p.Id == id) return p;
            return null;
        }

        public static Profile FindByName(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return null;
            foreach (var p in List())
                if (string.Equals(p.Name, nome, System.StringComparison.CurrentCultureIgnoreCase)) return p;
            return null;
        }

        public static void Save(Profile p)
        {
            if (p == null) return;
            if (string.IsNullOrEmpty(p.Id)) p.Id = System.Guid.NewGuid().ToString("N");
            ProfileStore.SaveTo(Path.Combine(ProfileStore.ProfilesDir, p.Id + ".xml"), p);
        }

        public static void Delete(Profile p)
        {
            if (p == null || string.IsNullOrEmpty(p.Id)) return;
            try
            {
                string f = Path.Combine(ProfileStore.ProfilesDir, p.Id + ".xml");
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
        }
    }
}
