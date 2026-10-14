using System.Runtime.CompilerServices;

namespace Simulador;

/// <summary>
/// A RAM da placa no interpretador: o malloc que devolve NULL quando acaba,
/// o new que (na ESP32) derruba a placa, a pilha que desce a cada funcao
/// chamada -- e as funcoes que o sketch usa para medir tudo isso, cada uma
/// com o numero da placa (ver <see cref="MemoriaDaPlaca"/>).
///
/// Antes o ESP.getFreeHeap() devolvia 245000 fixo, o malloc() nem existia e
/// a recursao so parava num limite de chamadas do simulador. Um vetor de
/// 1500 bytes local num Uno (que tem 2048 de RAM) passava; na placa ele
/// atropela as globais.
/// </summary>
public partial class Interpretador
{
    MemoriaDaPlaca Ram => maq.Memoria;

    /// <summary>Cada bloco da heap: o custo dele, se e da PSRAM, se ja foi solto.</summary>
    sealed class BlocoDaHeap
    {
        public long Custo;
        public bool Psram;
        public bool Solto;

        /// <summary>Veio do malloc (void*): o primeiro cast diz o tipo dos itens.</summary>
        public bool SemTipo;

        /// <summary>O tipo do ponteiro que o cast deu ("int*"), para o realloc manter.</summary>
        public string Ponteiro;
    }

    readonly ConditionalWeakTable<object, BlocoDaHeap> blocos = new();
    readonly Dictionary<Funcao, long> quadros = new(ReferenceEqualityComparer.Instance);

    /// <summary>Um bloco da heap nunca e mais que isto de itens no simulador (cada item custa ~40 bytes no PC).</summary>
    const long MaiorBlocoGuardado = 4_000_000;

    /// <summary>
    /// As globais do sketch com o core -- o que a IDE chama de "Global
    /// variables" (ver <see cref="Memoria.Estimar"/>).
    ///
    /// Na ESP32 classica ha mais uma conta: o core devolve para a heap os ~55
    /// KB que o controlador Bluetooth reserva (esp_bt_controller_mem_release no
    /// initArduino) -- mas so quando o sketch NAO usa Bluetooth. Com BLE ou
    /// BluetoothSerial, essa memoria continua presa desde o boot.
    /// </summary>
    void PrepararMemoria()
    {
        long estatica = Memoria.Estimar(prog, maq.Perfil).Ram;
        if (maq.Perfil?.Processador == "Xtensa LX6" && UsaBluetooth()) estatica += 56_156;
        Ram.DefinirEstatica(estatica);
    }

    bool UsaBluetooth()
    {
        string texto = prog?.Fonte ?? "";
        return texto.Contains("BLEDevice", StringComparison.Ordinal)
               || texto.Contains("BluetoothSerial", StringComparison.Ordinal)
               || texto.Contains("NimBLE", StringComparison.Ordinal)
               || texto.Contains("ArduinoBLE", StringComparison.Ordinal);
    }

    // =====================================================================
    //  a heap
    // =====================================================================

    bool EhAvr => maq.Perfil?.De8Bits == true;

    /// <summary>
    /// Um bloco novo da heap, zerado (calloc) ou nao. Nao coube: NULL, como o
    /// malloc da placa -- o sketch que nao confere o ponteiro quebra la e aqui.
    /// </summary>
    Valor Reservar(double pedido, bool psram, int linha)
    {
        long n = (long)Math.Max(0, pedido);
        if (psram && Ram.Layout.Psram <= 0) return Valor.DeInt(0);
        if (n > MaiorBlocoGuardado && Ram.CustoDoBloco(n) <= (psram ? Ram.PsramLivre : Ram.HeapLivre))
            throw new ErroDeExecucao($"malloc({n}): um bloco tao grande cabe na placa, mas o simulador nao consegue guardar "
                                     + "mais que 4 milhoes de bytes num bloco so.", linha);
        // Um bloco so tem que caber inteiro numa regiao: na ESP32 classica a
        // maior tem ~110 KB, e o malloc(150000) da NULL com 300 KB livres.
        if (n > MaiorBlocoLivre(psram)) return Valor.DeInt(0);
        if (!Ram.Alocar(n, psram, out long custo)) return Valor.DeInt(0);
        Cobrar(Custos.Alocar);

        var bloco = new Valor[n];
        var zero = Valor.DeC(0, TipoC.UChar);
        for (long i = 0; i < n; i++) bloco[i] = zero;
        blocos.AddOrUpdate(bloco, new BlocoDaHeap { Custo = custo, Psram = psram, SemTipo = true });
        return Valor.DeReferencia(bloco);
    }

    /// <summary>
    /// free(): o bloco volta para a heap. Soltar duas vezes (ou soltar o que
    /// nao veio do malloc) derruba a ESP32 com o assert da heap; no AVR a
    /// memoria so fica corrompida, sem aviso -- aqui o console avisa.
    /// </summary>
    void Soltar(Valor v, int linha)
    {
        if (v.Referencia is null && v.Num == 0) return;   // free(NULL) nao faz nada
        BlocoDaHeap bloco = null;
        if (v.Referencia is null || !blocos.TryGetValue(v.Referencia, out bloco) || bloco.Solto)
        {
            if (EhFamiliaEsp32)
            {
                int nucleo = (maq.Perfil?.Nucleos ?? 1) > 1 ? 1 : 0;
                Escrever(bloco?.Solto == true
                    ? "assert failed: tlsf_free tlsf_control_functions.h:343 (!block_is_free(block) && \"block already marked as free\")"
                    : "assert failed: multi_heap_free multi_heap.c:239 (head != NULL)", true);
                Escrever("", true);
                Escrever($"Backtrace: 0x40082b31:0x3ffb1f90 (core {nucleo})", true);
                Escrever("", true);
                Escrever("Rebooting...", true);
                maq.Avisar($"{Origens.Onde(linha)}: free() de um bloco que {(bloco?.Solto == true ? "ja tinha sido solto" : "nao veio do malloc")}: "
                           + "na ESP32 isso para a placa com o assert da heap, como na bancada.");
                throw Reiniciar(ReinicioDaPlaca.PeloPanico, 0, 0);
            }
            maq.Avisar($"{Origens.Onde(linha)}: free() de um bloco que {(bloco?.Solto == true ? "ja tinha sido solto" : "nao veio do malloc")}: "
                       + $"no {maq.Perfil?.Processador} isso corrompe a heap sem avisar -- e o erro aparece depois, em outro lugar.");
            return;
        }
        bloco.Solto = true;
        Ram.Liberar(bloco.Custo, bloco.Psram);
    }

    /// <summary>realloc(p, n): cresce (ou encolhe) o bloco; sem lugar, NULL e o antigo fica.</summary>
    Valor Realocar(Valor v, double pedido, bool psram, int linha)
    {
        if (v.Referencia is not Valor[] antigo) return Reservar(pedido, psram, linha);
        if (pedido <= 0)
        {
            Soltar(v, linha);
            return Valor.DeInt(0);
        }
        blocos.TryGetValue(antigo, out var info);
        var novo = Reservar(pedido, info?.Psram ?? psram, linha);
        if (novo.Referencia is not Valor[]) return novo;

        // O bloco antigo ja tinha tipo ("int*"): o novo tambem tem.
        if (info is { SemTipo: false, Ponteiro: not null }) novo = TiparBloco(novo, info.Ponteiro);
        var destino = (Valor[])novo.Referencia;
        Array.Copy(antigo, destino, Math.Min(antigo.Length, destino.Length));
        Soltar(v, linha);
        return novo;
    }

    /// <summary>
    /// "(int*)malloc(40)": o bloco de bytes vira um vetor de int (10 itens).
    /// O malloc devolve void*, e e o cast que diz o que mora nele.
    /// </summary>
    Valor TiparBloco(Valor v, string tipoDoPonteiro)
    {
        if (v.Referencia is not Valor[] bytes || !blocos.TryGetValue(bytes, out var info) || !info.SemTipo) return v;
        string item = tipoDoPonteiro.TrimEnd('*', ' ').Replace("const ", "").Trim();
        if (item is "void" or "") return v;
        int tamanho = Math.Max(1, BytesDoTipo(item));
        info.SemTipo = false;
        info.Ponteiro = tipoDoPonteiro;
        if (tamanho == 1 && !EmC) return v;

        var tipoC = TiposC.DoNome(item, ModeloC);
        var tipado = new Valor[bytes.Length / tamanho];
        var zero = tipoC != TipoC.Nenhum ? Valor.DeC(0, tipoC) : EhCaractere(item) ? Valor.DeCaractere(0) : Valor.DeInt(0);
        for (int i = 0; i < tipado.Length; i++) tipado[i] = zero;
        blocos.Remove(bytes);
        blocos.AddOrUpdate(tipado, info);
        return Valor.DeReferencia(tipado);
    }

    /// <summary>
    /// "new T" e "new T[n]": o que eles ocupam da heap. Nao coube: no AVR o
    /// operator new devolve NULL; nas de 32 bits ele lanca std::bad_alloc, que
    /// ninguem pega -- e a placa para no abort().
    /// </summary>
    long ReservarParaNovo(long bytes, int linha)
    {
        if (!Ram.Alocar(bytes, false, out long custo))
        {
            if (EhAvr) return -1;
            FalharNovo(bytes, linha);
        }
        Cobrar(Custos.Alocar);
        return custo;
    }

    /// <summary>O bloco do new fica anotado no que ele virou, para o delete soltar.</summary>
    void AnotarNovo(object feito, long custo)
    {
        if (feito is not null && custo > 0) blocos.AddOrUpdate(feito, new BlocoDaHeap { Custo = custo });
    }

    void FalharNovo(long bytes, int linha)
    {
        int nucleo = (maq.Perfil?.Nucleos ?? 1) > 1 ? 1 : 0;
        if (EhFamiliaEsp32)
        {
            Escrever("", true);
            Escrever("terminate called after throwing an instance of 'std::bad_alloc'", true);
            Escrever("  what():  std::bad_alloc", true);
            Escrever("", true);
            Escrever($"abort() was called at PC 0x420178e7 on core {nucleo}", true);
            Escrever("", true);
            Escrever("Rebooting...", true);
        }
        maq.Avisar($"{Origens.Onde(linha)}: new de {bytes} bytes nao coube na heap ({Ram.HeapLivre} livres): "
                   + "o new lanca std::bad_alloc, ninguem pega, e a placa para no abort() -- como na bancada.");
        throw Reiniciar(ReinicioDaPlaca.PeloPanico, 0, 0);
    }

    /// <summary>delete e delete[]: o bloco do new volta para a heap.</summary>
    void SoltarNovo(object feito)
    {
        if (feito is not null && blocos.TryGetValue(feito, out var bloco) && !bloco.Solto)
        {
            bloco.Solto = true;
            Ram.Liberar(bloco.Custo, bloco.Psram);
        }
    }

    /// <summary>
    /// Com a PSRAM ligada, o malloc da ESP32 manda o bloco grande (mais de 4
    /// KB, o CONFIG_SPIRAM_MALLOC_ALWAYSINTERNAL) para ela primeiro, e so usa
    /// a RAM interna se la nao couber.
    /// </summary>
    bool VaiParaPsram(long n) => Ram.Layout.Psram > 0 && n > 4096 && n <= Ram.PsramLivre;

    // =====================================================================
    //  a String
    // =====================================================================

    bool avisouDoTexto;

    /// <summary>
    /// "String s = ...": a String nova e o buffer dela. Na ESP32 e no ESP8266
    /// ate 13 letras moram dentro do proprio objeto e a heap nem e tocada;
    /// no AVR toda String tem bloco, ate a vazia. So as variaveis do sketch
    /// sao contadas -- os campos de um objeto nao.
    /// </summary>
    Valor DeclararTexto(Ambiente amb, string nome, Valor v, int linha)
    {
        if (EhPython || !v.EhTexto || amb.Dono is not null || amb.Classe is not null) return v;
        var t = new TextoNaHeap();
        (amb.Textos ??= new(StringComparer.Ordinal))[nome] = t;
        return CaberTexto(t, BytesDe(v), linha, invalida: true) ? v : Valor.DeTexto("");
    }

    static long BytesDe(Valor v) => System.Text.Encoding.UTF8.GetByteCount(v.Texto ?? "");

    /// <summary>
    /// A String vai guardar n letras: se elas nao cabem no buffer, ele cresce
    /// (o realloc do changeBuffer do WString.cpp). Encolher nao devolve nada:
    /// o buffer so cresce, ate a String morrer. Falso: nao coube. Com
    /// <paramref name="invalida"/> (o copy() do "="), a String perde o buffer
    /// e fica vazia; sem (o concat() do "+=", o reserve()), fica como estava.
    /// Na placa nenhum dos dois avisa nada.
    /// </summary>
    bool CaberTexto(TextoNaHeap t, long n, int linha, bool invalida)
    {
        if (t.TemBuffer && n <= t.Capacidade) return true;
        var layout = Ram.Layout;

        // O SSO: texto curto vai para dentro do objeto -- se ele ainda nao
        // tem bloco na heap; depois de crescer, o bloco fica.
        if (layout.TextoCurto > 0 && n < layout.TextoCurto && t.Custo == 0)
        {
            t.TemBuffer = true;
            t.Capacidade = layout.TextoCurto;
            return true;
        }

        long bloco = Ram.BlocoDoTexto(n);

        // O CAPACITY_MAX do WString da ESP32: 65535 (3 MB com a PSRAM ligada).
        bool grandeDemais = EhFamiliaEsp32 && bloco > (layout.Psram > 0 ? 3_145_728 : 65_535);
        if (!grandeDemais)
        {
            if (t.Custo > 0) Ram.Liberar(t.Custo, t.Psram);
            bool psram = VaiParaPsram(bloco);
            if ((psram || bloco <= MaiorBlocoLivre(false)) && Ram.Alocar(bloco, psram, out long custo))
            {
                t.TemBuffer = true;
                t.Capacidade = bloco - 1;
                t.Custo = custo;
                t.Psram = psram;
                return true;
            }
            if (t.Custo > 0) Ram.Retomar(t.Custo, t.Psram);
        }

        if (!avisouDoTexto)
        {
            avisouDoTexto = true;
            maq.Avisar($"{Origens.Onde(linha)}: a String precisou de {bloco} bytes "
                       + (grandeDemais ? "-- mais que o maximo de uma String na ESP32 (65535 bytes sem a PSRAM)"
                                       : $"e a heap so tinha {Ram.HeapLivre} livres")
                       + ". Na placa nao ha erro nenhum: "
                       + (invalida ? "a String fica vazia, e o texto some do que o sketch imprime."
                                   : "a String fica como estava -- o += e o concat() simplesmente nao acrescentam nada."));
        }

        if (invalida)
        {
            if (t.Custo > 0) Ram.Liberar(t.Custo, t.Psram);
            t.Custo = 0;
            t.TemBuffer = false;
            t.Capacidade = 0;
            t.Psram = false;
        }
        return false;
    }

    /// <summary>"s = ...", "s += ...", "s.trim()": o texto novo de uma variavel String, com a conta da heap.</summary>
    Valor TextoNovo(string nome, Valor v, Ambiente amb, bool concatenando, int linha)
    {
        if (!v.EhTexto || amb.TextoDe(nome) is not { } t) return v;
        if (CaberTexto(t, BytesDe(v), linha, invalida: !concatenando)) return v;
        return concatenando && amb.Buscar(nome, out var atual) ? atual : Valor.DeTexto("");
    }

    /// <summary>s.reserve(n): o buffer ja do tamanho de n letras. Falso: nao coube (a String fica como estava).</summary>
    bool ReservarTexto(string nome, long n, Ambiente amb, int linha)
        => amb.TextoDe(nome) is not { } t || CaberTexto(t, Math.Max(0, n), linha, invalida: false);

    // =====================================================================
    //  os radios
    // =====================================================================

    /// <summary>O que cada radio ligado tirou da heap, para devolver quando ele desliga.</summary>
    readonly Dictionary<string, long> radiosNaHeap = new(StringComparer.Ordinal);

    /// <summary>
    /// O radio liga e a pilha dele pega a parte dela da heap (ver
    /// <see cref="MemoriaDaPlaca.CustoDoRadio"/>) -- uma vez so: o begin() de
    /// novo nao aloca de novo. Falso: nao coube, e na placa o driver nao sobe
    /// (o esp_wifi_init devolve ESP_ERR_NO_MEM).
    /// </summary>
    bool LigarRadio(string radio, int linha)
    {
        if (radiosNaHeap.ContainsKey(radio)) return true;
        long bytes = MemoriaDaPlaca.CustoDoRadio(radio, maq.Perfil);
        if (bytes <= 0)
        {
            radiosNaHeap[radio] = 0;
            return true;
        }
        if (!Ram.Alocar(bytes, false, out long custo))
        {
            string nome = radio switch { "wifi" => "o WiFi", "ble" => "o BLE", _ => "o Bluetooth" };
            maq.Avisar($"{Origens.Onde(linha)}: {nome} precisa de ~{bytes / 1024} KB da heap para ligar, e so havia "
                       + $"{Ram.HeapLivre} bytes livres. Na placa o driver nao sobe e o radio fica desligado -- "
                       + "sobra de heap e o que o sketch deixa depois das globais e dos malloc.");
            return false;
        }
        radiosNaHeap[radio] = custo;
        return true;
    }

    void DesligarRadio(string radio)
    {
        if (radiosNaHeap.Remove(radio, out long custo) && custo > 0) Ram.Liberar(custo, false);
    }

    /// <summary>O fim de um escopo: o destrutor de cada String dele devolve o buffer.</summary>
    void FecharEscopo(Ambiente escopo)
    {
        if (escopo?.Textos is not { } textos) return;
        foreach (var t in textos.Values)
            if (t.Custo > 0) Ram.Liberar(t.Custo, t.Psram);
        escopo.Textos = null;
    }

    // =====================================================================
    //  a pilha
    // =====================================================================

    /// <summary>
    /// O quadro de uma funcao na pilha: o minimo da ABI (endereco de volta,
    /// registradores salvos), os parametros e as variaveis locais -- um vetor
    /// local de 500 int no Uno sao 1000 bytes de pilha.
    /// </summary>
    long QuadroDe(Funcao f)
    {
        if (quadros.TryGetValue(f, out long q)) return q;
        var layout = Ram.Layout;
        long bytes = layout.QuadroMinimo;
        foreach (var p in f.Params)
            bytes += p.PorReferencia || p.EhVetor || (p.Tipo?.EndsWith('*') ?? false) ? BytesDoPonteiro : TamanhoNaPilha(p.Tipo);
        bytes += LocaisDe(f.Corpo, 0);
        long a = Math.Max(1, layout.AlinhamentoDaPilha);
        q = (bytes + a - 1) / a * a;
        quadros[f] = q;
        return q;
    }

    long TamanhoNaPilha(string tipo)
    {
        if (string.IsNullOrEmpty(tipo) || tipo == "void") return 0;
        try { return Math.Max(1, BytesDoTipo(tipo)); }
        catch (ErroDeExecucao) { return BytesDoPonteiro; }
    }

    long LocaisDe(Cmd c, int profundidade)
    {
        if (c is null || profundidade > 64) return 0;
        switch (c)
        {
            case CmdDecl d when d.Externo || d.Secao is not null || d.PorReferencia:
                return d.PorReferencia ? BytesDoPonteiro : 0;
            case CmdDecl d:
            {
                long item = d.Tipo?.EndsWith('*') == true ? BytesDoPonteiro : TamanhoNaPilha(d.Tipo);
                if (!d.EhArray) return item;
                long linhas = d.Tamanho is Literal { EhTexto: false } n && n.Valor > 0 ? (long)n.Valor
                            : d.Itens is { Count: > 0 } ? d.Itens.Count
                            : d.Inicial is Literal { EhTexto: true } t ? (t.Texto?.Length ?? 0) + 1 : 1;
                long colunas = ProdutoConstante(d.Colunas);
                return item * linhas * colunas;
            }
            case CmdDeclObj o: return TamanhoNaPilha(o.Classe);
            case CmdVarios v: return v.Corpo.Sum(x => LocaisDe(x, profundidade + 1));
            case CmdBloco b: return b.Corpo.Sum(x => LocaisDe(x, profundidade + 1));
            case CmdSe s: return LocaisDe(s.Entao, profundidade + 1) + LocaisDe(s.Senao, profundidade + 1);
            case CmdEnquanto w: return LocaisDe(w.Corpo, profundidade + 1);
            case CmdFacaEnquanto dw: return LocaisDe(dw.Corpo, profundidade + 1);
            case CmdPara p: return LocaisDe(p.Ini, profundidade + 1) + LocaisDe(p.Corpo, profundidade + 1);
            case CmdParaCadaC pc: return TamanhoNaPilha(pc.Tipo) + LocaisDe(pc.Corpo, profundidade + 1);
            case CmdEscolha es: return es.Casos.Sum(k => k.Corpo.Sum(x => LocaisDe(x, profundidade + 1)));
            default: return 0;
        }
    }

    /// <summary>
    /// Entra numa funcao: o quadro dela desce a pilha. Estourou: o que a placa
    /// faria -- o Guru Meditation do "Stack canary" na ESP32, a memoria
    /// atropelada no AVR.
    /// </summary>
    long EntrarNaPilha(Funcao f, int linha)
    {
        if (!EmC || f is null) return 0;
        long q = QuadroDe(f);
        if (!Ram.Empilhar(q)) EstourarPilhaDaPlaca(f, linha, q);
        return q;
    }

    void SairDaPilha(long q)
    {
        if (q > 0) Ram.Desempilhar(q);
    }

    void EstourarPilhaDaPlaca(Funcao f, int linha, long quadro)
    {
        var layout = Ram.Layout;
        string onde = $"{Origens.Onde(linha)}: {f.Nome}()";
        Ram.Desempilhar(quadro);
        if (EhFamiliaEsp32)
        {
            int nucleo = (maq.Perfil?.Nucleos ?? 1) > 1 ? 1 : 0;
            Escrever($"Guru Meditation Error: Core  {nucleo} panic'ed (Unhandled debug exception). ", true);
            Escrever("Debug exception reason: Stack canary watchpoint triggered (loopTask) ", true);
            Escrever("", true);
            Escrever("Rebooting...", true);
            maq.Avisar($"{onde} passou dos {layout.PilhaDoLoop} bytes da pilha do loopTask (cada chamada, com as locais, "
                       + $"ocupa {quadro} bytes): a ESP32 para com o Guru Meditation do 'Stack canary', como na bancada.");
            throw Reiniciar(ReinicioDaPlaca.PeloPanico, 0, 0);
        }
        if (EhEsp8266)
        {
            Escrever("", true);
            Escrever("--------------- CUT HERE FOR EXCEPTION DECODER ---------------", true);
            Escrever("", true);
            Escrever("Exception (29):", true);
            Escrever("epc1=0x4000e1b2 epc2=0x00000000 epc3=0x00000000 excvaddr=0x3ffefffc depc=0x00000000", true);
            Escrever("", true);
            Escrever(">>>stack>>>", true);
            Escrever("", true);
            Escrever("ctx: cont", true);
            Escrever("<<<stack<<<", true);
            maq.Avisar($"{onde} passou dos {layout.PilhaDoLoop} bytes da pilha do loop do ESP8266 (o 'cont'): a placa "
                       + "atropela a memoria do lado e reinicia com uma excecao, como na bancada.");
            throw Reiniciar(ReinicioDaPlaca.PeloPanico, 0, 0);
        }
        maq.Avisar(layout.Compartilhada
            ? $"{onde}: a pilha encostou na heap e nas globais ({layout.RamTotal} bytes de RAM no total, "
              + $"{Ram.Estatica} de globais, {Ram.HeapUsada} de heap). Na placa isso corrompe variaveis sem aviso "
              + "e costuma terminar com a placa reiniciando sozinha -- aqui ela reinicia na hora."
            : $"{onde} passou dos {layout.PilhaDoLoop} bytes da pilha: a placa atropela a memoria e para.");
        throw Reiniciar(ReinicioDaPlaca.PelaMemoria, 0, 0);
    }

    // =====================================================================
    //  as funcoes que medem a memoria
    // =====================================================================

    const int MallocCapExec = 1 << 0, MallocCap32 = 1 << 1, MallocCap8 = 1 << 2, MallocCapDma = 1 << 3,
              MallocCapSpiram = 1 << 10, MallocCapInternal = 1 << 11, MallocCapDefault = 1 << 12;

    /// <summary>As constantes do esp_heap_caps.h (MALLOC_CAP_*), nas placas da familia ESP32.</summary>
    bool ConstanteDaMemoria(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!EhFamiliaEsp32 || !nome.StartsWith("MALLOC_CAP_", StringComparison.Ordinal)) return false;
        int? v = nome switch
        {
            "MALLOC_CAP_EXEC" => MallocCapExec,
            "MALLOC_CAP_32BIT" => MallocCap32,
            "MALLOC_CAP_8BIT" => MallocCap8,
            "MALLOC_CAP_DMA" => MallocCapDma,
            "MALLOC_CAP_SPIRAM" => MallocCapSpiram,
            "MALLOC_CAP_INTERNAL" => MallocCapInternal,
            "MALLOC_CAP_DEFAULT" => MallocCapDefault,
            _ => null,
        };
        if (v is null) return false;
        valor = DoTipo(v.Value, TipoC.ULong);
        return true;
    }

    /// <summary>
    /// A maior alocacao que cabe inteira. Na ESP32 classica a heap e feita de
    /// regioes separadas (a maior tem ~110 KB), e nenhum malloc passa disso
    /// mesmo com 300 KB livres; nas outras a SRAM e continua.
    /// </summary>
    long MaiorBlocoLivre(bool psram)
    {
        long livre = psram ? Ram.PsramLivre : Ram.HeapLivre;
        if (psram) return livre;
        if (maq.Perfil?.Processador == "Xtensa LX6") livre = Math.Min(livre, 110_000);
        return Math.Max(0, livre - Ram.Layout.Cabecalho);
    }

    /// <summary>
    /// malloc, calloc, realloc, free e as da heap da ESP32 e da avr-libc. Falso:
    /// nao e uma delas.
    /// </summary>
    bool ChamarMemoria(string nome, List<Valor> args, int linha, out Valor r)
    {
        r = Valor.Vazio;
        double A(int i) => i < args.Count ? args[i].Num : 0;
        bool esp32 = EhFamiliaEsp32;
        bool Psram(int i) => ((long)A(i) & MallocCapSpiram) != 0;

        switch (nome)
        {
            // Com a PSRAM ligada, o bloco grande vai para ela (ver VaiParaPsram).
            case "malloc": r = Reservar(A(0), esp32 && VaiParaPsram((long)A(0)), linha); return true;
            case "calloc": r = Reservar(A(0) * A(1), esp32 && VaiParaPsram((long)(A(0) * A(1))), linha); return true;
            case "realloc":
                r = Realocar(args.Count > 0 ? args[0] : Valor.DeInt(0), A(1), esp32 && VaiParaPsram((long)A(1)), linha);
                return true;
            case "free": Soltar(args.Count > 0 ? args[0] : Valor.DeInt(0), linha); return true;

            case "ps_malloc" when esp32: r = Reservar(A(0), true, linha); return true;
            case "ps_calloc" when esp32: r = Reservar(A(0) * A(1), true, linha); return true;
            case "ps_realloc" when esp32: r = Realocar(args.Count > 0 ? args[0] : Valor.DeInt(0), A(1), true, linha); return true;
            case "heap_caps_malloc" when esp32: r = Reservar(A(0), Psram(1), linha); return true;
            case "heap_caps_calloc" when esp32: r = Reservar(A(0) * A(1), Psram(2), linha); return true;
            case "heap_caps_realloc" when esp32: r = Realocar(args.Count > 0 ? args[0] : Valor.DeInt(0), A(1), Psram(2), linha); return true;
            case "heap_caps_free" when esp32: Soltar(args.Count > 0 ? args[0] : Valor.DeInt(0), linha); return true;

            case "heap_caps_get_free_size" when esp32:
                r = DoTipo(Psram(0) ? Ram.PsramLivre : Ram.HeapLivre + (((long)A(0) & MallocCapInternal) != 0 ? 0 : Ram.PsramLivre), TipoC.UInt);
                return true;
            case "heap_caps_get_total_size" when esp32:
                r = DoTipo(Psram(0) ? Ram.Layout.Psram : Ram.HeapTotal, TipoC.UInt);
                return true;
            case "heap_caps_get_largest_free_block" when esp32:
                r = DoTipo(MaiorBlocoLivre(Psram(0)), TipoC.UInt);
                return true;
            case "heap_caps_get_minimum_free_size" when esp32:
                r = DoTipo(Psram(0) ? Ram.PsramLivre : Ram.MenorHeapLivre, TipoC.UInt);
                return true;
            case "esp_get_free_heap_size" when esp32:
            case "esp_get_free_internal_heap_size" when esp32:
                r = DoTipo(Ram.HeapLivre, TipoC.ULong);
                return true;
            case "esp_get_minimum_free_heap_size" when esp32:
                r = DoTipo(Ram.MenorHeapLivre, TipoC.ULong);
                return true;

            // O quanto da pilha da tarefa nunca foi usado (no FreeRTOS do
            // ESP-IDF, em bytes).
            case "uxTaskGetStackHighWaterMark" when esp32:
                r = DoTipo(Ram.PilhaQueSobrouNoPior, TipoC.UInt);
                return true;

            // A MemoryFree do AVR (e o freeMemory() de todo tutorial): a
            // distancia entre a pilha e o topo da heap.
            case "freeMemory":
                r = DoTipo(Ram.Layout.Compartilhada ? Ram.PonteiroDaPilha - Math.Max(Ram.InicioDaHeap, Ram.TopoDaHeap) : Ram.HeapLivre,
                           TipoC.Int);
                return true;
        }
        return false;
    }

    /// <summary>
    /// Os nomes da avr-libc que o jeito classico de medir a RAM usa:
    /// "extern int __heap_start, *__brkval;" e o SP. Com eles o freeRam() de
    /// todo tutorial da o que daria no Uno.
    /// </summary>
    bool NomeDaMemoriaDoAvr(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!EhAvr) return false;
        long? v = nome switch
        {
            "__heap_start" or "__malloc_heap_start" or "__bss_end" => Ram.InicioDaHeap,
            "__brkval" => Ram.TopoDaHeap,
            "__malloc_margin" => Ram.Layout.MargemDoMalloc,
            "SP" => Ram.PonteiroDaPilha,
            "RAMEND" => Ram.Layout.RamInicio + Ram.Layout.RamTotal - 1,
            "RAMSTART" => Ram.Layout.RamInicio,
            _ => null,
        };
        if (v is null) return false;
        valor = DoTipo(v.Value, TipoC.UInt);
        return true;
    }

    /// <summary>
    /// "(int)&amp;v" no AVR: o endereco de uma variavel. A local mora na pilha
    /// (o SP de agora); a global, nas globais -- e o __heap_start e o
    /// endereco dele mesmo. E o que faz "(int)&amp;v - (int)__brkval" dar a RAM
    /// livre de verdade.
    /// </summary>
    bool EnderecoNoAvr(Expr alvo, Ambiente amb, out Valor endereco)
    {
        endereco = Valor.Vazio;
        if (!EhAvr || alvo is not Unaria { Op: "&", Alvo: Variavel v }) return false;
        if (v.Nome is "__heap_start" or "__malloc_heap_start" or "__bss_end")
        {
            endereco = DoTipo(Ram.InicioDaHeap, TipoC.UInt);
            return true;
        }
        bool local = amb.Pai is not null && EstaEmEscopoLocal(v.Nome, amb);
        endereco = DoTipo(local ? Ram.PonteiroDaPilha : Ram.Layout.RamInicio + 0x10, TipoC.UInt);
        return true;
    }

    static bool EstaEmEscopoLocal(string nome, Ambiente amb)
    {
        for (var e = amb; e is not null && e.Pai is not null; e = e.Pai)
            if (e.TemLocal(nome)) return true;
        return false;
    }

    /// <summary>ESP.getFreeHeap() e companhia: os numeros da heap da placa.</summary>
    bool MemoriaDoEsp(string metodo, bool esp8266, out Valor r)
    {
        r = Valor.Vazio;
        switch (metodo)
        {
            case "getFreeHeap": r = DoTipo(Ram.HeapLivre, TipoC.ULong); return true;
            case "getHeapSize" when !esp8266: r = DoTipo(Ram.HeapTotal, TipoC.ULong); return true;
            case "getMinFreeHeap" when !esp8266: r = DoTipo(Ram.MenorHeapLivre, TipoC.ULong); return true;
            case "getMaxAllocHeap" when !esp8266: r = DoTipo(MaiorBlocoLivre(false), TipoC.ULong); return true;
            case "getPsramSize" when !esp8266: r = DoTipo(Ram.Layout.Psram, TipoC.ULong); return true;
            case "getFreePsram" when !esp8266: r = DoTipo(Ram.PsramLivre, TipoC.ULong); return true;
            case "getMinFreePsram" when !esp8266: r = DoTipo(Ram.PsramLivre, TipoC.ULong); return true;
            case "getMaxAllocPsram" when !esp8266: r = DoTipo(MaiorBlocoLivre(true), TipoC.ULong); return true;
            case "getMaxFreeBlockSize" when esp8266: r = DoTipo(MaiorBlocoLivre(false), TipoC.ULong); return true;
            case "getHeapFragmentation" when esp8266: r = DoTipo(Ram.HeapUsada == 0 ? 0 : 3, TipoC.UChar); return true;
            case "getFreeContStack" when esp8266: r = DoTipo(Ram.PilhaQueSobrouNoPior, TipoC.ULong); return true;
        }
        return false;
    }

    /// <summary>O objeto "rp2040" do core da Pico (arduino-pico): heap, watchdog, ciclos.</summary>
    Valor ChamarRp2040(string metodo, Chamada c, Ambiente amb)
    {
        if (maq.Perfil?.Id is not ("pi-pico" or "pi-pico-w"))
            throw new ErroDeExecucao("o objeto 'rp2040' e do core da Raspberry Pi Pico (arduino-pico): "
                                     + $"na {maq.Perfil?.Nome} ele nao existe.", c.Linha);
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        if (WatchdogDoRp2040(metodo, args)) return Valor.Vazio;
        return metodo switch
        {
            "getFreeHeap" => DoTipo(Ram.HeapLivre, TipoC.Int),
            "getUsedHeap" => DoTipo(Ram.HeapUsada, TipoC.Int),
            "getTotalHeap" => DoTipo(Ram.HeapTotal, TipoC.Int),
            "f_cpu" => DoTipo((long)((maq.Perfil?.Megahertz ?? 133) * 1_000_000), TipoC.ULong),
            "getCycleCount" => DoTipo((long)(maq.AgoraUs() * (maq.Perfil?.Megahertz ?? 133)), TipoC.ULong),
            "getCycleCount64" => DoTipo((long)(maq.AgoraUs() * (maq.Perfil?.Megahertz ?? 133)), TipoC.ULongLong),
            "reboot" or "restart" => throw Reiniciar(ReinicioDaPlaca.PeloSoftware, 0, 0),
            _ => throw new ErroDeExecucao($"rp2040.{metodo}() ainda nao e suportado pelo simulador.", c.Linha),
        };
    }

    /// <summary>"[3][4]" escrito com numeros: o produto (o passo do primeiro indice). 1 se nao da para saber antes de rodar.</summary>
    static long ProdutoConstante(Expr e) => e switch
    {
        Literal { EhTexto: false } k when k.Valor > 0 => (long)k.Valor,
        Binaria { Op: "*" } b => ProdutoConstante(b.Esq) * ProdutoConstante(b.Dir),
        _ => 1,
    };
}
