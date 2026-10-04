<#
.SYNOPSIS
    Draws the SideDim icon (three monitors, the outer two dimmed) and writes:
      src/Assets/SideDim.ico   multi-size icon for the exe, tray and window
      docs/logo.png            256 px PNG for places that can't show SVG
    docs/logo.svg is the same drawing by hand; keep the two in sync if you change the design.
#>
param(
    [string]$Ico = (Join-Path $PSScriptRoot '..\src\Assets\SideDim.ico'),
    [string]$Png = (Join-Path $PSScriptRoot '..\docs\logo.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class SideDimIcon
{
    // Design grid is 32x32. Small sizes snap to whole pixels so the tray icon stays crisp.
    static RectangleF R(float x, float y, float w, float h, int size)
    {
        float u = size / 32f;
        if (size > 32) return new RectangleF(x * u, y * u, w * u, h * u);
        return RectangleF.FromLTRB((float)Math.Round(x * u), (float)Math.Round(y * u),
                                   (float)Math.Round((x + w) * u), (float)Math.Round((y + h) * u));
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d < 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void Monitor(Graphics g, int size, float x, float w, float y, float h, Brush screen, Pen outline, Brush stand)
    {
        float u = size / 32f;
        float standW = Math.Max(w * 0.22f, 2f);
        g.FillRectangle(stand, R(x + (w - standW) / 2, y + h, standW, 2.5f, size));
        g.FillRectangle(stand, R(x + w * 0.2f, y + h + 2.5f, w * 0.6f, 1.5f, size));
        using (var path = Rounded(R(x, y, w, h, size), 1.6f * u))
        {
            g.FillPath(screen, path);
            if (outline != null) g.DrawPath(outline, path);
        }
    }

    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float u = size / 32f;

            var dimScreen = new SolidBrush(Color.FromArgb(255, 0x37, 0x41, 0x51));
            var dimStand = new SolidBrush(Color.FromArgb(255, 0x4B, 0x55, 0x63));
            var dimEdge = size >= 24 ? new Pen(Color.FromArgb(255, 0x6B, 0x72, 0x80), Math.Max(1f, u * 0.8f)) : null;
            var litStand = new SolidBrush(Color.FromArgb(255, 0xD9, 0x8E, 0x04));

            // Soft glow behind the lit screen, only where there are enough pixels for it.
            if (size >= 48)
            {
                using (var glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(R(4, 0, 24, 30, size));
                    using (var glow = new PathGradientBrush(glowPath))
                    {
                        glow.CenterColor = Color.FromArgb(110, 0xFF, 0xC8, 0x4A);
                        glow.SurroundColors = new[] { Color.FromArgb(0, 0xFF, 0xC8, 0x4A) };
                        g.FillPath(glow, glowPath);
                    }
                }
            }

            Monitor(g, size, 0.5f, 8f, 9f, 11f, dimScreen, dimEdge, dimStand);
            Monitor(g, size, 23.5f, 8f, 9f, 11f, dimScreen, dimEdge, dimStand);

            var lit = R(9.5f, 5f, 13f, 17f, size);
            using (var litScreen = new LinearGradientBrush(lit, Color.FromArgb(255, 0xFF, 0xD5, 0x6B), Color.FromArgb(255, 0xF5, 0xA5, 0x1C), 90f))
                Monitor(g, size, 9.5f, 13f, 5f, 17f, litScreen, null, litStand);

            dimScreen.Dispose(); dimStand.Dispose(); litStand.Dispose();
            if (dimEdge != null) dimEdge.Dispose();
        }
        return bmp;
    }

    // Classic 32-bit DIB entry (what every Windows API reads), used for the small sizes.
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        int maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(s * s * 4 + maskStride * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            var data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new byte[s * 4];
            for (int y = s - 1; y >= 0; y--)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                w.Write(row);
            }
            bmp.UnlockBits(data);
            w.Write(new byte[maskStride * s]); // AND mask unused: alpha does the work
            return ms.ToArray();
        }
    }

    public static void WriteIco(string path, int[] sizes)
    {
        var frames = new List<byte[]>();
        foreach (var size in sizes)
        {
            using (var bmp = Draw(size))
            {
                if (size >= 256)
                {
                    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); frames.Add(ms.ToArray()); }
                }
                else frames.Add(Dib(bmp));
            }
        }

        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(frames[i].Length); w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var f in frames) w.Write(f);
        }
    }

    public static void WritePng(string path, int size)
    {
        using (var bmp = Draw(size)) bmp.Save(path, ImageFormat.Png);
    }
}
'@

foreach ($file in $Ico, $Png) { New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null }
[SideDimIcon]::WriteIco((Resolve-Path (Split-Path $Ico)).Path + '\' + (Split-Path $Ico -Leaf), @(16, 20, 24, 32, 40, 48, 64, 128, 256))
[SideDimIcon]::WritePng((Resolve-Path (Split-Path $Png)).Path + '\' + (Split-Path $Png -Leaf), 256)
Write-Host "Wrote $Ico and $Png"
