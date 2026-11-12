namespace Simulador.App;

/// <summary>
/// O DS18B20: o fio de 1-Wire com o chip modelado (a ROM, a busca, o
/// scratchpad com CRC, a conversao no tempo da resolucao, os 85 graus de
/// quando liga, o pull-up que o fio precisa), visto pelo OneWire e pelo
/// DallasTemperature do C++ e pelo onewire/ds18x20 do MicroPython.
/// </summary>
public static partial class Autoteste
{
    static void Sensor_Ds18b20()
    {
        saida.AppendLine("o sensor de temperatura DS18B20 (1-Wire)");
        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static double GiroDe(double graus) => (graus + 55) / 180;

        // ---- a peca ----
        {
            var d = Catalogo.Obter("ds18b20");
            Conferir("DS18B20: o TO-92 com as pernas GND, DQ e VCC (a ordem da ficha), na categoria Sensores, e o wokwi-ds18b20",
                d is not null && Pecas.Existe("ds18b20") && d.Pinos.Select(p => p.Nome).SequenceEqual(["GND", "DQ", "VCC"])
                && d.Categoria == "Sensores" && FormatoWokwi.TipoDoWokwi("wokwi-ds18b20") == "ds18b20"
                && FormatoWokwi.TipoParaWokwi("ds18b20") == "wokwi-ds18b20",
                d is null ? "(sem a peca)" : string.Join(",", d.Pinos.Select(p => p.Nome)));
        }

        // A placa com os sensores no pino do DQ (o VCC no 5V ou no 3V3), com ou sem o resistor de
        // 4,7 kohm do DQ para o VCC; o sketch em C++ pelo interpretador ou pelo compilador nativo
        // (o da janela), e o Python sempre pelo MicroPython de verdade (o nativo).
        bool nativoDisponivel = CompiladorNativo.Disponivel;
        (List<string> Linhas, Maquina Maq, string Status) Rodar(string placa, string pino, string vcc, string sketch, double[] graus,
                                                                bool pullUp, bool nativo)
        {
            bool eraNativo = Compilador.CppNativo;
            Compilador.CppNativo = nativo;
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = placa, X = 0, Y = 0, Usb = true, Sketch = sketch });
            for (int i = 0; i < graus.Length; i++)
            {
                string id = $"ds{i + 1}";
                c.Pecas.Add(new PecaNoCircuito { Id = id, Tipo = "ds18b20", X = 500 + 60 * i, Y = 100, Giro = GiroDe(graus[i]) });
                Ligar(c, id, "GND", "placa", "GND.1"); Ligar(c, id, "VCC", "placa", vcc); Ligar(c, id, "DQ", "placa", pino);
            }
            if (pullUp)
            {
                c.Pecas.Add(new PecaNoCircuito { Id = "r1", Tipo = "resistor", X = 500, Y = 300, Ohms = 4700 });
                Ligar(c, "r1", "1", "placa", pino); Ligar(c, "r1", "2", "placa", vcc);
            }
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, sketch);
                janela.TesteRodar();
                var maq = janela.TesteMaquinaDa("placa");
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto") || !janela.TestePlacaRodando("placa"), 20000);
                string avisos;
                lock (maq.Trava) avisos = string.Join(" / ", maq.Avisos);
                return (LinhasDoSerial(maq), maq, $"status: {janela.TesteStatus} | avisos: {avisos}");
            }
            finally
            {
                janela.TesteParar();
                Compilador.CppNativo = eraNativo;
            }
        }

        const string dallas = """
            #include <OneWire.h>
            #include <DallasTemperature.h>
            OneWire fio(2);
            DallasTemperature sensores(&fio);
            void setup() {
              Serial.begin(115200);
              sensores.begin();
              Serial.print("achados ");
              Serial.println(sensores.getDeviceCount());
              DeviceAddress a;
              if (sensores.getAddress(a, 0)) {
                Serial.print("antes ");
                Serial.println(sensores.getTempC(a));
              }
              unsigned long t0 = millis();
              sensores.requestTemperatures();
              Serial.print("esperou ");
              Serial.println(millis() - t0);
              Serial.print("temp ");
              Serial.println(sensores.getTempCByIndex(0), 4);
              sensores.setResolution(9);
              t0 = millis();
              sensores.requestTemperatures();
              Serial.print("esperou9 ");
              Serial.println(millis() - t0);
              Serial.print("temp9 ");
              Serial.println(sensores.getTempCByIndex(0), 4);
              Serial.println("pronto");
            }
            void loop() {}
            """;
        foreach (bool nativo in nativoDisponivel ? new[] { false, true } : [false])
        {
            string como = nativo ? "nativo" : "interpretador";
            var (linhas, _, status) = Rodar("arduino-uno", "2", "5V", dallas, [30.3], pullUp: true, nativo);
            string tudo = string.Join(" | ", linhas) + " || " + status;
            int Numero(string prefixo) => linhas.FirstOrDefault(l => l.StartsWith(prefixo)) is { } l
                && int.TryParse(l[prefixo.Length..].Trim(), out int n) ? n : -1;
            Conferir($"DS18B20 no Uno (OneWire + DallasTemperature, {como}): achado no begin(), e sem converter o scratchpad tem os 85 graus de quando liga",
                linhas.Contains("achados 1") && linhas.Contains("antes 85.00"), tudo);
            int esperou = Numero("esperou "), esperou9 = Numero("esperou9 ");
            Conferir($"DS18B20 ({como}): em 12 bits a conversao leva ~750 ms e da 30,3125 (o passo de 0,0625); em 9 bits, ~94 ms e 30,0 (o passo de 0,5)",
                linhas.Contains("temp 30.3125") && linhas.Contains("temp9 30.0000") && esperou is >= 700 and <= 950 && esperou9 is >= 60 and <= 250,
                tudo);
        }
        {
            var (linhas, maq, status) = Rodar("arduino-uno", "2", "5V", dallas, [30.3], pullUp: false, nativoDisponivel);
            bool avisou;
            lock (maq.Trava) avisou = maq.Avisos.Any(a => a.Contains("pull-up"));
            Conferir("DS18B20 sem o resistor de pull-up: o fio nao sobe, nenhum sensor responde, -127 graus (DEVICE_DISCONNECTED_C) e o aviso",
                linhas.Contains("achados 0") && linhas.Contains("temp -127.0000") && avisou, string.Join(" | ", linhas) + " || " + status);
        }
        foreach (bool nativo in nativoDisponivel ? new[] { false, true } : [false])
        {
            const string dois = """
                #include <OneWire.h>
                #include <DallasTemperature.h>
                OneWire fio(2);
                DallasTemperature sensores(&fio);
                void setup() {
                  Serial.begin(115200);
                  sensores.begin();
                  Serial.print("achados ");
                  Serial.println(sensores.getDeviceCount());
                  sensores.requestTemperatures();
                  for (int i = 0; i < sensores.getDeviceCount(); i++) {
                    DeviceAddress a;
                    sensores.getAddress(a, i);
                    Serial.print("sensor ");
                    for (int k = 0; k < 8; k++) { if (a[k] < 16) Serial.print("0"); Serial.print(a[k], HEX); }
                    Serial.print(" crc ");
                    Serial.print(OneWire::crc8(a, 7) == a[7] ? "ok" : "ruim");
                    Serial.print(" ");
                    Serial.println(sensores.getTempC(a), 2);
                  }
                  Serial.println("pronto");
                }
                void loop() {}
                """;
            var (linhas, _, status) = Rodar("esp32-devkit-v1", "D4", "3V3", dois.Replace("fio(2)", "fio(4)"), [25.0, 40.0], pullUp: true, nativo);
            var sensores = linhas.Where(l => l.StartsWith("sensor ")).ToList();
            var enderecos = sensores.Select(l => l.Split(' ')[1]).Distinct().ToList();
            Conferir($"DS18B20: dois no mesmo fio (a ESP32, {(nativo ? "nativo" : "interpretador")}) -- a busca da ROM acha os dois, cada endereco com a familia 28 e o CRC certo, e cada um com a sua temperatura",
                linhas.Contains("achados 2") && enderecos.Count == 2 && enderecos.All(e => e.StartsWith("28"))
                && sensores.All(l => l.Contains(" crc ok ")) && sensores.Any(l => l.EndsWith(" 25.00")) && sensores.Any(l => l.EndsWith(" 40.00")),
                string.Join(" | ", linhas) + " || " + status);
        }
        if (nativoDisponivel)
        {
            const string python = """
                import machine, onewire, ds18x20, time
                ow = onewire.OneWire(machine.Pin(4))
                ds = ds18x20.DS18X20(ow)
                roms = ds.scan()
                print('roms', len(roms))
                ds.convert_temp()
                time.sleep_ms(750)
                for r in roms:
                    print('temp', ds.read_temp(r))
                print('pronto')
                """;
            var (linhas, _, status) = Rodar("esp32-devkit-v1", "D4", "3V3", python, [22.5], pullUp: true, nativo: true);
            Conferir("DS18B20 no MicroPython: o onewire e o ds18x20 de verdade acham o sensor, convertem e leem 22,5 graus",
                linhas.Contains("roms 1") && linhas.Contains("temp 22.5"), string.Join(" | ", linhas) + " || " + status);
        }
    }
}
