// Adlx.cs — saturação pelo driver AMD (ADLX), sem depender do OSD do monitor.
//
// A LUT de gamma do Windows é por canal e não consegue mexer em saturação; o app
// AMD usa a ADLX (amdadlx64.dll) para isso. Aqui falamos direto com as interfaces
// C++ da ADLX (vtable) — só leitura/escrita de saturação, com fallback silencioso
// quando não há driver AMD. O DDC 0x8A continua como plano B.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ColorMatch
{
    public static class Adlx
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct IntRange { public int minValue; public int maxValue; public int step; }

        [DllImport("amdadlx64.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADLXQueryFullVersion(out ulong version);
        [DllImport("amdadlx64.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADLXInitialize(ulong version, out IntPtr system);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetDispServices(IntPtr self, out IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetDisplays(IntPtr self, out IntPtr list);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetCustomColor(IntPtr self, IntPtr display, out IntPtr cc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FnSize(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnAt(IntPtr self, uint index, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnName(IntPtr self, out IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnIsSat(IntPtr self, out int supported);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSat(IntPtr self, out int val);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSat(IntPtr self, int val);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetRange(IntPtr self, out IntRange range);

        static readonly object _lock = new object();
        static bool _tentou, _ok;
        static IntPtr _sys, _services;
        static readonly List<IntPtr> _colors = new List<IntPtr>();
        static readonly List<string> _names = new List<string>();
        static readonly List<IntRange> _ranges = new List<IntRange>();

        public static bool Disponivel { get { Init(); return _ok; } }

        static T Call<T>(IntPtr obj, int index) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            IntPtr fp = Marshal.ReadIntPtr(vt, index * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fp, typeof(T));
        }

        static void Init()
        {
            lock (_lock)
            {
                if (_tentou) return;
                _tentou = true;
                try
                {
                    ulong ver;
                    if (ADLXQueryFullVersion(out ver) != 0) return;
                    if (ADLXInitialize(ver, out _sys) != 0 || _sys == IntPtr.Zero) return;

                    IntPtr services;
                    if (Call<FnGetDispServices>(_sys, 3)(_sys, out services) != 0 || services == IntPtr.Zero) return;
                    _services = services;

                    IntPtr list;
                    if (Call<FnGetDisplays>(services, 4)(services, out list) != 0 || list == IntPtr.Zero) return;
                    uint n = Call<FnSize>(list, 3)(list);

                    for (uint i = 0; i < n; i++)
                    {
                        IntPtr disp;
                        if (Call<FnAt>(list, 11)(list, i, out disp) != 0 || disp == IntPtr.Zero) continue;

                        IntPtr np;
                        string nome = "";
                        if (Call<FnName>(disp, 6)(disp, out np) == 0 && np != IntPtr.Zero)
                            nome = Marshal.PtrToStringAnsi(np);

                        IntPtr cc;
                        if (Call<FnGetCustomColor>(services, 16)(services, disp, out cc) != 0 || cc == IntPtr.Zero) continue;
                        int sup;
                        Call<FnIsSat>(cc, 7)(cc, out sup);
                        if (sup == 0) continue;

                        IntRange r;
                        Call<FnGetRange>(cc, 8)(cc, out r);
                        _colors.Add(cc);
                        _names.Add(nome == null ? "" : nome);
                        _ranges.Add(r);
                    }
                    _ok = _colors.Count > 0;
                }
                catch { _ok = false; }
            }
        }

        static int Achar(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return -1;
            for (int i = 0; i < _names.Count; i++)
                if (string.Equals(_names[i], nome, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static bool Ler(string nome, out int valor, out int min, out int max)
        {
            valor = 0; min = 0; max = 0;
            Init();
            if (!_ok) return false;
            int i = Achar(nome);
            if (i < 0) return false;
            int v;
            if (Call<FnGetSat>(_colors[i], 9)(_colors[i], out v) != 0) return false;
            valor = v;
            min = _ranges[i].minValue;
            max = _ranges[i].maxValue;
            return true;
        }

        public static bool Definir(string nome, int valor)
        {
            Init();
            if (!_ok) return false;
            int i = Achar(nome);
            if (i < 0) return false;
            if (valor < _ranges[i].minValue) valor = _ranges[i].minValue;
            if (valor > _ranges[i].maxValue) valor = _ranges[i].maxValue;
            return Call<FnSetSat>(_colors[i], 10)(_colors[i], valor) == 0;
        }

        // -1 = não mexe (perfil antigo/sem saturação configurada)
        public static void Aplicar(string nome, int valor)
        {
            if (valor >= 0) Definir(nome, valor);
        }
    }
}
