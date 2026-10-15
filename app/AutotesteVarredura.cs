namespace Simulador.App;

/// <summary>
/// A varredura (Alpha 24): o catalogo inteiro, sem lista escrita a mao.
///
///   - Placas_PinoAPino: cada pino de cada placa. GPIO: a saida acende um
///     LED (e o pino so de entrada nao), a entrada le um botao com o pull-up,
///     o PWM acende pela metade, o analogico le um potenciometro; os de
///     alimentacao dao a tensao deles; os GND fecham o circuito; e todo pino
///     liga (o fio chega no no certo).
///   - Pecas_UmaAUma: cada peca do catalogo sozinha, sem fio nenhum (nada
///     queima, nada trava), e cada pino dela ligado sozinho numa placa (o fio
///     chega no no da placa, e o quadro roda).
///   - SemFio_UmAUm: cada tecla do controle infravermelho chega na placa, e
///     cada canal do radio poe o pulso certo no fio.
///
/// Uma peca ou um pino novo entram aqui sozinhos: e so estar no catalogo.
/// </summary>
public static partial class Autoteste
{
    // ---------------------------------------------------------------------
    //  as regras de cada placa (as da placa de verdade)
    // ---------------------------------------------------------------------

    /// <summary>O pino so le (nao tem saida): os GPIO 34 a 39 da ESP32 classica, o 46 da S2.</summary>
    static bool SoDeEntrada(PerfilPlaca perfil, int gpio) => perfil.PinoSoDeEntrada(gpio);


    /// <summary>O pino le tensao (tem ADC)?</summary>
    static bool TemAdc(PerfilPlaca perfil, string pino, int gpio)
    {
        if (PerfilPlaca.EhNomeAnalogico(pino) || perfil.SoAnalogico(gpio)) return true;
        return perfil.Id switch
        {
            // O ADC1 da ESP32 classica (o ADC2 divide com o WiFi).
            "esp32-devkit-v1" => gpio is 32 or 33 or 34 or 35 or 36 or 39,
            _ => false,
        };
    }

    static string PrimeiroPino(PecaDef def, Func<string, bool> qual) => def.Pinos.Select(p => p.Nome).FirstOrDefault(qual);

    static bool EhTres(string n) => n.StartsWith("3V3", StringComparison.Ordinal) || n.StartsWith("3.3V", StringComparison.Ordinal);

    static void Placas_PinoAPino()
    {
        saida.AppendLine("placas, pino a pino");
        int placas = 0, pinos = 0;
        foreach (var perfil in PerfilPlaca.Conhecidos)
        {
            var def = Catalogo.Obter(perfil.Id);
            if (def is null) continue;
            placas++;
            string gnd = PrimeiroPino(def, Netlist.EhPinoDeTerra);
            string alimentacao = PrimeiroPino(def, n => Netlist.EhPinoDeAlimentacao(n) && EhTres(n))
                                 ?? PrimeiroPino(def, n => Netlist.EhPinoDeAlimentacao(n) && !n.StartsWith("VIN", StringComparison.Ordinal));
            Conferir($"{perfil.Id}: tem GND e um pino de alimentacao", gnd is not null && alimentacao is not null,
                $"GND={gnd}, alimentacao={alimentacao}");
            if (gnd is null) continue;

            foreach (var pino in def.Pinos)
            {
                pinos++;
                string n = pino.Nome;
                int gpio = perfil.Numero(n);
                try
                {
                    if (Netlist.EhPinoDeTerra(n)) ConferirGnd(perfil, def, n, alimentacao);
                    else if (Netlist.EhPinoDeAlimentacao(n)) ConferirAlimentacao(perfil, n, gnd);
                    else if (gpio >= 0 && gpio < Maquina.NumPinos) ConferirGpio(perfil, def, n, gpio, gnd, alimentacao);
                    else ConferirSoLiga(perfil, n);
                }
                catch (Exception e)
                {
                    Conferir($"{perfil.Id} {n}: o teste do pino roda", false, $"{e.GetType().Name}: {e.Message}");
                }
            }
        }
        saida.AppendLine($"        {placas} placas, {pinos} pinos conferidos");
    }

    static Maquina MaquinaLigada(PerfilPlaca perfil)
    {
        var maq = new Maquina { Perfil = perfil };
        maq.Reiniciar();
        return maq;
    }

    /// <summary>A placa na USB e um LED com resistor de 220 ohms entre o pino e o GND.</summary>
    static double CorrenteNoLed(PerfilPlaca perfil, string pino, string gnd, Maquina maq)
    {
        var c = new Circuito();
        c.Pecas.AddRange([
            new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, X = 0, Y = 0, Usb = true },
            new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 600, Y = 0, Ohms = 220 },
            new PecaNoCircuito { Id = "led", Tipo = "led", X = 700, Y = 0 }]);
        c.Fios.Add(new Fio { DePeca = "placa", DePino = pino, ParaPeca = "r", ParaPino = "1" });
        c.Fios.Add(new Fio { DePeca = "r", DePino = "2", ParaPeca = "led", ParaPino = "A" });
        c.Fios.Add(new Fio { DePeca = "led", DePino = "C", ParaPeca = "placa", ParaPino = gnd });
        var conta = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq));
        return Math.Abs(conta.CorrenteDaPeca("led"));
    }

    static void ConferirGpio(PerfilPlaca perfil, PecaDef def, string n, int gpio, string gnd, string alimentacao)
    {
        string quem = $"{perfil.Id} {n} (GPIO {gpio})";
        bool soEntrada = SoDeEntrada(perfil, gpio);
        bool soAnalogico = perfil.SoAnalogico(gpio);   // o A6/A7 do Nano, o A0 da ESP8266

        // ---- a saida: HIGH acende o LED, LOW apaga ----
        var maq = MaquinaLigada(perfil);
        maq.PinMode(gpio, 1);
        maq.DigitalWrite(gpio, 1);
        double alto = CorrenteNoLed(perfil, n, gnd, maq);
        maq.DigitalWrite(gpio, 0);
        double baixo = CorrenteNoLed(perfil, n, gnd, maq);
        if (soEntrada || soAnalogico)
            Conferir($"{quem}: {(soAnalogico ? "so analogico" : "so de entrada")} -- o digitalWrite nao acende o LED", alto < 0.0005, $"{alto * 1000:0.0} mA");
        else
            Conferir($"{quem}: saida -- HIGH acende o LED (220 ohms), LOW apaga", alto > 0.002 && baixo < 0.0005,
                $"HIGH {alto * 1000:0.0} mA, LOW {baixo * 1000:0.0} mA");

        // ---- o PWM: metade do ciclo, metade da corrente ----
        if (!soEntrada && !soAnalogico && perfil.TemPwm(gpio))
        {
            var pwm = MaquinaLigada(perfil);
            pwm.PinMode(gpio, 1);
            pwm.AnalogWrite(gpio, 128);
            double meio = CorrenteNoLed(perfil, n, gnd, pwm);
            Conferir($"{quem}: PWM -- analogWrite(128) acende o LED pela metade", meio > alto * 0.2 && meio < alto * 0.8,
                $"{meio * 1000:0.0} de {alto * 1000:0.0} mA");
        }

        // ---- a entrada: o botao no GND com o INPUT_PULLUP ----
        if (!soAnalogico)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, X = 0, Y = 0, Usb = true },
                new PecaNoCircuito { Id = "bt", Tipo = "pushbutton", X = 600, Y = 0 }]);
            c.Fios.Add(new Fio { DePeca = "placa", DePino = n, ParaPeca = "bt", ParaPino = "1.l" });
            c.Fios.Add(new Fio { DePeca = "bt", DePino = "2.l", ParaPeca = "placa", ParaPino = gnd });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var rede = new Netlist(c);
            var entrada = MaquinaLigada(perfil);
            var b = Simulador.App.Bancada.DeUmaSo(entrada);
            entrada.PinMode(gpio, soEntrada ? 0 : 2);
            ed.AplicarEntradas(b, rede);
            int solto = entrada.DigitalRead(gpio);
            ed.BotoesPressionados.Add("bt");
            ed.AplicarEntradas(b, rede);
            int apertado = entrada.DigitalRead(gpio);
            if (soEntrada)
                Conferir($"{quem}: entrada (sem pull-up interno) -- o botao no GND le LOW", apertado == 0, $"apertado {apertado}");
            else
                Conferir($"{quem}: entrada -- com o INPUT_PULLUP, solto le HIGH e o botao no GND le LOW",
                    solto == 1 && apertado == 0, $"solto {solto}, apertado {apertado}");
        }

        // ---- o analogico: o potenciometro no meio ----
        if (TemAdc(perfil, n, gpio) && alimentacao is not null)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, X = 0, Y = 0, Usb = true },
                new PecaNoCircuito { Id = "pot", Tipo = "potentiometer", X = 600, Y = 0, Giro = 0.5 }]);
            c.Fios.Add(new Fio { DePeca = "pot", DePino = "VCC", ParaPeca = "placa", ParaPino = alimentacao });
            c.Fios.Add(new Fio { DePeca = "pot", DePino = "GND", ParaPeca = "placa", ParaPino = gnd });
            c.Fios.Add(new Fio { DePeca = "pot", DePino = "SIG", ParaPeca = "placa", ParaPino = n });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var analogica = MaquinaLigada(perfil);
            ed.AplicarEntradas(Simulador.App.Bancada.DeUmaSo(analogica), new Netlist(c));
            int lido = analogica.AnalogRead(gpio);
            Conferir($"{quem}: analogico -- o potenciometro no meio le ~metade do ADC ({perfil.MaxAdc})",
                Math.Abs(lido - perfil.MaxAdc / 2.0) <= perfil.MaxAdc * 0.05, $"{lido}");
        }
    }

    static void ConferirAlimentacao(PerfilPlaca perfil, string n, string gnd)
    {
        // Um resistor de 1 kohm do pino ao GND, a placa na USB.
        var c = new Circuito();
        c.Pecas.AddRange([
            new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, X = 0, Y = 0, Usb = true },
            new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 600, Y = 0, Ohms = 1000 }]);
        c.Fios.Add(new Fio { DePeca = "placa", DePino = n, ParaPeca = "r", ParaPino = "1" });
        c.Fios.Add(new Fio { DePeca = "r", DePino = "2", ParaPeca = "placa", ParaPino = gnd });
        var conta = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(MaquinaLigada(perfil)));
        double v = conta.TensaoDoPino("placa", n) - conta.TensaoDoPino("placa", gnd);
        string limpo = n.Split('.')[0];
        (double min, double max) faixa = EhTres(n) ? (3.1, 3.45)
            : limpo is "5V" or "VBUS" or "VUSB" ? (4.4, 5.2)
            // o IOREF diz a tensao dos pinos: 5 V no Uno e na Mega, 3,3 V nas Nucleo
            : limpo is "IOREF" ? (perfil.TensaoDeOperacao - 0.3, perfil.TensaoDeOperacao + 0.2)
            : limpo is "VIN" or "VSYS" ? (0, 5.2)          // na USB o VIN e uma entrada: pode ficar sem nada
            : (perfil.TensaoDeOperacao - 0.3, 5.2);           // VCC, VDD: o trilho do chip
        Conferir($"{perfil.Id} {n}: alimentacao na USB -- {faixa.min:0.0} a {faixa.max:0.0} V com 1 kohm",
            v >= faixa.min && v <= faixa.max, $"{v:0.00} V");
    }

    static void ConferirGnd(PerfilPlaca perfil, PecaDef def, string n, string alimentacao)
    {
        if (alimentacao is null) return;
        // Do 3V3 (ou 5V) da placa, por 1 kohm, a ESTE GND: a corrente volta por ele.
        var c = new Circuito();
        c.Pecas.AddRange([
            new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, X = 0, Y = 0, Usb = true },
            new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 600, Y = 0, Ohms = 1000 }]);
        c.Fios.Add(new Fio { DePeca = "placa", DePino = alimentacao, ParaPeca = "r", ParaPino = "1" });
        c.Fios.Add(new Fio { DePeca = "r", DePino = "2", ParaPeca = "placa", ParaPino = n });
        var conta = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(MaquinaLigada(perfil)));
        double i = Math.Abs(conta.CorrenteDaPeca("r"));
        Conferir($"{perfil.Id} {n}: GND -- fecha o circuito (1 kohm do {alimentacao} ate ele)", i > 0.0025, $"{i * 1000:0.00} mA");
    }

    /// <summary>EN, RESET, AREF, NC...: o fio chega no no do pino e o quadro roda.</summary>
    static void ConferirSoLiga(PerfilPlaca perfil, string n)
    {
        var c = new Circuito();
        c.Pecas.AddRange([
            new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, X = 0, Y = 0, Usb = true },
            new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 600, Y = 0, Ohms = 10000 }]);
        c.Fios.Add(new Fio { DePeca = "placa", DePino = n, ParaPeca = "r", ParaPino = "1" });
        var rede = new Netlist(c);
        var ed = new EditorCircuito();
        ed.Carregar(c);
        ed.AplicarEntradas(Simulador.App.Bancada.DeUmaSo(MaquinaLigada(perfil)), rede);
        Conferir($"{perfil.Id} {n}: pino especial -- o fio chega nele", rede.RaizDe(Netlist.No("placa", n)) == rede.RaizDe(Netlist.No("r", "1")));
    }

    // ---------------------------------------------------------------------
    //  cada peca, sozinha e pino a pino
    // ---------------------------------------------------------------------

    static void Pecas_UmaAUma()
    {
        saida.AppendLine("pecas, uma a uma (sozinhas e pino a pino)");
        int pecas = 0, ligacoes = 0;
        var placa = PerfilPlaca.Esp32;
        var defPlaca = Catalogo.Obter(placa.Id);
        foreach (var def in Catalogo.Todas.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
        {
            if (PerfilPlaca.EhPlaca(def.Id)) continue;   // as placas sao da varredura de cima
            pecas++;

            // ---- sozinha, sem fio nenhum ----
            try
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "peca", Tipo = def.Id, X = 100, Y = 100 });
                var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
                ed.Carregar(c);
                var rede = new Netlist(c);
                var b = Simulador.App.Bancada.DeUmaSo(MaquinaLigada(placa));
                for (int k = 0; k < 3; k++) ed.AplicarEntradas(b, rede);
                var avisos = ed.EstourosPelaEnergia().Where(e => e.Queimou).Select(e => e.Motivo).ToList();
                Conferir($"{def.Id}: sozinha, sem fio -- desenha, roda o quadro e nada queima",
                    avisos.Count == 0 && ed.Queimadas.Count == 0, string.Join(" / ", avisos));
            }
            catch (Exception e)
            {
                Conferir($"{def.Id}: sozinha, sem fio -- desenha, roda o quadro e nada queima", false, $"{e.GetType().Name}: {e.Message}");
            }

            // ---- cada pino ligado sozinho numa ESP32 ----
            // A protoboard e a PCB tem centenas de furos: os do canto e os do meio bastam.
            var pinosDaPeca = Catalogo.EhProtoboard(def.Id)
                ? def.Pinos.Where((_, i) => i == 0 || i == def.Pinos.Count / 2 || i == def.Pinos.Count - 1).ToList()
                : def.Pinos.ToList();
            foreach (var pino in pinosDaPeca)
            {
                ligacoes++;
                string n = pino.Nome;
                string naPlaca = Netlist.EhPinoDeTerra(n) ? "GND.1"
                    : Netlist.EhPinoDeAlimentacao(n) ? "3V3"
                    : "D4";
                try
                {
                    var c = new Circuito();
                    c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = placa.Id, X = 0, Y = 0, Usb = true });
                    c.Pecas.Add(new PecaNoCircuito { Id = "peca", Tipo = def.Id, X = 500, Y = 300 });
                    c.Fios.Add(new Fio { DePeca = "peca", DePino = n, ParaPeca = "placa", ParaPino = naPlaca });
                    var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
                    ed.Carregar(c);
                    var rede = new Netlist(c);
                    var maq = MaquinaLigada(placa);
                    var b = Simulador.App.Bancada.DeUmaSo(maq);
                    for (int k = 0; k < 2; k++) ed.AplicarEntradas(b, rede);
                    bool chegou = rede.RaizDe(Netlist.No("peca", n)) is { } raiz && raiz == rede.RaizDe(Netlist.No("placa", naPlaca));
                    Conferir($"{def.Id} {n}: ligado sozinho no {naPlaca} da ESP32 -- o fio chega e o quadro roda", chegou,
                        chegou ? "" : "o no do pino nao e o da placa");
                }
                catch (Exception e)
                {
                    Conferir($"{def.Id} {n}: ligado sozinho no {naPlaca} da ESP32 -- o fio chega e o quadro roda", false,
                        $"{e.GetType().Name}: {e.Message}");
                }
            }
        }
        saida.AppendLine($"        {pecas} pecas, {ligacoes} ligacoes pino a pino");
    }

    // ---------------------------------------------------------------------
    //  sem fio: o controle infravermelho e o radio
    // ---------------------------------------------------------------------

    static void SemFio_UmAUm()
    {
        saida.AppendLine("sem fio, um a um (o controle infravermelho e o radio)");

        // ---- cada tecla do controle infravermelho chega na placa (uma vez por aperto) ----
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true },
                new PecaNoCircuito { Id = "ir", Tipo = "ir-receiver", X = 400, Y = 0 },
                new PecaNoCircuito { Id = "controle", Tipo = "ir-remote", X = 700, Y = 0 }]);
            c.Fios.Add(new Fio { DePeca = "ir", DePino = "DAT", ParaPeca = "placa", ParaPino = "11" });
            c.Fios.Add(new Fio { DePeca = "ir", DePino = "VCC", ParaPeca = "placa", ParaPino = "5V" });
            c.Fios.Add(new Fio { DePeca = "ir", DePino = "GND", ParaPeca = "placa", ParaPino = "GND.1" });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = MaquinaLigada(PerfilPlaca.Uno);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            foreach (var (nome, codigo) in EditorCircuito.TeclasDoControleParaTeste())
            {
                ed.ApertarNoControle(c.Achar("controle"), nome);
                ed.AplicarEntradas(b, rede);
                long chegou = (long)maq.Leitura("ir:codigo");
                maq.Leituras["ir:codigo"] = 0;          // o resume() do sketch
                ed.AplicarEntradas(b, rede);             // segurando: nao repete o codigo no quadro seguinte
                long deNovo = (long)maq.Leitura("ir:codigo");
                ed.ApertarNoControle(c.Achar("controle"), null);
                ed.AplicarEntradas(b, rede);
                Conferir($"controle infravermelho, tecla {nome}: chega na placa como 0x{codigo:X6}, uma vez por aperto",
                    chegou == codigo && deNovo != codigo, $"chegou 0x{chegou:X}, segurando 0x{deNovo:X}");
            }
        }

        // ---- cada canal do radio, nas tres posicoes ----
        {
            var c = new Circuito();
            var controle = new PecaNoCircuito { Id = "controle", Tipo = "radio-controle", X = 900, Y = 0, Giro = 0.5 };
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true },
                new PecaNoCircuito { Id = "rx", Tipo = "receptor-rc", X = 500, Y = 0 }, controle]);
            c.Fios.Add(new Fio { DePeca = "rx", DePino = "VCC", ParaPeca = "placa", ParaPino = "VIN" });
            c.Fios.Add(new Fio { DePeca = "rx", DePino = "GND", ParaPeca = "placa", ParaPino = "GND.1" });
            int[] gpios = [13, 12, 14, 27, 26, 25];
            for (int k = 1; k <= 6; k++)
                c.Fios.Add(new Fio { DePeca = "rx", DePino = $"CH{k}", ParaPeca = "placa", ParaPino = $"D{gpios[k - 1]}" });
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = MaquinaLigada(PerfilPlaca.Esp32);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            void Posicao(int canal, double valor)
            {
                switch (canal)
                {
                    case 1: controle.Giro3 = valor; break;
                    case 2: controle.Giro2 = valor; break;
                    case 3: controle.Giro = valor; break;
                    case 4: controle.Giro4 = valor; break;
                    case 5: controle.Bits = valor > 0.5 ? controle.Bits | 1 : controle.Bits & ~1; break;
                    case 6: controle.Bits = valor > 0.5 ? controle.Bits | 2 : controle.Bits & ~2; break;
                }
            }
            for (int k = 1; k <= 6; k++)
                foreach (double posicao in k <= 4 ? new[] { 0.0, 0.5, 1.0 } : new[] { 0.0, 1.0 })
                {
                    Posicao(k, posicao);
                    for (int f = 0; f < 3; f++) { ed.AplicarEntradas(b, rede); ed.AvancarEnergiaParaTeste(b, rede, 0.02); }
                    var sinal = maq.SinaisExternos[gpios[k - 1]];
                    double largura = sinal?.Pulsos is { Length: 1 } p ? p[0].Largura : -1;
                    double esperado = 1000 + 1000 * posicao;
                    Conferir($"radio, canal {k} em {posicao * 100:0}%: o receptor poe {esperado:0} us no CH{k} (GPIO {gpios[k - 1]})",
                        Math.Abs(largura - esperado) < 1, $"{largura:0} us");
                    Posicao(k, k is 1 or 2 or 4 ? 0.5 : 0);
                }

            // Controle desligado: nenhum canal pulsa (o failsafe do iA6B).
            controle.Bits |= 4;
            for (int f = 0; f < 3; f++) { ed.AplicarEntradas(b, rede); ed.AvancarEnergiaParaTeste(b, rede, 0.02); }
            bool semPulso = gpios.All(g => maq.SinaisExternos[g] is null || maq.SinaisExternos[g].Pulsos.Length == 0);
            Conferir("radio: controle desligado, os seis canais param de pulsar (failsafe)", semPulso);
        }
    }
}
