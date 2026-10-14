namespace Simulador.App;

/// <summary>
/// O SPI do sketch interpretado: os bits saem nos pinos, no ritmo do
/// barramento, com a ordem e o modo pedidos -- e o que falta (begin, pino)
/// da o mesmo resultado que na placa.
/// </summary>
public static partial class Autoteste
{
    /// <summary>Roda o setup() no tempo virtual e devolve a maquina, para olhar os pinos.</summary>
    static (Maquina Maq, List<string> Serial, string Erro) RodarComPinos(string sketch, string placa, long tetoMs)
    {
        var prog = Compilador.CompilarProjeto(new Projeto { Sketch = sketch, Placa = placa, DoSimulador = Acervo.EhDoSimulador });
        var maq = new Maquina { Perfil = PerfilPlaca.De(placa) };
        maq.Reiniciar();
        maq.LimiteDoRelogioVirtual = tetoMs;
        var interp = new Interpretador(prog, maq) { LimitePassos = long.MaxValue };
        string erro = null;
        try
        {
            interp.Preparar();
            interp.Setup();
        }
        catch (ErroDeExecucao e) { erro = e.Message; }
        catch (ExecucaoInterrompida) { }
        var linhas = maq.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
        return (maq, linhas, erro);
    }

    static void Motor_Spi()
    {
        saida.AppendLine("o SPI do sketch");

        const string umByte = """
            #include <SPI.h>
            void setup() {
              Serial.begin(115200);
              SPI.begin();
              unsigned long t0 = micros();
              SPI.transfer(0xA5);
              unsigned long gasto = micros() - t0;
              SPI.beginTransaction(SPISettings(1000000, LSBFIRST, SPI_MODE3));
              SPI.transfer(0x01);
              SPI.endTransaction();
              byte buf[3] = {7, 8, 9};
              SPI.transfer(buf, 3);
              Serial.print("us: "); Serial.println(gasto);
              Serial.print("buf: "); Serial.println(buf[0] + buf[1] + buf[2]);
              Serial.println(IPAddress(10, 0, 0, 1));
            }
            void loop() {}
            """;
        var (maq, serial, erro) = RodarComPinos(umByte, "esp32-devkit-v1", 5_000);
        List<EventoPino> eventos;
        lock (maq.Trava) eventos = [.. maq.Eventos];

        // O MOSI da ESP32 e o GPIO 23; o SCK, o 18. O 0xA5 sai do bit mais
        // alto: 1 0 1 0 0 1 0 1 -- o MOSI muda 6 vezes (comeca em 0).
        var mosi = eventos.Where(e => e.Pino == 23).Select(e => e.Valor).ToList();
        int subidasDoSck = eventos.Count(e => e.Pino == 18 && e.Valor == 1);
        long us = NumeroDaLinha(serial, "us:");
        saida.AppendLine($"        0xA5: MOSI {string.Join("", mosi.Take(6))}..., {subidasDoSck} subidas do SCK, {us} us; {erro}");
        Conferir("spi: o byte sai no MOSI do bit mais alto para o mais baixo (0xA5 = 1 0 1 0 0 1 0 1)",
            erro is null && mosi.Count >= 6 && string.Join("", mosi.Take(6)) == "101010", string.Join("", mosi));
        Conferir("spi: a 1 MHz um byte leva ~8 us de barramento (7 a 14 us com a chamada)",
            us is >= 7 and <= 14, us.ToString());
        Conferir("spi: o transfer(buf, n) troca cada byte pelo que chegou no MISO (sem nada ligado, 0)",
            NumeroDaLinha(serial, "buf:") == 0, NumeroDaLinha(serial, "buf:").ToString());
        Conferir("spi: no modo 3 o SCK fica em repouso no nivel alto",
            maq.Digital[18] == 1, maq.Digital[18].ToString());
        Conferir("c++: IPAddress(10, 0, 0, 1) como expressao e um objeto temporario (antes: 'funcao nao suportada')",
            serial.Contains("10.0.0.1"), string.Join("|", serial));

        // No AVR, transfer() sem begin() espera o SPIF para sempre.
        const string semBegin = "#include <SPI.h>\nvoid setup() { Serial.begin(9600); Serial.println(\"antes\"); SPI.transfer(1); Serial.println(\"depois\"); }\nvoid loop() {}";
        var (maqUno, serialUno, _) = RodarComPinos(semBegin, "arduino-uno", 2_000);
        Conferir("spi no uno: transfer() antes do begin() trava a placa (o SPIF nunca chega), e o simulador avisa",
            serialUno.Contains("antes") && !serialUno.Contains("depois") && maqUno.Avisos.Any(a => a.Contains("antes do SPI.begin()")),
            $"{string.Join("|", serialUno)} / {string.Join(" / ", maqUno.Avisos)}");

        // SPI1 so existe onde o core declara (a Pico); na ESP32 o outro barramento e um SPIClass.
        var (_, _, erroSpi1) = RodarComPinos("#include <SPI.h>\nvoid setup() { SPI1.begin(); }\nvoid loop() {}", "esp32-devkit-v1", 1_000);
        Conferir("spi: SPI1 na ESP32 da o erro do core, e diz para usar SPIClass",
            erroSpi1?.Contains("SPIClass") == true, erroSpi1 ?? "rodou");
        var (_, serialPico, erroPico) = RodarComPinos("#include <SPI.h>\nvoid setup() { Serial.begin(115200); SPI1.begin(); SPI1.transfer(3); Serial.println(\"ok\"); }\nvoid loop() {}", "pi-pico", 1_000);
        Conferir("spi: na Pico o SPI1 existe", erroPico is null && serialPico.Contains("ok"), erroPico ?? string.Join("|", serialPico));
    }
}
