using System.Runtime.InteropServices;
using System.Threading;

namespace Simulador.App;

/// <summary>
/// O alto-falante tocando o som que a placa manda pelo I2S (o DOOM, um
/// player de WAV): as amostras da SaidaDeAudio da placa vao para o waveOut,
/// na taxa dela -- 11025 Hz no DOOM. A Buzina faz o tom do tone(); aqui e
/// audio de verdade, amostra por amostra.
///
/// Um fio de fundo enche blocos de 20 ms. Se a placa atrasa, entra silencio;
/// se ela adianta demais (mais de 150 ms esperando), o excesso vai fora: o
/// som acompanha a tela, e nao o contrario -- e o que o DMA do I2S faz na
/// placa, que toca e esquece.
/// </summary>
public sealed class Falante : IDisposable
{
    const int MsPorBloco = 20;
    const int Blocos = 5;
    const int MsDeFolga = 150;

    volatile SaidaDeAudio fonte;
    volatile bool estereo;
    volatile bool descartado;
    Thread fio;
    readonly object trava = new();

    /// <summary>A ultima vez que o waveOut recusou abrir: sem placa de som, nao insiste a cada quadro.</summary>
    DateTime recusou = DateTime.MinValue;

    /// <summary>
    /// O som a tocar agora; nulo e calar. Estereo: os dois canais separados
    /// (o fone no DAC); senao, a media dos dois (o alto-falante no amplificador).
    /// </summary>
    public void Tocar(SaidaDeAudio saida, bool emEstereo = false)
    {
        estereo = emEstereo;
        fonte = saida;
        if (saida is null || descartado) return;
        lock (trava)
        {
            if (fio is not null || DateTime.UtcNow - recusou < TimeSpan.FromSeconds(5)) return;
            fio = new Thread(Rodar) { IsBackground = true, Name = "alto-falante" };
            fio.Start();
        }
    }

    void Rodar()
    {
        try
        {
            // Volta a abrir quando a taxa muda (o sketch reabriu o I2S com outra).
            while (!descartado && fonte is { } atual && TocarNaTaxa(atual.Taxa, estereo)) { }
        }
        catch
        {
            // Dispositivo sumiu no meio: fica mudo.
        }
        finally
        {
            lock (trava) if (fio == Thread.CurrentThread) fio = null;
        }
    }

    /// <summary>Toca enquanto a taxa (e o estereo) for esta. Verdadeiro: mudou, e e para abrir de novo.</summary>
    bool TocarNaTaxa(int taxa, bool doisCanais)
    {
        int canais = doisCanais ? 2 : 1;
        var formato = new WAVEFORMATEX
        {
            wFormatTag = 1,
            nChannels = (short)canais,
            nSamplesPerSec = taxa,
            wBitsPerSample = 16,
            nBlockAlign = (short)(2 * canais),
            nAvgBytesPerSec = taxa * 2 * canais,
        };
        if (waveOutOpen(out IntPtr dispositivo, -1, ref formato, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        {
            recusou = DateTime.UtcNow;
            return false;
        }

        int porBloco = Math.Max(64, taxa * MsPorBloco / 1000);   // quadros
        int bytes = porBloco * 2 * canais;
        var amostras = new short[porBloco * canais];
        var buffers = new IntPtr[Blocos];
        var cabecas = new IntPtr[Blocos];
        var naFila = new bool[Blocos];
        int preparados = 0;
        try
        {
            for (int i = 0; i < Blocos; i++)
            {
                buffers[i] = Marshal.AllocHGlobal(bytes);
                cabecas[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                Marshal.StructureToPtr(new WAVEHDR { lpData = buffers[i], dwBufferLength = (uint)bytes }, cabecas[i], false);
                if (waveOutPrepareHeader(dispositivo, cabecas[i], Marshal.SizeOf<WAVEHDR>()) != 0) return false;
                preparados++;
            }

            int calado = 0;
            while (!descartado)
            {
                var atual = fonte;
                if (atual is null || atual.Taxa != taxa || estereo != doisCanais) return atual is not null;

                bool enfileirou = false;
                for (int i = 0; i < Blocos; i++)
                {
                    var h = Marshal.PtrToStructure<WAVEHDR>(cabecas[i]);
                    if (naFila[i] && (h.dwFlags & 1) == 0) continue;   // WHDR_DONE: ainda tocando

                    // O que passou da folga e atraso: vai fora.
                    for (int sobra = atual.Disponiveis - taxa * MsDeFolga / 1000; sobra > 0;)
                    {
                        int fora = atual.Ler(amostras.AsSpan(0, Math.Min(sobra, porBloco)));
                        if (fora == 0) break;
                        sobra -= fora;
                    }

                    int n = (doisCanais ? atual.LerEstereo(amostras) : atual.Ler(amostras)) * canais;
                    Array.Clear(amostras, n, amostras.Length - n);
                    calado = n == 0 ? calado + 1 : 0;

                    double volume = GeradorDeOnda.Volume;
                    if (volume != 1)
                        for (int k = 0; k < n; k++)
                            amostras[k] = (short)Math.Clamp(amostras[k] * volume, short.MinValue, short.MaxValue);

                    Marshal.Copy(amostras, 0, buffers[i], amostras.Length);
                    h.dwFlags &= ~1u;
                    h.dwBufferLength = (uint)bytes;
                    Marshal.StructureToPtr(h, cabecas[i], false);
                    if (waveOutWrite(dispositivo, cabecas[i], Marshal.SizeOf<WAVEHDR>()) != 0) return false;
                    naFila[i] = true;
                    enfileirou = true;
                }

                // Uns 3 s sem nada da placa: larga o dispositivo; o proximo
                // som abre de novo.
                if (calado > 3000 / MsPorBloco) return false;
                Thread.Sleep(enfileirou ? 2 : 5);
            }
            return false;
        }
        finally
        {
            try { waveOutReset(dispositivo); } catch { }
            for (int i = 0; i < preparados; i++)
                try { waveOutUnprepareHeader(dispositivo, cabecas[i], Marshal.SizeOf<WAVEHDR>()); } catch { }
            foreach (var p in buffers) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            foreach (var p in cabecas) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            try { waveOutClose(dispositivo); } catch { }
        }
    }

    public void Dispose()
    {
        descartado = true;
        fonte = null;
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
    static extern int waveOutOpen(out IntPtr h, int dispositivo, ref WAVEFORMATEX formato, IntPtr retorno, IntPtr instancia, int flags);

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
