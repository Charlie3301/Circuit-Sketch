using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Simulador;

/// <summary>
/// O depurador do C/C++ compilado (o lado do simulador; o int3 e a parada
/// sao do cs_depurador.cpp).
///
/// O PDB do sketch (o clang faz com -gcodeview, o link com -g) e lido pelo
/// DbgHelp do proprio Windows: o comeco de cada linha do sketch.ino vai para a
/// DLL (onde por o int3), e na parada o contexto da thread diz a linha, a
/// pilha e as variaveis -- as locais e os parametros do quadro, e as globais
/// do sketch, cada uma com o tipo dela (numero, char, bool, ponteiro, vetor,
/// struct, String). A janela recebe o mesmo Retrato do depurador do
/// interpretador.
/// </summary>
public sealed unsafe class DepuracaoNativa
{
    readonly Depurador depurador;
    readonly ulong baseDoModulo;
    readonly string sketch;
    readonly IntPtr modulo;
    GCHandle alca;

    delegate* unmanaged<int*, int, void> definirPausas;
    delegate* unmanaged<void> pedirPausa;
    delegate* unmanaged<void> desligar;
    delegate* unmanaged<void> seguir;
    delegate* unmanaged<void*> contextoDaPonte;

    /// <summary>As linhas do sketch que tem codigo (o PDB).</summary>
    readonly HashSet<int> linhasComCodigo = [];

    const string ArquivoDoSketch = "sketch.ino";

    DepuracaoNativa(Depurador depurador, IntPtr modulo, ulong baseDoModulo, string sketch)
    {
        this.depurador = depurador;
        this.modulo = modulo;
        this.baseDoModulo = baseDoModulo;
        this.sketch = sketch ?? "";
    }

    /// <summary>Liga o depurador na DLL carregada (null: sem PDB, ou a DLL e de antes).</summary>
    public static DepuracaoNativa Ligar(Depurador depurador, IntPtr modulo, string dllCopiada, string dllDoCache, string sketch)
    {
        if (depurador is null || modulo == IntPtr.Zero) return null;
        if (!NativeLibrary.TryGetExport(modulo, "cs_dep_ligar", out IntPtr pLigar)) return null;
        ulong baseDoModulo;
        lock (Dbg.Trava)
        {
            if (!Dbg.Iniciar()) return null;
            Dbg.SymSetSearchPathW(Dbg.Processo, Path.GetDirectoryName(dllDoCache));
            baseDoModulo = Dbg.SymLoadModuleExW(Dbg.Processo, IntPtr.Zero, dllCopiada, null, (ulong)modulo, 0, IntPtr.Zero, 0);
            if (baseDoModulo == 0) baseDoModulo = (ulong)modulo;
        }
        var d = new DepuracaoNativa(depurador, modulo, baseDoModulo, sketch);
        if (!d.LerLinhas(out var inicios, out var linhas) || inicios.Count == 0) return null;

        d.definirPausas = (delegate* unmanaged<int*, int, void>)NativeLibrary.GetExport(modulo, "cs_dep_pausas");
        d.pedirPausa = (delegate* unmanaged<void>)NativeLibrary.GetExport(modulo, "cs_dep_pedir_pausa");
        d.desligar = (delegate* unmanaged<void>)NativeLibrary.GetExport(modulo, "cs_dep_desligar");
        d.seguir = (delegate* unmanaged<void>)NativeLibrary.GetExport(modulo, "cs_dep_seguir");
        d.contextoDaPonte = (delegate* unmanaged<void*>)NativeLibrary.GetExport(modulo, "cs_dep_contexto_da_ponte");
        var trechos = (delegate* unmanaged<ulong*, int*, int, void>)NativeLibrary.GetExport(modulo, "cs_dep_trechos");
        fixed (ulong* pi = inicios.ToArray())
        fixed (int* pl = linhas.ToArray())
            trechos(pi, pl, inicios.Count);

        d.alca = GCHandle.Alloc(d);
        ((delegate* unmanaged<IntPtr, IntPtr, void>)pLigar)((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, int>)&Parou, GCHandle.ToIntPtr(d.alca));
        depurador.Nativa = d;
        d.DefinirPausas(depurador.LinhasMarcadas);
        return d;
    }

    /// <summary>O comeco de cada pedaco de linha do sketch.ino, em ordem de endereco.</summary>
    bool LerLinhas(out List<ulong> inicios, out List<int> linhas)
    {
        var pares = new List<(ulong Endereco, int Linha)>();
        lock (Dbg.Trava)
        {
            GCHandle lista = GCHandle.Alloc(pares);
            try
            {
                Dbg.SymEnumLinesW(Dbg.Processo, baseDoModulo, null, null,
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&UmaLinha, GCHandle.ToIntPtr(lista));
            }
            finally { lista.Free(); }
        }
        pares.Sort((a, b) => a.Endereco.CompareTo(b.Endereco));
        inicios = [.. pares.Select(p => p.Endereco)];
        linhas = [.. pares.Select(p => p.Linha)];
        foreach (var (_, l) in pares) linhasComCodigo.Add(l);
        return true;
    }

    [UnmanagedCallersOnly]
    static int UmaLinha(IntPtr info, IntPtr ctx)
    {
        // SRCCODEINFOW: o FileName em 546 (WCHAR[261]), a linha em 1068, o endereco em 1072.
        string arquivo = new string((char*)(info + 546));
        if (!arquivo.EndsWith(ArquivoDoSketch, StringComparison.OrdinalIgnoreCase)) return 1;
        int linha = *(int*)(info + 1068);
        ulong endereco = *(ulong*)(info + 1072);
        ((List<(ulong, int)>)GCHandle.FromIntPtr(ctx).Target!).Add((endereco, linha));
        return 1;
    }

    // =====================================================================
    //  os comandos da janela
    // =====================================================================

    public void DefinirPausas(IReadOnlyCollection<int> linhas)
    {
        var validas = linhas.Where(linhasComCodigo.Contains).ToArray();
        fixed (int* p = validas) definirPausas(p, validas.Length);
    }

    public void PedirPausa() => pedirPausa();

    /// <summary>Desfaz o pedido de pausa (o Continuar da pausa feita no delay).</summary>
    public void Seguir() => seguir();

    public void Desligar()
    {
        try { desligar(); } catch { }
        lock (Dbg.Trava)
            if (Dbg.Iniciado) Dbg.SymUnloadModule64(Dbg.Processo, baseDoModulo);
        depurador.Nativa = null;
        if (alca.IsAllocated) alca.Free();
    }

    /// <summary>A parada no int3 (na thread que parou, fora do despacho da excecao): 0 continuar, 1 passo, 2 sair.</summary>
    [UnmanagedCallersOnly]
    static int Parou(IntPtr ctx, IntPtr contexto, int linha)
    {
        try
        {
            var eu = (DepuracaoNativa)GCHandle.FromIntPtr(ctx).Target!;
            var variaveis = eu.Variaveis((byte*)contexto, subirAteOSketch: false, out _);
            return (int)eu.depurador.EntrarNaPausaNativa(linha, variaveis);
        }
        catch { return 0; }
    }

    /// <summary>
    /// O Pausar no meio de um delay (na thread do sketch, dentro do simulador):
    /// a linha e as variaveis vem do contexto guardado na entrada da ponte,
    /// subindo a pilha ate o quadro do sketch.
    /// </summary>
    public Depurador.Comando? PausarNaEspera()
    {
        byte* contexto = (byte*)contextoDaPonte();
        var variaveis = Variaveis(contexto, subirAteOSketch: true, out int linha);
        if (linha <= 0) return null;
        var comando = depurador.EntrarNaPausaNativa(linha, variaveis);
        if (comando == Depurador.Comando.Passo) pedirPausa();
        return comando;
    }

    // =====================================================================
    //  as variaveis
    // =====================================================================

    const int Rip = 0xF8, Rsp = 0x98, Rbp = 0xA0;

    List<Ambiente.VariavelVista> Variaveis(byte* contextoOriginal, bool subirAteOSketch, out int linha)
    {
        var vistas = new List<Ambiente.VariavelVista>();
        linha = 0;
        lock (Dbg.Trava)
        {
            // Uma copia: o StackWalk64 escreve nela a cada quadro.
            byte* contexto = (byte*)NativeMemory.AlignedAlloc(1232, 16);
            byte* quadro = stackalloc byte[512];
            try
            {
                Buffer.MemoryCopy(contextoOriginal, contexto, 1232, 1232);
                new Span<byte>(quadro, 512).Clear();
                *(ulong*)(quadro + 0) = *(ulong*)(contexto + Rip);
                *(int*)(quadro + 12) = 3;
                *(ulong*)(quadro + 32) = *(ulong*)(contexto + Rbp);
                *(int*)(quadro + 44) = 3;
                *(ulong*)(quadro + 48) = *(ulong*)(contexto + Rsp);
                *(int*)(quadro + 60) = 3;

                // O quadro do sketch: o primeiro (a parada no int3) ou o primeiro
                // da pilha que tem linha no sketch.ino (a pausa no delay).
                bool achou = false;
                for (int n = 0; n < 64; n++)
                {
                    if (!Dbg.StackWalk64(0x8664, Dbg.Processo, Dbg.Thread, quadro, contexto, IntPtr.Zero,
                                         Dbg.AcessoATabela, Dbg.BaseDoModulo, IntPtr.Zero))
                        break;
                    ulong pc = *(ulong*)quadro;
                    int l = LinhaDoSketch(n == 0 ? pc : pc - 1);
                    if (l > 0) { linha = l; achou = true; break; }
                    if (!subirAteOSketch) break;
                }
                if (!achou) return vistas;

                ulong pcDoQuadro = *(ulong*)quadro;
                var imagem = stackalloc byte[128];
                new Span<byte>(imagem, 128).Clear();
                *(ulong*)(imagem + 0) = pcDoQuadro;                       // InstructionOffset
                *(ulong*)(imagem + 16) = *(ulong*)(quadro + 32);           // FrameOffset
                *(ulong*)(imagem + 24) = *(ulong*)(quadro + 48);           // StackOffset
                Dbg.SymSetContext(Dbg.Processo, imagem, IntPtr.Zero);

                var coleta = new Coleta(this, contexto, *(ulong*)(quadro + 32));
                var alcaDaColeta = GCHandle.Alloc(coleta);
                try
                {
                    // As locais e os parametros do quadro (base 0: o contexto do SymSetContext).
                    Dbg.SymEnumSymbolsW(Dbg.Processo, 0, null, (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, int>)&UmSimbolo,
                                        GCHandle.ToIntPtr(alcaDaColeta));
                    vistas.AddRange(coleta.Vistas);

                    // As globais que o sketch declara (cada nome procurado no modulo).
                    coleta.Vistas.Clear();
                    coleta.Globais = true;
                    foreach (string nome in CompiladorNativo.NomesDasGlobais(sketch))
                        if (!vistas.Any(v => v.Nome == nome))
                            Dbg.SymEnumSymbolsW(Dbg.Processo, baseDoModulo, nome, (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, int>)&UmSimbolo,
                                                GCHandle.ToIntPtr(alcaDaColeta));
                    foreach (var v in coleta.Vistas)
                        if (!vistas.Any(x => x.Nome == v.Nome)) vistas.Add(v);
                }
                finally { alcaDaColeta.Free(); }
            }
            finally { NativeMemory.AlignedFree(contexto); }
        }
        return vistas;
    }

    sealed class Coleta(DepuracaoNativa dono, byte* contexto, ulong baseDoQuadro)
    {
        public readonly List<Ambiente.VariavelVista> Vistas = [];
        public DepuracaoNativa Dono => dono;
        public byte* Contexto => contexto;
        public ulong BaseDoQuadro => baseDoQuadro;
        public bool Globais;
    }

    const uint SymflagRegRel = 0x10, SymflagFrameRel = 0x20, SymflagParameter = 0x40, SymflagLocal = 0x80;

    [UnmanagedCallersOnly]
    static int UmSimbolo(IntPtr info, uint tamanho, IntPtr ctx)
    {
        try
        {
            var c = (Coleta)GCHandle.FromIntPtr(ctx).Target!;
            byte* p = (byte*)info;
            uint flags = *(uint*)(p + 40);
            bool local = (flags & (SymflagLocal | SymflagParameter)) != 0;
            if (local == c.Globais || *(uint*)(p + 72) != Dbg.SymTagData) return 1;
            int n = (int)*(uint*)(p + 76);
            string nome = new string((char*)(p + 84), 0, Math.Max(0, n));
            int fim = nome.IndexOf((char)0);
            if (fim >= 0) nome = nome[..fim];
            if (nome.Length == 0 || nome.StartsWith("__", StringComparison.Ordinal)) return 1;
            long deslocamento = (long)*(ulong*)(p + 56);
            ulong endereco;
            if ((flags & SymflagRegRel) != 0) endereco = (ulong)((long)Registrador(c.Contexto, *(uint*)(p + 64)) + deslocamento);
            else if ((flags & SymflagFrameRel) != 0) endereco = (ulong)((long)c.BaseDoQuadro + deslocamento);
            else endereco = *(ulong*)(p + 56);
            uint tipo = *(uint*)(p + 4);
            c.Vistas.Add(new Ambiente.VariavelVista(nome, c.Dono.Formatar(tipo, endereco, 0), c.Globais));
        }
        catch { }
        return 1;
    }

    /// <summary>O valor do registrador do CodeView (CV_AMD64_*) no contexto.</summary>
    static ulong Registrador(byte* contexto, uint cv) => cv switch
    {
        328 => *(ulong*)(contexto + 0x78),   // RAX
        329 => *(ulong*)(contexto + 0x90),   // RBX
        330 => *(ulong*)(contexto + 0x80),   // RCX
        331 => *(ulong*)(contexto + 0x88),   // RDX
        332 => *(ulong*)(contexto + 0xA8),   // RSI
        333 => *(ulong*)(contexto + 0xB0),   // RDI
        334 => *(ulong*)(contexto + Rbp),
        335 => *(ulong*)(contexto + Rsp),
        _ => *(ulong*)(contexto + Rsp),
    };

    int LinhaDoSketch(ulong pc)
    {
        var linha = stackalloc byte[48];
        new Span<byte>(linha, 48).Clear();
        *(uint*)linha = 40;
        uint deslocamento;
        if (!Dbg.SymGetLineFromAddrW64(Dbg.Processo, pc, &deslocamento, linha)) return 0;
        string arquivo = new((char*)*(IntPtr*)(linha + 24));
        return arquivo.EndsWith(ArquivoDoSketch, StringComparison.OrdinalIgnoreCase) ? *(int*)(linha + 16) : 0;
    }

    // =====================================================================
    //  o valor, pelo tipo
    // =====================================================================

    static bool Ler(ulong endereco, void* destino, int n)
        => Dbg.ReadProcessMemory(Dbg.Processo, (IntPtr)endereco, destino, (IntPtr)n, out _) && n > 0;

    string Formatar(uint tipo, ulong endereco, int fundo)
    {
        if (fundo > 2) return "…";
        uint marca = Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetSymtag);
        ulong tamanho = Dbg.Info<ulong>(baseDoModulo, tipo, Dbg.TiGetLength);
        switch (marca)
        {
            case Dbg.SymTagTypedef:
                return Formatar(Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetType), endereco, fundo);

            case Dbg.SymTagBaseType:
            {
                uint basico = Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetBasetype);
                long bruto = 0;
                if (tamanho is 0 or > 8 || !Ler(endereco, &bruto, (int)tamanho)) return "?";
                return basico switch
                {
                    8 => tamanho == 4 ? BitConverter.Int32BitsToSingle((int)bruto).ToString("G7", CultureInfo.InvariantCulture)
                                      : BitConverter.Int64BitsToDouble(bruto).ToString("G15", CultureInfo.InvariantCulture),
                    10 => bruto != 0 ? "true" : "false",
                    2 when tamanho == 1 => Caractere((byte)bruto),
                    7 or 14 or 31 => Sinal(bruto, (int)tamanho, false).ToString(CultureInfo.InvariantCulture),
                    _ => Sinal(bruto, (int)tamanho, true).ToString(CultureInfo.InvariantCulture),
                };
            }

            case Dbg.SymTagEnum:
            {
                int v = 0;
                return Ler(endereco, &v, 4) ? v.ToString(CultureInfo.InvariantCulture) : "?";
            }

            case Dbg.SymTagPointerType:
            {
                ulong alvo = 0;
                if (!Ler(endereco, &alvo, 8)) return "?";
                if (alvo == 0) return "nullptr";
                uint apontado = Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetType);
                if (EhChar(apontado)) return Texto(alvo, 80);
                return "0x" + alvo.ToString("X");
            }

            case Dbg.SymTagArrayType:
            {
                uint elemento = Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetType);
                uint quantos = Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetCount);
                ulong tamanhoDoElemento = Dbg.Info<ulong>(baseDoModulo, elemento, Dbg.TiGetLength);
                if (EhChar(elemento)) return Texto(endereco, (int)Math.Min(quantos, 80));
                var partes = new List<string>();
                for (uint i = 0; i < Math.Min(quantos, 12u); i++) partes.Add(Formatar(elemento, endereco + i * tamanhoDoElemento, fundo + 1));
                return "{" + string.Join(", ", partes) + (quantos > 12 ? ", …" : "") + "}";
            }

            case Dbg.SymTagUDT:
            {
                string nomeDoTipo = Dbg.Nome(baseDoModulo, tipo);
                var campos = Dbg.Campos(baseDoModulo, tipo);
                // O String do Arduino: o texto que ele guarda.
                if (nomeDoTipo == "String" && campos.FirstOrDefault(c => c.Nome == "buffer") is { Nome: not null } buffer)
                {
                    ulong ptr = 0;
                    return Ler(endereco + buffer.Deslocamento, &ptr, 8) && ptr != 0 ? Texto(ptr, 120) : "\"\"";
                }
                var partes = campos.Take(8).Select(c => $"{c.Nome}: {Formatar(c.Tipo, endereco + c.Deslocamento, fundo + 1)}");
                return "{" + string.Join(", ", partes) + (campos.Count > 8 ? ", …" : "") + "}";
            }
        }
        return nomeDoTipoOuInterrogacao(tipo);

        string nomeDoTipoOuInterrogacao(uint t) => Dbg.Nome(baseDoModulo, t) is { Length: > 0 } n ? $"({n})" : "?";
    }

    bool EhChar(uint tipo)
    {
        while (Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetSymtag) == Dbg.SymTagTypedef) tipo = Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetType);
        return Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetSymtag) == Dbg.SymTagBaseType
               && Dbg.Info<uint>(baseDoModulo, tipo, Dbg.TiGetBasetype) is 2 or 7 && Dbg.Info<ulong>(baseDoModulo, tipo, Dbg.TiGetLength) == 1;
    }

    static long Sinal(long bruto, int tamanho, bool comSinal) => tamanho switch
    {
        1 => comSinal ? (sbyte)bruto : (byte)bruto,
        2 => comSinal ? (short)bruto : (ushort)bruto,
        4 => comSinal ? (int)bruto : (uint)bruto,
        _ => bruto,
    };

    static string Caractere(byte b) => b is >= 32 and < 127 ? $"'{(char)b}' ({b})" : b.ToString(CultureInfo.InvariantCulture);

    static string Texto(ulong endereco, int maximo)
    {
        var bytes = new byte[maximo];
        int n = 0;
        fixed (byte* p = bytes)
            for (; n < maximo; n++)
                if (!Ler(endereco + (ulong)n, p + n, 1) || p[n] == 0) break;
        string t = Encoding.UTF8.GetString(bytes, 0, n);
        return "\"" + t.Replace("\n", "\\n").Replace("\r", "\\r") + (n == maximo ? "…" : "") + "\"";
    }

    /// <summary>O DbgHelp: um por processo, e uma chamada de cada vez.</summary>
    static class Dbg
    {
        public static readonly object Trava = new();
        public static readonly IntPtr Processo = (IntPtr)(-1);
        public static readonly IntPtr Thread = (IntPtr)(-2);
        public static bool Iniciado;
        public static IntPtr AcessoATabela, BaseDoModulo;

        public const uint SymTagData = 7, SymTagUDT = 11, SymTagEnum = 12, SymTagPointerType = 14, SymTagArrayType = 15,
                          SymTagBaseType = 16, SymTagTypedef = 17;
        public const int TiGetSymtag = 0, TiGetSymname = 1, TiGetLength = 2, TiGetType = 3, TiGetBasetype = 5,
                         TiFindChildren = 7, TiGetDatakind = 8, TiGetOffset = 10, TiGetCount = 12, TiGetChildrenCount = 13;

        public static bool Iniciar()
        {
            if (Iniciado) return true;
            SymSetOptions(0x2 | 0x10 | 0x200);   // UNDNAME, LOAD_LINES, FAIL_CRITICAL_ERRORS
            if (!SymInitializeW(Processo, null, false)) return false;
            var dbghelp = NativeLibrary.Load("dbghelp.dll");
            AcessoATabela = NativeLibrary.GetExport(dbghelp, "SymFunctionTableAccess64");
            BaseDoModulo = NativeLibrary.GetExport(dbghelp, "SymGetModuleBase64");
            return Iniciado = true;
        }

        public static string NomeDoModulo(ulong baseDoModulo)
        {
            var info = stackalloc byte[3136];
            new Span<byte>(info, 3136).Clear();
            *(uint*)info = 3136;
            if (!SymGetModuleInfoW64(Processo, baseDoModulo, info)) return "";
            return new string((char*)(info + 0x2C));   // IMAGEHLP_MODULEW64.ModuleName
        }

        public static T Info<T>(ulong baseDoModulo, uint tipo, int pedido) where T : unmanaged
        {
            T v = default;
            SymGetTypeInfo(Processo, baseDoModulo, tipo, pedido, &v);
            return v;
        }

        public static string Nome(ulong baseDoModulo, uint tipo)
        {
            IntPtr p = IntPtr.Zero;
            if (!SymGetTypeInfo(Processo, baseDoModulo, tipo, TiGetSymname, &p) || p == IntPtr.Zero) return "";
            string nome = Marshal.PtrToStringUni(p) ?? "";
            Marshal.FreeHGlobal(p);
            return nome;
        }

        public static List<(string Nome, uint Tipo, ulong Deslocamento)> Campos(ulong baseDoModulo, uint tipo)
        {
            var campos = new List<(string, uint, ulong)>();
            uint quantos = Info<uint>(baseDoModulo, tipo, TiGetChildrenCount);
            if (quantos == 0 || quantos > 4096) return campos;
            var pedido = (uint*)NativeMemory.AllocZeroed((nuint)(8 + 4 * quantos));
            try
            {
                pedido[0] = quantos;
                if (!SymGetTypeInfo(Processo, baseDoModulo, tipo, TiFindChildren, pedido)) return campos;
                for (uint i = 0; i < quantos; i++)
                {
                    uint filho = pedido[2 + i];
                    if (Info<uint>(baseDoModulo, filho, TiGetSymtag) != SymTagData) continue;
                    if (Info<uint>(baseDoModulo, filho, TiGetDatakind) != 7) continue;     // DataIsMember
                    campos.Add((Nome(baseDoModulo, filho), Info<uint>(baseDoModulo, filho, TiGetType), Info<uint>(baseDoModulo, filho, TiGetOffset)));
                }
            }
            finally { NativeMemory.Free(pedido); }
            return campos;
        }

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)] public static extern bool SymInitializeW(IntPtr p, string caminho, bool invadir);
        [DllImport("dbghelp.dll")] public static extern uint SymSetOptions(uint opcoes);
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)] public static extern bool SymSetSearchPathW(IntPtr p, string caminho);
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
        public static extern ulong SymLoadModuleExW(IntPtr p, IntPtr arquivo, string imagem, string modulo, ulong baseDaDll, uint tamanho, IntPtr dados, uint flags);
        [DllImport("dbghelp.dll")] public static extern bool SymUnloadModule64(IntPtr p, ulong baseDaDll);
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
        public static extern bool SymEnumLinesW(IntPtr p, ulong baseDaDll, string obj, string arquivo, IntPtr cb, IntPtr ctx);
        [DllImport("dbghelp.dll")] public static extern bool SymSetContext(IntPtr p, void* quadro, IntPtr ctx);
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
        public static extern bool SymEnumSymbolsW(IntPtr p, ulong baseDaDll, string mascara, IntPtr cb, IntPtr ctx);
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)] public static extern bool SymFromNameW(IntPtr p, string nome, void* info);
        [DllImport("dbghelp.dll")] public static extern bool SymGetTypeInfo(IntPtr p, ulong baseDaDll, uint tipo, int pedido, void* saida);
        [DllImport("dbghelp.dll")] public static extern bool SymGetLineFromAddrW64(IntPtr p, ulong endereco, uint* deslocamento, void* linha);
        [DllImport("dbghelp.dll")] public static extern bool SymGetModuleInfoW64(IntPtr p, ulong endereco, void* info);
        [DllImport("dbghelp.dll")]
        public static extern bool StackWalk64(uint maquina, IntPtr p, IntPtr t, void* quadro, void* contexto, IntPtr ler, IntPtr tabela, IntPtr baseDoModulo, IntPtr traduzir);
        [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr p, IntPtr endereco, void* destino, IntPtr n, out IntPtr lidos);
    }
}
