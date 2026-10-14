namespace Simulador;

/// <summary>
/// Como a RAM de uma familia de placas se divide entre o que e estatico
/// (as globais), a heap (malloc, new, String) e a pilha (as funcoes
/// chamadas).
///
/// Sao dois desenhos:
///  - compartilhada (AVR, STM32): um bloco so. As globais no comeco, a heap
///    crescendo logo depois delas, a pilha descendo do fim. Quando uma
///    encosta na outra, a placa corrompe a memoria -- e o "reinicia
///    sozinho" classico do Uno com String demais;
///  - separada (ESP32, ESP8266, RP2040): a pilha do loop e um bloco proprio
///    (8 KB na ESP32, 4 KB no ESP8266), e a heap e o que o sistema deixou
///    livre quando o setup() comeca.
/// </summary>
public sealed record LayoutDaRam
{
    public bool Compartilhada { get; init; }

    /// <summary>Compartilhada: o tamanho do bloco e o endereco do comeco (RAMSTART).</summary>
    public long RamTotal { get; init; }
    public long RamInicio { get; init; }

    /// <summary>
    /// Separada: a heap livre quando o setup() de um sketch VAZIO comeca. O
    /// sketch com mais globais que o vazio tem essa diferenca a menos.
    /// </summary>
    public long HeapNoSetup { get; init; }

    /// <summary>O que um sketch vazio ja ocupa de globais (a linha "Global variables use" da IDE).</summary>
    public long EstaticaVazia { get; init; }

    /// <summary>Separada: a pilha da tarefa do loop (0: dividida com a heap).</summary>
    public long PilhaDoLoop { get; init; }

    /// <summary>Quanto da pilha o core ja usou quando o setup() e chamado.</summary>
    public long PilhaNoSetup { get; init; }

    /// <summary>O que cada bloco da heap gasta alem do pedido, e o alinhamento dele.</summary>
    public int Cabecalho { get; init; }
    public int Alinhamento { get; init; } = 1;

    /// <summary>
    /// A String que cabe dentro do proprio objeto (o SSO do WString.h da
    /// ESP32 e do ESP8266): o buffer interno de 15 bytes guarda 14 letras, e
    /// um texto novo de ate 13 vai para la sem tocar a heap. 0: sempre na
    /// heap, como no AVR, na Pico e na STM32 -- la ate a String vazia tem
    /// um bloco (o '\0').
    /// </summary>
    public int TextoCurto { get; init; }

    /// <summary>O menor pedido que o malloc atende (no AVR, 2 bytes: o lugar do ponteiro da lista livre).</summary>
    public int MenorPedido { get; init; } = 1;

    /// <summary>AVR: o __malloc_margin da avr-libc, o que o malloc deixa para a pilha.</summary>
    public long MargemDoMalloc { get; init; }

    /// <summary>A PSRAM (a memoria de fora do chip), quando a placa tem e ela esta ligada.</summary>
    public long Psram { get; init; }

    /// <summary>O quadro minimo de uma chamada: endereco de volta e registradores salvos.</summary>
    public int QuadroMinimo { get; init; } = 8;

    /// <summary>A pilha cresce em passos deste tamanho (a ABI de cada chip).</summary>
    public int AlinhamentoDaPilha { get; init; } = 4;
}

/// <summary>
/// A RAM de uma placa rodando: quanto e estatico, quanto a heap tem em uso e
/// quanto a pilha desceu. E daqui que saem o ESP.getFreeHeap(), o malloc que
/// devolve NULL e a pilha que estoura -- como na placa, e nao os numeros
/// fixos de antes.
/// </summary>
public sealed class MemoriaDaPlaca(LayoutDaRam layout)
{
    public LayoutDaRam Layout { get; } = layout;

    /// <summary>As globais do sketch com o core (a linha da IDE).</summary>
    public long Estatica { get; private set; } = layout.EstaticaVazia;

    public long HeapUsada { get; private set; }
    public long PsramUsada { get; private set; }
    public long MaiorHeapUsada { get; private set; }

    /// <summary>A pilha que o sketch esta usando agora (sem a do core).</summary>
    public long PilhaUsada { get; private set; }
    public long MaiorPilha { get; private set; }

    readonly object trava = new();

    public void DefinirEstatica(long bytes)
    {
        lock (trava) Estatica = Math.Max(Layout.EstaticaVazia, bytes);
    }

    /// <summary>
    /// Toda a heap: na separada, o que sobra do sketch vazio menos as globais
    /// a mais deste; na compartilhada, o bloco menos as globais (a pilha
    /// divide o resto com ela).
    /// </summary>
    public long HeapTotal => Layout.Compartilhada
        ? Layout.RamTotal - Estatica
        : Math.Max(0, Layout.HeapNoSetup - (Estatica - Layout.EstaticaVazia));

    /// <summary>A heap livre agora. Na compartilhada, a pilha tambem come dela.</summary>
    public long HeapLivre
    {
        get
        {
            lock (trava)
                return Layout.Compartilhada
                    ? Math.Max(0, HeapTotal - HeapUsada - Layout.PilhaNoSetup - PilhaUsada)
                    : Math.Max(0, HeapTotal - HeapUsada);
        }
    }

    /// <summary>A menor heap livre desde o comeco (o getMinFreeHeap).</summary>
    public long MenorHeapLivre
    {
        get
        {
            lock (trava)
                return Layout.Compartilhada
                    ? Math.Max(0, HeapTotal - MaiorHeapUsada - Layout.PilhaNoSetup - MaiorPilha)
                    : Math.Max(0, HeapTotal - MaiorHeapUsada);
        }
    }

    public long PsramLivre => Math.Max(0, Layout.Psram - PsramUsada);

    /// <summary>O quanto um pedido de n bytes gasta de verdade: o cabecalho e o alinhamento.</summary>
    public long CustoDoBloco(long n)
    {
        long a = Math.Max(1, Layout.Alinhamento);
        return ((Math.Max(Math.Max(1, Layout.MenorPedido), n) + Layout.Cabecalho + a - 1) / a) * a;
    }

    /// <summary>
    /// O bloco que a String pede para caber n letras (o changeBuffer do
    /// WString.cpp): na ESP32 e no ESP8266, (n + 16) arredondado para baixo
    /// em 16 -- sempre sobra folga; no AVR, na Pico e na STM32, n + 1 exato.
    /// </summary>
    public long BlocoDoTexto(long n) => Layout.TextoCurto > 0 ? (n + 16) & ~15L : n + 1;

    /// <summary>
    /// O que cada radio tira da heap quando liga -- ESP.getFreeHeap() antes e
    /// depois, arredondado; e uma estimativa, que muda com a versao do core.
    /// Na ESP32 o driver do WiFi, com os buffers do lwIP e as tarefas dele,
    /// leva ~50 KB. O BLE do core 3.x e o Bluedroid na ESP32 classica (~70
    /// KB, fora os ~55 KB do controlador, que ja ficaram presos no boot) e o
    /// NimBLE nas outras (~32 KB). O BluetoothSerial (classico) ~85 KB. No
    /// ESP8266 o WiFi mora na RAM desde o boot -- conectar so abre os
    /// buffers. Na Pico W o lwIP e estatico; conectar aloca pouco. Na Nano
    /// RP2040 Connect o radio e o modulo NINA, que tem RAM propria.
    /// </summary>
    public static long CustoDoRadio(string radio, PerfilPlaca placa)
    {
        string proc = placa?.Processador ?? "";
        string id = placa?.Id ?? "";
        bool esp32 = proc.StartsWith("Xtensa", StringComparison.Ordinal) || proc == "RISC-V";
        return radio switch
        {
            "wifi" when proc == "Xtensa LX6" => 50_000,
            "wifi" when esp32 => 45_000,
            "wifi" when proc == "ESP8266" => 5_000,
            "wifi" when id == "pi-pico-w" => 12_000,
            "ble" when proc == "Xtensa LX6" => 70_000,
            "ble" when esp32 => 32_000,
            "bt" when proc == "Xtensa LX6" => 85_000,

            // A ArduinoBLE: na ESP32 so o controlador (sem o Bluedroid); na
            // Nano RP2040 Connect o BLE mora no modulo NINA, e aqui ficam so
            // os buffers do HCI.
            "arduinoble" when proc == "Xtensa LX6" => 30_000,
            "arduinoble" when esp32 => 20_000,
            "arduinoble" when id == "nano-rp2040-connect" => 4_000,
            _ => 0,
        };
    }

    /// <summary>
    /// Reserva um bloco. Falso: nao cabe -- o malloc da placa devolveria NULL.
    /// No AVR, o malloc ainda deixa o __malloc_margin livre para a pilha.
    /// </summary>
    public bool Alocar(long n, bool psram, out long custo)
    {
        custo = CustoDoBloco(n);
        lock (trava)
        {
            if (psram)
            {
                if (PsramUsada + custo > Layout.Psram) return false;
                PsramUsada += custo;
                return true;
            }
            if (custo + Layout.MargemDoMalloc > HeapLivreSemTrava()) return false;
            HeapUsada += custo;
            if (HeapUsada > MaiorHeapUsada) MaiorHeapUsada = HeapUsada;
            return true;
        }
    }

    long HeapLivreSemTrava() => Layout.Compartilhada
        ? HeapTotal - HeapUsada - Layout.PilhaNoSetup - PilhaUsada
        : HeapTotal - HeapUsada;

    public void Liberar(long custo, bool psram)
    {
        lock (trava)
        {
            if (psram) PsramUsada = Math.Max(0, PsramUsada - custo);
            else HeapUsada = Math.Max(0, HeapUsada - custo);
        }
    }

    /// <summary>O realloc que falhou: o bloco velho, que ja tinha saido da conta, volta como estava.</summary>
    public void Retomar(long custo, bool psram)
    {
        lock (trava)
        {
            if (psram) PsramUsada += custo;
            else HeapUsada += custo;
        }
    }

    /// <summary>
    /// Uma funcao chamada: o quadro dela desce a pilha. Falso: estourou -- a
    /// pilha do loop passou do tamanho dela, ou (compartilhada) encostou na
    /// heap.
    /// </summary>
    public bool Empilhar(long bytes)
    {
        lock (trava)
        {
            PilhaUsada += bytes;
            if (PilhaUsada > MaiorPilha) MaiorPilha = PilhaUsada;
            return !PilhaEstourada;
        }
    }

    public void Desempilhar(long bytes)
    {
        lock (trava) PilhaUsada = Math.Max(0, PilhaUsada - bytes);
    }

    public bool PilhaEstourada => Layout.Compartilhada
        ? Layout.PilhaNoSetup + PilhaUsada > HeapTotal - HeapUsada
        : Layout.PilhaNoSetup + PilhaUsada > Layout.PilhaDoLoop;

    /// <summary>A pilha que ainda cabe (o uxTaskGetStackHighWaterMark da tarefa do loop).</summary>
    public long PilhaQueSobrouNoPior => Layout.Compartilhada
        ? Math.Max(0, HeapTotal - HeapUsada - Layout.PilhaNoSetup - MaiorPilha)
        : Math.Max(0, Layout.PilhaDoLoop - Layout.PilhaNoSetup - MaiorPilha);

    // ---- os enderecos, para o jeito AVR de medir a RAM livre ----

    /// <summary>O __heap_start: logo depois das globais.</summary>
    public long InicioDaHeap => Layout.RamInicio + Estatica;

    /// <summary>O __brkval: o topo da heap -- zero enquanto ninguem pediu nada.</summary>
    public long TopoDaHeap => HeapUsada == 0 ? 0 : InicioDaHeap + HeapUsada;

    /// <summary>O ponteiro da pilha (SP): o fim da RAM menos o que desceu.</summary>
    public long PonteiroDaPilha => Layout.RamInicio + Layout.RamTotal - 1 - Layout.PilhaNoSetup - PilhaUsada;

    // =====================================================================
    //  cada familia
    // =====================================================================

    /// <summary>
    /// O desenho da RAM de cada placa.
    ///
    /// "Globais de um sketch vazio": o numero da propria IDE, compilando de
    /// verdade (core AVR 1.8.6 e esp32 3.3.11, 25/09/2026) -- 9 bytes no Uno e
    /// no Mega; 22.100 na ESP32, 16.496 na S2, 21.592 na S3, 13.192 na C3,
    /// 13.924 na C6 e 13.852 na H2.
    ///
    /// A heap no setup() das ESP32 sai do mapa de cada chip: do _heap_start do
    /// ELF do sketch vazio (0x3FC97A58 na S3, 0x3FC8ED90 na C3, 0x4080E9E0 na
    /// C6, 0x4080E340 na H2, 0x3FFC3930 na S2) ate o fim da SRAM que o ESP-IDF
    /// entrega a heap -- na ESP32 classica, as tres regioes de DRAM, a IRAM
    /// que sobra depois do codigo (de 0x4008EB10) e os ~55 KB do Bluetooth
    /// que o core devolve --, menos as pilhas e estruturas que o core cria
    /// antes do setup() (loopTask, IDLE, esp_timer). E uma estimativa: a
    /// medida de verdade so o chip da, e varia com a versao do core.
    /// </summary>
    public static LayoutDaRam De(PerfilPlaca placa)
    {
        string id = placa?.Id ?? "";
        return placa?.Processador switch
        {
            "ATmega328P" => Avr(2048, 0x100, 6),
            "ATmega2560" => Avr(8192, 0x200, 9),
            "ATtiny85" => Avr(512, 0x60, 6) with { MargemDoMalloc = 32 },
            "Xtensa LX6" => Esp32(346_000, 22_100),
            "Xtensa LX7" when id == "esp32-s2-devkit" => Esp32(214_000, 16_496),
            "Xtensa LX7" => Esp32(365_000, 21_592) with { Psram = placa.PsramBytes },
            "RISC-V" when id == "esp32-c3-devkit" => Esp32(308_000, 13_192) with { QuadroMinimo = 16, AlinhamentoDaPilha = 16 },
            "RISC-V" when id == "esp32-h2-devkit" => Esp32(252_000, 13_852) with { QuadroMinimo = 16, AlinhamentoDaPilha = 16 },
            "RISC-V" => Esp32(444_000, 13_924) with { QuadroMinimo = 16, AlinhamentoDaPilha = 16 },
            "ESP8266" => new LayoutDaRam
            {
                HeapNoSetup = 51_000, EstaticaVazia = 28_000, PilhaDoLoop = 4096, PilhaNoSetup = 400,
                Cabecalho = 8, Alinhamento = 8, TextoCurto = 14, QuadroMinimo = 16, AlinhamentoDaPilha = 16,
            },
            "RP2040" => new LayoutDaRam
            {
                HeapNoSetup = id == "nano-rp2040-connect" ? 190_000 : id == "pi-pico-w" ? 215_000 : 245_000,
                EstaticaVazia = 11_000, PilhaDoLoop = 8192, PilhaNoSetup = 300,
                Cabecalho = 8, Alinhamento = 8, QuadroMinimo = 8, AlinhamentoDaPilha = 8,
            },
            _ => new LayoutDaRam
            {
                // STM32: newlib, heap e pilha no mesmo bloco; o _sbrk do core
                // recusa passar do _Min_Stack_Size abaixo da pilha.
                Compartilhada = true, RamTotal = placa?.RamBytes ?? 8192, RamInicio = 0x20000000,
                EstaticaVazia = 1_100, PilhaNoSetup = 200, MargemDoMalloc = 1024,
                Cabecalho = 8, Alinhamento = 8, QuadroMinimo = 8, AlinhamentoDaPilha = 8,
            },
        };
    }

    static LayoutDaRam Avr(long ram, long inicio, long pilhaNoSetup) => new()
    {
        Compartilhada = true, RamTotal = ram, RamInicio = inicio, EstaticaVazia = 9, PilhaNoSetup = pilhaNoSetup,
        Cabecalho = 2, Alinhamento = 1, MenorPedido = 2, MargemDoMalloc = 128, QuadroMinimo = 4, AlinhamentoDaPilha = 1,
    };

    /// <summary>A pilha do loopTask e 8192 bytes (CONFIG_ARDUINO_LOOP_STACK_SIZE); o bloco do TLSF gasta 8.</summary>
    static LayoutDaRam Esp32(long heapNoSetup, long estaticaVazia) => new()
    {
        HeapNoSetup = heapNoSetup, EstaticaVazia = estaticaVazia, PilhaDoLoop = 8192, PilhaNoSetup = 900,
        Cabecalho = 8, Alinhamento = 4, TextoCurto = 14, QuadroMinimo = 32, AlinhamentoDaPilha = 16,
    };
}

/// <summary>
/// O buffer de uma String do sketch: quanto cabe nele (o capacity() do
/// WString) e o bloco da heap que ele ocupa (0: dentro do objeto, no SSO,
/// ou ainda sem buffer).
/// </summary>
public sealed class TextoNaHeap
{
    public bool TemBuffer;
    public long Capacidade;
    public long Custo;
    public bool Psram;
}
