using System.Globalization;
using System.Text;

namespace Simulador;

/// <summary>
/// O lado do simulador para o que o C chama: serial, relogio, semaforos,
/// filas, grupos de eventos, pinos, panico, NVS e numeros.
/// </summary>
public sealed unsafe partial class ModuloNativo
{
    // =====================================================================
    //  objetos (semaforos, filas, arquivos...) por numero
    // =====================================================================

    readonly Dictionary<long, object> objetos = [];
    long proximoObjeto = 1;

    long Guardar(object o)
    {
        lock (objetos)
        {
            long id = proximoObjeto++;
            objetos[id] = o;
            return id;
        }
    }

    T Obj<T>(long id) where T : class
    {
        lock (objetos) return objetos.TryGetValue(id, out var o) ? o as T : null;
    }

    internal void Apagar(long id)
    {
        object o;
        lock (objetos) objetos.Remove(id, out o);
        if (o is not null) lock (o) Monitor.PulseAll(o);
    }

    /// <summary>
    /// Espera ate pronto() (com a trava do objeto ja na mao). 1 quando ficou
    /// pronto, 0 quando o tempo acabou, -1 quando a tarefa tem que sair.
    /// Espera negativa e para sempre.
    /// </summary>
    int Esperar(object trava, Func<bool> pronto, long esperaUs)
    {
        if (pronto()) return 1;
        if (esperaUs == 0) return 0;
        var ctx = contexto;
        long limite = esperaUs < 0 ? long.MaxValue : Micros() + esperaUs;
        while (true)
        {
            if (encerrando || ctx is null || ctx.Encerrar) return -1;
            long agora = Micros();
            if (agora >= limite) return 0;
            int fatia = (int)Math.Clamp((limite - agora) / 1000, 1, 20);
            Monitor.Wait(trava, fatia);
            if (pronto()) return 1;
            while (Maquina.PausaPedida && !encerrando) Monitor.Wait(trava, 20);
        }
    }

    // =====================================================================
    //  serial
    // =====================================================================

    readonly List<byte> linhaPendente = [];

    internal void EscreverSerial(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return;
        lock (linhaPendente)
        {
            foreach (byte b in bytes)
            {
                if (b == (byte)'\n')
                {
                    if (linhaPendente.Count > 0 && linhaPendente[^1] == (byte)'\r') linhaPendente.RemoveAt(linhaPendente.Count - 1);
                    string linha = TextoArduino.Corrigir(TextoArduino.DeBytes([.. linhaPendente]));
                    linhaPendente.Clear();
                    Maquina.EscreverSerial($"[{Maquina.Ms,7}ms] {linha}");
                }
                else if (linhaPendente.Count < 4096)
                {
                    linhaPendente.Add(b);
                }
            }
        }
        if (Maquina.BaudDaSerial > 0) Maquina.EnviarPelaSerial(TextoArduino.DeBytes(bytes.ToArray()));
    }

    // =====================================================================
    //  relogio
    // =====================================================================

    internal long Micros() => (long)(Maquina.MsPreciso * 1000);

    internal int Dormir(long us)
    {
        var ctx = contexto;
        long alvo = Micros() + Math.Max(0, us);
        while (true)
        {
            if (encerrando || ctx is null || ctx.Encerrar) return -1;
            if (Maquina.PausaPedida)
            {
                ctx.Acordar.WaitOne(20);
                continue;
            }
            long falta = alvo - Micros();
            if (falta <= 0) return 0;
            if (falta >= 1500) ctx.Acordar.WaitOne((int)Math.Min(falta / 1000, 20));
            else if (falta > 300) Thread.Sleep(0);
            else Thread.SpinWait(50);
        }
    }

    // =====================================================================
    //  semaforos
    // =====================================================================

    sealed class Semaforo
    {
        public int Tipo, Maximo, Contagem, Recursao;
        public IntPtr Dono;
    }

    internal long SemCriar(int tipo, int maximo, int inicial)
        => Guardar(new Semaforo { Tipo = tipo, Maximo = Math.Max(1, maximo), Contagem = Math.Clamp(inicial, 0, Math.Max(1, maximo)) });

    internal int SemPegar(long id, long espera)
    {
        var s = Obj<Semaforo>(id);
        if (s is null) return espera == 0 ? 0 : (encerrando ? -1 : 0);
        IntPtr eu = contexto?.Tarefa ?? IntPtr.Zero;
        lock (s)
        {
            if (s.Tipo == 2 && s.Dono == eu && s.Dono != IntPtr.Zero && s.Contagem == 0)
            {
                s.Recursao++;
                return 1;
            }
            int r = s.Contagem > 0 ? 1 : Esperar(s, () => s.Contagem > 0, espera);
            if (r == 1)
            {
                s.Contagem--;
                if (s.Tipo != 0)
                {
                    s.Dono = eu;
                    s.Recursao = 1;
                }
            }
            return r;
        }
    }

    internal int SemDar(long id)
    {
        var s = Obj<Semaforo>(id);
        if (s is null) return 0;
        IntPtr eu = contexto?.Tarefa ?? IntPtr.Zero;
        lock (s)
        {
            if (s.Tipo != 0)
            {
                if (s.Dono != eu) return 0;
                if (--s.Recursao > 0) return 1;
                s.Dono = IntPtr.Zero;
            }
            if (s.Contagem >= s.Maximo) return 0;
            s.Contagem++;
            Monitor.PulseAll(s);
            return 1;
        }
    }

    internal int SemContagem(long id)
    {
        var s = Obj<Semaforo>(id);
        if (s is null) return 0;
        lock (s) return s.Contagem;
    }

    internal IntPtr SemDono(long id)
    {
        var s = Obj<Semaforo>(id);
        if (s is null) return IntPtr.Zero;
        lock (s) return s.Dono;
    }

    // =====================================================================
    //  filas
    // =====================================================================

    sealed class Fila
    {
        public int Tamanho, Item, Inicio, N;
        public byte[] Dados;
    }

    internal long FilaCriar(int tamanho, int item)
    {
        if (tamanho <= 0 || item < 0) return 0;
        return Guardar(new Fila { Tamanho = tamanho, Item = item, Dados = new byte[Math.Max(1, tamanho * Math.Max(item, 1))] });
    }

    internal int FilaEnviar(long id, IntPtr item, long espera, int modo)
    {
        var f = Obj<Fila>(id);
        if (f is null) return 0;
        lock (f)
        {
            if (modo == 2 && f.N == f.Tamanho)
            {
                // xQueueOverwrite: troca o que esta la (a fila e de um item).
                int ultimo = (f.Inicio + f.N - 1) % f.Tamanho;
                Copiar(item, f, ultimo);
                Monitor.PulseAll(f);
                return 1;
            }
            int r = f.N < f.Tamanho ? 1 : Esperar(f, () => f.N < f.Tamanho, espera);
            if (r != 1) return r;
            int vaga;
            if (modo == 1)
            {
                f.Inicio = (f.Inicio - 1 + f.Tamanho) % f.Tamanho;
                vaga = f.Inicio;
            }
            else
            {
                vaga = (f.Inicio + f.N) % f.Tamanho;
            }
            Copiar(item, f, vaga);
            f.N++;
            Monitor.PulseAll(f);
            return 1;
        }
    }

    static void Copiar(IntPtr item, Fila f, int vaga)
    {
        if (f.Item == 0 || item == IntPtr.Zero) return;
        new ReadOnlySpan<byte>((void*)item, f.Item).CopyTo(f.Dados.AsSpan(vaga * f.Item, f.Item));
    }

    internal int FilaReceber(long id, IntPtr item, long espera, bool espiar)
    {
        var f = Obj<Fila>(id);
        if (f is null) return 0;
        lock (f)
        {
            int r = f.N > 0 ? 1 : Esperar(f, () => f.N > 0, espera);
            if (r != 1) return r;
            if (f.Item > 0 && item != IntPtr.Zero)
                f.Dados.AsSpan(f.Inicio * f.Item, f.Item).CopyTo(new Span<byte>((void*)item, f.Item));
            if (!espiar)
            {
                f.Inicio = (f.Inicio + 1) % f.Tamanho;
                f.N--;
                Monitor.PulseAll(f);
            }
            return 1;
        }
    }

    internal int FilaContagem(long id)
    {
        var f = Obj<Fila>(id);
        if (f is null) return 0;
        lock (f) return f.N;
    }

    internal int FilaLivre(long id)
    {
        var f = Obj<Fila>(id);
        if (f is null) return 0;
        lock (f) return f.Tamanho - f.N;
    }

    internal void FilaLimpar(long id)
    {
        var f = Obj<Fila>(id);
        if (f is null) return;
        lock (f)
        {
            f.N = 0;
            f.Inicio = 0;
            Monitor.PulseAll(f);
        }
    }

    // =====================================================================
    //  grupos de eventos
    // =====================================================================

    sealed class Grupo
    {
        public uint Bits;
    }

    internal long GrupoCriar() => Guardar(new Grupo());

    internal uint GrupoMudar(long id, uint ligar, uint desligar)
    {
        var g = Obj<Grupo>(id);
        if (g is null) return 0;
        lock (g)
        {
            uint antes = g.Bits;
            g.Bits = (g.Bits | ligar) & ~desligar;
            if (ligar != 0) Monitor.PulseAll(g);
            return desligar != 0 && ligar == 0 ? antes : g.Bits;
        }
    }

    internal uint GrupoEsperar(long id, uint bits, bool limpar, bool todos, long espera, out bool parar)
    {
        parar = false;
        var g = Obj<Grupo>(id);
        if (g is null) return 0;
        lock (g)
        {
            bool Satisfeito() => todos ? (g.Bits & bits) == bits : (g.Bits & bits) != 0;
            int r = Satisfeito() ? 1 : Esperar(g, Satisfeito, espera);
            parar = r < 0;
            uint valor = g.Bits;
            if (r == 1 && limpar) g.Bits &= ~bits;
            return valor;
        }
    }

    // =====================================================================
    //  pinos
    // =====================================================================

    readonly object travaDosPinos = new();
    readonly int[] vigiaTipo = new int[Maquina.NumPinos];
    readonly int[] vigiaNivel = new int[Maquina.NumPinos];
    readonly Queue<int> bordas = new();

    static bool PinoValido(int p) => p >= 0 && p < Maquina.NumPinos;

    internal void GpioModo(int p, int modo)
    {
        if (!PinoValido(p)) return;
        int m = (modo & 2) != 0 ? 1 : (modo & 4) != 0 ? 2 : 0;
        lock (travaDosPinos) Maquina.PinMode(p, m);
    }

    internal void GpioEscrever(int p, int nivel)
    {
        if (!PinoValido(p)) return;
        nivel = nivel != 0 ? 1 : 0;
        lock (travaDosPinos)
        {
            int antes = Maquina.DigitalRead(p);
            Maquina.DigitalWrite(p, nivel);
            if (vigiaTipo[p] != 0 && antes != nivel) AnotarBorda(p, antes, nivel);
        }
    }

    internal int GpioLer(int p)
    {
        if (!PinoValido(p)) return 0;
        lock (travaDosPinos) return Maquina.DigitalRead(p);
    }

    internal void GpioInterrupcao(int p, int tipo)
    {
        if (!PinoValido(p)) return;
        lock (travaDosPinos)
        {
            vigiaTipo[p] = tipo;
            vigiaNivel[p] = Maquina.DigitalRead(p);
        }
    }

    void AnotarBorda(int p, int antes, int agora)
    {
        vigiaNivel[p] = agora;
        bool vale = vigiaTipo[p] switch
        {
            1 => antes == 0 && agora == 1,
            2 => antes == 1 && agora == 0,
            3 => true,
            4 => agora == 0,
            5 => agora == 1,
            _ => false,
        };
        if (vale) lock (bordas) if (bordas.Count < 1024) bordas.Enqueue(p);
    }

    /// <summary>O que o circuito mudou por fora (botao, chave): compara com o ultimo nivel visto.</summary>
    void ProcurarBordas()
    {
        lock (travaDosPinos)
        {
            for (int p = 0; p < vigiaTipo.Length; p++)
            {
                if (vigiaTipo[p] == 0) continue;
                int agora = Maquina.DigitalRead(p);
                if (agora != vigiaNivel[p]) AnotarBorda(p, vigiaNivel[p], agora);
                else if ((vigiaTipo[p] == 4 && agora == 0) || (vigiaTipo[p] == 5 && agora == 1))
                    lock (bordas) if (bordas.Count == 0) bordas.Enqueue(p);
            }
        }
    }

    internal int GpioProximaBorda(long espera)
    {
        var ctx = contexto;
        long limite = espera < 0 ? long.MaxValue : Micros() + espera;
        while (true)
        {
            lock (bordas) if (bordas.Count > 0) return bordas.Dequeue();
            ProcurarBordas();
            lock (bordas) if (bordas.Count > 0) return bordas.Dequeue();
            if (encerrando || ctx is null || ctx.Encerrar) return -2;
            if (Micros() >= limite) return -1;
            ctx.Acordar.WaitOne(1);
        }
    }

    // =====================================================================
    //  a placa
    // =====================================================================

    int panicos;

    internal int RazaoDoReinicio => razaoDoReinicio;

    /// <summary>
    /// Panico: a mensagem que a ESP32 mostraria e a placa reinicia. So o
    /// primeiro conta -- quando uma tarefa cai, as outras costumam cair atras.
    /// </summary>
    internal void Panico(string motivo, string detalhe)
    {
        if (Interlocked.Exchange(ref panicos, 1) != 0) return;
        var linhas = new List<string>();
        if (motivo == "abort()")
        {
            if (!string.IsNullOrEmpty(detalhe)) linhas.Add(detalhe);
            linhas.Add("");
            linhas.Add("abort() was called at PC 0x42000000 on core 0");
        }
        else if (motivo == "stack overflow")
        {
            linhas.Add(detalhe ?? "***ERROR*** A stack overflow has been detected.");
        }
        else
        {
            linhas.Add($"Guru Meditation Error: Core  0 panic'ed ({motivo}). Exception was unhandled.");
            if (!string.IsNullOrEmpty(detalhe)) linhas.Add("(simulador) " + detalhe);
        }
        linhas.Add("");
        linhas.Add("Rebooting...");
        foreach (string l in linhas) EscreverSerial(Encoding.UTF8.GetBytes(l + "\n"));
        PedirReinicio(4);
    }

    internal void PedirReinicio(int razao)
    {
        Maquina.PedirReinicio(new ReinicioDaPlaca(razao, 0, 0, new Dictionary<string, object>()));
        encerrando = true;
        if (parando != null) *parando = 1;
    }

    internal int NvsGravar(string espaco, string chave, int tipo, byte[] dados)
    {
        lock (Maquina.Nvs) Maquina.Nvs[(espaco ?? "") + "\0" + (chave ?? "")] = (tipo, dados);
        return 0;
    }

    internal int NvsLer(string espaco, string chave, out int tipo, out byte[] dados)
    {
        lock (Maquina.Nvs)
        {
            if (Maquina.Nvs.TryGetValue((espaco ?? "") + "\0" + (chave ?? ""), out var v))
            {
                tipo = v.Tipo;
                dados = v.Dados;
                return dados.Length;
            }
        }
        tipo = 0;
        dados = null;
        return -1;
    }

    internal int NvsApagar(string espaco, string chave)
    {
        lock (Maquina.Nvs)
        {
            if (espaco is null)
            {
                Maquina.Nvs.Clear();
                return 0;
            }
            string prefixo = espaco + "\0";
            if (chave is null)
            {
                foreach (string k in Maquina.Nvs.Keys.Where(k => k.StartsWith(prefixo, StringComparison.Ordinal)).ToList())
                    Maquina.Nvs.Remove(k);
                return 0;
            }
            return Maquina.Nvs.Remove(prefixo + chave) ? 0 : -1;
        }
    }

    // =====================================================================
    //  numeros
    // =====================================================================

    internal static double Matematica(int qual, double a, double b) => qual switch
    {
        0 => Math.Sin(a),
        1 => Math.Cos(a),
        2 => Math.Tan(a),
        3 => Math.Asin(a),
        4 => Math.Acos(a),
        5 => Math.Atan(a),
        6 => Math.Atan2(a, b),
        7 => Math.Sinh(a),
        8 => Math.Cosh(a),
        9 => Math.Tanh(a),
        10 => Math.Exp(a),
        11 => Math.Log(a),
        12 => Math.Log10(a),
        13 => Math.Pow(a, b),
        14 => Math.Sqrt(a),
        15 => Math.Cbrt(a),
        16 => Math.Pow(2, a),
        17 => Math.Log2(a),
        18 => Hipotenusa(a, b),
        19 => Math.Abs(a) < 1e-5 ? a + a * a / 2 + a * a * a / 6 : Math.Exp(a) - 1,
        20 => Math.Abs(a) < 1e-5 ? a - a * a / 2 + a * a * a / 3 : Math.Log(1 + a),
        21 => Math.Asinh(a),
        22 => Math.Acosh(a),
        23 => Math.Atanh(a),
        24 => a % b,
        25 => Math.IEEERemainder(a, b),
        26 => Erf(a),
        27 => 1 - Erf(a),
        28 => Gama(a),
        29 => Math.Log(Math.Abs(Gama(a))),
        _ => double.NaN,
    };

    static double Hipotenusa(double a, double b)
    {
        if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
        a = Math.Abs(a);
        b = Math.Abs(b);
        double maior = Math.Max(a, b), menor = Math.Min(a, b);
        if (maior == 0) return 0;
        double r = menor / maior;
        return maior * Math.Sqrt(1 + r * r);
    }

    static double Erf(double x)
    {
        // Abramowitz-Stegun 7.1.26 refinada: erro < 1.2e-7, suficiente para o float da placa.
        double t = 1 / (1 + 0.5 * Math.Abs(x));
        double y = 1 - t * Math.Exp(-x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418
                   + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587
                   + t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? y : -y;
    }

    static double Gama(double x)
    {
        if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gama(1 - x));
        x -= 1;
        double[] g = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
                      -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
                      1.5056327351493116e-7];
        double a = g[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++) a += g[i] / (x + i);
        return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
    }

    /// <summary>
    /// O numero como o printf da newlib o escreve: %f, %e, %g e %a, com a
    /// precisao (-1 = a padrao) e o "#". Sem largura nem sinal de "+": isso
    /// o lado C faz.
    /// </summary>
    internal static string FormatarReal(double v, char tipo, int precisao, bool alternativo)
    {
        var inv = CultureInfo.InvariantCulture;
        if (double.IsNaN(v)) return double.IsNegative(v) ? "-nan" : "nan";
        if (double.IsInfinity(v)) return v < 0 ? "-inf" : "inf";

        switch (tipo)
        {
            case 'f':
            {
                int p = precisao < 0 ? 6 : precisao;
                string s = v.ToString("F" + p, inv);
                if (p == 0 && alternativo) s += ".";
                return s;
            }
            case 'e':
                return Exponencial(v, precisao < 0 ? 6 : precisao, alternativo);
            case 'g':
            {
                int p = precisao < 0 ? 6 : precisao == 0 ? 1 : precisao;
                string e = v.ToString("E" + (p - 1), inv);
                int x = int.Parse(e[(e.IndexOf('E') + 1)..], inv);
                string s = (p > x && x >= -4)
                    ? v.ToString("F" + Math.Max(0, p - 1 - x), inv)
                    : Exponencial(v, p - 1, alternativo);
                if (!alternativo) s = TirarZeros(s);
                else if (!s.Contains('.') && !s.Contains('e')) s += ".";
                return s;
            }
            case 'a':
                return Hexadecimal(v, precisao);
        }
        return v.ToString("R", inv);
    }

    static string Exponencial(double v, int p, bool alternativo)
    {
        string e = v.ToString("E" + p, CultureInfo.InvariantCulture);
        int corte = e.IndexOf('E');
        string mantissa = e[..corte];
        int x = int.Parse(e[(corte + 1)..], CultureInfo.InvariantCulture);
        if (p == 0 && alternativo) mantissa += ".";
        return mantissa + (x < 0 ? "e-" : "e+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
    }

    static string TirarZeros(string s)
    {
        int e = s.IndexOf('e');
        string numero = e >= 0 ? s[..e] : s;
        string resto = e >= 0 ? s[e..] : "";
        if (numero.Contains('.')) numero = numero.TrimEnd('0').TrimEnd('.');
        return numero + resto;
    }

    static string Hexadecimal(double v, int precisao)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        bool negativo = bits < 0;
        int expoente = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        string sinal = negativo ? "-" : "";
        if (expoente == 0 && mantissa == 0) return sinal + "0x0p+0";
        int lider = 1;
        if (expoente == 0)
        {
            lider = 0;
            expoente = -1022;
        }
        else
        {
            expoente -= 1023;
        }
        string digitos = mantissa.ToString("x13", CultureInfo.InvariantCulture);
        digitos = precisao < 0 ? digitos.TrimEnd('0') : digitos.PadRight(precisao, '0')[..Math.Min(precisao, 13)];
        return $"{sinal}0x{lider}{(digitos.Length > 0 ? "." + digitos : "")}p{(expoente < 0 ? "-" : "+")}{Math.Abs(expoente)}";
    }

    /// <summary>O strtod: le o que der a partir do comeco e diz quanto consumiu.</summary>
    internal static double LerReal(byte* s, out int consumidos)
    {
        consumidos = 0;
        if (s == null) return 0;
        int i = 0;
        while (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r')) i++;
        int inicio = i;
        bool negativo = false;
        if (s[i] == '+' || s[i] == '-')
        {
            negativo = s[i] == '-';
            i++;
        }

        bool Palavra(int em, string p)
        {
            for (int k = 0; k < p.Length; k++)
                if (char.ToLowerInvariant((char)s[em + k]) != p[k]) return false;
            return true;
        }

        if (Palavra(i, "inf"))
        {
            i += Palavra(i, "infinity") ? 8 : 3;
            consumidos = i;
            return negativo ? double.NegativeInfinity : double.PositiveInfinity;
        }
        if (Palavra(i, "nan"))
        {
            i += 3;
            if (s[i] == '(')
            {
                int k = i + 1;
                while (char.IsAsciiLetterOrDigit((char)s[k]) || s[k] == '_') k++;
                if (s[k] == ')') i = k + 1;
            }
            consumidos = i;
            return double.NaN;
        }

        // hexadecimal: 0x1.8p3
        if (s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            int k = i + 2;
            double valor = 0;
            bool algum = false;
            int escala = 0;
            while (Uri.IsHexDigit((char)s[k])) { valor = valor * 16 + Convert.ToInt32(((char)s[k]).ToString(), 16); k++; algum = true; }
            if (s[k] == '.')
            {
                k++;
                while (Uri.IsHexDigit((char)s[k])) { valor = valor * 16 + Convert.ToInt32(((char)s[k]).ToString(), 16); escala -= 4; k++; algum = true; }
            }
            if (!algum)
            {
                consumidos = i + 1;
                return negativo ? -0.0 : 0.0;
            }
            if (s[k] == 'p' || s[k] == 'P')
            {
                int j = k + 1;
                bool neg = false;
                if (s[j] == '+' || s[j] == '-') { neg = s[j] == '-'; j++; }
                if (s[j] >= '0' && s[j] <= '9')
                {
                    int e = 0;
                    while (s[j] >= '0' && s[j] <= '9') { e = Math.Min(e * 10 + (s[j] - '0'), 100000); j++; }
                    escala += neg ? -e : e;
                    k = j;
                }
            }
            consumidos = k;
            double r = Math.ScaleB(valor, escala);
            return negativo ? -r : r;
        }

        int digitos = 0;
        int j2 = i;
        while (s[j2] >= '0' && s[j2] <= '9') { j2++; digitos++; }
        if (s[j2] == '.')
        {
            j2++;
            while (s[j2] >= '0' && s[j2] <= '9') { j2++; digitos++; }
        }
        if (digitos == 0) return 0;
        if (s[j2] == 'e' || s[j2] == 'E')
        {
            int k = j2 + 1;
            if (s[k] == '+' || s[k] == '-') k++;
            if (s[k] >= '0' && s[k] <= '9')
            {
                while (s[k] >= '0' && s[k] <= '9') k++;
                j2 = k;
            }
        }
        string texto = Encoding.ASCII.GetString(s + inicio, j2 - inicio);
        consumidos = j2;
        return double.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
