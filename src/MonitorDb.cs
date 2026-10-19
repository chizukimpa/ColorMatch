// MonitorDb.cs — ColorMatch: banco de dados de monitores.
// Cada monitor que aparece é identificado sozinho (EDID + DDC/CI) e fica guardado aqui,
// com a lista de controles (VCP) que ele responde. Assim na próxima vez já sabemos
// o que o monitor tem, sem precisar perguntar de novo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace ColorMatch
{
    public class MonitorEntry
    {
        public string Chave = "";        // MONITOR\AOCB501\{4d36e96e-...}  (estável entre portas)
        public string Id = "";           // último MonitorId completo visto
        public string Marca = "";
        public string Modelo = "";
        public string Serial = "";
        public string Edid = "";         // id de 3 letras do EDID (ACR, AOC...)
        public int TamanhoCm = 0;
        public int Ano = 0;
        public string Tipo = "";         // lcd / crt (do DDC/CI)
        public string Mccs = "";         // versão MCCS que o monitor declara
        public string Capacidades = "";  // string crua de capabilities do DDC/CI
        public List<string> Vcps = new List<string>();   // códigos suportados, "10", "12"...
        public List<string> Ausentes = new List<string>(); // já sondados e não responderam (não sondar de novo)
        public int Versao = 0;            // versão do conhecimento (força reperguntar quando o app muda)
        public string Visto = "";

        [XmlIgnore]
        public string Nome
        {
            get
            {
                string s = ((Marca == null ? "" : Marca) + " " + (Modelo == null ? "" : Modelo)).Trim();
                return s == "" ? Chave : s;
            }
        }

        [XmlIgnore]
        public double Polegadas
        {
            get
            {
                if (TamanhoCm <= 0) return 0;
                double d = Math.Sqrt(TamanhoCm * TamanhoCm + (TamanhoCm * 9.0 / 16.0) * (TamanhoCm * 9.0 / 16.0));
                return Math.Round(d / 2.54, 1);
            }
        }

        public bool Suporta(byte code)
        {
            if (Vcps == null) return false;
            string s = code.ToString("X2");
            foreach (var v in Vcps) if (string.Equals(v, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void AddVcp(byte code)
        {
            if (!Suporta(code)) Vcps.Add(code.ToString("X2"));
        }

        public bool Ausente(byte code)
        {
            if (Ausentes == null) return false;
            string s = code.ToString("X2");
            foreach (var v in Ausentes) if (string.Equals(v, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void AddAusente(byte code)
        {
            if (Ausentes == null) Ausentes = new List<string>();
            if (!Ausente(code)) Ausentes.Add(code.ToString("X2"));
        }
    }

    public static class MonitorDb
    {
        static List<MonitorEntry> _lista;

        public static string Caminho { get { return Path.Combine(ProfileStore.Dir, "monitores.xml"); } }

        // versão do conhecimento guardado: quando sobe, o app repergunta as capabilities
        public const int VersaoAtual = 3;

        static XmlSerializer _ser = new XmlSerializer(typeof(List<MonitorEntry>));

        public static List<MonitorEntry> Lista
        {
            get { if (_lista == null) _lista = Carregar(); return _lista; }
        }

        static List<MonitorEntry> Carregar()
        {
            try
            {
                if (!File.Exists(Caminho)) return new List<MonitorEntry>();
                using (var fs = File.OpenRead(Caminho))
                {
                    var l = _ser.Deserialize(fs) as List<MonitorEntry>;
                    return l == null ? new List<MonitorEntry>() : l;
                }
            }
            catch (Exception e) { ProfileStore.Log("MonitorDb: falha ao ler (" + e.Message + ")"); return new List<MonitorEntry>(); }
        }

        public static void Salvar()
        {
            try
            {
                using (var fs = File.Create(Caminho))
                    _ser.Serialize(fs, Lista);
            }
            catch (Exception e) { ProfileStore.Log("MonitorDb: falha ao salvar (" + e.Message + ")"); }
        }

        public static MonitorEntry PorChave(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return null;
            foreach (var m in Lista) if (m.Chave == chave) return m;
            return null;
        }

        public static void Esquecer(string chave)
        {
            var m = PorChave(chave);
            if (m != null) { Lista.Remove(m); Salvar(); }
        }

        // identifica o monitor: cria a entrada se for novo, completa com EDID e (se preciso) DDC/CI
        public static MonitorEntry Obter(DisplayScreen ds, bool lerCapacidades, bool forcar)
        {
            string chave = Profile.HardwareKey(ds == null ? "" : ds.MonitorId);
            if (chave == "") return null;

            var e = PorChave(chave);
            bool nova = false;
            if (e == null) { e = new MonitorEntry(); e.Chave = chave; Lista.Add(e); nova = true; }

            if (ds != null) e.Id = ds.MonitorId;

            // EDID (marca, modelo, polegadas, ano, serial)
            if (nova || forcar || string.IsNullOrEmpty(e.Modelo))
            {
                var info = Win.LerEdid(ds == null ? "" : ds.MonitorId);
                if (!string.IsNullOrEmpty(info.Marca)) e.Marca = info.Marca;
                if (!string.IsNullOrEmpty(info.Modelo)) e.Modelo = info.Modelo;
                if (!string.IsNullOrEmpty(info.Serial)) e.Serial = info.Serial;
                if (info.LarguraCm > 0) e.TamanhoCm = info.LarguraCm;
                if (info.Ano > 0) e.Ano = info.Ano;
            }
            else if (ds != null && string.IsNullOrEmpty(e.Marca))
            {
                e.Marca = ds.Brand;
                if (string.IsNullOrEmpty(e.Modelo)) e.Modelo = ds.FriendlyName;
            }

            // DDC/CI: pergunta as capabilities quando ainda não sabemos (ou quando o banco é de uma versão antiga)
            bool temVcp = e.Vcps != null && e.Vcps.Count > 0;
            bool velho = e.Versao < VersaoAtual;
            if (lerCapacidades && (forcar || !temVcp || velho))
            {
                if (ds != null && ds.PhysicalHandle == IntPtr.Zero) Win.AttachPhysical(ds);
                var cap = ds == null ? null : Win.GetCapabilities(ds.PhysicalHandle);
                if (!string.IsNullOrEmpty(cap))
                {
                    e.Capacidades = cap;
                    e.Tipo = Win.CapInfo(cap, "type");
                    e.Mccs = Win.CapInfo(cap, "mccs_ver");
                    string mod = Win.CapInfo(cap, "model");
                    if (!string.IsNullOrEmpty(mod) && string.IsNullOrEmpty(e.Modelo)) e.Modelo = mod;
                    var vcps = Win.ParseVcpList(cap);
                    if (vcps.Count > 0)
                    {
                        // soma ao que já sabíamos: a string do monitor às vezes vem corrompida
                        // e não pode apagar o que a sondagem já confirmou
                        int antes = e.Vcps == null ? 0 : e.Vcps.Count;
                        if (e.Vcps == null) e.Vcps = new List<string>();
                        foreach (var c in vcps) if (!e.Ausente(c)) e.AddVcp(c);
                        if (e.Versao < VersaoAtual && antes == 0) e.Ausentes = new List<string>();
                        ProfileStore.Log("monitor identificado: " + e.Nome + " — " + e.Vcps.Count +
                            " controles DDC/CI, MCCS " + e.Mccs);
                    }
                    e.Versao = VersaoAtual;
                }
                else if (ds != null)
                {
                    ProfileStore.Log("monitor sem capabilities DDC/CI: " + e.Nome + " (" + ds.DeviceName + ")");
                }
            }

            e.Visto = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Salvar();
            return e;
        }
    }
}
