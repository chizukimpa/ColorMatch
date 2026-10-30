// Api.cs — ColorMatch: camada Win32 (display, DDC/CI, gamma, EDID/friendly name)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Text;

namespace ColorMatch
{
    public class DisplayScreen
    {
        public IntPtr HMonitor;
        public string DeviceName;    // \\.\DISPLAY1
        public string MonitorId;     // MONITOR\AOCB501\{...}\0002
        public string FriendlyName;  // "24G50F"
        public string Brand;         // "AOC" / "Acer" (do EDID)
        public string AdapterName;   // AMD Radeon RX 6600
        public bool Primary;
        public int X, Y, W, H;
        public IntPtr PhysicalHandle;
        public MonitorCal Cal;       // entrada do perfil casada com esta tela (Profile.MapLive)

        public override string ToString()
        {
            string nm = "";
            if (!string.IsNullOrEmpty(Brand) && !string.IsNullOrEmpty(FriendlyName))
                nm = FriendlyName.StartsWith(Brand, StringComparison.OrdinalIgnoreCase)
                    ? FriendlyName : (Brand + " " + FriendlyName);
            else if (!string.IsNullOrEmpty(FriendlyName)) nm = FriendlyName;
            else if (!string.IsNullOrEmpty(Brand)) nm = Brand;
            if (string.IsNullOrEmpty(nm)) nm = DeviceName;
            return nm + (Primary ? "  (principal)" : "");
        }
    }

    public static class Win
    {
        // ---------- enum displays ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string dev, uint idx, ref DISPLAY_DEVICE dd, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO mi);

        // ---------- gamma ----------
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateDCW(string driver, string device, string port, IntPtr init);
        [DllImport("gdi32.dll")]
        public static extern bool GetDeviceGammaRamp(IntPtr hdc, [In, Out] short[] ramp);
        [DllImport("gdi32.dll")]
        public static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] short[] ramp);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        // ---------- DDC/CI ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }
        [DllImport("dxva2.dll")]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint count);
        [DllImport("dxva2.dll")]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")]
        public static extern bool GetMonitorBrightness(IntPtr h, ref uint min, ref uint cur, ref uint max);
        [DllImport("dxva2.dll")]
        public static extern bool GetMonitorContrast(IntPtr h, ref uint min, ref uint cur, ref uint max);
        [DllImport("dxva2.dll")]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, ref uint type, ref uint cur, ref uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
        [DllImport("dxva2.dll")]
        public static extern bool DestroyPhysicalMonitor(IntPtr h);

        // códigos VCP úteis
        public const byte VCP_BRIGHTNESS = 0x10;
        public const byte VCP_CONTRAST = 0x12;
        public const byte VCP_RED_GAIN = 0x16;
        public const byte VCP_GREEN_GAIN = 0x18;
        public const byte VCP_BLUE_GAIN = 0x1A;
        public const byte VCP_RED_BLACK = 0x6C;
        public const byte VCP_GREEN_BLACK = 0x6D;
        public const byte VCP_BLUE_BLACK = 0x6E;

        public static List<DisplayScreen> EnumerateDisplays()
        {
            _hmonByDevice = new Dictionary<string, IntPtr>();   // nunca reaproveitar handle de enumeração antiga
            var devMap = new Dictionary<string, DisplayScreen>();

            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 1) == 0) continue; // inativo

                var ds = new DisplayScreen();
                ds.DeviceName = dd.DeviceName;
                ds.AdapterName = dd.DeviceString;
                var mon = new DISPLAY_DEVICE(); mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0))
                {
                    ds.MonitorId = mon.DeviceID;
                    var edid = EdidDoMonitor(mon.DeviceID);
                    ds.FriendlyName = NomeDoEdid(edid);
                    ds.Brand = MarcaDoEdid(edid);
                }
                devMap[dd.DeviceName] = ds;
            }

            var cb = new MonitorEnumProc(_collect);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            try
            {
                string ks = "";
                foreach (var kv in devMap)
                    ks += kv.Key + " [" + (string.IsNullOrEmpty(kv.Value.MonitorId) ? "SEM ID" : kv.Value.MonitorId) + "]  ";
                ProfileStore.Log("Enum: devMap=" + devMap.Count + " hmon=" + _hmonByDevice.Count + " " + ks);
            } catch { }

            foreach (var kv in devMap)
            {
                IntPtr h;
                if (_hmonByDevice.TryGetValue(kv.Key, out h)) kv.Value.HMonitor = h;
            }

            var list = new List<DisplayScreen>();
            foreach (var kv in devMap)
            {
                if (kv.Value.HMonitor == IntPtr.Zero) continue;
                list.Add(kv.Value);
            }
            return list;
        }

        static Dictionary<string, IntPtr> _hmonByDevice = new Dictionary<string, IntPtr>();

        static bool _collect(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data)
        {
            var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfoW(hMon, ref mi)) return true;
            if (_hmonByDevice.ContainsKey(mi.szDevice)) return true;
            _hmonByDevice[mi.szDevice] = hMon;
            return true;
        }

        // passa as dicas de janela por monitor
        public static void AttachRects(List<DisplayScreen> list)
        {
            _hmonByDevice = new Dictionary<string, IntPtr>();
            var et = list;
            var useEnum = new MonitorEnumProc(_collectRects);
            foreach (var d in et) d.HMonitor = IntPtr.Zero;
            _currentList = et;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, useEnum, IntPtr.Zero);
        }

        static List<DisplayScreen> _currentList;

        static bool _collectRects(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data)
        {
            var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfoW(hMon, ref mi)) return true;
            foreach (var d in _currentList)
            {
                if (d.DeviceName != mi.szDevice) continue;
                d.HMonitor = hMon;
                d.Primary = (mi.dwFlags & 1) != 0;
                d.X = mi.rcMonitor.Left; d.Y = mi.rcMonitor.Top;
                d.W = mi.rcMonitor.Right - d.X; d.H = mi.rcMonitor.Bottom - d.Y;
            }
            return true;
        }

        public static void AttachPhysical(DisplayScreen ds)
        {
            uint cnt = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(ds.HMonitor, ref cnt) || cnt == 0) return;
            var arr = new PHYSICAL_MONITOR[cnt];
            if (!GetPhysicalMonitorsFromHMONITOR(ds.HMonitor, cnt, arr)) return;
            ds.PhysicalHandle = arr[0].hPhysicalMonitor;
            for (int i = (ds.PhysicalHandle == arr[0].hPhysicalMonitor ? 1 : 0); i < arr.Length; i++)
                if (arr[i].hPhysicalMonitor != IntPtr.Zero && arr[i].hPhysicalMonitor != ds.PhysicalHandle)
                    DestroyPhysicalMonitor(arr[i].hPhysicalMonitor);
        }

        public static bool ReadVCP(IntPtr h, byte code, out uint cur, out uint max)
        {
            uint t = 0, c = 0, m = 0;
            cur = max = 0;
            if (!GetVCPFeatureAndVCPFeatureReply(h, code, ref t, ref c, ref m)) return false;
            cur = c; max = m; return true;
        }
        public static bool WriteVCP(IntPtr h, byte code, uint val) { return h != IntPtr.Zero && SetVCPFeature(h, code, val); }

        // ---------- modo de tela (resolução / taxa de atualização) ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        const int ENUM_CURRENT_SETTINGS = -1;
        const uint DM_BITSPERPEL = 0x00040000;
        const uint DM_PELSWIDTH = 0x00080000;
        const uint DM_PELSHEIGHT = 0x00100000;
        const uint DM_DISPLAYFREQUENCY = 0x00400000;
        const uint CDS_UPDATEREGISTRY = 0x00000001;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsExW(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr param);

        public static DEVMODE CurrentMode(string deviceName)
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return dm;
            return dm;
        }

        // aplica resolução/taxa do perfil; 0 em qualquer campo = não mexer.
        // devolve true se já estava igual ou se a troca foi aceita.
        public static bool ApplyMode(string deviceName, int largura, int altura, int taxaHz)
        {
            if (string.IsNullOrEmpty(deviceName) || largura <= 0 || altura <= 0) return false;
            try
            {
                var atual = CurrentMode(deviceName);
                if (atual.dmPelsWidth == largura && atual.dmPelsHeight == altura &&
                    (taxaHz <= 0 || atual.dmDisplayFrequency == taxaHz)) return true;

                // procura o modo exato para herdar bits/cor/flags corretos do driver
                var alvo = new DEVMODE();
                bool achou = false;
                for (int i = 0; i < 4096; i++)
                {
                    var t = new DEVMODE();
                    t.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    if (!EnumDisplaySettingsW(deviceName, i, ref t)) break;
                    if (t.dmPelsWidth != largura || t.dmPelsHeight != altura) continue;
                    if (taxaHz > 0 && t.dmDisplayFrequency != taxaHz) continue;
                    alvo = t; achou = true; break;
                }
                if (!achou)
                {
                    ProfileStore.Log("modo " + largura + "x" + altura + " @" + taxaHz +
                        " Hz não existe em " + deviceName + " (mantido o atual)");
                    return false;
                }

                alvo.dmFields = (int)(DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY);
                int r = ChangeDisplaySettingsExW(deviceName, ref alvo, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                if (r != 0)
                {
                    ProfileStore.Log("troca de modo recusada em " + deviceName + " (" + largura + "x" +
                        altura + "@" + taxaHz + "): código " + r);
                    return false;
                }
                ProfileStore.Log("modo aplicado em " + deviceName + ": " + largura + "x" + altura +
                    (taxaHz > 0 ? "@" + taxaHz + "Hz" : ""));
                return true;
            }
            catch (Exception e)
            {
                ProfileStore.Log("ApplyMode: " + e.Message);
                return false;
            }
        }

        // extras do OSD que podem ser reaplicados sem risco: fora os 5 controles do
        // cartão, a saturação (campo próprio), os perigosos (fonte de entrada / energia)
        // e os códigos só de leitura ou de ação.
        public static bool VcpExtraSeguro(byte code)
        {
            if (code == VCP_BRIGHTNESS || code == VCP_CONTRAST) return false;
            if (code == VCP_RED_GAIN || code == VCP_GREEN_GAIN || code == VCP_BLUE_GAIN) return false;
            if (code == 0x8A) return false;              // saturação já vai pelo campo Saturation
            if (VcpPerigoso(code)) return false;
            if (VcpEhLeitura(code) || VcpEhAcao(code)) return false;
            return true;
        }

        // reescreve só o que estiver diferente (não re-inicializa o preset de cor à toa)
        public static int ApplyExtras(IntPtr h, List<VcpValor> extras)
        {
            int n = 0;
            if (h == IntPtr.Zero || extras == null) return 0;
            foreach (var v in extras)
            {
                if (v == null || !VcpExtraSeguro(v.Codigo)) continue;
                uint cur, max;
                if (ReadVCP(h, v.Codigo, out cur, out max) && (int)cur == v.Valor) continue;
                if (WriteVCP(h, v.Codigo, (uint)v.Valor)) n++;
            }
            return n;
        }

        public static short[] ReadRamp(string deviceName)
        {
            IntPtr hdc = CreateDCW(null, deviceName, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;
            var ramp = new short[768];
            bool ok = GetDeviceGammaRamp(hdc, ramp);
            DeleteDC(hdc);
            return ok ? ramp : null;
        }
        public static bool WriteRamp(string deviceName, short[] ramp)
        {
            IntPtr hdc = CreateDCW(null, deviceName, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;
            bool ok = SetDeviceGammaRamp(hdc, ramp);
            DeleteDC(hdc);
            return ok;
        }

        public static void ApplyGamma(DisplayScreen ds, short[] ramp)
        {
            WriteRamp(ds.DeviceName, ramp);
        }

        // EDID do monitor: "SYSTEM\CurrentControlSet\Enum\DISPLAY\<modelo>\<instancia>\Device Parameters"
        static byte[] EdidDoMonitor(string monitorId)
        {
            try
            {
                if (string.IsNullOrEmpty(monitorId)) return null;
                // DeviceID vem como "MONITOR\ACR0B70\{guid}\0003" (o sufixo não é o nome da chave no registro)
                var p = monitorId.Split('\\');
                if (p.Length < 3) return null;
                using (var raiz = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + p[1]))
                {
                    if (raiz == null) return null;
                    foreach (var inst in raiz.GetSubKeyNames())
                    {
                        using (var k = raiz.OpenSubKey(inst + @"\Device Parameters"))
                        {
                            if (k == null) continue;
                            var edid = k.GetValue("EDID") as byte[];
                            if (edid != null && edid.Length >= 128) return edid;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // marca de 3 letras do EDID (bytes 8..9, 5 bits por letra)
        static string MarcaDoEdid(byte[] edid)
        {
            if (edid == null || edid.Length < 10) return null;
            int v = (edid[8] << 8) | edid[9];
            if ((v & 0x8000) != 0) return null;
            char a = (char)('A' + ((v >> 10) & 0x1F) - 1);
            char b = (char)('A' + ((v >> 5) & 0x1F) - 1);
            char c = (char)('A' + (v & 0x1F) - 1);
            if (a < 'A' || a > 'Z' || b < 'A' || b > 'Z' || c < 'A' || c > 'Z') return null;
            string id = "" + a + b + c;
            switch (id)
            {
                case "ACR": return "Acer";
                case "AOC": return "AOC";
                case "DEL": return "Dell";
                case "SAM": return "Samsung";
                case "GSM": return "LG";
                case "LGD": return "LG";
                case "HWP": return "HP";
                case "HPN": return "HP";
                case "BNQ": return "BenQ";
                case "PHL": return "Philips";
                case "MSI": return "MSI";
                case "ACI": return "ASUS";
                case "AUS": return "ASUS";
                case "VSC": return "ViewSonic";
                case "IVM": return "iiyama";
                case "GBT": return "Gigabyte";
                case "LEN": return "Lenovo";
                case "SNY": return "Sony";
                case "NEC": return "NEC";
            }
            return id;
        }

        // descriptor 0xFC (nome) ou 0xFE (string ASCII) do EDID — 4 blocos de 18 bytes a partir de 54
        static string NomeDoEdid(byte[] edid)
        {
            if (edid == null || edid.Length < 72) return null;
            for (int tipo = 0; tipo < 2; tipo++)
            {
                byte alvo = tipo == 0 ? (byte)0xFC : (byte)0xFE;
                for (int d = 0; d < 4; d++)
                {
                    int b = 54 + d * 18;
                    if (b + 17 >= edid.Length) break;
                    if (edid[b] != 0 || edid[b + 1] != 0 || edid[b + 2] != 0) continue;
                    if (edid[b + 3] != alvo) continue;
                    var sb = new StringBuilder();
                    for (int i = b + 5; i <= b + 17; i++)
                        if (edid[i] >= 32 && edid[i] < 127) sb.Append((char)edid[i]);
                    string s = sb.ToString().Trim(' ', '\n', '\r', '\0');
                    if (s.Length > 1) return s;
                }
            }
            return null;
        }

        // ---------- DDC/CI: capabilities (o que o monitor diz que ele tem) ----------
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetCapabilitiesStringLength(IntPtr h, ref uint len);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr h, StringBuilder sb, uint len);

        public static string GetCapabilities(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            try
            {
                uint len = 0;
                if (!GetCapabilitiesStringLength(h, ref len)) return null;
                if (len == 0 || len > 32768) return null;
                var sb = new StringBuilder((int)len + 2);
                if (!CapabilitiesRequestAndCapabilitiesReply(h, sb, len)) return null;
                return sb.ToString();
            }
            catch { return null; }
        }

        // pega um campo do tipo "model(24G50F)" dentro da string de capabilities
        public static string CapInfo(string cap, string campo)
        {
            if (string.IsNullOrEmpty(cap)) return "";
            int i = cap.IndexOf(campo + "(", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            int j = cap.IndexOf(')', i);
            if (j < 0) return "";
            return cap.Substring(i + campo.Length + 1, j - i - campo.Length - 1).Trim();
        }

        // lista de códigos VCP suportados, do campo "vcp(10 12 14(01 05 06) 16 18 ...)"
        // (as listas de valores dentro de parênteses NÃO são códigos — precisa balancear)
        public static List<byte> ParseVcpList(string cap)
        {
            var l = new List<byte>();
            if (string.IsNullOrEmpty(cap)) return l;
            int i = cap.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return l;
            i += 4;

            var tok = new StringBuilder();
            int depth = 1;
            for (; i < cap.Length && l.Count < 128; i++)
            {
                char ch = cap[i];
                if (ch == '(')
                {
                    AddVcpTok(l, tok.ToString()); tok.Length = 0;
                    depth++;
                    continue;
                }
                if (ch == ')')
                {
                    depth--;
                    if (depth <= 0) { AddVcpTok(l, tok.ToString()); break; }
                    continue;   // fecha lista de valores: o que estava lá dentro não é código
                }
                if (depth != 1) continue;
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n' || ch == ',')
                {
                    AddVcpTok(l, tok.ToString()); tok.Length = 0;
                }
                else tok.Append(ch);
            }
            if (tok.Length > 0) AddVcpTok(l, tok.ToString());
            return l;
        }

        // mapa código -> valores permitidos (o monitor declara no capabilities: 14(01 05 06 08 0B))
        public static Dictionary<byte, List<uint>> CapMapa(string cap)
        {
            var d = new Dictionary<byte, List<uint>>();
            if (string.IsNullOrEmpty(cap)) return d;
            int i = cap.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return d;
            i += 4;

            var tokens = new List<string>();
            var tok = new StringBuilder();
            int depth = 1;
            for (; i < cap.Length; i++)
            {
                char ch = cap[i];
                if (ch == '(') { depth++; tok.Append(ch); continue; }
                if (ch == ')')
                {
                    depth--;
                    if (depth <= 0) break;
                    tok.Append(ch);
                    continue;
                }
                if (depth == 1 && (ch == ' ' || ch == '\t' || ch == ',' || ch == '\r' || ch == '\n'))
                {
                    if (tok.Length > 0) { tokens.Add(tok.ToString()); tok.Length = 0; }
                    continue;
                }
                tok.Append(ch);
            }
            if (tok.Length > 0) tokens.Add(tok.ToString());

            foreach (var t in tokens)
            {
                string cod = t, lista = null;
                int p = t.IndexOf('(');
                if (p > 0)
                {
                    cod = t.Substring(0, p);
                    lista = t.Substring(p + 1);
                    int f = lista.LastIndexOf(')');
                    if (f >= 0) lista = lista.Substring(0, f);
                }
                if (cod.Length > 2) cod = cod.Substring(0, 2);
                byte b;
                if (!byte.TryParse(cod, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out b)) continue;

                var l = new List<uint>();
                if (lista != null)
                    foreach (var v in lista.Split(new char[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        uint u;
                        if (uint.TryParse(v, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out u)) l.Add(u);
                    }
                if (!d.ContainsKey(b) || (l.Count > 0 && d[b].Count == 0)) d[b] = l;
            }
            return d;
        }

        public static List<uint> CapValores(string cap, byte code)
        {
            var m = CapMapa(cap);
            List<uint> l;
            if (m.TryGetValue(code, out l) && l != null) return l;
            return new List<uint>();
        }

        // código que só aceita valores tabelados (não é slider)
        public static bool VcpEhEnum(byte code)
        {
            switch (code)
            {
                case 0x14: case 0x60: case 0x82: case 0x84: case 0x86: case 0x8D:
                case 0xAA: case 0xCC: case 0xD6: case 0xDA: case 0xDB: case 0xDC:
                    return true;
            }
            return false;
        }

        // mexer nisto pode deixar a tela sem imagem (troca de entrada) — pede confirmação
        public static bool VcpPerigoso(byte code)
        {
            return code == 0x60 || code == 0xD6;
        }

        static void AddVcpTok(List<byte> l, string t)
        {
            t = (t == null ? "" : t.Trim());
            if (t.Length == 0) return;
            if (t.Length > 2) t = t.Substring(0, 2);   // código de 1 byte
            byte b;
            if (byte.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out b))
                if (!l.Contains(b)) l.Add(b);
        }

        // códigos que a gente sonda quando o monitor novo aparece (o que responder entra no banco)
        // códigos que a gente sonda quando o monitor novo aparece (o que responder entra no banco).
        // Lista completa dos códigos padrão do MCCS (VESA) até 0xDF — assim o app descobre controles
        // mesmo em monitores que não declaram as capacidades (vários LG/Dell/Samsung/BenQ/etc.),
        // em vez de ficar preso só aos dois monitores do desenvolvedor.
        public static readonly byte[] VcpCandidatos = new byte[] {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x08, 0x0A, 0x0B, 0x0C, 0x0E,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x16, 0x17, 0x18, 0x1A, 0x1C, 0x1E, 0x1F,
            0x20, 0x22, 0x24, 0x26, 0x28, 0x29, 0x2A, 0x2C, 0x2E,
            0x30, 0x32, 0x34, 0x36, 0x38, 0x39, 0x3A, 0x3C, 0x3E,
            0x40, 0x41, 0x42, 0x43, 0x44, 0x46, 0x48, 0x4A, 0x4C,
            0x52, 0x54, 0x56, 0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E,
            0x60, 0x62, 0x63, 0x64, 0x66, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
            0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x78, 0x7A, 0x7C, 0x7E,
            0x80, 0x82, 0x84, 0x86, 0x87, 0x88, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F,
            0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F,
            0xA0, 0xA2, 0xA4, 0xA5, 0xAA, 0xAC, 0xAE,
            0xB0, 0xB2, 0xB4, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE,
            0xC0, 0xC2, 0xC3, 0xC4, 0xC6, 0xC8, 0xC9, 0xCA, 0xCC, 0xCD, 0xCE, 0xCF,
            0xD0, 0xD2, 0xD4, 0xD6, 0xD7, 0xDA, 0xDB, 0xDC, 0xDE, 0xDF,
            // extensões proprietárias de fabricante (sonda profunda) — alguns monitores
            // usam 0xE0-0xFF para backlight/contraste/ganho extra
            0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xEB,
            0xEC, 0xED, 0xEE, 0xEF, 0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7,
            0xF8, 0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE, 0xFF
        };

        // ---------- nomes MCCS dos códigos VCP (base do padrão VESA, via ddcutil) ----------
        static readonly string[] TabelaVcp = new string[] {
            "01|Degauss","02|Novo valor de controle","03|Soft controls","04|Restaurar padrão de fábrica",
            "05|Restaurar brilho/contraste de fábrica","06|Restaurar geometria de fábrica","08|Restaurar cores de fábrica",
            "0A|Restaurar padrão de TV","0B|Incremento de temperatura de cor","0C|Temperatura de cor (Kelvin)","0E|Clock",
            "10|Brilho","11|Realce de tom de pele","12|Contraste","13|Controle de backlight","14|Preset de cor",
            "16|Ganho de vídeo: vermelho","17|Compensação de visão de cor","18|Ganho de vídeo: verde","1A|Ganho de vídeo: azul",
            "1C|Foco","1E|Auto ajuste","1F|Auto ajuste de cor","20|Posição horizontal","22|Tamanho horizontal",
            "24|Almofada horizontal","26|Balanço de almofada H","28|Convergência H R/B","29|Convergência H M/G",
            "2A|Linearidade horizontal","2C|Balanço de linearidade H","2E|Expansão de escala de cinza","30|Posição vertical",
            "32|Tamanho vertical","34|Almofada vertical","36|Balanço de almofada V","38|Convergência V R/B",
            "39|Convergência V M/G","3A|Linearidade vertical","3C|Balanço de linearidade V","3E|Fase do clock",
            "40|Paralelogramo horizontal","41|Paralelogramo vertical","42|Keystone horizontal","43|Keystone vertical",
            "44|Rotação","46|Canto superior: flare","48|Canto superior: hook","4A|Canto inferior: flare","4C|Canto inferior: hook",
            "52|Controle ativo","54|Preservação de desempenho","56|Moiré horizontal","58|Moiré vertical",
            "59|Saturação 6 eixos: vermelho","5A|Saturação 6 eixos: amarelo","5B|Saturação 6 eixos: verde",
            "5C|Saturação 6 eixos: ciano","5D|Saturação 6 eixos: azul","5E|Saturação 6 eixos: magenta",
            "60|Fonte de entrada","62|Volume do alto-falante","63|Seleção de alto-falante","64|Volume do microfone",
            "66|Sensor de luz ambiente","6B|Nível de backlight: branco","6C|Nível de preto: vermelho",
            "6D|Nível de backlight: vermelho","6E|Nível de preto: verde","6F|Nível de backlight: verde",
            "70|Nível de preto: azul","71|Nível de backlight: azul","72|Gamma","73|Tamanho da LUT",
            "74|LUT: ponto único","75|LUT: bloco","76|Chamada de rotina","78|Leitura de identificação",
            "7A|Plano focal","7C|Zoom","7E|Trapézio","80|Keystone","82|Espelho horizontal","84|Espelho vertical",
            "86|Escala de imagem","87|Nitidez","88|Modulação de varredura","8A|Saturação de cor","8B|Canal de TV",
            "8C|Nitidez de TV","8D|Mudo / tela em branco","8E|Contraste de TV","8F|Agudo (áudio)","90|Matiz",
            "91|Grave (áudio)","92|Preto de TV","93|Balanço L/R do áudio","94|Modo do processador de áudio",
            "95|Janela TL_X","96|Janela TL_Y","97|Janela BR_X","98|Janela BR_Y","99|Janela lig/deslig",
            "9A|Fundo da janela","9B|Matiz 6 eixos: vermelho","9C|Matiz 6 eixos: amarelo","9D|Matiz 6 eixos: verde",
            "9E|Matiz 6 eixos: ciano","9F|Matiz 6 eixos: azul","A0|Matiz 6 eixos: magenta","A2|Auto ajuste lig/deslig",
            "A4|Máscara de janela","A5|Trocar janela","AA|Orientação da tela","AC|Frequência horizontal",
            "AE|Frequência vertical","B0|Configurações","B2|Layout de subpixel","B4|Modo de timing",
            "B6|Tecnologia do painel","B7|Status do monitor","B8|Contagem de pacotes","B9|Origem X do monitor",
            "BA|Origem Y do monitor","BB|Erros de cabeçalho","BC|Erros de CRC","BD|ID do cliente","BE|Controle de link",
            "C0|Tempo de uso do monitor","C2|Tamanho do descritor","C3|Transmitir descritor","C4|Mostrar descritor",
            "C6|Chave de aplicação","C8|Tipo de controlador","C9|Versão de firmware","CA|Controle de OSD/botões",
            "CC|Idioma do OSD","CD|Indicadores de status","CE|Tamanho do display auxiliar","CF|Dados do display auxiliar",
            "D0|Seleção de saída","D2|Asset tag","D4|Modo de vídeo estéreo","D6|Modo de energia",
            "D7|Saída de energia auxiliar","DA|Modo de varredura","DB|Modo de imagem","DC|Modo de display",
            "DE|Rascunho","DF|Versão de VCP"
        };

        static Dictionary<byte, string> _vcpNomes;
        public static string VcpNome(byte code)
        {
            if (_vcpNomes == null)
            {
                _vcpNomes = new Dictionary<byte, string>();
                foreach (var linha in TabelaVcp)
                {
                    int p = linha.IndexOf('|');
                    if (p <= 0) continue;
                    byte c;
                    if (byte.TryParse(linha.Substring(0, p), System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out c))
                        _vcpNomes[c] = linha.Substring(p + 1);
                }
            }
            string s;
            return _vcpNomes.TryGetValue(code, out s) ? s : ("VCP " + code.ToString("X2"));
        }

        // códigos de leitura (não aceitam escrita) e de ação (não fazem sentido como slider)
        public static bool VcpEhAcao(byte code)
        {
            switch (code)
            {
                case 0x01: case 0x04: case 0x05: case 0x06: case 0x08: case 0x0A:
                case 0x1E: case 0x1F: case 0xA2: case 0x76: return true;
            }
            return false;
        }
        public static bool VcpEhLeitura(byte code)
        {
            switch (code)
            {
                case 0x02: case 0x03: case 0x0B: case 0x52: case 0x54: case 0x73: case 0x78:
                case 0xAC: case 0xAE: case 0xB7: case 0xB8: case 0xB9: case 0xBA: case 0xBB:
                case 0xBC: case 0xC0: case 0xC2: case 0xC8: case 0xC9: case 0xAA: case 0xDF:
                case 0xCE: case 0xB6: case 0xB2: case 0xB4: case 0xBE: case 0xBD: case 0xD4:
                case 0xD2: case 0xDE: case 0xCF: case 0xCA: return true;
            }
            return false;
        }
        // nome amigável de valores "não contínuos" conhecidos (entrada, preset, energia...)
        public static string VcpValorNome(byte code, uint v)
        {
            if (code == 0x60)
            {
                switch (v)
                {
                    case 0x01: return "VGA-1"; case 0x02: return "VGA-2";
                    case 0x03: return "DVI-1"; case 0x04: return "DVI-2";
                    case 0x05: return "Vídeo composto 1"; case 0x06: return "Vídeo composto 2";
                    case 0x07: return "S-Video-1"; case 0x08: return "S-Video-2";
                    case 0x0C: return "Componente 1"; case 0x0D: return "Componente 2"; case 0x0E: return "Componente 3";
                    case 0x0F: return "DisplayPort-1"; case 0x10: return "DisplayPort-2";
                    case 0x11: return "HDMI-1"; case 0x12: return "HDMI-2";
                    case 0x13: return "HDMI-3"; case 0x14: return "HDMI-4";
                    case 0x1B: return "USB-C"; case 0x1C: return "USB-C-2";
                }
            }
            if (code == 0x14)
            {
                switch (v)
                {
                    case 0x01: return "sRGB"; case 0x02: return "Nativo do painel";
                    case 0x03: return "4000 K"; case 0x04: return "5000 K"; case 0x05: return "6500 K";
                    case 0x06: return "7500 K"; case 0x07: return "8200 K"; case 0x08: return "9300 K";
                    case 0x09: return "10000 K"; case 0x0A: return "11500 K";
                    case 0x0B: return "Usuário 1"; case 0x0C: return "Usuário 2"; case 0x0D: return "Usuário 3";
                }
            }
            if (code == 0xD6)
            {
                switch (v)
                {
                    case 0x01: return "Ligado"; case 0x02: return "Standby"; case 0x03: return "Suspend";
                    case 0x04: return "Desligado"; case 0x05: return "Modo de baixo consumo";
                }
            }
            if (code == 0x8D)
            {
                switch (v) { case 0x01: return "Mudo"; case 0x02: return "Som ligado"; }
            }
            if (code == 0x82 || code == 0x84)
            {
                switch (v) { case 0x00: return "Normal"; case 0x01: return "Invertido"; }
            }
            if (code == 0xAA)
            {
                switch (v) { case 0x01: return "0°"; case 0x02: return "90°"; case 0x03: return "180°"; case 0x04: return "270°"; }
            }
            if (code == 0x86)
            {
                switch (v)
                {
                    case 0x01: return "Sem escala"; case 0x02: return "Máximo sem distorção";
                    case 0x03: return "Máx. vertical"; case 0x04: return "Máx. horizontal";
                    case 0x09: return "Squeeze";
                }
            }
            if (code == 0x0C && v >= 1000) return v + " K";
            if (code == 0xAE) return (v / 100.0).ToString("0.00") + " Hz";
            if (code == 0xAC) return (v / 1000.0).ToString("0.00") + " kHz";
            if (code == 0xC0) return v + " h";
            return null;
        }

        // ---------- EDID detalhado (marca, modelo, ano, polegadas, serial) ----------
        public class EdidInfo
        {
            public string Marca = "", Modelo = "", Serial = "";
            public int LarguraCm, AlturaCm, Ano, Semana;
        }
        public static EdidInfo LerEdid(string monitorId)
        {
            var e = new EdidInfo();
            var b = EdidDoMonitor(monitorId);
            if (b == null || b.Length < 128) return e;
            string id = MarcaDoEdid(b);
            e.Marca = string.IsNullOrEmpty(id) ? "" : id;
            e.Modelo = NomeDoEdid(b);
            if (e.Modelo == null) e.Modelo = "";
            e.Semana = b[16];
            e.Ano = 1990 + b[17];
            e.LarguraCm = b[21]; e.AlturaCm = b[22];
            // descriptor 0xFF = número de série
            for (int d = 0; d < 4; d++)
            {
                int o = 54 + d * 18;
                if (o + 17 >= b.Length) break;
                if (b[o] != 0 || b[o + 1] != 0 || b[o + 2] != 0 || b[o + 3] != 0xFF) continue;
                var sb = new StringBuilder();
                for (int i = o + 5; i <= o + 17; i++)
                    if (b[i] >= 32 && b[i] < 127) sb.Append((char)b[i]);
                e.Serial = sb.ToString().Trim(' ', '\n', '\r', '\0');
                break;
            }
            return e;
        }

        // ---------- curva gamma ----------
        // gammaBoost > 1 clareia meios-tons (1.0 = identidade)
        // gain = multiplicador por canal; black = levanta o preto (0..1)
        public static short[] BuildRamp(double gammaBoost, double brightness, double black, double gainR, double gainG, double gainB)
        {
            if (gammaBoost < 0.4) gammaBoost = 0.4;
            if (gammaBoost > 3.0) gammaBoost = 3.0;
            var ramp = new short[768];
            double[] gains = { gainR, gainG, gainB };
            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0;
                double y = Math.Pow(x, 1.0 / gammaBoost);
                for (int ch = 0; ch < 3; ch++)
                {
                    double v = y * gains[ch] * brightness + black;
                    if (v < 0) v = 0; if (v > 1) v = 1;
                    int iv = (int)Math.Round(v * 65535.0);
                    ramp[ch * 256 + i] = (short)iv;
                }
            }
            return ramp;
        }
    }
}
