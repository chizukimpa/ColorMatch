// IcoGen.cs — gera src\app.ico (ícone do ColorMatch) e um preview PNG
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class IcoGen
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2f;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Draw(int S)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float pad = Math.Max(0.6f, S * 0.035f);
            var rect = new RectangleF(pad, pad, S - pad * 2f, S - pad * 2f);
            float rad = (S - pad * 2f) * 0.245f;

            using (var path = Round(rect, rad))
            using (var br = new LinearGradientBrush(
                       new PointF(rect.Left, rect.Top), new PointF(rect.Right, rect.Bottom),
                       Color.FromArgb(255, 104, 138, 255), Color.FromArgb(255, 168, 82, 250)))
                g.FillPath(br, path);

            // brilho no topo
            var st0 = g.Save();
            using (var path = Round(rect, rad))
            {
                g.SetClip(path, CombineMode.Intersect);
                using (var br = new LinearGradientBrush(
                           new PointF(0, rect.Top), new PointF(0, rect.Top + rect.Height * 0.55f),
                           Color.FromArgb(58, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                    g.FillRectangle(br, rect);
            }
            g.Restore(st0);

            // marca: círculo meio cheio (contraste / igualar)
            float cx = S / 2f, cy = S / 2f, R = S * 0.295f;
            var st1 = g.Save();
            using (var cp = new GraphicsPath())
            {
                cp.AddEllipse(cx - R, cy - R, R * 2f, R * 2f);
                g.SetClip(cp, CombineMode.Intersect);
                using (var wb = new SolidBrush(Color.FromArgb(252, 255, 255, 255)))
                    g.FillRectangle(wb, cx - R, cy - R, R, R * 2f);
                using (var gb = new SolidBrush(Color.FromArgb(96, 255, 255, 255)))
                    g.FillRectangle(gb, cx, cy - R, R, R * 2f);
            }
            g.Restore(st1);

            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, S * 0.022f)))
                g.DrawEllipse(pen, cx - R, cy - R, R * 2f, R * 2f);
        }
        return bmp;
    }

    static byte[] BmpBytes(Bitmap bmp)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        int w = bmp.Width, h = bmp.Height;
        int xorSize = w * h * 4;
        int maskRow = ((w + 31) / 32) * 4;
        int maskSize = maskRow * h;

        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((short)1); bw.Write((short)32); bw.Write(0);
        bw.Write(xorSize + maskSize);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                Color c = bmp.GetPixel(x, y);
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
            }
        for (int y = h - 1; y >= 0; y--)
        {
            var row = new byte[maskRow];
            for (int x = 0; x < w; x++)
                if (bmp.GetPixel(x, y).A < 128) row[x / 8] |= (byte)(0x80 >> (x % 8));
            bw.Write(row);
        }
        bw.Flush();
        return ms.ToArray();
    }

    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            data[i] = BmpBytes(Draw(sizes[i]));

        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
        int off = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(data[i].Length);
            bw.Write(off);
            off += data[i].Length;
        }
        for (int i = 0; i < sizes.Length; i++) bw.Write(data[i]);
        bw.Flush();
        File.WriteAllBytes(@"src\app.ico", ms.ToArray());

        // preview: tira com alguns tamanhos sobre fundo escuro
        using (var sheet = new Bitmap(460, 300, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 16, 19, 25));
            int x = 20;
            foreach (int s in new int[] { 16, 24, 32, 48, 64, 128 })
            {
                using (var b = Draw(s))
                    g.DrawImage(b, x, 20 + (140 - s));
                x += s + 14;
            }
            x = 20;
            foreach (int s in new int[] { 16, 24, 32, 48 })
            {
                using (var b = Draw(s))
                    g.DrawImage(b, x, 200);
                x += s + 14;
            }
            sheet.Save(@"src\app_preview.png", ImageFormat.Png);
        }
        Console.WriteLine("ok: src\\app.ico + src\\app_preview.png");
    }
}
