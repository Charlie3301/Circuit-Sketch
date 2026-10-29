// Ponte.cs -- a API das placas para o C# (compilada pelo Roslyn junto do
// programa; ver CompiladorCSharp). Cada chamada ao hardware vira a mesma
// chamada que o sketch interpretado faria ("digitalWrite(2, 1)"), atendida
// pelo interpretador da placa (Interpretador.ChamarDoNativo), uma de cada
// vez: o programa pode ter threads, a placa e uma so.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CircuitSketch
{
    /// <summary>A placa parou (o Parar, o reset): a thread do programa sai por aqui.</summary>
    public sealed class ParadaDaPlaca : Exception
    {
        public ParadaDaPlaca() : base("a placa parou") { }
    }

    public static class Ponte
    {
        // ---- ligados pelo simulador (SketchDotNet), por reflexao ----
        public static Func<string, string, object[], bool[], object> Chamar;
        public static Action Ponto;
        public static Func<string, object> Constante;
        public static Action<Exception> Falhou;
        public static Thread ThreadDaPlaca;
        public static string Processador = "";
        public static int MaximoDoAdc = 4095;
        public static volatile bool Saindo;
        public static readonly CancellationTokenSource Fim = new CancellationTokenSource();

        // ---- as chamadas ----
        public static object C(string alvo, string nome, params object[] a) => Chamar(alvo, nome, a ?? Array.Empty<object>(), null);
        public static object CN(string alvo, string nome, object[] a, bool[] nomes) => Chamar(alvo, nome, a, nomes);

        public static long I(string alvo, string nome, params object[] a) => Inteiro(C(alvo, nome, a));
        public static double R(string alvo, string nome, params object[] a) => Real(C(alvo, nome, a));
        public static string T(string alvo, string nome, params object[] a) => Texto(C(alvo, nome, a));

        public static long Inteiro(object v) => v switch
        {
            null => 0,
            long l => l,
            double d => (long)d,
            bool b => b ? 1 : 0,
            string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) ? x : 0,
            _ => Convert.ToInt64(v, CultureInfo.InvariantCulture),
        };

        public static double Real(object v) => v switch
        {
            null => 0,
            double d => d,
            long l => l,
            bool b => b ? 1 : 0,
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : 0,
            _ => Convert.ToDouble(v, CultureInfo.InvariantCulture),
        };

        public static string Texto(object v) => v switch
        {
            null => "",
            string s => s,
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture),
        };

        public static bool EhEsp32 => Processador.StartsWith("ESP32", StringComparison.OrdinalIgnoreCase);

        /// <summary>A constante do core da placa ("OUTPUT", "A0"), ou o padrao.</summary>
        public static int Numero(string nome, int padrao)
        {
            object v = Constante?.Invoke(nome);
            return v is null ? padrao : (int)Inteiro(v);
        }

        // ---- as rotinas que o simulador chama de volta ("__nativa_3") ----
        static readonly List<Delegate> rotinas = new List<Delegate>();

        public static string Registrar(Delegate d)
        {
            lock (rotinas)
            {
                int i = rotinas.IndexOf(d);
                if (i < 0)
                {
                    rotinas.Add(d);
                    i = rotinas.Count - 1;
                }
                return "__nativa_" + i;
            }
        }

        public static void Despachar(int indice, object[] argumentos)
        {
            Delegate d;
            lock (rotinas) d = indice >= 0 && indice < rotinas.Count ? rotinas[indice] : null;
            if (d is null) return;
            var parametros = d.Method.GetParameters();
            var args = new object[parametros.Length];
            for (int i = 0; i < args.Length; i++)
            {
                object a = argumentos != null && i < argumentos.Length ? argumentos[i] : null;
                Type t = parametros[i].ParameterType;
                args[i] = a is null || t.IsInstanceOfType(a) ? a
                        : t == typeof(string) ? Texto(a)
                        : t.IsPrimitive || t.IsEnum ? Convert.ChangeType(t.IsEnum ? Inteiro(a) : a, t.IsEnum ? Enum.GetUnderlyingType(t) : t, CultureInfo.InvariantCulture)
                        : a;
            }
            try { d.DynamicInvoke(args); }
            catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        // ---- o tempo ----
        static bool NaPlaca => Thread.CurrentThread == ThreadDaPlaca;

        /// <summary>
        /// O Thread.Sleep do programa. Na thread da placa, o delay do
        /// simulador (que atende as interrupcoes) em pedacos, soltando a placa
        /// entre eles para as outras threads; nas outras, o sono do Windows.
        /// </summary>
        public static void Dormir(int ms)
        {
            if (ms < 0) ms = int.MaxValue;
            var relogio = Stopwatch.StartNew();
            do
            {
                if (Saindo) throw new ParadaDaPlaca();
                long resto = ms - relogio.ElapsedMilliseconds;
                if (resto <= 0) break;
                if (NaPlaca)
                {
                    C(null, "delay", Math.Min(resto, 2L));
                    Thread.Yield();
                }
                else
                {
                    Thread.Sleep((int)Math.Min(resto, 10L));
                }
            } while (true);
            if (ms == 0) Thread.Yield();
        }

        public static void Dormir(TimeSpan t) => Dormir((int)Math.Min(int.MaxValue, Math.Max(-1, (long)t.TotalMilliseconds)));

        public static Task Esperar(int ms) => Task.Delay(ms, Fim.Token);
        public static Task Esperar(TimeSpan t) => Task.Delay(t, Fim.Token);
        public static Task Esperar(int ms, CancellationToken c) => Task.Delay(ms, CancellationTokenSource.CreateLinkedTokenSource(c, Fim.Token).Token);
        public static Task Esperar(TimeSpan t, CancellationToken c) => Task.Delay(t, CancellationTokenSource.CreateLinkedTokenSource(c, Fim.Token).Token);

        // ---- os pontos que o compilador poe nos lacos e nos metodos ----
        [ThreadStatic] static int voltas;

        public static void Volta()
        {
            if ((++voltas & 63) != 0) return;
            if (NaPlaca) Ponto?.Invoke();
            else if (Saindo) throw new ParadaDaPlaca();
        }

        /// <summary>
        /// A pilha do programa: 1 MB por thread, contado de onde ela comecou
        /// (MarcarPilha). Sem limite, a recursao sem fim desceria milhoes de
        /// quadros, e so desenrolar a excecao por eles levaria segundos.
        /// </summary>
        const long LimiteDaPilha = 1 << 20;
        [ThreadStatic] static long inicioDaPilha;

        public static unsafe void MarcarPilha()
        {
            byte marca;
            inicioDaPilha = (long)&marca;
        }

        public static unsafe void Pilha()
        {
            byte marca;
            long aqui = (long)&marca;
            if (inicioDaPilha == 0) inicioDaPilha = aqui;
            if (inicioDaPilha - aqui > LimiteDaPilha) throw new InsufficientExecutionStackException();
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        // ---- as threads e os temporizadores do programa ----
        static void Proteger(Action corpo)
        {
            MarcarPilha();
            try { corpo(); }
            catch (ParadaDaPlaca) { }
            catch (Exception) when (Saindo) { }
            catch (Exception e) { Falhou?.Invoke(e); }
        }

        public static Thread NovaThread(ThreadStart inicio) => new Thread(() => Proteger(() => inicio())) { IsBackground = true };
        public static Thread NovaThread(ThreadStart inicio, int pilha) => new Thread(() => Proteger(() => inicio()), Math.Max(pilha, 1 << 20)) { IsBackground = true };
        public static Thread NovaThread(ParameterizedThreadStart inicio) => new Thread(o => Proteger(() => inicio(o))) { IsBackground = true };
        public static Thread NovaThread(ParameterizedThreadStart inicio, int pilha) => new Thread(o => Proteger(() => inicio(o)), Math.Max(pilha, 1 << 20)) { IsBackground = true };

        static readonly List<Timer> temporizadores = new List<Timer>();

        static Timer Guardar(Timer t)
        {
            lock (temporizadores) temporizadores.Add(t);
            return t;
        }

        static TimerCallback Protegido(TimerCallback cb) => o =>
        {
            if (!Saindo) Proteger(() => cb(o));
        };

        public static Timer NovoTimer(TimerCallback cb) => Guardar(new Timer(Protegido(cb)));
        public static Timer NovoTimer(TimerCallback cb, object estado, int inicio, int periodo) => Guardar(new Timer(Protegido(cb), estado, inicio, periodo));
        public static Timer NovoTimer(TimerCallback cb, object estado, long inicio, long periodo) => Guardar(new Timer(Protegido(cb), estado, inicio, periodo));
        public static Timer NovoTimer(TimerCallback cb, object estado, uint inicio, uint periodo) => Guardar(new Timer(Protegido(cb), estado, inicio, periodo));
        public static Timer NovoTimer(TimerCallback cb, object estado, TimeSpan inicio, TimeSpan periodo) => Guardar(new Timer(Protegido(cb), estado, inicio, periodo));

        /// <summary>O fim da rodada: as threads saem no proximo ponto, os temporizadores param.</summary>
        public static void Encerrar()
        {
            Saindo = true;
            try { Fim.Cancel(); } catch { }
            lock (temporizadores)
            {
                foreach (var t in temporizadores) t.Dispose();
                temporizadores.Clear();
            }
        }
    }

    /// <summary>
    /// O Console e o Debug do programa: saem no monitor serial (na placa, a
    /// UART do Debug.WriteLine do nanoFramework), e o ReadLine le dele.
    /// </summary>
    public static class ConsoleDaPlaca
    {
        static void Sair(string s)
        {
            if (!string.IsNullOrEmpty(s)) Ponte.C("Serial", "print", s);
        }

        static string Formatar(string formato, object[] args) => args == null || args.Length == 0 ? formato : string.Format(CultureInfo.InvariantCulture, formato, args);

        public static void Write(object v) => Sair(Ponte.Texto(v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString()));
        public static void Write(string formato, params object[] args) => Sair(Formatar(formato, args));
        public static void Write(char[] v) => Sair(v == null ? "" : new string(v));
        public static void WriteLine() => Sair("\n");
        public static void WriteLine(object v)
        {
            Write(v);
            Sair("\n");
        }
        public static void WriteLine(string formato, params object[] args) => Sair(Formatar(formato, args) + "\n");
        public static void WriteLine(char[] v) => Sair((v == null ? "" : new string(v)) + "\n");

        public static int Read()
        {
            while (true)
            {
                long c = Ponte.I("Serial", "read");
                if (c >= 0) return (int)c;
                Ponte.Dormir(1);
            }
        }

        public static string ReadLine()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int c = Read();
                if (c == '\n') return sb.ToString();
                if (c != '\r') sb.Append((char)c);
            }
        }

        public static void Clear() { }
        public static void Flush() { }
    }

    public static class DebugDaPlaca
    {
        public static void Write(object v) => ConsoleDaPlaca.Write(v);
        public static void Write(string mensagem, string categoria) => ConsoleDaPlaca.Write(categoria + ": " + mensagem);
        public static void WriteLine() => ConsoleDaPlaca.WriteLine();
        public static void WriteLine(object v) => ConsoleDaPlaca.WriteLine(v);
        public static void WriteLine(string formato, params object[] args) => ConsoleDaPlaca.WriteLine(formato, args);
        public static void WriteLine(string mensagem, string categoria) => ConsoleDaPlaca.WriteLine(categoria + ": " + mensagem);
        public static void Print(string mensagem) => ConsoleDaPlaca.WriteLine(mensagem);
        public static void Print(string formato, params object[] args) => ConsoleDaPlaca.WriteLine(formato, args);
        public static void WriteIf(bool condicao, object v)
        {
            if (condicao) Write(v);
        }
        public static void WriteLineIf(bool condicao, object v)
        {
            if (condicao) WriteLine(v);
        }
        public static void Assert(bool condicao)
        {
            if (!condicao) throw new InvalidOperationException("Debug.Assert falhou");
        }
        public static void Assert(bool condicao, string mensagem)
        {
            if (!condicao) throw new InvalidOperationException("Debug.Assert falhou: " + mensagem);
        }
        public static void Flush() { }
    }
}
