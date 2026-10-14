using System.Runtime.CompilerServices;

namespace Simulador;

/// <summary>
/// O núcleo AVR: decodifica e executa os opcodes de verdade.
///
/// Não interpreta uma lista de instruções — lê a palavra de 16 bits da
/// flash, decodifica como o chip decodifica e cobra os CICLOS que o
/// datasheet manda cobrar. É essa última parte que separa um simulador
/// de um brinquedo:
///
///   - a maioria das instruções custa 1 ciclo;
///   - <c>rcall</c> custa 3 e <c>ret</c> custa 4;
///   - um desvio custa 1 quando NÃO desvia e 2 quando desvia;
///   - <c>lds</c>, <c>sts</c>, <c>ld</c>, <c>st</c>, <c>push</c>,
///     <c>pop</c>, <c>sbi</c> e <c>cbi</c> custam 2;
///   - <c>lpm</c> custa 3.
///
/// Um laço de espera de três instruções — <c>dec</c>, <c>brne</c>,
/// <c>nop</c> — custa 4 ciclos por volta quando desvia, e não 3. A 16
/// MHz isso é 33% de diferença no tempo: quem calibrar uma rega de
/// quatro segundos num simulador que cobra um ciclo por instrução vai
/// regar três na horta.
///
/// O que continua sendo aproximação, dito em voz alta: corrente,
/// tensão, ruído e o tempo de subida de um pino. Um emulador não
/// substitui o osciloscópio — ele garante que a LÓGICA e o TEMPO em
/// ciclos são os do chip.
/// </summary>
public sealed class Avr
{
    readonly ProgramaMontado programa;
    readonly Maquina maquina;

    /// <summary>
    /// A memória de dados inteira: registradores, E/S e SRAM.
    ///
    /// Num bloco só porque no chip É um bloco só. Um programa que faça
    /// "lds r16, 0x0002" tem que ler o r2, e separar os três espaços
    /// faria isso ler lixo.
    /// </summary>
    readonly byte[] dados = new byte[AvrIsa.TamanhoDosDados];

    /// <summary>O registrador TEMP dos acessos de 16 bits do Timer1.</summary>
    byte temp16;

    /// <summary>Ponteiro de programa, em PALAVRAS.</summary>
    public int Pc { get; private set; }

    /// <summary>Ciclos desde o reset. É daqui que sai todo o tempo.</summary>
    public long Ciclos { get; private set; }

    /// <summary>Ciclos que o passo anterior gastou e o relógio ainda não viu.</summary>
    double nanosSobrando;

    /// <summary>O relógio da máquina no instante do reset.</summary>
    double inicioEmMs;

    public Avr(ProgramaMontado programa, Maquina maquina)
    {
        this.programa = programa;
        this.maquina = maquina;

        flash = programa.Flash;
        palavras = programa.Palavras;
    }

    // Guardados aqui porque são lidos a CADA instrução. Ir buscá-los no
    // ProgramaMontado era uma volta a mais na memória por instrução, e
    // no laço de espera de um pisca-pisca isso é milhões de vezes por
    // segundo.
    readonly ushort[] flash;
    readonly int palavras;

    /// <summary>
    /// Algum temporizador com relógio ligado.
    ///
    /// Guardado quando o programa escreve no TCCR0B ou no TCCR1B, em vez
    /// de relido a cada instrução: quase todo programa nunca liga um
    /// temporizador, e pagar a conferência toda vez era desperdício.
    /// </summary>
    bool algumTimer;

    // =================================================================
    //  acesso a registradores e memória
    // =================================================================
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte R(int i) => dados[i];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void R(int i, int v) => dados[i] = (byte)v;

    int Par(int baixo) => dados[baixo] | (dados[baixo + 1] << 8);

    void Par(int baixo, int v)
    {
        dados[baixo] = (byte)v;
        dados[baixo + 1] = (byte)(v >> 8);
    }

    byte Sreg
    {
        get => dados[AvrIsa.BaseDaES + AvrIsa.SREG];
        set => dados[AvrIsa.BaseDaES + AvrIsa.SREG] = value;
    }

    bool Bandeira(int bit) => (Sreg & (1 << bit)) != 0;

    void Bandeira(int bit, bool ligada)
        => Sreg = (byte)(ligada ? Sreg | (1 << bit) : Sreg & ~(1 << bit));

    int Sp
    {
        get => dados[AvrIsa.BaseDaES + AvrIsa.SPL]
               | (dados[AvrIsa.BaseDaES + AvrIsa.SPH] << 8);
        set
        {
            dados[AvrIsa.BaseDaES + AvrIsa.SPL] = (byte)value;
            dados[AvrIsa.BaseDaES + AvrIsa.SPH] = (byte)(value >> 8);
        }
    }

    /// <summary>
    /// Lê um byte da memória de dados.
    ///
    /// Os PIN vêm do CIRCUITO, e não de um byte guardado: é lendo PINB
    /// que o programa descobre que o botão foi apertado.
    /// </summary>
    byte Ler(int endereco)
    {
        if (endereco < 0 || endereco >= dados.Length) return 0;

        int es = endereco - AvrIsa.BaseDaES;

        if (es is AvrIsa.PINB or AvrIsa.PINC or AvrIsa.PIND) return LerPorta(es);

        // Ler o byte BAIXO do Timer1 guarda o ALTO no TEMP, e ler o alto
        // devolve o TEMP. É o que garante que os dois bytes são da MESMA
        // contagem: sem isso, o contador podia virar entre as duas
        // leituras e o programa leria 0x01FF como 0x02FF.
        if (endereco is AvrIsa.TCNT1L or AvrIsa.OCR1AL)
        {
            temp16 = dados[endereco + 1];
            return dados[endereco];
        }

        if (endereco is AvrIsa.TCNT1H or AvrIsa.OCR1AH) return temp16;

        // O ADC: o valor vem do circuito, como no chip.
        if (endereco is AvrIsa.ADCL or AvrIsa.ADCH)
        {
            int canal = dados[AvrIsa.ADMUX] & 0x0F;
            int bruto = maquina.AnalogRead(14 + canal);
            return (byte)(endereco == AvrIsa.ADCL ? bruto & 0xFF : bruto >> 8);
        }

        return dados[endereco];
    }

    void Guardar(int endereco, byte valor)
    {
        if (endereco < 0 || endereco >= dados.Length) return;

        int es = endereco - AvrIsa.BaseDaES;

        // Escrever 1 num bit de PINx INVERTE o bit de PORTx. É um
        // recurso pouco conhecido do AVR e alguns programas de
        // pisca-pisca usam justamente ele.
        if (es is AvrIsa.PINB or AvrIsa.PINC or AvrIsa.PIND)
        {
            int porta = es + 2;
            dados[AvrIsa.BaseDaES + porta] ^= valor;
            EspalharPorta(porta);
            return;
        }

        // As bandeiras de interrupção se apagam escrevendo 1 NELAS. É
        // contra-intuitivo e é o que o datasheet manda: um programa que
        // espera o TOV1 e depois faz "ldi r16, 1 / out TIFR1, r16" quer
        // APAGAR a bandeira. Gravar o 1 a deixaria ligada para sempre, e
        // o laço de espera seguinte terminaria na hora.
        if (es is AvrIsa.TIFR0 or AvrIsa.TIFR1 or AvrIsa.TIFR2 or AvrIsa.EIFR)
        {
            dados[endereco] &= (byte)~valor;
            return;
        }

        // Os registradores de 16 bits do Timer1 passam por um TEMP, como
        // no chip: o byte ALTO se escreve primeiro e fica guardado, e o
        // BAIXO grava os dois de uma vez. Sem isso, escrever um valor de
        // 16 bits podia deixar o contador meio atualizado por um ciclo.
        if (endereco is AvrIsa.TCNT1H or AvrIsa.OCR1AH)
        {
            temp16 = valor;
            return;
        }

        if (endereco is AvrIsa.TCNT1L or AvrIsa.OCR1AL)
        {
            dados[endereco] = valor;
            dados[endereco + 1] = temp16;
            return;
        }

        dados[endereco] = valor;

        if (endereco is AvrIsa.TCCR0B or AvrIsa.TCCR1B)
            algumTimer = ((dados[AvrIsa.TCCR0B] | dados[AvrIsa.TCCR1B]) & 7) != 0;

        if (es is AvrIsa.PORTB or AvrIsa.PORTC or AvrIsa.PORTD) EspalharPorta(es);
        else if (es is AvrIsa.DDRB or AvrIsa.DDRC or AvrIsa.DDRD) EspalharDirecao(es);
    }

    // =================================================================
    //  as portas: a ponte com o circuito
    // =================================================================
    //
    //     PORTD  bits 0..7  ->  pinos 0..7    do Arduino
    //     PORTB  bits 0..5  ->  pinos 8..13
    //     PORTC  bits 0..5  ->  pinos A0..A5  (14..19)
    //
    // É esta tabela que faz "sbi PORTB, 5" acender o pino 13, que é o
    // pisca-pisca mais copiado do mundo.
    static int PrimeiroPino(int registradorDeES) => registradorDeES switch
    {
        AvrIsa.PORTD or AvrIsa.PIND or AvrIsa.DDRD => 0,
        AvrIsa.PORTB or AvrIsa.PINB or AvrIsa.DDRB => 8,
        AvrIsa.PORTC or AvrIsa.PINC or AvrIsa.DDRC => 14,
        _ => -1,
    };

    static int QuantosPinos(int registradorDeES) => PrimeiroPino(registradorDeES) == 0 ? 8 : 6;

    void EspalharPorta(int porta)
    {
        int primeiro = PrimeiroPino(porta);
        if (primeiro < 0) return;

        byte valor = dados[AvrIsa.BaseDaES + porta];
        byte direcao = dados[AvrIsa.BaseDaES + porta - 1];

        for (int bit = 0; bit < QuantosPinos(porta); bit++)
        {
            bool ligado = (valor & (1 << bit)) != 0;

            // Com o pino como ENTRADA, escrever 1 em PORTx não acende
            // nada: liga o resistor de pull-up. É a diferença entre um
            // botão que funciona e um que lê lixo.
            if ((direcao & (1 << bit)) != 0) maquina.DigitalWrite(primeiro + bit, ligado ? 1 : 0);
            else maquina.PinMode(primeiro + bit, ligado ? 2 : 0);
        }
    }

    void EspalharDirecao(int ddr)
    {
        int primeiro = PrimeiroPino(ddr);
        if (primeiro < 0) return;

        byte direcao = dados[AvrIsa.BaseDaES + ddr];

        for (int bit = 0; bit < QuantosPinos(ddr); bit++)
            maquina.PinMode(primeiro + bit, (direcao & (1 << bit)) != 0 ? 1 : 0);

        // Mudar a direção muda o que os níveis de PORTx significam.
        EspalharPorta(ddr + 1);
    }

    byte LerPorta(int pin)
    {
        int primeiro = PrimeiroPino(pin);
        if (primeiro < 0) return 0;

        byte valor = 0;

        for (int bit = 0; bit < QuantosPinos(pin); bit++)
            if (maquina.DigitalRead(primeiro + bit) != 0)
                valor |= (byte)(1 << bit);

        return valor;
    }

    // =================================================================
    //  o ciclo de vida
    // =================================================================
    public void Reiniciar()
    {
        Array.Clear(dados);

        Pc = 0;
        Ciclos = 0;
        nanosSobrando = 0;
        ciclosVistos = 0;
        ciclosDoTimer0 = 0;
        ciclosDoTimer1 = 0;
        temp16 = 0;
        algumTimer = false;

        // O instante zero do chip. Tudo o que vier depois é medido a
        // partir daqui, em ciclos convertidos para milissegundos.
        inicioEmMs = maquina.MsPreciso;
        maquina.VelocidadeDoChip = 1.0;
        maquina.MsDeChipRodando = 0;

        // O ponteiro de pilha nasce no fim da SRAM, como no chip. Um
        // RCALL antes de o programa acertar o SP escreveria em 0x0000,
        // que são os registradores -- e é exatamente o que acontece na
        // placa de verdade.
        Sp = AvrIsa.FimDaSram;
    }

    public bool Terminou => (uint)Pc >= (uint)palavras;

    /// <summary>A linha do texto que está rodando agora.</summary>
    public int LinhaAtual => programa.LinhaDe(Pc);

    /// <summary>
    /// Roda até <paramref name="quantosCiclos"/> ciclos de relógio.
    ///
    /// Em CICLOS, e não em instruções: é o que faz o tempo na tela
    /// andar como o tempo no chip, que é a razão de existir deste
    /// arquivo.
    /// </summary>
    public void Passo(int quantosCiclos)
    {
        long alvo = Ciclos + quantosCiclos;
        long doInicio = Ciclos;
        int contador = 0;

        while (Ciclos < alvo && !Terminou)
        {
            // O botão de parar é lido de vez em quando, e não a cada
            // instrução: é uma leitura de memória compartilhada entre
            // threads, e mil instruções a 16 MHz são 60 microssegundos --
            // ninguém percebe a diferença, e o executor anda mais.
            if ((++contador & 1023) == 0 && maquina.Parar) break;

            Executar();

            // Temporizador parado não conta nada: o marcador só anda
            // junto, para ele não achar que houve ciclos atrasados quando
            // for ligado.
            if (algumTimer) AndarComOsTimers();
            else ciclosVistos = Ciclos;

            // Interrupção só se o I estiver ligado E houver bandeira
            // pedindo com a máscara liberando. É o caso raro, e testá-lo
            // primeiro poupa o resto do trabalho quase sempre.
            if ((Sreg & 0x80) != 0 && Pendente()) Interromper();
        }

        // O tempo anda uma vez por leva, e não por instrução: a conta é a
        // mesma, sem ponto flutuante dentro do laço quente.
        ContarTempo(Ciclos - doInicio);

        // O programa acabou (caiu fora da flash): o chip de verdade
        // continuaria executando para sempre. Aqui o relógio anda mesmo
        // assim -- e os ciclos junto, para os temporizadores não
        // pararem --, senão o tempo congela e um sketch ao lado trava.
        if (Terminou && Ciclos < alvo)
        {
            long faltam = alvo - Ciclos;
            Ciclos = alvo;
            ContarTempo(faltam);
            AndarComOsTimers();
        }

        // ---- o ritmo do chip ----
        //
        // Em tempo real, espera o relógio de parede alcançar o tempo do
        // chip. Sem isto o AVR rodava tão depressa quanto o computador
        // aguentasse: um laço de espera de 250 ms passava em dois, e o
        // LED piscava cem vezes mais rápido que na placa -- exatamente o
        // "funciona na tela e não na placa" que não pode acontecer.
        double alvoEmMs = inicioEmMs + Ciclos * AvrIsa.NanosPorCiclo / 1_000_000.0;
        maquina.EsperarAte(alvoEmMs);

        // Se mesmo sem dormir o relógio real passou do tempo do chip, o
        // computador não está dando conta. Fica anotado quanto -- quem
        // mostra isso para a pessoa é a janela.
        if (maquina.TempoReal)
        {
            double real = maquina.MsPreciso - inicioEmMs;
            double doChip = alvoEmMs - inicioEmMs;

            maquina.MsDeChipRodando = real;
            if (real > 0) maquina.VelocidadeDoChip = Math.Min(1.0, doChip / real);
        }
    }

    /// <summary>
    /// Converte ciclos em tempo de relógio.
    ///
    /// A 16 MHz um ciclo são 62,5 ns. A sobra é guardada entre uma
    /// chamada e outra: descartá-la faria o relógio atrasar 37% num
    /// programa de instruções curtas.
    /// </summary>
    void ContarTempo(long ciclos)
    {
        if (ciclos <= 0) return;

        nanosSobrando += ciclos * AvrIsa.NanosPorCiclo;

        long micros = (long)(nanosSobrando / 1000);
        if (micros <= 0) return;

        nanosSobrando -= micros * 1000.0;
        maquina.AvancarMicros(micros);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ushort Palavra(int endereco)
        => (uint)endereco < (uint)flash.Length ? flash[endereco] : (ushort)0;

    /// <summary>Quantas palavras a instrução neste endereço ocupa.</summary>
    static int TamanhoDe(ushort op)
    {
        // LDS, STS, JMP e CALL são as únicas de duas palavras no
        // ATmega328P. Saber isso importa para o SKIP: "sbrc" pula a
        // instrução INTEIRA, e pular só metade de um LDS cairia no
        // endereço, que é lido como instrução.
        if ((op & 0xFE0F) == 0x9000) return 2;   // lds
        if ((op & 0xFE0F) == 0x9200) return 2;   // sts
        if ((op & 0xFE0E) == 0x940C) return 2;   // jmp
        if ((op & 0xFE0E) == 0x940E) return 2;   // call

        return 1;
    }

    void Pular()
    {
        int quanto = TamanhoDe(Palavra(Pc));
        Pc += quanto;
        Ciclos += quanto;
    }

    // =================================================================
    //  a decodificação
    // =================================================================
    //
    // Pelos quatro bits de cima do opcode, que é como o próprio chip
    // agrupa as instruções. A primeira versão testava uma cadeia de
    // quarenta condições em sequência, e um "brne" -- que está no fim da
    // tabela -- passava por quase todas. Com isso o executor não dava
    // conta de 16 MHz: rodava o ATmega328P a pouco mais da metade da
    // velocidade real, e um LED de 250 ms piscava a cada 440.
    void Executar()
    {
        ushort op = Palavra(Pc++);
        Ciclos++;

        int d = (op >> 4) & 0x1F;
        int r = (op & 0x0F) | ((op >> 5) & 0x10);

        switch (op >> 12)
        {
            case 0x0: Grupo0(op, d, r); return;

            case 0x1:
                switch (op & 0x0C00)
                {
                    case 0x0000: if (R(d) == R(r)) Pular(); return;                 // cpse
                    case 0x0400: Comparar(R(d), R(r), 0); return;                  // cp
                    case 0x0800: Subtrair(d, R(r), 0, true); return;               // sub
                    default: Somar(d, R(r), Carry); return;                        // adc
                }

            case 0x2:
                switch (op & 0x0C00)
                {
                    case 0x0000: Logica(d, R(r), (a, b) => a & b); return;          // and
                    case 0x0400: Logica(d, R(r), (a, b) => a ^ b); return;          // eor
                    case 0x0800: Logica(d, R(r), (a, b) => a | b); return;          // or
                    default: R(d, R(r)); return;                                   // mov
                }

            // ---- registrador alto e constante ----
            case 0x3: Comparar(R(Alto(op)), K8(op), 0); return;                    // cpi
            case 0x4: Subtrair(Alto(op), K8(op), Carry, true, true); return;       // sbci
            case 0x5: Subtrair(Alto(op), K8(op), 0, true); return;                 // subi
            case 0x6: Logica(Alto(op), K8(op), (a, b) => a | b); return;           // ori
            case 0x7: Logica(Alto(op), K8(op), (a, b) => a & b); return;           // andi
            case 0xE: R(Alto(op), K8(op)); return;                                 // ldi

            // LDD e STD -- e LD e ST com Y ou Z sem incremento, que são as
            // mesmas instruções com deslocamento zero.
            case 0x8:
            case 0xA: ComDeslocamento(op, d); return;

            case 0x9: Grupo9(op, d); return;

            case 0xB:                                                              // in / out
            {
                int endereco = AvrIsa.BaseDaES + (((op >> 5) & 0x30) | (op & 0x0F));

                if ((op & 0x0800) == 0) R(d, Ler(endereco));
                else Guardar(endereco, R(d));

                return;
            }

            case 0xC: Pc += Estendido12(op); Ciclos++; return;                     // rjmp

            case 0xD:                                                              // rcall
            {
                int salto = Estendido12(op);
                Empilhar((byte)(Pc >> 8));
                Empilhar((byte)Pc);
                Pc += salto;
                Ciclos += 2;
                return;
            }

            default: GrupoF(op, d); return;
        }
    }

    int Carry => (Sreg & 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Alto(ushort op) => 16 + ((op >> 4) & 0x0F);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int K8(ushort op) => (op & 0x0F) | ((op >> 4) & 0xF0);

    void Desconhecido(ushort op)
        => throw new ErroDeExecucao(
            $"opcode {op:X4} nao reconhecido", programa.LinhaDe(Pc - 1));

    // ---- 0000 ----
    void Grupo0(ushort op, int d, int r)
    {
        switch (op & 0x0C00)
        {
            case 0x0400: Comparar(R(d), R(r), Carry, true); return;                // cpc
            case 0x0800: Subtrair(d, R(r), Carry, true, true); return;             // sbc
            case 0x0C00: Somar(d, R(r), 0); return;                                // add
        }

        switch (op & 0xFF00)
        {
            case 0x0000:
                if (op == 0x0000) return;                                          // nop
                break;

            case 0x0100:                                                           // movw
            {
                int dd = ((op >> 4) & 0x0F) * 2, rr = (op & 0x0F) * 2;
                dados[dd] = dados[rr];
                dados[dd + 1] = dados[rr + 1];
                return;
            }

            case 0x0200: Multiplicar(op, true, true); return;                      // muls

            case 0x0300:
            {
                // Os quatro da família do MULSU, pelos bits 7 e 3. As
                // três "F" são as fracionárias: o produto sai deslocado
                // um bit para a esquerda, que é o que as torna úteis para
                // ponto fixo.
                int dd = 16 + ((op >> 4) & 7), rr = 16 + (op & 7);

                int produto = ((op >> 7) & 1, (op >> 3) & 1) switch
                {
                    (0, 0) => (sbyte)R(dd) * R(rr),                                // mulsu
                    (0, 1) => R(dd) * R(rr),                                       // fmul
                    (1, 0) => (sbyte)R(dd) * (sbyte)R(rr),                         // fmuls
                    _ => (sbyte)R(dd) * R(rr),                                     // fmulsu
                };

                bool fracionaria = (op & 0x0088) != 0;

                Bandeira(AvrIsa.BitC, (produto & 0x8000) != 0);
                if (fracionaria) produto <<= 1;

                Par(0, produto & 0xFFFF);
                Bandeira(AvrIsa.BitZ, (produto & 0xFFFF) == 0);

                Ciclos++;
                return;
            }
        }

        Desconhecido(op);
    }

    // ---- 1001 ----
    void Grupo9(ushort op, int d)
    {
        int baixo = op & 0x0F;

        switch ((op >> 8) & 0x0F)
        {
            case 0x0:
            case 0x1:
                if (baixo == 0x0) { R(d, Ler(Palavra(Pc++))); Ciclos++; return; }  // lds
                if (baixo is 0x4 or 0x5) { CarregarDaFlash(op, d); return; }       // lpm Rd,Z(+)
                if (baixo == 0xF) { R(d, Desempilhar()); Ciclos++; return; }       // pop
                if (ApontadorDeCarga(op, d)) return;
                break;

            case 0x2:
            case 0x3:
                if (baixo == 0x0) { Guardar(Palavra(Pc++), R(d)); Ciclos++; return; } // sts
                if (baixo == 0xF) { Empilhar(R(d)); Ciclos++; return; }            // push
                if (ApontadorDeEscrita(op, d)) return;
                break;

            case 0x4:
            case 0x5:
                if (UmOperando(op, d)) return;
                break;

            case 0x6: PalavraInteira(op, true); return;                            // adiw
            case 0x7: PalavraInteira(op, false); return;                           // sbiw

            case 0x8:
            case 0xA: BitDeES(op); return;                                         // cbi / sbi

            case 0x9:                                                              // sbic
            case 0xB:                                                              // sbis
            {
                int es = (op >> 3) & 0x1F;
                bool ligado = (Ler(AvrIsa.BaseDaES + es) & (1 << (op & 7))) != 0;

                if (ligado == ((op & 0x0200) != 0)) Pular();
                return;
            }

            default: Multiplicar(op, false, false); return;                        // mul
        }

        Desconhecido(op);
    }

    bool UmOperando(ushort op, int d)
    {
        switch (op & 0x0F)
        {
            case 0x0: Um(d, v => ~v & 0xFF, true); return true;                    // com
            case 0x1: Negar(d); return true;                                       // neg

            case 0x2:                                                              // swap
            {
                int v = R(d);
                R(d, ((v & 0x0F) << 4) | (v >> 4));
                return true;
            }

            case 0x3: Incrementar(d, +1); return true;                             // inc
            case 0x5: Deslocar(d, aritmetico: true); return true;                  // asr
            case 0x6: Deslocar(d, aritmetico: false); return true;                 // lsr
            case 0x7: Girar(d); return true;                                       // ror
            case 0xA: Incrementar(d, -1); return true;                             // dec

            case 0xC:
            case 0xD:                                                              // jmp
                Pc = Palavra(Pc);
                Ciclos += 2;
                return true;

            case 0xE:
            case 0xF:                                                              // call
            {
                int destino = Palavra(Pc++);
                Empilhar((byte)(Pc >> 8));
                Empilhar((byte)Pc);
                Pc = destino;
                Ciclos += 3;
                return true;
            }

            case 0x8: return Diversos(op);

            case 0x9:
                if (op == 0x9409) { Pc = Par(30); Ciclos++; return true; }         // ijmp

                if (op == 0x9509)                                                  // icall
                {
                    Empilhar((byte)(Pc >> 8));
                    Empilhar((byte)Pc);
                    Pc = Par(30);
                    Ciclos += 2;
                    return true;
                }

                return false;
        }

        return false;
    }

    /// <summary>A família "1001 010x xxxx 1000": bandeiras, retorno e controle.</summary>
    bool Diversos(ushort op)
    {
        if ((op & 0xFF0F) == 0x9408)                                               // bset / bclr
        {
            Bandeira((op >> 4) & 7, (op & 0x0080) == 0);
            return true;
        }

        switch (op)
        {
            case 0x9508:                                                           // ret
            case 0x9518:                                                           // reti
                Pc = Desempilhar() | (Desempilhar() << 8);
                Ciclos += 3;
                if (op == 0x9518) Bandeira(AvrIsa.BitI, true);
                return true;

            case 0x9588:                                                           // sleep
            case 0x9598:                                                           // break
            case 0x95A8:                                                           // wdr
                return true;

            case 0x95C8: R(0, Flash8(Par(30))); Ciclos += 2; return true;          // lpm
        }

        return false;
    }

    void BitDeES(ushort op)
    {
        int es = (op >> 3) & 0x1F;
        int endereco = AvrIsa.BaseDaES + es;
        int bit = op & 7;
        bool ligar = (op & 0x0200) != 0;

        // Nos registradores de bandeira, o ATmega328P mexe SÓ no bit
        // pedido -- o datasheet destaca isso como diferença dele para os
        // AVR mais velhos. Um SBI TIFR1,0 apaga o TOV1 e deixa o OCF1A
        // como estava; ler-modificar-escrever apagaria as duas, e uma
        // interrupção se perderia sem aviso.
        if (es is AvrIsa.TIFR0 or AvrIsa.TIFR1 or AvrIsa.TIFR2 or AvrIsa.EIFR)
        {
            if (ligar) dados[endereco] &= (byte)~(1 << bit);
            Ciclos++;
            return;
        }

        byte agora = dados[endereco];
        Guardar(endereco, (byte)(ligar ? agora | (1 << bit) : agora & ~(1 << bit)));
        Ciclos++;
    }

    // ---- 1111 ----
    void GrupoF(ushort op, int d)
    {
        if ((op & 0x0800) == 0)                                                    // brbs / brbc
        {
            bool estado = (Sreg & (1 << (op & 7))) != 0;

            if (estado == ((op & 0x0400) == 0))
            {
                Pc += Estendido7((op >> 3) & 0x7F);
                Ciclos++;
            }

            return;
        }

        int mascara = 1 << (op & 7);

        switch (op & 0x0600)
        {
            case 0x0000:                                                           // bld
                R(d, (Sreg & 0x40) != 0 ? R(d) | mascara : R(d) & ~mascara);
                return;

            case 0x0200:                                                           // bst
                Bandeira(AvrIsa.BitT, (R(d) & mascara) != 0);
                return;

            case 0x0400:                                                           // sbrc
                if ((R(d) & mascara) == 0) Pular();
                return;

            default:                                                               // sbrs
                if ((R(d) & mascara) != 0) Pular();
                return;
        }
    }

    // =================================================================
    //  os ajudantes de instrução
    // =================================================================
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Estendido12(ushort op)
    {
        int k = op & 0x0FFF;
        return k >= 0x800 ? k - 0x1000 : k;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Estendido7(int k) => k >= 0x40 ? k - 0x80 : k;

    byte Flash8(int endereco)
    {
        ushort palavra = Palavra(endereco >> 1);
        return (byte)((endereco & 1) != 0 ? palavra >> 8 : palavra);
    }

    void CarregarDaFlash(ushort op, int d)
    {
        int z = Par(30);
        R(d, Flash8(z));

        if ((op & 1) != 0) Par(30, z + 1);
        Ciclos += 2;
    }

    void Empilhar(byte v)
    {
        int sp = Sp;
        if (sp >= 0 && sp < dados.Length) dados[sp] = v;
        Sp = sp - 1;
    }

    byte Desempilhar()
    {
        int sp = Sp + 1;
        Sp = sp;
        return sp >= 0 && sp < dados.Length ? dados[sp] : (byte)0;
    }

    /// <summary>LD com X, Y ou Z, com pós-incremento e pré-decremento.</summary>
    bool ApontadorDeCarga(ushort op, int d)
    {
        int ponteiro = (op & 0xFE0F) switch
        {
            0x900C or 0x900D or 0x900E => 26,
            0x8008 or 0x9009 or 0x900A => 28,
            0x8000 or 0x9001 or 0x9002 => 30,
            _ => -1,
        };

        if (ponteiro < 0) return false;

        int passo = (op & 0x000F) switch { 0x0D or 0x09 or 0x01 => +1,
                                           0x0E or 0x0A or 0x02 => -1, _ => 0 };

        int endereco = Par(ponteiro);
        if (passo < 0) { endereco--; Par(ponteiro, endereco); }

        R(d, Ler(endereco));

        if (passo > 0) Par(ponteiro, endereco + 1);

        Ciclos++;
        return true;
    }

    bool ApontadorDeEscrita(ushort op, int r)
    {
        int ponteiro = (op & 0xFE0F) switch
        {
            0x920C or 0x920D or 0x920E => 26,
            0x8208 or 0x9209 or 0x920A => 28,
            0x8200 or 0x9201 or 0x9202 => 30,
            _ => -1,
        };

        if (ponteiro < 0) return false;

        int passo = (op & 0x000F) switch { 0x0D or 0x09 or 0x01 => +1,
                                           0x0E or 0x0A or 0x02 => -1, _ => 0 };

        int endereco = Par(ponteiro);
        if (passo < 0) { endereco--; Par(ponteiro, endereco); }

        Guardar(endereco, R(r));

        if (passo > 0) Par(ponteiro, endereco + 1);

        Ciclos++;
        return true;
    }

    void ComDeslocamento(ushort op, int registrador)
    {
        int q = (op & 7) | ((op >> 7) & 0x18) | ((op >> 8) & 0x20);
        int ponteiro = (op & 8) != 0 ? 28 : 30;
        int endereco = Par(ponteiro) + q;

        if ((op & 0x0200) != 0) Guardar(endereco, R(registrador));
        else R(registrador, Ler(endereco));

        Ciclos++;
    }

    // =================================================================
    //  as bandeiras
    // =================================================================
    //
    // O SREG não é decoração: um "brne" lê o Z que o "dec" deixou, e o
    // "brlo" lê o C que o "cp" deixou. Errar uma bandeira faz o desvio
    // ir para o lado errado num programa perfeitamente correto.
    //
    // Cada instrução monta o SREG novo numa variável e grava UMA vez.
    // A primeira versão ligava e desligava bit por bit -- até oito
    // leituras e escritas por instrução --, e era isso que segurava o
    // executor abaixo dos 16 MHz do chip. As bandeiras que cada
    // instrução NÃO mexe são preservadas pela máscara de cada uma, que
    // é a parte que tem que bater com o manual.
    const int C = 0x01, Z = 0x02, N = 0x04, V = 0x08, S = 0x10, H = 0x20;

    const int EnderecoDoSreg = AvrIsa.BaseDaES + AvrIsa.SREG;

    /// <summary>Z, N e S a partir do resultado, sobre um SREG já montado.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ZNS(int sreg, int resultado)
    {
        if ((resultado & 0xFF) == 0) sreg |= Z;
        if ((resultado & 0x80) != 0) sreg |= N;
        if (((sreg & N) != 0) ^ ((sreg & V) != 0)) sreg |= S;
        return sreg;
    }

    /// <summary>ADD e ADC: mexem em H, S, V, N, Z e C.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Somar(int d, int quanto, int comCarry)
    {
        int antes = dados[d];
        int soma = antes + quanto + comCarry;

        int sreg = dados[EnderecoDoSreg] & ~(C | Z | N | V | S | H);
        if (soma > 0xFF) sreg |= C;
        if (((antes & 0x0F) + (quanto & 0x0F) + comCarry) > 0x0F) sreg |= H;
        if (((antes ^ soma) & (quanto ^ soma) & 0x80) != 0) sreg |= V;

        dados[d] = (byte)soma;
        dados[EnderecoDoSreg] = (byte)ZNS(sreg, soma);
    }

    /// <summary>
    /// SUB, SUBI, SBC e SBCI.
    ///
    /// Nas instruções "com carry" o Z só ZERA: ele acumula o resultado
    /// dos bytes anteriores. Sem isso, comparar dois números de 16 bits
    /// daria errado na metade das vezes.
    /// </summary>
    void Subtrair(int d, int quanto, int comCarry, bool guarda, bool acumulaZero = false)
    {
        int antes = dados[d];
        int conta = antes - quanto - comCarry;

        if (guarda) dados[d] = (byte)conta;
        dados[EnderecoDoSreg] = (byte)BandeirasDaSubtracao(antes, quanto, comCarry, conta,
                                                            acumulaZero);
    }

    /// <summary>CP, CPI e CPC: a subtração sem guardar o resultado.</summary>
    void Comparar(int a, int b, int comCarry, bool acumulaZero = false)
        => dados[EnderecoDoSreg] = (byte)BandeirasDaSubtracao(a, b, comCarry, a - b - comCarry,
                                                               acumulaZero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int BandeirasDaSubtracao(int a, int b, int comCarry, int conta, bool acumulaZero)
    {
        int atual = dados[EnderecoDoSreg];
        int sreg = atual & ~(C | Z | N | V | S | H);

        if (conta < 0) sreg |= C;
        if ((a & 0x0F) - (b & 0x0F) - comCarry < 0) sreg |= H;
        if (((a ^ b) & (a ^ conta) & 0x80) != 0) sreg |= V;
        if ((conta & 0x80) != 0) sreg |= N;

        bool zero = (conta & 0xFF) == 0;
        if (acumulaZero ? zero && (atual & Z) != 0 : zero) sreg |= Z;

        if (((sreg & N) != 0) ^ ((sreg & V) != 0)) sreg |= S;
        return sreg;
    }

    /// <summary>AND, OR, EOR e seus imediatos: S, V=0, N e Z. H e C ficam.</summary>
    void Logica(int d, int quanto, Func<int, int, int> conta)
    {
        int v = conta(dados[d], quanto) & 0xFF;
        dados[d] = (byte)v;
        dados[EnderecoDoSreg] = (byte)ZNS(dados[EnderecoDoSreg] & ~(Z | N | V | S), v);
    }

    /// <summary>COM: S, V=0, N, Z e C=1. H fica.</summary>
    void Um(int d, Func<int, int> conta, bool carry)
    {
        int v = conta(dados[d]) & 0xFF;
        dados[d] = (byte)v;

        int sreg = dados[EnderecoDoSreg] & ~(Z | N | V | S | (carry ? C : 0));
        if (carry) sreg |= C;
        dados[EnderecoDoSreg] = (byte)ZNS(sreg, v);
    }

    /// <summary>NEG: H, S, V, N, Z e C.</summary>
    void Negar(int d)
    {
        int antes = dados[d];
        int v = (0 - antes) & 0xFF;
        dados[d] = (byte)v;

        int sreg = dados[EnderecoDoSreg] & ~(C | Z | N | V | S | H);
        if (v != 0) sreg |= C;
        if (((v | antes) & 0x08) != 0) sreg |= H;
        if (v == 0x80) sreg |= V;
        dados[EnderecoDoSreg] = (byte)ZNS(sreg, v);
    }

    /// <summary>
    /// INC e DEC: mexem no V e no Z, e NÃO mexem no carry.
    ///
    /// Não mexer no carry é o detalhe que faz o laço de contagem de 16
    /// bits funcionar: o "dec" de baixo não pode apagar o carry que o
    /// "sbci" de cima vai ler.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Incrementar(int d, int quanto)
    {
        int antes = dados[d];
        int v = (antes + quanto) & 0xFF;
        dados[d] = (byte)v;

        int sreg = dados[EnderecoDoSreg] & ~(Z | N | V | S);
        if (quanto > 0 ? antes == 0x7F : antes == 0x80) sreg |= V;
        dados[EnderecoDoSreg] = (byte)ZNS(sreg, v);
    }

    /// <summary>ASR e LSR: S, V = N xor C, N, Z e C. H fica.</summary>
    void Deslocar(int d, bool aritmetico)
    {
        int antes = dados[d];
        int v = aritmetico ? (antes >> 1) | (antes & 0x80) : antes >> 1;
        dados[d] = (byte)v;

        dados[EnderecoDoSreg] = (byte)BandeirasDoDeslocamento((antes & 1) != 0, v);
    }

    /// <summary>ROR: o carry entra em cima e o bit de baixo vira o carry.</summary>
    void Girar(int d)
    {
        int antes = dados[d];
        int v = (antes >> 1) | ((dados[EnderecoDoSreg] & C) != 0 ? 0x80 : 0);
        dados[d] = (byte)v;

        dados[EnderecoDoSreg] = (byte)BandeirasDoDeslocamento((antes & 1) != 0, v);
    }

    int BandeirasDoDeslocamento(bool carry, int v)
    {
        int sreg = dados[EnderecoDoSreg] & ~(C | Z | N | V | S);

        if (carry) sreg |= C;
        if ((v & 0xFF) == 0) sreg |= Z;
        if ((v & 0x80) != 0) sreg |= N;

        // O V do deslocamento é N xor C -- e o S, como sempre, N xor V.
        bool n = (sreg & N) != 0;
        if (n ^ carry) sreg |= V;
        if (n ^ ((sreg & V) != 0)) sreg |= S;

        return sreg;
    }

    /// <summary>ADIW e SBIW: S, V, N, Z e C, sobre o par de 16 bits. H fica.</summary>
    void PalavraInteira(ushort op, bool somando)
    {
        int d = 24 + ((op >> 4) & 3) * 2;
        int k = (op & 0x0F) | ((op >> 2) & 0x30);

        int antes = dados[d] | (dados[d + 1] << 8);
        int v = somando ? antes + k : antes - k;

        dados[d] = (byte)v;
        dados[d + 1] = (byte)(v >> 8);

        int sreg = dados[EnderecoDoSreg] & ~(C | Z | N | V | S);

        if (somando ? v > 0xFFFF : v < 0) sreg |= C;
        if ((v & 0xFFFF) == 0) sreg |= Z;
        if ((v & 0x8000) != 0) sreg |= N;
        if (somando ? (~antes & v & 0x8000) != 0 : (antes & ~v & 0x8000) != 0) sreg |= V;
        if (((sreg & N) != 0) ^ ((sreg & V) != 0)) sreg |= S;

        dados[EnderecoDoSreg] = (byte)sreg;
        Ciclos++;
    }

    void Multiplicar(ushort op, bool comSinal, bool altos)
    {
        int d = altos ? 16 + ((op >> 4) & 0x0F) : (op >> 4) & 0x1F;
        int r = altos ? 16 + (op & 0x0F) : (op & 0x0F) | ((op >> 5) & 0x10);

        int a = comSinal ? (sbyte)R(d) : R(d);
        int b = comSinal ? (sbyte)R(r) : R(r);
        int v = a * b;

        Par(0, v & 0xFFFF);
        Bandeira(AvrIsa.BitC, (v & 0x8000) != 0);
        Bandeira(AvrIsa.BitZ, (v & 0xFFFF) == 0);

        Ciclos++;
    }

    void MultiplicarMeio(ushort op, bool comSinal)
    {
        int d = 16 + ((op >> 4) & 7), r = 16 + (op & 7);
        int v = (comSinal ? (sbyte)R(d) : R(d)) * R(r);

        Par(0, v & 0xFFFF);
        Bandeira(AvrIsa.BitC, (v & 0x8000) != 0);
        Bandeira(AvrIsa.BitZ, (v & 0xFFFF) == 0);

        Ciclos++;
    }

    // =================================================================
    //  interrupções e temporizadores
    // =================================================================
    //
    // É o que um sistema de irrigação de verdade usa: o Timer1 conta
    // enquanto o programa faz outra coisa, e a ISR avisa. Sem eles, o
    // único jeito de marcar tempo seria o laço de espera -- que trava
    // o programa inteiro enquanto conta.
    long ciclosDoTimer0, ciclosDoTimer1;

    static int Divisor(int ccr) => (ccr & 7) switch
    {
        1 => 1, 2 => 8, 3 => 64, 4 => 256, 5 => 1024, _ => 0,
    };

    /// <summary>Alguma bandeira de interrupção pedindo, com a máscara liberando?</summary>
    bool Pendente()
        => (dados[AvrIsa.BaseDaES + AvrIsa.TIFR1] & dados[AvrIsa.TIMSK1] & 3) != 0
           || (dados[AvrIsa.BaseDaES + AvrIsa.TIFR0] & dados[AvrIsa.TIMSK0] & 3) != 0;

    void Interromper()
    {
        foreach (var (registradorDaBandeira, bitDaBandeira,
                      registradorDaMascara, bitDaMascara, vetor) in new[]
                 {
                     (AvrIsa.BaseDaES + AvrIsa.TIFR1, 0, AvrIsa.TIMSK1, 0, AvrIsa.VetorTimer1Ovf),
                     (AvrIsa.BaseDaES + AvrIsa.TIFR1, 1, AvrIsa.TIMSK1, 1, AvrIsa.VetorTimer1CompA),
                     (AvrIsa.BaseDaES + AvrIsa.TIFR0, 0, AvrIsa.TIMSK0, 0, AvrIsa.VetorTimer0Ovf),
                     (AvrIsa.BaseDaES + AvrIsa.TIFR0, 1, AvrIsa.TIMSK0, 1, AvrIsa.VetorTimer0CompA),
                 })
        {
            bool pediu = (dados[registradorDaBandeira] & (1 << bitDaBandeira)) != 0;
            bool liberada = (dados[registradorDaMascara] & (1 << bitDaMascara)) != 0;

            if (!pediu || !liberada) continue;

            // A bandeira se apaga ao ATENDER, como no chip.
            dados[registradorDaBandeira] &= (byte)~(1 << bitDaBandeira);

            Empilhar((byte)(Pc >> 8));
            Empilhar((byte)Pc);

            // O chip desliga as interrupções ao entrar na ISR; o RETI
            // as liga de volta. Sem isso, uma ISR lenta se interrompe
            // sozinha e a pilha cresce até estourar.
            Bandeira(AvrIsa.BitI, false);

            Pc = vetor;
            Ciclos += 4;
            return;
        }
    }

    void AndarComOsTimers()
    {
        // ---- Timer0, de 8 bits ----
        int divisor0 = Divisor(dados[AvrIsa.TCCR0B]);

        if (divisor0 > 0)
        {
            ciclosDoTimer0 += Ciclos - ciclosVistos;

            while (ciclosDoTimer0 >= divisor0)
            {
                ciclosDoTimer0 -= divisor0;

                byte antes = dados[AvrIsa.TCNT0];
                dados[AvrIsa.TCNT0] = (byte)(antes + 1);

                if (dados[AvrIsa.TCNT0] == 0)
                    dados[AvrIsa.BaseDaES + AvrIsa.TIFR0] |= 1;

                if (dados[AvrIsa.TCNT0] == dados[AvrIsa.OCR0A])
                    dados[AvrIsa.BaseDaES + AvrIsa.TIFR0] |= 2;
            }
        }

        // ---- Timer1, de 16 bits ----
        int divisor1 = Divisor(dados[AvrIsa.TCCR1B]);

        if (divisor1 > 0)
        {
            ciclosDoTimer1 += Ciclos - ciclosVistos;

            while (ciclosDoTimer1 >= divisor1)
            {
                ciclosDoTimer1 -= divisor1;

                int conta = (dados[AvrIsa.TCNT1L] | (dados[AvrIsa.TCNT1H] << 8)) + 1;

                dados[AvrIsa.TCNT1L] = (byte)conta;
                dados[AvrIsa.TCNT1H] = (byte)(conta >> 8);

                if ((conta & 0xFFFF) == 0) dados[AvrIsa.BaseDaES + AvrIsa.TIFR1] |= 1;

                int alvo = dados[AvrIsa.OCR1AL] | (dados[AvrIsa.OCR1AH] << 8);

                if ((conta & 0xFFFF) == alvo && alvo != 0)
                {
                    dados[AvrIsa.BaseDaES + AvrIsa.TIFR1] |= 2;

                    // Modo CTC: o contador zera ao bater no alvo. É o
                    // modo que se usa para marcar um intervalo exato.
                    if ((dados[AvrIsa.TCCR1B] & (1 << 3)) != 0)
                    {
                        dados[AvrIsa.TCNT1L] = 0;
                        dados[AvrIsa.TCNT1H] = 0;
                    }
                }
            }
        }

        ciclosVistos = Ciclos;
    }

    /// <summary>
    /// Até que ciclo os temporizadores já contaram.
    ///
    /// Um marcador só para os dois: eles andam com o MESMO relógio, e o
    /// que cada um faz com os ciclos é dividir pelo próprio prescaler.
    /// </summary>
    long ciclosVistos;

    // =================================================================
    //  para o depurador e para o teste
    // =================================================================
    public int Registro(int n) => n is >= 0 and < 32 ? dados[n] : 0;

    public byte Memoria(int endereco)
        => endereco >= 0 && endereco < dados.Length ? dados[endereco] : (byte)0;

    public int Pilha => Sp;

    /// <summary>Para o teste: o SREG inteiro, como o chip o guarda.</summary>
    internal byte TesteSreg => Sreg;

    /// <summary>Para o teste: liga uma bandeira de interrupção, como o hardware ligaria.</summary>
    internal void TesteLigarBandeira(int registradorDeES, int bit)
        => dados[AvrIsa.BaseDaES + registradorDeES] |= (byte)(1 << bit);

    /// <summary>Para o teste: o byte de um registrador de E/S.</summary>
    internal byte TesteES(int registradorDeES) => dados[AvrIsa.BaseDaES + registradorDeES];

    /// <summary>O SREG em texto, na ordem da folha de dados.</summary>
    public string Flags
    {
        get
        {
            var letras = new[] { 'C', 'Z', 'N', 'V', 'S', 'H', 'T', 'I' };
            return string.Concat(letras.Select((c, i) => Bandeira(i) ? c : '-'));
        }
    }

    /// <summary>Quantos microssegundos de chip já se passaram.</summary>
    public double Microssegundos => Ciclos * AvrIsa.NanosPorCiclo / 1000.0;
}
