using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulador.App;

public class PecaNoCircuito
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Tipo { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Variante visual, ex.: "vermelho" para o LED.</summary>
    public string Cor { get; set; } = "vermelho";

    /// <summary>
    /// Nome que aparece embaixo da peca no circuito.
    ///
    /// Num circuito com seis LEDs iguais, "vermelho do semaforo" e
    /// "aviso de falha" e a diferenca entre entender o desenho e contar
    /// fios com o dedo.
    /// </summary>
    public string Nome { get; set; }
    /// <summary>Giro em graus (0, 90, 180, 270).</summary>
    public double Rotacao { get; set; }
    /// <summary>Resistencia em ohms (so para resistor).</summary>
    public double Ohms { get; set; } = 220;
    /// <summary>Espelhada na horizontal.</summary>
    public bool Espelhada { get; set; }

    /// <summary>A PCB virada: o lado de baixo para cima (as pecas montadas nela foram junto).</summary>
    public bool Virada { get; set; }
    /// <summary>Posicao do potenciometro / sensor, de 0 a 1.</summary>
    public double Giro { get; set; }
    /// <summary>Segundo eixo (joystick), de 0 a 1.</summary>
    public double Giro2 { get; set; } = 0.5;
    /// <summary>Chaves individuais (DIP switch), um bit por chave.</summary>
    public int Bits { get; set; }

    /// <summary>
    /// Terceiro e quarto eixos, de 0 a 1: o controle de radio tem dois
    /// manches (o direito em Giro3/Giro2, o esquerdo em Giro/Giro4).
    /// </summary>
    public double Giro3 { get; set; } = 0.5;
    public double Giro4 { get; set; } = 0.5;

    /// <summary>
    /// Tensao da bateria, em volts.
    ///
    /// E a que manda no circuito: uma bateria de 9 V queima um LED que
    /// sobrevive num trilho de 3,3. O padrao e a pilha de 9 V, que e a
    /// que aparece na caixa de componentes da aula.
    /// </summary>
    public double Volts { get; set; } = 9;

    /// <summary>Quanto a bateria aguenta entregar, em miliamperes.</summary>
    public double MiliAmperes { get; set; } = 500;

    /// <summary>
    /// Carga total, em mAh.
    ///
    /// Nao descarrega a bateria durante a simulacao -- o que ela faz e
    /// responder "quanto tempo isto dura", que e a conta que o aluno
    /// precisa fazer e quase nunca faz.
    /// </summary>
    public double MiliAmperesHora { get; set; } = 500;

    /// <summary>
    /// Motor: a ficha escolhida na lista ("rs-540", "2205-2300"...), ou
    /// "personalizado" com a <see cref="Ficha"/> digitada. Nulo: o padrao do tipo.
    /// </summary>
    public string Modelo { get; set; }

    /// <summary>A ficha digitada, quando o <see cref="Modelo"/> e "personalizado".</summary>
    public FichaPersonalizada Ficha { get; set; }

    /// <summary>
    /// Placa: o cabo USB. Nulo e o automatico -- ligado, a menos que haja uma
    /// bateria no circuito (ai a placa e alimentada por ela, ou fica apagada).
    /// </summary>
    public bool? Usb { get; set; }

    /// <summary>ESC: a corrente dele em amperes (0: o padrao da peca, 30 A ou 45 A no 4-em-1).</summary>
    public double Amperes { get; set; }

    /// <summary>ESC: a cor do termo-retratil ou da placa (nulo: a original).</summary>
    public string Acabamento { get; set; }

    /// <summary>
    /// Servo: o braco parafusado no eixo -- "nenhum", "simples", "duplo",
    /// "cruz" e, no MG996R, "estrela" e "disco" (nulo: o que vem montado).
    /// </summary>
    public string Braco { get; set; }

    /// <summary>
    /// Os "attrs" do Wokwi que o simulador nao usa (o volume do buzzer, a
    /// tecla e o rotulo do botao...): guardados como vieram, para o
    /// diagram.json sair de volta igual.
    /// </summary>
    public Dictionary<string, string> AtributosDoWokwi { get; set; }

    /// <summary>
    /// Teclas e botoes de controle ligados a esta peca, por acao.
    ///
    /// A chave e a acao ("apertar", "cima", "mais"...) e o valor e o
    /// vinculo em texto curto ("t:J", "g:LSCima"). Fica na PECA, e nao
    /// numa configuracao do programa, porque e do circuito: o jogo de
    /// dois botoes que alguem montou continua jogavel quando o arquivo
    /// muda de computador.
    /// </summary>
    public Dictionary<string, string> Teclas { get; set; }

    public Dictionary<string, string> Arquivos { get; set; }

    /// <summary>
    /// Arquivos do computador que entram no cartao microSD do jeito que
    /// estao, em bytes (o WAD do DOOM, uma imagem, um som): nome no cartao
    /// -> caminho no computador. O .sktch guarda so o caminho: sao arquivos
    /// grandes, e muitos nao podem ser redistribuidos junto com o projeto.
    /// </summary>
    public Dictionary<string, string> ArquivosDoComputador { get; set; }

    /// <summary>
    /// Os arquivos em bytes que o sketch gravou no cartao (nome -> Base64):
    /// um savegame, uma foto, um log binario. Ficam no .sktch, como os de
    /// texto -- no cartao de verdade eles tambem ficam depois de desligar.
    /// </summary>
    public Dictionary<string, string> ArquivosGravados { get; set; }

    /// <summary>
    /// Uma pasta do computador servindo de cartao (ou de pendrive): tudo o
    /// que esta nela, com as subpastas, aparece la e e lido do disco so
    /// quando o sketch abre -- uma biblioteca de musica de varios GB. O
    /// .sktch guarda so o caminho, e a pasta nunca muda: o que o sketch
    /// grava fica no cartao simulado.
    /// </summary>
    public string PastaDoComputador { get; set; }

    public string Ssid { get; set; }

    public string Senha { get; set; }

    /// <summary>
    /// O sketch desta placa, uma linha por item. So placa usa.
    ///
    /// Fica na peca, e nao solto no arquivo, porque e disso que se trata:
    /// o programa e da placa. Com duas placas no mesmo circuito, cada uma
    /// guarda o seu -- antes as duas dividiam o mesmo campo de texto e
    /// escolher a outra apagava o codigo da primeira.
    /// </summary>
    public List<string> Codigo { get; set; }

    /// <summary>
    /// Qual placa manda nesta peca, quando ela esta ligada em mais de uma.
    ///
    /// Nulo e o normal: a peca obedece a unica placa em que esta ligada.
    /// So faz diferenca na peca que alcanca duas -- ai, sem escolha, ela
    /// obedece a primeira que aparecer, e isso e arbitrario. A escolha e
    /// da pessoa e fica gravada no .sktch.
    ///
    /// Escolha que aponta para uma placa que a peca nao alcanca mais (o
    /// fio foi tirado, a placa foi apagada) e ignorada, e nao apagada:
    /// religar o fio devolve a escolha sem precisar fazer de novo.
    /// </summary>
    public string PlacaQueManda { get; set; }

    /// <summary>
    /// A escala do analisador logico: quanto tempo cabe na tela dele, em
    /// microssegundos. Zero e a de fabrica (10 ms).
    /// </summary>
    public double JanelaUs { get; set; }

    /// <summary>
    /// Mesma peca com Id novo. Existe para copiar/colar nao esquecer
    /// campo: antes cada lugar listava os campos a mao e Giro, Giro2 e
    /// Bits ficaram de fora -- colar um potenciometro perdia a posicao e
    /// colar uma chave DIP perdia as oito chaves.
    /// </summary>
    public PecaNoCircuito Copia() => new()
    {
        Tipo = Tipo, X = X, Y = Y, Cor = Cor, Nome = Nome, Rotacao = Rotacao,
        Ohms = Ohms, Espelhada = Espelhada, Virada = Virada,
        Giro = Giro, Giro2 = Giro2, Giro3 = Giro3, Giro4 = Giro4, Bits = Bits,
        Volts = Volts, MiliAmperes = MiliAmperes, MiliAmperesHora = MiliAmperesHora,
        Teclas = Teclas is null ? null : new Dictionary<string, string>(Teclas),
        Arquivos = Arquivos is null ? null : new Dictionary<string, string>(Arquivos),
        ArquivosDoComputador = ArquivosDoComputador is null ? null : new Dictionary<string, string>(ArquivosDoComputador, StringComparer.OrdinalIgnoreCase),
        ArquivosGravados = ArquivosGravados is null ? null : new Dictionary<string, string>(ArquivosGravados, StringComparer.OrdinalIgnoreCase),
        PastaDoComputador = PastaDoComputador,
        Ssid = Ssid, Senha = Senha,
        Codigo = Codigo is null ? null : [.. Codigo],
        PlacaQueManda = PlacaQueManda,
        JanelaUs = JanelaUs,
        Modelo = Modelo, Ficha = Ficha?.Copia(), Amperes = Amperes, Acabamento = Acabamento, Braco = Braco, Usb = Usb,
        AtributosDoWokwi = AtributosDoWokwi is null ? null : new Dictionary<string, string>(AtributosDoWokwi),
    };

    /// <summary>O sketch como texto; vazio quando ainda nao tem.</summary>
    [JsonIgnore]
    public string Sketch
    {
        get => Codigo is null ? null : string.Join(Environment.NewLine, Codigo);
        set => Codigo = value is null
            ? null : [.. value.Replace("\r\n", "\n").Split('\n')];
    }
}

public class Fio
{
    public string DePeca { get; set; }
    public string DePino { get; set; }
    public string ParaPeca { get; set; }
    public string ParaPino { get; set; }
    public string Cor { get; set; } = "#00A651";

    /// <summary>
    /// Tracado do fio: "solto" (curva que cai), "reto" (linha direta de
    /// ponta a ponta) ou "degrau" (em L, como o Wokwi). Guardado por fio
    /// para dar para misturar os tres no mesmo circuito.
    /// </summary>
    public string Estilo { get; set; } = EstiloSolto;

    /// <summary>
    /// Cantos que o usuario colocou no meio do fio, em coordenadas do
    /// circuito. Vazio = o fio vai direto de um pino ao outro no formato
    /// do estilo. Com dobras, o traçado passa a ser a linha quebrada que
    /// liga pino, dobras e pino -- e assim porque, a partir do momento em
    /// que a pessoa move um canto, quem manda na forma e ela, nao o
    /// estilo.
    /// </summary>
    public List<PontoFio> Dobras { get; set; } = [];

    /// <summary>
    /// O caminho como veio de um diagram.json ("v-12", "*", "h0"): enquanto
    /// o fio continuar com as mesmas dobras, ele sai de volta escrito igual.
    /// </summary>
    public List<string> CaminhoDoWokwi { get; set; }

    /// <summary>
    /// O lado da PCB por onde o fio passa: nulo (o de cima, o de sempre) ou
    /// "baixo" -- a trilha de solda feita com a placa virada.
    /// </summary>
    public string Lado { get; set; }

    public const string LadoDeBaixo = "baixo";

    public const string EstiloSolto = "solto";
    public const string EstiloReto = "reto";
    public const string EstiloDegrau = "degrau";
}

/// <summary>Um canto do fio. Classe propria para o JSON ficar legivel.</summary>
public class PontoFio
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class Circuito
{
    public List<PecaNoCircuito> Pecas { get; set; } = [];
    public List<Fio> Fios { get; set; } = [];

    /// <summary>O nome do projeto (o "name" do diagram.json). Nulo: o nome do arquivo.</summary>
    public string Nome { get; set; }

    /// <summary>Quem fez (o "author" do diagram.json). Nulo: o autor das configuracoes.</summary>
    public string Autor { get; set; }

    /// <summary>
    /// Os campos de topo do diagram.json que nao sao o circuito ("serialMonitor",
    /// "dependencies"...), em JSON como vieram: o arquivo volta com eles.
    /// </summary>
    public Dictionary<string, string> ExtrasDoDiagrama { get; set; }

    /// <summary>
    /// Um id legivel para uma peca nova, no jeito do Wokwi: "led1", "r2",
    /// "esp1" -- o diagram.json se le pelos ids, e "a3f9c2d1" nao diz nada.
    /// </summary>
    public string NovoId(string tipo)
    {
        string raiz = FormatoWokwi.RaizDoId(tipo ?? "p");
        for (int n = 1; ; n++)
        {
            string id = raiz + n;
            if (!Pecas.Any(p => p.Id == id)) return id;
        }
    }

    public Dictionary<string, string> Arquivos { get; set; }

    static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ParaJson() => JsonSerializer.Serialize(this, Formato);

    public static Circuito DeJson(string json)
        => TentarDeJson(json, out var c, out _) ? c : new Circuito();

    /// <summary>
    /// Le o JSON dizendo POR QUE falhou.
    ///
    /// O <see cref="DeJson"/> engole o erro e devolve circuito vazio, o
    /// que serve para abrir arquivo velho sem travar. Nao serve para a
    /// aba do diagram.json, onde a pessoa esta editando a mao: ali um
    /// circuito vazio no lugar do erro apagaria o trabalho dela sem
    /// dizer nada.
    /// </summary>
    public static bool TentarDeJson(string json, out Circuito circuito, out string erro)
    {
        circuito = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(json)) { erro = "o arquivo esta vazio"; return false; }

        try
        {
            circuito = JsonSerializer.Deserialize<Circuito>(json);
            if (circuito is null) { erro = "o JSON nao descreve um circuito"; return false; }
        }
        catch (JsonException e)
        {
            // A mensagem do .NET traz linha e coluna, que e exatamente o
            // que a pessoa precisa para achar a virgula que falta.
            erro = e.LineNumber is not null
                ? $"linha {e.LineNumber + 1}: {PrimeiraFrase(e.Message)}"
                : PrimeiraFrase(e.Message);
            return false;
        }

        circuito.Pecas ??= [];
        circuito.Fios ??= [];
        circuito.AtualizarPinosAntigos();

        // Peca sem tipo conhecido nao pode entrar: ela nao teria desenho
        // nem pinos, e o resto do programa quebraria longe daqui.
        var semTipo = circuito.Pecas.FirstOrDefault(p => Catalogo.Obter(p.Tipo) is null);
        if (semTipo is not null)
        {
            erro = $"a peca '{semTipo.Tipo}' nao existe no catalogo";
            circuito = null;
            return false;
        }

        var repetido = circuito.Pecas.GroupBy(p => p.Id)
            .FirstOrDefault(g => g.Key is not null && g.Count() > 1);
        if (repetido is not null)
        {
            erro = $"o id '{repetido.Key}' aparece em mais de uma peca";
            circuito = null;
            return false;
        }

        return true;
    }

    /// <summary>O essencial da mensagem do .NET, sem o paragrafo de ajuda.</summary>
    static string PrimeiraFrase(string mensagem)
    {
        int ponto = mensagem.IndexOf(". ", StringComparison.Ordinal);
        return ponto > 0 ? mensagem[..(ponto + 1)] : mensagem;
    }

    public PecaNoCircuito Achar(string id) => Pecas.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Pinos que mudaram de nome quando a placa ganhou a barra de verdade
    /// (Alpha 25). A C6 e a H2 tinham o GPIO do TX e do RX tambem com o
    /// numero, num furo a mais que a placa real nao tem: o fio ligado nele
    /// passa para o TX/RX, que e o mesmo GPIO.
    /// </summary>
    static readonly Dictionary<string, Dictionary<string, string>> PinosRenomeados = new()
    {
        ["esp32-c6-devkit"] = new() { ["16"] = "TX", ["17"] = "RX" },
        ["esp32-h2-devkit"] = new() { ["24"] = "TX", ["23"] = "RX" },
    };

    /// <summary>Passa os fios de um projeto antigo para os nomes de pino de agora.</summary>
    public void AtualizarPinosAntigos()
    {
        if (Pecas is null || Fios is null) return;
        string Novo(string peca, string pino)
            => Achar(peca) is { } p && PinosRenomeados.TryGetValue(p.Tipo, out var mapa)
               && pino is not null && mapa.TryGetValue(pino, out var novo) ? novo : pino;
        foreach (var f in Fios)
        {
            f.DePino = Novo(f.DePeca, f.DePino);
            f.ParaPino = Novo(f.ParaPeca, f.ParaPino);
        }
    }

    /// <summary>As placas de desenvolvimento, na ordem em que entraram.</summary>
    public IEnumerable<PecaNoCircuito> Placas()
        => Pecas.Where(p => PerfilPlaca.EhPlaca(p.Tipo));

    /// <summary>
    /// Fios que apontam para peca ou pino que nao existe.
    ///
    /// Eles somem do desenho sem avisar, porque PosicaoPino devolve null
    /// e o desenho pula. Foi assim que o LED verde do circuito padrao
    /// ficou sem ligacao por semanas: o fio apontava para "D16", que a
    /// ESP32 DevKit V1 nao tem. Vale a pena contar e dizer.
    /// </summary>
    public IEnumerable<string> FiosQuebrados()
    {
        foreach (var f in Fios)
        {
            foreach (var (idPeca, nomePino) in new[]
                     { (f.DePeca, f.DePino), (f.ParaPeca, f.ParaPino) })
            {
                var peca = Achar(idPeca);
                var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
                if (def is null)
                {
                    yield return $"{idPeca}:{nomePino} (peça não existe)";
                }
                else if (def.Pinos.All(p => p.Nome != nomePino))
                {
                    yield return $"{peca.Tipo}:{nomePino} (pino não existe)";
                }
            }
        }
    }
}

// ============================================================
//  Geometria
// ============================================================
/// <summary>
/// Posicao absoluta de um pino, com giro e espelho aplicados. Fica aqui
/// porque o netlist tambem precisa: e assim que ele descobre que a perna
/// de um LED esta enfiada num furo da protoboard.
/// </summary>
public static class Geometria
{
    public static System.Windows.Point PosicaoPino(
        PecaNoCircuito peca, PecaDef def, PinoDef pino)
        => PosicaoLocal(peca, def, pino.X, pino.Y);

    /// <summary>
    /// Deslocamento que encaixa a peca nos furos da protoboard, ou null
    /// se nenhuma perna estiver perto o bastante. Fica aqui, fora da
    /// interface, para poder ser testado sem abrir janela.
    /// </summary>
    public static System.Windows.Vector? AjusteParaFuro(
        PecaNoCircuito peca, PecaDef def, Circuito circuito, double alcance,
        bool evitarOcupados = true)
    {
        if (def is null || Catalogo.EhProtoboard(peca.Tipo) || def.Pinos.Count == 0) return null;

        var grade = GradeDeFuros(circuito);
        if (grade is null) return null;

        var ocupados = evitarOcupados ? FurosOcupados(circuito, grade, peca.Id) : [];

        // Todos os encaixes ao alcance, do mais perto para o mais longe.
        var candidatos = new List<(double dist, System.Windows.Vector ajuste)>();
        foreach (var pino in def.Pinos)
        {
            var q = PosicaoPino(peca, def, pino);
            foreach (var (_, _, pf) in FurosPerto(grade, q, alcance))
            {
                double dx = pf.X - q.X, dy = pf.Y - q.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < alcance) candidatos.Add((d, new System.Windows.Vector(dx, dy)));
            }
        }
        if (candidatos.Count == 0) return null;
        candidatos.Sort((a, b) => a.dist.CompareTo(b.dist));

        // O primeiro que nao pisa em furo de ninguem. Dois objetos nao
        // cabem no mesmo furo -- na bancada tambem nao -- e o encaixe
        // antigo aceitava qualquer um, entao o resistor ia parar por cima
        // do LED sem nada avisar.
        foreach (var (_, ajuste) in candidatos)
        {
            if (!PisaEmOcupado(peca, def, grade, ocupados, ajuste)) return ajuste;
            if (ocupados.Count == 0) return ajuste;      // nao ha em quem pisar
        }
        return null;
    }

    /// <summary>Furos de todas as protoboards, indexados por celula da grade.</summary>
    static Dictionary<(int, int), List<(string placa, string furo, System.Windows.Point pos)>>
        GradeDeFuros(Circuito circuito)
    {
        var placas = circuito.Pecas
            .Where(x => Catalogo.EhProtoboard(x.Tipo))
            .Select(x => (peca: x, def: Catalogo.Obter(x.Tipo)))
            .Where(x => x.def is not null)
            .ToList();
        if (placas.Count == 0) return null;

        var grade = new Dictionary<(int, int),
                                   List<(string, string, System.Windows.Point)>>();
        double celula = Catalogo.Passo;

        foreach (var (placa, defPlaca) in placas)
            foreach (var furo in defPlaca.Pinos)
            {
                var q = PosicaoPino(placa, defPlaca, furo);
                var chave = ((int)Math.Floor(q.X / celula), (int)Math.Floor(q.Y / celula));
                if (!grade.TryGetValue(chave, out var lista))
                    grade[chave] = lista = [];
                lista.Add((placa.Id, furo.Nome, q));
            }
        return grade;
    }

    static IEnumerable<(string placa, string furo, System.Windows.Point pos)> FurosPerto(
        Dictionary<(int, int), List<(string, string, System.Windows.Point)>> grade,
        System.Windows.Point p, double raio)
    {
        double celula = Catalogo.Passo;
        int alcance = (int)Math.Ceiling(raio / celula);
        int gx = (int)Math.Floor(p.X / celula), gy = (int)Math.Floor(p.Y / celula);

        for (int dx = -alcance; dx <= alcance; dx++)
            for (int dy = -alcance; dy <= alcance; dy++)
                if (grade.TryGetValue((gx + dx, gy + dy), out var lista))
                    foreach (var item in lista)
                        yield return item;
    }

    /// <summary>
    /// Furos que ja tem perna de outra peca ou ponta de fio dentro. Um
    /// furo de verdade segura um terminal so.
    /// </summary>
    static HashSet<string> FurosOcupados(
        Circuito circuito,
        Dictionary<(int, int), List<(string, string, System.Windows.Point)>> grade,
        string excluir)
    {
        var ocupados = new HashSet<string>();

        foreach (var outra in circuito.Pecas)
        {
            if (outra.Id == excluir || Catalogo.EhProtoboard(outra.Tipo)) continue;
            var defOutra = Catalogo.Obter(outra.Tipo);
            if (defOutra is null) continue;

            foreach (var pino in defOutra.Pinos)
            {
                var q = PosicaoPino(outra, defOutra, pino);
                foreach (var (placa, furo, pf) in FurosPerto(grade, q, Netlist.RaioDoFuro))
                {
                    double dx = pf.X - q.X, dy = pf.Y - q.Y;
                    if (dx * dx + dy * dy <= Netlist.RaioDoFuro * Netlist.RaioDoFuro)
                        ocupados.Add(Netlist.No(placa, furo));
                }
            }
        }

        // Ponta de fio espetada num furo tambem o ocupa.
        foreach (var fio in circuito.Fios)
        {
            foreach (var (idPeca, nome) in new[]
                     { (fio.DePeca, fio.DePino), (fio.ParaPeca, fio.ParaPino) })
            {
                var alvo = circuito.Achar(idPeca);
                if (alvo is null || !Catalogo.EhProtoboard(alvo.Tipo)) continue;
                ocupados.Add(Netlist.No(idPeca, nome));
            }
        }
        return ocupados;
    }

    /// <summary>Algum pino da peca cairia num furo ja tomado?</summary>
    static bool PisaEmOcupado(
        PecaNoCircuito peca, PecaDef def,
        Dictionary<(int, int), List<(string, string, System.Windows.Point)>> grade,
        HashSet<string> ocupados, System.Windows.Vector ajuste)
    {
        if (ocupados.Count == 0) return false;

        foreach (var pino in def.Pinos)
        {
            var q = PosicaoPino(peca, def, pino);
            q.Offset(ajuste.X, ajuste.Y);

            foreach (var (placa, furo, pf) in FurosPerto(grade, q, Netlist.RaioDoFuro))
            {
                double dx = pf.X - q.X, dy = pf.Y - q.Y;
                if (dx * dx + dy * dy > Netlist.RaioDoFuro * Netlist.RaioDoFuro) continue;
                if (ocupados.Contains(Netlist.No(placa, furo))) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Pecas cuja caixa encosta no retangulo. Fica aqui, fora da
    /// interface, para poder ser testada sem abrir janela.
    /// </summary>
    public static List<PecaNoCircuito> NoRetangulo(Circuito circuito, System.Windows.Rect area)
    {
        var achadas = new List<PecaNoCircuito>();
        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            var (largura, altura) = Tamanho(peca, def);
            var caixa = new System.Windows.Rect(peca.X, peca.Y, largura, altura);
            if (area.IntersectsWith(caixa)) achadas.Add(peca);
        }
        return achadas;
    }

    /// <summary>
    /// Quanto a peca e maior ou menor que o desenho do catalogo: o motor do
    /// tamanho da ficha escolhida (um RS-775 e o dobro do 130). 1 para as outras.
    /// </summary>
    public static double Escala(PecaNoCircuito peca) => FichasDeMotor.EscalaDe(peca);

    /// <summary>A largura e a altura da peca na tela (o catalogo vezes a escala dela).</summary>
    public static (double Largura, double Altura) Tamanho(PecaNoCircuito peca, PecaDef def)
    {
        double s = Escala(peca);
        return (def.Largura * s, def.Altura * s);
    }

    public static System.Windows.Point PosicaoLocal(
        PecaNoCircuito peca, PecaDef def, double lx, double ly)
    {
        // Os pontos da peca sao do desenho do catalogo; a escala os afasta
        // (ou junta) do canto, e o centro do giro vai junto.
        double s = Escala(peca);
        double cx = def.Largura * s / 2, cy = def.Altura * s / 2;
        double dx = lx * s - cx, dy = ly * s - cy;

        if (peca.Espelhada) dx = -dx;

        if (peca.Rotacao != 0)
        {
            double r = peca.Rotacao * Math.PI / 180;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            (dx, dy) = (dx * cos - dy * sin, dx * sin + dy * cos);
        }
        return new System.Windows.Point(peca.X + cx + dx, peca.Y + cy + dy);
    }

    /// <summary>
    /// O caminho de volta: um ponto da TELA em pixels da peca.
    ///
    /// E o inverso exato de <see cref="PosicaoLocal"/>, e existe por
    /// causa do clique. Os pinos sempre giraram junto com a peca porque
    /// passam por PosicaoLocal; o CORPO nao, porque o teste era um
    /// retangulo alinhado aos eixos montado direto de X, Y, Largura e
    /// Altura -- sem olhar a rotacao. O resultado era o que o professor
    /// descreveu: gira a peca e a area de clique fica para tras.
    ///
    /// Desfazendo a transformacao aqui, o teste do corpo volta a ser um
    /// retangulo simples, so que no espaco da peca -- onde ela nunca
    /// girou.
    /// </summary>
    public static System.Windows.Point PontoLocal(
        PecaNoCircuito peca, PecaDef def, System.Windows.Point tela)
    {
        double s = Escala(peca);
        double cx = def.Largura * s / 2, cy = def.Altura * s / 2;
        double dx = tela.X - (peca.X + cx), dy = tela.Y - (peca.Y + cy);

        if (peca.Rotacao != 0)
        {
            // Giro ao contrario: o seno troca de sinal.
            double r = peca.Rotacao * Math.PI / 180;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            (dx, dy) = (dx * cos + dy * sin, -dx * sin + dy * cos);
        }

        // O espelho vem DEPOIS na ida, entao vem antes aqui.
        if (peca.Espelhada) dx = -dx;

        // De volta as medidas do desenho do catalogo.
        return new System.Windows.Point((cx + dx) / s, (cy + dy) / s);
    }
}

// ============================================================
//  Netlist
// ============================================================
/// <summary>
/// Descobre o que esta ligado a que. Cada par peca:pino e um no; fios,
/// trilhos da protoboard e pernas de resistor unem nos. Depois basta
/// perguntar em qual GPIO do ESP32 um pino foi parar.
///
/// Sem isto os fios seriam so desenho -- e o ponto do editor e que ligar
/// o fio mude a simulacao de verdade.
/// </summary>
public class Netlist
{
    readonly Dictionary<string, string> pai = [];
    readonly bool resistorConduz = true;

    /// <summary>
    /// Furos de protoboard com alguma perna dentro. Usado para desenhar
    /// os slots preenchidos -- os outros milhares ficam de fora.
    /// </summary>
    public HashSet<string> FurosOcupados { get; } = [];

    public Netlist(Circuito circuito) : this(circuito, true) { }

    public Netlist(Circuito circuito, bool resistorConduz)
    {
        this.resistorConduz = resistorConduz;
        var resistores = new List<(string A, string B)>();

        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            foreach (var pino in def.Pinos) Registrar(No(peca.Id, pino.Nome));

            // Ligacoes internas da propria peca.
            foreach (var (a, b) in Catalogo.LigacoesInternas(peca.Tipo))
                Unir(No(peca.Id, a), No(peca.Id, b));

            foreach (var (a, b) in InternasDaPeca(peca.Tipo))
            {
                if (peca.Tipo == "resistor")
                {
                    if (resistorConduz) resistores.Add((No(peca.Id, a), No(peca.Id, b)));
                    continue;
                }
                Unir(No(peca.Id, a), No(peca.Id, b));
            }
        }

        foreach (var fio in circuito.Fios)
            Unir(No(fio.DePeca, fio.DePino), No(fio.ParaPeca, fio.ParaPino));

        LigarPelosFuros(circuito);
        UnirResistores(circuito, resistores);
    }

    /// <summary>
    /// O resistor leva o sinal -- o LED em serie com o pino acende com ele --,
    /// mas o que liga um pino da placa a um trilho nao faz do pino o trilho:
    /// o pull-up de uma chave e as pernas de um divisor de tensao so PUXAM o
    /// pino. Unidos, o pino virava o 3V3 (ou o GND), e a chave ligada nele
    /// passava a "estar" no pino de outra coisa qualquer do mesmo trilho.
    ///
    /// Os resistores entram por ultimo, depois dos fios e da protoboard.
    /// </summary>
    void UnirResistores(Circuito circuito, List<(string A, string B)> resistores)
    {
        if (resistores.Count == 0) return;

        var placas = new Dictionary<string, PerfilPlaca>();
        var fontes = new HashSet<string>();
        foreach (var p in circuito.Pecas)
        {
            if (PerfilPlaca.De(p.Tipo) is { } perfil) placas[p.Id] = perfil;
            if (EhFonte(p.Tipo)) fontes.Add(p.Id);
        }

        bool EhTrilho(string no)
        {
            int corte = no.IndexOf(':');
            if (corte < 0 || !fontes.Contains(no[..corte])) return false;
            string pino = no[(corte + 1)..];
            return EhPinoDeAlimentacao(pino) || EhPinoDeTerra(pino) || pino is "+" or "-";
        }

        bool EhGpio(string no)
        {
            int corte = no.IndexOf(':');
            return corte >= 0 && placas.TryGetValue(no[..corte], out var perfil)
                   && perfil.Numero(no[(corte + 1)..]) >= 0;
        }

        var comGpio = new HashSet<string>();
        var comTrilho = new HashSet<string>();
        foreach (string no in pai.Keys.ToList())
        {
            if (EhGpio(no)) comGpio.Add(Raiz(no));
            if (EhTrilho(no)) comTrilho.Add(Raiz(no));
        }

        foreach (var (a, b) in resistores)
        {
            string ra = Raiz(a), rb = Raiz(b);
            bool puxa = comGpio.Contains(ra) && comTrilho.Contains(rb)
                        || comGpio.Contains(rb) && comTrilho.Contains(ra);
            if (puxa || ra == rb) continue;

            // O no unido herda o que os dois tinham: o proximo resistor ja
            // ve que este lado agora tem pino (ou trilho).
            Unir(a, b);
            string unido = Raiz(a);
            if (comGpio.Contains(ra) || comGpio.Contains(rb)) comGpio.Add(unido);
            if (comTrilho.Contains(ra) || comTrilho.Contains(rb)) comTrilho.Add(unido);
        }
    }

    /// <summary>Raio, em pixels, para considerar a perna dentro do furo.</summary>
    public const double RaioDoFuro = 4.5;

    /// <summary>
    /// Perna encostada num furo conduz, sem precisar de fio desenhado --
    /// e o que faz a protoboard ser um componente de verdade e nao so um
    /// desenho de fundo. E como funciona na bancada: o LED espetado na
    /// coluna ja esta ligado a ela.
    /// </summary>
    void LigarPelosFuros(Circuito circuito)
    {
        var placas = circuito.Pecas
            .Where(p => Catalogo.EhProtoboard(p.Tipo))
            .Select(p => (peca: p, def: Catalogo.Obter(p.Tipo)))
            .Where(x => x.def is not null)
            .ToList();
        if (placas.Count == 0) return;

        // Furos indexados por celula da grade: evita comparar todo pino
        // com os ~1200 furos de uma protoboard de 830.
        var grade = new Dictionary<(int, int), List<(string peca, string pino)>>();
        double celula = Catalogo.Passo;

        foreach (var (placa, def) in placas)
        {
            foreach (var furo in def.Pinos)
            {
                var q = Geometria.PosicaoPino(placa, def, furo);
                var chave = ((int)Math.Floor(q.X / celula), (int)Math.Floor(q.Y / celula));
                if (!grade.TryGetValue(chave, out var lista))
                    grade[chave] = lista = [];
                lista.Add((placa.Id, furo.Nome));
            }
        }

        foreach (var peca in circuito.Pecas)
        {
            if (Catalogo.EhProtoboard(peca.Tipo)) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            foreach (var pino in def.Pinos)
            {
                var q = Geometria.PosicaoPino(peca, def, pino);
                int gx = (int)Math.Floor(q.X / celula), gy = (int)Math.Floor(q.Y / celula);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!grade.TryGetValue((gx + dx, gy + dy), out var candidatos)) continue;

                    foreach (var (idPlaca, nomeFuro) in candidatos)
                    {
                        var placa = circuito.Achar(idPlaca);
                        var defPlaca = Catalogo.Obter(placa.Tipo);
                        var furo = defPlaca.Pinos.First(f => f.Nome == nomeFuro);
                        var pf = Geometria.PosicaoPino(placa, defPlaca, furo);

                        double ex = pf.X - q.X, ey = pf.Y - q.Y;
                        if (ex * ex + ey * ey <= RaioDoFuro * RaioDoFuro)
                        {
                            Unir(No(peca.Id, pino.Nome), No(idPlaca, nomeFuro));
                            FurosOcupados.Add(No(idPlaca, nomeFuro));
                        }
                    }
                }
            }
        }
    }

    /// <summary>Ligacoes que existem dentro do componente.</summary>
    static IEnumerable<(string, string)> InternasDaPeca(string tipo)
    {
        switch (tipo)
        {
            // Resistor conduz: as duas pernas sao o mesmo no. A resistencia
            // nao muda o nivel logico, entao para a simulacao ele e um fio.
            case "resistor":
                yield return ("1", "2");
                break;

            // Botao de 4 pernas: cada par lateral e a MESMA perna de metal.
            // 1 e 2 so se ligam quando pressionado -- isso e dinamico e fica
            // com a Maquina, nao com o netlist.
            case "pushbutton":
            case "pushbutton-6mm":
                yield return ("1.l", "1.r");
                yield return ("2.l", "2.r");
                break;
        }
    }

    public static string No(string peca, string pino) => $"{peca}:{pino}";

    /// <summary>
    /// Nome de pino de terra ("GND", "GND.2", "VSS").
    ///
    /// Fica aqui, e nao na Queima, porque quem precisa da mesma resposta
    /// e o desenho: um LED ligado no trilho acende, e para saber isso e
    /// preciso saber o que e trilho.
    /// </summary>
    // (o AGND da Pico: o plano de terra analogico, que se junta ao GND na placa)
    public static bool EhPinoDeTerra(string pino) => SemSufixo(pino) is "GND" or "VSS" or "AGND";

    /// <summary>Nome de pino de alimentacao ("5V", "3V3", "VIN", "V+"...).</summary>
    public static bool EhPinoDeAlimentacao(string pino)
        => SemSufixo(pino) is "3V3" or "3.3V" or "5V" or "VIN" or "VCC" or "VDD"
                           or "VBUS" or "VUSB" or "V+" or "IOREF" or "VBAT";

    /// <summary>
    /// Tira so o sufixo de repeticao ("5V.2" -> "5V").
    ///
    /// Cortar em qualquer ponto transformaria "3.3V" em "3" e o pino de
    /// alimentacao do Arduino passaria batido.
    /// </summary>
    static string SemSufixo(string pino)
    {
        if (pino is null) return "";
        int ponto = pino.LastIndexOf('.');
        return ponto > 0 && pino[(ponto + 1)..].All(char.IsAsciiDigit)
            ? pino[..ponto] : pino;
    }

    /// <summary>
    /// A peca FORNECE alimentacao (ou terra) pelos pinos com esse nome?
    ///
    /// A placa fornece; o simbolo de VCC e o de GND, que existem so para
    /// isso, tambem. O VCC de um sensor NAO: ali e por onde ele consome.
    /// </summary>
    public static bool EhFonte(string tipo)
        => PerfilPlaca.EhPlaca(tipo) || tipo is "vcc-symbol" or "gnd-symbol" || EhBateria(tipo);

    /// <summary>O "+" e o "-" da bateria valem como alimentacao e terra.</summary>
    public static bool EhBateria(string tipo) => Baterias.EhBateria(tipo);

    void Registrar(string no) => pai.TryAdd(no, no);

    string Raiz(string no)
    {
        if (!pai.ContainsKey(no)) { pai[no] = no; return no; }
        while (pai[no] != no) { pai[no] = pai[pai[no]]; no = pai[no]; }
        return no;
    }

    void Unir(string a, string b)
    {
        Registrar(a); Registrar(b);
        string ra = Raiz(a), rb = Raiz(b);
        if (ra != rb) pai[ra] = rb;
    }

    public bool MesmoNo(string a, string b) => Raiz(a) == Raiz(b);

    /// <summary>
    /// Este pino chega num trilho de alimentacao (ou num terra) de
    /// verdade -- de uma placa ou de um simbolo.
    ///
    /// E o que faz o simbolo de VCC valer alguma coisa: sem isto ele
    /// seria um desenho bonito ligado a nada.
    /// </summary>
    public bool AlcancaFonte(Circuito circuito, string peca, string pino, bool terra)
    {
        string chave = No(peca, pino);
        if (!pai.ContainsKey(chave)) return false;

        // A resposta e da REDE, e a rede nao muda enquanto este netlist
        // existir: guardada, a pergunta de cada LED a cada quadro vira
        // uma consulta, e nao uma volta por todas as pecas e pinos.
        string raiz = Raiz(chave);
        if (fontesPorRaiz.TryGetValue((raiz, terra), out bool guardada)) return guardada;

        bool achou = AlcancaFonteSemGuardar(circuito, raiz, terra);
        fontesPorRaiz[(raiz, terra)] = achou;
        return achou;
    }

    readonly Dictionary<(string Raiz, bool Terra), bool> fontesPorRaiz = [];

    bool AlcancaFonteSemGuardar(Circuito circuito, string raiz, bool terra)
    {
        foreach (var outra in circuito.Pecas)
        {
            if (!EhFonte(outra.Tipo)) continue;

            var def = Catalogo.Obter(outra.Tipo);
            if (def is null) continue;

            foreach (var p in def.Pinos)
            {
                bool serve = terra ? EhPinoDeTerra(p.Nome) : EhPinoDeAlimentacao(p.Nome);
                if (!serve) continue;

                string no = No(outra.Id, p.Nome);
                if (pai.ContainsKey(no) && Raiz(no) == raiz) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Nome do no que representa a rede inteira. Serve para agrupar
    /// pinos por rede sem varrer a rede toda para cada um.
    /// </summary>
    public string RaizDe(string no) => pai.ContainsKey(no) ? Raiz(no) : null;

    /// <summary>Quantas PECAS diferentes cada rede toca.</summary>
    Dictionary<string, HashSet<string>> donosDaRede;

    /// <summary>
    /// O pino divide rede com alguma OUTRA PECA? E o que permite pintar
    /// de amarelo so os pinos realmente em uso.
    ///
    /// Conta pecas, e nao pinos: por dentro, um botao ja liga 1.l a 1.r
    /// e 2.l a 2.r, e um LED de barra liga o catodo comum. Contando
    /// pinos, essas redes internas tinham dois membros e todo botao
    /// solto na bancada nascia com os quatro aneis dourados acesos,
    /// dizendo que estava ligado em algo quando nao havia fio nenhum.
    /// </summary>
    public bool EstaLigado(string peca, string pino)
    {
        if (donosDaRede is null)
        {
            donosDaRede = [];
            foreach (string no in pai.Keys)
            {
                string r = Raiz(no);
                int corte = no.IndexOf(':');
                if (corte < 0) continue;

                if (!donosDaRede.TryGetValue(r, out var donos))
                    donosDaRede[r] = donos = [];

                donos.Add(no[..corte]);
            }
        }

        string chave = No(peca, pino);
        return pai.ContainsKey(chave)
               && donosDaRede.TryGetValue(Raiz(chave), out var quem)
               && quem.Count > 1;
    }

    /// <summary>
    /// Todos os nos ligados ao mesmo ponto que este. Varre a rede
    /// inteira, entao serve para conferir e depurar -- quem roda a cada
    /// quadro usa GpioDe, que e indexado.
    /// </summary>
    public IEnumerable<string> Rede(string no)
    {
        string r = Raiz(no);
        return pai.Keys.Where(k => Raiz(k) == r);
    }

    Dictionary<string, int> gpioPorRaiz;
    Dictionary<string, string> placaPorRaiz;
    Dictionary<string, PerfilPlaca> perfilPorPeca;

    /// <summary>
    /// Indexa, de uma vez so, o GPIO que cada raiz alcanca.
    ///
    /// Antes GpioDe montava o dicionario de placas e varria TODOS os nos
    /// a cada chamada -- e ele e chamado varias vezes por peca, 25 vezes
    /// por segundo. Num circuito de aula com uma protoboard de 830 a rede
    /// passa de 1700 nos, e o quadro custava 3,7 ms so nisso.
    ///
    /// O netlist nao muda depois de construido (mexer no circuito cria um
    /// novo), entao dá para indexar na primeira consulta e reusar.
    /// </summary>
    void Indexar(Circuito circuito)
    {
        if (gpioPorRaiz is not null) return;

        perfilPorPeca = [];
        foreach (var p in circuito.Pecas)
            if (PerfilPlaca.De(p.Tipo) is { } perfil) perfilPorPeca[p.Id] = perfil;

        gpioPorRaiz = [];
        placaPorRaiz = [];
        if (perfilPorPeca.Count == 0) return;

        // Copia as chaves: Raiz() compacta o caminho e mexe no dicionario.
        foreach (string no in pai.Keys.ToList())
        {
            int corte = no.IndexOf(':');
            if (corte < 0) continue;
            if (!perfilPorPeca.TryGetValue(no[..corte], out var perfil)) continue;

            int gpio = perfil.Numero(no[(corte + 1)..]);
            if (gpio < 0 || gpio >= perfil.NumPinos) continue;

            string raiz = Raiz(no);
            gpioPorRaiz.TryAdd(raiz, gpio);

            // De QUAL placa e este pino. Com duas placas no circuito, o
            // "GPIO 14" sozinho nao diz nada: cada uma tem o seu, e sao
            // maquinas diferentes.
            //
            // A primeira ganha se duas placas dividirem o mesmo no. Isso
            // e um curto entre saidas, e quem avisa disso e a Queima --
            // aqui so nao da para escolher melhor.
            placaPorRaiz.TryAdd(raiz, no[..corte]);
        }
    }

    /// <summary>
    /// GPIO do ESP32 alcancado a partir deste pino, ou -1. E o que liga o
    /// desenho a simulacao: LED cujo anodo chega no D14 acende com
    /// digitalWrite(14, HIGH).
    /// </summary>
    public int GpioDe(Circuito circuito, string peca, string pino)
    {
        // Cada placa tem o seu jeito de nomear: "D13" no ESP32, "13" no
        // Uno, "PB0" no Franzininho. O perfil sabe traduzir, e o indice
        // ja guarda a traducao por raiz.
        Indexar(circuito);
        string chave = No(peca, pino);
        return pai.ContainsKey(chave)
            ? gpioPorRaiz.GetValueOrDefault(Raiz(chave), -1)
            : -1;
    }

    /// <summary>
    /// A placa que este pino alcanca, e o numero do GPIO nela.
    ///
    /// Devolve os dois juntos de proposito: perguntar so o numero levava
    /// a indexar a maquina errada quando ha mais de uma placa, e esse
    /// tipo de erro nao aparece no circuito de uma placa so -- que e o
    /// caso de quase todo teste.
    /// </summary>
    public (string Placa, int Gpio) PinoDe(Circuito circuito, string peca, string pino)
    {
        Indexar(circuito);
        string chave = No(peca, pino);
        if (!pai.ContainsKey(chave)) return (null, -1);

        string raiz = Raiz(chave);
        return (placaPorRaiz.GetValueOrDefault(raiz),
                gpioPorRaiz.GetValueOrDefault(raiz, -1));
    }

    /// <summary>Os nomes dos pinos de placa no mesmo no deste pino ("5V", "GND.2", "D13"...).</summary>
    public IEnumerable<string> PinosDePlacaNoMesmoNo(Circuito circuito, string peca, string pino)
    {
        string raiz = RaizDe(No(peca, pino));
        if (raiz is null) yield break;
        foreach (var p in circuito.Pecas)
        {
            if (PerfilPlaca.De(p.Tipo) is null || Catalogo.Obter(p.Tipo) is not { } def) continue;
            foreach (var x in def.Pinos)
                if (RaizDe(No(p.Id, x.Nome)) == raiz) yield return x.Nome;
        }
    }

    readonly Dictionary<(string, int), List<(string Placa, int Gpio)>> noMesmoFio = [];

    /// <summary>
    /// Os pinos de placa ligados no mesmo no que o GPIO de uma placa -- de
    /// outra placa ou dela mesma (o TX no RX). E o fio de uma UART.
    /// </summary>
    public List<(string Placa, int Gpio)> PlacasNoMesmoNo(Circuito circuito, string placa, int gpio)
    {
        lock (noMesmoFio)
        {
            if (noMesmoFio.TryGetValue((placa, gpio), out var pronto)) return pronto;
            Indexar(circuito);
            var lista = new List<(string, int)>();
            string raiz = null;
            if (perfilPorPeca.TryGetValue(placa, out var perfilDaPlaca))
                foreach (string no in pai.Keys.ToList())
                    if (no.StartsWith(placa + ":", StringComparison.Ordinal) && perfilDaPlaca.Numero(no[(placa.Length + 1)..]) == gpio)
                    {
                        raiz = Raiz(no);
                        break;
                    }
            if (raiz is not null)
                foreach (string no in pai.Keys.ToList())
                {
                    int corte = no.IndexOf(':');
                    if (corte < 0 || !perfilPorPeca.TryGetValue(no[..corte], out var perfil) || Raiz(no) != raiz) continue;
                    int g = perfil.Numero(no[(corte + 1)..]);
                    if (g < 0 || no[..corte] == placa && g == gpio) continue;
                    if (!lista.Contains((no[..corte], g))) lista.Add((no[..corte], g));
                }
            noMesmoFio[(placa, gpio)] = lista;
            return lista;
        }
    }

    /// <summary>Perfil da primeira placa do circuito, ou null.</summary>
    public static PerfilPlaca PlacaDoCircuito(Circuito circuito)
    {
        foreach (var p in circuito.Pecas)
        {
            var perfil = PerfilPlaca.De(p.Tipo);
            if (perfil is not null) return perfil;
        }
        return null;
    }

}
