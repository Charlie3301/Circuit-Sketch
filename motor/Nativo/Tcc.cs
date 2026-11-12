using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Simulador;

/// <summary>
/// O TinyCC (libtcc.dll, LGPL): o compilador de C que roda dentro do
/// simulador. Carregado a mao, pelo caminho, e chamado por ponteiros de
/// funcao -- nada de DllImport, que dependeria do resolvedor de DLLs do
/// assembly inteiro.
/// </summary>
static unsafe class Tcc
{
    static IntPtr dll;
    static readonly object trava = new();

    public static delegate* unmanaged<IntPtr> Novo;
    public static delegate* unmanaged<IntPtr, void> Apagar;
    public static delegate* unmanaged<IntPtr, byte*, void> CaminhoDaLib;
    public static delegate* unmanaged<IntPtr, IntPtr, delegate* unmanaged<IntPtr, byte*, void>, void> FuncaoDeErro;
    public static delegate* unmanaged<IntPtr, byte*, int> Opcoes;
    public static delegate* unmanaged<IntPtr, byte*, int> Inclusao;
    public static delegate* unmanaged<IntPtr, byte*, int> InclusaoDeSistema;
    public static delegate* unmanaged<IntPtr, byte*, byte*, void> Definir;
    public static delegate* unmanaged<IntPtr, byte*, void> Desdefinir;
    public static delegate* unmanaged<IntPtr, byte*, int> AdicionarArquivo;
    public static delegate* unmanaged<IntPtr, byte*, int> CompilarTexto;
    public static delegate* unmanaged<IntPtr, int, int> TipoDeSaida;
    public static delegate* unmanaged<IntPtr, byte*, int> CaminhoDeBibliotecas;
    public static delegate* unmanaged<IntPtr, byte*, int> AdicionarBiblioteca;
    public static delegate* unmanaged<IntPtr, byte*, void*, int> AdicionarSimbolo;
    public static delegate* unmanaged<IntPtr, void*, int> Relocar;
    public static delegate* unmanaged<IntPtr, byte*, void*> Simbolo;

    // O TinyCC 0.9.27 guarda parte do estado do compilador em variaveis
    // globais da DLL (a profundidade dos blocos, a pilha de valores...). Um
    // erro no meio de uma funcao sai por longjmp e deixa lixo nelas, e a
    // compilacao seguinte da erros que nao existem ("sizeof applied to an
    // incomplete type" num arquivo que compila sozinho). O remedio e
    // recarregar a DLL depois de um erro -- quando nenhum estado dela estiver
    // vivo, porque o programa que roda ainda pergunta os simbolos ao dele.
    static int vivos;
    static bool sujo;

    /// <summary>Um TCCState novo (tcc_new).</summary>
    public static void Nasceu() => Interlocked.Increment(ref vivos);

    /// <summary>Um TCCState a menos (tcc_delete).</summary>
    public static void Morreu() => Interlocked.Decrement(ref vivos);

    /// <summary>A compilacao deu erro: a DLL volta limpa na proxima carga.</summary>
    public static void Sujou() => sujo = true;

    /// <summary>Carrega a DLL da pasta do runtime. Falso, e o motivo, se nao deu.</summary>
    public static bool Carregar(string pasta, out string erro)
    {
        lock (trava)
        {
            erro = null;
            if (dll != IntPtr.Zero && sujo && Volatile.Read(ref vivos) == 0)
            {
                NativeLibrary.Free(dll);
                dll = IntPtr.Zero;
                sujo = false;
            }
            if (dll != IntPtr.Zero) return true;
            string caminho = Path.Combine(pasta, "tcc", "libtcc.dll");
            if (!File.Exists(caminho))
            {
                erro = Idioma.T("o compilador de C (libtcc.dll) nao esta em {0}", caminho);
                return false;
            }
            try { dll = NativeLibrary.Load(caminho); }
            catch (Exception e)
            {
                erro = Idioma.T("nao deu para carregar o compilador de C: {0}", e.Message);
                return false;
            }
            TrocarSetjmp(dll);

            Novo = (delegate* unmanaged<IntPtr>)F("tcc_new");
            Apagar = (delegate* unmanaged<IntPtr, void>)F("tcc_delete");
            CaminhoDaLib = (delegate* unmanaged<IntPtr, byte*, void>)F("tcc_set_lib_path");
            FuncaoDeErro = (delegate* unmanaged<IntPtr, IntPtr, delegate* unmanaged<IntPtr, byte*, void>, void>)F("tcc_set_error_func");
            Opcoes = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_set_options");
            Inclusao = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_add_include_path");
            InclusaoDeSistema = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_add_sysinclude_path");
            Definir = (delegate* unmanaged<IntPtr, byte*, byte*, void>)F("tcc_define_symbol");
            Desdefinir = (delegate* unmanaged<IntPtr, byte*, void>)F("tcc_undefine_symbol");
            AdicionarArquivo = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_add_file");
            CompilarTexto = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_compile_string");
            TipoDeSaida = (delegate* unmanaged<IntPtr, int, int>)F("tcc_set_output_type");
            CaminhoDeBibliotecas = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_add_library_path");
            AdicionarBiblioteca = (delegate* unmanaged<IntPtr, byte*, int>)F("tcc_add_library");
            AdicionarSimbolo = (delegate* unmanaged<IntPtr, byte*, void*, int>)F("tcc_add_symbol");
            Relocar = (delegate* unmanaged<IntPtr, void*, int>)F("tcc_relocate");
            Simbolo = (delegate* unmanaged<IntPtr, byte*, void*>)F("tcc_get_symbol");
            return true;
        }
    }

    static void* F(string nome) => (void*)NativeLibrary.GetExport(dll, nome);

    /// <summary>
    /// O TinyCC se recupera de um erro de compilacao com setjmp/longjmp da
    /// msvcrt. O longjmp dele zera so a metade de baixo do campo Frame do
    /// buffer -- o que basta quando a pilha fica abaixo de 4 GB, como no
    /// tcc.exe. Num processo .NET as pilhas moram mais alto: o Frame sobra, a
    /// msvcrt tenta desenrolar a pilha ate a origem e o processo estoura (ou
    /// trava). A troca: o _setjmp que ele importa passa a ser este, que guarda
    /// os mesmos registradores no mesmo lugar com o Frame inteiro em zero, e o
    /// longjmp que ele chama, um que so restaura os registradores e salta.
    /// </summary>
    static IntPtr paginaDoSalto;

    static void TrocarSetjmp(IntPtr modulo)
    {
        byte[] codigo = Convert.FromHexString(
            "48C70100000000" + "48895908" + "488D442408" + "48894110" + "48896918" + "48897120" + "48897928"
            + "4C896130" + "4C896938" + "4C897140" + "4C897948" + "488B0424" + "48894150" + "0FAE5958" + "D9795C"
            + "F30F7F7160" + "F30F7F7970" + "F3440F7F8180000000" + "F3440F7F8990000000" + "F3440F7F91A0000000"
            + "F3440F7F99B0000000" + "F3440F7FA1C0000000" + "F3440F7FA9D0000000" + "F3440F7FB1E0000000"
            + "F3440F7FB9F0000000" + "31C0" + "C3");
        // E o longjmp: o TinyCC chama o da msvcrt por um ponteiro guardado na
        // propria DLL, e o da msvcrt faz verificacoes que derrubam o processo
        // quando o alvo e codigo de uma DLL sem as tabelas novas. Este so
        // restaura os registradores do mesmo buffer e salta.
        byte[] salto = Convert.FromHexString(
            "89D0" + "85C0" + "7502" + "FFC0" + "488B5908" + "488B6918" + "488B7120" + "488B7928" + "4C8B6130"
            + "4C8B6938" + "4C8B7140" + "4C8B7948" + "0FAE5158" + "DBE2" + "D9695C" + "F30F6F7160" + "F30F6F7970"
            + "F3440F6F8180000000" + "F3440F6F8990000000" + "F3440F6F91A0000000" + "F3440F6F99B0000000"
            + "F3440F6FA1C0000000" + "F3440F6FA9D0000000" + "F3440F6FB1E0000000" + "F3440F6FB9F0000000"
            + "488B6110" + "FF6150");

        // Uma pagina so, para todas as cargas da DLL: o codigo nao muda.
        if (paginaDoSalto == IntPtr.Zero)
        {
            IntPtr nova = Win32.VirtualAlloc(IntPtr.Zero, 4096, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
            if (nova == IntPtr.Zero) return;
            Marshal.Copy(codigo, 0, nova, codigo.Length);
            Marshal.Copy(salto, 0, nova + 256, salto.Length);
            Win32.VirtualProtect(nova, 4096, Win32.PAGE_EXECUTE_READ, out _);
            paginaDoSalto = nova;
        }
        IntPtr pagina = paginaDoSalto;

        byte* b = (byte*)modulo;
        byte* opcional = b + *(int*)(b + 0x3C) + 24;
        if (*(ushort*)opcional != 0x20B) return;          // PE32+

        // O embrulho do longjmp no TinyCC: "xor eax,eax; mov [rcx],eax;
        // lea rax,[rip+d]; jmp [rax]" -- o ponteiro fica em rip+d.
        uint tamanhoDaImagem = *(uint*)(opcional + 56);
        for (uint i = 0; i + 13 <= tamanhoDaImagem; i++)
        {
            byte* p = b + i;
            if (p[0] != 0x31 || p[1] != 0xC0 || p[2] != 0x89 || p[3] != 0x01 || p[4] != 0x48 || p[5] != 0x8D
                || p[6] != 0x05 || p[11] != 0xFF || p[12] != 0x20) continue;
            ulong* ponteiro = (ulong*)(p + 11 + *(int*)(p + 7));
            Win32.VirtualProtect((IntPtr)ponteiro, 8, Win32.PAGE_READWRITE, out uint antes);
            *ponteiro = (ulong)(pagina + 256);
            Win32.VirtualProtect((IntPtr)ponteiro, 8, antes, out _);
            break;
        }
        uint importacoes = *(uint*)(opcional + 112 + 8);  // DataDirectory[1]
        if (importacoes == 0) return;
        for (byte* d = b + importacoes; *(uint*)(d + 12) != 0; d += 20)
        {
            string nomeDaDll = Marshal.PtrToStringAnsi((IntPtr)(b + *(uint*)(d + 12))) ?? "";
            if (!nomeDaDll.Equals("msvcrt.dll", StringComparison.OrdinalIgnoreCase)) continue;
            uint original = *(uint*)d, primeiro = *(uint*)(d + 16);
            ulong* nomes = (ulong*)(b + (original != 0 ? original : primeiro));
            ulong* tabela = (ulong*)(b + primeiro);
            for (int i = 0; nomes[i] != 0; i++)
            {
                if ((nomes[i] & 0x8000000000000000UL) != 0) continue;
                string nome = Marshal.PtrToStringAnsi((IntPtr)(b + (uint)nomes[i] + 2));

                // O longjmp tambem: a DLL feita com o mingw-w64 de hoje (a do
                // .rodata, ver LICENCAS.txt) chama o da msvcrt direto, sem o
                // embrulho que a busca acima procura.
                ulong novo = nome switch
                {
                    "_setjmp" => (ulong)pagina,
                    "longjmp" => (ulong)(pagina + 256),
                    _ => 0,
                };
                if (novo == 0) continue;
                Win32.VirtualProtect((IntPtr)(&tabela[i]), 8, Win32.PAGE_READWRITE, out uint antes);
                tabela[i] = novo;
                Win32.VirtualProtect((IntPtr)(&tabela[i]), 8, antes, out _);
            }
        }
    }

    /// <summary>Texto em UTF-8 terminado em zero, para passar ao C.</summary>
    public static byte[] Z(string s) => Encoding.UTF8.GetBytes((s ?? "") + "\0");

    /// <summary>
    /// Um caminho do jeito que o TinyCC consegue abrir (sem o zero no fim). Ele
    /// abre os arquivos pelo open() da msvcrt, que le o nome na pagina de
    /// codigo ANSI do Windows (a 1252 num Windows em portugues): em UTF-8,
    /// "C:\Users\João" nao abriria. Se o nome tiver letra que a pagina nao
    /// tem, vai o nome curto (8.3) do Windows. Em <paramref name="comoVolta"/>,
    /// o caminho como ele aparece nas mensagens do compilador.
    /// </summary>
    public static byte[] Caminho(string caminho, out string comoVolta)
    {
        comoVolta = caminho;
        if (caminho.All(c => c < 0x80)) return Encoding.ASCII.GetBytes(caminho);
        if (Win32.GetACP() == 65001) return Encoding.UTF8.GetBytes(caminho);
        byte[] ansi = Win32.Ansi(caminho);
        if (ansi is not null) return ansi;
        string curto = Win32.NomeCurto(caminho);
        if (curto is not null && curto.All(c => c < 0x80))
        {
            comoVolta = curto;
            return Encoding.ASCII.GetBytes(curto);
        }
        return Encoding.UTF8.GetBytes(caminho);   // nao abre, e o erro diz qual arquivo
    }

    /// <summary>Um caminho para o TinyCC, terminado em zero.</summary>
    public static byte[] CaminhoZ(string caminho) => [.. Caminho(caminho, out _), 0];

    /// <summary>O texto de uma mensagem do TinyCC (na pagina de codigo ANSI, como os caminhos).</summary>
    public static string Mensagem(byte* texto) =>
        Win32.GetACP() == 65001 ? Marshal.PtrToStringUTF8((IntPtr)texto) ?? "" : Marshal.PtrToStringAnsi((IntPtr)texto) ?? "";
}

/// <summary>O que o modo nativo pede ao Windows.</summary>
static unsafe partial class Win32
{
    [LibraryImport("kernel32.dll")] public static partial uint TlsAlloc();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool TlsSetValue(uint indice, IntPtr valor);
    [LibraryImport("kernel32.dll")] public static partial IntPtr VirtualAlloc(IntPtr endereco, nuint tamanho, uint tipo, uint protecao);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool VirtualFree(IntPtr endereco, nuint tamanho, uint tipo);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool VirtualProtect(IntPtr endereco, nuint tamanho, uint protecao, out uint antes);
    [LibraryImport("kernel32.dll")] public static partial IntPtr AddVectoredExceptionHandler(uint primeiro, IntPtr tratador);
    [LibraryImport("kernel32.dll")] public static partial uint RemoveVectoredExceptionHandler(IntPtr alca);
    [LibraryImport("kernel32.dll")] public static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll")] public static partial IntPtr OpenThread(uint acesso, [MarshalAs(UnmanagedType.Bool)] bool herdar, uint id);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseHandle(IntPtr alca);
    [LibraryImport("kernel32.dll")] public static partial uint SuspendThread(IntPtr thread);
    [LibraryImport("kernel32.dll")] public static partial uint ResumeThread(IntPtr thread);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetThreadContext(IntPtr thread, void* contexto);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetThreadContext(IntPtr thread, void* contexto);
    [LibraryImport("winmm.dll")] public static partial uint timeBeginPeriod(uint ms);
    [LibraryImport("winmm.dll")] public static partial uint timeEndPeriod(uint ms);
    [LibraryImport("ntdll.dll")] [return: MarshalAs(UnmanagedType.U1)] public static partial bool RtlDeleteFunctionTable(IntPtr tabela);

    [LibraryImport("kernel32.dll")] public static partial IntPtr GetCurrentProcess();
    [LibraryImport("kernel32.dll")] public static partial uint GetLastError();
    [LibraryImport("kernel32.dll")] public static partial uint GetACP();
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WideCharToMultiByte(uint pagina, uint marcas, string texto, int n, byte* saida, int tamanho, IntPtr padrao, int* usouPadrao);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetShortPathNameW(string longo, char* curto, uint tamanho);

    /// <summary>O texto na pagina de codigo ANSI; nulo se alguma letra nao existe nela.</summary>
    public static byte[] Ansi(string texto)
    {
        const uint WC_NO_BEST_FIT_CHARS = 0x400;
        int usouPadrao = 0;
        int n = WideCharToMultiByte(0, WC_NO_BEST_FIT_CHARS, texto, texto.Length, null, 0, IntPtr.Zero, &usouPadrao);
        if (n <= 0 || usouPadrao != 0) return null;
        var saida = new byte[n];
        fixed (byte* p = saida)
            if (WideCharToMultiByte(0, WC_NO_BEST_FIT_CHARS, texto, texto.Length, p, n, IntPtr.Zero, &usouPadrao) != n || usouPadrao != 0)
                return null;
        return saida;
    }

    /// <summary>O nome curto (8.3) de um caminho que existe; nulo se o Windows nao tiver.</summary>
    public static string NomeCurto(string caminho)
    {
        char* curto = stackalloc char[1024];
        uint n = GetShortPathNameW(caminho, curto, 1024);
        return n is > 0 and < 1024 ? new string(curto, 0, (int)n) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AlvoDeContinuacao
    {
        public ulong Endereco;
        public ulong Marcas;
    }

    static delegate* unmanaged<IntPtr, ushort, AlvoDeContinuacao*, int> registrarAlvos;

    /// <summary>
    /// Com a pilha-sombra do processador (CET) ligada -- o .NET liga, e o
    /// Intel de 11a geracao em diante tem --, o Windows so deixa uma excecao
    /// continuar num endereco de codigo gerado em tempo de execucao se ele
    /// estiver registrado como alvo. E o que os compiladores JIT fazem; aqui
    /// o alvo e o salto de volta das tarefas (__cs_longjmp).
    /// </summary>
    public static void RegistrarAlvoDeExcecao(IntPtr endereco, bool registrar)
    {
        if (registrarAlvos == null)
        {
            if (kernel32 == IntPtr.Zero) kernel32 = NativeLibrary.Load("kernel32.dll");
            if (!NativeLibrary.TryGetExport(kernel32, "SetProcessDynamicEHContinuationTargets", out IntPtr f)) return;
            registrarAlvos = (delegate* unmanaged<IntPtr, ushort, AlvoDeContinuacao*, int>)f;
        }
        var alvo = new AlvoDeContinuacao { Endereco = (ulong)endereco, Marcas = registrar ? 1UL : 0UL };
        int ok = registrarAlvos(GetCurrentProcess(), 1, &alvo);
        if (Environment.GetEnvironmentVariable("CS_DEPURAR_VEH") == "1")
            Console.Error.WriteLine($"[veh] alvo {endereco:X} registrar={registrar} ok={ok} erro={GetLastError()} marcas={alvo.Marcas} sombra={PilhaSombraLigada()}");
    }

    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessMitigationPolicy(IntPtr processo, int politica, void* dados, nuint tamanho);

    /// <summary>A pilha-sombra (CET) esta ligada neste processo?</summary>
    public static string PilhaSombraLigada()
    {
        uint bits = 0;
        return GetProcessMitigationPolicy(GetCurrentProcess(), 15, &bits, 4) ? $"0x{bits:X}" : "?";
    }

    public const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000;
    public const uint PAGE_READWRITE = 0x04, PAGE_EXECUTE_READ = 0x20, PAGE_EXECUTE_READWRITE = 0x40;
    public const uint THREAD_ALL_ACCESS = 0x1FFFFF;

    static IntPtr kernel32;

    /// <summary>O TlsGetValue do Windows, para o C chamar direto -- sem passar pelo .NET.</summary>
    public static IntPtr TlsGetValue()
    {
        if (kernel32 == IntPtr.Zero) kernel32 = NativeLibrary.Load("kernel32.dll");
        return NativeLibrary.GetExport(kernel32, "TlsGetValue");
    }
}
