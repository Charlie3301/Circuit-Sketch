namespace Simulador.App;

/// <summary>
/// Como esta esta linha da lista.
///
/// Alpha 18: "ao dar play no circuito, deve piscar em verde e voltar
/// para branco com fade quais fios estao conectados, os que nao
/// estiverem em amarelo e os com algum erro em vermelho".
/// </summary>
public enum EstadoDaLigacao
{
    /// <summary>Rotulo, cabecalho, placa: nao e uma ligacao.</summary>
    Neutro,

    /// <summary>Ligado. Pisca verde no play e descansa em branco.</summary>
    Ligado,

    /// <summary>Esta no circuito mas nao chega em placa nenhuma.</summary>
    SemLigacao,

    /// <summary>Queimou, ou a ligacao esta errada.</summary>
    Erro,
}

/// <summary>
/// Um item da lista de componentes.
/// </summary>
/// <param name="Peca">Id da peca, ou null quando e so um rotulo.</param>
/// <param name="Titulo">O que aparece na linha.</param>
/// <param name="Detalhe">Texto secundario, mais claro, na mesma linha.</param>
/// <param name="Nivel">Quanto a linha e recuada.</param>
/// <param name="Filhos">O que abre com a seta.</param>
public sealed record ItemDeComponente(
    string Peca, string Titulo, string Detalhe, int Nivel, List<ItemDeComponente> Filhos)
{
    public bool TemFilhos => Filhos is { Count: > 0 };

    /// <summary>
    /// A cor do fio desta ligacao, em "#RRGGBB", ou null.
    ///
    /// O ">" da lista sai nessa cor. E o que liga a linha escrita ao
    /// fio desenhado: num circuito com sete fios, achar "o verde" na
    /// lista e imediato, e achar "D2 > A" nao e.
    /// </summary>
    public string Cor { get; init; }

    /// <summary>
    /// A ligacao chega por FIO, ou so pelos trilhos da protoboard.
    ///
    /// Sem fio nenhum no meio nao ha cor para mostrar, e dizer que ha
    /// seria inventar.
    /// </summary>
    public bool PorFio => Cor is not null;

    /// <summary>Ligada, sem ligacao ou com erro. Decide a cor da linha.</summary>
    public EstadoDaLigacao Estado { get; init; }

    /// <summary>
    /// O fio desta ligacao, quando ela vem de um fio.
    ///
    /// Alpha 18: "capacidade de controlar onde os fios vao e conexoes de
    /// pinos na aba circuito". Sem o fio na mao, a lista so conseguia
    /// DESCREVER a ligacao; com ele, o menu da linha muda o pino, a cor
    /// e o tracado do fio de verdade.
    ///
    /// E a referencia, e nao um indice: apagar um fio mexe nos indices
    /// de todos os outros, e a lista nao e remontada entre um clique e
    /// o seguinte.
    /// </summary>
    public Fio Fio { get; init; }

    /// <summary>O pino da placa deste par, para o menu saber o que trocar.</summary>
    public string PinoDaPlaca { get; init; }

    /// <summary>O pino da peca deste par.</summary>
    public string PinoDaPeca { get; init; }

    /// <summary>O tipo da peca da linha (o da protoboard, num grupo delas): a miniatura do cartao.</summary>
    public string Tipo { get; init; }

    /// <summary>A linha e um par de pinos (placa &gt; peca), e nao uma peca.</summary>
    public bool EhPar => PinoDaPlaca is not null;
}

/// <summary>
/// A lista de componentes do circuito, em arvore.
///
/// Item 5 da lista do professor, e ele desenhou o formato que queria:
///
/// <code>
///   [Protoboard 170 x2]
///       ESP32   &gt; MPU
///                 GND &gt; GND
///                 3V3 &gt; VCC
///                 D2  &gt; SCL
///                 D4  &gt; SDA
///
///   *MPU fora da Protoboard 170
/// </code>
///
/// Ou seja: agrupado por protoboard, com a placa mandando, o que esta
/// ligado nela pendurado embaixo, os pinos par a par, e um aviso para o
/// que ficou de fora.
///
/// A hierarquia nao esta gravada em lugar nenhum -- ela e DEDUZIDA da
/// geometria (quem esta espetado em qual protoboard) e do netlist (quem
/// esta ligado em quem). Por isso mora aqui, separada da janela: e
/// logica de circuito, e da para testar sem abrir tela nenhuma.
/// </summary>
public static class ArvoreDeComponentes
{
    public static List<ItemDeComponente> Montar(Circuito circuito, Netlist rede,
                                               IReadOnlyCollection<string> queimadas = null)
    {
        // Queimou nesta execucao: a linha sai vermelha. Fora de
        // execucao a lista nao tem queimadura nenhuma para mostrar, e o
        // parametro fica em null.
        bool Queimou(string peca) => queimadas is not null && queimadas.Contains(peca);

        var raiz = new List<ItemDeComponente>();
        if (circuito is null) return raiz;

        var protoboards = circuito.Pecas.Where(p => Catalogo.EhProtoboard(p.Tipo)).ToList();
        var placas = circuito.Placas().ToList();

        // Quem ja apareceu em algum galho.
        var colocadas = new HashSet<string>();
        foreach (var protoboard in protoboards) colocadas.Add(protoboard.Id);

        // ---- 1) as PLACAS primeiro, com o que esta ligado em cada uma ----
        //
        // A ordem importa e foi de onde vinha o erro: antes a protoboard
        // era montada antes, e como ninguem tinha contado as ligacoes
        // ainda, todo LED espetado nela saia marcado como "sem ligacao
        // com a placa" -- estando ligado.
        var galhosDePlaca = new Dictionary<string, ItemDeComponente>();

        foreach (var placa in placas)
        {
            colocadas.Add(placa.Id);
            galhosDePlaca[placa.Id] = GalhoDaPlaca(circuito, rede, placa, colocadas, 2, Queimou);
        }

        // ---- 2) as protoboards, e o que esta espetado em cada uma ----
        foreach (var grupo in protoboards.GroupBy(p => p.Tipo))
        {
            var doTipo = grupo.ToList();
            string nome = Catalogo.Obter(grupo.Key)?.Nome ?? grupo.Key;

            var filhos = new List<ItemDeComponente>();
            int espetadas = 0;

            foreach (var protoboard in doTipo)
            {
                // A placa espetada NESTA protoboard entra aqui dentro,
                // com tudo o que esta ligado nela.
                foreach (var placa in placas.Where(b => EspetadaEm(b, protoboard)))
                {
                    if (!galhosDePlaca.Remove(placa.Id, out var galho)) continue;
                    filhos.Add(galho);
                    espetadas++;
                }

                foreach (var peca in circuito.Pecas)
                {
                    if (Catalogo.EhProtoboard(peca.Tipo) || PerfilPlaca.EhPlaca(peca.Tipo)) continue;
                    if (!EspetadaEm(peca, protoboard)) continue;

                    espetadas++;

                    // A protoboard lista TUDO o que esta espetado nela,
                    // uma linha por peca. Os pinos ficam embaixo da
                    // placa, onde a informacao tem dono -- aqui e um
                    // indice do que esta fisicamente em cima dela, que e
                    // o que se quer olhar quando o circuito cresce.
                    var dona = DonaDe(circuito, rede, peca, placas);

                    filhos.Add(new ItemDeComponente(
                        peca.Id, Titulo(peca),
                        dona is not null ? Idioma.T("ligada na ") + Titulo(dona)
                                         : Idioma.T("sem ligação com placa nenhuma"),
                        2, [])
                    {
                        Tipo = peca.Tipo,
                        Estado = Queimou(peca.Id) ? EstadoDaLigacao.Erro
                               : dona is not null ? EstadoDaLigacao.Ligado
                                                  : EstadoDaLigacao.SemLigacao,
                    });

                    colocadas.Add(peca.Id);
                }
            }

            string titulo = doTipo.Count > 1 ? $"[{nome} x{doTipo.Count}]" : $"[{nome}]";

            raiz.Add(new ItemDeComponente(
                doTipo.Count == 1 ? doTipo[0].Id : null, titulo,
                espetadas == 0 ? Idioma.T("nada espetado") : Idioma.T("{0} peça(s) espetadas", espetadas),
                1, filhos) { Tipo = grupo.Key });
        }

        // ---- 3) placas que nao estao em protoboard nenhuma ----
        foreach (var placa in placas)
        {
            if (!galhosDePlaca.TryGetValue(placa.Id, out var galho)) continue;
            raiz.Add(Recuar(galho, -1));
        }

        // ---- 4) o que sobrou, solto ----
        foreach (var peca in circuito.Pecas)
        {
            if (colocadas.Contains(peca.Id)) continue;

            raiz.Add(new ItemDeComponente(
                peca.Id, Titulo(peca),
                protoboards.Count > 0 ? Idioma.T("fora da protoboard e sem ligação") : Idioma.T("sem ligação"),
                1, [])
            {
                Tipo = peca.Tipo,
                Estado = Queimou(peca.Id) ? EstadoDaLigacao.Erro : EstadoDaLigacao.SemLigacao,
            });
        }

        return raiz;
    }

    /// <summary>
    /// O mesmo galho, um nivel mais para a esquerda.
    ///
    /// A placa e montada como se fosse ficar DENTRO de uma protoboard.
    /// Quando ela nao esta em nenhuma, o galho inteiro sobe um nivel --
    /// e mais barato do que montar duas vezes.
    /// </summary>
    static ItemDeComponente Recuar(ItemDeComponente item, int quanto)
        => item with
        {
            Nivel = Math.Max(1, item.Nivel + quanto),
            Filhos = [.. item.Filhos.Select(f => Recuar(f, quanto))],
        };

    /// <summary>A placa e tudo o que esta ligado nela, pino a pino.</summary>
    static ItemDeComponente GalhoDaPlaca(Circuito circuito, Netlist rede,
                                         PecaNoCircuito placa, HashSet<string> colocadas,
                                         int nivel, Func<string, bool> queimou)
    {
        var filhos = new List<ItemDeComponente>();

        foreach (var (vizinha, ligacoes) in Vizinhas(circuito, rede, placa))
        {
            colocadas.Add(vizinha.Id);

            // A peca queimada contamina as ligacoes dela: sao esses os
            // fios que se quer achar depois de um estouro.
            var estado = queimou(vizinha.Id) ? EstadoDaLigacao.Erro : EstadoDaLigacao.Ligado;

            var pinos = ligacoes
                .Select(l => new ItemDeComponente(
                    vizinha.Id, $"{l.DaPlaca} > {l.DaPeca}", null, nivel + 2, [])
                {
                    Cor = l.Fio?.Cor,
                    Estado = estado,
                    Fio = l.Fio,
                    PinoDaPlaca = l.DaPlaca,
                    PinoDaPeca = l.DaPeca,
                })
                .ToList();

            filhos.Add(new ItemDeComponente(
                vizinha.Id, "> " + Titulo(vizinha), null, nivel + 1, pinos)
            {
                Tipo = vizinha.Tipo,
                Estado = estado,
            });
        }

        return new ItemDeComponente(placa.Id, Titulo(placa), null, nivel, filhos)
        {
            Tipo = placa.Tipo,
            Estado = queimou(placa.Id) ? EstadoDaLigacao.Erro : EstadoDaLigacao.Neutro,
        };
    }

    /// <summary>
    /// As pecas ligadas a esta placa, com os pares de pino.
    ///
    /// Vem do netlist, e nao dos fios: um LED ligado por trilho da
    /// protoboard esta tao ligado quanto um ligado por fio, e a lista
    /// tem que mostrar os dois.
    /// </summary>
    static List<(PecaNoCircuito Peca,
                 List<(string DaPlaca, string DaPeca, Fio Fio)> Ligacoes)>
        Vizinhas(Circuito circuito, Netlist rede, PecaNoCircuito placa)
    {
        var saida = new List<(PecaNoCircuito, List<(string, string, Fio)>)>();
        if (rede is null) return saida;

        var daPlaca = Catalogo.Obter(placa.Tipo);
        if (daPlaca is null) return saida;

        foreach (var peca in circuito.Pecas)
        {
            if (peca.Id == placa.Id) continue;
            if (Catalogo.EhProtoboard(peca.Tipo)) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var ligacoes = new List<(string, string, Fio)>();

            foreach (var pinoDaPeca in def.Pinos)
            {
                // TODOS os pinos da placa que caem no mesmo no, e nao o
                // primeiro: o GND de uma ESP32 aparece em tres pinos, e
                // sair sempre o "GND.1" esconderia em qual deles o fio
                // esta de verdade.
                var candidatos = daPlaca.Pinos
                    .Where(x => rede.MesmoNo(Netlist.No(placa.Id, x.Nome),
                                             Netlist.No(peca.Id, pinoDaPeca.Nome)))
                    .ToList();

                if (candidatos.Count == 0) continue;

                var escolhido = MelhorPino(candidatos, placa, peca.Id, pinoDaPeca.Nome, circuito);

                ligacoes.Add((escolhido, pinoDaPeca.Nome,
                              FioDaLigacao(circuito, rede, peca.Id, pinoDaPeca.Nome)));
            }

            // Pinos da peca que sao o MESMO ponto eletrico aparecem uma
            // vez so.
            //
            // Sao dois casos, e os dois enchiam a lista de linha inutil:
            // o botao de quatro pads, cujos lados vem unidos de fabrica
            // ("2 > 1.l", "2 > 2.l", "2 > 1.r", "2 > 2.r" para a mesma
            // ligacao), e o resistor, que une as proprias pernas por
            // conduzir.
            var vistos = new HashSet<string>();
            ligacoes = [.. ligacoes.Where(l =>
                vistos.Add(rede.RaizDe(Netlist.No(peca.Id, l.Item2)) ?? l.Item2))];

            if (ligacoes.Count > 0) saida.Add((peca, ligacoes));
        }

        return saida;
    }

    /// <summary>A placa em que esta peca esta ligada, ou null.</summary>
    static PecaNoCircuito DonaDe(Circuito circuito, Netlist rede,
                                 PecaNoCircuito peca, List<PecaNoCircuito> placas)
    {
        var def = Catalogo.Obter(peca.Tipo);
        if (def is null || rede is null) return null;

        foreach (var placa in placas)
        {
            var daPlaca = Catalogo.Obter(placa.Tipo);
            if (daPlaca is null) continue;

            foreach (var pinoDaPeca in def.Pinos)
                foreach (var pinoDaPlaca in daPlaca.Pinos)
                    if (rede.MesmoNo(Netlist.No(placa.Id, pinoDaPlaca.Nome),
                                     Netlist.No(peca.Id, pinoDaPeca.Nome)))
                        return placa;
        }

        return null;
    }

    /// <summary>
    /// Entre varios pinos da placa no mesmo no, qual mostrar.
    ///
    /// Primeiro o que tem FIO ligando direto na peca -- e o que a pessoa
    /// desenhou. Depois o que e GPIO de verdade, que diz mais do que um
    /// terra. So entao o primeiro da lista.
    /// </summary>
    static string MelhorPino(List<PinoDef> candidatos, PecaNoCircuito placa,
                             string peca, string pino, Circuito circuito)
    {
        foreach (var fio in circuito.Fios)
        {
            bool daPeca = (fio.DePeca == peca && fio.DePino == pino)
                          || (fio.ParaPeca == peca && fio.ParaPino == pino);
            if (!daPeca) continue;

            string naPlaca = fio.DePeca == placa.Id ? fio.DePino
                           : fio.ParaPeca == placa.Id ? fio.ParaPino : null;

            if (naPlaca is not null && candidatos.Any(x => x.Nome == naPlaca)) return naPlaca;
        }

        var perfil = PerfilPlaca.De(placa.Tipo);
        if (perfil is not null)
            foreach (var candidato in candidatos)
                if (perfil.Numero(candidato.Nome) >= 0) return candidato.Nome;

        return candidatos[0].Nome;
    }

    /// <summary>
    /// A cor do fio que chega neste pino.
    ///
    /// Procura primeiro o fio que encosta no proprio pino; se ele chega
    /// pelo trilho da protoboard, procura um fio que caia no MESMO no.
    /// Sem fio nenhum na conta -- peca espetada direto na placa --,
    /// devolve null, e a lista nao pinta cor nenhuma.
    /// </summary>
    static Fio FioDaLigacao(Circuito circuito, Netlist rede, string peca, string pino)
    {
        // Primeiro o fio que encosta no PROPRIO pino: e o que a pessoa
        // desenhou, e e o que o menu da linha tem que mexer.
        foreach (var fio in circuito.Fios)
        {
            if ((fio.DePeca == peca && fio.DePino == pino)
                || (fio.ParaPeca == peca && fio.ParaPino == pino))
                return fio;
        }

        if (rede is null) return null;

        // Chegou pelo trilho da protoboard: o fio existe, mas encosta em
        // outro furo do mesmo no.
        string no = Netlist.No(peca, pino);

        foreach (var fio in circuito.Fios)
        {
            if (rede.MesmoNo(no, Netlist.No(fio.DePeca, fio.DePino))
                || rede.MesmoNo(no, Netlist.No(fio.ParaPeca, fio.ParaPino)))
                return fio;
        }

        return null;
    }

    /// <summary>
    /// A peca esta espetada nesta protoboard?
    ///
    /// Mesma pergunta que o clique faz: algum pino dela cai dentro da
    /// area da placa.
    /// </summary>
    static bool EspetadaEm(PecaNoCircuito peca, PecaNoCircuito protoboard)
    {
        var def = Catalogo.Obter(peca.Tipo);
        var daPlaca = Catalogo.Obter(protoboard.Tipo);
        if (def is null || daPlaca is null) return false;

        var area = new System.Windows.Rect(
            protoboard.X, protoboard.Y, daPlaca.Largura, daPlaca.Altura);

        foreach (var pino in def.Pinos)
            if (area.Contains(Geometria.PosicaoLocal(peca, def, pino.X, pino.Y)))
                return true;

        return false;
    }

    /// <summary>O nome que a pessoa deu, ou o do catalogo.</summary>
    static string Titulo(PecaNoCircuito peca)
        => string.IsNullOrWhiteSpace(peca.Nome)
            ? Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Tipo
            : peca.Nome.Trim();

    /// <summary>A arvore inteira em texto, uma linha por item.</summary>
    public static string EmTexto(List<ItemDeComponente> itens)
    {
        var saida = new System.Text.StringBuilder();
        Escrever(itens, saida);
        return saida.ToString();
    }

    static void Escrever(List<ItemDeComponente> itens, System.Text.StringBuilder saida)
    {
        foreach (var item in itens)
        {
            saida.Append(new string(' ', Math.Max(0, item.Nivel - 1) * 4))
                 .Append(item.Titulo);

            if (item.Detalhe is not null) saida.Append("  (").Append(item.Detalhe).Append(')');
            saida.AppendLine();

            if (item.TemFilhos) Escrever(item.Filhos, saida);
        }
    }
}
