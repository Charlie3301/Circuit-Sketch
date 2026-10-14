using System.IO;
using System.Text;

namespace Simulador.App;

/// <summary>
/// Roda um .sktch inteiro sem abrir janela, e conta o que aconteceu.
///
/// Existe por causa da pasta "NOT WORKING" que o professor mandou: para
/// saber se um sketch funciona nao basta ver se ele compila. O motor
/// sozinho roda o CODIGO, mas sem o CIRCUITO -- e a metade dos sketches
/// dele nao faz nada sem as pecas: o botao nunca fecha, o sensor nao
/// tem valor, o display nao existe.
///
/// Aqui o projeto e montado como a janela monta: circuito, netlist,
/// bancada, arquivos do projeto e os controles no estado inicial. O que
/// sai e um relatorio em texto -- serial, pinos que mexeram, display,
/// fita de LED, e o erro com a linha, quando houver.
/// </summary>
public static class Diagnostico
{
    public static string Rodar(string caminho, long ms = 2000)
    {
        var saida = new StringBuilder();

        Circuito circuito;
        string codigo, descricao;
        try
        {
            (circuito, codigo, descricao) = Sktch.AbrirCompleto(caminho);
        }
        catch (Exception e)
        {
            return $"NAO ABRIU: {e.GetType().Name}: {e.Message}";
        }

        saida.AppendLine($"arquivo   : {Path.GetFileName(caminho)}");
        saida.AppendLine($"circuito  : {circuito.Pecas.Count} pecas, {circuito.Fios.Count} fios");

        foreach (var grupo in circuito.Pecas.GroupBy(p => p.Tipo).OrderBy(g => g.Key))
            saida.AppendLine($"            {grupo.Count(),3}x {grupo.Key}");

        var quebrados = circuito.FiosQuebrados().ToList();
        if (quebrados.Count > 0)
            saida.AppendLine($"FIOS SOLTOS: {quebrados.Count} -> "
                             + string.Join(", ", quebrados.Take(6)));

        // Diagrama do Wokwi guardado na descricao: e o caso de varios
        // sketches da pasta, e sem isto o circuito de verdade fica de
        // fora sem ninguem perceber.
        if (ImportarWokwi.PareceDiagrama(descricao))
            saida.AppendLine("DESCRICAO  : tem um diagram.json do Wokwi dentro");

        // A arvore de componentes, do jeito que a aba mostra.
        saida.AppendLine("--- componentes ---");
        saida.Append(ArvoreDeComponentes.EmTexto(
            ArvoreDeComponentes.Montar(circuito, new Netlist(circuito))));
        saida.AppendLine("---");

        var placas = circuito.Placas().ToList();
        if (placas.Count == 0) return saida.AppendLine("SEM PLACA").ToString();

        var placa = placas[0];
        string sketch = placa.Sketch ?? codigo ?? "";
        sketch = Montagem.Montar(sketch, circuito.Arquivos);

        var linguagem = Compilador.Detectar(sketch);
        saida.AppendLine($"linguagem : {Compilador.Nome(linguagem)}");

        var falta = Acervo.OQueFalta(sketch, circuito.Arquivos);
        if (falta.Count > 0)
            saida.AppendLine("BIBLIOTECA: falta "
                             + string.Join(", ", falta.Select(f => f.Cabecalho)));

        var maq = new Maquina
        {
            Perfil = PerfilPlaca.De(placa.Tipo) ?? PerfilPlaca.Esp32,
            Rede = new Rede(),
        };
        maq.Reiniciar();

        var rede = new Netlist(circuito);
        var bancada = Bancada.DeUmaSo(maq);

        // Os controles do circuito no estado em que o arquivo os deixou:
        // e por eles que o botao esta solto e o potenciometro no meio.
        var editor = new EditorCircuito();
        editor.Carregar(circuito);
        editor.AplicarEntradas(bancada, rede);

        // O cartao com o que esta gravado na peca, como a janela faz ao
        // rodar: sem isto o jogo que le a fase do cartao caia na do codigo.
        foreach (var (pecaDoCartao, doCartao) in editor.CartoesDoCircuito(bancada, rede))
        {
            if (doCartao is null) continue;
            if (pecaDoCartao.Arquivos is not null)
                foreach (var (nome, texto) in pecaDoCartao.Arquivos) doCartao.Cartao.Gravar(nome, texto);
            if (pecaDoCartao.ArquivosDoComputador is not null)
                foreach (var (nome, doPc) in pecaDoCartao.ArquivosDoComputador)
                    if (File.Exists(doPc)) doCartao.Cartao.GravarDoDisco(nome, doPc);
            if (pecaDoCartao.PastaDoComputador is { } pasta && Directory.Exists(pasta))
                doCartao.Cartao.PastaDoComputador = pasta;
        }
        foreach (var (pecaDoPendrive, doPendrive, problema) in editor.PendrivesDoCircuito(bancada, rede))
        {
            if (doPendrive is null) continue;
            if (pecaDoPendrive.Arquivos is not null)
                foreach (var (nome, texto) in pecaDoPendrive.Arquivos) doPendrive.Pendrive.Gravar(nome, texto);
            if (pecaDoPendrive.ArquivosDoComputador is not null)
                foreach (var (nome, doPc) in pecaDoPendrive.ArquivosDoComputador)
                    if (File.Exists(doPc)) doPendrive.Pendrive.GravarDoDisco(nome, doPc);
            if (pecaDoPendrive.PastaDoComputador is { } pasta && Directory.Exists(pasta))
                doPendrive.Pendrive.PastaDoComputador = pasta;
            if (problema is null) doPendrive.PendriveConectado = () => pecaDoPendrive.Giro < 0.5;
        }

        // O display e o cartao para o codigo C (modo nativo), como no Rodar.
        var aparelhos = editor.AparelhosSpiDe(bancada, rede, maq);
        maq.AparelhosSpi = () => aparelhos;

        // Alguem digitando no monitor. O sketch da calculadora espera
        // exatamente isto, e sem ninguem digitando ele nao passa da
        // primeira linha -- na placa de verdade tambem nao passaria.
        maq.EntradaSerial.Adicionar("12" + Environment.NewLine
                                    + "7" + Environment.NewLine
                                    + "+" + Environment.NewLine);

        try
        {
            var prog = Compilador.CompilarProjeto(new Projeto
            {
                Sketch = placa.Sketch ?? codigo ?? "",
                Arquivos = circuito.Arquivos,
                Placa = placa.Tipo,
                Bibliotecas = Acervo.ParaCompilar(),
                DoSimulador = Acervo.EhDoSimulador,
            });
            saida.AppendLine("funcoes   : "
                             + string.Join(", ", prog.Funcoes.Select(f => f.Nome + "()")));

            var interp = new Interpretador(prog, maq);

            // O tempo acaba mesmo no meio de uma volta que espera alguem:
            // sem isto, o sketch que aguarda o botao ser solto rodava para
            // sempre no relogio virtual.
            maq.LimiteDoRelogioVirtual = ms;

            // ESP.restart() e o sono profundo recomecam do setup(). Aqui o
            // relogio segue contando, para o tempo do diagnostico valer para
            // a soma das rodadas.
            for (int rodada = 0; ; rodada++)
            {
                try
                {
                    interp.Preparar();
                    interp.Setup();
                    break;
                }
                catch (ReinicioDaPlaca motivo) when (rodada < 20)
                {
                    saida.AppendLine($"reinicio  : {motivo.Resumo}, em {maq.Ms} ms");
                    interp = new Interpretador(prog, maq) { Origem = motivo };
                }
            }

            int voltas = 0;
            while (maq.Ms < ms && interp.TemLoop && voltas < 4000)
            {
                // A METADE do tempo com as entradas mexidas.
                //
                // Sem isto quase todo sketch da lista parece morto, e nao
                // esta: o de botao espera alguem apertar, o de teclado
                // espera alguem digitar, o de controle remoto espera
                // alguem apontar. Um sketch que so responde a acao nao
                // pode ser julgado sem acao nenhuma.
                if (voltas == 1) Mexer(editor, circuito, maq, true);
                if (voltas == 2000) Mexer(editor, circuito, maq, false);

                long antes = maq.Ms;
                interp.Loop();
                editor.AplicarEntradas(bancada, rede);
                voltas++;

                // Quem conta o pico e a propria fita: ela acende e apaga
                // DENTRO de uma volta do laco, e de fora so se ve o fim.

                // Uma volta de loop custa tempo numa placa de verdade.
                // Sem isto, um sketch que se organiza com millis() e nao
                // chama delay() nunca sai do lugar aqui -- o relogio
                // virtual so anda no delay, e o millis() dele ficaria
                // preso em zero para sempre. E o caso do painel de carro
                // da lista, que tranca no antirrebote do botao.
                if (maq.Ms == antes) maq.Avancar(1);
            }

            saida.AppendLine($"voltas    : {voltas}, relogio em {maq.Ms} ms");
        }
        catch (ReinicioDaPlaca motivo)
        {
            // O codigo C entrou em panico (ou chamou esp_restart) no loop.
            saida.AppendLine($"reinicio  : {motivo.Resumo}, em {maq.Ms} ms");
        }
        catch (ExecucaoInterrompida)
        {
            // O tempo do diagnostico acabou no meio de uma volta: normal.
            saida.AppendLine($"voltas    : o tempo acabou no meio de uma volta, em {maq.Ms} ms");
        }
        catch (Exception e)
        {
            saida.AppendLine($"ERRO      : {e.Message}");
        }
        finally
        {
            // As tarefas do codigo C sao threads de verdade: sem isto elas
            // seguiam rodando depois do diagnostico.
            maq.EncerrarNativo();
        }

        // ---- o que o sketch fez ----
        var mexidos = maq.Eventos.Select(ev => ev.Pino).Distinct().OrderBy(x => x).ToList();
        saida.AppendLine($"pinos     : {maq.Eventos.Count} eventos em "
                         + (mexidos.Count == 0 ? "nenhum pino"
                            : string.Join(", ", mexidos.Select(p => "GPIO" + p))));

        if (maq.Oled is not null) saida.AppendLine("oled      : " + LinhasDoLcd(maq.Oled));
        if (maq.Lcd is not null) saida.AppendLine("lcd       : " + LinhasDoLcd(maq.Lcd));
        if (maq.Tela is not null)
            saida.AppendLine($"tela tft  : {maq.Tela.Largura}x{maq.Tela.Altura}, "
                             + $"{maq.Tela.Textos.Count} texto(s)");
        foreach (var (pino, fita) in maq.Fitas)
            saida.AppendLine($"fita      : GPIO{pino}, {fita.Quantidade} LEDs, "
                             + $"{fita.Cores.Count(c => c != 0)} acesos agora, "
                             + $"pico de {fita.PicoAceso}");

        saida.AppendLine($"serial    : {maq.Serial.Count} linhas");
        foreach (string linha in maq.Serial.Take(8)) saida.AppendLine("            " + linha);
        if (maq.Serial.Count > 8) saida.AppendLine($"            ... mais {maq.Serial.Count - 8}");

        return saida.ToString();
    }

    /// <summary>
    /// Aperta tudo o que da para apertar, ou solta tudo.
    ///
    /// E grosseiro de proposito: o diagnostico nao sabe o que cada
    /// sketch espera, entao ele mexe em TODA entrada e ve o que
    /// acontece. Botao apertado, chave virada, potenciometro girado,
    /// tecla do teclado, botao do controle remoto.
    /// </summary>
    static void Mexer(EditorCircuito editor, Circuito circuito, Maquina maq, bool acionar)
    {
        foreach (var peca in circuito.Pecas)
        {
            switch (peca.Tipo)
            {
                case "pushbutton" or "pushbutton-6mm":
                    if (acionar) editor.BotoesPressionados.Add(peca.Id);
                    else editor.BotoesPressionados.Remove(peca.Id);
                    break;

                case "slide-switch" or "tilt-switch":
                    editor.DefinirGiro(peca, acionar ? 1 : 0);
                    break;

                case "potentiometer" or "slide-potentiometer" or "ldr"
                    or "photoresistor-sensor" or "ntc-temperature-sensor":
                    editor.DefinirGiro(peca, acionar ? 0.75 : 0.25);
                    break;

                case "dip-switch-8":
                    editor.AlternarChaveDip(peca, 0, acionar);
                    break;
            }
        }

        // O teclado matricial nao tem peca com giro: a tecla chega pela
        // tabela de leituras, e o AplicarEntradas nao mexe nela.
        if (circuito.Pecas.Any(x => x.Tipo == "membrane-keypad"))
            maq.Leituras["keypad:tecla"] = acionar ? '5' : 0;

        // O controle infravermelho, ao contrario, TEM peca -- e escrever
        // direto em "ir:codigo" seria apagado pelo AplicarEntradas na
        // volta seguinte. Tem que apertar pelo caminho de verdade.
        foreach (var controle in circuito.Pecas.Where(x => x.Tipo == "ir-remote"))
            editor.ApertarNoControle(controle, acionar ? "1" : null);
    }

    static string LinhasDoLcd(Lcd lcd)
    {
        var cheias = new List<string>();
        for (int l = 0; l < lcd.Linhas; l++)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < lcd.Colunas; c++) sb.Append(lcd.Tela[l, c]);
            string texto = sb.ToString().TrimEnd();
            if (texto.Length > 0) cheias.Add($"[{texto}]");
        }
        return cheias.Count == 0 ? "(vazio)" : string.Join(" ", cheias);
    }
}
