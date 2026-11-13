using System.Windows;

namespace Simulador.App;

/// <summary>
/// Os frames da Alpha 25 (Frames.cs): os chassis de carrinho, os frames de
/// drone, as bases de braco robotico e a base importada de um SVG.
/// </summary>
public static partial class Autoteste
{
    static readonly string[] FramesDeFabrica =
    [
        "frame-2wd", "frame-4wd", "frame-4wd-placa", "frame-drone-5", "frame-drone-6",
        "frame-braco-mearm", "frame-braco-dearm", "frame-braco-servo", "frame-suporte-sg90",
    ];

    /// <summary>Um ponto da chapa e um ponto de furo do frame (em mm), achados numa varredura.</summary>
    static (Point? chapa, Point? furo) PontosDoFrame(string id)
    {
        var f = Frames.Obter(id);
        Point? chapa = null, furo = null;
        for (double y = 0.25; y < f.AlturaMm && (chapa is null || furo is null); y += 0.5)
            for (double x = 0.25; x < f.LarguraMm; x += 0.5)
            {
                var local = new Point(x * Frames.A, y * Frames.A);
                bool naPlaca = Frames.NaPlaca(id, local);
                if (naPlaca) chapa ??= new Point(x, y);
                else if (Frames.DentroDoContorno(id, local)) furo ??= new Point(x, y);
                if (chapa is not null && furo is not null) break;
            }
        return (chapa, furo);
    }

    static void Frames_ChassisEBases()
    {
        saida.AppendLine("os frames: chassis, drone, braco robotico e a base de SVG");

        // ---- o catalogo ----
        var faltando = FramesDeFabrica.Where(id => Catalogo.Obter(id) is not { Categoria: "Frames", Pinos.Count: 0 }).ToList();
        Conferir("frames: os nove de fabrica estao no catalogo, na categoria Frames e sem pino nenhum",
            faltando.Count == 0 && Frames.DeFabrica.SequenceEqual(FramesDeFabrica), string.Join(", ", faltando));

        var tamanhoErrado = FramesDeFabrica.Where(id =>
        {
            var f = Frames.Obter(id);
            var def = Catalogo.Obter(id);
            return Math.Abs(def.Largura - f.LarguraMm * Frames.A) > 0.01 || Math.Abs(def.Altura - f.AlturaMm * Frames.A) > 0.01;
        }).ToList();
        Conferir("frames: o tamanho da peca e o do desenho de corte (mm em px de 96 dpi): o 2WD tem 148,98 x 209,97 mm, o drone de 5\" 177,52 x 208,43",
            tamanhoErrado.Count == 0 && Math.Abs(Frames.Obter("frame-2wd").LarguraMm - 148.98) < 0.01
            && Math.Abs(Frames.Obter("frame-drone-5").AlturaMm - 208.43) < 0.01, string.Join(", ", tamanhoErrado));

        var semFuro = FramesDeFabrica.Where(id => PontosDoFrame(id) is not ({ }, { })).ToList();
        Conferir("frames: cada um tem chapa e furo -- o furo esta dentro do contorno mas nao e chapa",
            semFuro.Count == 0, string.Join(", ", semFuro));

        var nomes = FramesDeFabrica.Select(id => Catalogo.Obter(id).Nome).ToList();
        Conferir("frames: cada um com nome proprio (nao o id)",
            nomes.Distinct().Count() == nomes.Count && nomes.All(n => !n.StartsWith("frame", StringComparison.Ordinal)), string.Join(" | ", nomes));

        Conferir("frames: a miniatura da paleta sai do contorno",
            FramesDeFabrica.All(id => JanelaPecas.Miniatura(id) is not null));

        // ---- no circuito: o clique, o material, o arraste que leva o que esta em cima ----
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "chassi", Tipo = "frame-2wd", X = 96, Y = 96 });
        c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 160, Y = 300 });
        c.Pecas.Add(new PecaNoCircuito { Id = "bb", Tipo = "protoboard-170", X = 200, Y = 640 });
        c.Pecas.Add(new PecaNoCircuito { Id = "led", Tipo = "led", X = 1200, Y = 300 });
        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(c, "void setup() {}\nvoid loop() {}");
            var ed = janela.TesteEditorCircuito;
            var chassi = ed.Atual.Achar("chassi");
            string foto = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "circuitsketch-teste-frames.png");
            FotografarEditor(ed, foto, 1000, 700);
            saida.AppendLine($"        foto: {foto}");
            var (chapa, furo) = PontosDoFrame("frame-2wd");
            Point NoMundo(Point mm) => new(chassi.X + mm.X * Frames.A, chassi.Y + mm.Y * Frames.A);

            // Um ponto da chapa longe do Uno e da protoboard: a varredura de cima a baixo acha o da frente.
            var naChapa = NoMundo(chapa!.Value);
            var acertoChapa = ed.Resolver(naChapa);
            var acertoFuro = ed.Resolver(NoMundo(furo!.Value));
            Conferir("frames: o clique na chapa e do frame; o clique no furo nao (passa para o que esta embaixo)",
                acertoChapa.Camada == EditorCircuito.Camada.CorpoPlaca && acertoChapa.Peca?.Id == "chassi"
                && acertoFuro.Peca?.Id != "chassi",
                $"chapa: {acertoChapa.Camada} {acertoChapa.Peca?.Id}; furo: {acertoFuro.Camada} {acertoFuro.Peca?.Id}");

            var uno = ed.Atual.Achar("uno");
            var defUno = Catalogo.Obter("arduino-uno");
            var meioDoUno = new Point(uno.X + defUno.Largura / 2, uno.Y + defUno.Altura / 2);
            Conferir("frames: a placa em cima do frame continua recebendo o clique",
                ed.Resolver(meioDoUno).Peca?.Id == "uno", $"veio {ed.Resolver(meioDoUno).Peca?.Id}");

            var emCima = ed.EmCimaDoFrame(chassi).Select(p => p.Id).OrderBy(x => x).ToList();
            Conferir("frames: em cima do chassi estao o Uno e a protoboard (o LED de fora nao)",
                emCima.SequenceEqual(["bb", "uno"]), string.Join(", ", emCima));

            // O arraste: o frame anda, e o que esta em cima anda o mesmo tanto.
            double x0 = chassi.X, y0 = chassi.Y, ux = uno.X, uy = uno.Y;
            var bb = ed.Atual.Achar("bb");
            var led = ed.Atual.Achar("led");
            double bx = bb.X, lx = led.X;
            ed.Apertar(naChapa);
            ed.Mover(new Point(naChapa.X + 96, naChapa.Y + 48), true);
            ed.Soltar();
            double dx = chassi.X - x0, dy = chassi.Y - y0;
            Conferir("frames: arrastar o frame leva junto o que esta em cima, e deixa o que esta fora",
                Math.Abs(dx) > 40 && Math.Abs(uno.X - ux - dx) < 0.01 && Math.Abs(uno.Y - uy - dy) < 0.01
                && Math.Abs(bb.X - bx - dx) < 0.01 && Math.Abs(led.X - lx) < 0.01,
                $"frame ({dx:0.#};{dy:0.#}) uno ({uno.X - ux:0.#};{uno.Y - uy:0.#}) bb {bb.X - bx:0.#} led {led.X - lx:0.#}");

            ed.MoverPara(chassi, chassi.X + 20, chassi.Y);
            Conferir("frames: o mover para do X/Y tambem leva o que esta em cima",
                Math.Abs(uno.X - ux - dx - 20) < 0.01, $"uno andou {uno.X - ux:0.#}");

            // O material.
            ed.SelecionarParaTeste("chassi");
            Conferir("frames: o material de fabrica do 2WD e o acrilico transparente", Frames.MaterialDe(chassi).Id == "acrilico");
            ed.DefinirMaterialDoFrame("carbono");
            string comCarbono = chassi.Cor;
            ed.DefinirMaterialDoFrame("original");
            Conferir("frames: a barra troca o material (fica no Cor); o \"como no SVG\" so vale para a base importada",
                comCarbono == "carbono" && chassi.Cor == "carbono" && Frames.MaterialDe(chassi).Id == "carbono", chassi.Cor);

            var pelaAba = ImportarWokwi.Ler(ExportarWokwi.Gerar(ed.Atual), ed.Atual, estrito: true).Circuito;
            Conferir("frames: o diagram.json leva o frame e o material",
                pelaAba?.Achar("chassi") is { Tipo: "frame-2wd", Cor: "carbono" }, pelaAba?.Achar("chassi")?.Cor);

            // ---- a base importada de um SVG ----
            const string svg = """
                <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="60mm" viewBox="0 0 100 60">
                  <path d="M0,0 H100 V60 H0 Z" fill="none" stroke="#f00" stroke-width="0.1"/>
                  <circle cx="20" cy="30" r="5" fill="none" stroke="#f00" stroke-width="0.1"/>
                  <rect x="60" y="20" width="20" height="20" fill="none" stroke="#f00" stroke-width="0.1"/>
                </svg>
                """;
            Conferir("frames: o tamanho do SVG vem do width/height em mm",
                Frames.TamanhoDoSvg(svg) is { } t && Math.Abs(t.larg - 100) < 0.01 && Math.Abs(t.alt - 60) < 0.01);

            var importada = janela.ImportarBase("placa do robo", svg, 1.5);
            var defImportada = importada is null ? null : Catalogo.Obter(importada.Tipo);
            Point Mm(double x, double y) => new(x * Frames.A, y * Frames.A);
            Conferir("frames: a base importada entra no circuito do tamanho pedido (100 mm x 1,5 = 150 mm) e com o nome do arquivo",
                defImportada is not null && Frames.EhImportado(importada.Tipo) && Math.Abs(defImportada.Largura - 150 * Frames.A) < 0.05
                && Math.Abs(defImportada.Altura - 90 * Frames.A) < 0.05 && defImportada.Nome.Contains("placa do robo"),
                defImportada is null ? "nao entrou" : $"{defImportada.Largura / Frames.A:0.##} x {defImportada.Altura / Frames.A:0.##} mm, {defImportada.Nome}");
            if (importada is not null)
                Conferir("frames: o contorno do SVG vira a chapa, o circulo e o quadrado viram furo",
                    Frames.NaPlaca(importada.Tipo, Mm(75, 15)) && !Frames.NaPlaca(importada.Tipo, Mm(30, 45))
                    && !Frames.NaPlaca(importada.Tipo, Mm(105, 45)) && Frames.DentroDoContorno(importada.Tipo, Mm(30, 45))
                    && !Frames.NaPlaca(importada.Tipo, Mm(160, 45)));

            ed.SelecionarParaTeste(importada?.Id);
            ed.DefinirMaterialDoFrame("original");
            Conferir("frames: na base importada o \"como no SVG\" vale", importada?.Cor == "original");

            // Vai no .sktch e volta; o historico (o JSON do circuito) tambem.
            string arquivo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"circuitsketch-frames-{Guid.NewGuid():N}.sktch");
            Circuito reaberto;
            try
            {
                Sktch.Salvar(arquivo, ed.Atual, "void setup() {}\nvoid loop() {}");
                reaberto = Sktch.Abrir(arquivo).circuito;
            }
            finally { try { System.IO.File.Delete(arquivo); } catch { } }
            bool doHistorico = Circuito.TentarDeJson(ed.Atual.ParaJson(), out var peloJson, out string erro);
            Conferir("frames: a base importada vai no .sktch (o SVG junto) e no historico, e volta com a peca",
                reaberto.Bases is [{ Svg: svg, Escala: 1.5 }] && reaberto.Pecas.Any(p => p.Tipo == importada?.Tipo)
                && doHistorico && peloJson.Bases?.Count == 1,
                $"bases={reaberto.Bases?.Count} historico={doHistorico} {erro}");

            var pelaAba2 = ImportarWokwi.Ler(ExportarWokwi.Gerar(ed.Atual), ed.Atual, estrito: true).Circuito;
            Conferir("frames: a aba do diagram.json nao perde a base importada",
                pelaAba2?.Bases?.Count == 1 && pelaAba2.Pecas.Any(p => p.Tipo == importada?.Tipo));

            // Sem a peca, a base sai do arquivo.
            ed.Atual.Pecas.RemoveAll(p => p.Tipo == importada?.Tipo);
            ed.Atual.CompletarBases();
            Conferir("frames: apagada a ultima peca da base, o SVG nao fica sobrando no arquivo", ed.Atual.Bases is null);

            Conferir("frames: um SVG que nao se le nao entra",
                janela.ImportarBase("quebrado", "<svg nao fecha", 1) is null);
        }
        finally { janela.TesteParar(); }
    }
}
