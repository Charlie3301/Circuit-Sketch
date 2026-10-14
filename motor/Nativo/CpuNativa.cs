using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Simulador;

/// <summary>
/// A CPU simulada do codigo nativo -- os .c que o TinyCC compila, como o DOOM.
///
/// O C nativo roda na velocidade do computador, varias vezes mais rapido que
/// o chip: neste computador de desenvolvimento o CoreMark compilado pelo
/// TinyCC fez 4134 iteracoes por segundo, e a ESP32-S3 faz 613,86 (a ESP32,
/// 504,85). Cada vez que o C chama o simulador -- o relogio, o SPI da tela, a
/// serial, um semaforo --, o tempo de processador que ele gastou desde a
/// chamada anterior vira o tempo que o chip levaria, e a thread espera a
/// diferenca. Assim o DOOM anda na velocidade da placa, e nao na do PC.
///
/// O tempo de processador e o da propria thread (QueryThreadCycleTime): o
/// que ela passou esperando -- um Sleep, um semaforo, o Windows dando a vez a
/// outra -- nao conta como conta feita. Cada tarefa do FreeRTOS e uma thread,
/// e cada uma paga o seu (as S3 tem dois nucleos).
/// </summary>
static partial class CpuNativa
{
    /// <summary>
    /// Quantas voltas da cs_bancada (nativo/runtime/cs_bancada.c) valem uma
    /// iteracao do CoreMark, os dois compilados pelo mesmo TinyCC. Medido no
    /// desenvolvimento (25/09/2026, Core i3-1115G4), com o CoreMark de verdade
    /// rodando no mesmo processo, alternado com a bancada: 120764/4685,
    /// 129622/4502 e 131781/4698 -- 25,8, 28,8 e 28,1. O CoreMark nao vai junto
    /// com o programa; a razao vale em qualquer computador, porque os dois sao
    /// o mesmo tipo de codigo do mesmo compilador.
    /// </summary>
    const double VoltasPorIteracaoDoCoreMark = 27.5;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryThreadCycleTime(IntPtr thread, out ulong ciclos);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();

    static ulong CiclosDaThread()
    {
        QueryThreadCycleTime(GetCurrentThread(), out ulong ciclos);
        return ciclos;
    }

    /// <summary>
    /// O CoreMark publicado de cada chip, com um nucleo, na frequencia de
    /// fabrica (as fichas da Espressif). A H2 nao tem numero publicado: e o
    /// nucleo da C6, a 96 MHz.
    /// </summary>
    public static double CoreMarkDo(PerfilPlaca placa) => placa?.Id switch
    {
        "esp32-s3-devkit" => 613.86,
        "esp32-s2-devkit" => 472.73,
        "esp32-c3-devkit" => 407.22,
        "esp32-c6-devkit" => 464.36,
        "esp32-h2-devkit" => 464.36 * 96 / 160,
        _ => 504.85,
    };

    /// <summary>Ciclos do computador por volta da bancada: medidos uma vez por processo.</summary>
    static double ciclosPorVolta;

    /// <summary>
    /// Microssegundos de chip por ciclo de processador do computador, para
    /// esta placa. Zero: sem a bancada (nao da para medir), a CPU simulada
    /// fica desligada e o C anda na velocidade do PC.
    /// </summary>
    public static unsafe double Calibrar(PerfilPlaca placa, IntPtr bancada)
    {
        // CS_CPU_NATIVA=0: o C na velocidade do PC (para medir e diagnosticar).
        if (bancada == IntPtr.Zero || Environment.GetEnvironmentVariable("CS_CPU_NATIVA") == "0") return 0;
        if (ciclosPorVolta <= 0) ciclosPorVolta = Medir((delegate* unmanaged<int, uint>)bancada);
        if (ciclosPorVolta <= 0) return 0;
        return 1e6 / CoreMarkDo(placa) / (ciclosPorVolta * VoltasPorIteracaoDoCoreMark);
    }

    static unsafe double Medir(delegate* unmanaged<int, uint> bancada)
    {
        bancada(5);
        var relogio = Stopwatch.StartNew();
        ulong antes = CiclosDaThread();
        long voltas = 0;
        while (relogio.ElapsedMilliseconds < 40)
        {
            bancada(20);
            voltas += 20;
        }
        ulong depois = CiclosDaThread();
        return voltas > 0 && depois > antes ? (double)(depois - antes) / voltas : 0;
    }

    [ThreadStatic] static ulong ciclosNaSaida;
    [ThreadStatic] static long paredeNaSaida;
    [ThreadStatic] static double divida;

    /// <summary>
    /// O C chamou o simulador: o que ele fez desde a ultima volta vira tempo
    /// de chip, e a thread espera o que falta. A divida so e paga acima de
    /// 1,5 ms -- o Sleep do Windows nao dorme menos que isso --, e o credito
    /// (a thread esperou por outro motivo) nao passa de 20 ms.
    /// </summary>
    public static void Entrar(double usPorCiclo)
    {
        if (usPorCiclo <= 0) return;
        ulong agora = CiclosDaThread();
        long parede = Stopwatch.GetTimestamp();
        if (ciclosNaSaida != 0 && agora > ciclosNaSaida)
        {
            double chipUs = (agora - ciclosNaSaida) * usPorCiclo;
            double passouUs = (parede - paredeNaSaida) * 1e6 / Stopwatch.Frequency;
            divida = Math.Max(-20_000, divida + chipUs - passouUs);
            if (divida > 1500)
            {
                long antes = Stopwatch.GetTimestamp();
                Thread.Sleep((int)(divida / 1000));
                divida -= (Stopwatch.GetTimestamp() - antes) * 1e6 / Stopwatch.Frequency;
            }
        }
        ciclosNaSaida = 0;
    }

    /// <summary>O simulador volta para o C: a conta do C recomeca daqui.</summary>
    public static void Sair(double usPorCiclo)
    {
        if (usPorCiclo <= 0) return;
        ciclosNaSaida = CiclosDaThread();
        paredeNaSaida = Stopwatch.GetTimestamp();
    }
}
