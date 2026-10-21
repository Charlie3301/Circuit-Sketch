namespace Simulador;

/// <summary>
/// O <c>Wire</c> do sketch: o barramento I2C do lado da linguagem.
///
/// Foi o erro que derrubou o painel de carro do professor na linha 89 --
/// <c>Wire.begin(21, 22)</c>. Quase toda peca de duas vias passa por
/// aqui: modulo do LCD, MPU6050, relogio DS1307, OLED. Sem o Wire, elas
/// existiam no catalogo e eram inalcancaveis pelo codigo.
///
/// A conta de sempre: simular no nivel da BIBLIOTECA. O sketch nunca ve
/// o START nem o ACK bit a bit -- ve quem respondeu, os bytes que foram
/// e os que voltaram. E o que o <see cref="BarramentoI2c"/> entrega, e e
/// o bastante para o scanner de I2C, que e o exercicio classico.
///
/// Os chips com dois controladores tem o <c>Wire1</c> (e o
/// <c>TwoWire(1)</c> da ESP32, o <c>TwoWire Wire2(PB11, PB10)</c> da
/// STM32): cada peca responde so no barramento em que o fio dela chega.
/// </summary>
public partial class Interpretador
{
    /// <summary>Endereco de fabrica de cada peca de I2C que o simulador tem.</summary>
    public static int EnderecoPadraoDe(string classe) => classe switch
    {
        "LiquidCrystal_I2C" => 0x27,
        "Adafruit_SSD1306" or "Adafruit_SH1106G" => 0x3C,
        "Adafruit_MPU6050" or "MPU6050" => 0x68,
        "RTC_DS1307" => 0x68,
        _ => -1,
    };

    /// <summary>O aparelho que cada objeto do sketch pendurou: o begin(&amp;Wire1) muda o barramento dele.</summary>
    readonly Dictionary<Objeto, DispositivoI2c> aparelhoDoObjeto = [];

    /// <summary>
    /// Pendura no barramento o aparelho de um objeto recem-declarado.
    ///
    /// So sem circuito (o teste de motor): um sketch que declara o LCD e
    /// depois procura o 0x27 com o Wire tem que achar. Com circuito, quem
    /// responde e a peca da tela, pelo fio dela -- o objeto declarado sem a
    /// peca ligada nao acha ninguem, como na bancada.
    /// </summary>
    void RegistrarNoI2c(Objeto obj, int enderecoDito = -1, int barramento = -1)
    {
        int endereco = enderecoDito >= 0 ? enderecoDito : EnderecoPadraoDe(obj.Classe);
        if (endereco < 0 || maq.I2cPeloCircuito) return;
        if (aparelhoDoObjeto.Remove(obj, out var antigo) && antigo.PinoSda < 0) maq.I2c.Desligar(antigo);
        aparelhoDoObjeto[obj] = maq.I2c.Ligar(new DispositivoI2c(endereco, obj.Classe) { Barramento = barramento });
    }

    /// <summary>
    /// O sketch passou o barramento para a biblioteca ("&Wire1", o TwoWire
    /// declarado): qual e. Nulo: o argumento nao e barramento.
    /// </summary>
    BarramentoI2c BarramentoDoValor(Valor v, int linha)
        => v.EhTexto && v.Texto is "Wire" or "Wire1" ? BarramentoI2cDe(v.Texto, linha)
           : v.Referencia is Objeto { Rede: BarramentoI2c b } ? b : null;

    static string NomeDoBarramento(BarramentoI2c b) => b.Numero == 0 ? "Wire" : "Wire1";

    /// <summary>
    /// O begin() de um sensor de I2C (MPU6050, RTC): a biblioteca liga o
    /// barramento que recebeu -- o Wire, se nenhum -- e pergunta se ha alguem
    /// no endereco. Sem a peca no fio devolve false: o "Couldn't find RTC" do
    /// exemplo, como na bancada.
    /// </summary>
    bool SensorNoI2c(Objeto obj, List<Valor> args, int linha, bool enderecoNoArgumento)
    {
        int endereco = obj.Campos.TryGetValue("_endereco", out var doConstrutor) ? (int)doConstrutor.Num : EnderecoPadraoDe(obj.Classe);
        BarramentoI2c bus = null;
        bool primeiro = true;
        foreach (var a in args)
        {
            if (BarramentoDoValor(a, linha) is { } b) bus ??= b;
            else if (enderecoNoArgumento && primeiro && !a.EhTexto && a.Num is > 0 and < 128) endereco = (int)a.Num;
            primeiro = false;
        }
        bus ??= BarramentoI2cDe("Wire", linha);
        RegistrarNoI2c(obj, endereco, bus.Numero);
        if (!ComecarBarramento(bus, NomeDoBarramento(bus))) return false;
        var quem = bus.Achar(endereco);
        if (quem is null) AvisarForaDoFio(bus, NomeDoBarramento(bus), endereco);
        // O MPU6050 confere o WHO_AM_I: o DS1307, que tambem e 0x68, responde
        // ao endereco mas nao passa. O RTClib so confere o ACK.
        if (quem is { PinoSda: >= 0 } && obj.Classe is "Adafruit_MPU6050" or "MPU6050"
            && !quem.Nome.Contains("MPU", StringComparison.OrdinalIgnoreCase)) return false;
        return quem is not null;
    }

    /// <summary>O core tem o Wire1? ESP32, S2, S3, H2 e C6 (o LP_I2C) no core 3.3; a Pico (o i2c1).</summary>
    bool TemWire1 => EhFamiliaEsp32
        ? ModuloNativo.Chip(maq.Perfil?.Id ?? "") != "esp32c3"
        : Chip == "RP2040" && maq.Perfil?.Id != "nano-rp2040-connect";

    /// <summary>Pinos de fabrica do Wire1: so a C6 (SDA1/SCL1 do LP_I2C) e a Pico (GP26/GP27).</summary>
    (int Sda, int Scl)? PinosDoWire1 => EhFamiliaEsp32
        ? ModuloNativo.Chip(maq.Perfil?.Id ?? "") == "esp32c6" ? (6, 7) : null
        : Chip == "RP2040" ? (26, 27) : null;

    /// <summary>O buffer da Wire de cada core: 32 no AVR e na STM32, 128 na ESP32 e na ESP8266, 256 na Pico.</summary>
    int BufferDaWire => EhFamiliaEsp32 || Chip == "ESP8266" ? 128 : Chip == "RP2040" ? 256 : 32;

    /// <summary>O begin(sda, scl) existe so na ESP32, na ESP8266 e na STM32; no Uno e na Pico nao compila.</summary>
    bool WireComPinos => EhFamiliaEsp32 || Chip == "ESP8266" || Chip.StartsWith("STM32", StringComparison.Ordinal);

    /// <summary>
    /// O barramento de um nome do sketch: "Wire" e "Wire1". O Wire1 so nos
    /// chips com dois controladores -- no Uno e na C3 o compilador recusa.
    /// </summary>
    BarramentoI2c BarramentoI2cDe(string nome, int linha)
    {
        var b = nome switch
        {
            "Wire" => maq.I2c,
            "Wire1" when TemWire1 => maq.I2c1,
            _ => throw new ErroDeExecucao(
                $"'{nome}' was not declared in this scope -- a {maq.Perfil?.Nome} tem um barramento I2C so (o Wire)."
                + (EhAvr ? " Para um segundo no Uno, so por software (a biblioteca SoftwareWire)." : ""), linha),
        };
        if (!b.BufferDefinido) b.TamanhoDoBuffer = BufferDaWire;
        return b;
    }

    /// <summary>
    /// "TwoWire I2Cdois = TwoWire(1);" (ESP32: o numero do controlador) e
    /// "TwoWire Wire2(PB11, PB10);" (STM32: os pinos, o I2C2 da Blue Pill).
    /// </summary>
    void MontarTwoWire(Objeto obj, string nome, List<Valor> args, int linha)
    {
        obj.Campos["_nome"] = Valor.DeTexto(nome);
        if (args.Count >= 2)
        {
            var b = maq.I2c1;
            if (!b.BufferDefinido) b.TamanhoDoBuffer = BufferDaWire;
            b.Pinos((int)args[0].Num, (int)args[1].Num);
            obj.Rede = b;
            return;
        }
        int numero = args.Count > 0 ? (int)args[0].Num : 0;
        if (!EhFamiliaEsp32)
            throw new ErroDeExecucao($"TwoWire({numero}): na {maq.Perfil?.Nome} o barramento vem pronto -- use o Wire"
                                     + (TemWire1 ? " e o Wire1." : "."), linha);
        obj.Rede = numero switch
        {
            0 => BarramentoI2cDe("Wire", linha),
            1 when TemWire1 => BarramentoI2cDe("Wire1", linha),
            // O core aceita o objeto; quem falha e o begin(), sem controlador.
            _ => null,
        };
        obj.Campos["_numero"] = Valor.DeInt(numero);
    }

    /// <summary>O TwoWire declarado fala pelo barramento dele; sem controlador (TwoWire(1) na C3), nada passa.</summary>
    Valor ChamarTwoWire(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        string nome = obj.Campos.TryGetValue("_nome", out var n) ? n.Texto : "TwoWire";
        if (obj.Rede is BarramentoI2c b) return ChamarI2c(b, nome, metodo, c, amb);
        foreach (var arg in c.Args) Avaliar(arg, amb);
        Avisar($"I2C: {nome} -- a {maq.Perfil?.Nome} nao tem o controlador {(int)obj.Campos.GetValueOrDefault("_numero", Valor.DeInt(0)).Num}; "
               + "o begin() falha e nada e transmitido.");
        return metodo switch { "begin" => Valor.DeBool(false), "endTransmission" => Valor.DeInt(4), _ => Valor.DeInt(0) };
    }

    /// <summary>
    /// O begin() sem pinos: os do setPins/setSDA, senao os de fabrica. O
    /// Wire1 da ESP32 nao tem pinos de fabrica -- o core recusa (devolve
    /// false), e o barramento fica parado.
    /// </summary>
    bool ComecarBarramento(BarramentoI2c b, string nome)
    {
        if (b.Ligado) return true;
        if (b.Numero == 0) b.Begin(b.Sda >= 0 ? b.Sda : maq.Perfil?.SdaPadrao ?? -1, b.Scl >= 0 ? b.Scl : maq.Perfil?.SclPadrao ?? -1);
        else if (b.Sda >= 0 && b.Scl >= 0) b.Begin(b.Sda, b.Scl);
        else if (PinosDoWire1 is { } fabrica && maq.I2c1 == b) b.Begin(fabrica.Sda, fabrica.Scl);
        else
        {
            Avisar($"I2C: {nome}.begin() sem pinos -- na {maq.Perfil?.Nome} o segundo barramento nao tem pinos de fabrica "
                   + $"(\"no Default SDA Pin for Second Peripheral\"). Use {nome}.begin(SDA, SCL).");
            return false;
        }
        return true;
    }

    void Avisar(string aviso)
    {
        lock (maq.Trava)
            if (!maq.Avisos.Contains(aviso)) maq.Avisos.Add(aviso);
    }

    Valor ChamarI2c(string metodo, Chamada c, Ambiente amb) => ChamarI2c(maq.I2c, "Wire", metodo, c, amb);

    Valor ChamarI2c(BarramentoI2c barramento, string nome, string metodo, Chamada c, Ambiente amb)
    {
        // Antes de avaliar os argumentos: o de onReceive e o NOME de uma
        // funcao, e avaliar como expressao daria "variavel nao declarada"
        // -- um erro que aponta para o lugar errado. Mesmo caso do
        // attachInterrupt.
        //
        // Escravo de verdade pediria uma segunda placa rodando o proprio
        // sketch. Recusa dita, em vez de registrar um retorno de chamada
        // que nunca seria chamado.
        if (metodo is "onReceive" or "onRequest")
            throw new ErroDeExecucao(
                $"{nome}.{metodo}(): a placa como ESCRAVA de I2C ainda nao e simulada. "
                + "O simulador roda a placa como mestre -- beginTransmission, write, "
                + "endTransmission e requestFrom funcionam.", c.Linha);

        if (!barramento.BufferDefinido) barramento.TamanhoDoBuffer = BufferDaWire;
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        int A(int i) => i < args.Count ? (int)args[i].Num : 0;

        switch (metodo)
        {
            // Wire.begin() usa os pinos de fabrica da placa; com dois
            // argumentos, os que o sketch mandou (o jeito da ESP32, com a
            // frequencia de terceiro); com um so, a placa vira ESCRAVA.
            case "begin":
                if (args.Count >= 2 && !(EhFamiliaEsp32 && args.Count == 4))
                {
                    if (!WireComPinos)
                        throw new ErroDeExecucao($"no matching function for call to 'TwoWire::begin(int, int)' -- na {maq.Perfil?.Nome} "
                            + "o I2C tem pinos fixos" + (Chip == "RP2040" ? $" (troque com {nome}.setSDA() e {nome}.setSCL() antes do begin())." : $" (SDA {maq.Perfil?.SdaPadrao}, SCL {maq.Perfil?.SclPadrao})."),
                            c.Linha);
                    if (barramento.Ligado && EhFamiliaEsp32) return Valor.DeBool(true);   // "Bus already started in Master Mode."
                    barramento.Begin(A(0), A(1));
                    if (args.Count >= 3 && A(2) > 0) barramento.Frequencia = A(2);
                    return Valor.DeBool(true);
                }
                if (args.Count >= 1)
                {
                    barramento.BeginEscravo(A(0));
                    return Valor.DeBool(true);
                }
                return Valor.DeBool(ComecarBarramento(barramento, nome));

            case "end": barramento.End(); return Valor.DeBool(true);
            case "setClock": barramento.Frequencia = A(0); return Valor.DeBool(true);
            case "getClock": return Valor.DeC(barramento.Frequencia, TipoC.UInt);
            case "getBusNum": return Valor.DeInt(barramento.Numero);

            // setPins (ESP32) e setSDA/setSCL (Pico, STM32): os pinos do proximo
            // begin(). Com o barramento rodando, a ESP32 recusa e a Pico para.
            case "setPins":
                if (!EhFamiliaEsp32) throw new ErroDeExecucao($"'class TwoWire' has no member named 'setPins' -- e da ESP32"
                                                              + (Chip == "RP2040" ? $"; na Pico use {nome}.setSDA() e {nome}.setSCL()." : "."), c.Linha);
                if (barramento.Ligado)
                {
                    Avisar($"I2C: {nome}.setPins() com o barramento ligado -- o core recusa (\"bus already initialized. change pins only when not.\").");
                    return Valor.DeBool(false);
                }
                barramento.Pinos(A(0), A(1));
                return Valor.DeBool(true);

            case "setSDA":
            case "setSCL":
            {
                if (EhFamiliaEsp32) throw new ErroDeExecucao($"'class TwoWire' has no member named '{metodo}' -- na ESP32 e {nome}.setPins(sda, scl).", c.Linha);
                int pino = A(0);
                bool sda = metodo == "setSDA";
                if (Chip == "RP2040")
                {
                    // O i2c0 so sai nos GPIO 0, 4, 8... (SDA) e 1, 5, 9... (SCL); o i2c1, nos 2, 6, 10... e 3, 7, 11...
                    int resto = (sda ? 0 : 1) + (barramento.Numero == 1 ? 2 : 0);
                    if ((sda ? barramento.Sda : barramento.Scl) == pino) return Valor.DeBool(true);
                    if (barramento.Ligado)
                        throw new ErroDeExecucao($"FATAL: Attempting to set {nome}.{(sda ? "SDA" : "SCL")} while running", c.Linha);
                    if (pino % 4 != resto || pino > 29)
                        throw new ErroDeExecucao($"FATAL: Attempting to set {nome}.{(sda ? "SDA" : "SCL")} to illegal pin {pino} -- "
                            + $"o {(barramento.Numero == 1 ? "i2c1" : "i2c0")} so sai nos GPIO {resto}, {resto + 4}, {resto + 8}...", c.Linha);
                }
                if (sda) barramento.Pinos(pino, -1); else barramento.Pinos(-1, pino);
                return Valor.DeBool(true);
            }

            // ESP32: antes do begin(), no minimo 32. Devolve o tamanho novo, ou 0.
            case "setBufferSize":
                if (A(0) < 32 || barramento.Ligado) return Valor.DeInt(0);
                barramento.TamanhoDoBuffer = A(0);
                return Valor.DeInt(A(0));

            case "setTimeOut": case "setTimeout": case "setWireTimeout": case "flush":
            case "clearWireTimeoutFlag":
                return Valor.Vazio;
            case "getTimeOut": return Valor.DeInt(50);
            case "getWireTimeoutFlag": return Valor.DeBool(false);

            case "beginTransmission":
                ExigirBarramentoLigado(barramento, nome);
                barramento.ComecarTransmissao(A(0));
                return Valor.Vazio;

            case "endTransmission":
            {
                int alvo = barramento.Destino;
                int r = barramento.TerminarTransmissao();
                if (r == BarramentoI2c.SemRespostaNoEndereco) AvisarForaDoFio(barramento, nome, alvo);
                return Valor.DeInt(r);
            }

            // write aceita byte, char, texto e vetor de bytes -- o mesmo
            // do Serial.write. Devolve quantos bytes entraram.
            case "write":
            {
                if (args.Count > 0 && args[0].EhTexto)
                {
                    string texto = ComoEscrita(args);
                    return Valor.DeInt(barramento.Escrever(TextoArduino.ParaBytes(texto)));
                }
                if (args.Count > 0 && args[0].Referencia is Valor[] vetor)
                {
                    int n = args.Count > 1 ? Math.Min(A(1), vetor.Length) : vetor.Length;
                    return Valor.DeInt(barramento.Escrever(vetor.Take(n).Select(x => (byte)((long)x.Num & 0xFF)).ToArray()));
                }
                return Valor.DeInt(barramento.Escrever((byte)((long)(args.Count > 0
                    ? args[0].Num : 0) & 0xFF)));
            }

            case "requestFrom":
            {
                ExigirBarramentoLigado(barramento, nome);
                int n = barramento.Pedir(A(0), args.Count > 1 ? A(1) : 1);
                if (n == 0) AvisarForaDoFio(barramento, nome, A(0) & 0x7F);
                return Valor.DeInt(n);
            }

            case "available": return Valor.DeInt(barramento.Disponivel());
            case "read": return Valor.DeInt(barramento.Ler());
            case "peek": return Valor.DeInt(barramento.Espiar());
        }

        throw new ErroDeExecucao($"{nome}.{metodo}() ainda nao e suportado pelo simulador. "
            + "O barramento tem begin, setClock, setPins, beginTransmission, write, endTransmission, "
            + "requestFrom, available, read e peek.", c.Linha);
    }

    /// <summary>
    /// Mexer no barramento sem <c>Wire.begin()</c> nao trava o sketch --
    /// so avisa. Na placa de verdade a transmissao falha em silencio, e o
    /// aluno fica meia hora procurando o fio errado.
    /// </summary>
    void ExigirBarramentoLigado(BarramentoI2c barramento, string nome)
    {
        if (barramento.Ligado) return;
        Avisar($"I2C: o sketch usou o {nome} sem chamar {nome}.begin() no setup(). "
               + "Na placa de verdade nada e transmitido.");
    }

    /// <summary>
    /// A peca existe, mas esta em outro fio: o LCD nos pinos 25/26 com o
    /// Wire.begin() nos 21/22. Na placa ela simplesmente nao responde; o
    /// aviso diz onde ela esta.
    /// </summary>
    void AvisarForaDoFio(BarramentoI2c barramento, string nome, int endereco)
    {
        if (endereco < 0 || barramento.ForaDoFio(endereco) is not { } peca || peca.PinoSda < 0) return;
        Avisar($"I2C: o {peca.Nome} (0x{endereco:X2}) esta ligado nos pinos SDA {peca.PinoSda} / SCL {peca.PinoScl}, "
               + $"mas o {nome} usa SDA {barramento.Sda} / SCL {barramento.Scl}. Na placa ele nao responde.");
    }
}
