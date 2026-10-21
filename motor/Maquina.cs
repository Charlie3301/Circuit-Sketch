using System.Diagnostics;

namespace Simulador;

// ============================================================
//  Valor
// ============================================================
public readonly struct Valor(double num, string texto, bool inteiro, bool ehTexto,
                            bool caractere = false, object referencia = null)
{
    public double Num { get; } = num;
    public string Texto { get; } = texto;
    public bool Inteiro { get; } = inteiro;
    public bool EhTexto { get; } = ehTexto;

    /// <summary>
    /// O valor por referencia: lista, dicionario ou objeto de biblioteca.
    ///
    /// O C do Arduino nao precisava disto -- vetor mora no Ambiente com
    /// nome proprio, e objeto idem. Python passa lista e objeto DENTRO de
    /// expressao ("rgb = neopixel.NeoPixel(pino, 16)", "sensor.measure()"),
    /// entao o valor tem que saber carregar um.
    /// </summary>
    public object Referencia { get; } = referencia;

    /// <summary>
    /// Um char: guarda o CODIGO e conta como numero, mas aparece como
    /// letra. E o que faz "msg += (char)payload[i]" montar "hello" em vez
    /// de "104101108108111".
    /// </summary>
    public bool Caractere { get; } = caractere;

    public static Valor DeInt(double n) => new(Math.Truncate(n), null, true, false);
    public static Valor DeReal(double n) => new(n, null, false, false);
    public static Valor DeTexto(string s) => new(0, s, false, true);

    /// <summary>
    /// O tipo do C deste numero ("unsigned long", "float"), ou Nenhum. E ele
    /// que decide a conta: no Uno, int + int estoura em 16 bits.
    /// </summary>
    public TipoC Tipo { get; init; }

    /// <summary>Um numero ja no tipo do C (quem chama converteu).</summary>
    public static Valor DeC(double n, TipoC tipo) => tipo switch
    {
        TipoC.Char or TipoC.SChar => new(n, null, true, false, true) { Tipo = tipo },
        TipoC.Bool => new(n, null, true, false) { Tipo = tipo, EhBooleano = true },
        TipoC.Float or TipoC.Double or TipoC.LongDouble => new(n, null, false, false) { Tipo = tipo },
        TipoC.Nenhum => new(n, null, n == Math.Truncate(n), false),
        _ => new(n, null, true, false) { Tipo = tipo },
    };
    /// <summary>
    /// Verdadeiro ou falso.
    ///
    /// Continua valendo 1 e 0 para a conta, como no C -- o que a marca
    /// muda e so a APARENCIA em Python, onde print(True) escreve
    /// "True" e nao "1".
    /// </summary>
    public static Valor DeBool(bool b) => new(b ? 1 : 0, null, true, false) { EhBooleano = true };

    /// <summary>Este valor veio de uma comparacao ou de True/False.</summary>
    public bool EhBooleano { get; init; }
    public static Valor DeCaractere(double codigo) => new((long)codigo & 0xFF, null, true, false, true);

    /// <summary>O mesmo valor, visto como char: texto vira a primeira letra.</summary>
    public static Valor ComoCaractere(Valor v)
        => v.Caractere ? v
         : v.EhTexto ? DeCaractere(string.IsNullOrEmpty(v.Texto) ? 0 : v.Texto[0])
         : DeCaractere(v.Num);
    public static readonly Valor Vazio = new(0, null, true, false);

    /// <summary>Um valor que carrega lista, dicionario ou objeto.</summary>
    public static Valor DeReferencia(object o) => new(0, null, true, false, false, o);

    /// <summary>
    /// O None do Python. Nao e o mesmo que Vazio: uma funcao sem return
    /// devolve None, e "if x is None" precisa distinguir isso de 0.
    /// </summary>
    public static readonly Valor Nenhum = new(0, null, true, false, false, NadaPy.Unico);

    public bool EhNenhum => ReferenceEquals(Referencia, NadaPy.Unico);

    public bool Verdadeiro
        => Referencia is not null ? ReferenciaVerdadeira()
         : EhTexto ? !string.IsNullOrEmpty(Texto)
         : Num != 0;

    /// <summary>
    /// A regra do Python: lista e dicionario vazios sao falsos, None e
    /// falso, objeto e sempre verdadeiro.
    /// </summary>
    bool ReferenciaVerdadeira() => Referencia switch
    {
        NadaPy => false,
        ListaPy l => l.Itens.Count > 0,
        DicionarioPy d => d.Itens.Count > 0,

        // O operator bool do File: verdadeiro so com o arquivo aberto.
        // "if (!dados) Serial.println("erro ao abrir");" e o teste que
        // todo exemplo do cartao faz -- e ele nunca disparava.
        Objeto { Classe: "File" } arquivo => arquivo.Arquivo is { Aberto: true },

        // O operator bool das outras classes: "if (central)", "if (!caracteristica)".
        Objeto { Rede: IValorBooleano b } => b.Verdadeiro,
        _ => true
    };

    public string Mostrar()
    {
        // Lista, dicionario e None so existem em Python, e e o Python
        // quem sabe mostra-los -- inclusive o "25.0" onde o Arduino
        // escreveria "25.00".
        if (Referencia is not null) return TextoPy.De(this);

        if (EhTexto) return Texto;

        // O char com sinal do AVR guarda 200 como -56: o byte e o mesmo.
        if (Caractere) return Num == 0 ? "" : ((char)((int)Num & 0xFF)).ToString();
        if (Inteiro)
            return Num >= 9.2233720368547758E18 ? ((ulong)Num).ToString() : ((long)Num).ToString();
        return TextoArduino.Real(Num, 2, Tipo == TipoC.Float);
    }
}

// ============================================================
//  Objeto do aluno (LiquidCrystal, ...)
// ============================================================
/// <summary>
/// O estado de um objeto que tem operator bool: o BLEDevice da ArduinoBLE e
/// verdadeiro so quando aponta um aparelho -- "if (central)" depende disso.
/// </summary>
public interface IValorBooleano
{
    bool Verdadeiro { get; }
}

public class Objeto(string classe)
{
    public string Classe { get; } = classe;
    public Lcd Lcd { get; set; }
    /// <summary>Pino do attach() ou do construtor.</summary>
    public int Pino { get; set; } = -1;
    /// <summary>Fita de LEDs, para o NeoPixel.</summary>
    public Fita Fita { get; set; }
    /// <summary>Pinos do motor de passo / teclado.</summary>
    public int[] Pinos { get; set; } = [];
    /// <summary>Passo atual do motor.</summary>
    public int Passo { get; set; }

    /// <summary>Arquivo do cartao SD que este objeto representa.</summary>
    public ArquivoSd Arquivo { get; set; }

    /// <summary>Tela grafica do display TFT.</summary>
    public TelaGrafica Tela { get; set; }

    /// <summary>Estado de rede deste objeto (WiFiClient, WebServer, PubSubClient...).</summary>
    public object Rede { get; set; }

    /// <summary>Texto que o objeto representa, como o IPAddress.</summary>
    public string Texto { get; set; }

    /// <summary>
    /// Campos que o sketch le com ponto ("results.value").
    ///
    /// So os objetos que a biblioteca real expoe como struct usam isto
    /// -- hoje o decode_results do infravermelho. Nao e um objeto de
    /// verdade com campos livres: quem preenche e o simulador.
    /// </summary>
    public readonly Dictionary<string, Valor> Campos = [];

    /// <summary>
    /// O escopo de um objeto de classe do C++: as variaveis dele SAO os
    /// campos, e os vetores e objetos de dentro moram nele tambem. E por
    /// ele que um metodo enxerga "_pino" sem escrever "this->".
    /// </summary>
    public Ambiente Escopo { get; set; }
}

/// <summary>
/// Um parametro por referencia: "void trocar(int &amp;a, int &amp;b)". Ler e
/// escrever o parametro e ler e escrever a variavel de quem chamou.
/// </summary>
public sealed class Apelido(Func<Valor> ler, Action<Valor> escrever)
{
    public Valor Ler() => ler();
    public void Escrever(Valor v) => escrever(v);
}

// ============================================================
//  Ambiente (escopos encadeados)
// ============================================================
public class Ambiente(Ambiente pai = null)
{
    // As colecoes nascem no primeiro nome guardado nelas. Todo bloco que roda
    // ganha um escopo -- cada chamada de funcao, cada volta de laco, cada
    // switch --, e as sete colecoes vazias de cada um eram quase todo o lixo
    // do interpretador: a coleta de lixo que elas provocavam parava a thread
    // do sketch, e cada parada atrasava um passo da AccelStepper.
    Dictionary<string, Valor> vars;
    Dictionary<string, Valor[]> arrays;
    Dictionary<string, Objeto> objetos;

    /// <summary>Variaveis declaradas como char: o que entrar nelas vira char.</summary>
    HashSet<string> deCaractere;

    /// <summary>Vetores que valem como texto (char, byte).</summary>
    HashSet<string> vetoresDeBytes;

    public Ambiente Pai { get; } = pai;

    /// <summary>
    /// O C da placa (tamanho do int, do double...). Com ele, guardar num nome
    /// que tem tipo converte para o tipo, como a atribuicao do C: "byte b =
    /// 300" guarda 44. Nulo nas linguagens que nao sao C.
    /// </summary>
    public ModeloC Modelo { get; set; } = pai?.Modelo;

    /// <summary>O escopo de um objeto: os campos dele sao as variaveis daqui.</summary>
    public Ambiente(Ambiente pai, Dictionary<string, Valor> campos, Objeto dono) : this(pai)
    {
        vars = campos;
        Dono = dono;
    }

    /// <summary>O objeto dono deste escopo, se ele e o de um objeto.</summary>
    public Objeto Dono { get; }

    /// <summary>A classe dos static que moram aqui, se este e o escopo deles.</summary>
    public string Classe { get; init; }

    /// <summary>O "this" de quem esta rodando: o objeto do metodo mais perto.</summary>
    public Objeto ObjetoAtual()
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.Dono is not null) return e.Dono;
        return null;
    }

    /// <summary>A classe de quem esta rodando -- tambem num metodo static.</summary>
    public string ClasseAtual()
    {
        for (var e = this; e != null; e = e.Pai)
        {
            if (e.Dono is not null) return e.Dono.Classe;
            if (e.Classe is not null) return e.Classe;
        }
        return null;
    }

    public bool BuscarLocal(string n, out Valor v)
    {
        if (vars is not null && vars.TryGetValue(n, out v))
        {
            if (v.Referencia is Apelido a) v = a.Ler();
            return true;
        }
        v = default;
        return false;
    }

    public bool BuscarArrayLocal(string n, out Valor[] a)
    {
        if (arrays is not null && arrays.TryGetValue(n, out a)) return true;
        if (vars is not null && vars.TryGetValue(n, out var v) && v.Referencia is Valor[] vetor) { a = vetor; return true; }
        a = null;
        return false;
    }

    public bool BuscarObjetoLocal(string n, out Objeto o)
    {
        if (objetos is not null && objetos.TryGetValue(n, out o)) return true;
        if (vars is not null && vars.TryGetValue(n, out var v))
        {
            if (v.Referencia is Apelido a) v = a.Ler();
            if (v.Referencia is Objeto dentro) { o = dentro; return true; }
        }
        o = null;
        return false;
    }

    bool TemVar(string n) => vars is not null && vars.ContainsKey(n);
    bool TemArray(string n) => arrays is not null && arrays.ContainsKey(n);
    bool TemObjeto(string n) => objetos is not null && objetos.ContainsKey(n);

    public bool TemLocal(string n) => TemVar(n) || TemArray(n) || TemObjeto(n);

    /// <summary>
    /// As String deste escopo, com o buffer de cada uma na heap da placa.
    /// Nulo enquanto nao ha nenhuma: quem roda o escopo devolve os blocos no
    /// fim dele, como o destrutor da String na chave de fechar.
    /// </summary>
    public Dictionary<string, TextoNaHeap> Textos;

    /// <summary>O buffer da String com esse nome, no escopo em que ela mora (nulo: nao e uma String contada).</summary>
    public TextoNaHeap TextoDe(string n)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.TemVar(n))
                return e.Textos is { } textos && textos.TryGetValue(n, out var t) ? t : null;
        return null;
    }

    /// <summary>
    /// Copia tudo o que mora aqui para outro escopo: e a copia de um objeto
    /// ("Botao b = a;"). Vetor de dentro e copiado; o que a variavel aponta
    /// (um ponteiro) continua o mesmo, como na copia do C++.
    /// </summary>
    public void CopiarConteudo(Ambiente destino, Func<Objeto, Objeto> copiarObjeto)
    {
        foreach (var (k, v) in vars ?? []) (destino.vars ??= [])[k] = v;
        foreach (var (k, a) in arrays ?? []) (destino.arrays ??= [])[k] = [.. a];
        foreach (var (k, o) in objetos ?? []) (destino.objetos ??= [])[k] = copiarObjeto(o);
        if (deCaractere is not null) (destino.deCaractere ??= []).UnionWith(deCaractere);
        if (vetoresDeBytes is not null) (destino.vetoresDeBytes ??= []).UnionWith(vetoresDeBytes);
        foreach (var (k, c) in colunasDoArray ?? []) (destino.colunasDoArray ??= [])[k] = c;
        foreach (var (k, d) in dimensoesDoArray ?? []) (destino.dimensoesDoArray ??= [])[k] = d;
        foreach (var (k, t) in tiposDeclarados ?? []) destino.DeclararTipo(k, t);
    }

    /// <summary>
    /// O tipo escrito na declaracao: "int", "char*", "Leitura".
    ///
    /// O valor sozinho nao diz: um "int" e um "long" guardam o mesmo
    /// numero, e o sizeof de um e 2 no Uno e o do outro, 4.
    ///
    /// Criado so no primeiro nome: todo bloco que roda ganha um escopo, e
    /// um dicionario a mais em cada volta de laco pesaria no sketch.
    /// </summary>
    Dictionary<string, string> tiposDeclarados;

    public void DeclararTipo(string n, string tipo)
    {
        if (tipo is not null) (tiposDeclarados ??= [])[n] = tipo;
    }

    /// <summary>O tipo declarado de um nome, no escopo em que ele mora.</summary>
    public string TipoDe(string n)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.TemLocal(n))
                return e.tiposDeclarados?.GetValueOrDefault(n);
        return null;
    }

    /// <summary>Tira um objeto deste escopo, para ele ir morar em outro.</summary>
    public bool TirarObjeto(string n, out Objeto o)
    {
        if (objetos is not null) return objetos.Remove(n, out o);
        o = null;
        return false;
    }

    public void Declarar(string n, Valor v)
    {
        (vars ??= [])[n] = v;
        deCaractere?.Remove(n);
    }

    public void DeclararArray(string n, Valor[] a) => (arrays ??= [])[n] = a;

    /// <summary>
    /// Vetores de duas dimensoes, e quantas colunas cada um tem.
    ///
    /// O vetor em si continua esticado em <c>arrays</c> -- e assim que o
    /// C guarda de verdade. Aqui fica so a largura da linha, sem a qual
    /// "teclas[1][2]" nao teria como virar um indice.
    /// </summary>
    Dictionary<string, int> colunasDoArray;

    public void DeclararColunas(string n, int colunas) => (colunasDoArray ??= [])[n] = colunas;

    /// <summary>
    /// As dimensoes de dentro de um vetor de tres ou mais ("[3][4]" do
    /// cubo[2][3][4]). O Colunas fica sendo o produto delas: o passo do
    /// primeiro indice.
    /// </summary>
    Dictionary<string, int[]> dimensoesDoArray;

    public void DeclararDimensoes(string n, int[] internas)
    {
        (dimensoesDoArray ??= [])[n] = internas;
        (colunasDoArray ??= [])[n] = internas.Aggregate(1, (a, b) => a * b);
    }

    /// <summary>As dimensoes de dentro (a coluna, no de duas). Falso: o vetor tem uma so.</summary>
    public bool DimensoesDe(string n, out int[] internas)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.TemArray(n))
            {
                if (e.dimensoesDoArray is not null && e.dimensoesDoArray.TryGetValue(n, out internas)) return true;
                if (e.colunasDoArray is not null && e.colunasDoArray.TryGetValue(n, out int c) && c > 0)
                {
                    internas = [c];
                    return true;
                }
                break;
            }
        internas = null;
        return false;
    }

    public bool ColunasDe(string n, out int colunas)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.TemArray(n))
            {
                if (e.colunasDoArray is not null) return e.colunasDoArray.TryGetValue(n, out colunas);
                break;
            }
        colunas = 0; return false;
    }

    /// <summary>
    /// "char c;". Guardar o tipo importa porque a atribuicao seguinte
    /// ("c = client.read()") tem que continuar sendo char -- senao o
    /// sketch volta a imprimir numeros no lugar das letras.
    /// </summary>
    public void DeclararCaractere(string n, Valor v)
    {
        (vars ??= [])[n] = Valor.ComoCaractere(v);
        (deCaractere ??= []).Add(n);
    }

    public void MarcarVetorDeBytes(string n) => (vetoresDeBytes ??= []).Add(n);

    public bool EhVetorDeBytes(string n)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.TemArray(n)) return e.vetoresDeBytes is not null && e.vetoresDeBytes.Contains(n);
        return false;
    }
    public void DeclararObjeto(string n, Objeto o) => (objetos ??= [])[n] = o;

    public bool Buscar(string n, out Valor v)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.vars is not null && e.vars.TryGetValue(n, out v))
            {
                if (v.Referencia is Apelido a) v = a.Ler();
                return true;
            }
        v = Valor.Vazio; return false;
    }

    /// <summary>
    /// O vetor com este nome. Tambem o que um ponteiro guarda: "uint8_t*
    /// buffer = new uint8_t[8]" deixa o vetor dentro da variavel.
    /// </summary>
    public bool BuscarArray(string n, out Valor[] a)
    {
        for (var e = this; e != null; e = e.Pai)
        {
            if (e.arrays is not null && e.arrays.TryGetValue(n, out a)) return true;
            if (e.vars is not null && e.vars.TryGetValue(n, out var v))
            {
                if (v.Referencia is Apelido ap) v = ap.Ler();
                if (v.Referencia is Valor[] vetor) { a = vetor; return true; }
            }
        }
        a = null; return false;
    }

    /// <summary>
    /// O objeto com este nome -- declarado aqui ou guardado numa variavel,
    /// que e como um ponteiro ou uma referencia chega a uma funcao. "this"
    /// e o objeto do metodo que esta rodando.
    /// </summary>
    public bool BuscarObjeto(string n, out Objeto o)
    {
        for (var e = this; e != null; e = e.Pai)
        {
            if (e.objetos is not null && e.objetos.TryGetValue(n, out o)) return true;
            if (e.vars is not null && e.vars.TryGetValue(n, out var v))
            {
                if (v.Referencia is Apelido a) v = a.Ler();
                if (v.Referencia is Objeto guardado) { o = guardado; return true; }
            }
        }

        if (n == "this" && ObjetoAtual() is { } eu) { o = eu; return true; }

        o = null; return false;
    }

    public bool Atribuir(string n, Valor v)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.vars is not null && e.vars.TryGetValue(n, out var atual))
            {
                if (atual.Referencia is Apelido a)
                {
                    a.Escrever(v);
                    return true;
                }
                var novo = e.deCaractere is not null && e.deCaractere.Contains(n) ? Valor.ComoCaractere(v) : v;
                if (atual.Tipo != TipoC.Nenhum) novo = Modelo.ParaOTipo(novo, atual.Tipo);
                e.vars[n] = novo;
                return true;
            }
        return false;
    }

    /// <summary>A variavel e char aqui? E o que um apelido precisa saber ao escrever.</summary>
    public bool EhCaractere(string n)
    {
        for (var e = this; e != null; e = e.Pai)
            if (e.TemVar(n)) return e.deCaractere is not null && e.deCaractere.Contains(n);
        return false;
    }

    /// <summary>Uma variável como o depurador mostra.</summary>
    public sealed record VariavelVista(string Nome, string Valor, bool Global);

    /// <summary>
    /// Tudo o que este ponto do programa enxerga, de dentro para fora.
    ///
    /// Nome repetido (uma local com o mesmo nome de uma global) aparece
    /// UMA vez, com o valor de dentro -- que é o que o sketch lê ali. Mostrar
    /// as duas faria a pessoa achar que o programa está usando a global.
    /// </summary>
    public List<VariavelVista> Listar()
    {
        var vistas = new List<VariavelVista>();
        var vistos = new HashSet<string>();

        for (var e = this; e != null; e = e.Pai)
        {
            bool global = e.Pai is null;

            foreach (var (nome, bruto) in e.vars ?? [])
                if (vistos.Add(nome) && (bruto.Referencia is Apelido ap ? ap.Ler() : bruto) is var valor)
                    vistas.Add(new(nome,
                        valor.EhTexto ? $"\"{valor.Texto}\""
                        : valor.Caractere ? $"'{valor.Mostrar()}'"
                        : valor.Mostrar(), global));

            foreach (var (nome, lista) in e.arrays ?? [])
                if (vistos.Add(nome))
                    vistas.Add(new(nome, MostrarLista(lista), global));

            foreach (var (nome, objeto) in e.objetos ?? [])
                if (vistos.Add(nome))
                    vistas.Add(new(nome, objeto.Classe, global));
        }

        return vistas;
    }

    /// <summary>Os primeiros itens do vetor; o resto vira reticências.</summary>
    static string MostrarLista(Valor[] lista)
    {
        const int Mostra = 8;
        var itens = lista.Take(Mostra).Select(v => v.EhTexto ? $"\"{v.Texto}\"" : v.Mostrar());
        return "[" + string.Join(", ", itens) + (lista.Length > Mostra ? ", …" : "") + "]";
    }
}

// ============================================================
//  Display LCD de caracteres
// ============================================================
public class Lcd(int colunas = 16, int linhas = 2)
{
    public int Colunas { get; private set; } = colunas;
    public int Linhas { get; private set; } = linhas;
    public char[,] Tela { get; private set; } = Vazia(colunas, linhas);
    public int Col { get; private set; }
    public int Lin { get; private set; }
    public bool Ligado { get; private set; }

    /// <summary>
    /// Os 8 caracteres do createChar() (a CGRAM do HD44780): oito linhas de
    /// 5 pontos cada, o bit 4 a esquerda. Aparecem com os codigos 0 a 7
    /// (e 8 a 15, o mesmo de novo), como no chip.
    /// </summary>
    public byte[][] Glifos { get; } = [.. Enumerable.Range(0, 8).Select(_ => new byte[8])];

    /// <summary>A luz de fundo (backlight()/noBacklight() do modulo I2C).</summary>
    public bool LuzDeFundo { get; private set; } = true;

    /// <summary>display()/noDisplay(): os caracteres somem, a memoria fica.</summary>
    public bool Mostrando { get; private set; } = true;

    /// <summary>Muda a cada coisa que muda no vidro: o editor so redesenha quando ela anda.</summary>
    public int Versao { get; private set; }

    public void DefinirGlifo(int n, IReadOnlyList<int> linhas)
    {
        var g = Glifos[n & 7];
        for (int i = 0; i < 8; i++) g[i] = (byte)(i < linhas.Count ? linhas[i] & 0x1F : 0);
        Versao++;
    }

    public void Luz(bool acesa) { LuzDeFundo = acesa; Versao++; }

    public void Mostrar(bool sim) { Mostrando = sim; Versao++; }

    static char[,] Vazia(int c, int l)
    {
        var t = new char[l, c];
        for (int i = 0; i < l; i++)
            for (int j = 0; j < c; j++) t[i, j] = ' ';
        return t;
    }

    public void Begin(int c, int l)
    {
        if (c > 0) Colunas = c;
        if (l > 0) Linhas = l;
        Tela = Vazia(Colunas, Linhas);
        Col = Lin = 0;
        Ligado = true;
        Mostrando = true;
        Versao++;
    }

    /// <summary>
    /// Troca o tamanho sem acender: o construtor do LiquidCrystal_I2C diz
    /// quantas colunas e linhas o display tem, mas quem acende e o init().
    /// </summary>
    public void Dimensionar(int c, int l)
    {
        if (c > 0) Colunas = c;
        if (l > 0) Linhas = l;
        Tela = Vazia(Colunas, Linhas);
        Col = Lin = 0;
    }

    public void Limpar()
    {
        Tela = Vazia(Colunas, Linhas);
        Col = Lin = 0;
        Versao++;
    }

    /// <summary>Sem energia o display apaga o fundo tambem.</summary>
    public void Desligar() { Ligado = false; Versao++; }

    public void PosicionarCursor(int col, int lin)
    {
        Col = Math.Clamp(col, 0, Colunas - 1);
        Lin = Math.Clamp(lin, 0, Linhas - 1);
    }

    /// <summary>
    /// Desce para o comeco da linha seguinte.
    ///
    /// Na ultima linha ele FICA, em vez de rolar a tela: o display de
    /// verdade tambem nao rola sozinho, e ver o texto parar ali ensina
    /// mais do que ver a tela andar.
    /// </summary>
    public void PularLinha()
    {
        Col = 0;
        if (Lin < Linhas - 1) Lin++;
    }

    /// <summary>
    /// Escreve a partir do cursor. Um LCD real NAO quebra linha sozinho:
    /// texto que passa da coluna 16 some. Manter essa fidelidade ajuda o
    /// aluno a enxergar o proprio bug.
    /// </summary>
    public void Escrever(string txt)
    {
        Ligado = true;
        foreach (char ch in txt)
        {
            if (Col >= Colunas) break;
            Tela[Lin, Col] = ch;
            Col++;
        }
        Versao++;
    }

    public string LinhaTexto(int i)
    {
        if (i < 0 || i >= Linhas) return "";
        var sb = new System.Text.StringBuilder();
        for (int j = 0; j < Colunas; j++) sb.Append(Tela[i, j]);
        return sb.ToString();
    }
}

// ============================================================
//  Maquina virtual: pinos, relogio, serial, LCD
// ============================================================
public record EventoPino(long Ms, int Pino, int Valor);

public class Maquina
{
    /// <summary>
    /// Vetores dimensionados pela MAIOR placa (o Mega vai ate 69). Quem
    /// limita de verdade e o perfil: um Uno recusa pino 30.
    /// </summary>
    public static readonly int NumPinos = PerfilPlaca.MaiorNumPinos;

    /// <summary>Placa em uso. Define pinos validos, faixa do ADC e avisos.</summary>
    public PerfilPlaca Perfil
    {
        get => perfil;
        set
        {
            perfil = value;
            memoria = null;
        }
    }

    PerfilPlaca perfil = PerfilPlaca.Esp32;

    /// <summary>
    /// A RAM da placa: as globais, a heap e a pilha (ver <see cref="MemoriaDaPlaca"/>).
    /// Cada rodada comeca com ela vazia, como a placa que acabou de ligar.
    /// </summary>
    public MemoriaDaPlaca Memoria => memoria ??= new MemoriaDaPlaca(MemoriaDaPlaca.De(Perfil));

    MemoriaDaPlaca memoria;

    public readonly int[] Modo = new int[NumPinos];      // 0=INPUT 1=OUTPUT 2=INPUT_PULLUP
    public readonly int[] Digital = new int[NumPinos];
    public readonly int[] Analogico = new int[NumPinos]; // 0..4095 (potenciometro)
    public readonly bool[] Pressionado = new bool[NumPinos];

    /// <summary>
    /// A placa esta energizada? Serve para o LED vermelho de energia da
    /// ESP32, que acende so por estar ligada -- nao depende de GPIO.
    /// </summary>
    public bool Energizada { get; private set; }
    public readonly int[] Pwm = new int[NumPinos];       // 0..255 (brilho)
    public readonly int[] Frequencia = new int[NumPinos];// Hz do tone()

    /// <summary>Quando o tone(pino, f, ms) de cada pino acaba (Ms), ou long.MaxValue.</summary>
    readonly long[] fimDoTom = new long[NumPinos];

    /// <summary>O que os pinos fizeram, em microssegundos -- o que o analisador le.</summary>
    public LinhaDoTempo Linha { get; } = new();

    // ---------------- os fios entre placas ----------------

    /// <summary>O relogio dos fios: o mesmo para todas as placas da bancada (o de cada uma recomeca no reset).</summary>
    public static readonly System.Diagnostics.Stopwatch RelogioDosFios = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Agora, no relogio em que os bytes viajam: o da bancada em tempo real, o da placa no virtual.</summary>
    public double AgoraNoFio => TempoReal ? RelogioDosFios.Elapsed.TotalMilliseconds * 1000 : AgoraUs();

    /// <summary>
    /// Os pinos de placa ligados no mesmo fio que um GPIO desta (de outra
    /// placa, ou desta mesma -- o TX no RX). Quem monta e a bancada, pelo
    /// circuito; sem ela (a linha de comando), nulo.
    /// </summary>
    public Func<int, IReadOnlyList<(Maquina Maquina, int Gpio)>> LigadosAoPino { get; set; }

    /// <summary>As portas seriais que o sketch ligou (begin): e nelas que chega o que vem pelo fio.</summary>
    public readonly List<PortaSerial> Portas = [];

    public List<PortaSerial> PortasNoPino(int gpio)
    {
        lock (Portas) return [.. Portas.Where(p => p.Ligada && p.Rx == gpio)];
    }

    /// <summary>O baud do Serial.begin, ou 0 se o sketch nao comecou a serial.</summary>
    public int BaudDaSerial { get; set; }

    /// <summary>Bits do analogWrite (analogWriteResolution). 8 e o de fabrica.</summary>
    public int ResolucaoDoPwm { get; set; } = 8;

    /// <summary>
    /// O topo do analogWrite pedido por analogWriteRange (ESP8266, Pico),
    /// ou 0 para o que os bits do analogWriteResolution dizem.
    /// </summary>
    public int MaximoDoPwm { get; set; }

    /// <summary>Bits do analogRead pedidos por analogReadResolution, ou 0 para os do core.</summary>
    public int ResolucaoDoAdc { get; set; }

    /// <summary>
    /// A referencia do conversor, pedida por analogReference(): DEFAULT (1)
    /// e a alimentacao, EXTERNAL (0) e a tensao no pino AREF, INTERNAL (3) e
    /// INTERNAL1V1 (2) sao os 1,1 V internos e INTERNAL2V56 (6) os 2,56 V
    /// (Mega e ATtiny85). So os AVR tem; nos outros chips ela fica DEFAULT.
    /// </summary>
    public int ReferenciaAnalogica { get; set; } = ReferenciaPadrao;

    public const int ReferenciaExterna = 0, ReferenciaPadrao = 1, ReferenciaInterna1V1 = 2,
                     ReferenciaInterna = 3, ReferenciaInterna2V56 = 6;

    /// <summary>A tensao no pino AREF (ou no ADC_VREF da Pico) agora (o editor mede a cada quadro); nulo com ele solto.</summary>
    public double? TensaoNoAref { get; set; }

    /// <summary>Em volts, a tensao que o conversor le como o valor maximo.</summary>
    public double TensaoDeReferencia => ReferenciaAnalogica switch
    {
        ReferenciaExterna => TensaoNoAref ?? 0,
        ReferenciaInterna1V1 or ReferenciaInterna => 1.1,
        ReferenciaInterna2V56 => 2.56,
        _ => Perfil.TensaoDeOperacao > 0 ? Perfil.TensaoDeOperacao : 5,
    };

    /// <summary>
    /// A leitura do analogRead() do Arduino: a do conversor, levada aos bits
    /// que o core entrega -- deslocando, como os cores fazem. Com outra
    /// referencia (analogReference), a mesma tensao vira outro numero: com
    /// os 1,1 V internos, 0,55 V ja e meia escala.
    /// </summary>
    public int AnalogReadDoArduino(int p)
    {
        int cru = AnalogRead(p);
        if (ReferenciaAnalogica != ReferenciaPadrao && Perfil.De8Bits)
        {
            double vcc = Perfil.TensaoDeOperacao > 0 ? Perfil.TensaoDeOperacao : 5;
            double vref = TensaoDeReferencia;
            // AREF solto com EXTERNAL: sem referencia, o conversor satura.
            cru = vref <= 0.01 ? (cru > 0 ? Perfil.MaxAdc : 0)
                : (int)Math.Round(Math.Clamp(cru * vcc / vref, 0, Perfil.MaxAdc));
        }
        int nativos = Perfil.BitsDoConversor;
        int pedidos = ResolucaoDoAdc > 0 ? ResolucaoDoAdc : Perfil.BitsDoAnalogRead;
        return pedidos >= nativos ? cru << (pedidos - nativos) : cru >> (nativos - pedidos);
    }

    /// <summary>Frequencia pedida por analogWriteFrequency, ou 0 para a da placa.</summary>
    public double FrequenciaDoPwmPedida { get; set; }

    /// <summary>
    /// Nivel que um componente externo IMPOE no pino (-1 = ninguem).
    /// Diferente de Pressionado, que e o modelo de botao e ja inverte
    /// sozinho no INPUT_PULLUP. Um sensor com saida digital nao e botao:
    /// ele decide o nivel, ponto.
    /// </summary>
    public readonly int[] NivelExterno = new int[NumPinos];

    /// <summary>Angulo comandado no servo ligado a este pino (-1 = nenhum).</summary>
    public readonly int[] AnguloServo = new int[NumPinos];

    /// <summary>
    /// O sinal periodico que uma peca de fora poe no pino (o canal de um
    /// receptor de radio, o PPM), ou nulo. Quem escreve e o editor; o nivel
    /// sai do relogio do chip, a cada leitura.
    /// </summary>
    public readonly SinalExterno[] SinaisExternos = new SinalExterno[NumPinos];

    /// <summary>Algum pino vigiado por interrupcao recebe um sinal de fora?</summary>
    public bool TemSinalVigiado
    {
        get
        {
            foreach (int pino in Interrupcoes.Keys)
                if (pino >= 0 && pino < NumPinos && SinaisExternos[pino] is not null) return true;
            return false;
        }
    }

    /// <summary>Ate onde as bordas de fora de cada pino vigiado ja foram atendidas (us).</summary>
    readonly double[] bordasAtendidasAte = new double[NumPinos];

    /// <summary>
    /// As interrupcoes que um sinal de fora disparou desde a ultima olhada,
    /// cada uma com o instante EXATO da borda, em ordem. Na placa a
    /// interrupcao corta o que o sketch estiver fazendo na hora da borda; o
    /// interpretador so olha entre um passo e outro (um Serial.print inteiro
    /// pode passar), entao quem atende roda a rotina com o relogio da borda --
    /// o micros() la dentro mede o pulso como na bancada, e duas bordas no
    /// mesmo intervalo viram duas chamadas.
    /// </summary>
    public List<(string Funcao, double Instante)> InterrupcoesExternas()
    {
        List<(string, double)> vencidas = null;
        double agora = AgoraUsLido;
        foreach (var (pino, alvo) in Interrupcoes)
        {
            if (pino < 0 || pino >= NumPinos || SinaisExternos[pino] is not { } sinal) continue;
            double desde = bordasAtendidasAte[pino];
            if (desde <= 0 || desde > agora) desde = agora;
            for (int k = 0; k < 1000; k++)
            {
                double borda = sinal.ProximaBorda(desde);
                if (borda > agora) break;
                int antes = sinal.Nivel(borda - 0.01), depois = sinal.Nivel(borda + 0.01);
                if (InterrupcoesLigadas && Vence(alvo.Borda, antes, depois)) (vencidas ??= []).Add((alvo.Funcao, borda));
                desde = borda;
            }
            bordasAtendidasAte[pino] = agora;
            nivelVisto[pino] = sinal.Nivel(agora);
        }
        vencidas?.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return vencidas ?? NenhumaExterna;
    }

    /// <summary>Devolvida quando nada venceu (so para leitura, como a NenhumaVencida).</summary>
    static readonly List<(string Funcao, double Instante)> NenhumaExterna = [];

    double? relogioForcado;
    double cursorNaInterrupcao;

    /// <summary>
    /// Enquanto a rotina de uma borda de fora roda: o relogio que o sketch le
    /// (micros, millis, digitalRead do sinal) parte do instante da borda e anda
    /// o que a rotina gastar.
    /// </summary>
    public void ForcarRelogio(double instanteUs)
    {
        relogioForcado = instanteUs;
        cursorNaInterrupcao = usPedidos;
    }

    public void SoltarRelogio() => relogioForcado = null;

    /// <summary>
    /// O relogio que o sketch le: o do chip, ou o da borda que a interrupcao
    /// esta atendendo mais o tempo de chip que a rotina pediu (os ciclos dela)
    /// -- e nao o relogio de parede, nem o cursor (que alcanca a parede quando
    /// o interpretador atrasa): os dois mediriam a lentidao do computador.
    /// </summary>
    public double TempoObservado()
        => relogioForcado is { } borda ? borda + Math.Max(0, usPedidos - cursorNaInterrupcao) : AgoraUs();

    double TempoObservadoLido
        => relogioForcado is { } borda ? borda + Math.Max(0, usPedidos - cursorNaInterrupcao) : AgoraUsLido;

    /// <summary>A proxima borda de um sinal de fora num pino vigiado, depois de <paramref name="desdeUs"/>.</summary>
    public double ProximaBordaVigiada(double desdeUs)
    {
        double menor = double.PositiveInfinity;
        foreach (int pino in Interrupcoes.Keys)
            if (pino >= 0 && pino < NumPinos && SinaisExternos[pino] is { } s)
                menor = Math.Min(menor, s.ProximaBorda(desdeUs));
        return menor;
    }

    /// <summary>
    /// Grandezas que os componentes expoem: distancia, temperatura,
    /// umidade, peso, aceleracao... A chave e "pino:grandeza", ou
    /// "tipo:grandeza" para os de barramento, que nao tem pino proprio.
    /// O editor escreve, as bibliotecas leem.
    /// </summary>
    public readonly Dictionary<string, double> Leituras = [];

    /// <summary>Fitas de LED enderecavel, por pino de dados.</summary>
    public readonly Dictionary<int, Fita> Fitas = [];

    /// <summary>Os mostradores TM1637 do sketch, pelos pinos CLK e DIO.</summary>
    public readonly Dictionary<(int Clk, int Dio), Tm1637> Mostradores = [];

    /// <summary>As matrizes do MAX7219 (a LedControl), pelos pinos DIN, CLK e CS.</summary>
    public readonly Dictionary<(int Din, int Clk, int Cs), Max7219> Matrizes = [];

    /// <summary>Telas de texto por barramento (SSD1306).</summary>
    public Lcd Oled { get; private set; }
    public Lcd CriarOled() => Oled ??= new Lcd(21, 8);

    /// <summary>
    /// O vidro do OLED: o que o ultimo display() mandou. E outro chip: o
    /// reset da placa nao apaga o desenho -- so cortar a energia.
    /// </summary>
    public TelaGrafica PainelOled
    {
        get { lock (PaineisOled) return PaineisOled.Count > 0 ? PaineisOled[0].Painel : null; }
    }

    /// <summary>
    /// Um vidro por display do sketch: dois OLEDs 0x3C, um no Wire e outro no
    /// Wire1 (ou 0x3C e 0x3D no mesmo fio). A bancada acha o de cada peca
    /// pelo fio dela.
    /// </summary>
    public readonly List<(int Barramento, int Endereco, bool Spi, TelaGrafica Painel)> PaineisOled = [];

    public TelaGrafica CriarPainelOled(int largura, int altura, int barramento = 0, int endereco = 0x3C, bool spi = false)
    {
        lock (PaineisOled)
        {
            int i = PaineisOled.FindIndex(p => p.Barramento == barramento && p.Endereco == endereco && p.Spi == spi);
            if (i >= 0 && PaineisOled[i].Painel is { } p && p.LarguraFisica == largura && p.AlturaFisica == altura) return p;
            var novo = new TelaGrafica(largura, altura) { Monocromatica = true };
            novo.Limpar(0);
            if (i >= 0) PaineisOled[i] = (barramento, endereco, spi, novo);
            else PaineisOled.Add((barramento, endereco, spi, novo));
            return novo;
        }
    }

    public double Leitura(string chave, double padrao = 0)
        => Leituras.TryGetValue(chave, out double v) ? v : padrao;

    /// <summary>
    /// A temperatura do proprio chip, em graus. So o RP2040 tem por onde
    /// le-la: o canal 4 do conversor e um sensor dentro do silicio.
    /// </summary>
    public double TemperaturaDoChip { get; set; } = 27;

    /// <summary>
    /// O que o conversor de 12 bits le no sensor de temperatura do RP2040.
    ///
    /// A conta e a da folha de dados, ao contrario: 0,706 V a 27 graus, e
    /// 1,721 mV a menos por grau. E a MESMA formula que o exemplo da
    /// Raspberry usa para voltar da leitura para a temperatura.
    /// </summary>
    public int LeituraDoSensorDeTemperatura
        => (int)Math.Round((0.706 - (TemperaturaDoChip - 27) * 0.001721) / 3.3 * 4095);

    /// <summary>Canal LEDC -> pino, do estilo ledcSetup/ledcAttachPin do core 2.x.</summary>
    public readonly Dictionary<int, int> CanalPwm = [];

    // ---------------- interrupcoes ----------------
    //
    // attachInterrupt era recusado com uma mensagem mandando trocar por
    // leitura dentro do loop(). Honesto, mas nao ajudava quem PRECISA de
    // interrupcao -- e o exercicio de contar pulso de botao pede isso.
    //
    // Aqui a interrupcao nao e assincrona de verdade: a Maquina anota o
    // nivel de cada pino vigiado e, nos pontos em que o interpretador
    // pergunta, diz quais mudaram do jeito que interessa. Basta para o
    // que o aluno ve: o sketch reage ao botao sem le-lo no loop.

    public const int Subindo = 1;    // RISING
    public const int Descendo = 2;   // FALLING
    public const int Mudando = 3;    // CHANGE

    /// <summary>Pino vigiado -> funcao do sketch e em que borda.</summary>
    public readonly Dictionary<int, (string Funcao, int Borda)> Interrupcoes = [];

    /// <summary>noInterrupts() segura a entrega ate interrupts().</summary>
    public bool InterrupcoesLigadas { get; set; } = true;

    readonly int[] nivelVisto = new int[NumPinos];

    public void Vigiar(int pino, string funcao, int borda)
    {
        if (!Valido(pino)) return;
        Interrupcoes[pino] = (funcao, borda);
        // Anota o nivel de agora: ligar a vigia nao pode disparar sozinho.
        nivelVisto[pino] = DigitalRead(pino);
        bordasAtendidasAte[pino] = AgoraUsLido;
    }

    public void PararDeVigiar(int pino) => Interrupcoes.Remove(pino);

    /// <summary>
    /// Quais interrupcoes venceram desde a ultima pergunta.
    ///
    /// Atualiza o nivel visto mesmo com as interrupcoes desligadas: o
    /// que passou enquanto estavam desligadas nao volta depois, que e o
    /// que o hardware faz com noInterrupts().
    /// </summary>
    /// <summary>
    /// Devolvida quando nada venceu. Compartilhada, e so para leitura:
    /// quem chama so olha Count e percorre.
    /// </summary>
    static readonly List<string> NenhumaVencida = [];

    public List<string> InterrupcoesPendentes()
    {
        // A lista so nasce quando alguma interrupcao vence. Esta funcao
        // passou a ser chamada a cada SUBEXPRESSAO quando ha interrupcao
        // registrada, e criar uma lista vazia em cada "a + b" encheria o
        // coletor de lixo por nada -- quase sempre nao ha borda nenhuma.
        List<string> vencidas = null;

        // Primeiro o que a propria placa escreveu: ja veio anotado borda
        // a borda, na ordem em que aconteceu.
        lock (filaDeBordas)
        {
            if (filaDeBordas.Count > 0)
            {
                vencidas = [.. filaDeBordas];
                filaDeBordas.Clear();
            }
        }

        // Depois o que vem de fora -- botao, sensor --, que nao passa por
        // DigitalWrite e so da para ver comparando o nivel. O que a placa
        // escreveu ja atualizou nivelVisto, entao nao conta duas vezes.
        foreach (var (pino, alvo) in Interrupcoes)
        {
            if (pino >= 0 && pino < NumPinos && SinaisExternos[pino] is not null) continue;
            int agora = DigitalRead(pino);
            int antes = nivelVisto[pino];
            if (agora == antes) continue;

            nivelVisto[pino] = agora;
            if (!InterrupcoesLigadas) continue;

            if (Vence(alvo.Borda, antes, agora)) (vencidas ??= []).Add(alvo.Funcao);
        }

        return vencidas ?? NenhumaVencida;
    }

    /// <summary>Rotinas cujas bordas a placa escreveu e ainda nao rodaram.</summary>
    readonly List<string> filaDeBordas = [];

    /// <summary>
    /// Mais de mil bordas esperando e sinal de rotina que se redispara
    /// sozinha. O teto so impede a memoria de crescer sem fim; quem
    /// avisa do laco preso e o contador de passos.
    /// </summary>
    const int TetoDaFila = 1024;

    void AnotarBorda(int pino, int antes, int agora)
    {
        if (antes == agora || !Interrupcoes.TryGetValue(pino, out var alvo)) return;

        // A escrita ja foi vista: a comparacao de niveis nao pode contar
        // esta mesma borda de novo depois.
        nivelVisto[pino] = agora;

        // Borda com interrupcoes desligadas nao volta depois -- a mesma
        // regra do caminho de fora.
        if (!InterrupcoesLigadas || !Vence(alvo.Borda, antes, agora)) return;

        lock (filaDeBordas)
            if (filaDeBordas.Count < TetoDaFila) filaDeBordas.Add(alvo.Funcao);
    }

    /// <summary>
    /// O editor chama a cada quadro, depois de mexer nas entradas (o botao, a
    /// chave): a borda de um pino vigiado entra na fila NA HORA em que mudou.
    /// Sem isto, com o sketch dormindo (delay, time.sleep) a placa so
    /// comparava o nivel ao acordar -- e soltar e apertar de novo no mesmo
    /// sono sumia com a borda. Na placa a interrupcao de hardware pega cada uma.
    /// </summary>
    public void NotarEntradasVigiadas()
    {
        if (Interrupcoes.Count == 0) return;
        // A lista muda na linha do sketch (o attachInterrupt, o irq): uma copia,
        // e se ela mudou no meio da copia, fica para o proximo quadro.
        KeyValuePair<int, (string Funcao, int Borda)>[] vigiados;
        try { vigiados = [.. Interrupcoes]; }
        catch (InvalidOperationException) { return; }
        catch (ArgumentException) { return; }
        lock (filaDeBordas)
            foreach (var (pino, alvo) in vigiados)
            {
                if (pino < 0 || pino >= NumPinos || SinaisExternos[pino] is not null) continue;
                int agora = DigitalRead(pino), antes = nivelVisto[pino];
                if (agora == antes) continue;
                nivelVisto[pino] = agora;
                if (InterrupcoesLigadas && Vence(alvo.Borda, antes, agora) && filaDeBordas.Count < TetoDaFila)
                    filaDeBordas.Add(alvo.Funcao);
            }
    }

    static bool Vence(int borda, int antes, int agora) => borda switch
    {
        Subindo => antes == 0 && agora == 1,
        Descendo => antes == 1 && agora == 0,
        _ => antes != agora,
    };

    public Maquina() => LimparNiveis();

    public Lcd Lcd { get; private set; }
    public Lcd CriarLcd() => Lcd ??= new Lcd();

    /// <summary>
    /// O barramento I2C: o "Wire" do sketch.
    ///
    /// Fica na maquina, e nao no interpretador, porque quem pendura os
    /// aparelhos e o circuito -- a bancada registra um endereco para cada
    /// peca de I2C que esta na tela, e ai um scanner escrito pelo aluno
    /// encontra exatamente o que ele desenhou.
    /// </summary>
    public readonly BarramentoI2c I2c = new();

    /// <summary>
    /// O segundo controlador: o Wire1 da ESP32 e da Pico, o TwoWire(1), o
    /// "TwoWire Wire2(PB11, PB10)" da STM32. As mesmas pecas do circuito,
    /// cada uma respondendo so no barramento em que o fio dela chega.
    /// </summary>
    public BarramentoI2c I2c1 => i2c1 ??= I2c.Irmao(1);
    BarramentoI2c i2c1;

    readonly List<BarramentoI2c> i2cPorSoftware = [];

    /// <summary>O SoftI2C do MicroPython (e o I2C do ESP8266): um barramento por par de pinos, numerado de 100 em diante.</summary>
    public BarramentoI2c I2cPorSoftware(int sda, int scl)
    {
        lock (i2cPorSoftware)
        {
            var achado = i2cPorSoftware.FirstOrDefault(b => b.Sda == sda && b.Scl == scl);
            if (achado is not null) return achado;
            var novo = I2c.Irmao(100 + i2cPorSoftware.Count);
            novo.Pinos(sda, scl);
            i2cPorSoftware.Add(novo);
            return novo;
        }
    }

    /// <summary>O barramento pelo numero: 0 (Wire), 1 (Wire1) ou um por software.</summary>
    public BarramentoI2c I2cNumero(int numero)
    {
        if (numero <= 0) return I2c;
        if (numero == 1) return I2c1;
        lock (i2cPorSoftware) return i2cPorSoftware.FirstOrDefault(b => b.Numero == numero);
    }

    /// <summary>
    /// A bancada pendurou as pecas de I2C do circuito: so elas respondem,
    /// cada uma no seu fio. Sem circuito (o teste de motor), o objeto que o
    /// sketch declara responde sozinho.
    /// </summary>
    public bool I2cPeloCircuito { get; set; }

    /// <summary>
    /// O cartao SD. Os arquivos vivem so enquanto a simulacao roda:
    /// reiniciar a placa esvazia o cartao, e o sketch e avisado disso na
    /// primeira vez que o abre.
    /// </summary>
    public readonly CartaoSd Cartao = new();

    /// <summary>O modulo de radio NRF24L01 esta ligado nesta placa?</summary>
    public bool RadioPresente { get; set; }

    /// <summary>
    /// A tela do TFT. Uma so: dois displays no mesmo circuito dividiriam
    /// o barramento SPI, e distinguir um do outro pediria simular o CS.
    /// </summary>
    public TelaGrafica Tela { get; private set; }

    /// <summary>
    /// A tela grafica da placa. O tamanho e do primeiro display que
    /// pedir: 240x320 no ILI9341, 240x240 no ST7789.
    /// </summary>
    public TelaGrafica CriarTela(int largura = 240, int altura = 320)
        => Tela ??= new TelaGrafica(largura, altura);

    /// <summary>Outro tamanho de painel (o initR() do ST7735 que escolhe o de 1,44").</summary>
    public TelaGrafica TrocarTela(int largura, int altura) => Tela = new TelaGrafica(largura, altura);

    public readonly List<EventoPino> Eventos = [];

    /// <summary>
    /// Quantos eventos ja sairam da frente da lista (o corte dos 400 mil, o
    /// reset): quem le a lista aos poucos -- o motor de passo do editor --
    /// sabe onde parou mesmo depois do corte.
    /// </summary>
    public long EventosAntesDaLista { get; private set; }
    public readonly List<string> Serial = [];

    /// <summary>O que foi digitado no monitor serial e o sketch ainda nao leu.</summary>
    public readonly FluxoDeEntrada EntradaSerial = new();

    string nome;

    /// <summary>Como a placa aparece no painel Rede.</summary>
    public string Nome { get => nome ?? Perfil?.Nome ?? "placa"; set => nome = value; }

    Rede rede;

    /// <summary>
    /// A rede da bancada. Sem ninguem ter posto uma (teste, linha de
    /// comando), a placa ganha uma so dela e SEM internet: nada sai deste
    /// computador sem a janela ter dito que pode.
    /// </summary>
    public Rede Rede
    {
        get => rede ??= new Rede { UsarInternet = false };
        set => rede = value;
    }
    public readonly List<string> Avisos = [];
    public readonly object Trava = new();

    // ---------------- modo nativo (os .c do projeto) ----------------

    /// <summary>O codigo C que esta rodando nesta placa, ou null.</summary>
    public ModuloNativo Nativo { get; set; }

    /// <summary>
    /// Os aparelhos do barramento SPI, do jeito que o circuito os liga.
    /// Quem conhece o circuito (a janela) responde; sem ele, nenhum aparelho.
    /// </summary>
    public Func<IReadOnlyList<AparelhoNoSpi>> AparelhosSpi { get; set; }

    /// <summary>O som do I2S, para a janela tocar quando ha alto-falante ligado.</summary>
    public volatile SaidaDeAudio Audio;

    /// <summary>A NVS da placa (espaco + chave): sobrevive ao reinicio, como na flash.</summary>
    public readonly Dictionary<string, (int Tipo, byte[] Dados)> Nvs = [];

    byte[] eeprom;
    int[] escritasNaEeprom;
    int commitsDaEeprom;

    /// <summary>
    /// A EEPROM de verdade do chip (AVR, STM32): nasce apagada (0xFF) e nao
    /// esquece nada -- nem no reset, nem cortando a energia, nem entre uma
    /// rodada e outra, como na placa.
    /// </summary>
    public byte[] EepromDoChip(int tamanho)
    {
        if (tamanho <= 0) return null;
        lock (Trava)
            if (eeprom is null || eeprom.Length != tamanho)
            {
                eeprom = new byte[tamanho];
                Array.Fill(eeprom, (byte)0xFF);
                escritasNaEeprom = new int[tamanho];
            }
        return eeprom;
    }

    /// <summary>Mais uma escrita na celula: devolve quantas ela ja levou (aguenta ~100 mil).</summary>
    public int ContarEscritaNaEeprom(int celula)
        => escritasNaEeprom is { } e && celula >= 0 && celula < e.Length ? ++e[celula] : 0;

    public int ContarCommitDaEeprom() => ++commitsDaEeprom;

    byte[] memoriaRtcDoUsuario;

    /// <summary>
    /// Os 512 bytes de memoria RTC do usuario do ESP8266: sobrevivem ao
    /// deepSleep e ao reset, e nascem com lixo quando a placa liga (por isso
    /// os exemplos conferem um CRC).
    /// </summary>
    public byte[] MemoriaRtcDoUsuario()
    {
        lock (Trava)
            if (memoriaRtcDoUsuario is null)
            {
                memoriaRtcDoUsuario = new byte[512];
                new Random(Environment.TickCount).NextBytes(memoriaRtcDoUsuario);
            }
        return memoriaRtcDoUsuario;
    }

    /// <summary>
    /// O pendrive na USB host da placa (a S3 nos GPIO 19 e 20): um segundo
    /// "cartao", montado pelo msc_host_vfs_register do codigo nativo.
    /// </summary>
    public readonly CartaoSd Pendrive = new();

    /// <summary>Ha um pendrive ligado (e conectado) nos D-/D+ desta placa? Quem sabe e a bancada.</summary>
    public Func<bool> PendriveConectado { get; set; }

    /// <summary>O sistema de arquivos da flash (SPIFFS, LittleFS, FAT): tambem sobrevive ao reinicio.</summary>
    public readonly CartaoSd SistemaDaFlash = new() { Presente = true };

    byte[] flash;

    /// <summary>A flash inteira da placa, apagada (0xFF) ate alguem gravar.</summary>
    public byte[] MemoriaFlash()
    {
        if (flash is null)
            lock (Trava)
                if (flash is null)
                {
                    var f = new byte[(int)Math.Min(Perfil.FlashTotalBytes, 64L * 1024 * 1024)];
                    Array.Fill(f, (byte)0xFF);
                    flash = f;
                }
        return flash;
    }

    /// <summary>
    /// Um reinicio pedido de fora do interpretador: o codigo nativo entrou em
    /// panico ou chamou esp_restart(). O interpretador o lanca no lugar da
    /// parada comum, e quem roda o sketch monta a rodada nova.
    /// </summary>
    public volatile ReinicioDaPlaca ReinicioPedido;

    public void PedirReinicio(ReinicioDaPlaca motivo)
    {
        ReinicioPedido ??= motivo;
        Parar = true;
    }

    /// <summary>A excecao que a parada vira: o reinicio pedido, ou a parada comum.</summary>
    public ExecucaoInterrompida MotivoDaParada() => ReinicioPedido ?? new ExecucaoInterrompida();

    /// <summary>Desliga o codigo nativo desta placa, se houver.</summary>
    public void EncerrarNativo()
    {
        var n = Nativo;
        Nativo = null;
        n?.Encerrar();
        Audio = null;
    }

    /// <summary>Pedido de parada vindo da interface.</summary>
    public volatile bool Parar;

    // ---------------- relogio ----------------
    /// <summary>
    /// Virtual: delay() adianta o relogio, execucao instantanea e
    /// deterministica (testes). Real: delay() dorme de verdade (app).
    /// </summary>
    public bool TempoReal { get; set; }

    readonly Stopwatch cronometro = new();
    long msVirtual;

    public long Ms => TempoReal ? cronometro.ElapsedMilliseconds : msVirtual;

    public void IniciarRelogio()
    {
        msVirtual = 0;
        microsSobrando = 0;
        cursorUs = 0;
        fracaoUs = 0;
        fimDaTransmissaoUs = 0;
        UsSemCeder = 0;
        usPedidos = 0;
        inicioDaJanelaUs = -1;
        pedidosNoInicio = 0;
        cronometro.Restart();
    }

    /// <summary>
    /// O tempo do chip, em microssegundos.
    ///
    /// E o relogio -- ou o que o chip ja gastou alem dele. Um digitalWrite
    /// custa 3,6 us num Uno e o interpretador o faz mais rapido que isso:
    /// sem o cursor, as vinte e quatro escritas de um shiftOut cairiam
    /// todas no mesmo instante, e o analisador mostraria o byte inteiro
    /// como um risco so. Com ele, cada escrita fica depois da outra, no
    /// ritmo do chip; num delay o relogio alcanca o cursor de novo.
    /// </summary>
    public double AgoraUs()
    {
        double real = MsPreciso * 1000;
        if (real > cursorUs) cursorUs = real;
        return cursorUs;
    }

    double cursorUs;

    /// <summary>
    /// O tempo do chip, so para ler -- de outra thread, como a da tela, que
    /// nao pode mexer no cursor da placa.
    /// </summary>
    public double AgoraUsLido => Math.Max(MsPreciso * 1000, cursorUs);

    /// <summary>O instante de uma operacao no chip -- e ela ja cobrada no cursor.</summary>
    double Gastar(double custoUs)
    {
        double t = AgoraUs();
        cursorUs = t + custoUs;
        usPedidos += custoUs;
        return t;
    }

    // ---------------- o processador ----------------

    /// <summary>
    /// Quanto o chip pode andar na frente do relogio de parede antes de a
    /// thread esperar: pouco para o olho ver, e o bastante para o Sleep do
    /// Windows (1 ms) dar conta.
    /// </summary>
    const double FolgaDoChipUs = 3000;

    /// <summary>O chip que a placa andou pedindo (conta, espera): e o que mede a velocidade.</summary>
    double usPedidos;

    /// <summary>
    /// Microssegundos de chip desde a ultima vez que o sketch cedeu o
    /// processador (delay, yield, a volta do loop). E o que os watchdogs
    /// olham: o do ESP8266 reinicia a placa depois de ~3,2 s sem ceder.
    /// </summary>
    public double UsSemCeder { get; private set; }

    /// <summary>O sketch cedeu o processador: delay(), yield(), o fim de uma volta do loop.</summary>
    public void Cedeu() => UsSemCeder = 0;

    /// <summary>
    /// O processador fez conta durante tantos microssegundos -- o custo, em
    /// ciclos do chip, que o interpretador somou.
    ///
    /// No tempo virtual o relogio anda. No real, o cursor do chip anda; se
    /// ele passou do relogio de parede, a thread espera ele chegar. E isso
    /// que faz o sketch andar na velocidade da placa, e nao na do computador:
    /// o Uno achando primos leva o minuto que leva na bancada. Quando o
    /// computador e que nao da conta (a ESP32 faz 240 milhoes de ciclos por
    /// segundo), o chip anda o que der, e a velocidade medida cai.
    /// </summary>
    public void GastarProcessador(double us)
    {
        if (us <= 0) return;
        UsSemCeder += us;
        usPedidos += us;
        if (!TempoReal)
        {
            AvancarMicrosFracao(us);
            return;
        }

        cursorUs = AgoraUs() + us;
        Acompanhar();
    }

    /// <summary>
    /// Uma espera do chip (a fila da serial cheia, um barramento lento): o
    /// relogio do chip anda e a thread espera a parede alcancar.
    /// </summary>
    public void EsperarNoChip(double us)
    {
        if (us <= 0) return;
        usPedidos += us;
        if (!TempoReal)
        {
            AvancarMicrosFracao(us);
            return;
        }
        cursorUs = AgoraUs() + us;
        Acompanhar();
    }

    /// <summary>Se o chip passou da parede, espera; e mede a velocidade de meio em meio segundo.</summary>
    void Acompanhar()
    {
        double parede = cronometro.Elapsed.TotalMilliseconds * 1000;
        if (cursorUs - parede > FolgaDoChipUs) EsperarAte((cursorUs - FolgaDoChipUs / 2) / 1000.0);

        if (inicioDaJanelaUs < 0)
        {
            inicioDaJanelaUs = parede;
            pedidosNoInicio = usPedidos;
            return;
        }
        double janela = parede - inicioDaJanelaUs;
        if (janela < 500_000) return;
        VelocidadeDoChip = Math.Min(1.0, (usPedidos - pedidosNoInicio) / janela);
        MsDeChipRodando += janela / 1000;
        inicioDaJanelaUs = parede;
        pedidosNoInicio = usPedidos;
    }

    double inicioDaJanelaUs = -1, pedidosNoInicio;

    /// <summary>O relogio virtual anda com fracao de microssegundo (a conta de poucos ciclos).</summary>
    void AvancarMicrosFracao(double us)
    {
        fracaoUs += us;
        if (fracaoUs < 1) return;
        long inteiros = (long)fracaoUs;
        fracaoUs -= inteiros;
        AvancarMicros(inteiros);
    }

    double fracaoUs;

    // ---------------- a serial saindo pelo fio ----------------

    /// <summary>Quando a UART acaba de mandar o que esta na fila, no relogio do chip.</summary>
    double fimDaTransmissaoUs;

    /// <summary>Quando a UART acaba de mandar o que esta na fila (us): o LED TX da placa pisca por aqui.</summary>
    public double FimDaTransmissaoUs => fimDaTransmissaoUs;

    /// <summary>
    /// A fila de saida do Serial: 64 bytes no AVR e na STM32, os 128 da FIFO
    /// da UART na ESP32 e no ESP8266 (o core 3.x nao poe fila em RAM na
    /// frente dela). Zero: o Serial e a USB do proprio chip (RP2040), que nao
    /// tem baud -- sai na hora.
    /// </summary>
    int FilaDaSerial => Perfil.Processador switch
    {
        "RP2040" => 0,
        "ESP8266" or "Xtensa LX6" or "Xtensa LX7" or "RISC-V" => 128,
        _ => 64,
    };

    /// <summary>
    /// Os bytes do Serial.print saem pela UART no ritmo do baud (10 bits por
    /// byte). Enquanto cabem na fila, o print volta na hora; com a fila
    /// cheia, ele espera -- a 9600 baud, 1 ms por caractere. E por isso que
    /// um loop que imprime muito anda devagar na placa.
    /// </summary>
    public void TransmitirSerial(int bytes)
    {
        if (bytes <= 0 || BaudDaSerial <= 0 || FilaDaSerial == 0) return;
        double porByte = 10_000_000.0 / BaudDaSerial;
        double agora = AgoraUs();
        if (fimDaTransmissaoUs < agora) fimDaTransmissaoUs = agora;
        double naFila = (fimDaTransmissaoUs - agora) / porByte;
        double excesso = naFila + bytes - FilaDaSerial;
        fimDaTransmissaoUs += bytes * porByte;
        if (excesso > 0) EsperarNoChip(excesso * porByte);
    }

    /// <summary>Serial.flush(): espera a fila esvaziar.</summary>
    public void EsvaziarSerial()
    {
        if (BaudDaSerial <= 0) return;
        EsperarNoChip(fimDaTransmissaoUs - AgoraUs());
    }

    /// <summary>
    /// delayMicroseconds: espera de verdade, em microssegundos.
    ///
    /// Antes virava milissegundos inteiros, e delayMicroseconds(500) nao
    /// esperava NADA -- todo protocolo feito na mao (sensor de um fio,
    /// serial por software, o pulso do ultrassom) perdia o tempo inteiro.
    /// Abaixo de dois milissegundos a espera e girando: o Sleep do Windows
    /// acorda com atraso de varios milissegundos, e isso nao e espera de
    /// microssegundo nenhum.
    /// </summary>
    public void EsperarMicros(double us)
    {
        if (us <= 0) return;

        double alvo = AgoraUs() + us;
        cursorUs = alvo;
        usPedidos += us;

        // No chip o delayMicroseconds e um laco que gira: nao cede o
        // processador, e o watchdog do ESP8266 conta esse tempo.
        UsSemCeder += us;

        if (!TempoReal)
        {
            AvancarMicros((long)Math.Round(us));
            return;
        }

        // Dorme ate perto e gira o resto: o Sleep do Windows passa do ponto
        // em ate 1,5 ms, e esse atraso entrava no micros() do sketch (o
        // motor de passo a 2,9 ms por passo andava 10% mais devagar).
        if (us > 2000) EsperarAte(alvo / 1000.0);
        while (!Parar && MsPreciso * 1000 < alvo) Thread.SpinWait(40);
    }

    /// <summary>
    /// O depurador pediu pausa. O delay olha isto enquanto dorme, para a
    /// pausa não esperar o delay inteiro terminar.
    /// </summary>
    public volatile bool PausaPedida;

    /// <summary>
    /// Quem atende a pausa pedida no meio de um delay. É o depurador que
    /// se pendura aqui; sem depurador, fica nulo e nada muda.
    /// </summary>
    public Action PontoDePausa { get; set; }

    /// <summary>
    /// Para o tempo real durante a pausa. Sem isso, `millis()` veria o
    /// tempo parado passar, e um sketch que mede intervalo "atrasaria"
    /// tudo ao continuar. No tempo virtual não há o que congelar: ele só
    /// anda quando o sketch manda.
    /// </summary>
    public void CongelarRelogio()
    {
        if (TempoReal) cronometro.Stop();
    }

    public void DescongelarRelogio()
    {
        if (TempoReal && !cronometro.IsRunning) cronometro.Start();
    }

    /// <summary>
    /// Anda com o relogio em MICROSSEGUNDOS, sem dormir.
    ///
    /// E para o Assembly: uma instrucao de AVR a 16 MHz custa 62,5 ns,
    /// entao mil delas valem 62 microssegundos. Dormir de verdade a
    /// cada instrucao deixaria o laco de espera mais lento do que o
    /// hardware, que e o oposto do que se quer.
    /// </summary>
    public void AvancarMicros(long micros)
    {
        if (micros <= 0 || TempoReal) return;

        microsSobrando += micros;
        msVirtual += microsSobrando / 1000;
        microsSobrando %= 1000;
        ConferirLimiteVirtual();
    }

    /// <summary>
    /// Ate onde o relogio virtual pode ir (0 = sem limite). Passou, a placa
    /// para -- no meio da volta, se for o caso.
    ///
    /// E para o diagnostico e os testes: um sketch que espera o botao ser
    /// solto ("while (apertado) delay(10);") nunca sai da volta quando
    /// ninguem solta, e o relogio virtual andaria para sempre.
    /// </summary>
    public long LimiteDoRelogioVirtual { get; set; }

    void ConferirLimiteVirtual()
    {
        if (LimiteDoRelogioVirtual > 0 && msVirtual >= LimiteDoRelogioVirtual) Parar = true;
    }

    long microsSobrando;

    /// <summary>
    /// O relógio em milissegundos, com fração.
    ///
    /// O <see cref="Ms"/> inteiro basta para o millis() do sketch, mas
    /// não para marcar o ritmo de um chip a 16 MHz, em que um
    /// milissegundo são dezesseis mil ciclos.
    /// </summary>
    public double MsPreciso => TempoReal
        ? cronometro.Elapsed.TotalMilliseconds
        : msVirtual + microsSobrando / 1000.0;

    /// <summary>
    /// Espera até o relógio chegar em <paramref name="ms"/>, em tempo real.
    ///
    /// É o que faz o Assembly andar na velocidade do chip, e não na do
    /// computador. O alvo é ABSOLUTO -- o instante desde o reset --, e
    /// não "espere mais tanto": o tempo que o próprio computador gastou
    /// executando as instruções já conta. Uma espera relativa somaria os
    /// dois e o chip simulado andaria mais devagar que o de verdade.
    ///
    /// O Windows acorda uma thread com atraso de até uns 15 ms. Como o
    /// alvo é absoluto, o atraso não acumula: a leva seguinte encontra o
    /// relógio adiantado, não dorme e alcança. O tempo médio sai certo, e
    /// a oscilação fica abaixo do que o olho percebe num LED.
    ///
    /// No tempo virtual dos testes não há o que esperar: quem anda com o
    /// relógio lá é o <see cref="AvancarMicros"/>.
    /// </summary>
    /// <summary>
    /// Quanto do tempo real o chip em Assembly está conseguindo andar.
    ///
    /// 1,0 é a velocidade da placa. Abaixo disso o computador não está
    /// acompanhando: a lógica e a contagem de ciclos continuam exatas,
    /// mas a TELA anda mais devagar que a placa -- e a pessoa precisa
    /// saber disso para não calibrar um tempo olhando para ela.
    /// </summary>
    public double VelocidadeDoChip { get; set; } = 1.0;

    /// <summary>Há quantos milissegundos de relógio real o chip está rodando.</summary>
    public double MsDeChipRodando { get; set; }

    /// <summary>
    /// O relogio do Windows em 1 ms, uma vez por processo: sem isso o
    /// Sleep(1) dorme ~15 ms, e o chip que espera a parede anda aos trancos.
    /// </summary>
    static int relogioFino;

    static void AfinarRelogioDoWindows()
    {
        if (Interlocked.Exchange(ref relogioFino, 1) != 0 || !OperatingSystem.IsWindows()) return;
        try { Win32.timeBeginPeriod(1); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }

    public void EsperarAte(double ms)
    {
        if (!TempoReal) return;
        AfinarRelogioDoWindows();

        while (true)
        {
            if (Parar) return;
            if (PausaPedida) PontoDePausa?.Invoke();

            // O Sleep(1) do Windows leva de 1 a 2 ms: o ultimo 1,6 ms fica
            // para quem chamou (o delayMicroseconds gira ate o alvo).
            double falta = ms - cronometro.Elapsed.TotalMilliseconds;
            if (falta <= 1.6) return;

            Thread.Sleep((int)Math.Clamp(falta - 1.5, 1, 20));
        }
    }

    public void Avancar(long ms)
    {
        if (ms <= 0) return;
        usPedidos += ms * 1000.0;
        if (!TempoReal) { msVirtual += ms; AtualizarTons(); ConferirLimiteVirtual(); return; }

        // O fim da espera e no relogio do CHIP: se ele esta um pouco na
        // frente da parede (a conta andou mais rapido que o computador
        // precisava), o delay(10) acaba 10 ms depois de onde o chip esta.
        double fimUs = AgoraUs() + ms * 1000.0;
        cursorUs = fimUs;

        // Em fatias, e não um Sleep inteiro. Dois motivos:
        //
        // - pausa pedida no meio de um delay(5000) é atendida na hora, e
        //   não cinco segundos depois;
        // - parar a simulação no meio de um delay longo solta a thread já,
        //   em vez de a janela esperar o delay acabar.
        //
        // A conta é pelo cronômetro, que fica parado durante a pausa: o
        // delay continua de onde parou, e não "acaba" durante a pausa.
        while (true)
        {
            if (Parar) return;
            if (PausaPedida) PontoDePausa?.Invoke();

            double falta = fimUs / 1000.0 - cronometro.Elapsed.TotalMilliseconds;
            if (falta <= 0) { AtualizarTons(); return; }
            Thread.Sleep((int)Math.Clamp(Math.Ceiling(falta), 1, 20));
        }
    }

    // ---------------- pinos ----------------
    public static bool SoEntrada(int p) => p is >= 34 and <= 39;
    public static bool LigadoAFlash(int p) => p is >= 6 and <= 11;

    // Os avisos abaixo so valem no ESP32: num Arduino o pino 7 nao tem
    // nada de especial, e avisar seria mentira.
    bool AvisoSoEntrada(int p) => Perfil.PinoSoDeEntrada(p);
    bool AvisoFlash(int p) => Perfil.AvisosDoEsp32 && LigadoAFlash(p);

    public void PinMode(int p, int modo)
    {
        if (!Valido(p)) return;
        if (Perfil.SoAnalogico(p))
        {
            if (modo != 0) Avisar($"o pino {p} da {Perfil.Nome} e so analogico: nao tem saida nem pull-up (use analogRead).");
            Modo[p] = 0;
            return;
        }
        if (modo == 1 && AvisoSoEntrada(p))
            Avisar($"GPIO {p} e somente entrada na {Perfil.Nome}: nao pode ser OUTPUT.");
        if (modo == 2 && AvisoSoEntrada(p))
            Avisar($"GPIO {p} nao tem pull-up interno na {Perfil.Nome}.");
        if (AvisoFlash(p))
            Avisar($"GPIO {p} e usado pela flash interna: evite.");
        Modo[p] = modo;
    }

    public void DigitalWrite(int p, int v)
    {
        if (!Valido(p)) return;
        if (Perfil.SoAnalogico(p)) { Avisar($"digitalWrite no pino {p} da {Perfil.Nome}, que e so analogico: nada acontece."); return; }
        if (AvisoSoEntrada(p))
        { Avisar($"digitalWrite em GPIO {p}, que e somente entrada."); return; }
        v = v != 0 ? 1 : 0;
        double quando = Gastar(Perfil.CustoDoPinoUs);

        // Com PWM ligado o pino nao esta "no mesmo nivel": o digitalWrite
        // desliga o PWM (e o turnOffPWM do core), e isso e uma mudanca.
        bool comPwm = Pwm[p] is > 0 and < 255;
        if (Digital[p] == v && !comPwm) return;
        MudarPino(p, v, quando);
    }

    /// <summary>
    /// Um periferico do chip (o SPI) mexe no pino, tantos microssegundos de
    /// barramento depois da borda anterior: sem o custo de um digitalWrite,
    /// que e o processador. E o que poe o byte do SPI.transfer() nos pinos,
    /// no ritmo do relogio do barramento.
    /// </summary>
    public void PinoDoPeriferico(int p, int v, double depoisDeUs)
    {
        if (!Valido(p)) return;
        v = v != 0 ? 1 : 0;
        double quando = Gastar(depoisDeUs);
        if (Digital[p] == v) return;
        MudarPino(p, v, quando);
    }

    void MudarPino(int p, int v, double quando)
    {
        // Pino vigiado: a borda e anotada AQUI, na escrita, e nao
        // descoberta depois comparando niveis. Comparar niveis perdia
        // tudo que sobe e desce dentro de uma chamada so -- um shiftOut
        // no pino vigiado da oito pulsos e terminava no mesmo nivel em
        // que comecou, entao a vigia nao via nenhum.
        bool vigiado = Interrupcoes.ContainsKey(p);
        int antes = vigiado ? DigitalRead(p) : 0;

        Digital[p] = v;
        Pwm[p] = v * 255;
        lock (Trava)
        {
            // O codigo nativo mexe no D/C do display milhares de vezes por
            // segundo: o registro guarda so o recente, para nao crescer sem fim.
            if (Eventos.Count >= 400_000)
            {
                Eventos.RemoveRange(0, 200_000);
                EventosAntesDaLista += 200_000;
            }
            Eventos.Add(new EventoPino(Ms, p, v));
        }
        Linha.Borda(quando, p, v);

        if (vigiado) AnotarBorda(p, antes, DigitalRead(p));
    }

    public int DigitalRead(int p)
    {
        if (!Valido(p)) return 0;
        if (Perfil.SoAnalogico(p)) return 0;      // o core le 0: o pino nao tem porta digital

        // Quem impoe nivel manda em tudo, inclusive no pull-up.
        if (NivelExterno[p] >= 0) return NivelExterno[p];
        // Um sinal de fora (o receptor de radio): o nivel deste instante.
        if (SinaisExternos[p] is { } sinal) return sinal.Nivel(TempoObservadoLido);
        // Botao com INPUT_PULLUP: solto = HIGH, pressionado = LOW. Os GPIO 34
        // a 39 da ESP32 nao tem o pull-up: o INPUT_PULLUP neles e um INPUT.
        if (Modo[p] == 2 && !AvisoSoEntrada(p)) return Pressionado[p] ? 0 : 1;
        if (Modo[p] == 2) return Pressionado[p] ? 0 : Digital[p];
        if (Pressionado[p]) return 1;
        return Digital[p];
    }

    public int AnalogRead(int p)
    {
        if (!Valido(p)) return 0;
        int cru = Analogico[p];
        // A Pico com uma referencia de fora no ADC_VREF: a mesma tensao vira
        // outro numero (3,0 V de referencia: 1,5 V ja e meia escala). No GND,
        // sem referencia, o conversor satura.
        if (Perfil.AdcVrefNoPino && TensaoNoAref is { } vref)
        {
            double vcc = Perfil.TensaoDeOperacao > 0 ? Perfil.TensaoDeOperacao : 3.3;
            cru = vref <= 0.01 ? (cru > 0 ? Perfil.MaxAdc : 0)
                : (int)Math.Round(Math.Clamp(cru * vcc / vref, 0, Perfil.MaxAdc));
        }
        return cru;
    }

    /// <summary>
    /// O botao BOOTSEL da Pico apertado agora: o "BOOTSEL" do core da Pico
    /// para Arduino, o rp2040.getBootselButton() e o rp2.bootsel_button() do
    /// MicroPython leem ele (o editor escreve a cada quadro).
    /// </summary>
    public bool BootselApertado { get; set; }

    /// <summary>
    /// PWM de 0 a 255. <paramref name="hz"/> e a frequencia pedida (ledc,
    /// analogWriteFrequency); sem ela, vale a do core da placa naquele pino.
    /// </summary>
    public void AnalogWrite(int p, int duty, double hz = 0)
    {
        if (!Valido(p)) return;
        // Pino sem PWM (o 2 do Uno): o analogWrite do core so liga ou desliga.
        if (hz <= 0 && !Perfil.TemPwm(p))
        {
            DigitalWrite(p, duty < 128 ? 0 : 1);
            return;
        }
        Pwm[p] = Math.Clamp(duty, 0, 255);
        Digital[p] = Pwm[p] > 127 ? 1 : 0;

        double f = hz > 0 ? hz
                 : FrequenciaDoPwmPedida > 0 ? FrequenciaDoPwmPedida
                 : Perfil.FrequenciaDoPwm(p);
        Linha.Periodico(Gastar(Perfil.CustoDoPinoUs), p, f, Pwm[p] / 255.0, continuarFase: true);
    }

    /// <summary>analogWrite com o valor na resolucao do analogWriteResolution.</summary>
    public void AnalogWriteNaResolucao(int p, double valor)
    {
        int maximo = MaximoDoPwm > 0 ? MaximoDoPwm : (1 << Math.Clamp(ResolucaoDoPwm, 1, 16)) - 1;
        AnalogWrite(p, (int)Math.Round(Math.Clamp(valor, 0, maximo) * 255.0 / maximo));
    }

    /// <summary>
    /// tone(pino, f) ou tone(pino, f, ms). Com a duracao ele para sozinho,
    /// como na placa -- antes o som nunca acabava.
    /// </summary>
    public void Tone(int p, int hz, long duracaoMs = 0)
    {
        if (!Valido(p)) return;
        Frequencia[p] = Math.Max(hz, 0);
        fimDoTom[p] = duracaoMs > 0 ? Ms + duracaoMs : long.MaxValue;
        Linha.Periodico(Gastar(Perfil.CustoDoPinoUs), p, hz, 0.5,
                        duracaoMs > 0 ? duracaoMs * 1000.0 : double.PositiveInfinity);
    }

    public void NoTone(int p)
    {
        if (!Valido(p)) return;
        Frequencia[p] = 0;
        fimDoTom[p] = 0;
        Linha.Parar(AgoraUs(), p);
    }

    /// <summary>Apaga os tons com duracao que ja acabaram.</summary>
    public void AtualizarTons()
    {
        long agora = Ms;
        for (int p = 0; p < NumPinos; p++)
            if (Frequencia[p] > 0 && fimDoTom[p] <= agora) Frequencia[p] = 0;
    }

    /// <summary>O que o Serial.print mandou, byte a byte, pelo pino TX.</summary>
    public void EnviarPelaSerial(string texto)
    {
        if (BaudDaSerial <= 0 || string.IsNullOrEmpty(texto)) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(texto);

        // Os bytes comecam a sair quando a UART acaba o que ja estava na fila;
        // com a fila cheia, o print espera (TransmitirSerial).
        double inicio = Math.Max(AgoraUs(), fimDaTransmissaoUs);
        TransmitirSerial(bytes.Length);
        if (Perfil.PinoTx >= 0) Linha.Serial(inicio, Perfil.PinoTx, bytes, BaudDaSerial);
    }

    public void EscreverSerial(string linha)
    {
        lock (Trava) Serial.Add(linha);
    }

    bool Valido(int p)
    {
        if (p >= 0 && p < Perfil.NumPinos) return true;
        Avisar($"pino {p} nao existe no {Perfil.Nome} (vai de 0 a {Perfil.NumPinos - 1}).");
        return false;
    }

    /// <summary>Um aviso para o console, uma vez so por texto.</summary>
    public void Avisar(string msg)
    {
        lock (Trava) if (!Avisos.Contains(msg)) Avisos.Add(msg);
    }

    /// <summary>
    /// Corta a energia: pinos, PWM e som zerados, LCD apagado. O log do
    /// serial fica -- o aluno acabou de rodar e quer ler o que saiu.
    /// </summary>
    public void Desligar()
    {
        EncerrarNativo();
        Energizada = false;
        Sono = null;
        memoriaRtcDoUsuario = null;   // a memoria RTC so dura com energia
        lock (Trava)
        {
            LimparNiveis();
            Array.Clear(Digital);
            Array.Clear(Pwm);
            Array.Clear(Frequencia);
            Array.Clear(Pressionado);
        }
        Lcd?.Limpar();
        Lcd?.Desligar();
        lock (PaineisOled) PaineisOled.Clear();

        // Placa sem energia nao serve pagina nem fala MQTT.
        rede?.EsquecerPlaca(this);
    }

    /// <summary>Ninguem impondo nivel: -1 em todos os pinos.</summary>
    public void LimparNiveis()
    {
        for (int i = 0; i < NumPinos; i++) { NivelExterno[i] = -1; AnguloServo[i] = -1; }
        Array.Clear(SinaisExternos);

        // Tela apagada: placa reiniciada nao guarda o desenho anterior.
        Tela?.Limpar(0);

        // O cartao e outro chip: o reset da placa nao apaga arquivo nenhum
        // -- o jogo que acorda do sono profundo le o mapa de novo --, so
        // desmonta, e o sketch novo chama SD.begin() outra vez. Quem
        // esvazia e recarrega o cartao no comeco de cada rodada e a janela,
        // a partir da peca.
        Cartao.Desmontar();

        // Placa reiniciada nao guarda interrupcao do programa anterior.
        Interrupcoes.Clear();
        lock (filaDeBordas) filaDeBordas.Clear();
        InterrupcoesLigadas = true;
        Array.Clear(nivelVisto);
    }

    /// <summary>Servo.write(): guarda o angulo comandado no pino.</summary>
    public void EscreverServo(int p, int graus, double pulsoUs = -1)
    {
        if (!Valido(p)) return;
        AnguloServo[p] = Math.Clamp(graus, 0, 180);

        // O servo e um pulso a cada 20 ms; a largura e o angulo.
        double pulso = pulsoUs > 0 ? pulsoUs : 544 + AnguloServo[p] * (2400 - 544) / 180.0;
        Linha.Periodico(Gastar(Perfil.CustoDoPinoUs), p, 50, pulso / 20000.0, continuarFase: true);
    }

    /// <summary>servo.detach(): o pulso para.</summary>
    public void SoltarServo(int p)
    {
        if (!Valido(p)) return;
        AnguloServo[p] = -1;
        Linha.Parar(AgoraUs(), p);
    }

    /// <summary>
    /// O que acorda a placa do sono profundo, enquanto ela dorme; null com
    /// ela acordada. E o que a janela mostra no lugar do "rodando".
    /// </summary>
    public string Sono { get; private set; }

    /// <summary>
    /// O fio do D0 (GPIO16) no RST do ESP8266 existe? Sem ele o timer do
    /// sono profundo nao tem como acordar a placa. Quem conhece o circuito
    /// (a janela) responde; sem circuito, vale como ligado.
    /// </summary>
    public Func<bool> D0NoReset { get; set; }

    /// <summary>
    /// Sono profundo: a CPU desliga e os pinos soltam -- o LED que o sketch
    /// acendeu apaga, o buzzer cala, o PWM para. Os segurados (gpio_hold_en)
    /// ficam como estavam. O WiFi cai junto, sem se despedir do broker. A
    /// energia continua: o 3V3 da placa segue ligado, e com ele o que o
    /// circuito alimenta por ali.
    /// </summary>
    public void Dormir(IReadOnlyCollection<int> segurados, string despertar)
    {
        lock (Trava)
        {
            for (int p = 0; p < Modo.Length; p++)
            {
                Frequencia[p] = 0;
                if (segurados.Contains(p)) continue;
                if (Modo[p] != 1 && Pwm[p] == 0 && Digital[p] == 0) continue;
                if (Modo[p] == 1) Modo[p] = 0;
                if (Digital[p] != 0 || Pwm[p] != 0) Eventos.Add(new EventoPino(Ms, p, 0));
                Digital[p] = 0;
                Pwm[p] = 0;
            }
            CanalPwm.Clear();
            Array.Clear(fimDoTom);
        }

        rede?.EsquecerPlaca(this);
        Sono = despertar;
    }

    public void Reiniciar()
    {
        EncerrarNativo();
        ReinicioPedido = null;
        Energizada = true;
        Sono = null;
        memoria = null;
        lock (Trava)
        {
            Fitas.Clear();
            lock (Mostradores) Mostradores.Clear();
            lock (Matrizes) Matrizes.Clear();
            LimparNiveis();
            Array.Clear(Modo); Array.Clear(Digital); Array.Clear(Pwm);
            Array.Clear(Frequencia); Array.Clear(Pressionado);
            EventosAntesDaLista += Eventos.Count;
            Eventos.Clear(); Serial.Clear(); Avisos.Clear();
            EntradaSerial.Limpar();
            CanalPwm.Clear();
            Array.Clear(fimDoTom);
        }

        Linha.Limpar();
        BaudDaSerial = 0;
        ResolucaoDoPwm = 8;
        MaximoDoPwm = 0;
        ResolucaoDoAdc = 0;
        ReferenciaAnalogica = ReferenciaPadrao;
        FrequenciaDoPwmPedida = 0;
        Lcd = null;
        Oled = null;
        lock (Portas) Portas.Clear();
        I2c.Zerar();
        i2c1?.Zerar();
        lock (i2cPorSoftware) i2cPorSoftware.Clear();

        // Reiniciar e como tirar e por a energia: o WiFi cai, os servidores
        // fecham e as conexoes MQTT somem.
        rede?.EsquecerPlaca(this);

        Parar = false;
        IniciarRelogio();
    }
}
