namespace Simulador.App;

/// <summary>
/// O Python pelo MicroPython de verdade (nativo/micropython), compilado junto do core:
/// a linguagem inteira, as bibliotecas dele, e os mesmos aparelhos da bancada.
/// </summary>
public static partial class Autoteste
{
    static void MicroPython_Real()
    {
        saida.AppendLine("MicroPython de verdade");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("micropython: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- a linguagem: o que o interpretador antigo nao tinha ----
        const string linguagem = """
            import sys
            def contar(n):
                for i in range(n):
                    yield i * i
            print(list(contar(4)))
            print(sum(x for x in contar(4) if x % 2 == 0))
            class Peca:
                total = 0
                def __init__(self, nome):
                    self.nome = nome
                    Peca.total += 1
                def __repr__(self):
                    return "Peca({})".format(self.nome)
                @property
                def grande(self):
                    return self.nome.upper()
                @staticmethod
                def quantas():
                    return Peca.total
            a = Peca("led")
            print(a, a.grande, Peca.quantas())
            def aspas(f):
                def g(*args, **kw):
                    return "<" + f(*args, **kw) + ">"
                return g
            @aspas
            def nome(x, y="z"):
                return x + y
            print(nome("a"), nome("a", y="b"))
            print(f"{2 ** 100} {3.5:.2f} {'txt':>5}")
            try:
                [1, 2][5]
            except IndexError as e:
                print("erro:", type(e).__name__)
            finally:
                print("fim")
            d = {"a": 1, "b": 2}
            print(sorted(d.items()), {k: v * 2 for k, v in d.items()}["b"], {1, 2} | {3})
            x, *resto = [1, 2, 3]
            print(x, resto, b"abc"[1], bytearray(2), 0.1 + 0.2, 7 // 2, -7 % 3)
            print(sys.implementation.name, sys.platform)
            import asyncio
            async def tarefa(n):
                await asyncio.sleep_ms(10)
                return n * 2
            async def principal():
                return await asyncio.gather(tarefa(1), tarefa(2))
            print(asyncio.run(principal()))
            import json, re, math, collections, struct, binascii, hashlib, random, gc
            print(json.dumps({"k": [1, True, None]}), json.loads('{"x": 2.5}')["x"])
            print(re.match(r"(\w+)-(\d+)", "abc-42").group(2), math.floor(2.7), struct.pack("<H", 513))
            print(binascii.hexlify(hashlib.sha256(b"a").digest())[:8], gc.mem_free() > 50000)
            """;
        var (serial, erro, nativo, _) = RodarNativo(linguagem, 600);
        saida.AppendLine($"        {serial}");
        Conferir("micropython: geradores, classes, decoradores, f-string, excecoes, async e a biblioteca dele",
            nativo && erro is null
            && serial.Contains("[0, 1, 4, 9]") && serial.Contains("|4|") && serial.Contains("Peca(led) LED 1")
            && serial.Contains("<az> <ab>") && serial.Contains("1267650600228229401496703205376 3.50   txt")
            && serial.Contains("erro: IndexError|fim") && serial.Contains("micropython esp32")
            && serial.Contains("[2, 4]") && serial.Contains("0.3") && serial.Contains("b'ca978112'"),
            $"{nativo} {erro} | {serial}");

        // ---- os pinos: os mesmos do C ----
        const string pinos = """
            from machine import Pin, ADC, PWM, Timer, RTC
            import time
            led = Pin(2, Pin.OUT)
            led.on()
            print("aceso", led.value())
            led.toggle()
            print("apagado", led(), Pin(2) is led)
            contagem = []
            botao = Pin(4, Pin.IN, Pin.PULL_UP)
            print("pullup", botao.value())
            def aoMudar(p):
                contagem.append(p.value())
            sinal = Pin(5, Pin.OUT, value=0)
            entrada = Pin(18, Pin.IN, Pin.PULL_DOWN)
            pwm = PWM(Pin(13), freq=1000, duty_u16=32768)
            print(pwm.freq(), pwm.duty_u16())
            t = Timer(0)
            voltas = []
            t.init(period=20, mode=Timer.PERIODIC, callback=lambda _: voltas.append(1))
            inicio = time.ticks_ms()
            time.sleep_ms(110)
            t.deinit()
            print("voltas", 3 <= len(voltas) <= 7, 100 <= time.ticks_diff(time.ticks_ms(), inicio) <= 130)
            r = RTC()
            r.datetime((2026, 11, 5, 3, 14, 30, 0, 0))
            print(r.datetime()[:3], time.localtime()[3])
            try:
                Pin(99)
            except ValueError as e:
                print("pino:", e)
            """;
        Maquina m;
        (serial, erro, nativo, m) = RodarNativo(pinos, 400);
        saida.AppendLine($"        {serial}");
        Conferir("micropython: Pin, PWM, Timer e RTC pela API da ESP32",
            nativo && erro is null && serial.Contains("aceso 1") && serial.Contains("apagado 0 True") && serial.Contains("pullup 1")
            && serial.Contains("1000 32768") && serial.Contains("voltas True True") && serial.Contains("(2026, 11, 5) 14")
            && serial.Contains("pino: invalid pin"),
            $"{nativo} {erro} | {serial}");
        Conferir("micropython: o pino mexido e o mesmo do C", m is not null && m.DigitalRead(2) == 0, "GPIO 2");

        // ---- a fita e o OLED: as bibliotecas em Python de verdade ----
        const string aparelhos = """
            from machine import Pin, SoftI2C, I2C
            import neopixel, framebuf
            fita = neopixel.NeoPixel(Pin(12), 16)
            fita[3] = (148, 0, 211)
            fita.write()
            print(fita[3], len(fita))
            buf = bytearray(8)
            fb = framebuf.FrameBuffer(buf, 8, 8, framebuf.MONO_HMSB)
            fb.pixel(1, 0, 1)
            print(buf[0], fb.pixel(1, 0))
            i2c = I2C(0, sda=Pin(21), scl=Pin(22))
            print(type(i2c).__name__, i2c.scan())
            """;
        (serial, erro, nativo, m) = RodarNativo(aparelhos, 300);
        saida.AppendLine($"        {serial}");
        int cor = m is not null && m.Fitas.TryGetValue(12, out var fita) ? fita.Cores[3] : -1;
        Conferir("micropython: o neopixel.py e o framebuf de verdade pintam a mesma fita da bancada",
            nativo && erro is null && serial.Contains("(148, 0, 211) 16") && serial.Contains("2 1") && serial.Contains("I2C []") && cor == Fita.Montar(148, 0, 211),
            $"{nativo} {erro} | {serial} | {cor:X6}");

        // ---- os arquivos e as abas ----
        const string arquivos = """
            import os, config
            print(config.VALOR, config.dobro(4))
            with open("dados.txt", "w") as f:
                f.write("linha 1\nlinha 2\n")
            with open("dados.txt") as f:
                print(f.readlines())
            print("dados.txt" in os.listdir(), os.stat("dados.txt")[6])
            os.rename("dados.txt", "d2.txt")
            os.mkdir("pasta")
            print(sorted(x for x in os.listdir() if x in ("d2.txt", "pasta")))
            os.remove("d2.txt")
            os.rmdir("pasta")
            try:
                open("nao_existe.txt")
            except OSError as e:
                print("sem arquivo", e.errno)
            """;
        (serial, erro, nativo, _) = RodarNativo(arquivos, 300, abas: new Dictionary<string, string>
        {
            ["config.py"] = "VALOR = 7\ndef dobro(x):\n    return 2 * x\n",
        });
        saida.AppendLine($"        {serial}");
        Conferir("micropython: open, os e o import de outra aba, na flash da placa",
            nativo && erro is null && serial.Contains("7 8") && serial.Contains("['linha 1\\n', 'linha 2\\n']") && serial.Contains("True 16")
            && serial.Contains("['d2.txt', 'pasta']") && serial.Contains("sem arquivo 2"),
            $"{nativo} {erro} | {serial}");

        // ---- o erro: o traceback no monitor, e a linha certa no editor ----
        const string comErro = """
            print("antes")
            def f():
                return 1 / 0
            f()
            """;
        (serial, erro, nativo, _) = RodarNativo(comErro, 200);
        saida.AppendLine($"        {serial} | {erro}");
        Conferir("micropython: o erro que escapa mostra o traceback e aponta o programa",
            nativo && serial.Contains("antes") && serial.Contains("ZeroDivisionError") && erro is not null && erro.Contains("ZeroDivisionError"),
            $"{nativo} {erro} | {serial}");

        const string semSintaxe = """
            print("nunca")
            def f(:
                pass
            """;
        (serial, erro, nativo, _) = RodarNativo(semSintaxe, 200);
        Conferir("micropython: o erro de sintaxe vem do compilador dele, e nada roda",
            nativo && !serial.Contains("nunca") && erro is not null && erro.Contains("SyntaxError"), $"{nativo} {erro} | {serial}");

        // ---- a Pico: o mesmo MicroPython, com o port rp2 (os pinos, os barramentos e o rp2) ----
        const string pico = """
            import sys, machine, rp2, time
            from machine import Pin, PWM, Timer, I2C, UART
            print(sys.platform, machine.freq())
            led = Pin(25, Pin.OUT)
            led.on()
            pwm = PWM(Pin(15))
            pwm.freq(500)
            pwm.duty_u16(16384)
            voltas = []
            t = Timer(period=20, mode=Timer.PERIODIC, callback=lambda _: voltas.append(1))
            time.sleep_ms(110)
            t.deinit()
            i2c = I2C(0)
            u = UART(0, 9600)
            print("pico", led.value(), pwm.freq(), 3 <= len(voltas) <= 7, rp2.bootsel_button(), i2c, u)
            """;
        (serial, erro, nativo, m) = RodarNativo(pico, 1500, "pi-pico");
        saida.AppendLine($"        {serial}");
        Conferir("micropython: a Pico roda o MicroPython de verdade -- sys.platform rp2, 125 MHz, Pin(25), PWM, Timer, I2C(0) nos GPIO 8/9 e o rp2.bootsel_button()",
            nativo && erro is null && serial.Contains("rp2 125000000") && serial.Contains("pico 1 500 True 0") && serial.Contains("scl=9, sda=8")
            && m is not null && m.DigitalRead(25) == 1,
            $"{nativo} {erro} | {serial}");
    }
}
