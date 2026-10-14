namespace Simulador.App;

/// <summary>
/// O MicroPython da Alpha 24: class (com __init__, self, campos que nascem na
/// atribuicao, heranca e super()), try/except/else/finally com a hierarquia
/// das excecoes do Python, raise (e o raise sozinho, que relevanta), assert,
/// with (o __enter__/__exit__) e lambda. Cada um conferido pelo que o sketch
/// imprime -- o mesmo que o MicroPython de verdade imprimiria.
/// </summary>
public static partial class Autoteste
{
    static void MicroPython_Linguagem()
    {
        saida.AppendLine("micropython: class, try/except, raise, assert, with, lambda");

        List<string> Rodar(string fonte, out string erro)
        {
            var (serial, _, _, e) = RodarComReinicios(fonte, "esp32-devkit-v1", 300);
            erro = e;
            return serial;
        }
        void Confere(string nome, string fonte, params string[] esperado)
        {
            var linhas = Rodar(fonte, out string erro);
            Conferir($"micropython: {nome}", erro is null && linhas.SequenceEqual(esperado),
                erro ?? string.Join(" | ", linhas));
        }

        Confere("classe com __init__, self e metodos (os campos nascem na atribuicao)", """
            class Contador:
                def __init__(self, inicio, passo=1):
                    self.valor = inicio
                    self.passo = passo
                def somar(self):
                    self.valor += self.passo
                    return self.valor
            c = Contador(10, 5)
            c.somar()
            print(c.somar(), c.valor, c.passo)
            """, "20 20 5");

        Confere("campo de classe vira o valor de partida de cada objeto", """
            class Placar:
                pontos = 0
                def marcar(self):
                    self.pontos += 1
            a = Placar()
            b = Placar()
            a.marcar(); a.marcar(); b.marcar()
            print(a.pontos, b.pontos)
            """, "2 1");

        Confere("heranca, metodo herdado e super().__init__", """
            class Animal:
                def __init__(self, nome):
                    self.nome = nome
                def falar(self):
                    return self.nome + " faz som"
            class Cachorro(Animal):
                def __init__(self, nome, raca):
                    super().__init__(nome)
                    self.raca = raca
                def latir(self):
                    return self.falar() + " (au)"
            d = Cachorro("Rex", "vira-lata")
            print(d.latir())
            print(d.raca)
            """, "Rex faz som (au)", "vira-lata");

        Confere("objeto do sketch com um Pin dentro (o botao de classe dos tutoriais)", """
            from machine import Pin
            class Led:
                def __init__(self, pino):
                    self.pino = Pin(pino, Pin.OUT)
                def ligar(self):
                    self.pino.value(1)
                def aceso(self):
                    return self.pino.value()
            l = Led(2)
            l.ligar()
            print(l.aceso())
            """, "1");

        Confere("try/except pega o erro do simulador pelo tipo do Python (ZeroDivisionError)", """
            try:
                x = 1 / 0
                print("nao chega")
            except ZeroDivisionError as e:
                print("divisao:", e)
            """, "divisao: divisao por zero");

        Confere("except com varios tipos, else e finally", """
            for v in [0, 5]:
                try:
                    if v == 0:
                        raise ValueError("zero")
                except (TypeError, ValueError) as e:
                    print("pegou", e)
                else:
                    print("sem erro", v)
                finally:
                    print("fim", v)
            """, "pegou zero", "fim 0", "sem erro 5", "fim 5");

        Confere("a hierarquia: OSError pega a classe do sketch que herda dele; LookupError pega o KeyError", """
            class SensorFalhou(OSError):
                pass
            try:
                raise SensorFalhou("sem resposta")
            except OSError as e:
                print("OSError:", e)
            try:
                raise KeyError("chave")
            except LookupError as e:
                print("LookupError:", e)
            """, "OSError: sem resposta", "LookupError: chave");

        Confere("o except que nao bate deixa passar (e o de fora pega); o raise sozinho relevanta", """
            try:
                try:
                    raise TypeError("t")
                except ValueError:
                    print("nao")
            except TypeError as e:
                print("fora:", e)
            try:
                try:
                    raise IndexError("i")
                except IndexError:
                    print("dentro")
                    raise
            except Exception as e:
                print("de novo:", e)
            """, "fora: t", "dentro", "de novo: i");

        Confere("finally roda no return, e assert levanta o AssertionError", """
            def f():
                try:
                    return "valor"
                finally:
                    print("finally")
            print(f())
            def positivo(v):
                assert v > 0, "precisa ser positivo"
                return v
            try:
                positivo(-3)
            except AssertionError as e:
                print("assert:", e)
            """, "finally", "valor", "assert: precisa ser positivo");

        Confere("with: o __enter__ entrega o objeto e o __exit__ roda no fim (mesmo com erro)", """
            class Recurso:
                def __init__(self, nome):
                    self.nome = nome
                def __enter__(self):
                    print("abriu", self.nome)
                    return self
                def __exit__(self, tipo, valor, rastro):
                    print("fechou", self.nome)
            with Recurso("a") as r:
                print("usando", r.nome)
            try:
                with Recurso("b"):
                    raise RuntimeError("falhou")
            except RuntimeError as e:
                print("erro:", e)
            """, "abriu a", "usando a", "fechou a", "abriu b", "fechou b", "erro: falhou");

        Confere("lambda guardada, chamada direto, com padrao, e como argumento", """
            dobro = lambda x: x * 2
            soma = lambda a, b=10: a + b
            print(dobro(21), soma(5), soma(1, 2), (lambda: "direto")())
            def aplicar(f, v):
                return f(v)
            print(aplicar(lambda n: n + 1, 41))
            """, "42 15 3 direto", "42");

        // Sem ninguem pegar, o erro para o sketch com o tipo na frente.
        var semPegar = Rodar("""
            print("antes")
            raise ValueError("ninguem pegou")
            """, out string erroSemPegar);
        Conferir("micropython: a excecao sem except para o sketch e diz o tipo e a mensagem",
            semPegar.SequenceEqual(["antes"]) && erroSemPegar is not null && erroSemPegar.Contains("ValueError: ninguem pegou"),
            erroSemPegar ?? "nao parou");
    }
}

public static partial class Autoteste
{
    /// <summary>
    /// O machine.Timer (periodico e de uma vez, rodando durante o time.sleep),
    /// o RTC (acertado, e o time.localtime junto), o SPI, o bytearray e o
    /// Pin.irq com o botao do circuito.
    /// </summary>
    static void MicroPython_TimerRtcSpi()
    {
        saida.AppendLine("micropython: Timer, RTC, SPI e Pin.irq");

        var (linhas, _, _, erro) = RodarComReinicios("""
            from machine import Pin, Timer, RTC, SPI
            import time
            led = Pin(2, Pin.OUT)
            voltas = 0
            def piscar(t):
                global voltas
                voltas += 1
                led.value(not led.value())
            tim = Timer(0)
            tim.init(period=250, mode=Timer.PERIODIC, callback=piscar)
            uma = []
            Timer(1).init(period=600, mode=Timer.ONE_SHOT, callback=lambda t: uma.append(1))
            time.sleep(1)
            print("voltas", voltas, len(uma))
            tim.deinit()
            time.sleep(1)
            print("parado", voltas)
            rtc = RTC()
            rtc.datetime((2026, 10, 14, 2, 8, 30, 0, 0))
            time.sleep(2)
            d = rtc.datetime()
            print("rtc", d[0], d[1], d[2], d[4], d[5], d[6], time.localtime()[3], time.localtime()[4])
            spi = SPI(2, baudrate=1000000, polarity=0, phase=0)
            buf = bytearray(4)
            spi.readinto(buf, 0xFF)
            spi.write(b'\x01\x02')
            print("spi", len(buf), len(spi.read(3)))
            print(list(bytearray(3)), list(bytearray([1, 2])))
            """, "esp32-devkit-v1", 6000);
        Conferir("micropython: Timer periodico de 250 ms dispara 4 vezes em 1 s (durante o sleep); o ONE_SHOT, uma vez",
            erro is null && linhas.Contains("voltas 4 1"), erro ?? string.Join(" | ", linhas));
        Conferir("micropython: Timer.deinit() para o callback", linhas.Contains("parado 4"), string.Join(" | ", linhas));
        Conferir("micropython: RTC.datetime() acerta e anda com a placa, e o time.localtime() acompanha",
            linhas.Contains("rtc 2026 10 14 8 30 2 8 30"), string.Join(" | ", linhas));
        Conferir("micropython: SPI(2) com readinto, write e read", linhas.Contains("spi 4 3"), string.Join(" | ", linhas));
        Conferir("micropython: bytearray(n) sao n bytes zerados; bytearray(lista) os bytes dela",
            linhas.Contains("[0, 0, 0] [1, 2]"), string.Join(" | ", linhas));

        // ---- o Pin.irq com o botao do circuito ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var botao = new PecaNoCircuito { Id = "botao", Tipo = "pushbutton", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, botao]);
            c.Fios.Add(new Fio { DePeca = "botao", DePino = "1.l", ParaPeca = "placa", ParaPino = "D14" });
            c.Fios.Add(new Fio { DePeca = "botao", DePino = "2.l", ParaPeca = "placa", ParaPino = "GND.1" });
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, """
                    from machine import Pin
                    import time
                    apertos = 0
                    def apertou(pino):
                        global apertos
                        apertos += 1
                        print("apertou", apertos, pino.value())
                    botao = Pin(14, Pin.IN, Pin.PULL_UP)
                    botao.irq(trigger=Pin.IRQ_FALLING, handler=apertou)
                    print("pronto")
                    while True:
                        time.sleep(1)
                    """);
                janela.TesteRodar();
                var maq = janela.TesteMaquinaDa("placa");
                var ed = janela.TesteEditorCircuito;
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto"), 4000);
                for (int k = 0; k < 2; k++)
                {
                    ed.BotoesPressionados.Add("botao");
                    EsperarPor(() => LinhasDoSerial(maq).Count(l => l.StartsWith("apertou")) > k, 2000);
                    ed.BotoesPressionados.Remove("botao");
                    EsperarPor(() => false, 300);
                }
                var vistas = LinhasDoSerial(maq);
                Conferir("micropython: Pin.irq(IRQ_FALLING) chama o handler a cada aperto, com o Pin de argumento (em LOW)",
                    vistas.Contains("apertou 1 0") && vistas.Contains("apertou 2 0") && !vistas.Any(l => l.StartsWith("apertou 3")),
                    string.Join(" | ", vistas));
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
    }
}
