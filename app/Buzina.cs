using System.Runtime.InteropServices;
using System.Threading;

namespace Simulador.App;

/// <summary>
/// Som do buzzer: fluxo continuo de audio com fase continua.
///
/// A primeira versao gerava um WAV de um ciclo e tocava em laco com o
/// SoundPlayer. Dois defeitos, os dois audiveis:
///
/// 1. O periodo era "TaxaAmostragem / hz" em divisao INTEIRA. A 5 kHz
///    sobravam 8 amostras por ciclo e o tom saia a 5512 Hz -- 10% agudo
///    demais, quase um tom e meio de erro. So batia certo quando a conta
///    dava exata.
///
/// 2. Trocar de frequencia trocava o Stream e chamava PlayLooping() de
///    novo. Um sketch que varre o tom -- som de motor, sirene, melodia --
///    reiniciava o tocador dezenas de vezes por segundo, e o que se ouvia
///    era estalo em cima de estalo.
///
/// Agora o audio sai por waveOut (winmm, que todo Windows tem, sem
/// dependencia nova) em blocos de 20 ms. Um fio de fundo enche os blocos
/// a partir da frequencia ATUAL, guardando a fase de um bloco para o
/// outro: mudar de tom vira uma curva continua, sem estalo e sem
/// reiniciar nada. O ganho sobe e desce em rampa pelo mesmo motivo.
///
/// O timbre e uma onda quadrada com as harmonicas limitadas abaixo de
/// Nyquist. Um piezo nao e senoide: e a harmonica impar que da o "bip"
/// caracteristico. Limitar evita o chiado de aliasing nos tons agudos.
/// </summary>
public sealed class Buzina : IDisposable
{
    const int Taxa = 44100;
    const int AmostrasPorBloco = 882;          // 20 ms
    const int Blocos = 4;                      // 80 ms de folga

    volatile bool parar;

    IntPtr dispositivo;
    Thread fio;
    readonly object trava = new();
    readonly GeradorDeOnda gerador = new();

    /// <summary>
    /// As outras vozes: varios buzzers tocando juntos (os seis cilindros do
    /// motor que o sketch faz na interrupcao do timer). A primeira e o gerador.
    /// </summary>
    const int Vozes = 8;
    readonly GeradorDeOnda[] outras = [.. Enumerable.Range(1, Vozes - 1).Select(_ => new GeradorDeOnda())];
    short[] mistura = [];

    public void Definir(int hz)
    {
        gerador.Hz = hz is >= 20 and <= 20000 ? hz : 0;
        foreach (var v in outras) v.Hz = 0;
        if (gerador.Hz > 0) Garantir();
    }

    /// <summary>Varios tons ao mesmo tempo (um por buzzer tocando).</summary>
    public void Definir(IReadOnlyList<int> tons)
    {
        var validos = tons.Where(h => h is >= 20 and <= 20000).Take(Vozes).ToList();
        gerador.Hz = validos.Count > 0 ? validos[0] : 0;
        for (int i = 0; i < outras.Length; i++) outras[i].Hz = i + 1 < validos.Count ? validos[i + 1] : 0;
        if (gerador.Hz > 0) Garantir();
    }

    public void Parar()
    {
        gerador.Hz = 0;
        foreach (var v in outras) v.Hz = 0;
    }

    /// <summary>As vozes somadas, cada uma mais baixa quando sao varias (sem estourar).</summary>
    void Preencher(short[] destino)
    {
        gerador.Preencher(destino);
        int tocando = 1 + outras.Count(v => v.Hz > 0 || !v.Silencioso);
        if (tocando == 1) return;
        if (mistura.Length != destino.Length) mistura = new short[destino.Length];
        var soma = new int[destino.Length];
        for (int i = 0; i < destino.Length; i++) soma[i] = destino[i];
        foreach (var v in outras)
        {
            if (v.Hz == 0 && v.Silencioso) continue;
            v.Preencher(mistura);
            for (int i = 0; i < destino.Length; i++) soma[i] += mistura[i];
        }
        double escala = 1.0 / Math.Sqrt(tocando);
        for (int i = 0; i < destino.Length; i++) destino[i] = (short)Math.Clamp(soma[i] * escala, short.MinValue, short.MaxValue);
    }

    /// <summary>Conseguiu abrir a saida de audio? Falso = maquina muda.</summary>
    public bool Ativa => dispositivo != IntPtr.Zero;

    // ================= winmm =================

    /// <summary>A buzina foi descartada: nenhum fio novo abre o audio.</summary>
    volatile bool descartada;

    void Garantir()
    {
        if (fio is not null || descartada) return;
        lock (trava)
        {
            if (fio is not null || descartada) return;

            var formato = new WAVEFORMATEX
            {
                wFormatTag = 1,                  // PCM
                nChannels = 1,
                nSamplesPerSec = Taxa,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = Taxa * 2,
                cbSize = 0
            };

            // Maquina sem placa de som: o simulador segue mudo.
            if (waveOutOpen(out IntPtr aberto, -1, ref formato, IntPtr.Zero,
                            IntPtr.Zero, 0) != 0)
                return;

            // Cada fio e DONO do dispositivo que abriu, e so ele o fecha.
            // Antes os dois dividiam um campo: o fio que saia por silencio
            // fechava o dispositivo que um tone() novo tinha acabado de
            // abrir para o fio seguinte -- e o fio seguinte morria dentro
            // do waveOutUnprepareHeader, levando o programa junto.
            dispositivo = aberto;
            parar = false;
            var novo = new Thread(() => Rodar(aberto)) { IsBackground = true, Name = "buzina" };
            fio = novo;
            novo.Start();
        }
    }

    void Rodar(IntPtr meu)
    {
        var amostras = new short[AmostrasPorBloco];
        var buffers = new IntPtr[Blocos];
        var cabecas = new IntPtr[Blocos];
        int preparados = 0;
        int bytes = AmostrasPorBloco * 2;

        try
        {
            for (int i = 0; i < Blocos; i++)
            {
                buffers[i] = Marshal.AllocHGlobal(bytes);
                cabecas[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                var h = new WAVEHDR { lpData = buffers[i], dwBufferLength = (uint)bytes };
                Marshal.StructureToPtr(h, cabecas[i], false);
                if (waveOutPrepareHeader(meu, cabecas[i], Marshal.SizeOf<WAVEHDR>()) != 0)
                    return;                    // bloco que nao prepara nao toca
                preparados++;
            }

            int proximo = 0;
            int ociosos = 0;
            var naFila = new bool[Blocos];

            while (!parar)
            {
                bool enfileirou = false;

                for (int k = 0; k < Blocos; k++)
                {
                    int i = (proximo + k) % Blocos;
                    var h = Marshal.PtrToStructure<WAVEHDR>(cabecas[i]);

                    // WHDR_DONE = 1. Bloco ainda tocando: pula.
                    if (naFila[i] && (h.dwFlags & 1) == 0) continue;

                    Preencher(amostras);
                    Marshal.Copy(amostras, 0, buffers[i], amostras.Length);

                    h.dwFlags &= ~1u;
                    h.dwBufferLength = (uint)bytes;
                    Marshal.StructureToPtr(h, cabecas[i], false);

                    if (waveOutWrite(meu, cabecas[i], Marshal.SizeOf<WAVEHDR>()) != 0)
                        break;

                    naFila[i] = true;
                    enfileirou = true;
                    proximo = (i + 1) % Blocos;
                }

                // Calado ha um tempo: fecha tudo e deixa o fio morrer.
                // Um fio acordando a cada 2 ms para gerar silencio nao
                // custa muito, mas o alvo aqui sao os PCs fracos do
                // laboratorio -- e o proximo tone() reabre em 1 ms.
                ociosos = gerador.Hz == 0 && gerador.Silencioso ? ociosos + 1 : 0;
                if (ociosos > 150)
                {
                    lock (trava)
                    {
                        // Chegou um tone() bem na hora de sair: desiste.
                        if (gerador.Hz != 0) { ociosos = 0; continue; }

                        // Sai sem mexer no "parar", que e de todos: so larga o
                        // posto, para o proximo tone() abrir um fio novo.
                        if (fio == Thread.CurrentThread) fio = null;
                    }
                    break;
                }

                // Sem bloco livre: dorme menos que a duracao de um bloco,
                // senao a fila seca e o som pica.
                Thread.Sleep(enfileirou ? 2 : 5);
            }
        }
        catch
        {
            // Audio ocupado ou dispositivo sumiu no meio: fica mudo.
        }
        finally
        {
            // A limpeza e sempre do PROPRIO dispositivo, e so depois dela a
            // memoria dos blocos vai embora: o waveOutReset devolve todos
            // os blocos antes do unprepare.
            try { waveOutReset(meu); } catch { }
            for (int i = 0; i < preparados; i++)
                try { waveOutUnprepareHeader(meu, cabecas[i], Marshal.SizeOf<WAVEHDR>()); } catch { }

            foreach (var p in buffers) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            foreach (var p in cabecas) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);

            try { waveOutClose(meu); } catch { }

            lock (trava)
            {
                if (dispositivo == meu) dispositivo = IntPtr.Zero;
                if (fio == Thread.CurrentThread) fio = null;
            }
        }
    }

    /// <summary>
    /// Para o som e espera o fio fechar o dispositivo dele. Quem fecha e o
    /// fio, e nunca daqui: fechar enquanto ele ainda usava o dispositivo era
    /// o outro caminho da queda no waveOutUnprepareHeader.
    /// </summary>
    public void Dispose()
    {
        descartada = true;
        gerador.Hz = 0;
        parar = true;

        Thread ultimo;
        lock (trava) ultimo = fio;
        ultimo?.Join(1000);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX
    {
        public short wFormatTag, nChannels;
        public int nSamplesPerSec, nAvgBytesPerSec;
        public short nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    [DllImport("winmm.dll")]
    static extern int waveOutOpen(out IntPtr h, int dispositivo,
        ref WAVEFORMATEX formato, IntPtr retorno, IntPtr instancia, int flags);

    [DllImport("winmm.dll")]
    static extern int waveOutPrepareHeader(IntPtr h, IntPtr cabeca, int tamanho);

    [DllImport("winmm.dll")]
    static extern int waveOutUnprepareHeader(IntPtr h, IntPtr cabeca, int tamanho);

    [DllImport("winmm.dll")]
    static extern int waveOutWrite(IntPtr h, IntPtr cabeca, int tamanho);

    [DllImport("winmm.dll")]
    static extern int waveOutReset(IntPtr h);

    [DllImport("winmm.dll")]
    static extern int waveOutClose(IntPtr h);
}

/// <summary>
/// Gera as amostras do buzzer. Separado da Buzina de proposito: assim o
/// autoteste mede a onda sem abrir placa de som -- e a afinacao e a
/// continuidade da fase sao exatamente o que estava errado antes.
/// </summary>
public sealed class GeradorDeOnda
{
    public const int Taxa = 44100;
    public const double Amplitude = 0.22;
    const double Rampa = 0.004;              // segundos de ataque/corte

    /// <summary>Frequencia pedida agora; 0 = silencio.</summary>
    public volatile int Hz;

    /// <summary>
    /// O volume das configuracoes, multiplicando a amplitude: 1 e o de
    /// fabrica. Estatico porque o som e um so para o programa inteiro.
    /// </summary>
    public static double Volume { get; set; } = 1.0;

    double fase;
    double ganho;

    /// <summary>Ja desceu a rampa e nao ha mais nada para tocar.</summary>
    public bool Silencioso => ganho <= 0;

    /// <summary>
    /// Quadrada com harmonicas impares ate abaixo de Nyquist. Um piezo
    /// nao e senoide: e a harmonica impar que da o "bip". Cortar no
    /// limite evita o chiado de aliasing nos tons agudos.
    /// </summary>
    public static double Onda(double fase, int hz)
    {
        double v = 0;
        for (int n = 1; n <= 9; n += 2)
        {
            if (n * hz > Taxa * 0.45) break;
            v += Math.Sin(fase * n) / n;
        }
        return v * (4 / Math.PI) * 0.5;
    }

    public void Preencher(short[] destino)
    {
        int hz = Hz;
        double amplitude = Amplitude * Volume;
        double alvoGanho = hz > 0 ? amplitude : 0;
        double passo = Math.Max(amplitude, 1e-6) / (Rampa * Taxa);

        // O avanco por amostra vem da frequencia REAL, sem arredondar o
        // periodo para um numero inteiro de amostras. Era ai que 5 kHz
        // virava 5512 Hz.
        double avanco = hz > 0 ? 2 * Math.PI * hz / Taxa : 0;

        for (int i = 0; i < destino.Length; i++)
        {
            if (ganho < alvoGanho) ganho = Math.Min(alvoGanho, ganho + passo);
            else if (ganho > alvoGanho) ganho = Math.Max(alvoGanho, ganho - passo);

            if (ganho <= 0 && alvoGanho <= 0) { destino[i] = 0; continue; }

            // A fase segue de onde parou mesmo com o tom mudando: e o que
            // faz a varredura soar como curva em vez de picotada.
            fase += avanco;
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;

            destino[i] = (short)(Onda(fase, hz) * ganho * short.MaxValue);
        }
    }
}
