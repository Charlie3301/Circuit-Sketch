using System.IO;

namespace Simulador.App;

/// <summary>
/// Os chips proprios que estao no circuito, rodando.
///
/// Cada instancia ganha uma Maquina so dela, com um PerfilPlaca
/// sintetizado a partir da lista de pinos do .chip.json: dai para
/// baixo, um chip e uma placa minuscula. Isso e de proposito -- assim o
/// chip entra na Netlist como qualquer outra peca, e a deteccao de
/// conflito digital e o modelo de queima valem para ele sem uma linha a
/// mais.
///
/// O relogio e o mesmo quadro de 40 ms do resto da interface. Um chip
/// combinacional ou um contador funcionam bem; um que precise gerar um
/// sinal de kHz, nao -- e isso e dito na tela em vez de fingir.
/// </summary>
public sealed class ChipsNoCircuito
{
    sealed class EmExecucao
    {
        public ChipCustom Chip;
        public Maquina Maquina;
        public Interpretador Interpretador;
        public string Erro;
        public string[] Pinos = [];
    }

    readonly Dictionary<string, EmExecucao> rodando = [];

    /// <summary>Os chips que o circuito conhece agora, por id de peca.</summary>
    public IReadOnlyDictionary<string, ChipCustom> Catalogados => catalogados;
    readonly Dictionary<string, ChipCustom> catalogados = [];

    /// <summary>O que deu errado em cada chip, para a barra de status.</summary>
    public IReadOnlyDictionary<string, string> Erros
        => rodando.Where(x => x.Value.Erro is not null)
                  .ToDictionary(x => x.Key, x => x.Value.Erro);

    // =====================================================================
    //  Catalogo
    // =====================================================================

    /// <summary>
    /// Le os chips do projeto e da pasta do computador e poe no
    /// catalogo de pecas.
    ///
    /// Os do projeto vem primeiro: um chip que viaja dentro do .sktch
    /// tem que ganhar do que esta instalado na maquina, senao abrir o
    /// arquivo de outra pessoa usaria o chip errado em silencio.
    /// </summary>
    public void Recarregar(Circuito circuito)
    {
        Catalogo.EsquecerRegistradas(ChipCustom.Prefixo);
        Pecas.EsquecerDesenhos(ChipCustom.Prefixo);
        catalogados.Clear();

        foreach (var chip in DaPasta().Concat(DoProjeto(circuito)))
        {
            catalogados[chip.Id] = chip;

            Pecas.DefinirDesenho(chip.Id, chip.Svg());
            Catalogo.Registrar(new PecaDef(
                chip.Id, chip.Nome, chip.LarguraFinal, chip.AlturaFinal,
                chip.PinosNaCaixa(), "Chips"));
        }
    }

    static IEnumerable<ChipCustom> DoProjeto(Circuito circuito)
    {
        if (circuito?.Arquivos is null) yield break;

        foreach (var (nome, json) in circuito.Arquivos)
        {
            if (!nome.EndsWith(".chip.json", StringComparison.OrdinalIgnoreCase)) continue;

            string codigo = circuito.Arquivos.GetValueOrDefault(
                nome[..^"json".Length] + "ino", "");

            var chip = ChipCustom.Ler(json, codigo, doProjeto: true);
            if (chip is not null) yield return chip;
        }
    }

    static IEnumerable<ChipCustom> DaPasta()
    {
        string pasta = ChipCustom.PastaDosChips;

        string[] arquivos;
        try
        {
            if (!Directory.Exists(pasta)) yield break;
            arquivos = Directory.GetFiles(pasta, "*.chip.json", SearchOption.AllDirectories);
        }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (string arquivo in arquivos)
        {
            ChipCustom chip = null;
            try
            {
                string codigo = arquivo[..^"json".Length] + "ino";
                chip = ChipCustom.Ler(
                    File.ReadAllText(arquivo),
                    File.Exists(codigo) ? File.ReadAllText(codigo) : "",
                    doProjeto: false);
            }
            catch (IOException) { }

            if (chip is not null) yield return chip;
        }
    }

    // =====================================================================
    //  Execucao
    // =====================================================================

    /// <summary>Prepara uma maquina para cada chip do circuito.</summary>
    public void Comecar(Circuito circuito)
    {
        rodando.Clear();

        foreach (var peca in circuito.Pecas)
        {
            if (!catalogados.TryGetValue(peca.Tipo, out var chip)) continue;

            var alvo = new EmExecucao
            {
                Chip = chip,
                Pinos = [.. chip.Pinos.Select(x => x.Nome)],
            };

            var porNome = alvo.Pinos
                .Select((nome, i) => (nome, i))
                .ToDictionary(x => x.nome, x => x.i, StringComparer.OrdinalIgnoreCase);

            var perfil = new PerfilPlaca(chip.Id, chip.Nome, alvo.Pinos.Length, 4095, -1)
            {
                Numero = nome => porNome.GetValueOrDefault(nome ?? "", -1),
            };

            alvo.Maquina = new Maquina { Perfil = perfil, TempoReal = true };
            alvo.Maquina.Reiniciar();

            try
            {
                var prog = new Parser(Lexer.Tokenizar(chip.Codigo ?? "")).AnalisarPrograma();
                alvo.Interpretador = new Interpretador(prog, alvo.Maquina);
                alvo.Interpretador.Preparar();

                if (!alvo.Interpretador.EhChip())
                    alvo.Erro = $"{chip.Nome}: falta chipInit() ou chipLoop() no .chip.ino.";
                else
                    alvo.Interpretador.IniciarChip();
            }
            catch (ErroDeSintaxe e) { alvo.Erro = $"{chip.Nome}: linha {e.Linha}: {e.Message}"; }
            catch (ErroDeExecucao e) { alvo.Erro = $"{chip.Nome}: {e.Message}"; }

            rodando[peca.Id] = alvo;
        }
    }

    public void Parar() => rodando.Clear();

    public bool Algum => rodando.Count > 0;

    /// <summary>
    /// Um quadro de todos os chips: le os pinos da rede, roda o
    /// chipLoop() e devolve o que os chips estao mandando para fora.
    /// </summary>
    public void Passo(Circuito circuito, Netlist rede, Bancada bancada)
    {
        foreach (var (idDaPeca, alvo) in rodando)
        {
            if (alvo.Interpretador is null || alvo.Erro is not null) continue;

            // Entrada: o que a rede esta pondo em cada pino do chip.
            for (int i = 0; i < alvo.Pinos.Length; i++)
            {
                if (alvo.Maquina.Modo[i] == 1) continue;

                int nivel = NivelNaRede(circuito, rede, bancada, idDaPeca, alvo.Pinos[i]);
                alvo.Maquina.NivelExterno[i] = nivel;
            }

            try { alvo.Interpretador.PassoDoChip(); }
            catch (ErroDeExecucao e) { alvo.Erro = $"{alvo.Chip.Nome}: {e.Message}"; }
            catch (ExecucaoInterrompida) { }
        }
    }

    /// <summary>O que um pino de chip esta mandando, ou -1 se e entrada.</summary>
    public int SaidaDoPino(string idDaPeca, string pino)
    {
        if (!rodando.TryGetValue(idDaPeca, out var alvo) || alvo.Erro is not null) return -1;

        int i = Array.FindIndex(alvo.Pinos,
            x => string.Equals(x, pino, StringComparison.OrdinalIgnoreCase));

        if (i < 0 || alvo.Maquina.Modo[i] != 1) return -1;
        return alvo.Maquina.Digital[i];
    }

    /// <summary>
    /// O nivel que a rede entrega num pino do chip.
    ///
    /// Vale o que a placa esta escrevendo no mesmo no; sem placa nenhuma
    /// ligada, o pino fica solto (-1) e o chip le zero, como um CMOS sem
    /// pull-up.
    /// </summary>
    static int NivelNaRede(Circuito circuito, Netlist rede, Bancada bancada,
                           string idDaPeca, string pino)
    {
        var (placa, gpio) = rede.PinoDe(circuito, idDaPeca, pino);
        if (placa is null || gpio < 0) return -1;

        var maq = bancada.De(placa);
        if (maq is null) return -1;

        return maq.Modo[gpio] == 1 ? maq.Digital[gpio] : -1;
    }
}
