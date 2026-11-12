using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Simulador.App;

/// <summary>
/// O estado do controle de video game, lido do XInput.
///
/// Os gatilhos vem como 0..255 e os manches como -32768..32767; aqui
/// viram 0..1 e -1..1, que e o que o resto do programa entende.
/// </summary>
public readonly record struct EstadoDoControle(
    bool Ligado, ushort Botoes, double Lt, double Rt,
    double Lx, double Ly, double Rx, double Ry)
{
    public static readonly EstadoDoControle Desligado = new();

    // Mascaras do XInput.
    public const ushort Cima = 0x0001, Baixo = 0x0002, Esquerda = 0x0004, Direita = 0x0008;
    public const ushort Start = 0x0010, Back = 0x0020;
    public const ushort L3 = 0x0040, R3 = 0x0080, Lb = 0x0100, Rb = 0x0200;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    public bool Apertado(ushort mascara) => (Botoes & mascara) != 0;
}

/// <summary>
/// Le o controle de video game pelo XInput -- o de Xbox e qualquer um
/// que se passe por ele, que hoje e quase todo controle de PC.
///
/// P/Invoke direto na DLL do Windows: nao ha pacote para instalar e o
/// XInput vem com o sistema. Sem controle ligado, a chamada devolve
/// "nao conectado" e o programa segue como se nada fosse -- o teclado
/// continua valendo.
/// </summary>
public static class Controle
{
    [StructLayout(LayoutKind.Sequential)]
    struct EstadoCru
    {
        public ushort Botoes;
        public byte GatilhoEsquerdo;
        public byte GatilhoDireito;
        public short Lx, Ly, Rx, Ry;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PacoteCru
    {
        public uint Pacote;
        public EstadoCru Controle;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int Ler14(int indice, out PacoteCru estado);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern int Ler91(int indice, out PacoteCru estado);

    /// <summary>Zona morta do manche: sem ela ele nunca fica em repouso.</summary>
    const double ZonaMorta = 0.24;

    static bool semXInput;

    /// <summary>
    /// Controle de mentira para o autoteste.
    ///
    /// Ninguem vai pendurar um controle de Xbox na maquina que roda o
    /// teste, e a parte que pode quebrar nao e a leitura do XInput: e o
    /// que o circuito faz com ela.
    /// </summary>
    internal static EstadoDoControle? Simulado { get; set; }

    /// <summary>
    /// O autoteste e o passeio nao leem o teclado nem o controle de
    /// verdade -- como ja nao mexem no radio Bluetooth da maquina. Com a
    /// janela deles na frente, quem estivesse digitando em outro programa
    /// apertaria os botoes do circuito: o "L" de qualquer palavra
    /// desligava a chave da demo do jogo no meio da suite. O controle
    /// fingido (<see cref="Simulado"/>) continua valendo.
    /// </summary>
    internal static bool SemDispositivos => Autoteste.Rodando || Passeio.Ativo;

    /// <summary>O primeiro controle conectado, ou "desligado".</summary>
    public static EstadoDoControle Ler()
    {
        if (Simulado is { } fingido) return fingido;
        if (semXInput || SemDispositivos) return EstadoDoControle.Desligado;

        for (int i = 0; i < 4; i++)
        {
            PacoteCru cru;
            int erro;
            try
            {
                erro = Ler14(i, out cru);
            }
            catch (DllNotFoundException)
            {
                try
                {
                    erro = Ler91(i, out cru);
                }
                catch (DllNotFoundException)
                {
                    // Windows sem XInput: nao adianta tentar de novo a
                    // cada quadro.
                    semXInput = true;
                    return EstadoDoControle.Desligado;
                }
            }
            catch (EntryPointNotFoundException)
            {
                semXInput = true;
                return EstadoDoControle.Desligado;
            }

            if (erro != 0) continue;       // 1167 = ERROR_DEVICE_NOT_CONNECTED

            return new EstadoDoControle(
                true, cru.Controle.Botoes,
                cru.Controle.GatilhoEsquerdo / 255.0,
                cru.Controle.GatilhoDireito / 255.0,
                Eixo(cru.Controle.Lx), Eixo(cru.Controle.Ly),
                Eixo(cru.Controle.Rx), Eixo(cru.Controle.Ry));
        }

        return EstadoDoControle.Desligado;
    }

    static double Eixo(short cru)
    {
        double v = cru / 32767.0;
        if (Math.Abs(v) < ZonaMorta) return 0;

        // Reescala o que sobrou: passando da zona morta o manche comeca
        // do zero de novo, em vez de pular para 0,24.
        double sinal = Math.Sign(v);
        return sinal * Math.Clamp((Math.Abs(v) - ZonaMorta) / (1 - ZonaMorta), 0, 1);
    }
}

/// <summary>
/// Atalho de teclado ou de controle ligado a uma acao de uma peca.
///
/// Serve para jogar com o circuito: o botao do desenho responde ao J, o
/// manche do joystick anda no WASD ou no manche esquerdo do controle de
/// Xbox. Sem isto, todo aperto pede um clique com o mouse -- e um
/// circuito de dois botoes ja e impossivel de testar sozinho.
///
/// O vinculo e guardado como TEXTO curto na peca ("t:J", "g:A"), e por
/// isso vai junto no .sktch sem formato novo nenhum.
/// </summary>
public static class Vinculos
{
    public const string Apertar = "apertar";
    public const string Alternar = "alternar";
    public const string Cima = "cima";
    public const string Baixo = "baixo";
    public const string Esquerda = "esquerda";
    public const string Direita = "direita";
    public const string Mais = "mais";
    public const string Menos = "menos";

    /// <summary>As acoes que cada tipo de peca aceita, na ordem da tela.</summary>
    public static IReadOnlyList<(string Acao, string Rotulo)> Acoes(string tipo) => tipo switch
    {
        "pushbutton" or "pushbutton-6mm" or "micro-switch" => [(Apertar, Idioma.T("Apertar"))],

        "analog-joystick" =>
        [
            (Cima, Idioma.T("Cima")), (Baixo, Idioma.T("Baixo")), (Esquerda, Idioma.T("Esquerda")),
            (Direita, Idioma.T("Direita")), (Apertar, Idioma.T("Apertar o manche")),
        ],

        "slide-switch" or "tilt-switch" or "pir-motion-sensor" => [(Alternar, Idioma.T("Ligar/desligar"))],

        "pendrive-usb" => [(Alternar, Idioma.T("Tirar/pôr o pendrive"))],

        "radio-controle" =>
        [
            (Mais, Idioma.T("Acelerar")), (Menos, Idioma.T("Reduzir o acelerador")),
            ("leme-esq", Idioma.T("Leme ←")), ("leme-dir", Idioma.T("Leme →")),
            (Esquerda, Idioma.T("Aileron ←")), (Direita, Idioma.T("Aileron →")),
            (Cima, Idioma.T("Profundor ↑")), (Baixo, Idioma.T("Profundor ↓")),
            ("chave-a", Idioma.T("Chave SWA")), ("chave-b", Idioma.T("Chave SWB")),
        ],

        "potentiometer" or "slide-potentiometer" =>
            [(Mais, Idioma.T("Aumentar")), (Menos, Idioma.T("Diminuir"))],

        // A roda anda um clique por toque; o botao dela e o OK.
        "ky-040" => [(Mais, Idioma.T("Girar para a direita")), (Menos, Idioma.T("Girar para a esquerda")), (Apertar, Idioma.T("Apertar a roda"))],

        _ => [],
    };

    public static bool Aceita(string tipo) => Acoes(tipo).Count > 0;

    // ---------------- teclado ----------------
    public static string DeTecla(Key tecla) => "t:" + tecla;

    static Key? TeclaDe(string vinculo)
        => vinculo is not null && vinculo.StartsWith("t:", StringComparison.Ordinal)
           && Enum.TryParse<Key>(vinculo[2..], out var k) ? k : null;

    // ---------------- controle ----------------
    /// <summary>Nome curto de cada controle do gamepad, na ordem da lista.</summary>
    public static readonly (string Codigo, string Rotulo)[] DoControle =
    [
        ("g:A", Idioma.T("Botão A")), ("g:B", Idioma.T("Botão B")), ("g:X", Idioma.T("Botão X")), ("g:Y", Idioma.T("Botão Y")),
        ("g:LB", "LB"), ("g:RB", "RB"), ("g:LT", Idioma.T("Gatilho LT")), ("g:RT", Idioma.T("Gatilho RT")),
        ("g:Start", "Start"), ("g:Back", "Back"),
        ("g:Cima", Idioma.T("Direcional ↑")), ("g:Baixo", Idioma.T("Direcional ↓")),
        ("g:Esquerda", Idioma.T("Direcional ←")), ("g:Direita", Idioma.T("Direcional →")),
        ("g:L3", Idioma.T("Apertar o manche esquerdo")), ("g:R3", Idioma.T("Apertar o manche direito")),
        ("g:LSCima", Idioma.T("Manche esquerdo ↑")), ("g:LSBaixo", Idioma.T("Manche esquerdo ↓")),
        ("g:LSEsquerda", Idioma.T("Manche esquerdo ←")), ("g:LSDireita", Idioma.T("Manche esquerdo →")),
        ("g:RSCima", Idioma.T("Manche direito ↑")), ("g:RSBaixo", Idioma.T("Manche direito ↓")),
        ("g:RSEsquerda", Idioma.T("Manche direito ←")), ("g:RSDireita", Idioma.T("Manche direito →")),
    ];

    /// <summary>Quanto o controle esta acionando este vinculo, de 0 a 1.</summary>
    static double NoControle(string vinculo, EstadoDoControle c)
    {
        if (!c.Ligado || vinculo is null
            || !vinculo.StartsWith("g:", StringComparison.Ordinal)) return 0;

        // Gatilho e manche sao analogicos: devolvem o quanto, e nao so
        // o se. E o que faz o manche do Xbox virar posicao do joystick
        // do circuito em vez de um liga-desliga.
        return vinculo[2..] switch
        {
            "A" => c.Apertado(EstadoDoControle.A) ? 1 : 0,
            "B" => c.Apertado(EstadoDoControle.B) ? 1 : 0,
            "X" => c.Apertado(EstadoDoControle.X) ? 1 : 0,
            "Y" => c.Apertado(EstadoDoControle.Y) ? 1 : 0,
            "LB" => c.Apertado(EstadoDoControle.Lb) ? 1 : 0,
            "RB" => c.Apertado(EstadoDoControle.Rb) ? 1 : 0,
            "L3" => c.Apertado(EstadoDoControle.L3) ? 1 : 0,
            "R3" => c.Apertado(EstadoDoControle.R3) ? 1 : 0,
            "Start" => c.Apertado(EstadoDoControle.Start) ? 1 : 0,
            "Back" => c.Apertado(EstadoDoControle.Back) ? 1 : 0,
            "Cima" => c.Apertado(EstadoDoControle.Cima) ? 1 : 0,
            "Baixo" => c.Apertado(EstadoDoControle.Baixo) ? 1 : 0,
            "Esquerda" => c.Apertado(EstadoDoControle.Esquerda) ? 1 : 0,
            "Direita" => c.Apertado(EstadoDoControle.Direita) ? 1 : 0,

            "LT" => c.Lt,
            "RT" => c.Rt,

            "LSCima" => Math.Max(0, c.Ly),
            "LSBaixo" => Math.Max(0, -c.Ly),
            "LSEsquerda" => Math.Max(0, -c.Lx),
            "LSDireita" => Math.Max(0, c.Lx),
            "RSCima" => Math.Max(0, c.Ry),
            "RSBaixo" => Math.Max(0, -c.Ry),
            "RSEsquerda" => Math.Max(0, -c.Rx),
            "RSDireita" => Math.Max(0, c.Rx),
            _ => 0,
        };
    }

    /// <summary>
    /// Quanto este vinculo esta acionado agora, de 0 a 1.
    ///
    /// Tecla e liga-desliga; gatilho e manche sao analogicos. Devolver
    /// a forca em vez de um booleano e o que permite empurrar o manche
    /// do circuito "so um pouco" com o manche do controle.
    /// </summary>
    public static double Forca(string vinculo, EstadoDoControle controle)
    {
        if (string.IsNullOrEmpty(vinculo)) return 0;

        // "t:Up|g:Cima": a seta OU o direcional do controle -- vale o que
        // estiver mais apertado. E como a demo do jogo vem ligada nos dois.
        if (vinculo.Contains('|'))
            return vinculo.Split('|').Max(parte => Forca(parte, controle));

        if (TeclaDe(vinculo) is { } tecla)
            return !Controle.SemDispositivos && Keyboard.IsKeyDown(tecla) ? 1 : 0;
        return NoControle(vinculo, controle);
    }

    /// <summary>O nome que aparece na tela para um vinculo.</summary>
    public static string Rotulo(string vinculo)
    {
        if (string.IsNullOrEmpty(vinculo)) return "—";
        if (vinculo.Contains('|')) return string.Join(" " + Idioma.T("ou") + " ", vinculo.Split('|').Select(Rotulo));
        if (TeclaDe(vinculo) is { } tecla) return NomeDaTecla(tecla);

        foreach (var (codigo, rotulo) in DoControle)
            if (codigo == vinculo) return rotulo;
        return vinculo;
    }

    /// <summary>Nome curto e legivel de uma tecla ("J", "Espaço", "Enter").</summary>
    public static string NomeDaTecla(Key tecla) => tecla switch
    {
        Key.Space => Idioma.T("Espaço"),
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        >= Key.D0 and <= Key.D9 => ((char)('0' + (tecla - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (tecla - Key.NumPad0),
        _ => tecla.ToString(),
    };

    /// <summary>
    /// Teclas que NAO servem de vinculo.
    ///
    /// Sao as que o programa precisa mesmo durante a simulacao: parar
    /// (F5), depurar (F6/F9/F10) e sair do que estiver aberto (Esc).
    /// </summary>
    public static bool Reservada(Key tecla)
        => tecla is Key.F5 or Key.F6 or Key.F9 or Key.F10 or Key.Escape
                 or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.System or Key.Tab;
}
