using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Simulador.App;

/// <summary>
/// O multimetro (a aba MULTÍMETRO), a selecao por camadas, a paleta que nao trava,
/// a visao em cinza transparente, as variaveis ao vivo e o nome na barra.
/// </summary>
public static partial class Autoteste
{
    static void LigarFio(Circuito c, string a, string pa, string b, string pb, string estilo = null)
        => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb, Estilo = estilo ?? Fio.EstiloSolto });

    // =====================================================================
    //  o multimetro
    // =====================================================================

    static void Multimetro_Medidas()
    {
        saida.AppendLine("o multimetro: V~, V⎓, Ω, continuidade e A");

        // Um divisor de 1k + 1k no 5V do Uno, e um resistor de 1k no pino 9 em PWM (analogWrite 128).
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true });
        c.Pecas.Add(new PecaNoCircuito { Id = "r1", Tipo = "resistor", X = 420, Y = 40, Ohms = 1000 });
        c.Pecas.Add(new PecaNoCircuito { Id = "r2", Tipo = "resistor", X = 420, Y = 160, Ohms = 1000 });
        c.Pecas.Add(new PecaNoCircuito { Id = "r3", Tipo = "resistor", X = 420, Y = 280, Ohms = 1000 });
        c.Pecas.Add(new PecaNoCircuito { Id = "r4", Tipo = "resistor", X = 420, Y = 400, Ohms = 1000 });   // solto
        LigarFio(c, "placa", "5V", "r1", "1");
        LigarFio(c, "r1", "2", "r2", "1", Fio.EstiloReto);
        LigarFio(c, "r2", "2", "placa", "GND.1");
        LigarFio(c, "placa", "9", "r3", "1");
        LigarFio(c, "r3", "2", "placa", "GND.2");
        const string sketch = "void setup() { pinMode(9, OUTPUT); analogWrite(9, 128); }\nvoid loop() {}";

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(c, sketch);
            var ed = janela.TesteEditorCircuito;
            var painel = janela.TesteMultimetro;

            // ---- parado: a aba vira o multimetro, e o ohmimetro ja mede ----
            janela.MostrarModo("circuito");
            Conferir("multimetro: a aba MULTÍMETRO mostra o aparelho e entra no modo de so medir",
                ed.SoMedir && painel.Visibility == Visibility.Visible, $"SoMedir={ed.SoMedir} painel={painel.Visibility}");

            painel.EscolherParaTeste(FuncaoDoMultimetro.Resistencia);
            ed.PorPontaNoPino("r1", "1", preta: false);
            ed.PorPontaNoPino("r1", "2", preta: true);
            var r = janela.TesteMedir();
            Conferir("Ω: entre as pernas do resistor de 1 kΩ (com o circuito parado) le 1.000 kΩ",
                r.Visor == "1.000" && r.Unidade == "kΩ", $"{r.Visor} {r.Unidade} | {r.Nota}");

            ed.PorPontaNoPino("r1", "1", preta: false);
            ed.PorPontaNoPino("r2", "2", preta: true);
            r = janela.TesteMedir();
            Conferir("Ω: os dois em serie (do 5V ao GND, por fora da placa) somam 2.000 kΩ",
                r.Visor == "2.000" && r.Unidade == "kΩ", $"{r.Visor} {r.Unidade}");

            ed.PorPontaNoPino("r3", "1", preta: false);
            ed.PorPontaNoPino("r1", "2", preta: true);
            var porFora = janela.TesteMedir();
            ed.PorPontaNoPino("r4", "1", preta: false);
            r = janela.TesteMedir();
            Conferir("Ω: do pino 9 ao meio do divisor o caminho e r3 + GND + r2 (2.000 kΩ); ate um resistor solto, OL",
                porFora.Visor == "2.000" && porFora.Unidade == "kΩ" && r.Fora && r.Visor == "OL",
                $"{porFora.Visor} {porFora.Unidade} e {r.Visor} {r.Unidade}");

            painel.EscolherParaTeste(FuncaoDoMultimetro.Continuidade);
            ed.PorPontaNoPino("r1", "2", preta: false);
            ed.PorPontaNoPino("r2", "1", preta: true);
            var bipa = janela.TesteMedir();
            ed.PorPontaNoPino("r1", "1", preta: true);
            var naoBipa = janela.TesteMedir();
            Conferir("continuidade: no mesmo fio bipa (0 Ω); atraves do resistor de 1 kΩ, nao",
                bipa.Bipe && bipa.Valor == 0 && !naoBipa.Bipe, $"{bipa.Visor}/{bipa.Bipe} e {naoBipa.Visor}/{naoBipa.Bipe}");

            painel.EscolherParaTeste(FuncaoDoMultimetro.TensaoContinua);
            r = janela.TesteMedir();
            Conferir("V⎓ com o circuito parado: 0, e a nota diz para rodar", r.Valor == 0 && r.Nota.Contains("rode"), r.Nota);

            // ---- rodando ----
            janela.TesteRodar();
            EsperarPor(() => ed.EnergiaAtual is not null && ed.EnergiaAtual.ContinuaEntre(
                ed.EnergiaAtual.RaizDoPino("r3", "1"), null) > 1, 6000);

            ed.TirarPontas();
            ed.PorPontaNoPino("r1", "2", preta: false);
            r = janela.TesteMedir();
            double meio = r.Valor ?? -1;
            saida.AppendLine($"        meio do divisor: {r.Visor} {r.Unidade}");
            Conferir("V⎓: o meio do divisor (sem a ponta preta: contra o terra) da a metade dos 5 V",
                meio is > 2.2 and < 2.6 && r.Unidade == "V" && r.Nota?.Contains("terra") == true, $"{r.Visor} {r.Unidade} | {r.Nota}");

            ed.PorPontaNoPino("r1", "1", preta: true);
            r = janela.TesteMedir();
            Conferir("V⎓: com a ponta preta no 5V, o meio fica negativo (o sinal no visor)",
                r.Valor is < -2.2 and > -2.6 && r.Visor.StartsWith('-'), $"{r.Visor} {r.Unidade}");

            // O fio clicado: a ponta no fio mede o no dele.
            var a = ed.PosicaoPino("r1", "2")!.Value;
            var b = ed.PosicaoPino("r2", "1")!.Value;
            var meioDoFio = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var pontaNoFio = ed.PontaNoPonto(meioDoFio);
            ed.TirarPontas();
            if (pontaNoFio is not null) ed.PorPonta(pontaNoFio, preta: false);
            r = janela.TesteMedir();
            Conferir("V⎓: clicar no fio mede a tensao dele, e a leitura aparece do lado da ponta",
                pontaNoFio is { Fio: >= 0 } && r.Valor is > 2.2 and < 2.6 && ed.TextoDaLeitura.EndsWith(" V"),
                $"{pontaNoFio?.Rotulo} -> {r.Visor} {r.Unidade} | etiqueta '{ed.TextoDaLeitura}'");

            painel.EscolherParaTeste(FuncaoDoMultimetro.Corrente);
            ed.PorPontaNoPino("r1", "1", preta: false);
            r = janela.TesteMedir();
            saida.AppendLine($"        corrente no divisor: {r.Visor} {r.Unidade}");
            Conferir("A: pelo resistor do divisor passam 5 V / 2 kΩ = 2,5 mA",
                r.Unidade == "mA" && r.Valor is > 0.0022 and < 0.0026, $"{r.Visor} {r.Unidade}");

            painel.EscolherParaTeste(FuncaoDoMultimetro.TensaoAlternada);
            ed.PorPontaNoPino("r3", "1", preta: false);
            ed.PorPontaNoPino("r3", "2", preta: true);
            var ac = janela.TesteMedir();
            painel.EscolherParaTeste(FuncaoDoMultimetro.TensaoContinua);
            var dc = janela.TesteMedir();
            ed.PorPontaNoPino("r1", "2", preta: false);
            painel.EscolherParaTeste(FuncaoDoMultimetro.TensaoAlternada);
            var semAc = janela.TesteMedir();
            saida.AppendLine($"        pino 9 em PWM: {ac.Visor} {ac.Unidade} AC, {dc.Visor} {dc.Unidade} DC");
            Conferir("V~: o PWM de 50% de 5 V tem 2,5 V eficazes de alternada (e 2,5 V de continua); o divisor nao tem alternada",
                ac.Valor is > 2.2 and < 2.6 && dc.Valor is > 2.2 and < 2.6 && semAc.Valor is >= 0 and < 0.01,
                $"AC {ac.Visor}, DC {dc.Visor}, divisor AC {semAc.Visor}");

            // ---- nada se edita ----
            int fios = c.Fios.Count, pecas = c.Pecas.Count;
            double x0 = c.Pecas[1].X;
            var centroDoR1 = new Point(c.Pecas[1].X + 19, c.Pecas[1].Y + 4);
            ed.ApertarNoMultimetro(centroDoR1, new Point(100, 100), shift: false);
            ed.Mover(new Point(centroDoR1.X + 60, centroDoR1.Y + 40), true);
            ed.Soltar();
            ed.CliqueDireitoNoMultimetro(ed.PosicaoPino("r2", "2")!.Value);
            Conferir("multimetro: clicar e arrastar nao seleciona nem move peca, nao liga fio; o direito poe a ponta preta",
                ed.QuantidadeSelecionada == 0 && ed.FiosSelecionados.Count == 0 && c.Fios.Count == fios && c.Pecas.Count == pecas
                && c.Pecas[1].X == x0 && ed.PontaPreta is { Peca: "r2", Pino: "2" },
                $"sel={ed.QuantidadeSelecionada} fios={c.Fios.Count} x={c.Pecas[1].X} preta={ed.PontaPreta?.Rotulo}");

            janela.MostrarModo("simulacao");
            Conferir("multimetro: voltando para a SIMULAÇÃO o aparelho some e a edicao volta",
                !ed.SoMedir && painel.Visibility != Visibility.Visible, $"SoMedir={ed.SoMedir}");
        }
        finally { janela.TesteParar(); }

        // A faixa automatica.
        Conferir("faixa automatica: 0,5123 V -> 512.3 mV; 4700 Ω -> 4.700 kΩ; 0,0153 A -> 15.30 mA; 12 V -> 12.00 V",
            Multimetro.Formatar(0.5123, "V") == ("512.3", "mV") && Multimetro.Formatar(4700, "Ω") == ("4.700", "kΩ")
            && Multimetro.Formatar(0.0153, "A") == ("15.30", "mA") && Multimetro.Formatar(12, "V") == ("12.00", "V"),
            $"{Multimetro.Formatar(0.5123, "V")} {Multimetro.Formatar(4700, "Ω")} {Multimetro.Formatar(0.0153, "A")}");
    }

    // =====================================================================
    //  a selecao por camadas
    // =====================================================================

    static void Camadas_CliqueDeNovo()
    {
        saida.AppendLine("camadas: clicar de novo no mesmo lugar pega a de baixo");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Id = "pb", Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
        var esp = new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 160, Y = 20 };
        c.Pecas.AddRange([placa, esp]);

        var defPb = Catalogo.Obter(placa.Tipo);
        var defEsp = Catalogo.Obter(esp.Tipo);
        var (larg, alt) = Geometria.Tamanho(esp, defEsp);
        var centro = new Point(esp.X + larg / 2, esp.Y + alt / 2);
        var furos = defPb.Pinos.Select(p => (p.Nome, Pos: Geometria.PosicaoPino(placa, defPb, p))).ToList();
        double linha = furos.MinBy(f => Math.Abs(f.Pos.Y - centro.Y)).Pos.Y;
        var daLinha = furos.Where(f => Math.Abs(f.Pos.Y - linha) < 0.5).OrderBy(f => f.Pos.X).ToList();
        var esquerda = daLinha.Last(f => f.Pos.X < centro.X - larg);
        var direita = daLinha.First(f => f.Pos.X > centro.X + larg);
        LigarFio(c, "pb", esquerda.Nome, "pb", direita.Nome, Fio.EstiloReto);
        double colunaPerto = daLinha.MinBy(f => Math.Abs(f.Pos.X - centro.X)).Pos.X;
        var ponto = new Point(colunaPerto + 4.8, linha + 4.8);

        var ed = new EditorCircuito();
        ed.Carregar(c);
        var vistos = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            ed.CliqueParaTeste(ponto);
            vistos.Add(ed.FioSelecionado >= 0 ? "fio" : ed.PecaSelecionada ?? "(nada)");
        }
        saida.AppendLine("        cliques: " + string.Join(" -> ", vistos));
        Conferir("camadas: 1 clique a ESP32, 2 a protoboard, 3 o fio debaixo, 4 a ESP32 de novo",
            vistos.SequenceEqual(["esp", "pb", "fio", "esp"]), string.Join(" -> ", vistos));

        // Arrastar depois do clique de novo move o que esta selecionado, e nao troca a camada.
        ed.CliqueParaTeste(new Point(ponto.X + 200, ponto.Y + 400));
        ed.CliqueParaTeste(ponto);
        double xAntes = esp.X;
        ed.ApertarDeNovo(ponto);
        ed.Mover(new Point(ponto.X + 30, ponto.Y), true);
        ed.Soltar();
        Conferir("camadas: apertar de novo e arrastar move a ESP32 selecionada (e nao passa para a protoboard)",
            ed.PecaSelecionada == "esp" && esp.X > xAntes + 10, $"selecionada={ed.PecaSelecionada} x {xAntes} -> {esp.X}");
    }

    // =====================================================================
    //  a paleta e a visao em cinza
    // =====================================================================

    static void Paleta_SemTravar()
    {
        saida.AppendLine("paleta: abre sem ler todas as artes");
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var janela = new JanelaPecas(null);
        long ms = relogio.ElapsedMilliseconds;
        janela.Close();
        saida.AppendLine($"        montou a lista em {ms} ms");
        Conferir("paleta: a janela do + monta a lista na hora (as miniaturas vem depois, so as que aparecem)",
            ms < 1500 && janela.TesteLinhasNaLista > 100, $"{ms} ms, {janela.TesteLinhasNaLista} linhas");

        var miniatura = JanelaPecas.Miniatura("esp32-devkit-v1") as BitmapSource;
        Conferir("paleta: a miniatura e um bitmap pequeno (e nao o desenho vetorial inteiro)",
            miniatura is { PixelWidth: 76, PixelHeight: 56 } && miniatura.IsFrozen, miniatura is null ? "(nada)" : $"{miniatura.PixelWidth}x{miniatura.PixelHeight}");
    }

    static void Visao_CinzaTransparente()
    {
        saida.AppendLine("multimetro: as pecas em cinza e transparentes, sem a caixa preta");
        var arte = Pecas.Obter("led");
        var cinza = Pecas.Cinza(arte, arte.Width, arte.Height) as BitmapSource;
        // Quadro apos quadro o editor pede o cinza de novo: tem que ser o mesmo, e nao um mais apagado.
        bool estavel = ReferenceEquals(Pecas.Cinza(cinza, arte.Width, arte.Height), cinza);
        bool ok = cinza is not null && Pecas.EhCinza(cinza) && cinza.Format == PixelFormats.Pbgra32 && estavel;
        int transparentes = 0, opacoDemais = 0, colorido = 0, visiveis = 0, total = 0;
        if (ok)
        {
            int passo = cinza.PixelWidth * 4;
            var px = new byte[passo * cinza.PixelHeight];
            cinza.CopyPixels(px, passo, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                total++;
                if (px[i + 3] == 0) transparentes++;
                if (px[i + 3] > 40) visiveis++;
                if (px[i + 3] > Math.Ceiling(255 * Pecas.OpacidadeDoCinza)) opacoDemais++;
                if (px[i] != px[i + 1] || px[i + 1] != px[i + 2]) colorido++;
            }
        }
        if (Environment.GetEnvironmentVariable("CS_SALVAR_CINZA") is { Length: > 0 } arquivo && cinza is not null)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(cinza));
            using var f = System.IO.File.Create(arquivo);
            png.Save(f);
        }
        Conferir("cinza: o fundo da peca fica transparente (nao preto), a peca aparece meio apagada e sem cor",
            ok && transparentes > 0 && visiveis > total / 10 && opacoDemais == 0 && colorido == 0,
            $"transparentes={transparentes} visiveis={visiveis}/{total} opacos demais={opacoDemais} coloridos={colorido}");
    }

    // =====================================================================
    //  as variaveis ao vivo
    // =====================================================================

    static void Depurador_AoVivo()
    {
        saida.AppendLine("depurador: as variaveis ao vivo, sem pausar");
        const string sketch = "int contador = 0;\nvoid setup() { }\nvoid loop() {\n  contador++;\n  delay(20);\n}";

        foreach (bool nativo in CompiladorNativo.Disponivel ? new[] { false, true } : [false])
        {
            string como = nativo ? "nativo" : "interpretador";
            bool eraNativo = Compilador.CppNativo;
            Compilador.CppNativo = nativo;
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true, Sketch = sketch });
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, sketch);
                janela.TesteRodar();
                EsperarPor(() => janela.TestePlacaRodando("placa"), 8000);
                janela.TesteAoVivo(true);

                var valores = new HashSet<string>();
                string titulo = "";
                EsperarPor(() =>
                {
                    var (t, texto, visivel) = janela.TestePainelDeVariaveis;
                    var achado = System.Text.RegularExpressions.Regex.Match(texto ?? "", @"contador = (\d+)");
                    if (visivel && achado.Success) { valores.Add(achado.Groups[1].Value); titulo = t; }
                    return valores.Count >= 3;
                }, 8000);
                bool rodando = janela.TestePlacaRodando("placa") && !janela.TesteStatus.Contains("pausado");
                Conferir($"variaveis ao vivo ({como}): o painel mostra o contador mudando, com o sketch rodando (sem pausar)",
                    valores.Count >= 3 && titulo.StartsWith("AO VIVO") && rodando,
                    $"{valores.Count} valores ({string.Join(",", valores)}) | '{titulo}' | rodando={rodando} | {janela.TesteStatus}");
                janela.TesteAoVivo(false);
            }
            finally
            {
                janela.TesteParar();
                Compilador.CppNativo = eraNativo;
            }
        }
    }

    // =====================================================================
    //  o nome na barra
    // =====================================================================

    static void Barra_NomeDoCircuito()
    {
        saida.AppendLine("barra: o nome do circuito e o autor do lado do Mandar");
        var c = new Circuito { Nome = "Semaforo da turma", Autor = "Prof. Joao" };
        c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0 });
        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(c, "void setup() {}\nvoid loop() {}");
            var (nome, autor, dica) = janela.TesteBarraDeCima;
            Conferir("barra: o nome e o autor aparecem do lado dos botoes de mandar o codigo, e a contagem de pecas saiu",
                nome == "Semaforo da turma" && autor == "por Prof. Joao" && !dica.Contains("arraste no vazio"),
                $"'{nome}' '{autor}' dica='{dica}'");
            var itens = janela.TesteItensDeExportar();
            Conferir("menu: com o plugin do Wokwi ligado, aparece o \"Exportar para o Wokwi\" logo abaixo do Exportar",
                itens.Contains("Exportar para o Wokwi (diagram.json)..."), string.Join(" | ", itens));
        }
        finally { janela.TesteParar(); }
    }
}
