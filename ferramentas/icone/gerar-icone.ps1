# ============================================================
#  Desenha o icone do CircuitSketch e grava:
#    app\assets\CircuitSketch.ico       o do executavel e das janelas
#    ferramentas\instalador\logo.png    o do topo do instalador
#    ferramentas\icone\previa.png       todos os tamanhos, para conferir
#
#  Desenhado em codigo, e nao exportado de um editor de imagem:
#  cada tamanho e desenhado de novo, e os pequenos (16 a 24 px)
#  perdem detalhe de proposito -- um chip com seis pinos em 16 px
#  vira borrao.
#
#  O desenho: um chip escuro com um sinal digital verde, sobre o
#  azul do botao "+" do programa. O verde e o do botao de rodar.
#
#  Uso: powershell -ExecutionPolicy Bypass -File ferramentas\icone\gerar-icone.ps1
# ============================================================

$ErrorActionPreference = 'Stop'
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconeCircuitSketch
{
    static readonly int[] Tamanhos = { 16, 20, 24, 32, 40, 48, 64, 256 };

    static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var p = new GraphicsPath();
        float d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Bitmap Desenhar(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float k = s / 256f;
            bool pequeno = s <= 24;

            // Fundo
            float m = pequeno ? 0 : 8 * k;
            var fundo = new RectangleF(m, m, s - 2 * m, s - 2 * m);
            using (var p = Arredondado(fundo, 56 * k))
            using (var b = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s),
                       Color.FromArgb(0x3A, 0x9B, 0xF5), Color.FromArgb(0x0D, 0x47, 0xA1)))
                g.FillPath(b, p);

            // Chip
            float lado = (pequeno ? 150 : 128) * k;
            float cx = s / 2f, cy = s / 2f;
            var chip = new RectangleF(cx - lado / 2, cy - lado / 2, lado, lado);

            // Pinos: tres por lado; nos pequenos, dois e mais grossos.
            int pinos = pequeno ? 2 : 3;
            float largura = (pequeno ? 30 : 17) * k;
            float comprimento = (pequeno ? 32 : 28) * k;
            float sobra = 6 * k;
            using (var pb = new SolidBrush(Color.FromArgb(0xEC, 0xF1, 0xF6)))
                for (int i = 0; i < pinos; i++)
                {
                    float t = (i + 1f) / (pinos + 1f);
                    float px = chip.X + lado * t - largura / 2;
                    float py = chip.Y + lado * t - largura / 2;
                    g.FillRectangle(pb, px, chip.Y - comprimento, largura, comprimento + sobra);
                    g.FillRectangle(pb, px, chip.Bottom - sobra, largura, comprimento + sobra);
                    g.FillRectangle(pb, chip.X - comprimento, py, comprimento + sobra, largura);
                    g.FillRectangle(pb, chip.Right - sobra, py, comprimento + sobra, largura);
                }

            using (var p = Arredondado(chip, 18 * k))
            using (var cb = new SolidBrush(Color.FromArgb(0x1A, 0x1D, 0x22)))
                g.FillPath(cb, p);

            // Sinal digital
            float espessura = (pequeno ? 22 : 14) * k;
            float x0 = chip.X + lado * 0.2f, x1 = chip.Right - lado * 0.2f;
            float alto = cy - lado * 0.17f, baixo = cy + lado * 0.17f;
            PointF[] pontos;
            if (pequeno)
            {
                float meio = (x0 + x1) / 2;
                pontos = new[] { new PointF(x0, baixo), new PointF(meio, baixo), new PointF(meio, alto), new PointF(x1, alto) };
            }
            else
            {
                float passo = (x1 - x0) / 4;
                pontos = new[]
                {
                    new PointF(x0, baixo), new PointF(x0 + passo, baixo),
                    new PointF(x0 + passo, alto), new PointF(x0 + 2 * passo, alto),
                    new PointF(x0 + 2 * passo, baixo), new PointF(x0 + 3 * passo, baixo),
                    new PointF(x0 + 3 * passo, alto), new PointF(x1, alto)
                };
            }
            using (var pen = new Pen(Color.FromArgb(0x3D, 0xDC, 0x6A), espessura))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, pontos);
            }
        }
        return bmp;
    }

    // ---- .ico ----
    // 256 px vai como PNG (o que o Windows espera nesse tamanho); os
    // menores vao como bitmap de 32 bits, que qualquer leitor de icone
    // entende -- inclusive o do WPF e o do compilador de recursos.
    public static void GravarIco(string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        var imagens = new List<byte[]>();
        foreach (int s in Tamanhos)
            using (var b = Desenhar(s))
                imagens.Add(s >= 256 ? Png(b) : Dib(b));

        using (var f = new FileStream(caminho, FileMode.Create))
        using (var w = new BinaryWriter(f))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)Tamanhos.Length);
            int offset = 6 + 16 * Tamanhos.Length;
            for (int i = 0; i < Tamanhos.Length; i++)
            {
                int s = Tamanhos[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(imagens[i].Length);
                w.Write(offset);
                offset += imagens[i].Length;
            }
            foreach (var im in imagens) w.Write(im);
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static byte[] Dib(Bitmap b)
    {
        int s = b.Width;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2);
            w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    Color c = b.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            // Mascara AND toda zero: a transparencia vem do alfa.
            int linha = ((s + 31) / 32) * 4;
            w.Write(new byte[linha * s]);
            w.Flush();
            return ms.ToArray();
        }
    }

    public static void GravarPng(string caminho, int s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        using (var b = Desenhar(s)) b.Save(caminho, ImageFormat.Png);
    }

    // Tamanho real numa faixa escura e numa clara, e os pequenos
    // ampliados sem suavizar, para ver pixel a pixel.
    public static void GravarPrevia(string caminho)
    {
        using (var tela = new Bitmap(760, 400))
        using (var g = Graphics.FromImage(tela))
        {
            g.Clear(Color.FromArgb(0x20, 0x20, 0x20));
            using (var clara = new SolidBrush(Color.FromArgb(0xF3, 0xF3, 0xF3)))
                g.FillRectangle(clara, 0, 330, 760, 70);

            using (var b = Desenhar(256)) g.DrawImage(b, 20, 20, 256, 256);

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int x = 300;
            foreach (int s in new[] { 16, 24, 32, 48 })
            {
                int z = s <= 24 ? 5 : s <= 32 ? 4 : 3;
                using (var b = Desenhar(s)) g.DrawImage(b, x, 20, s * z, s * z);
                x += s * z + 14;
            }

            g.PixelOffsetMode = PixelOffsetMode.None;
            x = 20;
            foreach (int s in Tamanhos)
            {
                if (s > 64) continue;
                using (var b = Desenhar(s))
                {
                    g.DrawImageUnscaled(b, x, 290 - s / 2);
                    g.DrawImageUnscaled(b, x, 365 - s / 2);
                }
                x += s + 18;
            }
            tela.Save(caminho, ImageFormat.Png);
        }
    }
}
'@

[IconeCircuitSketch]::GravarIco((Join-Path $raiz 'app\assets\CircuitSketch.ico'))
[IconeCircuitSketch]::GravarPng((Join-Path $raiz 'ferramentas\instalador\logo.png'), 256)
[IconeCircuitSketch]::GravarPrevia((Join-Path $PSScriptRoot 'previa.png'))
Write-Host 'icone gravado' -ForegroundColor Green
