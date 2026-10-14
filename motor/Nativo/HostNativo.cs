using System.Runtime.InteropServices;

namespace Simulador;

/// <summary>
/// A tabela de funcoes que o C chama (o cs_host de nativo/include/cs_host.h).
///
/// A ordem e contrato: e a mesma dos campos da struct em C, e o modulo
/// confere o tamanho ao carregar. Cada funcao so repassa para o modulo da
/// thread que chamou -- e nunca deixa uma excecao voltar para o C, o que
/// derrubaria o processo inteiro.
/// </summary>
static unsafe class HostNativo
{
    static ModuloNativo M => ModuloNativo.Atual;

    public static IntPtr[] Tabela() =>
    [
        // texto
        (IntPtr)(delegate* unmanaged<byte*, int, void>)&Escrever,
        (IntPtr)(delegate* unmanaged<int>)&LerSerial,
        (IntPtr)(delegate* unmanaged<int>)&SerialDisponivel,
        // tempo
        (IntPtr)(delegate* unmanaged<long>)&Micros,
        (IntPtr)(delegate* unmanaged<long, int>)&Dormir,
        (IntPtr)(delegate* unmanaged<void>)&Ceder,
        // tarefas
        (IntPtr)(delegate* unmanaged<IntPtr, int, int>)&TarefaCriar,
        (IntPtr)(delegate* unmanaged<IntPtr, void>)&TarefaAcordar,
        // semaforos, filas, grupos
        (IntPtr)(delegate* unmanaged<int, int, int, long>)&SemCriar,
        (IntPtr)(delegate* unmanaged<long, long, int>)&SemPegar,
        (IntPtr)(delegate* unmanaged<long, int>)&SemDar,
        (IntPtr)(delegate* unmanaged<long, int>)&SemContagem,
        (IntPtr)(delegate* unmanaged<long, IntPtr>)&SemDono,
        (IntPtr)(delegate* unmanaged<long, void>)&SemApagar,
        (IntPtr)(delegate* unmanaged<int, int, long>)&FilaCriar,
        (IntPtr)(delegate* unmanaged<long, byte*, long, int, int>)&FilaEnviar,
        (IntPtr)(delegate* unmanaged<long, byte*, long, int, int>)&FilaReceber,
        (IntPtr)(delegate* unmanaged<long, int>)&FilaContagem,
        (IntPtr)(delegate* unmanaged<long, int>)&FilaLivre,
        (IntPtr)(delegate* unmanaged<long, void>)&FilaLimpar,
        (IntPtr)(delegate* unmanaged<long, void>)&FilaApagar,
        (IntPtr)(delegate* unmanaged<long>)&GrupoCriar,
        (IntPtr)(delegate* unmanaged<long, uint, uint, uint>)&GrupoMudar,
        (IntPtr)(delegate* unmanaged<long, uint, int, int, long, int*, uint>)&GrupoEsperar,
        (IntPtr)(delegate* unmanaged<long, void>)&GrupoApagar,
        // pinos
        (IntPtr)(delegate* unmanaged<int, int, void>)&GpioModo,
        (IntPtr)(delegate* unmanaged<int, int, void>)&GpioEscrever,
        (IntPtr)(delegate* unmanaged<int, int>)&GpioLer,
        (IntPtr)(delegate* unmanaged<int, int, void>)&GpioInterrupcao,
        (IntPtr)(delegate* unmanaged<long, int>)&GpioProximaBorda,
        // SPI
        (IntPtr)(delegate* unmanaged<int, int, int, int, int>)&SpiBarramento,
        (IntPtr)(delegate* unmanaged<int, int>)&SpiLiberar,
        (IntPtr)(delegate* unmanaged<int, int, int, int, int, long>)&SpiDispositivo,
        (IntPtr)(delegate* unmanaged<long, void>)&SpiRemover,
        (IntPtr)(delegate* unmanaged<long, byte*, byte*, int, int, int>)&SpiTransferir,
        // arquivos
        (IntPtr)(delegate* unmanaged<byte*, int, int, int, byte*, int>)&Montar,
        (IntPtr)(delegate* unmanaged<byte*, int>)&Desmontar,
        (IntPtr)(delegate* unmanaged<byte*, int, long>)&ArqAbrir,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int>)&ArqLer,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int>)&ArqEscrever,
        (IntPtr)(delegate* unmanaged<long, long, int, long>)&ArqPosicionar,
        (IntPtr)(delegate* unmanaged<long, long>)&ArqTamanho,
        (IntPtr)(delegate* unmanaged<long, void>)&ArqFechar,
        (IntPtr)(delegate* unmanaged<byte*, long*, int>)&ArqInfo,
        (IntPtr)(delegate* unmanaged<byte*, int>)&ArqApagar,
        (IntPtr)(delegate* unmanaged<byte*, byte*, int>)&ArqRenomear,
        (IntPtr)(delegate* unmanaged<byte*, int>)&ArqCriarPasta,
        (IntPtr)(delegate* unmanaged<byte*, long>)&PastaAbrir,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int*, int>)&PastaLer,
        (IntPtr)(delegate* unmanaged<long, void>)&PastaFechar,
        (IntPtr)(delegate* unmanaged<int, int, byte*, int, long*, long*, byte*, int*, int*, long>)&ParticaoAchar,
        (IntPtr)(delegate* unmanaged<long, long, byte*, long, int>)&ParticaoLer,
        (IntPtr)(delegate* unmanaged<long, long, byte*, long, int>)&ParticaoEscrever,
        (IntPtr)(delegate* unmanaged<long, long, long, int>)&ParticaoApagar,
        (IntPtr)(delegate* unmanaged<long, byte*, long, int>)&FlashLer,
        // numeros
        (IntPtr)(delegate* unmanaged<int, double, double, double>)&Mat,
        (IntPtr)(delegate* unmanaged<double, int, int, int, byte*, int, int>)&RealTexto,
        (IntPtr)(delegate* unmanaged<byte*, int*, double>)&TextoReal,
        // audio
        (IntPtr)(delegate* unmanaged<int, int, int, int, int, int, int, long>)&I2sAbrir,
        (IntPtr)(delegate* unmanaged<long, byte*, int, long, int>)&I2sEscrever,
        (IntPtr)(delegate* unmanaged<long, void>)&I2sFechar,
        // WiFi e HTTP
        (IntPtr)(delegate* unmanaged<byte*, byte*, int>)&WifiConectar,
        (IntPtr)(delegate* unmanaged<int>)&WifiEstado,
        (IntPtr)(delegate* unmanaged<byte*, byte*, byte*, int>)&WifiIp,
        (IntPtr)(delegate* unmanaged<void>)&WifiDesligar,
        (IntPtr)(delegate* unmanaged<int>)&WifiRssi,
        (IntPtr)(delegate* unmanaged<byte*, void>)&WifiMac,
        (IntPtr)(delegate* unmanaged<int, long>)&HttpIniciar,
        (IntPtr)(delegate* unmanaged<long, void>)&HttpParar,
        (IntPtr)(delegate* unmanaged<long, long, byte*, long>)&HttpEsperar,
        (IntPtr)(delegate* unmanaged<long, byte*, int>)&HttpStatus,
        (IntPtr)(delegate* unmanaged<long, byte*, int>)&HttpTipo,
        (IntPtr)(delegate* unmanaged<long, byte*, byte*, int>)&HttpCabecalho,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int>)&HttpEnviar,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int>)&HttpPedaco,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int>)&HttpConsulta,
        (IntPtr)(delegate* unmanaged<long, byte*, byte*, int, int>)&HttpLerCabecalho,
        (IntPtr)(delegate* unmanaged<long, byte*, int, int>)&HttpLerCorpo,
        (IntPtr)(delegate* unmanaged<long, int, void>)&HttpFim,
        // a placa
        (IntPtr)(delegate* unmanaged<byte*, byte*, void>)&Panico,
        (IntPtr)(delegate* unmanaged<void>)&Reiniciar,
        (IntPtr)(delegate* unmanaged<long>)&PsramTamanho,
        (IntPtr)(delegate* unmanaged<byte*, int, int>)&Chip,
        (IntPtr)(delegate* unmanaged<uint>)&Aleatorio,
        (IntPtr)(delegate* unmanaged<byte*, byte*, int, byte*, int, int>)&NvsGravar,
        (IntPtr)(delegate* unmanaged<byte*, byte*, int*, byte*, int, int>)&NvsLer,
        (IntPtr)(delegate* unmanaged<byte*, byte*, int>)&NvsApagar,
        (IntPtr)(delegate* unmanaged<int>)&RazaoDoReinicio,
        // o USB host
        (IntPtr)(delegate* unmanaged<int>)&UsbPendrive,
        // a thread (direto do Windows)
        Win32.TlsGetValue(),
    ];

    static string T(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((IntPtr)p);

    // Cada chamada: o modulo da thread, e nenhuma excecao volta para o C.
    static void Seguro(Action<ModuloNativo> acao)
    {
        var m = M;
        if (m is null) return;
        CpuNativa.Entrar(m.UsPorCiclo);
        try { acao(m); } catch (Exception e) { m.Falha(e); } finally { CpuNativa.Sair(m.UsPorCiclo); }
    }

    static R Seguro<R>(Func<ModuloNativo, R> funcao, R seFalhar)
    {
        var m = M;
        if (m is null) return seFalhar;
        CpuNativa.Entrar(m.UsPorCiclo);
        try { return funcao(m); } catch (Exception e) { m.Falha(e); return seFalhar; }
        finally { CpuNativa.Sair(m.UsPorCiclo); }
    }

    // ---- texto ----
    [UnmanagedCallersOnly] static void Escrever(byte* p, int n) { var m = M; if (m is null) return; CpuNativa.Entrar(m.UsPorCiclo); try { m.EscreverSerial(new ReadOnlySpan<byte>(p, Math.Max(0, n))); } catch (Exception e) { m.Falha(e); } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int LerSerial() => Seguro(m => m.Maquina.EntradaSerial.Ler(), -1);
    [UnmanagedCallersOnly] static int SerialDisponivel() => Seguro(m => m.Maquina.EntradaSerial.Disponivel, 0);

    // ---- tempo ----
    [UnmanagedCallersOnly] static long Micros() { var m = M; if (m is null) return 0; CpuNativa.Entrar(m.UsPorCiclo); try { return m.Micros(); } catch (Exception e) { m.Falha(e); return 0; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int Dormir(long us) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.Dormir(us); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly]
    static void Ceder()
    {
        double u = M?.UsPorCiclo ?? 0;
        CpuNativa.Entrar(u);
        Thread.Yield();
        CpuNativa.Sair(u);
    }

    // ---- tarefas ----
    [UnmanagedCallersOnly] static int TarefaCriar(IntPtr t, int pilha) => Seguro(m => m.CriarTarefa(t, pilha) ? 1 : 0, 0);
    [UnmanagedCallersOnly] static void TarefaAcordar(IntPtr t) => Seguro(m => m.AcordarTarefa(t));

    // ---- semaforos, filas, grupos ----
    [UnmanagedCallersOnly] static long SemCriar(int tipo, int maximo, int inicial) => Seguro(m => m.SemCriar(tipo, maximo, inicial), 0L);
    [UnmanagedCallersOnly] static int SemPegar(long s, long espera) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.SemPegar(s, espera); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int SemDar(long s) { var m = M; if (m is null) return 0; CpuNativa.Entrar(m.UsPorCiclo); try { return m.SemDar(s); } catch (Exception e) { m.Falha(e); return 0; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int SemContagem(long s) => Seguro(m => m.SemContagem(s), 0);
    [UnmanagedCallersOnly] static IntPtr SemDono(long s) => Seguro(m => m.SemDono(s), IntPtr.Zero);
    [UnmanagedCallersOnly] static void SemApagar(long s) => Seguro(m => m.Apagar(s));
    [UnmanagedCallersOnly] static long FilaCriar(int tamanho, int item) => Seguro(m => m.FilaCriar(tamanho, item), 0L);
    [UnmanagedCallersOnly] static int FilaEnviar(long f, byte* item, long espera, int modo) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.FilaEnviar(f, (IntPtr)item, espera, modo); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int FilaReceber(long f, byte* item, long espera, int espiar) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.FilaReceber(f, (IntPtr)item, espera, espiar != 0); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int FilaContagem(long f) => Seguro(m => m.FilaContagem(f), 0);
    [UnmanagedCallersOnly] static int FilaLivre(long f) => Seguro(m => m.FilaLivre(f), 0);
    [UnmanagedCallersOnly] static void FilaLimpar(long f) => Seguro(m => m.FilaLimpar(f));
    [UnmanagedCallersOnly] static void FilaApagar(long f) => Seguro(m => m.Apagar(f));
    [UnmanagedCallersOnly] static long GrupoCriar() => Seguro(m => m.GrupoCriar(), 0L);
    [UnmanagedCallersOnly] static uint GrupoMudar(long g, uint ligar, uint desligar) => Seguro(m => m.GrupoMudar(g, ligar, desligar), 0u);
    [UnmanagedCallersOnly]
    static uint GrupoEsperar(long g, uint bits, int limpar, int todos, long espera, int* parar)
    {
        bool deveParar = false;
        uint r = Seguro(m => m.GrupoEsperar(g, bits, limpar != 0, todos != 0, espera, out deveParar), 0u);
        if (parar != null) *parar = deveParar || M is null ? 1 : 0;
        return r;
    }
    [UnmanagedCallersOnly] static void GrupoApagar(long g) => Seguro(m => m.Apagar(g));

    // ---- pinos ----
    [UnmanagedCallersOnly] static void GpioModo(int pino, int modo) => Seguro(m => m.GpioModo(pino, modo));
    [UnmanagedCallersOnly] static void GpioEscrever(int pino, int nivel) { var m = M; if (m is null) return; CpuNativa.Entrar(m.UsPorCiclo); try { m.GpioEscrever(pino, nivel); } catch (Exception e) { m.Falha(e); } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int GpioLer(int pino) { var m = M; if (m is null) return 0; CpuNativa.Entrar(m.UsPorCiclo); try { return m.GpioLer(pino); } catch (Exception e) { m.Falha(e); return 0; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static void GpioInterrupcao(int pino, int tipo) => Seguro(m => m.GpioInterrupcao(pino, tipo));
    [UnmanagedCallersOnly] static int GpioProximaBorda(long espera) => Seguro(m => m.GpioProximaBorda(espera), -2);

    // ---- SPI ----
    [UnmanagedCallersOnly] static int SpiBarramento(int host, int mosi, int miso, int sclk) => Seguro(m => m.SpiBarramento(host, mosi, miso, sclk), 0x102);
    [UnmanagedCallersOnly] static int SpiLiberar(int host) => Seguro(m => m.SpiLiberar(host), 0);
    [UnmanagedCallersOnly] static long SpiDispositivo(int host, int cs, int hz, int modo, int flags) => Seguro(m => m.SpiDispositivo(host, cs, hz, modo, flags), 0L);
    [UnmanagedCallersOnly] static void SpiRemover(long d) => Seguro(m => m.SpiRemover(d));
    [UnmanagedCallersOnly]
    static int SpiTransferir(long d, byte* tx, byte* rx, int bitsTx, int bitsRx)
    {
        var m = M;
        if (m is null) return 0;
        CpuNativa.Entrar(m.UsPorCiclo);
        try { return m.SpiTransferir(d, tx, rx, bitsTx, bitsRx); }
        catch (Exception e) { m.Falha(e); return 0; }
        finally { CpuNativa.Sair(m.UsPorCiclo); }
    }

    // ---- arquivos ----
    [UnmanagedCallersOnly] static int Montar(byte* ponto, int tipo, int host, int cs, byte* particao) { string a = T(ponto), b = T(particao); return Seguro(m => m.Montar(a, tipo, host, cs, b), 0x107); }
    [UnmanagedCallersOnly] static int Desmontar(byte* ponto) { string a = T(ponto); return Seguro(m => m.Desmontar(a), 0x103); }
    [UnmanagedCallersOnly] static long ArqAbrir(byte* caminho, int modo) { string a = T(caminho); return Seguro(m => m.ArqAbrir(a, modo), -5L); }
    [UnmanagedCallersOnly] static int ArqLer(long a, byte* buf, int n) { var m = M; if (m is null) return -5; CpuNativa.Entrar(m.UsPorCiclo); try { return m.ArqLer(a, (IntPtr)buf, n); } catch (Exception e) { m.Falha(e); return -5; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int ArqEscrever(long a, byte* buf, int n) { IntPtr p = (IntPtr)buf; return Seguro(m => m.ArqEscrever(a, p, n), -5); }
    [UnmanagedCallersOnly] static long ArqPosicionar(long a, long pos, int deOnde) => Seguro(m => m.ArqPosicionar(a, pos, deOnde), -22L);
    [UnmanagedCallersOnly] static long ArqTamanho(long a) => Seguro(m => m.ArqTamanho(a), 0L);
    [UnmanagedCallersOnly] static void ArqFechar(long a) => Seguro(m => m.ArqFechar(a));
    [UnmanagedCallersOnly]
    static int ArqInfo(byte* caminho, long* tamanho)
    {
        string a = T(caminho);
        long t = 0;
        int r = Seguro(m => m.ArqInfo(a, out t), 0);
        if (tamanho != null) *tamanho = t;
        return r;
    }
    [UnmanagedCallersOnly] static int ArqApagar(byte* caminho) { string a = T(caminho); return Seguro(m => m.ArqApagar(a), -5); }
    [UnmanagedCallersOnly] static int ArqRenomear(byte* velho, byte* novo) { string a = T(velho), b = T(novo); return Seguro(m => m.ArqRenomear(a, b), -5); }
    [UnmanagedCallersOnly] static int ArqCriarPasta(byte* caminho) { string a = T(caminho); return Seguro(m => m.ArqCriarPasta(a), -5); }
    [UnmanagedCallersOnly] static long PastaAbrir(byte* caminho) { string a = T(caminho); return Seguro(m => m.PastaAbrir(a), 0L); }
    [UnmanagedCallersOnly]
    static int PastaLer(long p, byte* nome, int n, int* tipo)
    {
        string achado = null;
        bool pasta = false;
        int r = Seguro(m => m.PastaLer(p, out achado, out pasta), 0);
        if (r > 0 && nome != null && n > 0)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(achado ?? "");
            int k = Math.Min(bytes.Length, n - 1);
            bytes.AsSpan(0, k).CopyTo(new Span<byte>(nome, k));
            nome[k] = 0;
            if (tipo != null) *tipo = pasta ? 2 : 1;
        }
        return r;
    }
    [UnmanagedCallersOnly] static void PastaFechar(long p) => Seguro(m => m.PastaFechar(p));
    [UnmanagedCallersOnly]
    static long ParticaoAchar(int tipo, int subtipo, byte* rotulo, int indice, long* endereco, long* tamanho, byte* achado,
                              int* tipoAchado, int* subtipoAchado)
    {
        string r = T(rotulo);
        long e = 0, t = 0;
        int ta = 0, sa = 0;
        string nome = null;
        long id = Seguro(m => m.ParticaoAchar(tipo, subtipo, r, indice, out e, out t, out nome, out ta, out sa), 0L);
        if (id > 0)
        {
            if (endereco != null) *endereco = e;
            if (tamanho != null) *tamanho = t;
            if (tipoAchado != null) *tipoAchado = ta;
            if (subtipoAchado != null) *subtipoAchado = sa;
            if (achado != null)
            {
                var b = System.Text.Encoding.UTF8.GetBytes(nome ?? "");
                int k = Math.Min(b.Length, 16);
                b.AsSpan(0, k).CopyTo(new Span<byte>(achado, k));
                achado[k] = 0;
            }
        }
        return id;
    }
    [UnmanagedCallersOnly] static int ParticaoLer(long p, long desloc, byte* buf, long n) { IntPtr b = (IntPtr)buf; return Seguro(m => m.ParticaoLer(p, desloc, b, n), 0x102); }
    [UnmanagedCallersOnly] static int ParticaoEscrever(long p, long desloc, byte* buf, long n) { IntPtr b = (IntPtr)buf; return Seguro(m => m.ParticaoEscrever(p, desloc, b, n), 0x102); }
    [UnmanagedCallersOnly] static int ParticaoApagar(long p, long desloc, long n) => Seguro(m => m.ParticaoApagarFaixa(p, desloc, n), 0x102);
    [UnmanagedCallersOnly] static int FlashLer(long endereco, byte* buf, long n) { IntPtr b = (IntPtr)buf; return Seguro(m => m.FlashLer(endereco, b, n), 0x102); }

    // ---- numeros ----
    [UnmanagedCallersOnly] static double Mat(int qual, double a, double b) => ModuloNativo.Matematica(qual, a, b);

    [UnmanagedCallersOnly]
    static int RealTexto(double v, int tipo, int precisao, int alternativo, byte* buf, int n)
    {
        try
        {
            string s = ModuloNativo.FormatarReal(v, (char)tipo, precisao, alternativo != 0);
            var bytes = System.Text.Encoding.ASCII.GetBytes(s);
            int k = Math.Min(bytes.Length, Math.Max(0, n - 1));
            bytes.AsSpan(0, k).CopyTo(new Span<byte>(buf, k));
            if (n > 0) buf[k] = 0;
            return k;
        }
        catch { return 0; }
    }

    [UnmanagedCallersOnly]
    static double TextoReal(byte* s, int* consumidos)
    {
        try
        {
            double v = ModuloNativo.LerReal(s, out int k);
            if (consumidos != null) *consumidos = k;
            return v;
        }
        catch
        {
            if (consumidos != null) *consumidos = 0;
            return 0;
        }
    }

    // ---- audio ----
    [UnmanagedCallersOnly] static long I2sAbrir(int porta, int taxa, int bits, int canais, int bclk, int ws, int dout) => Seguro(m => m.I2sAbrir(porta, taxa, bits, canais, bclk, ws, dout), 0L);
    [UnmanagedCallersOnly] static int I2sEscrever(long canal, byte* buf, int n, long espera) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.I2sEscrever(canal, (IntPtr)buf, n, espera); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static void I2sFechar(long canal) => Seguro(m => m.I2sFechar(canal));

    // ---- WiFi e HTTP ----
    [UnmanagedCallersOnly] static int WifiConectar(byte* ssid, byte* senha) { string a = T(ssid), b = T(senha); return Seguro(m => m.WifiConectar(a, b), 0); }
    [UnmanagedCallersOnly] static int WifiEstado() => Seguro(m => m.WifiEstado(), 0);
    [UnmanagedCallersOnly]
    static int WifiIp(byte* ip, byte* mascara, byte* saida)
    {
        byte[] a = null, b = null, c = null;
        int r = Seguro(m => m.WifiIp(out a, out b, out c), 0);
        if (r != 0)
        {
            for (int i = 0; i < 4; i++)
            {
                if (ip != null) ip[i] = a[i];
                if (mascara != null) mascara[i] = b[i];
                if (saida != null) saida[i] = c[i];
            }
        }
        return r;
    }
    [UnmanagedCallersOnly] static void WifiDesligar() => Seguro(m => m.WifiDesligar());
    [UnmanagedCallersOnly] static int WifiRssi() => Seguro(m => m.WifiRssi(), -127);
    [UnmanagedCallersOnly]
    static void WifiMac(byte* mac)
    {
        var bytes = Seguro(m => m.WifiMac(), new byte[] { 0x24, 0x0A, 0xC4, 0, 0, 1 });
        for (int i = 0; i < 6; i++) mac[i] = bytes[i];
    }
    [UnmanagedCallersOnly] static long HttpIniciar(int porta) => Seguro(m => m.HttpIniciar(porta), 0L);
    [UnmanagedCallersOnly] static void HttpParar(long s) => Seguro(m => m.HttpParar(s));
    [UnmanagedCallersOnly] static long HttpEsperar(long s, long espera, byte* info) { IntPtr p = (IntPtr)info; return Seguro(m => m.HttpEsperar(s, espera, p), -1L); }
    [UnmanagedCallersOnly] static int HttpStatus(long req, byte* status) { string a = T(status); return Seguro(m => m.HttpStatus(req, a), -1); }
    [UnmanagedCallersOnly] static int HttpTipo(long req, byte* tipo) { string a = T(tipo); return Seguro(m => m.HttpTipo(req, a), -1); }
    [UnmanagedCallersOnly] static int HttpCabecalho(long req, byte* nome, byte* valor) { string a = T(nome), b = T(valor); return Seguro(m => m.HttpCabecalho(req, a, b), -1); }
    [UnmanagedCallersOnly] static int HttpEnviar(long req, byte* buf, int n) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.HttpEnviar(req, buf == null ? default : new ReadOnlySpan<byte>(buf, Math.Max(0, n))); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly] static int HttpPedaco(long req, byte* buf, int n) { var m = M; if (m is null) return -1; CpuNativa.Entrar(m.UsPorCiclo); try { return m.HttpPedaco(req, buf == null ? default : new ReadOnlySpan<byte>(buf, Math.Max(0, n))); } catch (Exception e) { m.Falha(e); return -1; } finally { CpuNativa.Sair(m.UsPorCiclo); } }
    [UnmanagedCallersOnly]
    static int HttpConsulta(long req, byte* buf, int n)
    {
        string c = Seguro(m => m.HttpConsulta(req), null);
        return CopiarTexto(c, buf, n);
    }
    [UnmanagedCallersOnly]
    static int HttpLerCabecalho(long req, byte* nome, byte* buf, int n)
    {
        string a = T(nome);
        string c = Seguro(m => m.HttpLerCabecalho(req, a), null);
        return CopiarTexto(c, buf, n);
    }
    [UnmanagedCallersOnly] static int HttpLerCorpo(long req, byte* buf, int n) { IntPtr p = (IntPtr)buf; return Seguro(m => m.HttpLerCorpo(req, p, n), -1); }
    [UnmanagedCallersOnly] static void HttpFim(long req, int resultado) => Seguro(m => m.HttpFim(req, resultado));

    /// <summary>Texto para um buffer do C: devolve o tamanho inteiro (-1 se nao tem), copia o que cabe.</summary>
    static int CopiarTexto(string texto, byte* buf, int n)
    {
        if (texto is null) return -1;
        var bytes = System.Text.Encoding.UTF8.GetBytes(texto);
        if (buf != null && n > 0)
        {
            int k = Math.Min(bytes.Length, n - 1);
            bytes.AsSpan(0, k).CopyTo(new Span<byte>(buf, k));
            buf[k] = 0;
        }
        return bytes.Length;
    }

    // ---- a placa ----
    [UnmanagedCallersOnly] static void Panico(byte* motivo, byte* detalhe) { string a = T(motivo), b = T(detalhe); Seguro(m => m.Panico(a, b)); }
    [UnmanagedCallersOnly] static void Reiniciar() => Seguro(m => m.PedirReinicio(ReinicioDaPlaca.PeloSoftware));
    [UnmanagedCallersOnly] static long PsramTamanho() => Seguro(m => m.Maquina.Perfil.PsramBytes, 0L);
    [UnmanagedCallersOnly] static int Chip(byte* buf, int n) => CopiarTexto(Seguro(m => ModuloNativo.Chip(m.Maquina.Perfil.Id), "esp32"), buf, n);
    [UnmanagedCallersOnly] static uint Aleatorio() => (uint)Random.Shared.NextInt64(0, 1L << 32);
    [UnmanagedCallersOnly]
    static int NvsGravar(byte* espaco, byte* chave, int tipo, byte* dados, int n)
    {
        string a = T(espaco), b = T(chave);
        var bytes = dados == null ? [] : new ReadOnlySpan<byte>(dados, Math.Max(0, n)).ToArray();
        return Seguro(m => m.NvsGravar(a, b, tipo, bytes), -1);
    }
    [UnmanagedCallersOnly]
    static int NvsLer(byte* espaco, byte* chave, int* tipo, byte* dados, int n)
    {
        string a = T(espaco), b = T(chave);
        byte[] achado = null;
        int t = 0;
        int r = Seguro(m => m.NvsLer(a, b, out t, out achado), -1);
        if (r < 0) return -1;
        if (tipo != null) *tipo = t;
        if (dados != null && achado is not null)
            achado.AsSpan(0, Math.Min(achado.Length, Math.Max(0, n))).CopyTo(new Span<byte>(dados, Math.Min(achado.Length, Math.Max(0, n))));
        return achado?.Length ?? 0;
    }
    [UnmanagedCallersOnly] static int NvsApagar(byte* espaco, byte* chave) { string a = T(espaco), b = T(chave); return Seguro(m => m.NvsApagar(a, b), -1); }
    [UnmanagedCallersOnly] static int RazaoDoReinicio() => Seguro(m => m.RazaoDoReinicio, 1);
    [UnmanagedCallersOnly] static int UsbPendrive() => Seguro(m => m.Maquina.PendriveConectado?.Invoke() == true ? 1 : 0, 0);
}
