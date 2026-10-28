using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Simulador;

/// <summary>
/// O sketch compilado (a DLL do CompiladorNativo) rodando numa placa.
///
/// Cada placa carrega a sua copia da DLL -- as globais de uma nao sao as da
/// outra. O sketch roda na thread da placa; tudo o que ele pede ao hardware
/// chega aqui pela ponte (cs_ponte.h) e e atendido pelo interpretador da
/// placa, com a mesma implementacao do caminho interpretado.
///
/// O vigia (uma thread por placa) faz o que o hardware faz por fora do
/// codigo:
///
///   - a interrupcao: com o sketch num laco que nao chama nada, a borda do
///     botao suspende a thread dele, a rotina roda, e o sketch continua de
///     onde estava -- como a CPU interrompida;
///   - a parada: com a placa parada, o sketch preso num laco e desviado para
///     o salto de volta (a mesma coisa que o depurador faz com uma thread).
///
/// Uma falha no codigo (ponteiro invalido, divisao por zero, pilha
/// estourada) nao derruba o simulador: o tratador da ponte devolve a thread
/// ao salto e a placa para com a falha dita em portugues.
/// </summary>
public sealed unsafe class SketchNativo
{
    [StructLayout(LayoutKind.Sequential)]
    struct Val
    {
        public int Tipo;
        public int N;
        public long I;
        public double D;
        public IntPtr S;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Ponte
    {
        public int Versao;
        public int Tamanho;
        public IntPtr Ctx;
        public IntPtr Chamar;
        public IntPtr Micros;
    }

    const int Nada = 0, Int = 1, Real = 2, Texto = 3, Bool = 4, Char = 5, Nome = 6;
    const int Ok = 0, Sair = 1, Falhou = 3;

    /// <summary>Quem atende a chamada: o interpretador da placa (alvo, nome, argumentos, quais sao nomes).</summary>
    public delegate Valor Atendente(string alvo, string nome, List<Valor> args, List<bool> nomes);

    readonly Maquina maq;
    readonly Atendente atender;
    readonly Action atenderInterrupcoes;
    readonly Func<bool> temInterrupcoes;

    IntPtr modulo;
    string copia;
    GCHandle alca;
    Ponte* ponte;
    IntPtr veh;
    IntPtr salto;
    IntPtr memoriaDoSalto;
    static int limpou;
    ulong inicio, fim;

    delegate* unmanaged<int, int, int> entrar;
    delegate* unmanaged<byte*, int, uint> descreverFalha;
    delegate* unmanaged<void> refazerGuarda;
    delegate* unmanaged<uint, int*, void*> saltoDaThread;
    delegate* unmanaged<byte*, uint*, void*> secao;

    // O estado da thread do sketch: 0 fora do C, 1 rodando o sketch, 2 dentro
    // do simulador (numa chamada da ponte), 3 suspensa pelo vigia.
    int estado;
    int idDoSketch = -1;
    uint tidDoSketch;
    volatile bool encerrando;
    Thread vigia;

    /// <summary>A excecao do simulador que fez o C sair (relancada na thread que chamou).</summary>
    [ThreadStatic] static Exception guardada;

    /// <summary>O que a rotina de interrupcao (na thread do vigia) deixou para o sketch relancar.</summary>
    volatile Exception daRotina;

    [ThreadStatic] static IntPtr textoDaVolta;
    [ThreadStatic] static int capacidadeDoTexto;

    public SketchNativo(Maquina maquina, Atendente atendente, Action atenderInterrupcoes, Func<bool> temInterrupcoes)
    {
        maq = maquina;
        atender = atendente;
        this.atenderInterrupcoes = atenderInterrupcoes;
        this.temInterrupcoes = temInterrupcoes;
    }

    // =====================================================================
    //  carregar
    // =====================================================================

    /// <summary>
    /// Carrega a copia da DLL desta placa, liga a ponte e roda os
    /// construtores das globais (que ja podem chamar o simulador).
    /// </summary>
    public void Carregar(SketchCompilado compilado, ReinicioDaPlaca origem = null)
    {
        string pasta = Path.Combine(Path.GetTempPath(), "circuitsketch-sketch");
        Directory.CreateDirectory(pasta);
        // As copias que sobraram de rodadas que acabaram com a DLL ainda
        // carregada (a de agora, presa, nao apaga; as outras, sim).
        if (Interlocked.Exchange(ref limpou, 1) == 0)
            foreach (string velha in Directory.GetFiles(pasta, "*.dll"))
                try { File.Delete(velha); } catch { }
        copia =Path.Combine(pasta, $"{Environment.ProcessId}-{Guid.NewGuid():N}.dll");
        File.Copy(compilado.Dll, copia, true);
        modulo = NativeLibrary.Load(copia);

        IntPtr Exigir(string nome) => NativeLibrary.TryGetExport(modulo, nome, out IntPtr p) ? p
            : throw new ErroDeExecucao($"o sketch compilado nao tem '{nome}' (o core nativo e de outra versao).", 0);

        var iniciar = (delegate* unmanaged<Ponte*, int>)Exigir("cs_iniciar");
        entrar = (delegate* unmanaged<int, int, int>)Exigir("cs_entrar");
        descreverFalha = (delegate* unmanaged<byte*, int, uint>)Exigir("cs_descrever_falha");
        refazerGuarda = (delegate* unmanaged<void>)Exigir("cs_refazer_guarda");
        saltoDaThread = (delegate* unmanaged<uint, int*, void*>)Exigir("cs_salto_da_thread");
        secao = (delegate* unmanaged<byte*, uint*, void*>)Exigir("cs_secao");
        var medir = (delegate* unmanaged<ulong*, ulong>)Exigir("cs_modulo");
        // O salto copiado para memoria alocada aqui (ver cs_codigo_do_salto).
        int tamanhoDoSalto;
        byte* codigoDoSalto = (byte*)((delegate* unmanaged<int*, void*>)Exigir("cs_codigo_do_salto"))(&tamanhoDoSalto);
        memoriaDoSalto = Win32.VirtualAlloc(IntPtr.Zero, 4096, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
        Buffer.MemoryCopy(codigoDoSalto, (void*)memoriaDoSalto, 4096, tamanhoDoSalto);
        Win32.VirtualProtect(memoriaDoSalto, 4096, Win32.PAGE_EXECUTE_READ, out _);
        salto = memoriaDoSalto;
        ((delegate* unmanaged<void*, void>)Exigir("cs_usar_salto"))((void*)salto);

        alca = GCHandle.Alloc(this);
        ponte = (Ponte*)NativeMemory.AllocZeroed((nuint)sizeof(Ponte));
        ponte->Versao = 1;
        ponte->Tamanho = sizeof(Ponte);
        ponte->Ctx = GCHandle.ToIntPtr(alca);
        ponte->Chamar = (IntPtr)(delegate* unmanaged<IntPtr, byte*, byte*, int, Val*, Val*, int>)&Chamar;
        ponte->Micros = (IntPtr)(delegate* unmanaged<IntPtr, long>)&Micros;
        if (iniciar(ponte) != 0) throw new ErroDeExecucao("o sketch compilado e de outra versao da ponte.", 0);
        ulong f;
        inicio = medir(&f);
        fim = f;

        veh = Win32.AddVectoredExceptionHandler(1, Exigir("cs_excecao"));
        if (Depurando)
        {
            Console.Error.WriteLine($"[nativo] modulo {inicio:X}..{fim:X} salto {salto:X}");
            Win32.AddVectoredExceptionHandler(0, (IntPtr)(delegate* unmanaged<IntPtr, int>)&MostrarExcecao);
        }
        Win32.RegistrarAlvoDeExcecao(salto, true);
        Win32.timeBeginPeriod(1);

        // O relogio e o real: o sketch roda na velocidade do PC, e o tempo
        // so pode andar com o relogio de parede. Nos testes ele era virtual.
        if (!maq.TempoReal)
        {
            maq.TempoReal = true;
            maq.IniciarRelogio();
        }

        if (Depurando) Console.Error.WriteLine($"[nativo] thread do sketch {Win32.GetCurrentThreadId()}");
        // A memoria RTC: a RTC_DATA_ATTR volta so do sono profundo; a
        // RTC_NOINIT_ATTR, de qualquer reinicio. Antes dos construtores, como
        // na placa (ela nao e inicializada de novo).
        if (origem is not null)
        {
            Restaurar(".rtcnoi", maq.RtcSemIniciarDoSketch);
            if (origem.Razao == ReinicioDaPlaca.PeloSonoProfundo) Restaurar(".rtcdat", maq.RtcDoSketch);
        }

        vigia = new Thread(Vigiar) { IsBackground = true, Name = "vigia do sketch nativo", Priority = ThreadPriority.AboveNormal };
        vigia.Start();

        Rodar(3, 0);      // os construtores das globais
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool GetModuleHandleExW(uint f, IntPtr end, out IntPtr h);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern int GetModuleFileNameW(IntPtr h, char* buf, int n);
    static readonly bool SemVigia = Environment.GetEnvironmentVariable("CS_SEM_VIGIA") == "1";
    static readonly bool Depurando = Environment.GetEnvironmentVariable("CS_DEPURAR_NATIVO") == "1";

    [UnmanagedCallersOnly]
    static int MostrarExcecao(IntPtr ponteiros)
    {
        byte* registro = ((byte**)ponteiros)[0];
        byte* contexto = ((byte**)ponteiros)[1];
        uint codigo = *(uint*)registro;
        if (codigo is not (0x406D1388 or 0xE0434352))
            Console.Error.WriteLine($"[nativo] excecao {codigo:X8} em {*(ulong*)(contexto + 0xF8):X} (registro {*(ulong*)(registro + 0x10):X}) thread {Win32.GetCurrentThreadId()}");
        if (codigo == 0x80000003)
        {
            byte* rip = (byte*)*(ulong*)(contexto + 0xF8);
            ulong* rsp = (ulong*)*(ulong*)(contexto + 0x98);
            Console.Error.WriteLine($"[nativo]   bytes {rip[-2]:X2} {rip[-1]:X2} [{rip[0]:X2}] {rip[1]:X2} {rip[2]:X2}  rsp {(ulong)rsp:X}: {rsp[0]:X} {rsp[1]:X} {rsp[2]:X} {rsp[3]:X} {rsp[4]:X} {rsp[5]:X}");
            byte* b0 = (byte*)(((ulong)rip & ~0xFFFUL));
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < 0x80; k++) sb.Append(b0[k].ToString("X2"));
            Console.Error.WriteLine($"[nativo]   pagina {(ulong)b0:X}: {sb}");
            var nome = new char[600];
            fixed (char* pn = nome)
            {
                IntPtr h;
                if (GetModuleHandleExW(6, (IntPtr)rip, out h))
                {
                    int n = GetModuleFileNameW(h, pn, 600);
                    Console.Error.WriteLine($"[nativo]   modulo {h:X} {new string(pn, 0, n)}");
                }
            }
        }
        return 0;
    }

    // =====================================================================
    //  rodar
    // =====================================================================

    public void Setup() => Rodar(0, 0);
    public void Loop() => Rodar(1, 0);

    /// <summary>Uma rotina de interrupcao ("__nativa_3"), na thread de quem chamou.</summary>
    public void Rotina(int indice) => Rodar(2, indice);

    void Rodar(int qual, int arg)
    {
        bool doSketch = qual != 2 || Environment.CurrentManagedThreadId == idDoSketch;
        if (qual != 2)
        {
            idDoSketch = Environment.CurrentManagedThreadId;
            tidDoSketch = Win32.GetCurrentThreadId();
        }
        int antes = doSketch ? Interlocked.Exchange(ref estado, 1) : 0;
        int r;
        try
        {
            if (Depurando) Console.Error.WriteLine($"[nativo] entrar {qual} {arg}");
            r = entrar(qual, arg);
            if (Depurando) Console.Error.WriteLine($"[nativo] saiu {qual} {arg} = {r}");
        }
        finally
        {
            if (doSketch) Volatile.Write(ref estado, antes);
        }
        if (r == Ok) return;

        if (r == Sair)
        {
            var e = guardada ?? daRotina ?? (Exception)maq.MotivoDaParada();
            if (e is ReinicioDaPlaca)
            {
                maq.RtcDoSketch = Guardar(".rtcdat");
                maq.RtcSemIniciarDoSketch = Guardar(".rtcnoi");
            }
            guardada = null;
            if (e == daRotina) daRotina = null;
            ExceptionDispatchInfo.Capture(e).Throw();
        }

        byte* buf = stackalloc byte[300];
        uint codigo = descreverFalha(buf, 300);
        string texto = Marshal.PtrToStringUTF8((IntPtr)buf) ?? "falha";
        if (codigo == 0xC00000FD) refazerGuarda();
        string onde = qual switch { 0 => "no setup()", 1 => "no loop()", 2 => "na rotina de interrupcao", _ => "nas globais" };
        throw new ErroDeExecucao($"o programa travou {onde}: {texto}.", 0);
    }

    byte* Secao(string nome, out uint tamanho)
    {
        uint t = 0;
        void* p;
        fixed (byte* n = Encoding.ASCII.GetBytes(nome + "\0")) p = secao(n, &t);
        tamanho = t;
        return (byte*)p;
    }

    byte[] Guardar(string nome)
    {
        byte* p = Secao(nome, out uint t);
        return p == null || t == 0 ? null : new ReadOnlySpan<byte>(p, (int)t).ToArray();
    }

    void Restaurar(string nome, byte[] bytes)
    {
        if (bytes is null) return;
        byte* p = Secao(nome, out uint t);
        if (p == null || t == 0) return;
        bytes.AsSpan(0, Math.Min(bytes.Length, (int)t)).CopyTo(new Span<byte>(p, (int)t));
    }

    // =====================================================================
    //  a ponte (o C chamando o simulador)
    // =====================================================================

    [UnmanagedCallersOnly]
    static int Chamar(IntPtr ctx, byte* alvo, byte* nome, int n, Val* args, Val* volta)
    {
        try
        {
            var eu = (SketchNativo)GCHandle.FromIntPtr(ctx).Target!;
            return eu.Atender(alvo, nome, n, args, volta);
        }
        catch (Exception e)
        {
            guardada = e;
            return Sair;
        }
    }

    [UnmanagedCallersOnly]
    static long Micros(IntPtr ctx)
    {
        try
        {
            var eu = (SketchNativo)GCHandle.FromIntPtr(ctx).Target!;
            return eu.Relogio();
        }
        catch
        {
            return -1;
        }
    }

    long Relogio()
    {
        bool doSketch = Environment.CurrentManagedThreadId == idDoSketch;
        int antes = doSketch ? Entrar() : 0;
        try
        {
            if (maq.Parar || encerrando) return -1;
            return (long)maq.AgoraUs();
        }
        finally
        {
            if (doSketch) Volatile.Write(ref estado, antes);
        }
    }

    /// <summary>A thread do sketch entrou no simulador (o vigia nao a suspende mais); devolve o estado de antes.</summary>
    int Entrar()
    {
        while (true)
        {
            int e = Volatile.Read(ref estado);
            if (e == 3) { Thread.SpinWait(20); continue; }
            if (Interlocked.CompareExchange(ref estado, 2, e) == e) return e;
        }
    }

    int Atender(byte* alvo, byte* nome, int n, Val* args, Val* volta)
    {
        bool doSketch = Environment.CurrentManagedThreadId == idDoSketch;
        int antes = doSketch ? Entrar() : 0;
        try
        {
            if (daRotina is not null && doSketch)
            {
                guardada = daRotina;
                daRotina = null;
                return Sair;
            }

            string sAlvo = alvo == null ? null : Marshal.PtrToStringUTF8((IntPtr)alvo);
            string sNome = Marshal.PtrToStringUTF8((IntPtr)nome) ?? "";
            var lista = new List<Valor>(n);
            var nomes = new List<bool>(n);
            for (int i = 0; i < n; i++)
            {
                var a = args[i];
                nomes.Add(a.Tipo == Nome);
                lista.Add(a.Tipo switch
                {
                    Int => Valor.DeInt(a.I),
                    Real => Valor.DeReal(a.D),
                    Texto or Nome => Valor.DeTexto(a.S == IntPtr.Zero ? "" : Encoding.UTF8.GetString((byte*)a.S, Math.Max(0, a.N))),
                    Bool => Valor.DeBool(a.I != 0),
                    Char => Valor.DeCaractere(a.I),
                    _ => Valor.Vazio,
                });
            }

            var v = atender(sAlvo, sNome, lista, nomes);
            Escrever(v, volta);
            return Ok;
        }
        catch (Exception e)
        {
            guardada = e;
            return Sair;
        }
        finally
        {
            if (doSketch) Volatile.Write(ref estado, antes);
        }
    }

    static void Escrever(Valor v, Val* volta)
    {
        volta->S = IntPtr.Zero;
        volta->N = 0;
        if (v.EhTexto)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(v.Texto ?? "");
            if (capacidadeDoTexto < bytes.Length + 1)
            {
                if (textoDaVolta != IntPtr.Zero) NativeMemory.Free((void*)textoDaVolta);
                capacidadeDoTexto = Math.Max(256, bytes.Length + 1);
                textoDaVolta = (IntPtr)NativeMemory.Alloc((nuint)capacidadeDoTexto);
            }
            Marshal.Copy(bytes, 0, textoDaVolta, bytes.Length);
            ((byte*)textoDaVolta)[bytes.Length] = 0;
            volta->Tipo = Texto;
            volta->S = textoDaVolta;
            volta->N = bytes.Length;
            return;
        }
        if (v.Referencia is not null && v.Referencia is not NadaPy)
        {
            volta->Tipo = Nada;
            return;
        }
        if (v.Caractere) { volta->Tipo = Char; volta->I = (long)v.Num; return; }
        if (v.EhBooleano) { volta->Tipo = Bool; volta->I = v.Num != 0 ? 1 : 0; return; }
        if (v.Inteiro) { volta->Tipo = Int; volta->I = (long)v.Num; volta->D = v.Num; return; }
        volta->Tipo = Real;
        volta->D = v.Num;
        volta->I = (long)v.Num;
    }

    // =====================================================================
    //  o vigia
    // =====================================================================

    void Vigiar()
    {
        while (!encerrando)
        {
            Thread.Sleep(1);
            if (encerrando || Volatile.Read(ref estado) != 1) continue;
            if (SemVigia) continue;

            if (maq.Parar)
            {
                Interromper(forcarSaida: true);
                continue;
            }
            if (temInterrupcoes()) Interromper(forcarSaida: false);
        }
    }

    /// <summary>
    /// Suspende a thread do sketch -- so se ela estiver no codigo do sketch,
    /// e nao no .NET -- e atende as interrupcoes, ou a desvia para a saida.
    /// </summary>
    void Interromper(bool forcarSaida)
    {
        if (Interlocked.CompareExchange(ref estado, 3, 1) != 1) return;
        IntPtr h = Win32.OpenThread(Win32.THREAD_ALL_ACCESS, false, tidDoSketch);
        if (h == IntPtr.Zero) { Volatile.Write(ref estado, 1); return; }
        byte* bruto = (byte*)NativeMemory.AlignedAlloc(1232 + 64, 64);
        bool suspensa = false;
        try
        {
            if (Win32.SuspendThread(h) == uint.MaxValue) return;
            suspensa = true;
            new Span<byte>(bruto, 1232).Clear();
            *(uint*)(bruto + 0x30) = 0x10000B;   // CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_FLOATING_POINT
            if (!Win32.GetThreadContext(h, bruto)) return;
            ulong rip = *(ulong*)(bruto + 0xF8);

            // Fora do codigo do sketch (na libc do Windows, ou de volta no
            // .NET): fica para a proxima volta do vigia.
            if (rip < inicio || rip >= fim) return;

            if (forcarSaida)
            {
                int rodando;
                ulong* buffer = (ulong*)saltoDaThread(tidDoSketch, &rodando);
                if (buffer == null || rodando == 0) return;
                *(ulong*)(bruto + 0x98) = (buffer[8] - 64) & ~15UL;   // Rsp
                *(ulong*)(bruto + 0x80) = (ulong)buffer;              // Rcx
                *(ulong*)(bruto + 0x88) = Sair;                       // Rdx
                *(ulong*)(bruto + 0xF8) = (ulong)salto;               // Rip
                Win32.SetThreadContext(h, bruto);
                return;
            }

            // A interrupcao: a rotina roda aqui, com o sketch parado onde
            // estava. Se ela falhar, o sketch sai com o erro dela.
            try { atenderInterrupcoes(); }
            catch (Exception e)
            {
                daRotina = e;
                int rodando;
                ulong* buffer = (ulong*)saltoDaThread(tidDoSketch, &rodando);
                if (buffer != null && rodando != 0)
                {
                    *(ulong*)(bruto + 0x98) = (buffer[8] - 64) & ~15UL;
                    *(ulong*)(bruto + 0x80) = (ulong)buffer;
                    *(ulong*)(bruto + 0x88) = Sair;
                    *(ulong*)(bruto + 0xF8) = (ulong)salto;
                    Win32.SetThreadContext(h, bruto);
                }
            }
        }
        finally
        {
            if (suspensa) Win32.ResumeThread(h);
            Volatile.Write(ref estado, 1);
            NativeMemory.AlignedFree(bruto);
            Win32.CloseHandle(h);
        }
    }

    // =====================================================================
    //  encerrar
    // =====================================================================

    public void Encerrar()
    {
        if (encerrando) return;
        encerrando = true;
        vigia?.Join(500);

        // Alguem ainda dentro do C (a thread da placa presa num laco): a
        // memoria fica, para nao soltar o codigo debaixo dela.
        if (Volatile.Read(ref estado) != 0)
        {
            maq.Avisar("modo nativo: o sketch nao parou; a memoria dele fica presa ate fechar o simulador.");
            return;
        }
        if (veh != IntPtr.Zero) { Win32.RemoveVectoredExceptionHandler(veh); veh = IntPtr.Zero; }
        if (salto != IntPtr.Zero) Win32.RegistrarAlvoDeExcecao(salto, false);
        if (memoriaDoSalto != IntPtr.Zero) { Win32.VirtualFree(memoriaDoSalto, 0, Win32.MEM_RELEASE); memoriaDoSalto = IntPtr.Zero; }
        Win32.timeEndPeriod(1);
        try { if (modulo != IntPtr.Zero) NativeLibrary.Free(modulo); } catch { }
        modulo = IntPtr.Zero;
        if (ponte != null) { NativeMemory.Free(ponte); ponte = null; }
        if (alca.IsAllocated) alca.Free();
        try { if (copia is not null) File.Delete(copia); } catch { }
    }
}
