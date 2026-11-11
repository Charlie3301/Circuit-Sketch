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

        // ---- a rede de ponta a ponta: o WLAN, o socket cru, o requests, o umqtt e o ntptime ----
        using var servidorPy = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        servidorPy.Start();
        int portaPy = ((System.Net.IPEndPoint)servidorPy.LocalEndpoint).Port;
        string pedidosPy = "";
        _ = Task.Run(() =>
        {
            try
            {
                for (int k = 0; k < 3; k++)
                {
                    using var c = servidorPy.AcceptTcpClient();
                    var s = c.GetStream();
                    var b = new byte[4096];
                    // O requests manda o pedido em varios writes: le ate o cabecalho fechar e o corpo chegar.
                    string pedido = "";
                    while (true)
                    {
                        int fimDoCabecalho = pedido.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        var tamanho = System.Text.RegularExpressions.Regex.Match(pedido, @"(?i)content-length:\s*(\d+)");
                        if (fimDoCabecalho >= 0 && pedido.Length - fimDoCabecalho - 4 >= (tamanho.Success ? int.Parse(tamanho.Groups[1].Value) : 0)) break;
                        int n = s.Read(b, 0, b.Length);
                        if (n <= 0) break;
                        pedido += System.Text.Encoding.ASCII.GetString(b, 0, n);
                    }
                    pedidosPy += pedido.Split('\n')[0].Trim() + (pedido.Contains("\"luz\": 1") ? " luz" : "") + ";";
                    string corpo = pedido.StartsWith("GET /cru") ? "cru ok" : pedido.StartsWith("GET") ? "{\"temp\": 21}" : "criado";
                    var r = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.0 {(pedido.StartsWith("POST") ? "201 Created" : "200 OK")}\r\nContent-Type: application/json\r\nContent-Length: {corpo.Length}\r\n\r\n{corpo}");
                    s.Write(r, 0, r.Length);
                }
            }
            catch { }
        });
        string redePy = """
            import network, socket, time, requests, ntptime
            from umqtt.simple import MQTTClient
            w = network.WLAN(network.STA_IF)
            w.active(True)
            w.connect("CircuitSketch-WiFi", "12345678")
            for _ in range(100):
                if w.isconnected():
                    break
                time.sleep_ms(100)
            print("wifi", w.isconnected(), w.ifconfig()[0].count("."))

            s = socket.socket()
            s.connect(socket.getaddrinfo("127.0.0.1", PORTA)[0][-1])
            s.send(b"GET /cru HTTP/1.0\r\n\r\n")
            dados = b""
            while True:
                p = s.recv(64)
                if not p:
                    break
                dados += p
            s.close()
            print("socket", dados.split(b"\r\n\r\n")[1])

            r = requests.get("http://127.0.0.1:PORTA/sensor")
            print("get", r.status_code, r.json()["temp"])
            r.close()
            r = requests.post("http://127.0.0.1:PORTA/luz", json={"luz": 1})
            print("post", r.status_code, r.text)
            r.close()

            chegou = []
            c = MQTTClient("placa-py", "broker.hivemq.com")
            c.set_callback(lambda t, m: chegou.append((t, m)))
            c.connect()
            c.subscribe(b"casa/luz")
            c.publish(b"casa/luz", b"liga")
            for _ in range(100):
                c.check_msg()
                if chegou:
                    break
                time.sleep_ms(20)
            print("mqtt", chegou)
            c.disconnect()

            ntptime.settime()
            print("ntp", time.localtime()[0] >= 2026)
            """.Replace("PORTA", portaPy.ToString());
        (serial, erro, nativo, m) = RodarNativo(redePy, 12000, antes: (_, maq) =>
        {
            // Sem internet: o broker de fora nao resolve, e o interno (aberto numa porta livre) atende.
            maq.Rede = new Rede { UsarInternet = false };
            maq.Rede.AbrirBrokerLocal(0);
        });
        m?.Rede?.Encerrar();
        saida.AppendLine($"        {serial}");
        Conferir("micropython: a rede de ponta a ponta -- network.WLAN, o socket cru, o requests (GET com json e POST), o umqtt.simple pelo broker e o ntptime",
            nativo && erro is null && serial.Contains("wifi True 3") && serial.Contains("socket b'cru ok'") && serial.Contains("get 200 21")
            && serial.Contains("post 201 criado") && serial.Contains("mqtt [(b'casa/luz', b'liga')]") && serial.Contains("ntp True")
            && pedidosPy.Contains("GET /cru") && pedidosPy.Contains("GET /sensor") && pedidosPy.Contains("POST /luz HTTP/1.0 luz"),
            $"{nativo} {erro} | {serial} | {pedidosPy}");

        // ---- o espnow de verdade (o modulo do port da ESP32): duas placas, sem roteador ----
        const string quemRecebe = """
            import network, espnow
            w = network.WLAN(network.STA_IF)
            w.active(True)
            e = espnow.ESPNow()
            e.active(True)
            print("pronto")
            de, msg = e.recv(6000)
            print("chegou", msg, len(de))
            e.add_peer(de)
            print("respondeu", e.send(de, b"eco"))
            """;
        const string quemManda = """
            import network, espnow, time
            w = network.WLAN(network.STA_IF)
            w.active(True)
            e = espnow.ESPNow()
            e.active(True)
            todos = b"\xff" * 6
            e.add_peer(todos)
            time.sleep_ms(800)
            e.send(todos, b"oi", False)
            de, msg = e.recv(4000)
            print("voltou", msg, e.peer_count())
            print("fim")
            """;
        var (serRecebe, serManda, erroNow) = RodarDuasNativas(quemRecebe, quemManda, "esp32-devkit-v1", 9000, (a, b) => b.Contains("fim"));
        saida.AppendLine($"        {serRecebe} || {serManda}");
        Conferir("micropython: o espnow de verdade -- uma placa manda para todos, a outra recebe com o MAC de quem mandou, cadastra o par e responde",
            erroNow is null && serRecebe?.Contains("chegou b'oi' 6") == true && serRecebe.Contains("respondeu True") && serManda?.Contains("voltou b'eco'") == true,
            $"{erroNow} | {serRecebe} || {serManda}");

        // ---- o _thread: as threads do Python com a trava, o coletor e a que so faz conta ----
        const string threads = """
            import _thread, time, gc
            trava = _thread.allocate_lock()
            contagem = [0]
            feitos = []
            def trabalho(nome, voltas):
                for i in range(voltas):
                    with trava:
                        contagem[0] += 1
                    if i % 50 == 0:
                        time.sleep_ms(1)
                lixo = [str(j) * 3 for j in range(200)]
                feitos.append((nome, len(lixo)))
            _thread.start_new_thread(trabalho, ("a", 300))
            _thread.start_new_thread(trabalho, ("b", 300))
            for i in range(300):
                with trava:
                    contagem[0] += 1
                if i % 100 == 0:
                    gc.collect()
            while len(feitos) < 2:
                time.sleep_ms(10)
            print("threads", contagem[0], sorted(feitos), _thread.get_ident() != 0)
            rodando = [True]
            n = [0]
            def gira():
                while rodando[0]:
                    n[0] += 1
            _thread.start_new_thread(gira, ())
            time.sleep_ms(100)
            rodando[0] = False
            time.sleep_ms(30)
            print("girou", n[0] > 100)
            """;
        foreach (string placaDasThreads in new[] { "esp32-devkit-v1", "pi-pico" })
        {
            (serial, erro, nativo, _) = RodarNativo(threads, 6000, placaDasThreads);
            Conferir($"micropython: o _thread na {placaDasThreads} -- tres threads somam com a trava, o coletor roda no meio e a thread que so faz conta nao prende as outras",
                nativo && erro is null && serial.Contains("threads 900 [('a', 200), ('b', 200)] True") && serial.Contains("girou True"), $"{nativo} {erro} | {serial}");
        }

        // ---- o bluetooth.BLE: a placa como periferico (a UART da Nordic), com o celular da bancada ----
        const string perifericoBle = """
            import bluetooth, struct, time
            ble = bluetooth.BLE()
            ble.active(True)
            UART = bluetooth.UUID("6E400001-B5A3-F393-E0A9-E50E24DCCA9E")
            TX = (bluetooth.UUID("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"), 0x0002 | 0x0010)
            RX = (bluetooth.UUID("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"), 0x0008 | 0x0004)
            AMBIENTE = bluetooth.UUID(0x181A)
            TEMP = (bluetooth.UUID(0x2A6E), 0x0002 | 0x0010)
            def anunciar():
                nome = b"py-uart"
                ble.gap_advertise(100000, adv_data=bytes([2, 1, 6, len(nome) + 1, 9]) + nome)
            def irq(ev, d):
                if ev == 1:
                    print("conectou", d[0], d[1], len(bytes(d[2])))
                elif ev == 2:
                    print("saiu", d[0])
                    anunciar()
                elif ev == 3:
                    v = bytes(ble.gatts_read(d[1]))
                    print("chegou", d[1] == rx, v)
                    ble.gatts_notify(d[0], tx, b"eco:" + v[:4])
            ble.irq(irq)
            ((tx, rx), (temp,)) = ble.gatts_register_services(((UART, (TX, RX)), (AMBIENTE, (TEMP,))))
            ble.gatts_write(temp, struct.pack("<h", 2150))
            anunciar()
            tipo, mac = ble.config("mac")
            print("ble", ble.active(), tipo, len(mac), ble.config("gap_name"), ble.config("mtu"), tx, rx, temp)
            # O laco que nao espera tambem recebe os eventos do radio.
            voltas = 0
            while True:
                voltas += 1
            """;
        var notificacoesPy = new List<byte[]>();
        int passoBle = 0;
        string nomeBle = null;
        byte[] tempLida = null;
        RadioBle radioPy = null;
        (serial, erro, nativo, m) = RodarNativo(perifericoBle, 8000, durante: maq =>
        {
            var rede = maq.Rede;
            if (passoBle == 0)
            {
                radioPy = rede.AnunciosBle().FirstOrDefault();
                if (radioPy is null) return;
                nomeBle = radioPy.Nome;
                rede.NotificouOCelular += (_, _, dados) => { lock (notificacoesPy) notificacoesPy.Add(dados); };
                rede.ConectarBleDoCelular(radioPy);
                var doTx = radioPy.Visiveis().FirstOrDefault(c => c.Uuid == Ble.UartTx);
                var doRx = radioPy.Visiveis().FirstOrDefault(c => c.Uuid == Ble.UartRx);
                rede.AssinarDoCelular(doTx, true);
                rede.EscreverDoCelular(doRx, "oi"u8.ToArray());
                // Passa dos 20 bytes do valor de fabrica: a placa fica so com os 20 primeiros.
                rede.EscreverDoCelular(doRx, "abcdefghijklmnopqrstuvwxy"u8.ToArray());
                tempLida = rede.LerDoCelular(radioPy.Visiveis().FirstOrDefault(c => Ble.Curto(c.Uuid) == 0x2A6E));
                passoBle = 1;
            }
            else if (passoBle == 1)
            {
                lock (notificacoesPy) if (notificacoesPy.Count < 2) return;
                rede.DesconectarBleDoCelular();
                passoBle = 2;
            }
            else if (passoBle == 2 && rede.AnunciosBle().Contains(radioPy))
            {
                passoBle = 3;
                maq.Parar = true;
            }
        });
        saida.AppendLine($"        {serial}");
        string ecos;
        lock (notificacoesPy) ecos = string.Join(",", notificacoesPy.Select(System.Text.Encoding.ASCII.GetString));
        Conferir("micropython: o bluetooth.BLE como periferico -- registra a UART da Nordic e um sensor, anuncia com o nome, o celular conecta, escreve (so 20 bytes cabem), le o valor, recebe as notificacoes e, ao sair, a placa volta a anunciar",
            nativo && erro is null && passoBle == 3 && nomeBle == "py-uart" && serial.Contains("ble True 0 6 b'MPY ESP32' 256 18 21 24")
            && serial.Contains("conectou 0 1 6") && serial.Contains("chegou True b'oi'") && serial.Contains("chegou True b'abcdefghijklmnopqrst'")
            && serial.Contains("saiu 0") && ecos == "eco:oi,eco:abcd" && tempLida is [0x66, 0x08],
            $"{nativo} {erro} passo {passoBle} nome {nomeBle} | {serial} | {ecos} | {(tempLida is null ? "-" : Convert.ToHexString(tempLida))}");

        // ---- o bluetooth.BLE entre duas placas: uma anuncia e serve, a outra procura, conecta e descobre ----
        const string sensorBle = """
            import bluetooth, time
            ble = bluetooth.BLE()
            ble.active(True)
            TEMP = (bluetooth.UUID(0x2A6E), 0x0002 | 0x0010)
            CMD = (bluetooth.UUID("12345678-1234-5678-1234-56789abcdef1"), 0x0008)
            def irq(ev, d):
                if ev == 1:
                    print("central", d[0])
                elif ev == 2:
                    print("saiu", d[0])
                elif ev == 3:
                    print("cmd", bytes(ble.gatts_read(d[1])))
                    ble.gatts_notify(d[0], temp, b"\x01\x02")
            ble.irq(irq)
            ((temp, cmd),) = ble.gatts_register_services(((bluetooth.UUID(0x181A), (TEMP, CMD)),))
            ble.gatts_write(temp, b"\x66\x08")
            nome = b"sensor-py"
            ble.gap_advertise(100000, bytes([2, 1, 6, 3, 3, 0x1A, 0x18, len(nome) + 1, 9]) + nome)
            print("pronto", temp, cmd)
            while True:
                time.sleep_ms(20)
            """;
        const string centralBle = """
            import bluetooth, time
            ble = bluetooth.BLE()
            ble.active(True)
            e = {"alvo": None, "conn": None, "svc": None, "chars": {}, "descs": [], "lido": None, "fim": False}
            def irq(ev, d):
                if ev == 5:
                    tipo, addr, adv, rssi, carga = d
                    if e["alvo"] is None and b"sensor-py" in bytes(carga):
                        e["alvo"] = (tipo, bytes(addr))
                        print("achou", adv, rssi < 0)
                        ble.gap_scan(None)
                elif ev == 6:
                    ble.gap_connect(*e["alvo"])
                elif ev == 7:
                    e["conn"] = d[0]
                    ble.gattc_discover_services(d[0])
                elif ev == 9:
                    if d[3] == bluetooth.UUID(0x181A):
                        e["svc"] = (d[1], d[2])
                elif ev == 10:
                    print("servico", e["svc"])
                    ble.gattc_discover_characteristics(e["conn"], *e["svc"])
                elif ev == 11:
                    e["chars"][str(d[4])] = (d[2], d[1], d[3])
                elif ev == 12:
                    valor, fim, _ = e["chars"]["UUID(0x2a6e)"]
                    ble.gattc_discover_descriptors(e["conn"], valor, fim)
                elif ev == 13:
                    e["descs"].append((d[1], str(d[2])))
                elif ev == 14:
                    print("achados", sorted(e["chars"].values()), e["descs"])
                    ble.gattc_read(e["conn"], e["chars"]["UUID(0x2a6e)"][0])
                elif ev == 15:
                    e["lido"] = bytes(d[2])
                elif ev == 16:
                    print("leu", e["lido"], d[2])
                    ble.gattc_write(e["conn"], e["descs"][0][0], b"\x01\x00", 1)
                elif ev == 17:
                    print("escreveu", d[1], d[2])
                    if d[1] == e["descs"][0][0]:
                        ble.gattc_write(e["conn"], e["chars"]["UUID('12345678-1234-5678-1234-56789abcdef1')"][0], b"liga", 1)
                elif ev == 18:
                    print("notificou", d[1], bytes(d[2]))
                    ble.gap_disconnect(d[0])
                elif ev == 8:
                    print("desconectou", d[0])
                    e["fim"] = True
            ble.irq(irq)
            time.sleep_ms(300)
            ble.gap_scan(5000, 30000, 30000, True)
            while not e["fim"]:
                time.sleep_ms(20)
            print("fim")
            """;
        var (serSensor, serCentral, erroBlePy) = RodarDuasNativas(sensorBle, centralBle, "esp32-devkit-v1", 12000, (a, b) => b.Contains("fim") && a.Contains("saiu"));
        saida.AppendLine($"        {serSensor} || {serCentral}");
        Conferir("micropython: o bluetooth.BLE entre duas placas -- a central acha o sensor na varredura, conecta, descobre o servico, as caracteristicas e o CCCD, le, assina, escreve e recebe a notificacao",
            erroBlePy is null && serSensor?.Contains("pronto 18 21") == true && serSensor.Contains("central 0") && serSensor.Contains("cmd b'liga'") && serSensor.Contains("saiu 0")
            && serCentral?.Contains("achou 0 True") == true && serCentral.Contains("servico (16, 21)") && serCentral.Contains("achados [(18, 19, 18), (21, 21, 8)] [(19, 'UUID(0x2902)')]")
            && serCentral.Contains(@"leu b'f\x08' 0") && serCentral.Contains("escreveu 19 0") && serCentral.Contains(@"notificou 18 b'\x01\x02'") && serCentral.Contains("desconectou 0"),
            $"{erroBlePy} | {serSensor} || {serCentral}");

        // ---- o Python como central de um servidor BLE do Arduino (a biblioteca BLE da ESP32) ----
        const string bateriaArduino = """
            #include <BLEDevice.h>
            #include <BLEServer.h>
            BLECharacteristic *nivel;
            void setup() {
              Serial.begin(115200);
              BLEDevice::init("ardu-bat");
              BLEServer *servidor = BLEDevice::createServer();
              BLEService *servico = servidor->createService(BLEUUID((uint16_t)0x180F));
              nivel = servico->createCharacteristic(BLEUUID((uint16_t)0x2A19), BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE);
              uint8_t v = 87;
              nivel->setValue(&v, 1);
              servico->start();
              BLEDevice::getAdvertising()->addServiceUUID(BLEUUID((uint16_t)0x180F));
              BLEDevice::startAdvertising();
              Serial.println("pronto");
            }
            void loop() {
              static int antes = 87;
              int agora = (uint8_t)nivel->getValue()[0];
              if (agora != antes) {
                Serial.printf("mudou %d\n", agora);
                antes = agora;
              }
              delay(20);
            }
            """;
        const string centralDoArduino = """
            import bluetooth, time
            ble = bluetooth.BLE()
            ble.active(True)
            e = {"alvo": None, "conn": None, "nivel": None, "leituras": 0, "fim": False}
            def irq(ev, d):
                if ev == 5:
                    if e["alvo"] is None and b"ardu-bat" in bytes(d[4]):
                        e["alvo"] = (d[0], bytes(d[1]))
                        ble.gap_scan(None)
                elif ev == 6:
                    ble.gap_connect(*e["alvo"])
                elif ev == 7:
                    e["conn"] = d[0]
                    ble.gattc_discover_characteristics(d[0], 1, 0xffff, bluetooth.UUID(0x2A19))
                elif ev == 11:
                    e["nivel"] = d[2]
                elif ev == 12:
                    ble.gattc_read(e["conn"], e["nivel"])
                elif ev == 15:
                    print("nivel", bytes(d[2]))
                elif ev == 16:
                    e["leituras"] += 1
                    if e["leituras"] == 1:
                        ble.gattc_write(e["conn"], e["nivel"], b"\x2a", 1)
                    else:
                        ble.gap_disconnect(e["conn"])
                elif ev == 17:
                    ble.gattc_read(e["conn"], e["nivel"])
                elif ev == 8:
                    e["fim"] = True
            ble.irq(irq)
            time.sleep_ms(500)
            ble.gap_scan(5000, 30000, 30000)
            while not e["fim"]:
                time.sleep_ms(20)
            print("fim")
            """;
        var (serArduino, serPython, erroMisto) = RodarDuasNativas(bateriaArduino, centralDoArduino, "esp32-devkit-v1", 12000, (a, b) => b.Contains("fim") && a.Contains("mudou"));
        saida.AppendLine($"        {serArduino} || {serPython}");
        Conferir("micropython: o Python como central de um servidor BLE do Arduino -- acha pelo nome do anuncio, conecta, descobre a caracteristica pelo UUID, le, escreve e le de novo",
            erroMisto is null && serArduino?.Contains("mudou 42") == true && serPython?.Contains("nivel b'W'") == true && serPython.Contains("nivel b'*'") && serPython.Contains("fim"),
            $"{erroMisto} | {serArduino} || {serPython}");

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
