namespace Simulador;

// ============================================================
//  Controle de fluxo
// ============================================================

/// <summary>
/// O que um comando pede para quem esta em volta dele: sair do laco
/// (break), pular para a proxima volta (continue) ou voltar da funcao
/// (return). Cada bloco para de rodar comandos quando ha um desvio, e quem
/// o atende (o laco, o switch, a funcao) o apaga.
///
/// Antes eram excecoes do .NET, e cada return custava dezenas de
/// microssegundos do computador: a AccelStepper, que faz quatro returns por
/// volta do loop(), girava a 1% da velocidade da ESP32, e a rampa do motor
/// de passo saia 25% mais lenta que a da placa.
/// </summary>
enum Desvio : byte { Nenhum, Break, Continue, Retorno }

public class ErroDeExecucao(string msg, int linha)
    : Exception($"{Origens.Onde(linha)}: {msg}") { }

/// <summary>Interrupcao pedida pela interface (botao Parar).</summary>
public class ExecucaoInterrompida : Exception;

// ============================================================
//  Interpretador (tree-walking)
// ============================================================
public partial class Interpretador(Programa prog, Maquina maq)
{
    readonly Ambiente global = new();
    readonly Dictionary<string, Funcao> funcoes = [];

    /// <summary>A placa desta execucao, para as outras partes do interpretador.</summary>
    Maquina Maq => maq;

    /// <summary>O programa, idem -- e dele que sai a linguagem do sketch.</summary>
    Programa Prog => prog;

    /// <summary>O escopo das globais, idem.</summary>
    Ambiente Global => global;

    /// <summary>Teto de passos por iteracao, para nao travar num while(1) sem delay.</summary>
    public long LimitePassos { get; set; } = 2_000_000;

    /// <summary>Quando existe, é avisado antes de cada comando. Ver <see cref="Depurador"/>.</summary>
    public Depurador Depurador { get; set; }

    /// <summary>Quantos comandos estão abertos um dentro do outro agora.</summary>
    int profundidade;
    long passos;

    /// <summary>O break, continue ou return pedido e ainda nao atendido.</summary>
    Desvio desvio;

    /// <summary>O valor do return pendente.</summary>
    Valor valorDoRetorno;

    /// <summary>
    /// Roda o corpo de uma funcao. True quando ele saiu por um return, que
    /// chega em <paramref name="retorno"/>. O desvio de quem esta em volta
    /// fica guardado: a funcao pode rodar enquanto outra esta saindo (uma
    /// interrupcao no meio do caminho), e o return daqui nao e o de la.
    /// </summary>
    bool ExecCorpoDeFuncao(CmdBloco corpo, Ambiente local, out Valor retorno)
    {
        var desvioDeFora = desvio;
        var valorDeFora = valorDoRetorno;
        desvio = Desvio.Nenhum;
        try
        {
            ExecBloco(corpo, local);
            bool voltou = desvio == Desvio.Retorno;
            retorno = voltou ? valorDoRetorno : Valor.Vazio;
            return voltou;
        }
        finally
        {
            desvio = desvioDeFora;
            valorDoRetorno = valorDeFora;
        }
    }

    /// <summary>
    /// Depois do corpo de um laco: o break sai dele, o continue segue para
    /// a proxima volta (os dois acabam aqui), e o return sobe ate a funcao.
    /// </summary>
    bool SaiuDoLaco()
    {
        switch (desvio)
        {
            case Desvio.Nenhum: return false;
            case Desvio.Continue: desvio = Desvio.Nenhum; return false;
            case Desvio.Break: desvio = Desvio.Nenhum; return true;
            default: return true;
        }
    }

    public void Preparar()
    {
        // O C da placa primeiro: as globais ja nascem com o tipo dela, e a
        // RAM ja sabe quanto elas ocupam.
        PrepararTiposC();
        PrepararMemoria();

        // As classes antes de tudo: o corpo de "Botao::ler" escrito fora
        // da classe so tem para onde ir se a classe ja existir.
        RegistrarClasses();
        foreach (var f in prog.Funcoes) RegistrarFuncao(f);

        // O C nativo antes das globais: uma global pode ser iniciada
        // chamando uma funcao dele.
        PrepararNativo();
        foreach (var g in prog.Globais) Exec(g, global);
        desvio = Desvio.Nenhum;
    }

    public void Setup()
    {
        // O C++ compilado de verdade: o setup() e o da DLL (ver
        // InterpretadorSketchNativo.cs), carregada aqui, na thread da placa.
        if (prog.Compilado is not null)
        {
            AbrirSketchNativo();
            sketchNativo.Setup();
            return;
        }
        ChamarSeExistir("setup");
    }

    public void Loop()
    {
        // A volta do loopTask: o watchdog do loop e alimentado, o ESP8266
        // cede o processador, e chamar o loop() custa uma chamada.
        Ceder();
        CobrarChamada(0);
        if (ciclos >= CiclosPorCobranca) PagarCiclos();

        // Tambem aqui, para o caso de o loop nao ter comando nenhum.
        AtenderInterrupcoes();
        EntregarEspNow();
        EntregarBluetooth();
        EntregarBle();
        if (sketchNativo is not null)
        {
            sketchNativo.Loop();
            return;
        }
        ChamarSeExistir("loop");
    }

    /// <summary>
    /// "attachInterrupt(digitalPinToInterrupt(4), contar, FALLING)".
    ///
    /// O pino e a borda sao valores; o do meio e o NOME de uma funcao do
    /// sketch, e por isso esta chamada nao passa pela avaliacao normal
    /// dos argumentos.
    /// </summary>
    Valor Vigiar(Chamada c, Ambiente amb)
    {
        if (c.Args.Count >= 2 && c.Args[1] is Variavel nativa && RotinaNativa(nativa.Nome, out _))
        {
            int pinoNativo = (int)Avaliar(c.Args[0], amb).Num;
            int bordaNativa = c.Args.Count > 2 ? (int)Avaliar(c.Args[2], amb).Num : Maquina.Mudando;
            maq.Vigiar(pinoNativo, nativa.Nome, bordaNativa);
            return Valor.Vazio;
        }
        if (c.Args.Count < 2 || c.Args[1] is not Variavel alvo)
            throw new ErroDeExecucao(
                "attachInterrupt() espera o nome de uma funcao no 2o argumento, "
                + "como em attachInterrupt(0, contar, FALLING).", c.Linha);

        if (!funcoes.ContainsKey(alvo.Nome))
            throw new ErroDeExecucao(
                $"attachInterrupt() aponta para '{alvo.Nome}()', que nao existe "
                + "neste sketch.", c.Linha);

        int pino = (int)Avaliar(c.Args[0], amb).Num;
        int borda = c.Args.Count > 2
            ? (int)Avaliar(c.Args[2], amb).Num : Maquina.Mudando;

        maq.Vigiar(pino, alvo.Nome, borda);
        return Valor.Vazio;
    }

    /// <summary>
    /// Chama o que estiver vencido. Nao reentra: interrupcao disparada
    /// de dentro de outra viraria pilha infinita com o botao segurado.
    /// </summary>
    bool atendendo;

    public void AtenderInterrupcoes()
    {
        // Nao reentra: interrupcao disparada de dentro do proprio
        // atendimento viraria pilha infinita com o botao segurado.
        if (atendendo) return;

        // Os Timers do MicroPython vencidos (InterpretadorMicroPythonTempo.cs).
        if (timersPy.Count > 0)
        {
            atendendo = true;
            try { AtenderTimersPy(); }
            finally { atendendo = false; }
        }

        // O timer de hardware do C++ compilado (timerAlarm): as interrupcoes
        // vencidas, em lote, cada uma no seu tempo (cs_timer.cpp).
        if (sketchNativo is SketchNativo nativo && nativo.TimersAtivos)
        {
            atendendo = true;
            try { nativo.AtenderTimers(); }
            finally { atendendo = false; }
        }

        var externas = maq.InterrupcoesExternas();
        var vencidas = maq.InterrupcoesPendentes();
        if (vencidas.Count == 0 && externas.Count == 0) return;

        atendendo = true;
        try
        {
            // As bordas de fora primeiro, cada uma no instante dela.
            foreach (var (nome, instante) in externas)
            {
                maq.ForcarRelogio(instante);
                try { ChamarInterrupcao(nome); }
                finally { maq.SoltarRelogio(); }
            }
            foreach (string nome in vencidas) ChamarInterrupcao(nome);
        }
        finally { atendendo = false; }
    }
    public bool TemLoop => prog.Compilado is not null || funcoes.ContainsKey("loop");

    /// <summary>
    /// Roda setup() ou loop().
    ///
    /// Em Python os dois correm NAS GLOBAIS, e nao num escopo filho. E o
    /// que o modulo faz de verdade: "prev = ''" escrito antes do
    /// "while True:" e a mesma variavel que o laco le depois. Num escopo
    /// filho ela morreria no fim do setup, e o sketch de MQTT do Wokwi
    /// republicaria a mesma medicao para sempre.
    /// </summary>
    void ChamarSeExistir(string nome)
    {
        if (!funcoes.TryGetValue(nome, out var f)) return;
        passos = 0;
        long quadro = EntrarNaPilha(f, f.Linha);
        var escopo = EhPython ? global : new Ambiente(global);
        try { ExecCorpoDeFuncao(f.Corpo, escopo, out _); }
        finally
        {
            if (escopo != global) FecharEscopo(escopo);
            SairDaPilha(quadro);
        }
    }

    /// <summary>
    /// Roda a rotina de interrupcao sem zerar o contador de passos.
    ///
    /// ChamarSeExistir zera o contador -- certo para setup() e loop(),
    /// que comecam do zero a cada volta. Para a interrupcao estava
    /// errado, e ficou perigoso quando ela passou a cortar comandos e
    /// expressoes: um "while (true) {}" com o botao pulsando nunca
    /// chegaria ao limite, porque cada interrupcao devolveria o contador
    /// a zero. A rotina conta passos normalmente (um laco preso DENTRO
    /// dela ainda estoura) e, ao terminar, o contador volta a ser o do
    /// codigo interrompido.
    /// </summary>
    void ChamarInterrupcao(string nome)
    {
        if (ChamarIrqPy(nome)) return;   // o Pin.irq do MicroPython
        if (RotinaNativa(nome, out int nativa))
        {
            sketchNativo?.Rotina(nativa);
            return;
        }
        if (!funcoes.TryGetValue(nome, out var f)) return;
        long antes = passos;
        var escopo = new Ambiente(global);
        try { ExecCorpoDeFuncao(f.Corpo, escopo, out _); }
        finally
        {
            FecharEscopo(escopo);
            passos = antes;
        }
    }

    // ---------------- comandos ----------------
    /// <summary>
    /// Todo comando passa por aqui. Sem depurador, vai direto; com ele,
    /// o depurador é avisado antes -- e é onde a pausa acontece.
    ///
    /// A profundidade é contada neste invólucro, e não dentro do comando,
    /// para ficar certa mesmo quando o comando lança exceção (break,
    /// return, loop preso): o finally a devolve.
    /// </summary>
    void Exec(Cmd c, Ambiente amb)
    {
        if (Depurador is null) { ExecComando(c, amb); return; }

        profundidade++;
        try
        {
            Depurador.NoComando(c, amb, profundidade);
            ExecComando(c, amb);
        }
        finally { profundidade--; }
    }

    void ExecComando(Cmd c, Ambiente amb)
    {
        if (maq.Parar) throw maq.MotivoDaParada();

        // Interrupcao a cada comando. Tambem e atendida dentro das
        // expressoes (ver Avaliar) -- esta aqui cobre o comando que nao
        // avalia expressao nenhuma, como o "for (;;) {}" vazio.
        //
        // Custa uma comparacao quando nao ha interrupcao registrada, que
        // e o caso de quase todo sketch.
        if (TemAtendimento) AtenderInterrupcoes();

        // O limite de passos e so do tempo virtual (testes, diagnostico):
        // na placa um laco sem delay() nao e erro nenhum, e no tempo real
        // quem vigia sao os watchdogs de cada chip (InterpretadorCiclos).
        if (++passos > LimitePassos && !maq.TempoReal)
            throw new ErroDeExecucao(
                $"loop preso: passou de {LimitePassos} passos sem sair. " +
                "Falta um delay() ou uma condicao de saida?", c.Linha);

        CobrarComando();

        switch (c)
        {
            case CmdVazio: break;

            case CmdExpr e:
                Avaliar(e.E, amb);
                break;

            case CmdParaCada pc:
                ExecParaCada(pc, amb);
                break;

            case CmdParaCadaC pcc:
                ExecParaCadaC(pcc, amb);
                break;

            case CmdImporta imp:
                ExecImporta(imp, amb);
                break;

            case CmdDecl d when d.EstaticoLocal:
                DeclararEstaticoLocal(d, d.Nome, amb);
                amb.DeclararTipo(d.Nome, d.Tipo);
                break;

            case CmdDeclObj o when o.EstaticoLocal:
                DeclararEstaticoLocal(o, o.Nome, amb);
                amb.DeclararTipo(o.Nome, o.Classe);
                break;

            case CmdDecl d:
                ExecDecl(d, amb);
                amb.DeclararTipo(d.Nome, d.Tipo);
                if (d.Secao is not null) RestaurarDaRtc(d, amb);
                break;

            case CmdDeclObj o:
                ExecDeclObj(o, amb);
                amb.DeclararTipo(o.Nome, o.Classe);
                break;

            case CmdEstrutura es:
                foreach (string nome in es.Nomes) estruturas[nome] = es.Campos;
                break;

            case ClasseDecl cd:
                IniciarEstaticos(cd);
                break;

            case CmdBloco b:
                // Em Python o bloco NAO cria escopo: so a funcao cria.
                // Uma variavel escrita dentro de um "if" continua
                // existindo depois dele, e e assim que quase todo sketch
                // de Python guarda o resultado de uma conta. Com escopo
                // por bloco, "result = a + b" dentro do if morria na
                // chave de fechar e a linha seguinte dizia que a
                // variavel nao existia.
                //
                // E um bloco que nao declara nada tambem roda no escopo de
                // fora: o escopo vazio de cada "if" e de cada volta de laco
                // so dava trabalho a coleta de lixo.
                if (EhPython || !DeclaraNomes(b)) ExecBloco(b, amb);
                else
                {
                    var bloco = new Ambiente(amb);
                    try { ExecBloco(b, bloco); }
                    finally { FecharEscopo(bloco); }
                }
                break;

            case CmdVarios v:
                foreach (var x in v.Corpo)   // mesmo escopo, de proposito
                {
                    Exec(x, amb);
                    if (desvio != Desvio.Nenhum) break;
                }
                break;

            case CmdSe s:
                if (Avaliar(s.Cond, amb).Verdadeiro) Exec(s.Entao, amb);
                else if (s.Senao != null) Exec(s.Senao, amb);
                break;

            case CmdEnquanto w:
                while (Avaliar(w.Cond, amb).Verdadeiro)
                {
                    Exec(w.Corpo, amb);
                    if (SaiuDoLaco()) break;
                }
                break;

            case CmdFacaEnquanto dw:
                do
                {
                    Exec(dw.Corpo, amb);
                    if (SaiuDoLaco()) break;
                } while (Avaliar(dw.Cond, amb).Verdadeiro);
                break;

            case CmdPara f:
            {
                var escopo = EhPython ? amb : new Ambiente(amb);
                try
                {
                    if (f.Ini != null) Exec(f.Ini, escopo);
                    while (f.Cond is null || Avaliar(f.Cond, escopo).Verdadeiro)
                    {
                        Exec(f.Corpo, escopo);
                        if (SaiuDoLaco()) break;
                        if (f.Passo != null) Avaliar(f.Passo, escopo);
                    }
                }
                finally { if (escopo != amb) FecharEscopo(escopo); }
                break;
            }

            case CmdEscolha sw:
                ExecEscolha(sw, amb);
                break;

            case CmdRetorno r:
                valorDoRetorno = r.Valor is null ? Valor.Vazio : Avaliar(r.Valor, amb);
                desvio = Desvio.Retorno;
                break;

            case CmdBreak: desvio = Desvio.Break; break;
            case CmdContinue: desvio = Desvio.Continue; break;

            // O try, o raise e o with do Python (InterpretadorExcecoes.cs).
            case CmdTente tente: ExecTente(tente, amb); break;
            case CmdLevanta levanta: ExecLevanta(levanta, amb); break;
            case CmdCom com: ExecCom(com, amb); break;

            default:
                throw new ErroDeExecucao($"comando nao suportado: {c.GetType().Name}", c.Linha);
        }
    }

    /// <summary>Os blocos que declaram algum nome direto neles: so esses precisam de escopo proprio.</summary>
    readonly Dictionary<CmdBloco, bool> blocosQueDeclaram = new(ReferenceEqualityComparer.Instance);

    bool DeclaraNomes(CmdBloco b)
    {
        if (blocosQueDeclaram.TryGetValue(b, out bool declara)) return declara;
        declara = false;
        foreach (var c in b.Corpo)
            if (!NaoDeclara(c)) { declara = true; break; }
        blocosQueDeclaram[b] = declara;
        return declara;
    }

    /// <summary>
    /// O comando nao poe nome nenhum no escopo em que roda. O bloco de dentro,
    /// o for e o switch tem o escopo deles; o "if (x) int y = 1;" sem chaves
    /// declararia aqui, e por isso o corpo do if e dos lacos e olhado tambem.
    /// </summary>
    static bool NaoDeclara(Cmd c) => c switch
    {
        null => true,
        CmdExpr or CmdBloco or CmdPara or CmdEscolha or CmdRetorno or CmdBreak or CmdContinue or CmdVazio => true,
        CmdSe s => NaoDeclara(s.Entao) && NaoDeclara(s.Senao),
        CmdEnquanto w => NaoDeclara(w.Corpo),
        CmdFacaEnquanto dw => NaoDeclara(dw.Corpo),
        _ => false,
    };

    void ExecBloco(CmdBloco b, Ambiente amb)
    {
        foreach (var c in b.Corpo)
        {
            Exec(c, amb);
            if (desvio != Desvio.Nenhum) return;
        }
    }

    /// <summary>switch com fall-through: sem break, cai no case seguinte.</summary>
    void ExecEscolha(CmdEscolha sw, Ambiente amb)
    {
        var alvo = Avaliar(sw.Sujeito, amb);

        int inicio = -1;
        for (int i = 0; i < sw.Casos.Count && inicio < 0; i++)
        {
            var caso = sw.Casos[i];
            if (caso.Valor is null) continue;                       // o default fica para depois
            if (Avaliar(caso.Valor, amb).Num == alvo.Num) inicio = i;
        }
        if (inicio < 0) inicio = sw.Casos.FindIndex(k => k.Valor is null);
        if (inicio < 0) return;

        var escopo = new Ambiente(amb);
        try
        {
            for (int i = inicio; i < sw.Casos.Count; i++)
                foreach (var cmd in sw.Casos[i].Corpo)
                {
                    Exec(cmd, escopo);
                    if (desvio == Desvio.Nenhum) continue;
                    // O break e do switch; o continue e o return sao de quem esta em volta.
                    if (desvio == Desvio.Break) desvio = Desvio.Nenhum;
                    return;
                }
        }
        finally { FecharEscopo(escopo); }
    }

    /// <summary>Onde mora cada "static" de dentro de funcao (uma por declaracao do codigo).</summary>
    readonly Dictionary<Cmd, Ambiente> estaticosLocais = new(ReferenceEqualityComparer.Instance);

    /// <summary>O apelido de cada static numero: o mesmo em toda chamada.</summary>
    readonly Dictionary<Cmd, Valor> apelidosEstaticos = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// O static de dentro de funcao: na primeira vez a declaracao roda num
    /// escopo so dela (filho do da chamada, para o valor inicial poder usar os
    /// parametros, como no C++) e fica guardada; em toda chamada o nome aponta
    /// para la -- o numero por apelido, o vetor e o objeto pelo mesmo
    /// conteudo. Antes ela nascia de novo a cada chamada, e o
    /// "static unsigned long ultimo = 0;" do pisca sem delay nunca guardava nada.
    /// </summary>
    void DeclararEstaticoLocal(Cmd decl, string nome, Ambiente amb)
    {
        if (!estaticosLocais.TryGetValue(decl, out var guarda))
        {
            guarda = new Ambiente(amb);
            if (decl is CmdDecl d) ExecDecl(d with { EstaticoLocal = false }, guarda);
            else if (decl is CmdDeclObj o) ExecDeclObj(o with { EstaticoLocal = false }, guarda);
            estaticosLocais[decl] = guarda;
        }

        if (guarda.BuscarArrayLocal(nome, out var vetor))
        {
            amb.DeclararArray(nome, vetor);
            if (guarda.EhVetorDeBytes(nome)) amb.MarcarVetorDeBytes(nome);
            if (guarda.DimensoesDe(nome, out var internas)) amb.DeclararDimensoes(nome, internas);
            else if (guarda.ColunasDe(nome, out int colunas)) amb.DeclararColunas(nome, colunas);
            return;
        }
        if (guarda.BuscarObjetoLocal(nome, out var objeto))
        {
            amb.DeclararObjeto(nome, objeto);
            return;
        }
        if (!apelidosEstaticos.TryGetValue(decl, out var apelido))
        {
            apelido = Valor.DeReferencia(new Apelido(
                () => guarda.BuscarLocal(nome, out var v) ? v : Valor.DeInt(0),
                v => guarda.Atribuir(nome, v)));
            apelidosEstaticos[decl] = apelido;
        }
        amb.Declarar(nome, apelido);
    }

    void ExecDecl(CmdDecl d, Ambiente amb)
    {
        // "extern int contador;" so promete: quem cria e a declaracao de
        // verdade, em outro arquivo. Criar aqui zeraria o valor dela.
        if (d.Externo) return;

        // "int Botao::contador = 0;": o static de uma classe.
        if (d.Nome.Contains("::") && DeclararEstatico(d)) return;

        // "Coisa &c = coisas[i];": o mesmo objeto, e nao uma copia dele.
        if (d.PorReferencia && DeclararApelido(d, amb)) return;

        // Objeto de classe declarado com o tipo (o parser nao sabia que era classe).
        if (!d.EhArray && !d.Tipo.EndsWith('*') && classes.ContainsKey(d.Tipo))
        {
            ExecDeclObj(new CmdDeclObj(d.Tipo, d.Nome,
                d.Itens ?? [], d.Linha) { Inicial = d.Inicial }, amb);
            return;
        }

        // "GnssState gnss;" -- variavel de um tipo que o proprio projeto
        // declarou com struct.
        //
        // Ela chega aqui, e nao no ExecDeclObj, porque o parser passou a
        // conhecer o nome da struct como TIPO (foi o que permitiu campo
        // de struct dentro de struct). Sem esta porta, "gnss" virava um
        // numero solto e "gnss.speedKmh = 123" dizia "destino de
        // atribuicao invalido".
        if (!d.EhArray && !d.Tipo.EndsWith('*') && estruturas.TryGetValue(SemPonteiro(d.Tipo), out var molde))
        {
            var estrutura = new Objeto(SemPonteiro(d.Tipo));
            foreach (var campo in molde)
                estrutura.Campos[campo.Nome] = ValorInicialDoCampo(campo, amb);

            // "Ponto p = {1, 2};" preenche campo a campo; "Ponto p = q;" copia.
            if (d.Itens is not null) PreencherEstrutura(estrutura, molde, d.Itens, amb);
            else if (d.Inicial is not null && Avaliar(d.Inicial, amb).Referencia is Objeto fonte)
                foreach (var (chave, copiado) in fonte.Campos) estrutura.Campos[chave] = copiado;

            amb.DeclararObjeto(d.Nome, estrutura);
            return;
        }

        if (d.EhArray)
        {
            amb.DeclararArray(d.Nome, MontarVetor(d, amb));
            if (EhDeBytes(d.Tipo) && !d.Tipo.EndsWith('*')) amb.MarcarVetorDeBytes(d.Nome);

            // Vetor de duas ou mais dimensoes: o conteudo ficou esticado, e
            // as dimensoes de dentro e que fazem "teclas[1][2]" e
            // "cubo[1][2][3]" acharem o item.
            if (d.Dimensoes is { Count: > 0 })
                amb.DeclararDimensoes(d.Nome, [.. d.Dimensoes.Select(x => Math.Max(1, (int)Avaliar(x, amb).Num))]);
            else if (d.Colunas is not null)
                amb.DeclararColunas(d.Nome, Math.Max(1, (int)Avaliar(d.Colunas, amb).Num));

            return;
        }

        // "int* p = leituras;", "const char** arte = ROBO;": o ponteiro
        // aponta o vetor, e "p[2]" le o item dele.
        if (d.Tipo.EndsWith('*') && d.Inicial is not null && VetorComoPonteiro(d.Inicial, amb, out var ponteiro))
        {
            amb.Declarar(d.Nome, ponteiro);
            return;
        }

        var v = d.Inicial is not null ? Coagir(Avaliar(d.Inicial, amb), d.Tipo)
              : d.Tipo == "String" ? Valor.DeTexto("")
              : ZeroDe(d.Tipo);

        // A String ganha o buffer dela na heap da placa.
        if (d.Tipo == "String") v = DeclararTexto(amb, d.Nome, v, d.Linha);

        if (EhCaractere(d.Tipo)) amb.DeclararCaractere(d.Nome, v);
        else amb.Declarar(d.Nome, v);
    }

    readonly Dictionary<string, List<CmdDecl>> estruturas = [];

    /// <summary>Frequencia e bits de cada canal do LEDC (ledcSetup, ledcAttach).</summary>
    readonly Dictionary<int, (double Hz, int Bits)> ledc = [];

    /// <summary>
    /// Structs do ESP-IDF que os sketches declaram sem definir, com os
    /// campos que elas têm lá.
    /// </summary>
    static readonly Dictionary<string, (string Campo, Valor Inicial)[]> EstruturasDoSistema = new()
    {
        ["esp_now_peer_info_t"] = CamposDoParEspNow(),
        ["esp_now_peer_info"] = CamposDoParEspNow(),

        // O pwm_config do SDK da Pico, ja com o que o
        // pwm_get_default_config() devolve: divisor 1 (16 em ponto fixo
        // 8.4) e o contador ate 65535.
        ["pwm_config"] =
        [
            ("csr", Valor.DeInt(0)), ("div", Valor.DeInt(16)), ("top", Valor.DeInt(0xFFFF)),
        ],
    };

    static (string, Valor)[] CamposDoParEspNow() =>
    [
        ("peer_addr", Valor.DeTexto("")), ("lmk", Valor.DeTexto("")),
        ("channel", Valor.DeInt(0)), ("ifidx", Valor.DeInt(0)),
        ("encrypt", Valor.DeInt(0)), ("priv", Valor.DeInt(0)),
    ];

    /// <summary>"GeoPoint*" e "GeoPoint": para o simulador, a mesma coisa.</summary>
    static string SemPonteiro(string tipo) => tipo?.TrimEnd('*', ' ') ?? "";

    /// <summary>
    /// Classes que sao outra com outro nome. O servidor do ESP8266 tem a
    /// mesma API do WebServer da ESP32 -- so o nome muda --, e sem o
    /// apelido a NodeMCU nao subia pagina nenhuma.
    /// </summary>
    static readonly Dictionary<string, string> ApelidosDeClasse = new()
    {
        ["ESP8266WebServer"] = "WebServer",
        ["BearSSL::WiFiClientSecure"] = "WiFiClientSecure",

        // O DS3231 fala a mesma lingua do DS1307 no RTClib (e no mesmo
        // endereco, 0x68); so tem a mais o termometro e o lostPower().
        ["RTC_DS3231"] = "RTC_DS1307",
        ["RTC_PCF8523"] = "RTC_DS1307",
        ["RTC_PCF8563"] = "RTC_DS1307",
    };

    void ExecDeclObj(CmdDeclObj d, Ambiente amb)
    {
        string classeDoSketch = d.Classe;
        if (ApelidosDeClasse.TryGetValue(d.Classe, out var verdadeira)) d = d with { Classe = verdadeira };

        // Classe do proprio programa ou de uma biblioteca instalada.
        if (DeclararObjetoDeClasse(d, amb)) return;

        // "Ponto p{1, 2};" de uma struct so de campos.
        if (estruturas.TryGetValue(d.Classe, out var moldeComItens) && d.Args.Count > 0)
        {
            var estrutura = new Objeto(d.Classe);
            foreach (var campo in moldeComItens)
                estrutura.Campos[campo.Nome] = ValorInicialDoCampo(campo, amb);
            PreencherEstrutura(estrutura, moldeComItens, d.Args, amb);
            amb.DeclararObjeto(d.Nome, estrutura);
            return;
        }

        // "struct tm agora;": o tm nao e declarado pelo sketch, vem do C.
        if (EhHorario(d.Classe) && !estruturas.ContainsKey(d.Classe))
        {
            amb.DeclararObjeto(d.Nome, NovoHorario());
            return;
        }

        // "esp_now_peer_info_t par;": outra struct que vem do sistema da
        // placa, e nao do sketch. Nasce com os campos de la, zerados --
        // e o que o "= {}" do tutorial faz.
        if (EstruturasDoSistema.TryGetValue(d.Classe, out var doSistema)
            && !estruturas.ContainsKey(d.Classe))
        {
            var estrutura = new Objeto(d.Classe);
            foreach (var (campo, inicial) in doSistema) estrutura.Campos[campo] = inicial;
            amb.DeclararObjeto(d.Nome, estrutura);
            return;
        }

        if (estruturas.TryGetValue(d.Classe, out var campos))
        {
            var estrutura = new Objeto(d.Classe);
            foreach (var campo in campos)
                estrutura.Campos[campo.Nome] = ValorInicialDoCampo(campo, amb);

            amb.DeclararObjeto(d.Nome, estrutura);
            return;
        }

        // "Adafruit_ST7789 tft = Adafruit_ST7789(5, 2, 4);": do lado
        // direito esta o CONSTRUTOR escrito por extenso, e nao uma
        // funcao. E o jeito que as bibliotecas da Adafruit mostram no
        // proprio exemplo, e ate aqui o sketch parava na linha 3 com
        // "funcao nao suportada".
        if (d.Inicial is Chamada construtor
            && construtor.Alvo is Variavel classe && classe.Nome == d.Classe)
        {
            d = d with { Args = construtor.Args, Inicial = null };
        }

        // Os argumentos do construtor sao avaliados (e os pinos ficam
        // registrados), mesmo que a v1 nao simule a fiacao de 4 bits.
        foreach (var a in d.Args) Avaliar(a, amb);

        // "File dados = SD.open(...)": a chamada tem que rodar ANTES de
        // montar o objeto, porque e ela que abre o arquivo.
        if (d.Inicial is not null) valorInicialDoObjeto = Avaliar(d.Inicial, amb);

        if (d.Classe is "LiquidCrystal" or "LiquidCrystal_I2C")
        {
            var display = new Objeto(d.Classe) { Lcd = maq.CriarLcd() };

            if (d.Classe == "LiquidCrystal_I2C")
            {
                // LiquidCrystal_I2C lcd(0x27, 20, 4): endereco, colunas,
                // linhas. O tamanho vem daqui porque o sketch de modulo
                // chama lcd.init(), e nao lcd.begin(20, 4) -- sem isto o
                // 20x4 escrevia numa tela de 16x2 e cortava o texto.
                var argsLcd = d.Args.Select(a => Avaliar(a, amb)).ToList();
                int Num(int k) => k < argsLcd.Count ? (int)argsLcd[k].Num : 0;

                if (argsLcd.Count >= 3) display.Lcd.Dimensionar(Num(1), Num(2));
                RegistrarNoI2c(display, argsLcd.Count > 0 ? Num(0) : -1);
            }

            amb.DeclararObjeto(d.Nome, display);
            return;
        }

        if (Bibliotecas.Existe(d.Classe))
        {
            var obj = new Objeto(d.Classe);
            var args = d.Args.Select(a => Avaliar(a, amb)).ToList();
            int Arg(int k) => k < args.Count ? (int)args[k].Num : 0;

            // A ArduinoBLE: o BLEService, o BLECharacteristic e o BLEDevice sao
            // os dela -- "BLEDevice central = BLE.central();" e o proprio aparelho.
            if (UsaArduinoBle && EhClasseDaArduinoBle(d.Classe))
            {
                amb.DeclararObjeto(d.Nome, MontarObjetoDaArduinoBle(obj, d.Classe, args,
                    d.Inicial is not null ? valorInicialDoObjeto : Valor.Vazio));
                return;
            }

            switch (d.Classe)
            {
                case "DHT":
                case "IRrecv":
                    obj.Pino = Arg(0);
                    break;

                case "sensors_event_t":
                    MontarEventoDeSensor(obj);
                    break;

                case "Adafruit_NeoPixel":
                    // Adafruit_NeoPixel fita(quantidade, pino, formato)
                    obj.Fita = new Fita(Arg(0));
                    obj.Pino = Arg(1);
                    maq.Fitas[obj.Pino] = obj.Fita;
                    break;

                case "Stepper":
                    // Stepper motor(passosPorVolta, p1, p2, p3, p4)
                    MontarMotorDePasso(obj, args);
                    break;

                // Keypad(makeKeymap(teclas), rowPins, colPins, ROWS, COLS)
                case "Keypad":
                    MontarTeclado(obj, d, amb);
                    break;

                case "HardwareSerial":
                case "SoftwareSerial":
                    MontarPortaSerial(obj, d.Classe, args);
                    break;

                // "TwoWire I2Cdois = TwoWire(1);" na ESP32, "TwoWire Wire2(PB11, PB10);" na STM32.
                case "TwoWire":
                    MontarTwoWire(obj, d.Nome, args, d.Linha);
                    break;

                case "Adafruit_SSD1306":
                // O SH1106 de 1,3" (a Adafruit_SH110X): a mesma Adafruit_GFX num buffer de 1 bit.
                case "Adafruit_SH1106G":
                    obj.Lcd = maq.CriarOled();
                    MontarOled(obj, args);
                    if (!obj.Campos["_spi"].Verdadeiro) RegistrarNoI2c(obj, -1, (int)obj.Campos["_wire"].Num);
                    break;

                // "TM1637Display display(CLK, DIO);" e "LedControl lc = LedControl(DIN, CLK, CS, 1);"
                case "TM1637Display":
                    MontarTm1637(obj, args);
                    break;
                case "LedControl":
                    MontarMax7219(obj, args);
                    break;

                // "MPU6050 mpu(0x69);": o endereco vem do construtor (o AD0 em 3V3).
                case "Adafruit_MPU6050":
                case "MPU6050":
                    if (args.Count > 0 && !args[0].EhTexto && args[0].Num is 0x68 or 0x69) obj.Campos["_endereco"] = args[0];
                    RegistrarNoI2c(obj, obj.Campos.TryGetValue("_endereco", out var doAd0) ? (int)doAd0.Num : -1);
                    break;

                // O relogio nasce acertado com a hora do computador, como um
                // modulo com bateria que alguem ja acertou uma vez.
                case "RTC_DS1307":
                    if (classeDoSketch == "RTC_PCF8563") obj.Campos["_endereco"] = Valor.DeInt(0x51);
                    RegistrarNoI2c(obj, classeDoSketch == "RTC_PCF8563" ? 0x51 : -1);
                    obj.Campos["_base"] = Valor.DeInt(DateTimeOffset.Now.ToUnixTimeSeconds()
                                                      + (long)DateTimeOffset.Now.Offset.TotalSeconds
                                                      - maq.Ms / 1000);
                    break;

                // "DateTime agora = rtc.now();" ou "DateTime d(2026, 9, 24, 8, 0, 0);"
                case "DateTime":
                    obj = d.Inicial is not null
                          && valorInicialDoObjeto.Referencia is Objeto { Classe: "DateTime" } pronta
                        ? DataEm(SegundosDaData(pronta))
                        : NovaData(args, d.Linha);
                    break;

                case "UniversalTelegramBot":
                    obj = NovoTelegram(args.Count > 0 ? args[0].Mostrar() : "");
                    amb.DeclararObjeto(d.Nome, obj);
                    break;

                case "Preferences":
                    obj.Rede = new Dictionary<string, Valor>(StringComparer.Ordinal);
                    break;

                case "SPIClass":
                    MontarBarramentoSpi(obj, args, d.Linha);
                    break;

                case "SPISettings":
                    obj = NovaConfiguracaoSpi(args);
                    break;

                case "Adafruit_ILI9341":
                    obj.Tela = maq.CriarTela();
                    break;

                case "Adafruit_ST7789":
                    obj.Tela = maq.CriarTela(240, 240);
                    break;

                // O de 1,8" (128 x 160); o initR() troca para o 1,44" ou o mini, se o sketch pedir.
                case "Adafruit_ST7735":
                    obj.Tela = maq.CriarTela(128, 160);
                    break;

                case "File":
                    // "File dados = SD.open(...)": o SD.open ja rodou ao
                    // avaliar o valor da declaracao, e deixou o arquivo
                    // aqui esperando.
                    obj.Arquivo = arquivoRecemAberto;
                    arquivoRecemAberto = null;
                    break;

                default:
                    if (EhClasseDeRede(d.Classe)) MontarObjetoDeRede(obj, d, amb);
                    if (EhClasseBle(d.Classe)) MontarObjetoBle(obj, d, amb);
                    break;
            }

            amb.DeclararObjeto(d.Nome, obj);
            return;
        }

        throw new ErroDeExecucao(
            $"a classe '{d.Classe}' ainda nao e suportada. Suportadas: "
            + string.Join(", ", Bibliotecas.Conhecidas), d.Linha);
    }

    Valor ValorInicialDoCampo(CmdDecl campo, Ambiente amb)
    {
        // Campo cujo tipo e outra struct: "struct GeoLine { GeoPoint a; }".
        // Ele nasce como OBJETO, senao "linha.a.lat" nao teria onde
        // guardar nada.
        if (!campo.EhArray && estruturas.TryGetValue(SemPonteiro(campo.Tipo), out var molde))
        {
            var dentro = new Objeto(SemPonteiro(campo.Tipo));
            foreach (var subcampo in molde)
                dentro.Campos[subcampo.Nome] = ValorInicialDoCampo(subcampo, amb);
            return Valor.DeReferencia(dentro);
        }

        if (campo.EhArray || campo.Tipo == "String")
            return Valor.DeTexto(campo.Inicial is null ? "" : Avaliar(campo.Inicial, amb).Mostrar());

        return campo.Inicial is null
            ? ZeroDe(campo.Tipo)
            : Coagir(Avaliar(campo.Inicial, amb), campo.Tipo);
    }

    /// <summary>Trunca ao atribuir a um tipo inteiro: preserva a semantica de C.</summary>
    /// <summary>
    /// Monta o vetor de uma declaracao. Alem do caso comum, cobre o
    /// "char nome[] = \"ESP32\";", que vira os bytes do texto mais o zero
    /// do fim -- antes disso o vetor saia com tamanho zero.
    /// </summary>
    Valor[] MontarVetor(CmdDecl d, Ambiente amb)
    {
        bool deCaractere = EhCaractere(d.Tipo);
        int tamanho = d.Tamanho is not null ? (int)Avaliar(d.Tamanho, amb).Num : 0;

        // "Botao botoes[3];" e "Nota melodia[] = { {262, 4}, {294, 4} };":
        // cada item e um objeto, montado pelo construtor ou campo a campo.
        if (!d.Tipo.EndsWith('*') && d.Colunas is null)
        {
            if (classes.TryGetValue(d.Tipo, out var daClasse))
            {
                int quantos = Math.Max(d.Itens?.Count ?? 0, tamanho);
                var objetos = new Valor[quantos];
                for (int i = 0; i < quantos; i++)
                    objetos[i] = ElementoDeClasse(daClasse, d.Itens is not null && i < d.Itens.Count ? d.Itens[i] : null,
                                                  amb, d.Linha);
                return objetos;
            }

            // "Servo motor[4];": cada item e um objeto da biblioteca, montado
            // como se fosse declarado sozinho ("Servo motor_0;").
            if (Bibliotecas.Existe(d.Tipo) && !estruturas.ContainsKey(d.Tipo))
            {
                int quantos = Math.Max(d.Itens?.Count ?? 0, tamanho);
                var objetos = new Valor[quantos];
                for (int i = 0; i < quantos; i++)
                {
                    var item = d.Itens is not null && i < d.Itens.Count ? d.Itens[i] : null;
                    var argsDoItem = item is Chamada { Alvo: Variavel construtor } chamada && construtor.Nome == d.Tipo ? chamada.Args
                                   : item is ListaLiteral lista ? lista.Itens
                                   : item is null ? [] : [item];
                    var escopo = new Ambiente(amb);
                    ExecDeclObj(new CmdDeclObj(d.Tipo, "__item", argsDoItem, d.Linha), escopo);
                    objetos[i] = escopo.BuscarObjeto("__item", out var montado) ? Valor.DeReferencia(montado) : Valor.Vazio;
                }
                return objetos;
            }

            if (estruturas.ContainsKey(d.Tipo))
            {
                int quantas = Math.Max(d.Itens?.Count ?? 0, tamanho);
                var structs = new Valor[quantas];
                for (int i = 0; i < quantas; i++)
                {
                    var item = d.Itens is not null && i < d.Itens.Count ? d.Itens[i] : null;
                    structs[i] = Valor.DeReferencia(NovaEstrutura(d.Tipo,
                        item is ListaLiteral lista ? lista.Itens : item is null ? null : [item], amb));
                }
                return structs;
            }
        }

        if (d.Dimensoes is { Count: > 1 }) return MontarVetorDeDimensoes(d, tamanho, amb);
        if (d.Colunas is not null) return MontarVetorDeLinhas(d, tamanho, deCaractere, amb);

        if (d.Itens is null && d.Inicial is not null)
        {
            var inicial = Avaliar(d.Inicial, amb);
            if (inicial.EhTexto)
            {
                var bytes = TextoArduino.ParaBytes(inicial.Texto);
                var texto = new Valor[Math.Max(tamanho, bytes.Length + 1)];
                for (int i = 0; i < texto.Length; i++)
                    texto[i] = Coagir(Valor.DeCaractere(i < bytes.Length ? bytes[i] : 0), d.Tipo);
                return texto;
            }
        }

        int n = Math.Max(d.Itens?.Count ?? 0, tamanho);
        var vetor = new Valor[Math.Max(n, 0)];
        var zero = ZeroDe(d.Tipo);
        for (int i = 0; i < vetor.Length; i++)
            vetor[i] = d.Itens is not null && i < d.Itens.Count
                ? Coagir(Avaliar(d.Itens[i], amb), d.Tipo)
                : zero;
        return vetor;
    }

    /// <summary>
    /// Uma referencia local: o nome novo le e escreve o que ja existe. A
    /// struct vira o mesmo objeto; o numero, um apelido da variavel (ou do
    /// item do vetor) de onde veio.
    /// </summary>
    bool DeclararApelido(CmdDecl d, Ambiente amb)
    {
        var alvo = SemEndereco(d.Inicial);
        if (ApelidoDe(alvo, amb) is { } apelido)
        {
            if (apelido.Ler().Referencia is Objeto mesmo) amb.DeclararObjeto(d.Nome, mesmo);
            else amb.Declarar(d.Nome, Valor.DeReferencia(apelido));
            return true;
        }
        if (Avaliar(alvo, amb).Referencia is Objeto objeto)
        {
            amb.DeclararObjeto(d.Nome, objeto);
            return true;
        }
        return false;
    }

    /// <summary>
    /// O nome de um vetor onde vale um ponteiro: no C ele "decai" para o
    /// endereco do primeiro item. Vetor de char continua sendo texto, que
    /// e como o resto do motor o le.
    /// </summary>
    bool VetorComoPonteiro(Expr e, Ambiente amb, out Valor ponteiro)
    {
        ponteiro = Valor.Vazio;
        if (e is not Variavel v || amb.Buscar(v.Nome, out _) || !amb.BuscarArray(v.Nome, out var vetor)) return false;
        if (vetor.Length > 0 && vetor[0].Caractere || amb.EhVetorDeBytes(v.Nome)) return false;
        ponteiro = Valor.DeReferencia(vetor);
        return true;
    }

    /// <summary>
    /// O vetor de duas dimensoes, esticado linha atras de linha.
    ///
    /// "int m[3][2];" tem SEIS lugares, e nao tres. Cada "{...}" e uma
    /// linha, e a linha curta completa com zeros; num vetor de char, cada
    /// texto e uma linha inteira ("char mapa[16][17] = {"#...#", ...}"),
    /// com o '\0' no fim. Um numero solto fora de chave segue enchendo a
    /// linha da vez, como o C deixa.
    /// </summary>
    Valor[] MontarVetorDeLinhas(CmdDecl d, int linhas, bool deCaractere, Ambiente amb)
    {
        int colunas = Math.Max(1, (int)Avaliar(d.Colunas, amb).Num);
        var zeroDoTipo = ZeroDe(d.Tipo);
        Valor Zero() => zeroDoTipo;

        var itens = new List<Valor>();
        void CompletarLinha()
        {
            while (itens.Count % colunas != 0) itens.Add(Zero());
        }

        foreach (var item in d.Itens ?? [])
        {
            if (item is ListaLiteral linha)
            {
                CompletarLinha();
                if (linha.Itens.Count > colunas)
                    throw new ErroDeExecucao(
                        $"linha com {linha.Itens.Count} itens num vetor de {colunas} colunas", d.Linha);
                foreach (var dentro in linha.Itens) itens.Add(Coagir(Avaliar(dentro, amb), d.Tipo));
                CompletarLinha();
                continue;
            }

            var valor = Avaliar(item, amb);
            if (EhDeBytes(d.Tipo) && valor.EhTexto)
            {
                // O compilador recusa o texto que nao cabe com o '\0':
                // "initializer-string for 'char [5]' is too long".
                var bytes = TextoArduino.ParaBytes(valor.Texto);
                if (bytes.Length >= colunas)
                    throw new ErroDeExecucao(
                        $"o texto \"{valor.Texto}\" tem {bytes.Length} letras e nao cabe numa linha de "
                        + $"{colunas} (sobra lugar para o '\\0' so ate {colunas - 1})", d.Linha);
                CompletarLinha();
                foreach (byte b in bytes) itens.Add(Coagir(deCaractere ? Valor.DeCaractere(b) : Valor.DeInt(b), d.Tipo));
                CompletarLinha();
                if (bytes.Length % colunas == 0) itens.Add(Zero());
                CompletarLinha();
                continue;
            }

            itens.Add(Coagir(valor, d.Tipo));
        }

        CompletarLinha();
        int total = Math.Max(itens.Count, Math.Max(linhas, 0) * colunas);
        while (itens.Count < total) itens.Add(Zero());
        return [.. itens];
    }

    static (double Min, double Max) LimitesDoServo(Objeto servo)
        => (servo.Campos.TryGetValue("_minUs", out var min) ? min.Num : 544,
            servo.Campos.TryGetValue("_maxUs", out var max) ? max.Num : 2400);

    /// <summary>O pulso vira angulo pela mesma conta da Servo.h, dentro dos limites do attach.</summary>
    void EscreverPulso(Objeto servo, double us)
    {
        var (min, max) = LimitesDoServo(servo);
        us = Math.Clamp(us, min, max);
        maq.EscreverServo(servo.Pino, (int)Math.Round((us - min) * 180 / (max - min)), us);
    }

    Valor Coagir(Valor v, string tipo)
    {
        // Objeto, vetor, lista: passam inteiros. Converter um objeto em
        // numero era o que fazia o ponteiro de um objeto chegar zerado na
        // funcao que o recebia.
        if (v.Referencia is not null || tipo is null or "auto" or "funcao*") return v;

        // No C, o numero vai para o tipo da declaracao -- com os bits da
        // placa: "byte b = 300" guarda 44, "int t = millis()" dobra no Uno.
        if (EmC && !v.EhTexto && TiposC.DoNome(tipo, ModeloC) is var tipoC && tipoC != TipoC.Nenhum)
            return ModeloC.ParaOTipo(EhCaractere(tipo) ? Valor.ComoCaractere(v) : v, tipoC);

        if (EhCaractere(tipo)) return Valor.ComoCaractere(v);
        if (tipo == "String") return v.EhTexto ? v : Valor.DeTexto(v.Mostrar());
        if (v.EhTexto) return v;
        bool inteiro = !(tipo.Contains("float") || tipo.Contains("double"));
        return inteiro ? Valor.DeInt(v.Num) : Valor.DeReal(v.Num);
    }

    // ---------------- expressoes ----------------
    Valor Avaliar(Expr e, Ambiente amb)
    {
        // Interrupcao no MEIO da expressao, como no hardware.
        //
        // Antes so era atendida entre comandos, e uma conta como
        // "int total = cliques + digitalWrite(4, HIGH) + cliques;" lia os
        // dois "cliques" iguais mesmo com a borda acontecendo entre eles.
        // No hardware a rotina roda assim que a borda chega, e o segundo
        // "cliques" ja ve o valor novo -- e e exatamente por isso que o
        // Arduino pede "volatile" nessas variaveis.
        //
        // Cortar aqui e seguro: o estado da conta pela metade mora na
        // pilha do C#, e a rotina roda num ambiente proprio, sem mexer
        // nele.
        if (TemAtendimento) AtenderInterrupcoes();

        switch (e)
        {
            case Literal l:
                if (EmC && !l.EhTexto) return LiteralC(l);
                return l.EhTexto ? Valor.DeTexto(l.Texto)
                     : l.Caractere ? Valor.DeCaractere(l.Valor)
                     : l.Inteiro ? Valor.DeInt(l.Valor)
                                 : Valor.DeReal(l.Valor);

            case Pronto pronto:
                return pronto.Valor;

            case Novo novo:
                return AvaliarNovo(novo, amb);

            case Variavel v:
                // Depende da placa, entao nao pode morar no dicionario fixo.
                if (v.Nome == "LED_BUILTIN") return Valor.DeInt(maq.Perfil.LedInterno);
                // O LED RGB da placa (so as DevKits que tem um) e o brilho que o
                // digitalWrite(RGB_BUILTIN, HIGH) do core usa.
                if (v.Nome == "RGB_BUILTIN" && PerfilPlaca.TemLedRgb(maq.Perfil.Id)) return Valor.DeInt(maq.Perfil.LedInterno);
                if (v.Nome == "RGB_BRIGHTNESS" && PerfilPlaca.TemLedRgb(maq.Perfil.Id)) return Valor.DeInt(64);

                // A0..A19 tambem dependem da placa: 14 no Uno, 54 no Mega,
                // 36 na ESP32, 26 na Pico -- o que o pins_arduino.h de cada
                // uma diz. Antes so os Arduino tinham A0.
                if (PerfilPlaca.EhNomeAnalogico(v.Nome))
                {
                    int an = maq.Perfil.PinoDoCore(v.Nome);
                    if (an >= 0) return Valor.DeInt(an);
                }
                // True, False e None. Antes das constantes do Arduino
                // porque "true" e "True" sao palavras diferentes, e o
                // sketch em Python so escreve a segunda.
                if (EhPython && ConstantePython(v.Nome, out var kpy)) return kpy;

                if (Constantes.TryGetValue(v.Nome, out var k)) return k;

                // As do SDK da Pico que nao estao na tabela de cima
                // (GPIO_FUNC_PWM, PWM_CHAN_A). A funcao existia e ninguem
                // a chamava: o exemplo de PWM da Raspberry parava na linha
                // do gpio_set_function.
                if (ConstanteDoPicoSdk(v.Nome, out var doSdk)) return doSdk;
                if (amb.Buscar(v.Nome, out var val))
                {
                    CobrarLeitura(val);
                    return val;
                }

                // "BLECharacteristic::PROPERTY_READ", "ESP_GATT_PERM_READ".
                if (ConstanteDoBle(v.Nome, out var doBle)) return doBle;

                // "this": o objeto do metodo que esta rodando.
                if (v.Nome == "this" && amb.ObjetoAtual() is { } eu) return Valor.DeReferencia(eu);

                // "::x": a global, mesmo com uma local de mesmo nome.
                if (v.Nome.StartsWith("::", StringComparison.Ordinal))
                    return Avaliar(new Variavel(v.Nome[2..], v.Linha), global);

                // "Classe::CONSTANTE", "Cor::VERMELHO".
                if (v.Nome.Contains("::") && NomeComEscopo(v.Nome, amb, out var comEscopo)) return comEscopo;

                // Os objetos do core usados como VALOR: "&Wire" no construtor
                // do display da Adafruit (o exemplo dela comeca assim), o
                // "&Serial" de quem passa a porta para uma biblioteca.
                if (v.Nome is "Wire" or "Wire1" or "SPI" or "SPI1" or "Serial" or "Serial1"
                        or "Serial2" or "SerialUSB" or "SerialBT")
                    return Valor.DeTexto(v.Nome);

                // Uma funcao do programa usada como valor: o callback do
                // MQTT no MicroPython ("set_callback(chegou)"), o ponteiro de
                // funcao do C. Quem recebe o callback le o nome.
                if (funcoes.ContainsKey(v.Nome)) return Valor.DeTexto(v.Nome);

                // O botao BOOTSEL da Pico ("if (BOOTSEL)", do core arduino-pico).
                if (v.Nome == "BOOTSEL" && maq.Perfil.Id is "pi-pico" or "pi-pico-w" && !EhPython)
                    return Valor.DeInt(maq.BootselApertado ? 1 : 0);

                // A data e a hora da compilacao, que o exemplo do RTClib usa
                // para acertar o relogio: "Sep 24 2026" e "14:05:09".
                if (v.Nome is "__DATE__" or "__TIME__")
                    return Valor.DeTexto(v.Nome == "__DATE__"
                        ? Compilado.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture)
                          + " " + Compilado.Day.ToString().PadLeft(2) + " " + Compilado.Year
                        : Compilado.ToString("HH:mm:ss"));

                // Os outros nomes do core da placa: SDA, SCL, TX, o D5 da
                // NodeMCU, o PC13 da Blue Pill. Depois das variaveis do
                // sketch -- que podem ter o mesmo nome -- e so no C++ do
                // Arduino: em MicroPython "SDA" nao existe.
                if ((Prog?.Linguagem ?? Linguagem.Arduino) == Linguagem.Arduino
                    && maq.Perfil.PinoDoCore(v.Nome) is var doCore && doCore >= 0)
                    return Valor.DeInt(doCore);

                // Constante de biblioteca, vetor de char lido como texto,
                // objeto usado como valor ("if (client)").
                if (ValorDeNomeEspecial(v, amb, out var especial)) return especial;

                // O mesmo valor, escrito do jeito de cada linguagem:
                // "Output" em Ada, "OUTPUT" em C++, "Output" em C#.
                // So vale nas linguagens de fora do Arduino, e so
                // DEPOIS de tudo -- uma variavel chamada "high" que o
                // proprio programa declarou continua sendo dele.
                if (Vocabulario.Vale(Prog?.Linguagem ?? Linguagem.Arduino)
                    && Vocabulario.Constante(v.Nome) is { } canonica
                    && Constantes.TryGetValue(canonica, out var traduzida))
                    return traduzida;

                // MALLOC_CAP_* da ESP32; __heap_start, __brkval e SP do AVR.
                if (ConstanteDaMemoria(v.Nome, out var daHeap)) return daHeap;
                if (ConstanteDoSpi(v.Nome, out var doSpi)) return doSpi;
                if (ConstanteDaArduinoBle(v.Nome, out var daArduinoBle)) return daArduinoBle;
                if (v.Nome is "string::npos" or "String::npos") return Valor.DeC(NposDoTexto, TipoC.UInt);
                if (NomeDaMemoriaDoAvr(v.Nome, out var doAvr)) return doAvr;
                if (LerRegistradorDoAvr(v.Nome, out var daPorta)) return daPorta;
                if (BitDoAvr(v.Nome, out var bitDaPorta)) return bitDaPorta;

                if (EhRegistradorDoAvr(v.Nome))
                    throw new ErroDeExecucao(
                        $"'{v.Nome}' e um registrador do chip AVR (ATmega/ATtiny). Este codigo mexe direto "
                        + "no hardware do microcontrolador, e o C++ do simulador nao roda isso -- so o "
                        + "Assembly roda registrador. Se veio de uma biblioteca, ela so funciona na placa.",
                        v.Linha);
                throw new ErroDeExecucao($"variavel '{v.Nome}' nao foi declarada", v.Linha);

            case Indice ix:
            {
                Cobrar(Custos.Indexar);

                // Lista, tupla e dicionario do Python vem primeiro: eles
                // nao moram no Ambiente como vetor, e sim dentro do valor.
                if (EhPython && LerIndicePython(ix, amb, out var doPython)) return doPython;

                // "bot.messages[0].text": a mensagem recebida vira um
                // objeto na hora da leitura.
                if (MensagemDoTelegram(ix, amb, out var doTelegram)) return doTelegram;

                // "this->buffer[i]", "fila.itens[2]": o vetor de dentro de um objeto.
                if (ix.Alvo is Membro doMembro && VetorDoMembro(doMembro, amb) is { } vetorDoObjeto)
                {
                    int noObjeto = (int)Avaliar(ix.Idx, amb).Num;
                    if (noObjeto < 0 || noObjeto >= vetorDoObjeto.Length)
                        throw new ErroDeExecucao(
                            $"indice {noObjeto} fora do vetor '{doMembro.Nome}' (0..{vetorDoObjeto.Length - 1})", ix.Linha);
                    return vetorDoObjeto[noObjeto];
                }

                // "lista[3]" com o operator[] da classe.
                if (IndiceDeClasse(ix, amb, out var doOperador)) return doOperador;

                // "EEPROM[0]": o byte da EEPROM.
                if (ix.Alvo is Variavel { Nome: "EEPROM" } && !amb.BuscarArray("EEPROM", out _) && !amb.BuscarObjeto("EEPROM", out _))
                    return LerEepromNoIndice(ix, amb);

                // "teclas[linha][coluna]", "cubo[i][j][k]": o item; "cubo[1]",
                // o plano -- um pedaco que le e escreve no vetor de verdade.
                if (LerDoVetorDeDimensoes(ix, amb, out var doVetorNd)) return doVetorNd;

                // "msg[0]" numa String: o caractere, como o operator[] do Arduino.
                if (ix.Alvo is Variavel varTexto && !amb.BuscarArray(varTexto.Nome, out _)
                    && amb.Buscar(varTexto.Nome, out var textoIndexado) && textoIndexado.EhTexto)
                {
                    int posicao = (int)Avaliar(ix.Idx, amb).Num;
                    return Valor.DeCaractere(posicao >= 0 && posicao < textoIndexado.Texto.Length
                        ? textoIndexado.Texto[posicao] : 0);
                }

                if (ix.Alvo is not Variavel av || !amb.BuscarArray(av.Nome, out var arr))
                {
                    if (ix.Alvo is not Variavel)
                    {
                        var alvoAvaliado = Avaliar(ix.Alvo, amb);

                        // "criar()[2]", "(*p)[1]": o vetor que uma expressao devolve.
                        if (alvoAvaliado.Referencia is Valor[] devolvido)
                        {
                            int noDevolvido = (int)Avaliar(ix.Idx, amb).Num;
                            if (noDevolvido < 0 || noDevolvido >= devolvido.Length)
                                throw new ErroDeExecucao($"indice {noDevolvido} fora do vetor (0..{devolvido.Length - 1})", ix.Linha);
                            return devolvido[noDevolvido];
                        }

                        // "linhas[y][x]" num vetor de const char*, "nome()[0]":
                        // o caractere do texto que a expressao devolve. Depois
                        // do ultimo vem o '\0', como na memoria da placa.
                        if (alvoAvaliado.EhTexto)
                        {
                            int noTexto = (int)Avaliar(ix.Idx, amb).Num;
                            return Valor.DeCaractere(noTexto >= 0 && noTexto < alvoAvaliado.Texto.Length
                                ? alvoAvaliado.Texto[noTexto] : 0);
                        }
                    }
                    throw new ErroDeExecucao("indexacao so funciona em vetores", ix.Linha);
                }
                int i = (int)Avaliar(ix.Idx, amb).Num;

                // "Serial.println(nomes[2])" num "char nomes[4][10]": a
                // linha inteira, lida ate o '\0' -- e nao a terceira letra.
                if (amb.ColunasDe(av.Nome, out int largura) && largura > 0 && arr.Length > 0 && arr[0].Caractere)
                {
                    if (i < 0 || (i + 1) * largura > arr.Length)
                        throw new ErroDeExecucao(
                            $"linha {i} fora do vetor '{av.Nome}' (0..{arr.Length / largura - 1})", ix.Linha);
                    return Valor.DeTexto(TextoDoVetor(arr[(i * largura)..((i + 1) * largura)]));
                }

                if (i < 0 || i >= arr.Length)
                    throw new ErroDeExecucao(
                        $"indice {i} fora do vetor '{av.Nome}' (0..{arr.Length - 1})", ix.Linha);
                return arr[i];
            }

            case Unaria u:
            {
                if (u.Op is "++" or "--")
                {
                    var atual = Avaliar(u.Alvo, amb);
                    var novo = SomarUm(atual, u.Op == "++" ? 1 : -1);
                    CobrarIncremento(novo);
                    Guardar(u.Alvo, novo, amb);
                    return novo;
                }
                var x = Avaliar(u.Alvo, amb);
                if (EmC && u.Op is "-" or "~" or "+" && EhNumeroDeC(x)) return UnariaC(u.Op, x, u.Linha);
                return u.Op switch
                {
                    "-" => x.Inteiro ? Valor.DeInt(-x.Num) : Valor.DeReal(-x.Num),
                    "+" => x,
                    "!" => Valor.DeBool(!x.Verdadeiro),
                    // Sem ponteiro aqui: "&x" e o proprio x.
                    "&" => x,

                    // "*p": o apontado. Num vetor, o primeiro item -- "*buffer"
                    // e "buffer[0]" sao o mesmo no C.
                    "*" => x.Referencia is Valor[] { Length: > 0 } apontado ? apontado[0] : x,
                    "~" => Valor.DeInt(~(long)x.Num),
                    _ => throw new ErroDeExecucao($"operador '{u.Op}' nao suportado", u.Linha)
                };
            }

            case PosFixa pf:
            {
                var antes = Avaliar(pf.Alvo, amb);
                var depois = SomarUm(antes, pf.Op == "++" ? 1 : -1);
                CobrarIncremento(depois);
                Guardar(pf.Alvo, depois, amb);
                return antes;
            }

            case Binaria b:
                return Operar(b, amb);

            case Ternaria t:
            {
                var escolhido = Avaliar(t.Cond, amb).Verdadeiro ? t.Sim : t.Nao;
                return VetorComoPonteiro(escolhido, amb, out var doVetor) ? doVetor : Avaliar(escolhido, amb);
            }

            // "p = {1, 2};": a struct recebe os campos na ordem, no lugar
            // dela -- quem apontava para ela ve os valores novos.
            case Atribuicao { Op: "=", Valor: ListaLiteral chaves } ac when !EhPython:
            {
                if (Avaliar(ac.Alvo, amb).Referencia is Objeto alvoDaLista && estruturas.ContainsKey(alvoDaLista.Classe))
                {
                    var montada = NovaEstrutura(alvoDaLista.Classe, chaves.Itens, amb);
                    foreach (var (campoDaLista, valorDaLista) in montada.Campos)
                        alvoDaLista.Campos[campoDaLista] = valorDaLista;
                    return Valor.DeReferencia(alvoDaLista);
                }
                throw new ErroDeExecucao("atribuir uma lista entre chaves so vale para uma struct", ac.Linha);
            }

            case Atribuicao a:
            {
                var novo = a.Op == "="
                    ? (VetorComoPonteiro(a.Valor, amb, out var apontado) ? apontado : Avaliar(a.Valor, amb))
                    : Operar(new Binaria(a.Op[..^1], a.Alvo, a.Valor, a.Linha), amb);
                CobrarLeitura(novo);

                // Uma String contada: o texto novo tem que caber na heap. O
                // "+=" que nao coube deixa a String como estava; o "=", vazia.
                if (novo.EhTexto && a.Alvo is Variavel textoAlvo)
                    novo = TextoNovo(textoAlvo.Nome, novo, amb, a.Op == "+=", a.Linha);
                Guardar(a.Alvo, novo, amb);
                return novo;
            }

            case Chamada c:
                return Invocar(c, amb);

            // "Pin.OUT", "network.STA_IF", "resultado.campo" do Python.
            case Membro mp when EhPython && MembroPython(mp, amb, out var doModulo):
                return doModulo;

            // "botao.contagem", "this->_pino", "sensor->leitura".
            case Membro mu when LerMembroDeClasse(mu, amb, out var deClasse):
                return deClasse;

            // Campo de campo: "IrReceiver.decodedIRData.decodedRawData".
            // Um objeto pode guardar outro dentro de um campo, e a
            // leitura desce a corrente inteira.
            case Membro mc when ObjetoDaExpressao(mc.Obj, amb) is { } dentro
                                && dentro.Campos.TryGetValue(mc.Nome, out var doCampo):
                return doCampo;

            // "results.value": leitura de campo de um objeto. Existe por
            // causa do infravermelho, cujo jeito classico de usar e
            // "if (irrecv.decode(&results)) ... results.value".
            case Membro m when m.Obj is Variavel dono
                               && amb.BuscarObjeto(dono.Nome, out var alvo)
                               && alvo.Campos.TryGetValue(m.Nome, out var campo):
                return campo;

            case Membro m2 when m2.Obj is Variavel dono2
                                && amb.BuscarObjeto(dono2.Nome, out var alvo2):
                throw new ErroDeExecucao(
                    $"'{dono2.Nome}' nao tem o campo '{m2.Nome}'. "
                    + (alvo2.Campos.Count == 0
                       ? "Este objeto nao expoe campos."
                       : "Tem: " + string.Join(", ", alvo2.Campos.Keys)), m2.Linha);

            case Membro m3:
                throw new ErroDeExecucao($"'{m3.Nome}' nao pode ser lido assim", m3.Linha);

            case ListaLiteral ll:
                return MontarLista(ll, amb);

            case DicionarioLiteral dl:
                return MontarDicionario(dl, amb);

            case Fatia ft:
                return Fatiar(ft, amb);

            // Argumento com nome avaliado fora de uma chamada: so pode
            // ser "a = b" escrito onde cabia uma expressao.
            case ArgNomeado an:
                return Avaliar(an.Valor, amb);

            case Conversao cv:
                return Converter(cv, amb);

            // Funcao anonima guardada ou passada para uma funcao do programa:
            // "botao.aoApertar([]() { ... })".
            case FuncaoAnonima fa:
                return Valor.DeReferencia(new Rotina { Anonima = fa, Captura = amb });

            default:
                throw new ErroDeExecucao($"expressao nao suportada: {e.GetType().Name}", e.Linha);
        }
    }

    /// <summary>
    /// A biblioteca SD: "SD.begin(5)", "SD.open(...)", "SD.exists(...)".
    ///
    /// "SD" nao e declarado no sketch -- ele ja existe, como o "Serial".
    /// Por isso e atendido antes da busca por objeto, que falharia com
    /// "variavel 'SD' nao declarada".
    /// </summary>
    Valor ChamarCartao(string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string Texto(int i) => i < args.Count ? args[i].Mostrar() : "";

        // Na ESP32 o SD e um sistema de arquivos do ESP-IDF, e o caminho
        // comeca com "/": sem a barra a biblioteca recusa (open devolve um
        // arquivo invalido, exists e remove devolvem false). No Uno e no
        // ESP8266 a barra e opcional. Aceitar aqui sem ela era o sketch
        // que funcionava na tela e nao abria arquivo nenhum na bancada.
        bool CaminhoRecusado(string caminho)
        {
            if (!EhDaFamiliaEsp32 || caminho.StartsWith('/')) return false;
            maq.Avisar($"SD.{metodo}(\"{caminho}\"): na ESP32 o caminho do cartao comeca com \"/\" -- "
                       + $"escreva \"/{caminho}\". Sem a barra a biblioteca recusa, e o arquivo nao abre.");
            return true;
        }

        switch (metodo)
        {
            case "begin":
            {
                // O cartao responde no CS do begin() (o SS da placa, sem
                // argumento), pelos pinos do barramento: o da ESP32 pode ser
                // outro ("SD.begin(5, hspi)"). A biblioteca liga o SPI.
                var barramento = args.Count > 1 ? BarramentoDoArgumento(args[1]) : BarramentoGlobal("SPI");
                int cs = args.Count > 0 ? (int)args[0].Num : barramento.Ss;
                if (maq.Cartao.Presente && ProblemaNaLigacaoDoSd(cs, barramento) is { } problema)
                {
                    string ligacao = $"SD.begin() devolveu false: {problema} -- na placa o cartao nao responde."
                                     + (problema.StartsWith("o CS", StringComparison.Ordinal)
                                        ? " O numero do begin() e o pino onde o fio do CS esta."
                                        : DicaDosPinosDoSpi(barramento));
                    lock (maq.Trava)
                        if (!maq.Avisos.Contains(ligacao)) maq.Avisos.Add(ligacao);
                    return Valor.DeBool(false);
                }
                if (maq.Cartao.Presente && !barramento.Iniciado && barramento.Sck >= 0 && barramento.Mosi >= 0)
                {
                    barramento.Iniciado = true;
                    maq.PinMode(barramento.Sck, 1);
                    maq.PinMode(barramento.Mosi, 1);
                    if (barramento.Miso >= 0) maq.PinMode(barramento.Miso, 0);
                }

                if (!maq.Cartao.Montar())
                {
                    const string semCartao =
                        "SD.begin() devolveu false: nao ha cartao microSD ligado nesta placa. "
                        + "Ponha a peca no circuito e ligue o CS, o SCK, o MOSI, o MISO "
                        + "e a alimentacao dela.";

                    lock (maq.Trava)
                        if (!maq.Avisos.Contains(semCartao)) maq.Avisos.Add(semCartao);

                    return Valor.DeBool(false);
                }

                lock (maq.Trava)
                    if (!maq.Avisos.Contains(AvisoDoCartao)) maq.Avisos.Add(AvisoDoCartao);
                return Valor.DeBool(true);
            }

            case "end": maq.Cartao.Desmontar(); return Valor.Vazio;
            case "exists": return Valor.DeBool(!CaminhoRecusado(Texto(0)) && maq.Cartao.Existe(Texto(0)));
            case "remove": return Valor.DeBool(!CaminhoRecusado(Texto(0)) && maq.Cartao.Remover(Texto(0)));
            case "mkdir": return Valor.DeBool(!CaminhoRecusado(Texto(0)) && maq.Cartao.CriarPasta(Texto(0)));
            case "rmdir": return Valor.DeBool(!CaminhoRecusado(Texto(0)) && maq.Cartao.RemoverPasta(Texto(0)));
            case "rename": return Valor.DeBool(!CaminhoRecusado(Texto(0)) && maq.Cartao.Renomear(Texto(0), Texto(1)));

            case "open":
            {
                if (CaminhoRecusado(Texto(0)))
                {
                    arquivoRecemAberto = null;
                    return Valor.DeBool(false);
                }

                // O modo de cada plataforma. Na ESP32 o FILE_WRITE e o "w"
                // do C, que COMECA O ARQUIVO DO ZERO -- quem quer continuar
                // escrevendo usa FILE_APPEND ("a"). No Uno e no ESP8266 o
                // FILE_WRITE escreve no fim. Sem 2o argumento e leitura.
                string modo = "r";
                if (c.Args.Count > 1)
                {
                    string escrito = c.Args[1] is Variavel constante ? constante.Nome : args[1].Mostrar();
                    modo = escrito switch
                    {
                        "FILE_READ" => "r",
                        "FILE_APPEND" => "a",
                        "FILE_WRITE" => EhDaFamiliaEsp32 ? "w" : "a",
                        _ when args[1].EhTexto => escrito.Contains('a') ? "a" : escrito.Contains('w') ? "w"
                                                  : escrito.Contains('+') ? "a" : "r",
                        _ => args[1].Num > 0 ? (EhDaFamiliaEsp32 ? "w" : "a") : "r",
                    };
                }
                bool escrever = modo != "r";

                var arquivo = maq.Cartao.Abrir(Texto(0), escrever, truncar: modo == "w");

                // O sketch guarda em "File dados = SD.open(...)" e testa
                // com "if (!dados)". Devolver o proprio objeto nao cabe
                // num Valor, entao o arquivo aberto fica pendurado e o
                // valor diz so se deu certo.
                arquivoRecemAberto = arquivo;
                return Valor.DeBool(arquivo is not null);
            }
        }

        throw new ErroDeExecucao(
            $"SD.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    const string AvisoDoCartao =
        "cartao microSD: o que o sketch grava fica na peca do cartao quando a simulacao para "
        + "(e vai junto no .sktch). Dois cliques na peca mostram os arquivos.";

    /// <summary>
    /// O ultimo arquivo que SD.open() devolveu, esperando para ser
    /// amarrado ao objeto File da declaracao.
    /// </summary>
    ArquivoSd arquivoRecemAberto;

    /// <summary>
    /// "irrecv.decode(&results)": diz se chegou codigo e, se chegou,
    /// preenche o struct que o sketch passou.
    /// </summary>
    Valor DecodificarIr(Chamada c, Ambiente amb)
    {
        double codigo = maq.Leitura("ir:codigo");
        if (codigo <= 0) return Valor.DeBool(false);

        if (c.Args.Count > 0
            && SemEndereco(c.Args[0]) is Variavel v
            && amb.BuscarObjeto(v.Nome, out var saida))
        {
            saida.Campos["value"] = Valor.DeInt(codigo);
            saida.Campos["command"] = Valor.DeInt((long)codigo & 0xFF);
            saida.Campos["decode_type"] = Valor.DeInt(1);
            saida.Campos["bits"] = Valor.DeInt(32);
        }
        return Valor.DeBool(true);
    }

    /// <summary>
    /// O sensors_event_t da Adafruit Unified Sensor. Na biblioteca o vetor e
    /// uma union: acceleration, gyro, magnetic e orientation sao a MESMA
    /// memoria -- aqui, o mesmo objeto.
    /// </summary>
    static void MontarEventoDeSensor(Objeto evento)
    {
        var vetor = new Objeto("sensors_vec_t");
        foreach (string eixo in new[] { "x", "y", "z", "roll", "pitch", "heading" }) vetor.Campos[eixo] = Valor.DeReal(0);
        foreach (string nome in new[] { "acceleration", "gyro", "magnetic", "orientation" })
            evento.Campos[nome] = Valor.DeReferencia(vetor);
        evento.Campos["temperature"] = Valor.DeReal(0);
        evento.Campos["version"] = Valor.DeInt(1);
        evento.Campos["sensor_id"] = Valor.DeInt(0);
        evento.Campos["type"] = Valor.DeInt(0);
        evento.Campos["timestamp"] = Valor.DeInt(0);
    }

    /// <summary>
    /// "mpu.getEvent(&a, &g, &temp)" da Adafruit_MPU6050: a aceleracao em
    /// m/s2, a rotacao em rad/s e a temperatura em graus, das leituras da
    /// peca (as cruas sao as do registrador: 16384 por g, 131 por grau/s).
    /// </summary>
    Valor EventoDoMpu(Chamada c, Ambiente amb)
    {
        const double G = 9.80665, Rad = Math.PI / 180;
        Objeto Alvo(int k)
        {
            if (k >= c.Args.Count || SemEndereco(c.Args[k]) is not Variavel v || !amb.BuscarObjeto(v.Nome, out var o)) return null;
            if (!o.Campos.ContainsKey("acceleration")) MontarEventoDeSensor(o);
            return o;
        }
        void Vetor(Objeto evento, double x, double y, double z, int tipo)
        {
            var vetor = (Objeto)evento.Campos["acceleration"].Referencia;
            vetor.Campos["x"] = Valor.DeReal(x);
            vetor.Campos["y"] = Valor.DeReal(y);
            vetor.Campos["z"] = Valor.DeReal(z);
            evento.Campos["type"] = Valor.DeInt(tipo);
            evento.Campos["timestamp"] = Valor.DeInt(maq.Ms);
        }

        if (Alvo(0) is { } a)
            Vetor(a, maq.Leitura("mpu:ax") / 16384.0 * G, maq.Leitura("mpu:ay") / 16384.0 * G,
                  maq.Leitura("mpu:az", 16384) / 16384.0 * G, 1);
        if (Alvo(1) is { } g)
            Vetor(g, maq.Leitura("mpu:gx") / 131.0 * Rad, maq.Leitura("mpu:gy") / 131.0 * Rad,
                  maq.Leitura("mpu:gz") / 131.0 * Rad, 4);
        if (Alvo(2) is { } t)
        {
            double graus = maq.Leitura("mpu:temp", 25);
            Vetor(t, graus, 0, 0, 13);
            t.Campos["temperature"] = Valor.DeReal(graus);
        }
        return Valor.DeBool(true);
    }

    /// <summary>
    /// "&x" devolve o proprio x: nao ha ponteiro aqui. Quem precisa do
    /// objeto apontado olha o argumento sem avaliar.
    /// </summary>
    static Expr SemEndereco(Expr e) => e is Unaria { Op: "&" } u ? u.Alvo : e;

    /// <summary>
    /// O objeto por tras de uma expressao, descendo a corrente de campos.
    ///
    /// Resolve tanto "results" quanto "IrReceiver.decodedIRData": o
    /// segundo e um objeto guardado DENTRO do campo de outro, que e como
    /// a biblioteca nova do infravermelho expoe o resultado.
    /// </summary>
    Objeto ObjetoDaExpressao(Expr e, Ambiente amb)
    {
        e = SemEndereco(e);
        if (e is Unaria { Op: "*" } apontado) return ObjetoDaExpressao(apontado.Alvo, amb);

        if (e is Variavel v)
        {
            if (v.Nome == "IrReceiver") return ReceptorIr;
            return amb.BuscarObjeto(v.Nome, out var achado) ? achado : null;
        }

        if (e is Membro m)
        {
            var dono = ObjetoDaExpressao(m.Obj, amb);
            if (dono is not null && dono.Campos.TryGetValue(m.Nome, out var campo))
                return (campo.Referencia is Apelido a ? a.Ler() : campo).Referencia as Objeto;

            // O objeto de dentro de um objeto de classe: "robo.motor.girar()".
            if (dono?.Escopo is not null && dono.Escopo.BuscarObjetoLocal(m.Nome, out var dentro))
                return dentro;
            return null;
        }

        // "botoes[1].ler()", "fabrica().x", "(*p).x": o objeto que a
        // expressao devolve.
        if (e is Indice or Chamada or Novo or Pronto)
            return Avaliar(e, amb).Referencia as Objeto;

        return null;
    }

    Objeto receptorIr;

    /// <summary>
    /// O "IrReceiver" global da IRremote.hpp.
    ///
    /// Nasce na primeira vez que o sketch o usa, com o
    /// "decodedIRData" dentro -- que e onde a biblioteca poe o codigo
    /// recebido e de onde o sketch o le.
    /// </summary>
    Objeto ReceptorIr
    {
        get
        {
            if (receptorIr is not null) return receptorIr;

            receptorIr = new Objeto("IRremote");
            var dados = new Objeto("IRData");
            foreach (string campo in new[]
                     { "decodedRawData", "command", "address", "protocol", "numberOfBits", "flags" })
                dados.Campos[campo] = Valor.DeInt(0);

            receptorIr.Campos["decodedIRData"] = Valor.DeReferencia(dados);
            return receptorIr;
        }
    }

    /// <summary>
    /// O mesmo codigo, no formato que a biblioteca NOVA reporta.
    ///
    /// As duas bibliotecas de infravermelho mostram o MESMO botao com
    /// numeros diferentes, e isso confunde quem troca de uma para a
    /// outra. A antiga (IRrecv) entrega o NEC como 0x00FFA25D; a nova
    /// (IRremote.hpp, decodedRawData) entrega os mesmos quatro bytes na
    /// ordem inversa: 0x5DA2FF00.
    ///
    /// O sketch de controle remoto da lista compara com 0x5DA2FF00 --
    /// se a tabela do simulador respondesse no formato antigo, nenhum
    /// botao bateria e todos cairiam no "Unknown button".
    /// </summary>
    static long ComoRawData(long codigo)
    {
        long cheio = codigo & 0xFFFFFFFF;

        return ((cheio & 0x000000FF) << 24)
             | ((cheio & 0x0000FF00) << 8)
             | ((cheio & 0x00FF0000) >> 8)
             | ((cheio & 0xFF000000) >> 24);
    }

    /// <summary>
    /// A API nova do infravermelho: IrReceiver.begin(), .decode(), .resume().
    ///
    /// A leitura e a mesma do IRrecv antigo -- o codigo vem do controle
    /// que esta no circuito, por Maquina.Leituras. O que muda e so o
    /// caminho pelo qual o sketch o pega.
    /// </summary>
    Valor ChamarReceptorIr(string metodo, Chamada c, Ambiente amb)
    {
        var dados = ReceptorIr.Campos["decodedIRData"].Referencia as Objeto;

        switch (metodo)
        {
            case "begin": case "start": case "enableIRIn":
            case "end": case "stop": case "restartTimer":
            case "printIRResultShort": case "printIRSendUsage":
                foreach (var a in c.Args) _ = a;
                return Valor.Vazio;

            case "decode":
            {
                double codigo = maq.Leitura("ir:codigo");
                if (codigo <= 0) return Valor.DeBool(false);

                long cru = ComoRawData((long)codigo);

                dados.Campos["decodedRawData"] = Valor.DeInt(cru);
                dados.Campos["command"] = Valor.DeInt(((long)codigo >> 8) & 0xFF);
                dados.Campos["address"] = Valor.DeInt(((long)codigo >> 24) & 0xFF);
                dados.Campos["protocol"] = Valor.DeInt(8);
                dados.Campos["numberOfBits"] = Valor.DeInt(32);
                return Valor.DeBool(true);
            }

            case "resume":
                // O controle solta o botao: o proximo decode() so volta a
                // ver alguma coisa se a pessoa apertar de novo.
                maq.Leituras["ir:codigo"] = 0;
                return Valor.Vazio;

            case "getProtocolString": return Valor.DeTexto("NEC");
            case "isIdle": return Valor.DeBool(maq.Leitura("ir:codigo") <= 0);
        }

        throw new ErroDeExecucao(
            $"IrReceiver.{metodo}() ainda nao e suportado pelo simulador", c.Linha);
    }

    Valor Operar(Binaria b, Ambiente amb)
    {
        // curto-circuito
        if (b.Op is "&&" or "||") Cobrar(Custos.Comando);
        if (b.Op == "&&")
            return Valor.DeBool(Avaliar(b.Esq, amb).Verdadeiro && Avaliar(b.Dir, amb).Verdadeiro);
        if (b.Op == "||")
            return Valor.DeBool(Avaliar(b.Esq, amb).Verdadeiro || Avaliar(b.Dir, amb).Verdadeiro);

        var x = Avaliar(b.Esq, amb);
        var y = Avaliar(b.Dir, amb);

        // As contas em que as duas linguagens divergem: divisao, resto,
        // "%" de texto, lista somada com lista.
        if (EhPython && OperarPython(b.Op, x, y, b.Linha, out var doPython)) return doPython;

        // operator== e companhia de uma classe, e ponteiro comparado com ponteiro.
        if ((x.Referencia is not null || y.Referencia is not null)
            && OperarObjetos(b.Op, x, y, b.Linha, out var deObjeto))
            return deObjeto;

        if (b.Op == "+" && (x.EhTexto || y.EhTexto))
            return Valor.DeTexto(x.Mostrar() + y.Mostrar());

        // A conta do C, com os tipos da placa: o int de 16 bits do Uno
        // estoura, a divisao por zero faz o que o chip faz.
        if (EmC && (x.Tipo != TipoC.Nenhum || y.Tipo != TipoC.Nenhum) && EhNumeroDeC(x) && EhNumeroDeC(y))
            return OperarC(b.Op, x, y, b.Linha);

        bool inteiro = x.Inteiro && y.Inteiro;
        CobrarOperacaoSemTipo(b.Op, inteiro);
        double a = x.Num, c = y.Num;

        switch (b.Op)
        {
            case "+": return Num(a + c, inteiro);
            case "-": return Num(a - c, inteiro);
            case "*": return Num(a * c, inteiro);
            case "/":
                if (c == 0) throw new ErroDeExecucao("divisao por zero", b.Linha);
                // Divisao inteira quando ambos sao inteiros: pega estudante desprevenido.
                return inteiro ? Valor.DeInt(Math.Truncate(a / c)) : Valor.DeReal(a / c);
            case "%":
                if (c == 0) throw new ErroDeExecucao("resto por zero", b.Linha);
                return Valor.DeInt((long)a % (long)c);
            case "==": return Valor.DeBool(x.EhTexto || y.EhTexto ? x.Mostrar() == y.Mostrar() : a == c);
            case "!=": return Valor.DeBool(x.EhTexto || y.EhTexto ? x.Mostrar() != y.Mostrar() : a != c);
            case "<": return Valor.DeBool(a < c);
            case ">": return Valor.DeBool(a > c);
            case "<=": return Valor.DeBool(a <= c);
            case ">=": return Valor.DeBool(a >= c);
            case "&": return Valor.DeInt((long)a & (long)c);
            case "|": return Valor.DeInt((long)a | (long)c);
            case "^": return Valor.DeInt((long)a ^ (long)c);
            case "<<": return Valor.DeInt((long)a << (int)c);
            case ">>": return Valor.DeInt((long)a >> (int)c);
            default: throw new ErroDeExecucao($"operador '{b.Op}' nao suportado", b.Linha);
        }
    }

    static Valor Num(double n, bool inteiro) => inteiro ? Valor.DeInt(n) : Valor.DeReal(n);

    static readonly System.Text.RegularExpressions.Regex RegistradorDoAvr = new(
        @"^(TCCR\d[A-C]?|TIMSK\d|TIFR\d|OCR\d[A-C]?[HL]?|ICR\d[HL]?|TCNT\d[HL]?|PORT[A-L]|DDR[A-L]|PIN[A-L]"
        + @"|ADCSR[AB]|ADMUX|ADC[HL]?|SREG|EICR[AB]|EIMSK|EIFR|PCICR|PCIFR|PCMSK\d|UCSR\d[A-C]|UBRR\d[HL]?"
        + @"|UDR\d|SPCR|SPSR|SPDR|TWBR|TWCR|TWDR|TWSR|TWAR|EECR|EEDR|EEAR[HL]?|MCUSR|MCUCR|WDTCSR|SMCR|PRR"
        + @"|GTCCR|ASSR|DIDR[01]|ACSR|OSCCAL|CLKPR|GIMSK|GIFR|TIMSK|TIFR|USICR|USISR|USIDR)$");

    static bool EhRegistradorDoAvr(string nome) => RegistradorDoAvr.IsMatch(nome);

    /// <summary>Onde o relogio estava na ultima vez que a contagem de passos recomecou.</summary>
    double relogioDosPassos;

    /// <summary>
    /// millis() e micros().
    ///
    /// Ler o relogio custa tempo -- no Uno, o micros() gasta uns 3,5 us --,
    /// e no tempo virtual isso agora e cobrado. Sem cobrar, um laco que
    /// espera olhando o micros() (o step() da Stepper, o tempo limite de
    /// todo sensor) ficava parado no tempo para sempre: o relogio virtual
    /// so anda quando alguem o empurra.
    ///
    /// E um laco que espera o tempo passar nao esta preso: a cada
    /// milissegundo que passa, a contagem de passos recomeca, como num
    /// delay(). Sem isso, o motor de passo girando uma volta inteira
    /// estourava o limite no meio.
    /// </summary>
    Valor LerRelogio(bool micro)
    {
        // Ler o relogio custa os ciclos do core (o micros() do Uno, ~3,5 us).
        // Sem a CPU simulada, o custo antigo de cada leitura no tempo virtual.
        if (RitmoDaCpu)
        {
            Cobrar(micro ? Custos.Micros : Custos.Millis);
            PagarCiclos();
        }
        else if (!maq.TempoReal)
            maq.AvancarMicros(Math.Max(1, (long)Math.Round(maq.Perfil?.CustoDoPinoUs ?? 1)));

        double agora = maq.TempoObservado();
        if (agora - relogioDosPassos >= 1000)
        {
            passos = 0;
            relogioDosPassos = agora;
        }

        // unsigned long, como no core: o millis() volta a zero depois de
        // 49,7 dias, e o micros() depois de 71 minutos -- na placa tambem.
        return micro ? DoTipo((long)agora, TipoC.ULong)
                     : DoTipo(RitmoDaCpu ? (long)(agora / 1000) : maq.Ms, TipoC.ULong);
    }

    void Guardar(Expr alvo, Valor v, Ambiente amb)
    {
        // Em Python a atribuicao DECLARA, e o destino pode ser item de
        // lista ou chave de dicionario -- nada disso existe no C.
        if (EhPython && GuardarPython(alvo, v, amb)) return;

        switch (alvo)
        {
            case Variavel va:
                if (amb.Atribuir(va.Nome, v)) break;

                // "a = b;" entre dois objetos de classe: copia (ou o operator=).
                if (AtribuirObjetoDeClasse(va.Nome, v, amb)) break;

                // "::total = 0;" e "Botao::contador = 0;"
                if (va.Nome.StartsWith("::", StringComparison.Ordinal) && global.Atribuir(va.Nome[2..], v)) break;
                if (va.Nome.Contains("::") && AtribuirComEscopo(va.Nome, v)) break;

                // "client = server.available();": o objeto ja existe e
                // recebe a conexao nova.
                if (RebindarObjeto(va, v, amb)) break;

                // "PORTB |= _BV(PB5);": cada bit vira um pino.
                if (EscreverRegistradorDoAvr(va.Nome, v)) break;
                if (EhRegistradorDoAvr(va.Nome)) Avaliar(va, amb);
                throw new ErroDeExecucao($"variavel '{va.Nome}' nao foi declarada", va.Linha);

            // "*p = 5": o apontado. Num vetor, o primeiro item.
            case Unaria { Op: "*" } apontado:
                if (Avaliar(apontado.Alvo, amb).Referencia is Valor[] { Length: > 0 } vetorApontado)
                    vetorApontado[0] = NoLugar(vetorApontado[0], v);
                else Guardar(apontado.Alvo, v, amb);
                break;

            // "this->buffer[i] = x"
            case Indice ixm when ixm.Alvo is Membro membroDoVetor && VetorDoMembro(membroDoVetor, amb) is { } vetorDoObjeto:
            {
                int k = (int)Avaliar(ixm.Idx, amb).Num;
                if (k < 0 || k >= vetorDoObjeto.Length)
                    throw new ErroDeExecucao(
                        $"indice {k} fora do vetor '{membroDoVetor.Nome}' (0..{vetorDoObjeto.Length - 1})", ixm.Linha);
                vetorDoObjeto[k] = NoLugar(vetorDoObjeto[k], v);
                break;
            }

            // "EEPROM[0] = 5": escreve na EEPROM.
            case Indice ixe when ixe.Alvo is Variavel { Nome: "EEPROM" } && !amb.BuscarArray("EEPROM", out _) && !amb.BuscarObjeto("EEPROM", out _):
                EscreverEepromNoIndice(ixe, v, amb);
                break;

            case Indice ix2 when PosicaoNoVetor(ix2, amb, out var vetorNd, out int plano, out var faltam, out string nomeNd):
                if (faltam.Length > 0)
                    throw new ErroDeExecucao($"'{nomeNd}' tem mais dimensoes: falta indice para chegar num item "
                                             + "(no C++, \"array type is not assignable\")", ix2.Linha);
                if (plano < 0 || plano >= vetorNd.Length)
                    throw new ErroDeExecucao($"indice fora do vetor '{nomeNd}' (ele tem {vetorNd.Length} itens ao todo)", ix2.Linha);
                vetorNd[plano] = NoLugar(vetorNd[plano], v);
                break;

            case Indice ix when ix.Alvo is Variavel av && amb.BuscarArray(av.Nome, out var arr):
            {
                int i = (int)Avaliar(ix.Idx, amb).Num;
                if (i < 0 || i >= arr.Length)
                    throw new ErroDeExecucao(
                        $"indice {i} fora do vetor '{av.Nome}' (0..{arr.Length - 1})", ix.Linha);
                // Vetor de char continua de char: "msg[0] = 65" guarda 'A'.
                arr[i] = NoLugar(arr[i], v);
                break;
            }

            // Campo de objeto de classe: "this->_pino = p", "sensor->x = 1".
            case Membro mu when GuardarMembroDeClasse(mu, v, amb):
                break;

            // "linha.a.lat = 12": o dono do campo e outro objeto, e nao
            // uma variavel. Descer a corrente inteira e o que permite
            // struct dentro de struct.
            case Membro mc when mc.Obj is not Variavel
                                && ObjetoDaExpressao(mc.Obj, amb) is { } dentro:
                dentro.Campos[mc.Nome] = dentro.Campos.TryGetValue(mc.Nome, out var velho) ? NoLugar(velho, v) : v;
                break;

            case Membro mb when mb.Obj is Variavel dono && amb.BuscarObjeto(dono.Nome, out var alvoObj):
                alvoObj.Campos[mb.Nome] = !alvoObj.Campos.TryGetValue(mb.Nome, out var antes) ? v
                    : antes.EhTexto && !v.EhTexto ? Valor.DeTexto(v.Mostrar())
                    : NoLugar(antes, v);
                break;

            default:
                throw new ErroDeExecucao("destino de atribuicao invalido", alvo.Linha);
        }
    }


    /// <summary>
    /// O executor de Assembly desta placa, criado na primeira vez.
    ///
    /// Preguicoso porque a esmagadora maioria dos programas nao e em
    /// Assembly, e montar registradores e pilha para eles seria gastar
    /// memoria a toa.
    /// </summary>
    Avr avr;

    /// <summary>
    /// O Assembly só roda na placa em que ele foi validado.
    ///
    /// Os endereços de E/S, a ligação de cada porta com cada pino e o
    /// mapa da memória são os do ATmega328P -- o chip do Uno e do Nano.
    /// No ATtiny85 o PORTB mora em outro endereço; no Mega o pino 13 é
    /// o bit 7 do PORTB, e não o 5; e numa ESP32, numa Pico ou numa
    /// STM32 o conjunto de instruções nem é AVR.
    ///
    /// Rodar mesmo assim seria exatamente o defeito que um simulador
    /// não pode ter: funcionar na tela e não funcionar na placa. Então
    /// ele recusa, e diz em qual placa roda.
    /// </summary>
    void ExigirAtmega328()
    {
        string chip = maq.Perfil?.Processador ?? "?";
        if (chip == "ATmega328P") return;

        string porque = chip switch
        {
            "ATtiny85" or "ATmega2560" =>
                $"o {chip} também é AVR, mas os endereços de E/S e a ligação das portas "
                + "com os pinos são outros -- o programa acenderia o pino errado",
            _ => $"o {chip} não é AVR: ele tem outro conjunto de instruções",
        };

        throw new ErroDeExecucao(
            $"o Assembly está validado para o ATmega328P (Arduino Uno e Nano); "
            + $"na {maq.Perfil?.Nome ?? "placa"}, {porque}.", 1);
    }

    /// <summary>
    /// O programa marca o próprio ritmo, e quem o roda não deve dormir.
    ///
    /// É o Assembly: ele espera o relógio de parede alcançar o tempo do
    /// chip dentro do próprio passo. O laço que chama o loop() dorme 1 ms
    /// quando a volta foi rápida -- para um sketch com millis() não girar
    /// a CPU a 100% --, e no Windows esse 1 ms vira 15. Aplicado ao
    /// Assembly, cada milissegundo de chip custava quinze de relógio, e o
    /// LED piscava quinze vezes mais devagar que na placa.
    /// </summary>
    public bool TemRitmoProprio => Prog?.Montado is ProgramaMontado || RitmoDaCpu;

    Avr Avr()
        => avr ??= Prog?.Montado is ProgramaMontado montado
            ? new Avr(montado, maq)
            : throw new Exception("este programa nao tem codigo de maquina montado");

    // ---------------- constantes Arduino ----------------
    static readonly Dictionary<string, Valor> Constantes = new()
    {
        ["HIGH"] = Valor.DeInt(1),
        ["LOW"] = Valor.DeInt(0),

        // Os enums da Adafruit_MPU6050 (os valores dos registradores).
        ["MPU6050_RANGE_2_G"] = Valor.DeInt(0), ["MPU6050_RANGE_4_G"] = Valor.DeInt(1),
        ["MPU6050_RANGE_8_G"] = Valor.DeInt(2), ["MPU6050_RANGE_16_G"] = Valor.DeInt(3),
        ["MPU6050_RANGE_250_DEG"] = Valor.DeInt(0), ["MPU6050_RANGE_500_DEG"] = Valor.DeInt(1),
        ["MPU6050_RANGE_1000_DEG"] = Valor.DeInt(2), ["MPU6050_RANGE_2000_DEG"] = Valor.DeInt(3),
        ["MPU6050_BAND_260_HZ"] = Valor.DeInt(0), ["MPU6050_BAND_184_HZ"] = Valor.DeInt(1),
        ["MPU6050_BAND_94_HZ"] = Valor.DeInt(2), ["MPU6050_BAND_44_HZ"] = Valor.DeInt(3),
        ["MPU6050_BAND_21_HZ"] = Valor.DeInt(4), ["MPU6050_BAND_10_HZ"] = Valor.DeInt(5),
        ["MPU6050_BAND_5_HZ"] = Valor.DeInt(6),
        ["MPU6050_HIGHPASS_DISABLE"] = Valor.DeInt(0), ["MPU6050_HIGHPASS_5_HZ"] = Valor.DeInt(1),
        ["INPUT"] = Valor.DeInt(0),
        ["OUTPUT"] = Valor.DeInt(1),
        ["INPUT_PULLUP"] = Valor.DeInt(2),
        // bool do C++: vale 1 e 0 na conta e no print, e o publish() do
        // MQTT sabe que "true" e a flag de retida, e nao um tamanho.
        ["true"] = Valor.DeBool(true),
        ["false"] = Valor.DeBool(false),
        ["PI"] = Valor.DeReal(Math.PI),

        // ---- constantes de biblioteca ----
        // Os valores nao importam para a simulacao; importa o sketch
        // compilar sem "variavel nao declarada" na cara do aluno.
        ["DHT11"] = Valor.DeInt(11),
        ["DHT12"] = Valor.DeInt(12),
        ["DHT21"] = Valor.DeInt(21),
        ["DHT22"] = Valor.DeInt(22),
        ["AM2302"] = Valor.DeInt(22),
        ["NEO_GRB"] = Valor.DeInt(82),
        ["NEO_RGB"] = Valor.DeInt(6),
        ["NEO_GRBW"] = Valor.DeInt(90),
        ["NEO_KHZ800"] = Valor.DeInt(0),
        ["NEO_KHZ400"] = Valor.DeInt(256),
        // o KeyState da Keypad
        ["IDLE"] = Valor.DeInt(0),
        ["PRESSED"] = Valor.DeInt(1),
        ["HOLD"] = Valor.DeInt(2),
        ["RELEASED"] = Valor.DeInt(3),
        ["NO_KEY"] = Valor.DeCaractere(0),
        // analogReference(): os numeros sao os da Maquina (o EXTERNAL e o 0 e o
        // DEFAULT o 1, como no Uno; o INTERNAL2V56, o do ATtiny85).
        ["DEFAULT"] = Valor.DeInt(Maquina.ReferenciaPadrao),
        ["EXTERNAL"] = Valor.DeInt(Maquina.ReferenciaExterna),
        ["INTERNAL"] = Valor.DeInt(Maquina.ReferenciaInterna),
        ["INTERNAL1V1"] = Valor.DeInt(Maquina.ReferenciaInterna1V1),
        ["INTERNAL2V56"] = Valor.DeInt(Maquina.ReferenciaInterna2V56),
        ["SERIAL_8N1"] = Valor.DeInt(0x06),
        ["SERIAL_8N2"] = Valor.DeInt(0x0E),
        ["SERIAL_8E1"] = Valor.DeInt(0x26),
        ["SERIAL_7E1"] = Valor.DeInt(0x24),
        ["SSD1306_SWITCHCAPVCC"] = Valor.DeInt(2),
        ["SSD1306_EXTERNALVCC"] = Valor.DeInt(1),
        ["SSD1306_WHITE"] = Valor.DeInt(1),
        ["SSD1306_BLACK"] = Valor.DeInt(0),
        ["SSD1306_INVERSE"] = Valor.DeInt(2),
        // a Adafruit_SH110X (o OLED de 1,3")
        ["SH110X_WHITE"] = Valor.DeInt(1),
        ["SH110X_BLACK"] = Valor.DeInt(0),
        ["SH110X_INVERSE"] = Valor.DeInt(2),
        // o initR() da Adafruit_ST7735 (a cor da etiqueta da pelicula diz a versao)
        ["INITR_GREENTAB"] = Valor.DeInt(0x00),
        ["INITR_REDTAB"] = Valor.DeInt(0x01),
        ["INITR_BLACKTAB"] = Valor.DeInt(0x02),
        ["INITR_18GREENTAB"] = Valor.DeInt(0x00),
        ["INITR_18REDTAB"] = Valor.DeInt(0x01),
        ["INITR_18BLACKTAB"] = Valor.DeInt(0x02),
        ["INITR_144GREENTAB"] = Valor.DeInt(0x01 | 0x100),
        ["INITR_MINI160x80"] = Valor.DeInt(0x04),
        // os segmentos da TM1637Display
        ["SEG_A"] = Valor.DeInt(0x01),
        ["SEG_B"] = Valor.DeInt(0x02),
        ["SEG_C"] = Valor.DeInt(0x04),
        ["SEG_D"] = Valor.DeInt(0x08),
        ["SEG_E"] = Valor.DeInt(0x10),
        ["SEG_F"] = Valor.DeInt(0x20),
        ["SEG_G"] = Valor.DeInt(0x40),
        ["SEG_DP"] = Valor.DeInt(0x80),
        ["WHITE"] = Valor.DeInt(1),
        ["BLACK"] = Valor.DeInt(0),
        ["INVERSE"] = Valor.DeInt(2),
        // os comandos do controlador que os exemplos mandam na mao
        ["SSD1306_DISPLAYOFF"] = Valor.DeInt(0xAE),
        ["SSD1306_DISPLAYON"] = Valor.DeInt(0xAF),
        ["SSD1306_NORMALDISPLAY"] = Valor.DeInt(0xA6),
        ["SSD1306_INVERTDISPLAY"] = Valor.DeInt(0xA7),
        ["SSD1306_SETCONTRAST"] = Valor.DeInt(0x81),
        ["SSD1306_DEACTIVATE_SCROLL"] = Valor.DeInt(0x2E),
        ["INPUT_PULLDOWN"] = Valor.DeInt(3),

        // ---- SDK da Raspberry Pi Pico ----
        // Trocar GPIO_OUT por GPIO_IN deixa o LED apagado sem nenhuma
        // mensagem, entao os dois valores tem que ser os de verdade:
        // saida e 1, entrada e 0.
        ["GPIO_OUT"] = Valor.DeInt(1),
        ["GPIO_IN"] = Valor.DeInt(0),
        ["PICO_DEFAULT_LED_PIN"] = Valor.DeInt(25),
        ["PICO_DEFAULT_I2C_SDA_PIN"] = Valor.DeInt(4),
        ["PICO_DEFAULT_I2C_SCL_PIN"] = Valor.DeInt(5),
        ["RISING"] = Valor.DeInt(1),
        ["FALLING"] = Valor.DeInt(2),
        ["CHANGE"] = Valor.DeInt(3),
        ["MSBFIRST"] = Valor.DeInt(1),
        ["LSBFIRST"] = Valor.DeInt(0),

        // Cartao SD. FILE_WRITE vale 1 e FILE_READ vale 0 na biblioteca
        // do Arduino; e o segundo argumento de SD.open().
        ["FILE_WRITE"] = Valor.DeInt(1),
        ["FILE_READ"] = Valor.DeInt(0),
        ["FILE_APPEND"] = Valor.DeInt(1),
    };

    /// <summary>
    /// Metodos de String, como o Arduino os expoe.
    ///
    /// O motor guarda texto num Valor, nao num objeto, entao estes nao
    /// passam pelo caminho das bibliotecas. Sao os que aparecem em
    /// exercicio: medir, cortar, procurar, comparar e converter.
    ///
    /// As posicoes seguem a convencao do Arduino: fora do intervalo
    /// devolve vazio ou -1 em vez de estourar, porque no hardware real
    /// tambem nao estoura.
    /// </summary>
    /// <summary>O std::string::npos: o maior size_t -- 4294967295 nos chips de 32 bits.</summary>
    long NposDoTexto => maq.Perfil?.De8Bits == true ? 0xFFFF : 0xFFFFFFFFL;

    Valor ChamarTexto(string texto, string metodo, Chamada c, Ambiente amb)
    {
        texto ??= "";
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";
        int N(int i) => i < args.Count ? (int)args[i].Num : 0;

        switch (metodo)
        {
            case "length": return Valor.DeInt(texto.Length);
            case "isEmpty": return Valor.DeBool(texto.Length == 0);

            // Um char, como o "s[i]": 'A' + 1 e 66, e nao "A1". Fora do
            // texto e o '\0'.
            case "charAt":
            {
                int i = N(0);
                return Valor.DeCaractere(i >= 0 && i < texto.Length ? texto[i] : 0);
            }

            // "s.toCharArray(buf, sizeof(buf))": copia o que couber, sempre
            // com o '\0' no fim -- o texto comprido e cortado, como na placa.
            case "toCharArray":
            case "getBytes":
            {
                if (c.Args.Count < 2 || !VetorDoArgumento(c.Args[0], amb, out var destino))
                    throw new ErroDeExecucao(
                        $"String.{metodo}(vetor, tamanho): o primeiro argumento e o vetor que recebe o texto", c.Linha);
                int cabe = Math.Min(N(1), destino.Length);
                if (cabe <= 0) return Valor.Vazio;
                int desde = c.Args.Count > 2 ? Math.Max(0, N(2)) : 0;
                var bytes = TextoArduino.ParaBytes(texto);
                bool deChar = destino.Length > 0 && destino[0].Caractere;
                int k = 0;
                for (; k < cabe - 1 && desde + k < bytes.Length; k++)
                    destino[k] = deChar ? Valor.DeCaractere(bytes[desde + k]) : Valor.DeInt(bytes[desde + k]);
                destino[k] = deChar ? Valor.DeCaractere(0) : Valor.DeInt(0);
                return Valor.Vazio;
            }

            case "substring":
            {
                int ini = Math.Clamp(N(0), 0, texto.Length);
                int fim = args.Count > 1 ? Math.Clamp(N(1), ini, texto.Length) : texto.Length;
                return Valor.DeTexto(texto[ini..fim]);
            }

            case "indexOf": return Valor.DeInt(texto.IndexOf(S(0), StringComparison.Ordinal));
            case "lastIndexOf":
                return Valor.DeInt(texto.LastIndexOf(S(0), StringComparison.Ordinal));

            case "equals": return Valor.DeBool(texto == S(0));
            case "equalsIgnoreCase":
                return Valor.DeBool(string.Equals(texto, S(0),
                    StringComparison.OrdinalIgnoreCase));

            case "startsWith": return Valor.DeBool(texto.StartsWith(S(0), StringComparison.Ordinal));
            case "endsWith": return Valor.DeBool(texto.EndsWith(S(0), StringComparison.Ordinal));

            case "toUpperCase": return Valor.DeTexto(texto.ToUpperInvariant());
            case "toLowerCase": return Valor.DeTexto(texto.ToLowerInvariant());
            case "trim": return Valor.DeTexto(texto.Trim());

            case "replace" when args.Count >= 3 && !args[0].EhTexto:
            {
                int ini = Math.Clamp(N(0), 0, texto.Length);
                int quantos = (int)Math.Clamp((long)args[1].Num, 0, texto.Length - ini);
                return Valor.DeTexto(texto.Remove(ini, quantos).Insert(ini, S(2)));
            }
            case "replace": return Valor.DeTexto(texto.Replace(S(0), S(1), StringComparison.Ordinal));
            case "concat": return Valor.DeTexto(texto + S(0));

            case "toInt":
                return Valor.DeInt(int.TryParse(NumeroDaFrente(texto),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0);

            case "toFloat":
            case "toDouble":
                return Valor.DeReal(double.TryParse(NumeroDaFrente(texto),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0);

            // c_str() so faz sentido com ponteiro; aqui o texto e o proprio
            // valor, entao devolver ele mesmo mantem o sketch funcionando.
            case "c_str": return Valor.DeTexto(texto);

            case "remove":
            {
                int ini = Math.Clamp(N(0), 0, texto.Length);
                int quantos = args.Count > 1
                    ? Math.Clamp(N(1), 0, texto.Length - ini) : texto.Length - ini;
                return Valor.DeTexto(texto.Remove(ini, quantos));
            }

            case "setCharAt":
            {
                int i = N(0);
                if (i < 0 || i >= texto.Length) return Valor.DeTexto(texto);
                var letras = texto.ToCharArray();
                letras[i] = (char)CodigoDe(args.Count > 1 ? args[1] : Valor.DeInt(0));
                return Valor.DeTexto(new string(letras));
            }

            case "compareTo": return Valor.DeInt(string.CompareOrdinal(texto, S(0)));

            // "WiFi.localIP().toString()" e companhia.
            case "toString": return Valor.DeTexto(texto);
            case "reserve": return Valor.DeBool(true);

            // ---- o std::string (o simulador guarda os dois do mesmo jeito) ----
            case "size": return Valor.DeC(texto.Length, TipoC.UInt);
            case "empty": return Valor.DeBool(texto.Length == 0);
            case "data": return Valor.DeTexto(texto);
            case "front": return Valor.DeCaractere(texto.Length > 0 ? texto[0] : 0);
            case "back": return Valor.DeCaractere(texto.Length > 0 ? texto[^1] : 0);
            case "at":
            {
                int i = N(0);
                if (i < 0 || i >= texto.Length)
                    throw new ErroDeExecucao($"std::string::at({i}) fora do texto (tamanho {texto.Length}): na placa isso "
                                             + "lanca std::out_of_range, ninguem pega, e o abort() reinicia a placa.", c.Linha);
                return Valor.DeCaractere(texto[i]);
            }
            case "substr":
            {
                int ini = N(0);
                if (ini > texto.Length)
                    throw new ErroDeExecucao($"std::string::substr({ini}) depois do fim do texto (tamanho {texto.Length}): "
                                             + "na placa isso lanca std::out_of_range e a placa reinicia.", c.Linha);
                int quantos = args.Count > 1 ? (int)Math.Clamp((long)args[1].Num, 0, texto.Length - ini) : texto.Length - ini;
                return Valor.DeTexto(texto.Substring(Math.Max(0, ini), quantos));
            }
            case "find":
            case "rfind":
            {
                string alvo = args.Count > 0 && args[0].Caractere ? ((char)args[0].Num).ToString() : S(0);
                int de = args.Count > 1 ? N(1) : metodo == "find" ? 0 : texto.Length;
                int achou = metodo == "find"
                    ? (de <= texto.Length ? texto.IndexOf(alvo, Math.Max(0, de), StringComparison.Ordinal) : -1)
                    : texto.LastIndexOf(alvo, Math.Clamp(de, 0, Math.Max(0, texto.Length - 1)), StringComparison.Ordinal);
                return Valor.DeC(achou < 0 ? NposDoTexto : achou, TipoC.UInt);
            }
            case "compare": return Valor.DeInt(Math.Sign(string.CompareOrdinal(texto, S(0))));
            case "append":
                return Valor.DeTexto(texto + (args.Count > 0 && args[0].Caractere ? ((char)args[0].Num).ToString() : S(0)));
            case "push_back": return Valor.DeTexto(texto + (char)CodigoDe(args.Count > 0 ? args[0] : Valor.DeInt(0)));
            case "pop_back": return Valor.DeTexto(texto.Length > 0 ? texto[..^1] : texto);
            case "clear": return Valor.DeTexto("");
            case "erase":
            {
                int ini = Math.Clamp(N(0), 0, texto.Length);
                int quantos = args.Count > 1 ? (int)Math.Clamp((long)args[1].Num, 0, texto.Length - ini) : texto.Length - ini;
                return Valor.DeTexto(texto.Remove(ini, quantos));
            }
            case "insert":
                return Valor.DeTexto(texto.Insert(Math.Clamp(N(0), 0, texto.Length), S(1)));

            default:
                throw new ErroDeExecucao(
                    $"String.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
        }
    }

    /// <summary>
    /// O numero do comeco do texto, como o toInt() do Arduino faz: ele
    /// para no primeiro caractere que nao serve em vez de dar erro.
    /// </summary>
    static string NumeroDaFrente(string texto)
    {
        int i = 0;
        while (i < texto.Length && char.IsWhiteSpace(texto[i])) i++;

        int ini = i;
        if (i < texto.Length && (texto[i] == '-' || texto[i] == '+')) i++;
        while (i < texto.Length && (char.IsAsciiDigit(texto[i]) || texto[i] == '.')) i++;
        return texto[ini..i];
    }

    // ---------------- chamadas ----------------
    /// <summary>Os argumentos de uma chamada, ja avaliados, na ordem.</summary>
    List<Valor> AvaliarTodos(List<Expr> exprs, Ambiente amb)
    {
        var valores = new List<Valor>(exprs.Count);
        foreach (var e in exprs) valores.Add(Avaliar(e, amb));
        return valores;
    }

    /// <summary>O programa tem uma funcao com este nome (a dele vale mais que a traduzida).</summary>
    bool ProgramaDeclarou(string nome)
    {
        foreach (var f in prog.Funcoes)
            if (f.Nome == nome) return true;
        return false;
    }

    Valor Invocar(Chamada c, Ambiente amb)
    {
        // Em Python quase tudo e metodo de um valor ("pino.value(1)") ou
        // embutida ("print"). O que nao for cai no caminho de sempre, que
        // atende tambem as funcoes declaradas com "def".
        if (EhPython && ChamarPython(c, amb, out var doPython)) return doPython;

        if (c.Alvo is Membro m && m.Obj is Variavel o)
        {
            AntesDoCore();
            if (o.Nome == "Serial") return ChamarSerial(m.Nome, c, amb);
            if (o.Nome == "rp2040" && !amb.BuscarObjeto("rp2040", out _)) return ChamarRp2040(m.Nome, c, amb);

            // O IRremote novo (IRremote.hpp) nao declara objeto nenhum:
            // ele traz um "IrReceiver" pronto, como o Serial. Os sketches
            // de controle remoto dos ultimos anos usam so essa forma.
            if (o.Nome == "IrReceiver") return ChamarReceptorIr(m.Nome, c, amb);
            if (o.Nome is "Wire" or "Wire1" && !amb.BuscarObjeto(o.Nome, out _))
                return ChamarI2c(BarramentoI2cDe(o.Nome, c.Linha), o.Nome, m.Nome, c, amb);
            if (o.Nome == "BLE" && UsaArduinoBle && !amb.BuscarObjeto("BLE", out _)) return ChamarBleLocal(m.Nome, c, amb);
            if (o.Nome is "Serial1" or "Serial2" or "Serial3" && !amb.BuscarObjeto(o.Nome, out _))
                return ChamarPorta(PortaGlobal(o.Nome, c.Linha), m.Nome, c, amb);
            if (o.Nome is "SPI" or "SPI1" && !amb.BuscarObjeto(o.Nome, out _))
            {
                // No C++ compilado o "SPI1" e o nome que o core da ao segundo barramento
                // da ESP32 (o SPIClass(HSPI), o SPI(1) do MicroPython): o sketch nao o escreve.
                if (o.Nome == "SPI1" && Chip != "RP2040" && !(RodaNativo && EhFamiliaEsp32))
                    throw new ErroDeExecucao($"'SPI1' nao existe no core da {maq.Perfil?.Nome}: o outro barramento e um "
                                             + (EhFamiliaEsp32 ? "SPIClass (\"SPIClass hspi(HSPI);\")." : "SPIClass declarado no sketch."), c.Linha);
                return ChamarSpi(BarramentoGlobal(o.Nome), m.Nome, c, amb);
            }
            if (o.Nome == "SD") return ChamarCartao(m.Nome, c, amb);
            if (o.Nome is "LittleFS" or "SPIFFS" or "FFat" && !amb.BuscarObjeto(o.Nome, out _)) return ChamarFlash(o.Nome, m.Nome, c, amb);
            if (o.Nome == "WiFi") return ChamarWiFi(m.Nome, c, amb);
            if (o.Nome == "ESP") return ChamarEsp(m.Nome, c, amb);
            if (o.Nome == "EEPROM" && !amb.BuscarObjeto("EEPROM", out _)) return ChamarEeprom(m.Nome, c, amb);
            if (amb.BuscarObjeto(o.Nome, out var obj)) return ChamarObjeto(obj, m.Nome, c, amb);

            // "_porta.println(x)" com "Stream& _porta" guardando o Serial: a
            // referencia leva ao objeto do core.
            if (amb.Buscar(o.Nome, out var doCore) && doCore.EhTexto && !EhMetodoDeTexto(m.Nome))
            {
                if (doCore.Texto == "Serial") return ChamarSerial(m.Nome, c, amb);
                if (doCore.Texto is "Wire" or "Wire1") return ChamarI2c(BarramentoI2cDe(doCore.Texto, c.Linha), doCore.Texto, m.Nome, c, amb);
            }

            // Metodo de String: "msg.length()". A variavel guarda texto,
            // nao um objeto de biblioteca, entao a busca acima falha --
            // e ate agora o sketch parava aqui. Exercicio de texto usa
            // isso o tempo todo.
            if (amb.Buscar(o.Nome, out var texto) && texto.EhTexto)
            {
                // reserve(): o buffer da String cresce agora, se couber na heap.
                if (m.Nome == "reserve" && c.Args.Count > 0)
                    return Valor.DeBool(ReservarTexto(o.Nome, (long)Avaliar(c.Args[0], amb).Num, amb, c.Linha));

                var resultado = ChamarTexto(texto.Texto, m.Nome, c, amb);

                // trim(), toUpperCase(), replace() e remove() MUDAM a
                // String no Arduino, em vez de devolver uma nova. Escrever
                // "comando.trim();" numa linha sozinha e o jeito normal --
                // e ate agora essa linha nao fazia nada: o texto continuava
                // com o "\r" do fim e nenhuma comparacao batia.
                if (resultado.EhTexto && MetodosQueMudamOTexto.Contains(m.Nome))
                {
                    // O concat() que nao coube na heap devolve false e nao muda nada.
                    var guardado = TextoNovo(o.Nome, resultado, amb, concatenando: true, c.Linha);
                    amb.Atribuir(o.Nome, guardado);
                    if (m.Nome == "concat") return Valor.DeBool(guardado.Texto == resultado.Texto);
                }
                return resultado;
            }

            throw new ErroDeExecucao(
                $"'{o.Nome}.{m.Nome}()': o objeto '{o.Nome}' nao foi declarado neste sketch, "
                + "ou a biblioteca dele ainda nao existe no simulador. "
                + "As conhecidas estao em: " + string.Join(", ", Bibliotecas.Conhecidas), c.Linha);
        }

        // O mesmo para um literal ou expressao: "String(x).length()".
        if (c.Alvo is Membro mt)
        {
            // "robo.motor.girar()", "botoes[1].ler()", "(*p).ler()": o
            // objeto que a expressao devolve.
            if (ObjetoDaExpressao(mt.Obj, amb) is { } dono) return ChamarObjeto(dono, mt.Nome, c, amb);

            var alvo = Avaliar(mt.Obj, amb);
            if (alvo.EhTexto) return ChamarTexto(alvo.Texto, mt.Nome, c, amb);
            if (alvo.Referencia is Objeto devolvido) return ChamarObjeto(devolvido, mt.Nome, c, amb);
        }

        // "(*acao)()": o ponteiro de funcao escrito com a estrela.
        if (c.Alvo is Unaria { Op: "*" } estrela) c = c with { Alvo = estrela.Alvo };

        // "(lambda x: x * 2)(21)", "acoes[0]()", "fabrica()()": a expressao
        // devolve uma funcao, e e ela que se chama.
        if (c.Alvo is not Variavel && ChamarGuardada(Avaliar(c.Alvo, amb), c, amb, out var devolvida))
            return devolvida;

        if (c.Alvo is not Variavel fn)
            throw new ErroDeExecucao("chamada invalida", c.Linha);

        // ---- o mesmo verbo, escrito do jeito de cada linguagem ----
        //
        // "Digital_Write" em Ada, "digital_write" em Rust e
        // "DigitalWrite" em C# sao a MESMA operacao. A traducao vem
        // aqui, antes de tudo, mas so quando o programa NAO e do
        // Arduino: la o nome e o que esta escrito, e traduzir trocaria
        // a funcao da pessoa pela do simulador sem avisar.
        //
        // Funcao que o proprio programa declarou tem prioridade: quem
        // escreveu "procedure Print" quer a sua, e nao a nossa.
        if (Vocabulario.Vale(Prog?.Linguagem ?? Linguagem.Arduino)
            && !ProgramaDeclarou(fn.Nome)
            && Vocabulario.Traduzir(fn.Nome, c.Args.Count) is { } canonico
            && canonico != fn.Nome)
        {
            fn = new Variavel(canonico, fn.Linha);
            c = c with { Alvo = fn };
        }

        // Metodo sem "this->", static, construtor, funcao do programa e
        // ponteiro de funcao. Antes das do simulador: uma funcao que o
        // programa escreveu e dele, e o metodo esconde a global de mesmo
        // nome -- como no C++.
        if (ChamarDoPrograma(fn, c, amb, out var doPrograma)) return doPrograma;
        if (fn.Nome.StartsWith("::", StringComparison.Ordinal))
        {
            fn = new Variavel(fn.Nome[2..], fn.Linha);
            c = c with { Alvo = fn };
        }

        // attachInterrupt tem que vir ANTES da avaliacao dos argumentos:
        // o 2o e o NOME de uma funcao, e avalia-lo como expressao dava
        // "variavel 'contar' nao foi declarada".
        if (fn.Nome == "attachInterrupt") return Vigiar(c, amb);

        // A BLE da ESP32: "BLEDevice::init("nome")", "BLEUUID((uint16_t)0x180F)".
        if (fn.Nome.StartsWith("BLEDevice::", StringComparison.Ordinal)) return ChamarBleDevice(fn.Nome, c, amb);
        if (fn.Nome == "BLEUUID")
            return Valor.DeReferencia(new Objeto("BLEUUID")
            {
                Texto = c.Args.Count > 0 ? UuidDoValor(Avaliar(c.Args[0], amb)) : "",
            });

        // Pinos por nome: so um chip proprio chama, e eles precisam vir
        // antes da avaliacao porque o aoMudarPino recebe o NOME de uma
        // funcao no ultimo argumento.
        if (ChamarPinoPorNome(fn.Nome, c, amb, out var doChip)) return doChip;

        // O relogio de rede recebe o ENDERECO de um struct tm, entao
        // tambem vem antes da avaliacao normal dos argumentos.
        if (fn.Nome == "getLocalTime" && PegarHorario(c, amb, out var doRelogio))
            return doRelogio;
        if (fn.Nome == "strftime" && FormatarHorario(c, amb, out var formatado))
            return formatado;

        if (fn.Nome == "sizeof") return TamanhoDe(c, amb);
        if (EhDoSono(fn.Nome) && ChamarSono(fn.Nome, c, amb, out var doSono)) return doSono;
        if (fn.Nome == "memcpy" && CopiarMemoria(c, amb, out var copiado)) return copiado;
        if (fn.Nome.StartsWith("esp_now_", StringComparison.Ordinal)
            && ChamarEspNow(fn.Nome, c, amb, out var doEspNow)) return doEspNow;

        // Tambem antes de avaliar: as funcoes de texto do C recebem o VETOR
        // de destino ("snprintf(msg, 50, ...)"), e uma funcao do aluno pode
        // receber um vetor inteiro. Avaliados como valor, os dois se perdiam.
        if (FuncaoDeTexto(fn.Nome, c, amb, out var deTexto)) return deTexto;
        if (ChamarFuncaoComVetores(fn.Nome, c, amb, out var comVetores)) return comVetores;

        // A configuracao de PWM do SDK da Pico vai por ENDERECO de struct
        // ("pwm_config_set_wrap(&cfg, 999)"), entao tambem antes.
        if (fn.Nome.StartsWith("pwm_config_", StringComparison.Ordinal) || fn.Nome == "pwm_init")
            if (ConfigurarPwmDaPico(fn.Nome, c, amb, out var doPwm)) return doPwm;

        // Um laco, e nao um Select: a lambda que capturava o "amb" fazia o C#
        // criar um objeto em TODA chamada de funcao, mesmo nas que voltam antes daqui.
        var args = AvaliarTodos(c.Args, amb);
        double A(int i) => i < args.Count ? args[i].Num : 0;
        AntesDoCore();

        // Os watchdogs de cada core: avr/wdt.h, o loop da ESP32.
        if (ChamarWatchdog(fn.Nome, args, c.Linha, out var doWatchdog)) return doWatchdog;

        // malloc, free, as da heap da ESP32 e as que medem a RAM.
        if (ChamarMemoria(fn.Nome, args, c.Linha, out var daMemoria)) return daMemoria;

        // O SDK da Pico primeiro: sao nomes proprios dele
        // ("gpio_put", "sleep_ms") e nao colidem com nada do Arduino.
        if (ChamarPicoSdk(fn.Nome, args, out var doPico)) return doPico;

        // As da math.h e as macros do Arduino.h, com o tipo que cada core da
        // a elas: floor() devolve double, o round() do AVR devolve long.
        if (MatematicaC(fn.Nome, args, c.Linha) is { } matematica)
        {
            CobrarMatematica(fn.Nome, matematica);
            return matematica;
        }

        // O Assembly entra por aqui: as duas funcoes de fachada que o
        // montador cria chamam o executor de verdade.
        if (fn.Nome == "__avr_reiniciar")
        {
            ExigirAtmega328();
            Avr().Reiniciar();
            return Valor.Vazio;
        }
        if (fn.Nome == "__avr_passo") { Avr().Passo((int)A(0)); return Valor.Vazio; }

        switch (fn.Nome)
        {
            // Escrever no monitor, venha de Put_Line, de println! ou
            // de Console.WriteLine. Um argumento so, e ele e o texto.
            case "__println":
            case "__print":
            {
                string texto = args.Count > 0 ? args[0].Mostrar() : "";

                // O "{}" de Rust e o "{0}" de C# sao onde o proximo
                // argumento entra. Sem isso, um println! com valores
                // imprimiria as chaves em vez do numero.
                for (int k = 1; k < args.Count; k++)
                {
                    int chaves = texto.IndexOf("{}", StringComparison.Ordinal);

                    if (chaves >= 0)
                        texto = texto[..chaves] + args[k].Mostrar() + texto[(chaves + 2)..];
                    else
                        texto += " " + args[k].Mostrar();
                }

                Escrever(texto, fn.Nome == "__println");
                return Valor.Vazio;
            }

            case "pinMode": maq.PinMode((int)A(0), (int)A(1)); return Valor.Vazio;
            case "digitalWrite": maq.DigitalWrite((int)A(0), (int)A(1)); return Valor.Vazio;
            case "digitalRead": return DoTipo(maq.DigitalRead((int)A(0)), TipoC.Int);
            // O analogRead espera o conversor: ~111 us no Uno, ~25 na ESP32.
            case "analogRead":
                if (RitmoDaCpu) maq.GastarProcessador(Custos.AnalogReadUs);
                return DoTipo(maq.AnalogReadDoArduino((int)A(0)), TipoDoAnalogRead);

            // O core ESP32 converte a leitura com a calibracao do chip e
            // devolve milivolts (uint32_t) -- e o que mede uma bateria sem a
            // conta de bits. Nos outros cores a funcao nao existe.
            case "analogReadMilliVolts":
                if (!EhFamiliaEsp32)
                    throw new ErroDeExecucao($"analogReadMilliVolts() so existe no core da ESP32; no {maq.Perfil.Processador} "
                        + "a conta e analogRead(pino) * tensao de referencia / leitura maxima.", c.Linha);
                if (RitmoDaCpu) maq.GastarProcessador(Custos.AnalogReadUs);
                return DoTipo((int)Math.Round(maq.AnalogRead((int)A(0)) * 3300.0 / maq.Perfil.MaxAdc), TipoC.UInt);
            case "analogWrite": maq.AnalogWriteNaResolucao((int)A(0), A(1)); return Valor.Vazio;

            // O LED RGB enderecavel soldado nas DevKits S2/S3/C3/C6/H2: o core
            // da ESP32 escreve nele sem biblioteca (neopixelWrite, que o core
            // 3.x chama de rgbLedWrite). Vira uma fita de um LED no pino.
            case "neopixelWrite":
            case "rgbLedWrite":
            {
                if (!EhFamiliaEsp32)
                    throw new ErroDeExecucao($"{fn.Nome}() so existe no core da ESP32.", c.Linha);
                int pinoRgb = (int)A(0);
                if (!maq.Fitas.TryGetValue(pinoRgb, out var fitaRgb)) maq.Fitas[pinoRgb] = fitaRgb = new Fita(1);
                fitaRgb.Definir(0, Fita.Montar((int)A(1), (int)A(2), (int)A(3)));
                fitaRgb.Mostrar();
                return Valor.Vazio;
            }

            // analogWriteResolution(10): o analogWrite passa a ir de 0 a 1023
            // (ESP32, RP2040, STM32). analogWriteFrequency(f) ou (pino, f).
            case "analogWriteResolution": maq.ResolucaoDoPwm = (int)A(0); return Valor.Vazio;

            // A referencia do conversor (o pino AREF). So existe nos AVR; o
            // 2,56 V so no ATmega2560 e no ATtiny85.
            case "analogReference":
            {
                int modo = (int)A(0);
                if (!maq.Perfil.De8Bits) return Valor.Vazio;
                if (modo == Maquina.ReferenciaInterna2V56 && maq.Perfil.Processador == "ATmega328P")
                    throw new ErroDeExecucao("INTERNAL2V56 nao existe no ATmega328P (Uno, Nano): a referencia interna dele e "
                        + "so a de 1,1 V (INTERNAL).", c.Linha);
                maq.ReferenciaAnalogica = modo;
                return Valor.Vazio;
            }
            case "analogWriteFrequency":
                maq.FrequenciaDoPwmPedida = args.Count > 1 ? A(1) : A(0);
                return Valor.Vazio;

            case "millis": return LerRelogio(micro: false);
            // Em microssegundos de verdade: antes era millis() vezes mil, e
            // medir um pulso ou uma volta do loop com micros() dava sempre
            // multiplo de mil.
            case "micros": return LerRelogio(micro: true);

            // Virtual: adianta o relogio. Real: dorme de verdade.
            // Espera longa e onde o botao costuma ser apertado: sem
            // olhar aqui, um delay(1000) engolia a interrupcao inteira.
            //
            // Esperar zera a contagem de passos: o limite existe para pegar
            // o laco preso SEM espera. "while (!Serial.available())
            // delay(10);" esperando o aluno digitar e o jeito certo, e
            // estourava o limite depois de uns minutos.
            case "delay":
            {
                long ms = (long)A(0);
                long ate = DelayAteOWatchdog(ms);

                // Interrupcao num pino com sinal de fora (o canal do receptor):
                // o delay anda de borda em borda, e cada uma dispara a rotina
                // no instante dela -- e o micros() la dentro mede o pulso certo.
                if (maq.TemSinalVigiado)
                {
                    double fimDoDelay = maq.AgoraUs() + ate * 1000.0;
                    while (!maq.Parar)
                    {
                        double borda = maq.ProximaBordaVigiada(maq.AgoraUs());
                        if (borda >= fimDoDelay) { maq.EsperarMicros(fimDoDelay - maq.AgoraUs()); break; }
                        maq.EsperarMicros(borda - maq.AgoraUs() + 0.5);
                        maq.Cedeu();    // o delay cede o processador (o watchdog nao conta)
                        AtenderInterrupcoes();
                    }
                    if (ate < ms) ConferirWatchdogs();
                    Ceder();
                    AtenderInterrupcoes();
                    EntregarBle();
                    passos = 0;
                    return Valor.Vazio;
                }

                // Com um radio ligado, o que chega pelo ar e entregue durante
                // a espera: na placa os callbacks do BLE e do ESP-NOW rodam
                // na tarefa do radio, e nao esperam o delay(2000) acabar.
                if (ate > 20 && (radioBle is not null || Maq.Rede.EspNowDe(Maq).Ligado))
                {
                    for (long feito = 0; feito < ate && !maq.Parar;)
                    {
                        long passo = Math.Min(20, ate - feito);
                        maq.Avancar(passo);
                        feito += passo;
                        EntregarEspNow();
                        EntregarBle();
                    }
                }
                else maq.Avancar(ate);
                if (ate < ms) ConferirWatchdogs();
                Ceder();
                AtenderInterrupcoes();
                // Tambem na espera curta: quem espera o pacote de 1 em 1 ms (o recv do espnow do
                // MicroPython) nunca passa pelo delay longo nem pela volta do loop().
                EntregarEspNow();
                EntregarBle();
                passos = 0;
                return Valor.Vazio;
            }
            case "delayMicroseconds":
                maq.EsperarMicros(A(0));
                if (A(0) >= 1000) passos = 0;
                return Valor.Vazio;

            // tone(pino, f, ms): com a duracao, para sozinho.
            case "tone": maq.Tone((int)A(0), (int)A(1), args.Count > 2 ? (long)A(2) : 0); return Valor.Vazio;
            case "noTone": maq.NoTone((int)A(0)); return Valor.Vazio;

            // PWM do core 2.x: ledcSetup(canal, freq, bits) + ledcAttachPin(pino, canal).
            // A frequencia e os bits contam: ledcWrite(canal, 512) com 10
            // bits e metade, e nao o maximo -- antes a conta era fixa.
            case "ledcSetup":
                ledc[(int)A(0)] = (A(1), (int)A(2));
                return Valor.DeReal(A(1));
            case "ledcAttachPin": maq.CanalPwm[(int)A(1)] = (int)A(0); return Valor.Vazio;

            // PWM do core 3.x: ledcAttach(pino, freq, bits); o "canal" e o pino.
            case "ledcAttach":
            case "ledcAttachChannel":
                maq.CanalPwm[(int)A(0)] = (int)A(0);
                ledc[(int)A(0)] = (A(1), (int)A(2));
                return Valor.DeBool(true);
            case "ledcChangeFrequency":
                ledc[(int)A(0)] = (A(1), (int)A(2));
                return Valor.DeReal(A(1));
            case "ledcWrite":
            {
                int canal = (int)A(0);
                int alvoPino = maq.CanalPwm.TryGetValue(canal, out var pino) ? pino : canal;
                var (hz, bits) = ledc.GetValueOrDefault(canal, (5000.0, 8));
                int maximo = (1 << Math.Clamp(bits, 1, 16)) - 1;
                // O servo comandado pelo LEDC (50 Hz, o pulso de 0,5 a 2,6 ms):
                // e o pulso que o servo le, como o da biblioteca Servo.
                double pulsoUs = hz is >= 40 and <= 400 ? Math.Clamp(A(1), 0, maximo) / (maximo + 1.0) * 1e6 / hz : 0;
                if (pulsoUs is >= 400 and <= 2700)
                {
                    int graus = (int)Math.Round(Math.Clamp((pulsoUs - 544) * 180.0 / (2400 - 544), 0, 180));
                    maq.EscreverServo(alvoPino, graus, pulsoUs);
                    return Valor.DeBool(true);
                }
                maq.AnalogWrite(alvoPino, (int)Math.Round(Math.Clamp(A(1), 0, maximo) * 255.0 / maximo), hz);
                return Valor.DeBool(true);
            }
            case "ledcWriteTone":
            {
                int canal = (int)A(0);
                maq.Tone(maq.CanalPwm.TryGetValue(canal, out var pino) ? pino : canal, (int)A(1));
                return Valor.DeReal(A(1));
            }

            // Muda os bits que o analogRead devolve (ESP32, RP2040, STM32).
            // No AVR e no ESP8266 a funcao nao existe no core -- e nem
            // precisa: o conversor deles e de 10 bits.
            case "analogReadResolution":
                if (!maq.Perfil.MudaResolucaoDoAdc)
                    throw new ErroDeExecucao($"analogReadResolution() nao existe no core do "
                        + $"{maq.Perfil.Processador}: o conversor dele e de 10 bits, e o analogRead "
                        + "ja vai de 0 a 1023.", c.Linha);
                maq.ResolucaoDoAdc = Math.Clamp((int)A(0), 1, 16);
                return Valor.Vazio;

            // Tempo do eco, em microssegundos. E o que o HC-SR04 usa:
            // o editor guarda a distancia e a conta vira o pulso.
            case "pulseIn":
            case "pulseInLong":
            {
                int pinoDoPulso = (int)A(0);
                // Um sinal de fora no pino (o receptor de radio): espera a
                // borda de verdade e mede o pulso, gastando esse tempo -- seis
                // canais lidos com pulseIn levam os ~120 ms que levam na bancada.
                if (pinoDoPulso >= 0 && pinoDoPulso < Maquina.NumPinos && maq.SinaisExternos[pinoDoPulso] is { } sinal)
                {
                    int nivel = args.Count > 1 && A(1) == 0 ? 0 : 1;
                    double limite = args.Count > 2 ? A(2) : 1_000_000;
                    double agora = maq.AgoraUs();
                    if (sinal.ProximoPulso(agora, nivel) is not { } pulso || pulso.Inicio - agora > limite)
                    {
                        maq.EsperarMicros(limite);
                        return DoTipo(0, TipoC.ULong);
                    }
                    maq.EsperarMicros(pulso.Fim - agora);
                    return DoTipo(Math.Round(pulso.Fim - pulso.Inicio), TipoC.ULong);
                }
                return DoTipo(maq.Leitura($"{pinoDoPulso}:pulso"), TipoC.ULong);
            }

            case "yield":
                Ceder();
                passos = 0;
                return Valor.Vazio;

            // As da math.h que dizem se a conta deu certo. E como o sketch
            // descobre que o sensor nao respondeu: o DHT devolve NAN.
            case "isnan": return Valor.DeBool(double.IsNaN(A(0)));
            case "isinf": return Valor.DeBool(double.IsInfinity(A(0)));
            case "isfinite": return Valor.DeBool(double.IsFinite(A(0)));

            // O PWM do ESP8266 e do RP2040 se ajusta por faixa, e nao por
            // bits: analogWriteRange(1023) faz o analogWrite ir de 0 a 1023.
            case "analogWriteRange":
                if (maq.Perfil.Processador is not ("ESP8266" or "RP2040"))
                    throw new ErroDeExecucao($"analogWriteRange() e do core do ESP8266 e do da "
                        + $"Pico; no {maq.Perfil.Processador} use analogWriteResolution().", c.Linha);
                maq.MaximoDoPwm = Math.Max(1, (int)A(0));
                return Valor.Vazio;
            case "analogWriteFreq":
                maq.FrequenciaDoPwmPedida = A(0);
                return Valor.Vazio;

            // Seguram a entrega da interrupcao, como no hardware. O que
            // passou com elas desligadas nao volta depois.
            case "noInterrupts": maq.InterrupcoesLigadas = false; return Valor.Vazio;
            case "interrupts": maq.InterrupcoesLigadas = true; return Valor.Vazio;

            // digitalPinToInterrupt devolve o proprio pino nas placas que
            // suportamos, entao a conta continua valendo.
            case "digitalPinToInterrupt":
                return Valor.DeInt(A(0));

            case "detachInterrupt":
                maq.PararDeVigiar((int)A(0));
                return Valor.Vazio;

            // Registrador de deslocamento: ninguem simula o 74HC595 aqui,
            // mas os pinos mexem de verdade -- e e isso que o osciloscopio
            // da tela (a linha do tempo) mostra.
            case "shiftOut":
            {
                int dados = (int)A(0), relogio = (int)A(1);
                int ordem = (int)A(2), valor = (int)A(3);
                for (int i = 0; i < 8; i++)
                {
                    int bit = ordem != 0 ? (valor >> (7 - i)) & 1 : (valor >> i) & 1;
                    maq.DigitalWrite(dados, bit);
                    maq.DigitalWrite(relogio, 1);
                    maq.DigitalWrite(relogio, 0);
                }
                return Valor.Vazio;
            }

            // std::to_string(42), std::stoi("42"): o "std::" ja saiu no lexer.
            case "to_string" when args.Count > 0:
                // O de float e double e o "%f": seis casas, sempre.
                return Valor.DeTexto(args[0].Inteiro
                    ? ((long)args[0].Num).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : args[0].Num.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
            case "stoi" or "stol" or "stoll" or "stoul" or "stoull" when args.Count > 0:
            {
                string t = args[0].Mostrar().TrimStart();
                int base10 = args.Count > 2 ? (int)args[2].Num : 10;
                int k = 0;
                if (k < t.Length && (t[k] == '-' || t[k] == '+')) k++;
                while (k < t.Length && Uri.IsHexDigit(t[k]) && Convert.ToInt32(t[k].ToString(), 16) < Math.Max(2, base10)) k++;
                if (k == 0 || k == 1 && t[0] is '-' or '+')
                    throw new ErroDeExecucao($"std::{fn.Nome}(\"{t}\"): nao ha numero no comeco do texto -- na placa isso "
                                             + "lanca std::invalid_argument, ninguem pega, e o abort() reinicia a placa.", c.Linha);
                long n = Convert.ToInt64(t[..k].TrimStart('+'), Math.Max(2, base10) is 2 or 8 or 16 ? base10 : 10);
                return DoTipo(n, fn.Nome switch { "stoi" => TipoC.Int, "stoul" => TipoC.ULong, "stoull" => TipoC.ULongLong,
                                                  "stoll" => TipoC.LongLong, _ => TipoC.Long });
            }
            case "stof" or "stod" or "stold" when args.Count > 0:
            {
                string t = args[0].Mostrar().Trim();
                int k = 0;
                while (k < t.Length && (char.IsDigit(t[k]) || t[k] is '.' or '-' or '+' or 'e' or 'E')) k++;
                if (!double.TryParse(t[..k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                    throw new ErroDeExecucao($"std::{fn.Nome}(\"{t}\"): nao ha numero no comeco do texto -- na placa isso "
                                             + "lanca std::invalid_argument e a placa reinicia.", c.Linha);
                return DoTipo(d, fn.Nome == "stof" ? TipoC.Float : TipoC.Double);
            }

            case "shiftIn":
            {
                int dados = (int)A(0), relogio = (int)A(1), ordem = (int)A(2);
                int lido = 0;
                for (int i = 0; i < 8; i++)
                {
                    maq.DigitalWrite(relogio, 1);
                    if (maq.DigitalRead(dados) != 0)
                        lido |= ordem != 0 ? 1 << (7 - i) : 1 << i;
                    maq.DigitalWrite(relogio, 0);
                }
                return Valor.DeInt(lido);
            }

            case "map":
            {
                double v = A(0), i1 = A(1), i2 = A(2), o1 = A(3), o2 = A(4);
                if (i2 - i1 == 0) throw new ErroDeExecucao("map() com faixa de entrada zero", c.Linha);
                return Valor.DeInt(Math.Truncate((v - i1) * (o2 - o1) / (i2 - i1) + o1));
            }
            // Macros no Arduino (e std::abs na ESP32): o tipo do valor fica.
            // "abs(-2.5)" e 2.5 -- antes virava 2, e a conta de distancia
            // do jogo andava em degraus.
            case "constrain":
                return Num(Math.Min(Math.Max(A(0), A(1)), A(2)), args.Take(3).All(x => x.Inteiro));
            case "abs": return Num(Math.Abs(A(0)), args.Count == 0 || args[0].Inteiro);
            case "min": return Num(Math.Min(A(0), A(1)), args.All(x => x.Inteiro));
            case "max": return Num(Math.Max(A(0), A(1)), args.All(x => x.Inteiro));
            case "sqrt": return Valor.DeReal(Math.Sqrt(A(0)));
            case "pow": return Valor.DeReal(Math.Pow(A(0), A(1)));

            // O round() do Arduino arredonda para o inteiro mais perto,
            // meio para cima -- e nao "para o par" como o padrao do .NET.
            case "round": return Valor.DeInt(Math.Round(A(0), MidpointRounding.AwayFromZero));
            case "floor": return Valor.DeInt(Math.Floor(A(0)));
            case "ceil": return Valor.DeInt(Math.Ceiling(A(0)));
            case "trunc": return Valor.DeInt(Math.Truncate(A(0)));
            case "fabs": return Valor.DeReal(Math.Abs(A(0)));
            case "fmod": return Valor.DeReal(A(0) - Math.Truncate(A(0) / A(1)) * A(1));
            case "sq": return Num(A(0) * A(0), args.All(x => x.Inteiro));
            case "exp": return Valor.DeReal(Math.Exp(A(0)));
            case "log": return Valor.DeReal(Math.Log(A(0)));
            case "log10": return Valor.DeReal(Math.Log10(A(0)));
            case "sin": return Valor.DeReal(Math.Sin(A(0)));
            case "cos": return Valor.DeReal(Math.Cos(A(0)));
            case "tan": return Valor.DeReal(Math.Tan(A(0)));
            case "atan2": return Valor.DeReal(Math.Atan2(A(0), A(1)));
            case "asin": return Valor.DeReal(Math.Asin(A(0)));
            case "acos": return Valor.DeReal(Math.Acos(A(0)));
            case "atan": return Valor.DeReal(Math.Atan(A(0)));
            case "hypot": return Valor.DeReal(Math.Sqrt(A(0) * A(0) + A(1) * A(1)));
            case "fmin": return Valor.DeReal(Math.Min(A(0), A(1)));
            case "fmax": return Valor.DeReal(Math.Max(A(0), A(1)));
            case "cbrt": return Valor.DeReal(Math.Cbrt(A(0)));
            case "copysign": return Valor.DeReal(Math.CopySign(A(0), A(1)));

            // As versoes em float do math.h: as mesmas contas, com o
            // resultado sempre real -- floorf(2.7) e 2.0, e nao o inteiro 2.
            case "sqrtf": return Valor.DeReal(Math.Sqrt(A(0)));
            case "powf": return Valor.DeReal(Math.Pow(A(0), A(1)));
            case "fabsf": return Valor.DeReal(Math.Abs(A(0)));
            case "fmodf": return Valor.DeReal(A(0) - Math.Truncate(A(0) / A(1)) * A(1));
            case "floorf": return Valor.DeReal(Math.Floor(A(0)));
            case "ceilf": return Valor.DeReal(Math.Ceiling(A(0)));
            case "roundf": return Valor.DeReal(Math.Round(A(0), MidpointRounding.AwayFromZero));
            case "truncf": return Valor.DeReal(Math.Truncate(A(0)));
            case "expf": return Valor.DeReal(Math.Exp(A(0)));
            case "logf": return Valor.DeReal(Math.Log(A(0)));
            case "log10f": return Valor.DeReal(Math.Log10(A(0)));
            case "sinf": return Valor.DeReal(Math.Sin(A(0)));
            case "cosf": return Valor.DeReal(Math.Cos(A(0)));
            case "tanf": return Valor.DeReal(Math.Tan(A(0)));
            case "asinf": return Valor.DeReal(Math.Asin(A(0)));
            case "acosf": return Valor.DeReal(Math.Acos(A(0)));
            case "atanf": return Valor.DeReal(Math.Atan(A(0)));
            case "atan2f": return Valor.DeReal(Math.Atan2(A(0), A(1)));
            case "hypotf": return Valor.DeReal(Math.Sqrt(A(0) * A(0) + A(1) * A(1)));
            case "fminf": return Valor.DeReal(Math.Min(A(0), A(1)));
            case "fmaxf": return Valor.DeReal(Math.Max(A(0), A(1)));
            case "radians": return Valor.DeReal(A(0) * Math.PI / 180);
            case "degrees": return Valor.DeReal(A(0) * 180 / Math.PI);

            // "makeKeymap(teclas)" so devolve o proprio vetor na
            // biblioteca de verdade; aqui o teclado le a tecla do
            // editor, entao o valor nem e usado.
            case "makeKeymap": return args.Count > 0 ? args[0] : Valor.Vazio;
            // random(max) e random(min, max), com o fim de fora. Faixa vazia
            // ou invertida devolve o minimo, como no WMath do Arduino.
            case "random":
            {
                sorteio ??= new Random(1234);
                long menor = args.Count >= 2 ? (long)A(0) : 0;
                long maior = args.Count >= 2 ? (long)A(1) : (long)A(0);
                return DoTipo(maior <= menor ? menor : sorteio.NextInt64(menor, maior), TipoC.Long);
            }

            // Semente nova, sequencia nova -- como na placa. Antes era
            // ignorada, e um dado eletronico com randomSeed(micros())
            // tirava sempre os mesmos numeros.
            case "randomSeed":
                sorteio = new Random(unchecked((int)(long)A(0)));
                return Valor.Vazio;

            // printf do C: na ESP32, na ESP8266 e no RP2040 ele sai pela
            // mesma porta do Serial (no SDK da Pico, e a saida padrao). No
            // AVR ele compila e nao sai em lugar nenhum -- la nao existe
            // stdout, e quem espera ver o texto precisa saber disso.
            case "printf":
            case "puts":
            case "putchar":
            {
                if (maq.Perfil.De8Bits)
                {
                    maq.Avisar($"{fn.Nome}() num {maq.Perfil.Processador} compila, mas nao sai em "
                               + "lugar nenhum: o AVR nao tem saida padrao. Use Serial.print.");
                    return Valor.DeInt(0);
                }
                string impresso = fn.Nome switch
                {
                    "printf" => Formatar(args),
                    "putchar" => ((char)((int)A(0) & 0xFF)).ToString(),
                    _ => (args.Count > 0 ? args[0].Mostrar() : "") + "\n",
                };
                Escrever(impresso, false);
                return Valor.DeInt(impresso.Length);
            }

            // "server.on(UriBraces("/led/{}"), ...)": a funcao so embrulha
            // o texto do caminho. Quem entende o "{}" e a rota.
            case "UriBraces": case "UriRegex": case "Uri":
                return args.Count > 0 ? args[0] : Valor.DeTexto("");

            case "configTime": case "configTzTime": return AjustarRelogio(args);

            // "ESP32PWM::allocateTimer(0)": a ESP32Servo reserva um dos
            // quatro temporizadores do LEDC. Aqui nao ha o que reservar --
            // o servo le o angulo direto --, mas o exemplo da biblioteca
            // comeca assim, e tem que passar.
            case "ESP32PWM::allocateTimer": return Valor.DeInt(0);

            // "rtc.adjust(DateTime(F(__DATE__), F(__TIME__)))": o DateTime
            // escrito como expressao, e nao como declaracao.
            case "DateTime": return Valor.DeReferencia(NovaData(args, c.Linha));

            // As contas do NeoPixel tambem existem sem objeto -- sao
            // estaticas na classe --, e os exemplos da Adafruit as chamam
            // assim.
            case "Adafruit_NeoPixel::Color":
                return Valor.DeInt(Fita.Montar((int)A(0), (int)A(1), (int)A(2)));
            case "Adafruit_NeoPixel::ColorHSV":
                return Valor.DeInt(Fita.Hsv((long)A(0), args.Count > 1 ? (int)A(1) : 255,
                                            args.Count > 2 ? (int)A(2) : 255));
            case "Adafruit_NeoPixel::gamma32": return Valor.DeInt(Fita.Gama32((long)A(0)));
            case "Adafruit_NeoPixel::gamma8": return Valor.DeInt(Fita.Gama8((int)A(0)));
            case "Adafruit_NeoPixel::sine8": return Valor.DeInt(Fita.Seno8((int)A(0)));

            // A temperatura de dentro do RP2040, no core da Pico para
            // Arduino. A conta e a do proprio core, a partir da leitura.
            case "analogReadTemp":
            {
                if (!EhRp2040)
                    throw new ErroDeExecucao("analogReadTemp() e do core da Raspberry Pi Pico: "
                        + "so as placas com RP2040 tem o sensor de temperatura dentro do chip.", c.Linha);
                double vref = args.Count > 0 ? A(0) : 3.3;
                double volts = maq.LeituraDoSensorDeTemperatura * vref / 4096.0;
                return Valor.DeReal(27.0 - (volts - 0.706) / 0.001721);
            }
            case "setenv": case "tzset": return Valor.Vazio;

            // "i++, j--": a virgula do C vale o ultimo.
            case "__virgula": return args.Count > 0 ? args[^1] : Valor.Vazio;

            // "delete sensor;"
            case "__apagar":
                if (args.Count > 0)
                {
                    Apagar(args[0]);
                    SoltarNovo(args[0].Referencia);
                }
                return Valor.Vazio;
        }

        // Em Python, chamar uma classe monta o objeto: "Botao(14)", "ValueError('x')".
        if (EhPython && !funcoes.ContainsKey(fn.Nome) && InstanciarPython(fn.Nome, args, amb, c.Linha) is { } instancia)
            return instancia;

        // funcao definida pelo aluno
        if (funcoes.TryGetValue(fn.Nome, out var f))
        {
            CobrarChamada(args.Count);
            var local = new Ambiente(global);
            for (int i = 0; i < f.Params.Count; i++)
            {
                // Argumento que nao veio: em Python pode ter valor
                // padrao ("def piscar(pino, vezes=3)"); em C vira zero.
                var valor = i < args.Count ? Coagir(args[i], f.Params[i].Tipo)
                          : f.Params[i].Padrao is not null ? Avaliar(f.Params[i].Padrao, local)
                          : Valor.DeInt(0);

                // "void enviar(String msg)": a copia da String e uma String nova.
                if (f.Params[i].Tipo == "String" && !f.Params[i].PorReferencia)
                    valor = DeclararTexto(local, f.Params[i].Nome, valor, c.Linha);
                local.Declarar(f.Params[i].Nome, valor);
            }

            chamadasAbertas++;
            long quadro = 0;
            try
            {
                if (PilhaCheia()) EstourarPilha(c.Linha);
                quadro = EntrarNaPilha(f, c.Linha);
                // "return 2.5" numa funcao int devolve 2: o valor vai para o tipo
                // da funcao, como o metodo de classe ja fazia.
                if (ExecCorpoDeFuncao(f.Corpo, local, out var devolvido)) return Retornar(devolvido, f);
            }
            finally
            {
                chamadasAbertas--;
                FecharEscopo(local);
                SairDaPilha(quadro);
            }
            return Valor.Vazio;
        }

        // Um ponteiro de funcao que ainda nao recebeu funcao nenhuma.
        if (amb.Buscar(fn.Nome, out var vazio) && !vazio.EhTexto && vazio.Referencia is null)
            throw new ErroDeExecucao(
                $"'{fn.Nome}' e uma variavel (vale {vazio.Mostrar()}), e nao uma funcao. Se ela guarda "
                + "um ponteiro de funcao, ainda nao recebeu nenhuma -- na placa, isso trava o chip.", c.Linha);

        // Uma funcao do C nativo (os .c do projeto e das bibliotecas).
        if (TemNativa(fn.Nome, out var nativa)) return ChamarNativa(fn.Nome, nativa, args, c.Linha);

        // "SPISettings(1000000, MSBFIRST, SPI_MODE0)", "IPAddress(10, 0, 0, 1)":
        // o construtor de uma classe de biblioteca escrito como expressao e um
        // objeto temporario, como no C++.
        if (Bibliotecas.Existe(fn.Nome))
        {
            var provisorio = new Ambiente(amb);
            ExecDeclObj(new CmdDeclObj(fn.Nome, "__temporario", c.Args, c.Linha), provisorio);
            if (provisorio.TirarObjeto("__temporario", out var temporario))
                return temporario.Classe == "IPAddress" ? ValorDoObjeto(temporario) : Valor.DeReferencia(temporario);
        }

        throw new ErroDeExecucao($"funcao '{fn.Nome}()' ainda nao e suportada pelo simulador", c.Linha);
    }

    Valor ChamarSerial(string metodo, Chamada c, Ambiente amb)
    {
        // "Serial.println(&agora, "%H:%M:%S")": o primeiro argumento e
        // um endereco, e avalia-lo como valor daria erro de variavel.
        if (metodo is "print" or "println" && TextoDeHorario(c, amb, out string doRelogio))
        {
            Escrever(doRelogio, metodo == "println");
            return Valor.DeInt(doRelogio.Length);
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();

        switch (metodo)
        {
            // O baud importa agora: e com ele que os bytes saem pelo pino TX
            // -- o que o analisador logico mostra.
            case "begin":
                maq.BaudDaSerial = args.Count > 0 ? (int)args[0].Num : 9600;
                LigarPorta(PortaGlobal("Serial", c.Linha), maq.BaudDaSerial);
                return Valor.Vazio;
            case "end":
                maq.BaudDaSerial = 0;
                PortaGlobal("Serial", c.Linha).Ligada = false;
                return Valor.Vazio;
            case "print":
            case "println":
            {
                string impresso = Imprimivel(args);
                CobrarImpressao(impresso, args.Count > 0 && !args[0].EhTexto && !args[0].Caractere);
                Escrever(impresso, metodo == "println");
                SerialPeloFio(metodo, args, impresso + (metodo == "println" ? "\r\n" : ""));
                return Valor.DeInt(TextoArduino.ParaBytes(impresso).Length + (metodo == "println" ? 2 : 0));
            }

            // flush() espera a fila da UART esvaziar -- na placa ele trava
            // ate o ultimo byte sair pelo fio.
            case "flush":
                maq.EsvaziarSerial();
                return Valor.Vazio;
            case "printf":
            {
                string formatado = Formatar(args);
                Escrever(formatado, false);
                SerialPeloFio(metodo, args, formatado);
                return Valor.Vazio;
            }

            // write() manda um BYTE: "Serial.write(SerialBT.read())" repete
            // a letra, em vez de imprimir o numero dela.
            case "write":
            {
                string escrito = ComoEscrita(args);
                Escrever(escrito, false);
                SerialPeloFio(metodo, args, escrito);
                return Valor.Vazio;
            }
        }

        // available/read/readString/parseInt: o que foi digitado no monitor
        // -- e o que chegou pelo pino RX, de outra placa.
        if (maq.LigadosAoPino is not null) PortaGlobal("Serial", c.Linha).Atualizar();
        if (ChamarFluxo(maq.EntradaSerial, metodo, args, out var lido)) return lido;

        throw new ErroDeExecucao($"Serial.{metodo}() ainda nao e suportado", c.Linha);
    }

    Valor ChamarObjeto(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        if (obj.Escopo is not null && classes.TryGetValue(obj.Classe, out var daClasse))
            return ChamarObjetoDeClasse(obj, daClasse, metodo, c, amb);

        // "lcd.println(&agora, "%H:%M:%S")": o endereco de um struct tm
        // no lugar do texto. E a forma do exemplo de relogio do core.
        if (obj.Lcd is not null && metodo is "print" or "println"
            && TextoDeHorario(c, amb, out string doRelogio))
        {
            obj.Lcd.Escrever(doRelogio);
            if (metodo == "println") obj.Lcd.PularLinha();
            return Valor.DeInt(doRelogio.Length);
        }

        // "irrecv.decode(&results)" vem antes da avaliacao dos
        // argumentos: "results" e um OBJETO, nao uma variavel, e avaliar
        // dava "variavel 'results' nao foi declarada". Mesmo caso do
        // attachInterrupt, que recebe o nome de uma funcao.
        if (obj.Classe == "IRrecv" && metodo == "decode")
            return DecodificarIr(c, amb);
        if (obj.Classe == "Adafruit_MPU6050" && metodo == "getEvent")
            return EventoDoMpu(c, amb);

        if (obj.Classe == "RF24") return ChamarRf24(obj, metodo, c, amb);
        if (obj.Classe == "SPIClass" && obj.Rede is BarramentoDoSketch barramento) return ChamarSpi(barramento, metodo, c, amb);
        if (obj.Classe is "Adafruit_ILI9341" or "Adafruit_ST7789" or "Adafruit_ST7735") return ChamarTft(obj, metodo, c, amb);
        if (obj.Classe is "Adafruit_SSD1306" or "Adafruit_SH1106G") return ChamarOled(obj, metodo, c, amb);
        if (obj.Classe == "TM1637Display") return ChamarTm1637(obj, metodo, c, amb);
        if (obj.Classe == "LedControl") return ChamarMax7219(obj, metodo, c, amb);
        if (obj.Classe == "Keypad") return ChamarTeclado(obj, metodo, c, amb);
        if (obj.Classe == "Stepper") return ChamarMotorDePasso(obj, metodo, c, amb);
        if (obj.Classe == "UniversalTelegramBot") return ChamarTelegram(obj, metodo, c, amb);
        if (obj.Classe == "Preferences") return ChamarPreferencias(obj, metodo, c, amb);

        // As de rede cuidam dos proprios argumentos: algumas recebem o NOME
        // de uma funcao (server.on, client.setCallback).
        if (EhClasseDeRede(obj.Classe)) return ChamarRede(obj, metodo, c, amb);

        // A BLE tambem: o setCallbacks recebe um objeto do sketch, e o
        // setValue olha o "(uint8_t*)&valor" escrito.
        if (obj.Rede is PortaSerial porta) return ChamarPorta(porta, metodo, c, amb);
        if (obj.Classe == "TwoWire") return ChamarTwoWire(obj, metodo, c, amb);
        if (obj.Rede is ServicoArduino or CaracteristicaArduino or DispositivoArduino or DescritorArduino)
            return ChamarObjetoDaArduinoBle(obj, metodo, c, amb);
        if (EhClasseBle(obj.Classe)) return ChamarBle(obj, metodo, c, amb);

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        double A(int i) => i < args.Count ? args[i].Num : 0;

        switch (obj.Classe)
        {
            // ---------- DHT: temperatura e umidade ----------
            case "DHT":
                switch (metodo)
                {
                    case "begin": return Valor.Vazio;
                    case "readTemperature":
                        return Valor.DeReal(maq.Leitura($"{obj.Pino}:temperatura", 25));
                    case "readHumidity":
                        return Valor.DeReal(maq.Leitura($"{obj.Pino}:umidade", 50));
                    case "computeHeatIndex":
                        return Valor.DeReal(maq.Leitura($"{obj.Pino}:temperatura", 25));
                }
                break;

            // ---------- NeoPixel ----------
            case "Adafruit_NeoPixel":
                switch (metodo)
                {
                    case "begin": case "updateType": case "setPin": return Valor.Vazio;

                    // E o show() que acende: ate ele, o que o sketch escreveu
                    // fica so na memoria, como na fita de verdade.
                    case "show": obj.Fita?.Mostrar(); return Valor.Vazio;
                    case "canShow": return Valor.DeBool(true);
                    case "clear": obj.Fita?.Limpar(); return Valor.Vazio;
                    case "numPixels": return Valor.DeInt(obj.Fita?.Quantidade ?? 0);
                    case "setBrightness":
                        if (obj.Fita is not null) obj.Fita.Brilho = (int)A(0) & 0xFF;
                        return Valor.Vazio;
                    case "getBrightness": return Valor.DeInt(obj.Fita?.Brilho ?? 0);
                    case "Color":
                        return Valor.DeInt(Fita.Montar((int)A(0), (int)A(1), (int)A(2)));
                    case "ColorHSV":
                        return Valor.DeInt(Fita.Hsv((long)A(0), args.Count > 1 ? (int)A(1) : 255,
                                                    args.Count > 2 ? (int)A(2) : 255));
                    case "gamma32": return Valor.DeInt(Fita.Gama32((long)A(0)));
                    case "gamma8": return Valor.DeInt(Fita.Gama8((int)A(0)));
                    case "sine8": return Valor.DeInt(Fita.Seno8((int)A(0)));
                    case "getPixelColor": return Valor.DeInt(obj.Fita?.Ler((int)A(0)) ?? 0);
                    case "setPixelColor":
                        // setPixelColor(i, cor) ou setPixelColor(i, r, g, b)
                        obj.Fita?.Definir((int)A(0), args.Count >= 4
                            ? Fita.Montar((int)A(1), (int)A(2), (int)A(3))
                            : (int)(long)A(1));
                        return Valor.Vazio;
                    case "fill":
                    {
                        // fill(cor, primeiro, quantos): quantos = 0 vai ate o fim.
                        int total = obj.Fita?.Quantidade ?? 0;
                        int cor = (int)(long)A(0);
                        int primeiro = args.Count > 1 ? (int)A(1) : 0;
                        int quantos = args.Count > 2 && (int)A(2) > 0 ? (int)A(2) : total - primeiro;
                        for (int k = primeiro; k < Math.Min(total, primeiro + quantos); k++)
                            obj.Fita.Definir(k, cor);
                        return Valor.Vazio;
                    }
                    case "rainbow":
                    {
                        // rainbow(primeira_matiz, voltas, saturacao, brilho, gama):
                        // o arco-iris da propria biblioteca, LED a LED.
                        int total = obj.Fita?.Quantidade ?? 0;
                        long primeira = args.Count > 0 ? (long)A(0) & 0xFFFF : 0;
                        int voltas = args.Count > 1 ? (int)A(1) : 1;
                        int saturacao = args.Count > 2 ? (int)A(2) : 255;
                        int brilho = args.Count > 3 ? (int)A(3) : 255;
                        bool gama = args.Count <= 4 || A(4) != 0;
                        for (int k = 0; k < total; k++)
                        {
                            long matiz = (primeira + k * voltas * 65536L / total) & 0xFFFF;
                            int cor = Fita.Hsv(matiz, saturacao, brilho);
                            obj.Fita.Definir(k, gama ? Fita.Gama32(cor) : cor);
                        }
                        return Valor.Vazio;
                    }
                }
                break;

            // ---------- Motor de passo ----------
            // ---------- Acelerometro ----------
            case "Adafruit_MPU6050":
            case "MPU6050":
                switch (metodo)
                {
                    // O begin() pergunta se o sensor responde no barramento que recebeu.
                    case "begin": return Valor.DeBool(SensorNoI2c(obj, args, c.Linha, true));
                    case "initialize": SensorNoI2c(obj, args, c.Linha, false); return Valor.Vazio;
                    case "testConnection": return Valor.DeBool(SensorNoI2c(obj, [], c.Linha, false));
                    case "getAccelerationX": return Valor.DeInt(maq.Leitura("mpu:ax"));
                    case "getAccelerationY": return Valor.DeInt(maq.Leitura("mpu:ay"));
                    case "getAccelerationZ": return Valor.DeInt(maq.Leitura("mpu:az", 16384));
                    case "readTemperature": return Valor.DeReal(maq.Leitura("mpu:temp", 25));
                    case "getRotationX": return Valor.DeInt(maq.Leitura("mpu:gx"));
                    case "getRotationY": return Valor.DeInt(maq.Leitura("mpu:gy"));
                    case "getRotationZ": return Valor.DeInt(maq.Leitura("mpu:gz"));

                    // A configuracao da Adafruit_MPU6050: guarda, e devolve no get.
                    case "setAccelerometerRange": case "setGyroRange": case "setFilterBandwidth":
                    case "setHighPassFilter": case "setMotionDetectionThreshold": case "setMotionDetectionDuration":
                    case "setInterruptPinLatch": case "setInterruptPinPolarity": case "setMotionInterrupt":
                    case "setSampleRateDivisor": case "setCycleRate": case "enableSleep": case "enableCycle":
                    case "reset": case "setClock": case "setTemperatureStandby":
                        if (args.Count > 0) obj.Campos["_" + metodo[3..]] = args[0];
                        return Valor.Vazio;
                    case "getAccelerometerRange": return obj.Campos.GetValueOrDefault("_AccelerometerRange", Valor.DeInt(0));
                    case "getGyroRange": return obj.Campos.GetValueOrDefault("_GyroRange", Valor.DeInt(0));
                    case "getFilterBandwidth": return obj.Campos.GetValueOrDefault("_FilterBandwidth", Valor.DeInt(0));
                    case "getMotionInterruptStatus": return Valor.DeBool(false);
                }
                break;

            // ---------- Relogio (RTClib) ----------
            //
            // O relogio anda sozinho, no tempo da placa: now() e a hora em
            // que ele foi acertado mais o que passou desde entao. Antes o
            // now() devolvia so os segundos desde o boot, e o
            // "agora.hour()" do exemplo da biblioteca nao tinha onde
            // funcionar.
            case "RTC_DS1307":
            {
                long agora = (long)obj.Campos.GetValueOrDefault("_base", Valor.DeInt(0)).Num + maq.Ms / 1000;
                switch (metodo)
                {
                    case "begin": return Valor.DeBool(SensorNoI2c(obj, args, c.Linha, false));
                    case "isrunning": return Valor.DeBool(true);
                    case "lostPower": return Valor.DeBool(false);
                    case "adjust":
                        if (args.Count > 0 && args[0].Referencia is Objeto { Classe: "DateTime" } nova)
                            obj.Campos["_base"] = Valor.DeInt(SegundosDaData(nova) - maq.Ms / 1000);
                        return Valor.Vazio;
                    case "now": return Valor.DeReferencia(DataEm(agora));
                    case "getTemperature": return Valor.DeReal(25.0);
                    case "readSqwPinMode": return Valor.DeInt(0);
                    case "writeSqwPinMode": return Valor.Vazio;
                }
                break;
            }

            case "DateTime":
            {
                var data = DateTimeOffset.FromUnixTimeSeconds(SegundosDaData(obj)).UtcDateTime;
                switch (metodo)
                {
                    case "year": return Valor.DeInt(data.Year);
                    case "month": return Valor.DeInt(data.Month);
                    case "day": return Valor.DeInt(data.Day);
                    case "hour": return Valor.DeInt(data.Hour);
                    case "minute": return Valor.DeInt(data.Minute);
                    case "second": return Valor.DeInt(data.Second);
                    case "dayOfTheWeek": return Valor.DeInt((int)data.DayOfWeek);   // 0 = domingo
                    case "unixtime": return Valor.DeInt(SegundosDaData(obj));
                    case "secondstime": return Valor.DeInt(SegundosDaData(obj) - 946684800);
                    case "twelveHour": return Valor.DeInt(data.Hour % 12 == 0 ? 12 : data.Hour % 12);
                    case "isPM": return Valor.DeBool(data.Hour >= 12);
                    case "isValid": return Valor.DeBool(true);
                    case "timestamp": return Valor.DeTexto(data.ToString("yyyy-MM-dd'T'HH:mm:ss"));
                }
                break;
            }

            // ---------- Celula de carga (HX711) ----------
            //
            // Como a biblioteca: read() devolve a contagem crua do conversor
            // de 24 bits -- que NAO e grama --, tare() guarda o zero e
            // set_scale() diz quantas contagens valem uma unidade. Antes o
            // get_units() ja saia em gramas, e o sketch sem calibracao
            // funcionava aqui e mostrava um numero sem sentido na bancada.
            case "HX711":
            {
                long cru = ContagemDoHx711();
                double zero = obj.Campos.GetValueOrDefault("_zero", Valor.DeInt(0)).Num;
                double escala = obj.Campos.GetValueOrDefault("_escala", Valor.DeReal(1)).Num;
                if (escala == 0) escala = 1;

                switch (metodo)
                {
                    case "begin": case "power_up": case "power_down": case "set_gain":
                        return Valor.Vazio;
                    case "is_ready": case "wait_ready": case "wait_ready_retry": case "wait_ready_timeout":
                        return Valor.DeBool(true);
                    case "tare": obj.Campos["_zero"] = Valor.DeInt(cru); return Valor.Vazio;
                    case "set_scale":
                        obj.Campos["_escala"] = Valor.DeReal(args.Count > 0 ? A(0) : 1);
                        return Valor.Vazio;
                    case "get_scale": return Valor.DeReal(escala);
                    case "set_offset": obj.Campos["_zero"] = Valor.DeInt((long)A(0)); return Valor.Vazio;
                    case "get_offset": return Valor.DeInt((long)zero);
                    case "read": case "read_average": return Valor.DeInt(cru);
                    case "get_value": return Valor.DeReal(cru - zero);
                    case "get_units": return Valor.DeReal((cru - zero) / escala);
                }
                break;
            }

            // ---------- Receptor infravermelho ----------
            // ---------- receptor infravermelho ----------
            //
            // O jeito classico de usar, e o que aparece em aula:
            //
            //   IRrecv irrecv(PINO);  decode_results results;
            //   if (irrecv.decode(&results)) { ... results.value ... }
            //   irrecv.resume();
            //
            // O codigo vem do controle remoto que esta na tela, por
            // Maquina.Leituras["ir:codigo"]. Sem controle no circuito
            // nao chega nada -- que e o certo: receptor sozinho nao
            // recebe.
            case "IRrecv":
                switch (metodo)
                {
                    case "enableIRIn": case "start": case "begin":
                        return Valor.Vazio;

                    case "resume":
                        // Consome: o mesmo aperto nao pode ser lido duas
                        // vezes, senao um clique vira dez.
                        maq.Leituras["ir:codigo"] = 0;
                        return Valor.Vazio;

                    // IRremote 3.x: "IrReceiver.decodedIRData.command".
                    case "decodedValue": return Valor.DeInt(maq.Leitura("ir:codigo"));
                }
                break;

            // decode_results existe so para guardar o que o decode()
            // escreveu; nao tem metodo nenhum.
            case "decode_results":
                break;

            // ---------- arquivo do cartao SD ----------
            case "File":
                switch (metodo)
                {
                    case "println":
                        obj.Arquivo?.EscreverLinha(args.Count > 0 ? args[0].Mostrar() : "");
                        return Valor.Vazio;
                    case "print":
                        obj.Arquivo?.Escrever(args.Count > 0 ? args[0].Mostrar() : "");
                        return Valor.Vazio;
                    case "write":
                        obj.Arquivo?.Escrever(args.Count > 0 ? args[0].Mostrar() : "");
                        return Valor.Vazio;
                    case "close": obj.Arquivo?.Fechar(); return Valor.Vazio;
                    case "available": return Valor.DeInt(obj.Arquivo?.Disponivel() ?? 0);
                    case "read": return Valor.DeInt(obj.Arquivo?.Ler() ?? -1);
                    // Do C++ compilado: os bytes que nao sao texto (o arquivo
                    // binario), um caractere por byte, como o cartao guarda.
                    case "__escrever_bytes":
                        if (args.Count > 0 && args[0].Referencia is Valor[] bytes)
                            obj.Arquivo?.Escrever(new string([.. bytes.Select(b => (char)((long)b.Num & 0xFF))]));
                        return Valor.Vazio;
                    // Do C++ compilado: um bloco de bytes de uma vez (o read(buf, n)
                    // de um MP3 de varios MB nao cabe numa chamada por byte).
                    case "__ler_bytes":
                    {
                        if (args.Count == 0 || args[0].Referencia is not Valor[] destino) return Valor.DeInt(0);
                        int k = 0;
                        for (; k < destino.Length; k++)
                        {
                            int lido = obj.Arquivo?.Ler() ?? -1;
                            if (lido < 0) break;
                            destino[k] = Valor.DeC(lido & 0xFF, TipoC.UChar);
                        }
                        return Valor.DeInt(k);
                    }
                    case "readStringUntil":
                    case "readString":
                        return Valor.DeTexto(obj.Arquivo?.LerLinha() ?? "");
                    case "size": return Valor.DeInt(obj.Arquivo?.Tamanho() ?? 0);
                    case "name": return Valor.DeTexto(obj.Arquivo?.Nome ?? "");
                    case "path": return Valor.DeTexto("/" + (obj.Arquivo?.Nome ?? ""));
                    case "flush": return Valor.Vazio;
                    case "isDirectory": return Valor.DeBool(obj.Arquivo?.EhPasta ?? false);
                    case "openNextFile":
                        arquivoRecemAberto = obj.Arquivo?.ProximoDaPasta();
                        return Valor.DeBool(arquivoRecemAberto is not null);
                    case "rewindDirectory": obj.Arquivo?.VoltarAoComecoDaPasta(); return Valor.Vazio;
                    case "seek":
                        return Valor.DeBool(obj.Arquivo?.Posicionar(args.Count > 0 ? (long)args[0].Num : 0, args.Count > 1 ? (int)args[1].Num : 0) ?? false);
                    case "position": return Valor.DeInt(obj.Arquivo?.Posicao() ?? 0);
                    case "peek": return Valor.DeInt(obj.Arquivo?.Espiar() ?? -1);
                    case "getLastWrite": return Valor.DeInt(0);
                }
                break;

        }

        if (obj.Classe == "Servo")
        {
            switch (metodo)
            {
                // attach(pino) ou attach(pino, min, max): os limites do
                // pulso sao os da biblioteca de verdade -- 544 e 2400 us
                // quando o sketch nao diz. E com eles que o angulo e o
                // pulso se convertem, em write e em writeMicroseconds.
                case "attach":
                    obj.Pino = (int)A(0);
                    obj.Campos["_minUs"] = Valor.DeInt(args.Count >= 3 ? (int)A(1) : 544);
                    obj.Campos["_maxUs"] = Valor.DeInt(args.Count >= 3 ? (int)A(2) : 2400);
                    maq.EscreverServo(obj.Pino, 90);   // repouso, como o real
                    return Valor.DeInt(obj.Pino >= 0 ? 1 : 0);

                // write(angulo) -- mas de 544 para cima o valor ja e o pulso
                // em microssegundos, como na Servo.h: "servo.write(1500)" e
                // o meio, e nao um angulo de mil e quinhentos graus.
                case "write":
                {
                    double v = A(0);
                    if (v >= 544) EscreverPulso(obj, v);
                    else
                    {
                        var (min, max) = LimitesDoServo(obj);
                        int angulo = (int)Math.Clamp(v, 0, 180);
                        maq.EscreverServo(obj.Pino, angulo, min + angulo * (max - min) / 180.0);
                    }
                    return Valor.Vazio;
                }

                case "writeMicroseconds":
                    EscreverPulso(obj, A(0));
                    return Valor.Vazio;

                case "readMicroseconds":
                {
                    var (min, max) = LimitesDoServo(obj);
                    int angulo = obj.Pino >= 0 ? Math.Max(0, maq.AnguloServo[obj.Pino]) : 0;
                    return Valor.DeInt((int)Math.Round(min + angulo * (max - min) / 180.0));
                }

                // A frequencia do PWM do servo (ESP32Servo). O angulo nao
                // depende dela aqui.
                case "setPeriodHertz":
                    return Valor.Vazio;

                case "read":
                    return Valor.DeInt(obj.Pino >= 0 ? Math.Max(0, maq.AnguloServo[obj.Pino]) : 0);

                case "detach":
                    if (obj.Pino >= 0) maq.SoltarServo(obj.Pino);
                    return Valor.Vazio;

                case "attached":
                    return Valor.DeBool(obj.Pino >= 0);

                default:
                    throw new ErroDeExecucao($"Servo.{metodo}() nao e suportado", c.Linha);
            }
        }

        if (obj.Lcd is null)
            throw new ErroDeExecucao($"objeto '{obj.Classe}' sem display associado", c.Linha);

        switch (metodo)
        {
            case "begin":
                // O modulo de I2C liga o Wire sozinho (o init_priv() da biblioteca).
                if (obj.Classe == "LiquidCrystal_I2C") ComecarBarramento(maq.I2c, "Wire");
                obj.Lcd.Begin(args.Count > 0 ? (int)A(0) : 16, args.Count > 1 ? (int)A(1) : 2);
                return Valor.Vazio;
            case "clear": obj.Lcd.Limpar(); return Valor.Vazio;
            case "home": obj.Lcd.PosicionarCursor(0, 0); return Valor.Vazio;
            case "setCursor": obj.Lcd.PosicionarCursor((int)A(0), (int)A(1)); return Valor.Vazio;
            // "lcd.print(temperatura, 1)": o segundo argumento e o numero de
            // casas (ou a base, com HEX e BIN). Era ignorado, e o LCD
            // mostrava "23.50" onde a placa mostra "23.5" -- o texto nao
            // cabia na conta de colunas que o aluno fez.
            case "print":
                obj.Lcd.Escrever(Imprimivel(args));
                return Valor.Vazio;
            // "lcd.write(0)" escreve o CARACTERE 0 (o primeiro do createChar),
            // e nao o texto "0"; "lcd.write("oi")" escreve o texto.
            case "write":
                if (args.Count > 0 && !args[0].EhTexto) obj.Lcd.Escrever(((char)((long)args[0].Num & 0xFF)).ToString());
                else obj.Lcd.Escrever(args.Count > 0 ? args[0].Mostrar() : "");
                return Valor.Vazio;

            // "lcd.createChar(0, coracao)": as oito linhas do desenho (5 bits cada).
            case "createChar":
            {
                if (c.Args.Count < 2 || !VetorDoArgumento(c.Args[1], amb, out var linhas))
                    throw new ErroDeExecucao("lcd.createChar(n, desenho): o desenho tem que ser um vetor de 8 bytes", c.Linha);
                obj.Lcd.DefinirGlifo((int)A(0), [.. linhas.Select(v => (int)(long)v.Num)]);
                return Valor.Vazio;
            }

            case "backlight": obj.Lcd.Luz(true); return Valor.Vazio;
            case "noBacklight": obj.Lcd.Luz(false); return Valor.Vazio;
            case "setBacklight": obj.Lcd.Luz(args.Count == 0 || A(0) != 0); return Valor.Vazio;
            case "display": obj.Lcd.Mostrar(true); return Valor.Vazio;
            case "noDisplay": obj.Lcd.Mostrar(false); return Valor.Vazio;

            // O println() do display desce o cursor para a linha
            // seguinte, como o Print do Arduino faz. O relogio de NTP
            // depende disso: ele escreve a hora numa linha e a data na
            // outra, sem chamar setCursor entre as duas.
            case "println":
                obj.Lcd.Escrever(Imprimivel(args));
                obj.Lcd.PularLinha();
                return Valor.Vazio;
            // init() e o begin() do modulo de I2C: e ele que liga o
            // display. Sem isto o painel ficava apagado num sketch que
            // so chama init() -- que e o normal do LiquidCrystal_I2C.
            case "init":
                if (obj.Classe == "LiquidCrystal_I2C") ComecarBarramento(maq.I2c, "Wire");
                obj.Lcd.Begin(0, 0);
                return Valor.Vazio;

            case "cursor": case "noCursor":
            case "blink": case "noBlink":
                return Valor.Vazio;
            default:
                throw new ErroDeExecucao(
                    $"'{obj.Classe}.{metodo}()' ainda nao e suportado", c.Linha);
        }
    }

    string pendente = "";

    /// <summary>A hora em que o sketch "foi compilado": a do __DATE__ e do __TIME__.</summary>
    readonly DateTime compilado = DateTime.Now;

    DateTime Compilado => compilado;

    // ---------------- datas do RTClib ----------------

    /// <summary>Um DateTime do RTClib com estes segundos desde 1970.</summary>
    static Objeto DataEm(long segundos)
    {
        var data = new Objeto("DateTime");
        data.Campos["_unix"] = Valor.DeInt(segundos);
        return data;
    }

    static long SegundosDaData(Objeto data)
        => (long)data.Campos.GetValueOrDefault("_unix", Valor.DeInt(946684800)).Num;

    /// <summary>
    /// Os construtores do DateTime: vazio (1/1/2000), segundos desde 1970,
    /// ano-mes-dia-hora-minuto-segundo, ou os textos do __DATE__ e do
    /// __TIME__ -- "Sep 24 2026" e "14:05:09".
    /// </summary>
    static Objeto NovaData(List<Valor> args, int linha)
    {
        if (args.Count == 0) return DataEm(946684800);

        if (args.Count == 1 && !args[0].EhTexto)
        {
            if (args[0].Referencia is Objeto { Classe: "DateTime" } copia) return DataEm(SegundosDaData(copia));
            return DataEm((long)args[0].Num);
        }

        if (args[0].EhTexto)
        {
            var cultura = System.Globalization.CultureInfo.InvariantCulture;
            string texto = args.Count > 1 ? $"{args[0].Texto} {args[1].Texto}" : args[0].Texto;
            texto = System.Text.RegularExpressions.Regex.Replace(texto.Trim(), @"\s+", " ");

            if (DateTime.TryParseExact(texto, ["MMM d yyyy HH:mm:ss", "MMM d yyyy",
                                               "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss"],
                                       cultura, System.Globalization.DateTimeStyles.AssumeUniversal
                                                | System.Globalization.DateTimeStyles.AdjustToUniversal,
                                       out var lida))
                return DataEm(new DateTimeOffset(lida, TimeSpan.Zero).ToUnixTimeSeconds());

            throw new ErroDeExecucao($"DateTime(\"{texto}\"): nao entendi a data", linha);
        }

        int N(int k) => k < args.Count ? (int)args[k].Num : 0;
        int ano = N(0) < 100 ? 2000 + N(0) : N(0);
        try
        {
            var data = new DateTime(ano, Math.Max(1, N(1)), Math.Max(1, N(2)), 0, 0, 0, DateTimeKind.Utc)
                .AddHours(N(3)).AddMinutes(N(4)).AddSeconds(N(5));
            return DataEm(new DateTimeOffset(data).ToUnixTimeSeconds());
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ErroDeExecucao($"DateTime({ano}, {N(1)}, {N(2)}): essa data nao existe", linha);
        }
    }

    /// <summary>
    /// A contagem crua do HX711: o zero da celula mais 420 contagens por
    /// grama -- o que uma celula de 5 kg (1 mV/V) da com o ganho de 128.
    /// E por isso que o set_scale(420) dos tutoriais faz o get_units()
    /// sair em gramas.
    /// </summary>
    long ContagemDoHx711() => (long)Math.Round(84213 + maq.Leitura("hx711:peso") * 420);

    void Escrever(string txt, bool quebraLinha)
    {
        maq.EnviarPelaSerial(quebraLinha ? txt + "\r\n" : txt);
        pendente += txt;
        if (quebraLinha) pendente += "\n";

        // Cada "\n" fecha uma linha do monitor, inclusive o que vem no meio
        // do texto -- e o caso do Serial.printf("t=%d\n", t) do ESP32.
        int quebra;
        while ((quebra = pendente.IndexOf('\n')) >= 0)
        {
            string linha = pendente[..quebra].TrimEnd('\r');
            maq.EscreverSerial($"[{maq.Ms,7}ms] {TextoArduino.Corrigir(linha)}");
            pendente = pendente[(quebra + 1)..];
        }
    }
}
