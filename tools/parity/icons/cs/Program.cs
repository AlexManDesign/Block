using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlockcraftPort;

static class IconParityProgram
{
    // args: atlas.png ids.json out.json
    static int Main(string[] args)
    {
        byte[] rgba = DecodePng(File.ReadAllBytes(args[0]), out int w, out int h);
        MainIconPainter.Atlas = rgba; MainIconPainter.AtlasW = w; MainIconPainter.AtlasH = h;
        var ids = JsonSerializer.Deserialize<int[]>(File.ReadAllText(args[1]));
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (int id in ids)
        {
            var c = MainIconPainter.PaintSourceId(id);
            if (!first) sb.Append(','); first = false;
            sb.Append('"').Append(id).Append("\":");
            if (c == null) sb.Append("null");
            else sb.Append("{\"s\":").Append(c.S).Append(",\"px\":\"").Append(Convert.ToBase64String(c.Px)).Append("\"}");
        }
        sb.Append('}');
        File.WriteAllText(args[2], sb.ToString());
        return 0;
    }

    /// <summary>Minimal PNG decoder: 8-bit RGBA / RGB, non-interlaced (what the atlas uses).</summary>
    static byte[] DecodePng(byte[] f, out int w, out int h)
    {
        int p = 8; w = h = 0; int bitDepth = 0, colorType = 0; var idat = new MemoryStream();
        while (p < f.Length)
        {
            int len = (f[p] << 24) | (f[p + 1] << 16) | (f[p + 2] << 8) | f[p + 3];
            string type = Encoding.ASCII.GetString(f, p + 4, 4);
            if (type == "IHDR")
            {
                w = (f[p + 8] << 24) | (f[p + 9] << 16) | (f[p + 10] << 8) | f[p + 11];
                h = (f[p + 12] << 24) | (f[p + 13] << 16) | (f[p + 14] << 8) | f[p + 15];
                bitDepth = f[p + 16]; colorType = f[p + 17];
                if (f[p + 20] != 0) throw new Exception("interlaced PNG not supported");
            }
            else if (type == "IDAT") idat.Write(f, p + 8, len);
            else if (type == "IEND") break;
            p += 12 + len;
        }
        if (bitDepth != 8 || (colorType != 6 && colorType != 2)) throw new Exception($"unsupported PNG {bitDepth}/{colorType}");
        int bpp = colorType == 6 ? 4 : 3, stride = w * bpp;
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        byte[] d = raw.ToArray(); var cur = new byte[stride]; var prev = new byte[stride];
        var outp = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int ft = d[y * (stride + 1)];
            Array.Copy(d, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                int v = cur[i];
                switch (ft)
                {
                    case 1: v += a; break;
                    case 2: v += b; break;
                    case 3: v += (a + b) >> 1; break;
                    case 4: { int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break; }
                }
                cur[i] = (byte)v;
            }
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4, s = x * bpp;
                outp[o] = cur[s]; outp[o + 1] = cur[s + 1]; outp[o + 2] = cur[s + 2]; outp[o + 3] = bpp == 4 ? cur[s + 3] : (byte)255;
            }
            var t = prev; prev = cur; cur = t;
        }
        return outp;
    }
}
