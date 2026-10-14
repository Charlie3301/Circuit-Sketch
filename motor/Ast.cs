namespace Simulador;

public abstract record No(int Linha);

// ---------- expressoes ----------
public abstract record Expr(int Linha) : No(Linha);
public record Literal(double Valor, string Texto, bool Inteiro, bool EhTexto, int Linha) : Expr(Linha)
{
    /// <summary>Literal de caractere ('a'): guarda o codigo, mas imprime a letra.</summary>
    public bool Caractere { get; init; }
}
public record Variavel(string Nome, int Linha) : Expr(Linha);
public record Binaria(string Op, Expr Esq, Expr Dir, int Linha) : Expr(Linha);
public record Unaria(string Op, Expr Alvo, int Linha) : Expr(Linha);
public record PosFixa(string Op, Expr Alvo, int Linha) : Expr(Linha);
public record Atribuicao(string Op, Expr Alvo, Expr Valor, int Linha) : Expr(Linha);
public record Chamada(Expr Alvo, List<Expr> Args, int Linha) : Expr(Linha);
public record Membro(Expr Obj, string Nome, int Linha) : Expr(Linha);
public record Indice(Expr Alvo, Expr Idx, int Linha) : Expr(Linha);
public record Ternaria(Expr Cond, Expr Sim, Expr Nao, int Linha) : Expr(Linha);
/// <summary>Conversao "(char)x", "(int)x", "(char*)payload".</summary>
public record Conversao(string Tipo, Expr Alvo, int Linha) : Expr(Linha);
/// <summary>"sizeof(unsigned long)": um TIPO no lugar da expressao.</summary>
public record NomeDeTipo(string Tipo, int Linha) : Expr(Linha);
/// <summary>Funcao anonima: server.on("/", []() { ... }).</summary>
public record FuncaoAnonima(List<Param> Params, CmdBloco Corpo, int Linha) : Expr(Linha);

/// <summary>
/// "new Botao(4)", "new uint8_t[n]". Com Tamanho, e um vetor; sem ele, um
/// objeto montado pelo construtor.
/// </summary>
public record Novo(string Tipo, List<Expr> Args, Expr Tamanho, int Linha) : Expr(Linha);

// ---------- so do Python ----------
/// <summary>"[1, 2, 3]" ou "(1, 2, 3)".</summary>
public record ListaLiteral(List<Expr> Itens, bool EhTupla, int Linha) : Expr(Linha);

/// <summary>"{\"temp\": 20, \"umidade\": 50}".</summary>
public record DicionarioLiteral(List<Expr> Chaves, List<Expr> Valores, int Linha) : Expr(Linha);

/// <summary>
/// Fatia de lista ou texto: "msg[1:4]", "linha[:-1]".
///
/// Limite ausente vira null, e nao 0: "x[:3]" e "x[0:3]" dao no mesmo,
/// mas "x[-2:]" so funciona se o fim souber que nao foi escrito.
/// </summary>
public record Fatia(Expr Alvo, Expr Inicio, Expr Fim, int Linha) : Expr(Linha);

/// <summary>
/// Argumento com nome: "I2C(0, scl=Pin(22))".
///
/// Nao e uma expressao de verdade -- so aparece dentro da lista de
/// argumentos de uma chamada, e quem o le e a biblioteca.
/// </summary>
public record ArgNomeado(string Nome, Expr Valor, int Linha) : Expr(Linha);

// ---------- comandos ----------
public abstract record Cmd(int Linha) : No(Linha);
public record CmdExpr(Expr E, int Linha) : Cmd(Linha);
public record CmdDecl(string Tipo, string Nome, Expr Inicial, bool EhArray, Expr Tamanho,
                      List<Expr> Itens, int Linha) : Cmd(Linha)
{
    /// <summary>
    /// "static int voltas = 0;" dentro de uma funcao: nasce e e inicializada
    /// uma vez so, e guarda o valor entre uma chamada e outra.
    /// </summary>
    public bool EstaticoLocal { get; init; }

    /// <summary>
    /// "extern int contador;": so promete que a variavel existe em outro
    /// arquivo. Nao cria nada -- quem cria e a declaracao de verdade, e
    /// criar aqui zeraria o valor que ela ja tivesse recebido.
    /// </summary>
    public bool Externo { get; init; }

    /// <summary>
    /// A segunda dimensao: "char teclas[4][4]".
    ///
    /// O vetor continua sendo UM so, esticado; esta e a largura de cada
    /// linha, que e o que transforma "teclas[2][3]" no indice 11.
    /// </summary>
    public Expr Colunas { get; init; }

    /// <summary>
    /// As dimensoes de dentro, da segunda em diante: "[3][4]" em "int
    /// cubo[2][3][4]". O <see cref="Colunas"/> e o produto delas -- o passo
    /// do primeiro indice --, que e o que o resto do motor ja usava.
    /// </summary>
    public List<Expr> Dimensoes { get; init; }

    /// <summary>
    /// A secao da memoria que o __attribute__((section(...))) pediu. E o
    /// que o RTC_DATA_ATTR da ESP32 vira: ".rtc.data", a memoria que
    /// continua ligada no sono profundo.
    /// </summary>
    public string Secao { get; init; }

    /// <summary>
    /// "Coisa &amp;c = coisas[i];" e "int &amp;r = x;": um outro nome para o
    /// que ja existe. Mexer em c e mexer no item do vetor.
    /// </summary>
    public bool PorReferencia { get; init; }
}
public record CmdBloco(List<Cmd> Corpo, int Linha) : Cmd(Linha);
/// <summary>Varios comandos no MESMO escopo: "int a = 1, b = 2;".</summary>
public record CmdVarios(List<Cmd> Corpo, int Linha) : Cmd(Linha);
public record CmdSe(Expr Cond, Cmd Entao, Cmd Senao, int Linha) : Cmd(Linha);
public record CmdEnquanto(Expr Cond, Cmd Corpo, int Linha) : Cmd(Linha);
public record CmdPara(Cmd Ini, Expr Cond, Expr Passo, Cmd Corpo, int Linha) : Cmd(Linha);
public record CmdFacaEnquanto(Cmd Corpo, Expr Cond, int Linha) : Cmd(Linha);
/// <summary>Um "case X:" ou, quando Valor e null, o "default:".</summary>
public record CasoSwitch(Expr Valor, List<Cmd> Corpo);
public record CmdEscolha(Expr Sujeito, List<CasoSwitch> Casos, int Linha) : Cmd(Linha);
/// <summary>Declaracao de objeto: "LiquidCrystal lcd(12, 11, 5, 4, 3, 2);".</summary>
public record CmdDeclObj(string Classe, string Nome, List<Expr> Args, int Linha)
    : Cmd(Linha)
{
    /// <summary>"static Servo s;" dentro de uma funcao: o mesmo objeto em toda chamada.</summary>
    public bool EstaticoLocal { get; init; }

    /// <summary>
    /// Valor inicial, como em "File dados = SD.open(...)". A maioria das
    /// bibliotecas usa argumentos no construtor; o cartao SD e a que
    /// entrega o objeto pronto por uma chamada.
    /// </summary>
    public Expr Inicial { get; init; }
}
public record CmdEstrutura(List<string> Nomes, List<CmdDecl> Campos, int Linha) : Cmd(Linha);

/// <summary>
/// Uma classe do C++ -- ou uma struct com metodo, construtor ou base.
///
/// A struct so com campos continua sendo <see cref="CmdEstrutura"/>, que
/// e o caminho de sempre; esta e a que tem comportamento. E o que as
/// bibliotecas de fora sao por dentro: "class Botao { ... }" com
/// construtor, campos privados e metodos.
/// </summary>
public record ClasseDecl(string Nome, List<string> Bases, int Linha) : Cmd(Linha)
{
    public bool EhStruct { get; init; }

    /// <summary>Os campos de cada objeto, na ordem em que o C++ os inicia.</summary>
    public List<Cmd> Campos { get; init; } = [];

    /// <summary>Os "static" da classe e as constantes dos enum de dentro dela.</summary>
    public List<Cmd> Estaticos { get; init; } = [];

    /// <summary>Metodos, construtores e o destrutor -- com corpo ou so declarados.</summary>
    public List<Funcao> Metodos { get; init; } = [];
}

/// <summary>Um item da lista de inicializacao: ": _pino(pino), Base(x)".</summary>
public record Inicializador(string Nome, List<Expr> Args);
public record CmdRetorno(Expr Valor, int Linha) : Cmd(Linha);
public record CmdBreak(int Linha) : Cmd(Linha);
public record CmdContinue(int Linha) : Cmd(Linha);
public record CmdVazio(int Linha) : Cmd(Linha);

/// <summary>
/// "for p in range(8, 15):" -- o for do Python, que percorre uma
/// sequencia em vez de contar com indice proprio.
/// </summary>
public record CmdParaCada(List<string> Nomes, Expr Sequencia, Cmd Corpo, int Linha) : Cmd(Linha);

/// <summary>
/// "for (int x : leituras)" e "for (auto &amp;b : botoes)": o for do C++ que
/// percorre um vetor inteiro. Com o "&amp;", mexer em x e mexer no item.
/// </summary>
public record CmdParaCadaC(string Tipo, string Nome, bool PorReferencia, Expr Sequencia, Cmd Corpo, int Linha)
    : Cmd(Linha);

/// <summary>
/// "import machine" / "from machine import Pin, I2C".
///
/// Nao carrega arquivo nenhum: so declara os nomes para o interpretador
/// saber que "Pin" veio de "machine". Modulo que o simulador nao conhece
/// da erro aqui, com a lista do que existe.
/// </summary>
public record CmdImporta(string Modulo, List<string> Nomes, string Apelido, int Linha) : Cmd(Linha);

// ---------- topo ----------
public record Param(string Tipo, string Nome)
{
    /// <summary>
    /// O valor de quando o argumento nao vem: "def piscar(pino, vezes=3)".
    /// Nulo em C, que nao tem isso.
    /// </summary>
    public Expr Padrao { get; init; }

    /// <summary>
    /// "int &amp;x": o parametro E a variavel de quem chamou. Mexer nele muda
    /// a de fora -- e o que o "trocar(a, b)" de toda aula de funcao precisa.
    /// </summary>
    public bool PorReferencia { get; init; }

    /// <summary>
    /// "int v[]": declarado com colchetes. Chega como ponteiro do mesmo
    /// jeito, mas e o que o compilador olha para avisar do sizeof.
    /// </summary>
    public bool EhVetor { get; init; }

    /// <summary>"int m[][4]", "byte q[2][8][8]": as dimensoes de dentro, que dizem onde cada linha comeca.</summary>
    public List<Expr> Dimensoes { get; init; }
}
public record Funcao(string Retorno, string Nome, List<Param> Params, CmdBloco Corpo, int Linha)
{
    /// <summary>A classe dona do metodo, ou null numa funcao solta.</summary>
    public string Classe { get; init; }

    public bool Estatico { get; init; }
    public bool Virtual { get; init; }
    public bool EhConstrutor { get; init; }

    /// <summary>": _pino(pino), Base(x)" de um construtor.</summary>
    public List<Inicializador> Inicializadores { get; init; } = [];

    /// <summary>
    /// "void piscar(int vezes);": so a declaracao, sem corpo. O corpo vem
    /// depois, ou em outro arquivo.
    /// </summary>
    public bool Prototipo => Corpo is null;
}
public record Programa(List<Cmd> Globais, List<Funcao> Funcoes)
{
    /// <summary>
    /// Em que linguagem o sketch foi escrito.
    ///
    /// O interpretador e o mesmo para as duas; o que muda sao detalhes
    /// de semantica que nao da para adivinhar pela arvore, como
    /// atribuicao que declara variavel e divisao que sempre da real.
    /// </summary>
    public Linguagem Linguagem { get; init; } = Linguagem.Arduino;

    /// <summary>
    /// Para o Assembly: o programa montado, instrucao a instrucao.
    ///
    /// E a unica linguagem que nao cabe numa arvore -- nao ha expressao
    /// nem funcao em Assembly, ha instrucao e registrador. Entao o
    /// Programa dela carrega o codigo montado aqui, e as duas funcoes de
    /// fachada (setup e loop) so chamam o executor.
    ///
    /// Fica como object para o Ast nao depender do montador: quem o
    /// converte e o interpretador, que conhece os dois.
    /// </summary>
    public object Montado { get; init; }

    /// <summary>As classes do programa, de todos os arquivos juntos.</summary>
    public List<ClasseDecl> Classes { get; init; } = [];

    /// <summary>O que o pre-processador achou que vale avisar (#warning).</summary>
    public List<string> Avisos { get; init; } = [];

    /// <summary>
    /// Os arquivos .c do projeto e das bibliotecas, numa placa ESP32: esses
    /// nao passam pelo interpretador -- sao compilados de verdade (TinyCC) e
    /// rodam nativos, com o ESP-IDF do simulador por baixo. O sketch chama
    /// as funcoes deles pelo nome, como na placa.
    /// </summary>
    public List<FonteNativa> Nativos { get; init; } = [];

    /// <summary>O id da placa para a qual o programa foi compilado.</summary>
    public string Placa { get; init; }

    /// <summary>
    /// O partitions.csv do projeto, se ele tiver um: como na Arduino IDE, ele
    /// troca a tabela de particoes da placa (o esp_partition_find do codigo C
    /// acha as particoes dele). Nulo: a tabela padrao da placa.
    /// </summary>
    public string TabelaDeParticoes { get; init; }

    /// <summary>
    /// O texto do sketch principal. Serve para saber o que o programa usa do
    /// core -- o Serial so ocupa RAM no Uno se o sketch o chamar.
    /// </summary>
    public string Fonte { get; init; }
}

/// <summary>
/// Um .c que roda nativo. <paramref name="Caminho"/> e o arquivo no disco
/// (biblioteca instalada); sem ele, o texto e o de uma aba do projeto.
/// <paramref name="Inclusao"/> e a pasta onde o "#include &lt;...&gt;" da
/// biblioteca procura (a src/ dela).
/// </summary>
public sealed record FonteNativa(string Nome, string Caminho, string Texto, string Inclusao, string Biblioteca);
