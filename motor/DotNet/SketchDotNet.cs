using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace Simulador;

/// <summary>
/// O programa em C# compilado (CompiladorCSharp) rodando numa placa.
///
/// Ele roda no proprio .NET, num contexto de carga so dele (solto no fim da
/// rodada). O Main e o setup(); cada chamada da API ao hardware cai no
/// interpretador da placa (ChamarDoNativo), como as do C++ compilado. O
/// programa pode ter threads de verdade; a placa atende uma chamada de cada
/// vez (a trava). As interrupcoes e a parada chegam nos pontos que o
/// compilador pos nos lacos, e no delay do simulador.
/// </summary>
public sealed class SketchDotNet : ISketchCompilado
{
    readonly Maquina maq;
    readonly SketchNativo.Atendente atender;
    readonly Action atenderInterrupcoes;
    readonly object trava = new();

    Contexto contexto;
    Assembly programa;
    Type ponte;
    MethodInfo despachar;
    Thread daPlaca;
    readonly Stopwatch relogio = Stopwatch.StartNew();
    long ultimoPonto;

    /// <summary>O erro de uma thread do programa, para a thread da placa contar.</summary>
    volatile Exception daThread;

    public SketchDotNet(Maquina maquina, SketchNativo.Atendente atendente, Action atenderInterrupcoes)
    {
        maq = maquina;
        atender = atendente;
        this.atenderInterrupcoes = atenderInterrupcoes;
    }

    /// <summary>O contexto do programa: a API e o programa, carregados dos bytes.</summary>
    sealed class Contexto(byte[] api) : AssemblyLoadContext("programa C# da placa", isCollectible: true)
    {
        Assembly carregada;

        public Assembly CarregarApi() => carregada ??= LoadFromStream(new MemoryStream(api));

        protected override Assembly Load(AssemblyName nome)
            => nome.Name == "CircuitSketch.Api" ? CarregarApi() : null;
    }

    public void Carregar(ProgramaDotNet compilado)
    {
        contexto = new Contexto(compilado.Api);
        var api = contexto.CarregarApi();
        programa = contexto.LoadFromStream(new MemoryStream(compilado.Programa));
        ponte = api.GetType("CircuitSketch.Ponte", throwOnError: true);
        despachar = ponte.GetMethod("Despachar");

        Ligar("Chamar", (Func<string, string, object[], bool[], object>)Chamar);
        Ligar("Ponto", (Action)Ponto);
        Ligar("Constante", (Func<string, object>)Constante);
        Ligar("Falhou", (Action<Exception>)(e => daThread ??= e));
        Ligar("Processador", maq.Perfil?.Processador ?? "");
        Ligar("MaximoDoAdc", maq.Perfil?.MaxAdc ?? 4095);

        // O relogio e o real: o programa roda na velocidade do PC.
        if (!maq.TempoReal)
        {
            maq.TempoReal = true;
            maq.IniciarRelogio();
        }
    }

    void Ligar(string campo, object valor) => ponte.GetField(campo)!.SetValue(null, valor);

    bool Saindo
    {
        get => (bool)ponte.GetField("Saindo")!.GetValue(null)!;
        set => ponte.GetField("Saindo")!.SetValue(null, value);
    }

    // =====================================================================
    //  rodar
    // =====================================================================

    public void Setup()
    {
        daPlaca = Thread.CurrentThread;
        Ligar("ThreadDaPlaca", daPlaca);
        ponte.GetMethod("MarcarPilha")!.Invoke(null, null);
        // O monitor: o Console.WriteLine e o Debug.WriteLine saem nele.
        Chamar("Serial", "begin", [115200L], null);

        var entrada = programa.EntryPoint ?? throw new ErroDeExecucao("o programa C# nao tem Main.", 0);
        object[] args = entrada.GetParameters().Length > 0 ? [Array.Empty<string>()] : null;
        try
        {
            object volta = entrada.Invoke(null, args);
            if (volta is Task t) t.GetAwaiter().GetResult();
        }
        catch (TargetInvocationException e) when (e.InnerException is not null) { Relancar(e.InnerException); }
        catch (Exception e) when (e is not (ErroDeExecucao or ExecucaoInterrompida or ReinicioDaPlaca)) { Relancar(e); }
        ConferirThreads();
    }

    /// <summary>Depois do Main: a placa espera (as threads e as interrupcoes seguem).</summary>
    public void Loop()
    {
        ConferirThreads();
        Chamar(null, "delay", [10L], null);
    }

    public void Rotina(int indice) => Rotina(indice, null);

    public void Rotina(int indice, object[] argumentos)
    {
        var args = argumentos?.Select(a => a switch
        {
            Valor v when v.EhTexto => v.Texto,
            Valor v when v.Inteiro || v.Caractere || v.EhBooleano => (object)(long)v.Num,
            Valor v => v.Num,
            Valor[] vetor => vetor.Select(x => (byte)((long)x.Num & 0xFF)).ToArray(),
            _ => a,
        }).ToArray();
        try { despachar.Invoke(null, [indice, args]); }
        catch (TargetInvocationException e) when (e.InnerException is not null) { Relancar(e.InnerException); }
    }

    /// <summary>
    /// A excecao do programa vira o erro da placa, com a linha (o PDB vem
    /// junto); a parada e o reset seguem como sao.
    /// </summary>
    void Relancar(Exception e)
    {
        if (e is ErroDeExecucao or ExecucaoInterrompida or ReinicioDaPlaca) ExceptionDispatchInfo.Capture(e).Throw();
        if (e.GetType().Name == "ParadaDaPlaca" || Saindo && maq.Parar) throw maq.MotivoDaParada();
        int linha = 0;
        foreach (var quadro in new StackTrace(e, true).GetFrames() ?? [])
            if (quadro.GetFileName() == "Program.cs" && quadro.GetFileLineNumber() > 0)
            {
                linha = quadro.GetFileLineNumber();
                break;
            }
        string tipo = e is InsufficientExecutionStackException ? "a pilha acabou (recursao sem fim?)" : $"{e.GetType().Name}: {e.Message}";
        throw new ErroDeExecucao($"o programa parou com uma excecao sem catch -- {tipo}", linha);
    }

    void ConferirThreads()
    {
        var e = daThread;
        if (e is null) return;
        daThread = null;
        if (e is ErroDeExecucao erro) throw new ErroDeExecucao("numa thread do programa: " + erro.Message, 0);
        Relancar(e);
    }

    // =====================================================================
    //  a ponte (a API chamando a placa)
    // =====================================================================

    object Chamar(string alvo, string nome, object[] args, bool[] nomes)
    {
        lock (trava)
        {
            try
            {
                if (maq.Parar) throw maq.MotivoDaParada();
                var lista = new List<Valor>(args.Length);
                var marcas = new List<bool>(args.Length);
                for (int i = 0; i < args.Length; i++)
                {
                    marcas.Add(nomes is not null && i < nomes.Length && nomes[i]);
                    lista.Add(args[i] switch
                    {
                        null => Valor.Vazio,
                        string s => Valor.DeTexto(s),
                        char c => Valor.DeCaractere(c),
                        bool b => Valor.DeBool(b),
                        float f => Valor.DeReal(f),
                        double d => Valor.DeReal(d),
                        decimal m => Valor.DeReal((double)m),
                        byte[] bytes => Valor.DeReferencia(bytes.Select(x => Valor.DeC(x, TipoC.UChar)).ToArray()),
                        Enum en => Valor.DeInt(Convert.ToInt64(en)),
                        IConvertible n => Valor.DeInt(n.ToInt64(System.Globalization.CultureInfo.InvariantCulture)),
                        var o => Valor.DeTexto(o.ToString()),
                    });
                }
                var v = atender(alvo, nome, lista, marcas);
                if (v.EhTexto) return v.Texto ?? "";
                if (v.Referencia is not null) return null;
                if (v.EhBooleano) return v.Num != 0;
                if (v.Inteiro || v.Caractere) return (long)v.Num;
                return v.Num;
            }
            catch (Exception e)
            {
                // A placa saiu (Parar, reset, erro): o programa nao segue,
                // nem o catch dele pega. O erro de outra thread fica para a
                // da placa contar.
                Saindo = true;
                if (Thread.CurrentThread != daPlaca && e is ErroDeExecucao) daThread ??= e;
                throw;
            }
        }
    }

    /// <summary>O ponto do laco na thread da placa: a parada e as interrupcoes.</summary>
    void Ponto()
    {
        long agora = relogio.ElapsedTicks;
        if (agora - ultimoPonto < Stopwatch.Frequency / 1000) return;
        ultimoPonto = agora;
        lock (trava)
        {
            if (maq.Parar)
            {
                Saindo = true;
                throw maq.MotivoDaParada();
            }
            ConferirThreads();
            atenderInterrupcoes();
        }
    }

    object Constante(string nome)
    {
        if (nome == "NUM_DIGITAL_PINS") return (long)(maq.Perfil?.NumPinos ?? 40);
        if (Interpretador.ConstanteDoCore(nome) is { } v) return long.Parse(v);
        if (maq.Perfil?.PinoDoCore(nome) is >= 0 and var p) return (long)p;
        return null;
    }

    public void Encerrar()
    {
        try { ponte?.GetMethod("Encerrar")?.Invoke(null, null); } catch { }
        try { contexto?.Unload(); } catch { }
        contexto = null;
    }
}
