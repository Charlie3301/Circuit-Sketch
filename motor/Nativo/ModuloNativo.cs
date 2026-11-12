using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Simulador;

/// <summary>
/// O codigo C da placa, compilado de verdade e rodando nativo.
///
/// Os .c do projeto e das bibliotecas (o DOOM inteiro, por exemplo) sao
/// compilados pelo TinyCC junto com o runtime do simulador (nativo/runtime):
/// uma libc e um ESP-IDF que, em vez de mexer em registrador, chamam o
/// simulador por uma tabela de funcoes (o cs_host). O .ino continua no
/// interpretador e chama as funcoes do C pelo nome.
///
/// Cada tarefa do FreeRTOS e uma thread do PC. Parar a placa acende o
/// cs_parando: as esperas voltam, as tarefas saltam para o comeco delas e
/// terminam. So depois que todas terminaram a memoria do codigo e solta --
/// uma thread que nao sai (um laco sem chamada nenhuma) e desviada a forca.
///
/// Uma falha no C (ponteiro invalido, pilha estourada) nao derruba o
/// programa: o tratador de excecoes do runtime devolve a tarefa ao comeco,
/// e o monitor serial mostra o "Guru Meditation" que a placa mostraria.
/// </summary>
public sealed unsafe partial class ModuloNativo
{
    /// <summary>A pasta do runtime (a "nativo": tcc, include, runtime). Quem roda define.</summary>
    public static string Pasta { get; set; }

    static uint indiceTls = uint.MaxValue;
    static readonly object travaGlobal = new();

    [ThreadStatic] static ModuloNativo atual;
    [ThreadStatic] static ContextoDaThread contexto;

    public Maquina Maquina { get; }

    /// <summary>O que o compilador disse (avisos), para quem quiser mostrar.</summary>
    public IReadOnlyList<string> Avisos => avisos;

    readonly List<string> avisos = [];
    IntPtr estado;
    IntPtr codigo;
    nuint tamanhoDoCodigo;
    IntPtr regiaoInterna, regiaoPsram;
    nuint tamanhoInterna, tamanhoPsram;
    IntPtr tabela;
    IntPtr tratadorDeExcecoes;
    IntPtr saltoDeVolta;
    int* parando;
    string pastaTemporaria;

    delegate* unmanaged<IntPtr, void> rodarTarefa;
    delegate* unmanaged<IntPtr, IntPtr, ulong*, int, double*, ulong*, int> chamar;
    delegate* unmanaged<byte*, int, int, IntPtr> tarefaDoSimulador;
    delegate* unmanaged<IntPtr, byte*> nomeDaTarefa;

    readonly Dictionary<IntPtr, ContextoDaThread> contextos = [];
    readonly object travaDasThreads = new();
    ContextoDaThread doSketch;
    volatile bool encerrando;
    int encerrado;
    int dentro;
    readonly int razaoDoReinicio;

    /// <summary>A placa esta parando (ou ja parou): as esperas voltam na hora.</summary>
    public bool Encerrando => encerrando;

    ModuloNativo(Maquina maquina, int razao)
    {
        Maquina = maquina;
        razaoDoReinicio = razao;
    }

    /// <summary>A thread que roda codigo do C: a tarefa dela e como acorda-la.</summary>
    internal sealed class ContextoDaThread
    {
        public ModuloNativo Modulo;
        public IntPtr Tarefa;
        public string Nome = "";
        public Thread Thread;
        public uint IdNativo;
        public volatile bool Encerrar;
        public readonly AutoResetEvent Acordar = new(false);
        public volatile bool Viva;
    }

    // =====================================================================
    //  compilar e carregar
    // =====================================================================

    /// <summary>
    /// Compila os .c do programa com o runtime e deixa tudo pronto para
    /// rodar. Erro de compilacao vira ErroDeSintaxe com arquivo e linha.
    /// </summary>
    public static ModuloNativo Carregar(Maquina maquina, Programa programa, int razaoDoReinicio = 1)
    {
        string pasta = Pasta ?? AcharPasta()
            ?? throw new ErroDeExecucao(Idioma.T("o modo nativo (os arquivos .c) precisa da pasta \"nativo\" do simulador, e ela nao foi encontrada."), 0);
        if (!Tcc.Carregar(pasta, out string erro)) throw new ErroDeExecucao(erro, 0);

        var modulo = new ModuloNativo(maquina, razaoDoReinicio) { tabelaDeParticoes = programa.TabelaDeParticoes };
        try
        {
            // O TinyCC gasta muita pilha num arquivo grande (o DOOM tem um de
            // 5 mil linhas): compila numa thread com pilha folgada, e nao na
            // da janela, que tem 1 MB.
            Exception falha = null;
            var compilador = new Thread(() =>
            {
                try { modulo.Compilar(pasta, programa); }
                catch (Exception e) { falha = e; }
            }, 256 << 20) { IsBackground = true, Name = "TinyCC" };
            compilador.Start();
            compilador.Join();
            if (falha is not null) throw falha;
            modulo.Iniciar();
        }
        catch
        {
            modulo.Liberar();
            Tcc.Sujou();
            throw;
        }
        return modulo;
    }

    /// <summary>A pasta "nativo" ao lado do programa, ou subindo a partir dele (a do codigo-fonte).</summary>
    static string AcharPasta()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            string candidata = Path.Combine(dir, "nativo");
            if (File.Exists(Path.Combine(candidata, "tcc", "libtcc.dll"))) return candidata;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    [UnmanagedCallersOnly]
    static void ErroDoTcc(IntPtr opaco, byte* mensagem)
    {
        try
        {
            string texto = Tcc.Mensagem(mensagem);
            if (Environment.GetEnvironmentVariable("CS_DEPURAR_NATIVO") == "1") Console.Error.WriteLine("[tcc] diz: " + texto);
            if (GCHandle.FromIntPtr(opaco).Target is List<string> lista)
                lock (lista) lista.Add(texto);
        }
        catch { }
    }

    void Compilar(string pasta, Programa programa)
    {
        var mensagens = new List<string>();
        var alcaDasMensagens = GCHandle.Alloc(mensagens);
        try
        {
            estado = Tcc.Novo();
            if (estado == IntPtr.Zero) throw new ErroDeExecucao(Idioma.T("o compilador de C nao abriu."), 0);
            Tcc.Nasceu();
            Tcc.FuncaoDeErro(estado, GCHandle.ToIntPtr(alcaDasMensagens), &ErroDoTcc);

            string pastaTcc = Path.Combine(pasta, "tcc");
            fixed (byte* p = Tcc.CaminhoZ(pastaTcc)) Tcc.CaminhoDaLib(estado, p);
            Tcc.TipoDeSaida(estado, 1);   // TCC_OUTPUT_MEMORY
            // -fno-common: o padrao do GCC da ESP32 (e de todo GCC desde o 10) -- "int x;"
            // em dois .c e erro la tambem --, e o que poe as globais sem valor
            // no .bss na ordem, para as marcas medirem a RAM do projeto.
            fixed (byte* p = Tcc.Z("-nostdinc -nostdlib -funsigned-char -fno-common")) Tcc.Opcoes(estado, p);

            foreach (var (nome, valor) in Macros(programa.Placa))
                fixed (byte* n = Tcc.Z(nome), v = Tcc.Z(valor)) Tcc.Definir(estado, n, v);

            Mostrar(pasta, "nativo");
            fixed (byte* p = Tcc.CaminhoZ(Path.Combine(pasta, "include"))) Tcc.InclusaoDeSistema(estado, p);
            // O que so existe num chip ("esp32s3/rom/..."), como as pastas
            // esp_rom/<chip>/include do ESP-IDF.
            fixed (byte* p = Tcc.CaminhoZ(Path.Combine(pasta, "include", "chip", Chip(programa.Placa)))) Tcc.InclusaoDeSistema(estado, p);

            // As abas do projeto viram arquivos numa pasta temporaria: o
            // #include "config.h" de um .c precisa achar o .h no disco.
            var doProjeto = programa.Nativos.Where(f => f.Caminho is null).ToList();
            if (doProjeto.Count > 0)
            {
                pastaTemporaria = Path.Combine(Path.GetTempPath(), "circuitsketch-nativo",
                                               $"{Environment.ProcessId}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(pastaTemporaria);
                foreach (var f in doProjeto)
                {
                    string destino = Path.Combine(pastaTemporaria, f.Nome.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                    File.WriteAllText(destino, f.Texto ?? "", new UTF8Encoding(false));
                }
                Mostrar(pastaTemporaria, "");
                fixed (byte* p = Tcc.CaminhoZ(pastaTemporaria)) Tcc.Inclusao(estado, p);
            }
            foreach (string inclusao in programa.Nativos.Select(f => f.Inclusao).Where(i => i is not null).Distinct())
                fixed (byte* p = Tcc.CaminhoZ(inclusao)) Tcc.Inclusao(estado, p);

            // O runtime primeiro, depois o que e do projeto.
            var arquivos = Directory.GetFiles(Path.Combine(pasta, "runtime"), "*.c").OrderBy(a => a, StringComparer.Ordinal).ToList();
            int doRuntime = arquivos.Count;
            foreach (var f in programa.Nativos)
            {
                if (!f.Nome.EndsWith(".c", StringComparison.OrdinalIgnoreCase)) continue;
                arquivos.Add(f.Caminho ?? Path.Combine(pastaTemporaria, f.Nome.Replace('/', Path.DirectorySeparatorChar)));
            }

            bool depurar = Environment.GetEnvironmentVariable("CS_DEPURAR_NATIVO") == "1";
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            for (int indice = 0; indice < arquivos.Count; indice++)
            {
                string arquivo = arquivos[indice];
                if (indice == doRuntime) CompilarMarca("runtime", mensagens);
                long antes = relogio.ElapsedMilliseconds;
                // Cada .c entra por um #include num texto que antes tira o
                // _WIN32: o -U do TinyCC so vale no primeiro arquivo (no fim
                // de cada um ele devolve as macros que ele mesmo define), e o
                // codigo de uma biblioteca nao pode pegar os caminhos do Windows.
                byte[] caminho = Tcc.Caminho(arquivo, out _);
                for (int i = 0; i < caminho.Length; i++) if (caminho[i] == (byte)'\\') caminho[i] = (byte)'/';
                byte[] texto = [.. "#undef _WIN32\n#undef _WIN64\n#include \""u8, .. caminho, .. "\"\n\0"u8];
                int r;
                fixed (byte* p = texto) r = Tcc.CompilarTexto(estado, p);
                if (depurar) Console.Error.WriteLine($"[tcc] {relogio.ElapsedMilliseconds - antes,6} ms  {Path.GetFileName(arquivo)}");
                if (r < 0) throw ErroDeCompilacao(mensagens);
            }
            if (doRuntime == arquivos.Count) CompilarMarca("runtime", mensagens);
            CompilarMarca("fim", mensagens);

            fixed (byte* p = Tcc.CaminhoZ(Path.Combine(pastaTcc, "lib"))) Tcc.CaminhoDeBibliotecas(estado, p);
            fixed (byte* p = Tcc.Z("tcc1-64")) Tcc.AdicionarBiblioteca(estado, p);

            int tamanho = Tcc.Relocar(estado, null);
            if (tamanho <= 0) throw ErroDeCompilacao(mensagens);
            tamanhoDoCodigo = (nuint)tamanho;
            codigo = Win32.VirtualAlloc(IntPtr.Zero, tamanhoDoCodigo, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
            if (codigo == IntPtr.Zero) throw new ErroDeExecucao(Idioma.T("sem memoria para o codigo nativo."), 0);
            if (Tcc.Relocar(estado, (void*)codigo) < 0) throw ErroDeCompilacao(mensagens);

            lock (mensagens)
                foreach (string m in mensagens)
                    avisos.Add(Limpar(m));
            MedirEstatica();
        }
        finally
        {
            if (estado != IntPtr.Zero) Tcc.FuncaoDeErro(estado, IntPtr.Zero, null);
            alcaDasMensagens.Free();
        }
    }

    /// <summary>
    /// As marcas que medem a RAM estatica (.data e .bss) do que e do projeto:
    /// uma depois do runtime, outra no fim. O runtime faz o papel do ESP-IDF e
    /// do core -- esses ja estao no "sketch vazio" da placa.
    /// </summary>
    void CompilarMarca(string nome, List<string> mensagens)
    {
        string c = $"static char cs__d_{nome} = 1; static char cs__b_{nome};\n"
                 + $"void *cs_marca_dados_{nome} = &cs__d_{nome}; void *cs_marca_bss_{nome} = &cs__b_{nome};\n";
        byte[] texto = Tcc.Z(c);
        int r;
        fixed (byte* p = texto) r = Tcc.CompilarTexto(estado, p);
        if (r < 0) throw ErroDeCompilacao(mensagens);
    }

    /// <summary>
    /// Quanto as globais dos .c do projeto ocupam (os bytes do PC: um
    /// ponteiro aqui tem 8, e na placa 4 -- a conta erra um pouco para mais).
    /// So o que mora na RAM: a nossa libtcc.dll poe os const, os textos entre
    /// aspas e as constantes de ponto flutuante na .rodata (ver
    /// nativo/LICENCAS.txt), que na placa e a flash. Zero se as marcas nao deram.
    /// </summary>
    internal long EstaticaDoProjeto;

    void MedirEstatica()
    {
        long Ler(string nome)
        {
            IntPtr p = Simbolo(nome);
            return p == IntPtr.Zero ? 0 : (long)*(IntPtr*)p;
        }
        long d0 = Ler("cs_marca_dados_runtime"), d1 = Ler("cs_marca_dados_fim");
        long b0 = Ler("cs_marca_bss_runtime"), b1 = Ler("cs_marca_bss_fim");
        if (d0 == 0 || d1 < d0 || b0 == 0 || b1 < b0) return;
        EstaticaDoProjeto = (d1 - d0) + (b1 - b0);
    }

    ErroDeSintaxe ErroDeCompilacao(List<string> mensagens)
    {
        List<string> linhas;
        lock (mensagens) linhas = [.. mensagens.Select(Limpar)];
        var erros = linhas.Where(l => !l.Contains("warning:", StringComparison.Ordinal)).ToList();
        if (erros.Count == 0) erros = linhas;
        string texto = erros.Count == 0 ? Idioma.T("o compilador de C recusou o programa (sem mensagem).")
                     : string.Join(Environment.NewLine, erros.Take(12));
        if (erros.Count > 12) texto += Idioma.T("{0}... e mais {1}", Environment.NewLine, erros.Count - 12);
        return new ErroDeSintaxe(Idioma.T("compilando o C: {0}", texto), 0);
    }

    // As pastas como o compilador as escreve nas mensagens, e o que mostrar
    // no lugar delas.
    readonly List<(Regex Padrao, string Nome)> pastasConhecidas = [];

    void Mostrar(string pasta, string nome)
    {
        Tcc.Caminho(pasta, out string comoVolta);
        // Qualquer barra entre as partes: o TinyCC junta "pasta" + "/" + "arquivo".
        string padrao = string.Join(@"[\\/]", comoVolta.Split('\\', '/').Select(Regex.Escape)) + @"[\\/]";
        pastasConhecidas.Add((new Regex(padrao, RegexOptions.IgnoreCase), nome.Length == 0 ? "" : nome + "/"));
    }

    /// <summary>Caminho absoluto vira o nome que a pessoa reconhece.</summary>
    string Limpar(string mensagem)
    {
        // A linha do texto que embrulha cada arquivo (o #include) nao interessa.
        mensagem = Regex.Replace(mensagem, @"^In file included from <string>:\d+:\r?\n", "");
        foreach (var (padrao, nome) in pastasConhecidas)
            mensagem = padrao.Replace(mensagem, nome.Replace("$", "$$"));
        return mensagem;
    }

    /// <summary>
    /// As macros de uma compilacao do core ESP32 do Arduino: as da placa
    /// (ARDUINO, ARDUINO_ESP32S3_DEV...), as do ESP-IDF e a que escolhe o
    /// sdkconfig.h do chip.
    /// </summary>
    static IEnumerable<(string Nome, string Valor)> Macros(string placa)
    {
        // As do recipe.c.o.pattern do core (platform.txt), para um .c: nada
        // de __cplusplus, e o CONFIG_ vem do sdkconfig.h do chip.
        string chip = Chip(placa);
        string variante = chip.ToUpperInvariant();
        var perfil = PerfilPlaca.De(placa);
        yield return ("ARDUINO", "10607");
        yield return ("ESP32", "ESP32");
        yield return ("ARDUINO_ARCH_ESP32", "1");
        yield return ($"ARDUINO_{(chip == "esp32" ? "ESP32" : variante)}_DEV", "1");
        yield return ("ARDUINO_BOARD", chip == "esp32" ? "\"ESP32_DEV\"" : $"\"{variante}_DEV\"");
        yield return ("ARDUINO_VARIANT", "\"" + chip + "\"");
        yield return ("F_CPU", ((long)((perfil?.Megahertz ?? 240) * 1_000_000)) + "L");
        yield return ("ESP_PLATFORM", "1");
        yield return ("IDF_VER", "\"v5.5.5\"");
        yield return ("CORE_DEBUG_LEVEL", "0");
        yield return ("ARDUINO_RUNNING_CORE", "1");
        yield return ("ARDUINO_EVENT_RUNNING_CORE", "1");
        if (chip == "esp32s3")
        {
            yield return ("ARDUINO_USB_MODE", "1");
            yield return ("ARDUINO_USB_CDC_ON_BOOT", "0");
        }
        yield return ("CS_CHIP_" + variante, "1");
    }

    /// <summary>O chip da placa, como o ESP-IDF o chama ("esp32s3").</summary>
    public static string Chip(string placa) => placa switch
    {
        "esp32-s3-devkit" => "esp32s3",
        "esp32-s2-devkit" => "esp32s2",
        "esp32-c3-devkit" => "esp32c3",
        "esp32-c6-devkit" => "esp32c6",
        "esp32-h2-devkit" => "esp32h2",
        _ => "esp32",
    };

    IntPtr Simbolo(string nome)
    {
        fixed (byte* p = Tcc.Z(nome)) return (IntPtr)Tcc.Simbolo(estado, p);
    }

    /// <summary>O endereco de uma funcao (ou variavel) do C, ou zero.</summary>
    public IntPtr Endereco(string nome) => encerrando || estado == IntPtr.Zero ? IntPtr.Zero : Simbolo(nome);

    void Iniciar()
    {
        IntPtr Exigir(string nome)
        {
            IntPtr p = Simbolo(nome);
            if (p == IntPtr.Zero) throw new ErroDeExecucao(Idioma.T("o runtime nativo esta incompleto (falta {0}).", nome), 0);
            return p;
        }

        rodarTarefa = (delegate* unmanaged<IntPtr, void>)Exigir("cs_rodar_tarefa");
        chamar = (delegate* unmanaged<IntPtr, IntPtr, ulong*, int, double*, ulong*, int>)Exigir("cs_chamar");
        tarefaDoSimulador = (delegate* unmanaged<byte*, int, int, IntPtr>)Exigir("cs_tarefa_do_simulador");
        nomeDaTarefa = (delegate* unmanaged<IntPtr, byte*>)Exigir("cs_nome_da_tarefa");
        parando = (int*)Exigir("cs_parando");
        var iniciar = (delegate* unmanaged<IntPtr, uint, IntPtr, nuint, nuint, IntPtr, nuint, nuint, ulong, ulong, int>)Exigir("cs_iniciar");
        int campos = *(int*)Exigir("cs_tamanho_do_host");

        var funcoes = HostNativo.Tabela();
        if (campos != funcoes.Length)
            throw new ErroDeExecucao(Idioma.T("o runtime nativo e o simulador nao combinam ({0} funcoes no C, {1} no simulador).", campos, funcoes.Length), 0);
        tabela = Marshal.AllocHGlobal(funcoes.Length * IntPtr.Size);
        for (int i = 0; i < funcoes.Length; i++) ((IntPtr*)tabela)[i] = funcoes[i];

        lock (travaGlobal)
            if (indiceTls == uint.MaxValue) indiceTls = Win32.TlsAlloc();

        // A memoria da placa: a interna com folga para os ponteiros de 8
        // bytes; a PSRAM, do tamanho dela e mais metade.
        var perfil = Maquina.Perfil;

        // A heap interna e a da placa: o que sobra do sketch vazio, menos as
        // globais do sketch e as dos .c do projeto (o DOOM tem ~130 KB delas --
        // na S3 de verdade sobram ~250 KB, e nao os 320 de antes).
        Maquina.Memoria.DefinirEstatica(Maquina.Memoria.Estatica + EstaticaDoProjeto);
        long interna = Math.Max(Maquina.Memoria.HeapLivre, 16 * 1024);
        long psram = perfil.PsramBytes;
        tamanhoInterna = (nuint)(interna * 2);
        regiaoInterna = Win32.VirtualAlloc(IntPtr.Zero, tamanhoInterna, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
        if (psram > 0)
        {
            tamanhoPsram = (nuint)(psram + psram / 2);
            regiaoPsram = Win32.VirtualAlloc(IntPtr.Zero, tamanhoPsram, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
        }
        if (regiaoInterna == IntPtr.Zero || (psram > 0 && regiaoPsram == IntPtr.Zero))
            throw new ErroDeExecucao(Idioma.T("sem memoria para a RAM da placa."), 0);

        // O tratador de falhas e o do proprio runtime (C): so olha o codigo
        // deste modulo, e nao chama o .NET.
        tratadorDeExcecoes = Win32.AddVectoredExceptionHandler(1, Exigir("cs_excecao"));
        saltoDeVolta = Exigir("__cs_longjmp");
        Win32.RegistrarAlvoDeExcecao(saltoDeVolta, true);
        if (Environment.GetEnvironmentVariable("CS_DEPURAR_VEH") == "1")
        {
            Console.Error.WriteLine($"[veh] codigo {codigo:X}..{(ulong)codigo + tamanhoDoCodigo:X} tratador {Exigir("cs_excecao"):X}");
            Win32.AddVectoredExceptionHandler(1, (IntPtr)(delegate* unmanaged<IntPtr, int>)&Depurar);
        }

        // A CPU simulada: quanto o chip e mais lento que este computador no
        // C do TinyCC (a bancada contra o CoreMark publicado da placa).
        UsPorCiclo = CpuNativa.Calibrar(perfil, Simbolo("cs_bancada"));

        // Relogio do Windows em 1 ms: o vTaskDelay(1) e o sleep curto das
        // tarefas dependem disso.
        Win32.timeBeginPeriod(1);

        ulong inicio = (ulong)codigo, fim = inicio + tamanhoDoCodigo;
        iniciar(tabela, indiceTls, regiaoInterna, tamanhoInterna, (nuint)interna, regiaoPsram, tamanhoPsram,
                (nuint)Math.Max(psram, 0), inicio, fim);

        // Tarefas sao threads de verdade: o relogio tem que ser o real. Na
        // linha de comando e nos testes ele era virtual -- passa a andar agora.
        if (!Maquina.TempoReal)
        {
            Maquina.TempoReal = true;
            Maquina.IniciarRelogio();
        }
        new Thread(Vigiar) { IsBackground = true, Name = "vigia do modo nativo" }.Start();
    }

    [UnmanagedCallersOnly]
    static int Depurar(IntPtr ponteiros)
    {
        byte* registro = ((byte**)ponteiros)[0];
        byte* contexto = ((byte**)ponteiros)[1];
        uint codigo = *(uint*)registro;
        ulong rip = *(ulong*)(contexto + 0xF8);
        if (codigo != 0x406D1388 && codigo != 0xE06D7363)
            Console.Error.WriteLine($"[veh] excecao {codigo:X8} em {rip:X}");
        return 0;
    }

    /// <summary>Para as tarefas quando a placa para (botao Parar, RESET, fim do tempo).</summary>
    void Vigiar()
    {
        while (!encerrando)
        {
            if (Maquina.Parar)
            {
                Encerrar();
                return;
            }
            Thread.Sleep(20);
        }
    }

    // =====================================================================
    //  tarefas
    // =====================================================================

    /// <summary>
    /// Microssegundos de chip por ciclo de processador do computador: o que a
    /// CPU simulada cobra do C (ver <see cref="CpuNativa"/>). Zero: desligada.
    /// </summary>
    internal double UsPorCiclo;

    internal bool CriarTarefa(IntPtr tarefa, int pilha)
    {
        if (encerrando) return false;
        var ctx = new ContextoDaThread { Modulo = this, Tarefa = tarefa, Nome = NomeDa(tarefa) };

        // A pilha do PC e bem maior que a da placa: o codigo de 64 bits do
        // TinyCC gasta mais, e estourar aqui nao ensinaria nada.
        int tamanho = Math.Clamp(pilha * 8, 1 << 20, 16 << 20);
        var thread = new Thread(() => RodarTarefa(ctx), tamanho)
        {
            IsBackground = true,
            Name = "tarefa " + ctx.Nome,
        };
        ctx.Thread = thread;
        ctx.Viva = true;
        lock (travaDasThreads) contextos[tarefa] = ctx;
        thread.Start();
        return true;
    }

    void RodarTarefa(ContextoDaThread ctx)
    {
        atual = this;
        contexto = ctx;
        ctx.IdNativo = Win32.GetCurrentThreadId();
        Win32.TlsSetValue(indiceTls, ctx.Tarefa);
        try
        {
            CpuNativa.Sair(UsPorCiclo);
            if (!encerrando) rodarTarefa(ctx.Tarefa);
            CpuNativa.Entrar(UsPorCiclo);
        }
        catch (Exception e)
        {
            Falha(e);
        }
        finally
        {
            ctx.Viva = false;
            Win32.TlsSetValue(indiceTls, IntPtr.Zero);
            lock (travaDasThreads) contextos.Remove(ctx.Tarefa);
        }
    }

    string NomeDa(IntPtr tarefa)
        => tarefa == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8((IntPtr)nomeDaTarefa(tarefa)) ?? "";

    internal void AcordarTarefa(IntPtr tarefa)
    {
        ContextoDaThread ctx;
        lock (travaDasThreads) contextos.TryGetValue(tarefa, out ctx);
        if (ctx is null) return;
        ctx.Encerrar = true;
        ctx.Acordar.Set();
    }

    /// <summary>A thread que chama: e do modulo? tem que sair?</summary>
    internal static bool DeveSair()
    {
        var ctx = contexto;
        return ctx is null || ctx.Encerrar || ctx.Modulo.encerrando;
    }

    internal static ModuloNativo Atual => atual;
    internal static ContextoDaThread Contexto => contexto;

    // =====================================================================
    //  o sketch interpretado chamando o C
    // =====================================================================

    /// <summary>
    /// Chama uma funcao do C da thread do interpretador. Cada argumento e
    /// um "slot" de 8 bytes (inteiro, ponteiro, ou os bits de um double ou
    /// float). Status: 0 voltou, 1 a placa parou no meio, 2 deu falha.
    /// </summary>
    public (int Status, ulong Inteiro, double Real) Chamar(IntPtr funcao, ReadOnlySpan<ulong> argumentos)
    {
        if (encerrando || funcao == IntPtr.Zero) return (1, 0, 0);

        if (doSketch is null)
        {
            IntPtr t;
            fixed (byte* nome = Tcc.Z("loopTask")) t = tarefaDoSimulador(nome, 1, 1);
            doSketch = new ContextoDaThread { Modulo = this, Tarefa = t, Nome = "loopTask", Viva = true };
            lock (travaDasThreads) contextos[t] = doSketch;
        }

        var slots = new ulong[Math.Max(4, argumentos.Length)];
        argumentos.CopyTo(slots);

        var antes = atual;
        var ctxAntes = contexto;
        atual = this;
        contexto = doSketch;
        doSketch.Thread = Thread.CurrentThread;
        doSketch.IdNativo = Win32.GetCurrentThreadId();
        Win32.TlsSetValue(indiceTls, doSketch.Tarefa);
        Interlocked.Increment(ref dentro);
        try
        {
            if (encerrando) return (1, 0, 0);
            double real = 0;
            ulong inteiro = 0;
            int status;
            CpuNativa.Sair(UsPorCiclo);
            fixed (ulong* s = slots) status = chamar(doSketch.Tarefa, funcao, s, argumentos.Length, &real, &inteiro);
            CpuNativa.Entrar(UsPorCiclo);
            return (status, inteiro, real);
        }
        finally
        {
            Interlocked.Decrement(ref dentro);
            atual = antes;
            contexto = ctxAntes;
        }
    }

    /// <summary>Um texto do .NET em memoria do C (da RAM da placa). Solte com <see cref="Soltar"/>.</summary>
    public IntPtr TextoNoC(string texto)
    {
        IntPtr malloc = Endereco("malloc");
        if (malloc == IntPtr.Zero) return IntPtr.Zero;
        var bytes = TextoArduino.ParaBytes(texto ?? "");
        var r = Chamar(malloc, [(ulong)(bytes.Length + 1)]);
        if (r.Status != 0 || r.Inteiro == 0) return IntPtr.Zero;
        var p = (byte*)r.Inteiro;
        bytes.AsSpan().CopyTo(new Span<byte>(p, bytes.Length));
        p[bytes.Length] = 0;
        return (IntPtr)p;
    }

    public void Soltar(IntPtr p)
    {
        IntPtr free = Endereco("free");
        if (p != IntPtr.Zero && free != IntPtr.Zero) Chamar(free, [(ulong)p]);
    }

    // =====================================================================
    //  parar
    // =====================================================================

    /// <summary>
    /// Para todas as tarefas e solta a memoria. Pode ser chamado de
    /// qualquer thread, menos de dentro de uma chamada ao simulador.
    /// </summary>
    public void Encerrar()
    {
        if (Interlocked.Exchange(ref encerrado, 1) != 0) return;
        encerrando = true;
        if (parando != null) *parando = 1;

        List<ContextoDaThread> vivas;
        lock (travaDasThreads) vivas = [.. contextos.Values];
        foreach (var ctx in vivas) ctx.Acordar.Set();
        PararPerifericos();

        var relogio = Stopwatch.StartNew();
        foreach (var ctx in vivas.Where(c => c.Thread is not null && c != doSketch))
            ctx.Thread.Join(TimeSpan.FromMilliseconds(Math.Max(1, 1500 - relogio.ElapsedMilliseconds)));

        // Quem nao saiu esta num laco que nao chama nada: desvia a forca.
        foreach (var ctx in vivas.Where(c => c.Viva && c != doSketch && c.Thread is { IsAlive: true }))
        {
            Forcar(ctx);
            ctx.Thread.Join(500);
        }

        while (Volatile.Read(ref dentro) > 0 && relogio.ElapsedMilliseconds < 4000) Thread.Sleep(5);

        bool presas = vivas.Any(c => c != doSketch && c.Thread is { IsAlive: true }) || Volatile.Read(ref dentro) > 0;
        if (presas)
        {
            // Soltar a memoria com codigo rodando nela derrubaria o programa.
            Maquina.Avisar(Idioma.T("modo nativo: uma tarefa do C nao parou; a memoria dela fica presa ate fechar o simulador."));
            return;
        }
        Liberar();
    }

    /// <summary>
    /// Desvia uma thread presa num laco de C puro para o salto de volta da
    /// tarefa dela, pelo contexto da thread (como um debugger faria).
    /// </summary>
    void Forcar(ContextoDaThread ctx)
    {
        if (ctx.IdNativo == 0) return;
        IntPtr h = Win32.OpenThread(Win32.THREAD_ALL_ACCESS, false, ctx.IdNativo);
        if (h == IntPtr.Zero) return;
        byte* bruto = (byte*)NativeMemory.AlignedAlloc(1232 + 64, 64);
        try
        {
            if (Win32.SuspendThread(h) == uint.MaxValue) return;
            try
            {
                new Span<byte>(bruto, 1232).Clear();
                *(uint*)(bruto + 0x30) = 0x10000B;   // CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_FLOATING_POINT
                if (!Win32.GetThreadContext(h, bruto)) return;
                ulong rip = *(ulong*)(bruto + 0xF8);
                ulong inicio = (ulong)codigo, fim = inicio + tamanhoDoCodigo;
                if (rip < inicio || rip >= fim) return;   // esta no simulador: vai sair sozinha

                ulong* salto = (ulong*)ctx.Tarefa;          // o salto e o primeiro campo da tarefa
                IntPtr longjmp = Simbolo("__cs_longjmp");
                if (longjmp == IntPtr.Zero) return;
                *(ulong*)(bruto + 0x98) = (salto[8] - 64) & ~15UL;   // Rsp
                *(ulong*)(bruto + 0x80) = (ulong)salto;              // Rcx
                *(ulong*)(bruto + 0x88) = 1;                         // Rdx
                *(ulong*)(bruto + 0xF8) = (ulong)longjmp;            // Rip
                Win32.SetThreadContext(h, bruto);
            }
            finally
            {
                Win32.ResumeThread(h);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(bruto);
            Win32.CloseHandle(h);
        }
    }

    void Liberar()
    {
        try { PararPerifericos(); } catch { }
        if (saltoDeVolta != IntPtr.Zero)
        {
            Win32.RegistrarAlvoDeExcecao(saltoDeVolta, false);
            saltoDeVolta = IntPtr.Zero;
        }
        if (tratadorDeExcecoes != IntPtr.Zero)
        {
            Win32.RemoveVectoredExceptionHandler(tratadorDeExcecoes);
            tratadorDeExcecoes = IntPtr.Zero;
            Win32.timeEndPeriod(1);
        }
        if (codigo != IntPtr.Zero)
        {
            // O TinyCC registrou a tabela de desenrolar do codigo e guardou o
            // endereco dela nos 8 primeiros bytes da memoria que recebeu (o
            // "*(void**)mem = win64_add_function_table(s1)" do tccrun.c). E
            // ESSA que sai. Antes ela era procurada pelo endereco codigo + 16, e
            // quando ali nao havia funcao a tabela ficava registrada apontando
            // para memoria ja devolvida -- a proxima busca do Windows (qualquer
            // excecao, em qualquer thread) derrubava o processo inteiro.
            try
            {
                IntPtr tabelaDoTcc = Marshal.ReadIntPtr(codigo);
                if (tabelaDoTcc != IntPtr.Zero) Win32.RtlDeleteFunctionTable(tabelaDoTcc);
            }
            catch { }
        }
        if (estado != IntPtr.Zero)
        {
            Tcc.Apagar(estado);
            Tcc.Morreu();
            estado = IntPtr.Zero;
        }
        if (codigo != IntPtr.Zero) { Win32.VirtualFree(codigo, 0, Win32.MEM_RELEASE); codigo = IntPtr.Zero; }
        if (regiaoInterna != IntPtr.Zero) { Win32.VirtualFree(regiaoInterna, 0, Win32.MEM_RELEASE); regiaoInterna = IntPtr.Zero; }
        if (regiaoPsram != IntPtr.Zero) { Win32.VirtualFree(regiaoPsram, 0, Win32.MEM_RELEASE); regiaoPsram = IntPtr.Zero; }
        if (tabela != IntPtr.Zero) { Marshal.FreeHGlobal(tabela); tabela = IntPtr.Zero; }
        if (pastaTemporaria is not null)
        {
            try { Directory.Delete(pastaTemporaria, true); } catch { }
            pastaTemporaria = null;
        }
    }

    /// <summary>Erro do lado C# dentro de uma chamada do C: vira aviso, e a placa para.</summary>
    internal void Falha(Exception e)
    {
        Maquina.Avisar(Idioma.T("modo nativo: erro interno do simulador ({0}: {1})", e.GetType().Name, e.Message));
        Maquina.Parar = true;
    }
}
