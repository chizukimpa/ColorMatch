// BuiltinThemes.cs — ColorMatch: temas prontos que acompanham o app.
//
// Tema "Mac (nativo)": reproduz o visual que o macOS 26.7 entrega nos mesmos dois
// monitores (AOC 24G50F + Acer KA242Y E), capturado em 29/10/2026 com os monitores
// ligados no Mac:
//   - perfil de cor ativo do macOS: "Generic RGB Profile" (TRC γ1.80, D65, sem vcgt)
//   - LUT de gamma do sistema: identidade (nenhuma correção de software)
//   - a curva que o ColorSync manda para a tela deixa o cinza 50% em 0.427, o que
//     equivale a Gamma 0.81 no ColorMatch (mesma conta do Icc.cs)
//   - DDC/CI que já estava gravado nos monitores: AOC 62/54 com ganhos 51/53/68;
//     Acer 75/55 com ganhos 50/50/50
//   - OSD (VCP) lido na sessão: preset de cor 0x14 = 11 (Usuário), níveis de preto
//     0x6C/0x6E = 50 nos dois. Ficam em Extras e são reaplicados junto com o perfil.
//
// O tema é semeado uma única vez na biblioteca de perfis; se o usuário excluir,
// ele não volta sozinho (a marca fica em temas.txt). Ao subir a versão do tema,
// a semente é reaplicada (o original é sempre protegido: editar cria cópia).
using System;
using System.Collections.Generic;
using System.IO;

namespace ColorMatch
{
    public static class BuiltinThemes
    {
        const string MarcaArquivo = "temas.txt";

        const string MacId = "macnativo0000000000000000000001";
        const int MacVersao = 2;

        public static void Seed()
        {
            try { SemearMac(); }
            catch (Exception e) { ProfileStore.Log("BuiltinThemes: " + e.Message); }
        }

        static void SemearMac()
        {
            string marca = Path.Combine(ProfileStore.Dir, MarcaArquivo);
            string chave = MacId + "=" + MacVersao;

            try
            {
                if (File.Exists(marca))
                {
                    foreach (var linha in File.ReadAllLines(marca))
                        if (linha.Trim() == chave) return;
                }
            }
            catch { }

            var p = TemaMac();
            ProfileStore.SaveTo(Path.Combine(ProfileStore.ProfilesDir, p.Id + ".xml"), p);
            ProfileStore.Log("tema embutido instalado: " + p.Name + " (v" + MacVersao + ")");

            try
            {
                using (var fs = new StreamWriter(marca, true))
                    fs.WriteLine(chave);
            }
            catch { }
        }

        static Profile TemaMac()
        {
            var p = new Profile();
            p.Name = "Mac (nativo)";
            p.Id = MacId;
            p.Editavel = false;          // original protegido: editar cria "Mac (nativo) (editado)"
            p.UseSoftwareCurve = true;
            p.Monitors = new List<MonitorCal>();

            p.Monitors.Add(Cal("MONITOR\\AOCB501\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0002",
                "24G50F", 0, 62, 54, 51, 53, 68));

            p.Monitors.Add(Cal("MONITOR\\ACR0B70\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0003",
                "KA242Y E", 1, 75, 55, 50, 50, 50));

            return p;
        }

        // Gamma 0.81 = TRC γ1.80 do perfil "Generic RGB" do macOS compensada para sRGB
        static MonitorCal Cal(string monitorId, string nome, int ordem,
            int brilho, int contraste, int ganhoR, int ganhoG, int ganhoB)
        {
            var c = new MonitorCal();
            c.MonitorId = monitorId;
            c.FriendlyName = nome;
            c.Ordem = ordem;

            c.Brightness = brilho;
            c.Contrast = contraste;
            c.RedGain = ganhoR;
            c.GreenGain = ganhoG;
            c.BlueGain = ganhoB;

            c.Gamma = 0.81;
            c.BrightMult = 1.0;
            c.BlackLift = 0.0;
            c.GainR = 1.0;
            c.GainG = 1.0;
            c.GainB = 1.0;

            c.Temp = 0.0;
            c.Tint = 0.0;
            c.Saturation = -1;
            c.Extras = new List<VcpValor>();

            // OSD capturado por DDC/CI (VCP) nos dois monitores:
            //   0x14 = 11  -> preset de cor "Usuário" (não sRGB/6500K)
            //   0x6C = 50  -> nível de preto: vermelho (neutro)
            //   0x6E = 50  -> nível de preto: azul (neutro)
            // Gravados assim o monitor volta ao mesmo estado do OSD que estava no Mac.
            c.Extras.Add(new VcpValor(0x14, 11));
            c.Extras.Add(new VcpValor(0x6C, 50));
            c.Extras.Add(new VcpValor(0x6E, 50));
            return c;
        }
    }
}
