using System.IO;

namespace Simulador.App;

/// <summary>
/// A placa de verdade na USB (Alpha 25): o menu USB ao lado do visto, que
/// acha a placa sozinho; o monitor serial da placa; e o monitor serial da
/// Arduino IDE falando com o sketch simulado nos dois sentidos.
/// </summary>
public static partial class Autoteste
{
    static void Placa_Usb()
    {
        saida.AppendLine("placa de verdade na USB");

        // ---- quem esta do outro lado do cabo ----
        Conferir("usb: o Uno pelo VID/PID, sem precisar do arduino-cli",
            PlacaDeVerdade.NomePeloUsb("0x2341", "0x0043") == "Arduino Uno"
            && PlacaDeVerdade.NomePeloUsb("0x2341", "0x0042") == "Arduino Mega 2560",
            PlacaDeVerdade.NomePeloUsb("0x2341", "0x0043"));
        Conferir("usb: a ESP32 generica pelo conversor (CP210x, CH340)",
            PlacaDeVerdade.NomePeloUsb("0x10c4", "0xea60")?.Contains("CP210x") == true
            && PlacaDeVerdade.NomePeloUsb("0x1A86", "0x7523")?.Contains("CH340") == true
            && PlacaDeVerdade.NomePeloUsb("0x9999", "0x0001") is null,
            PlacaDeVerdade.NomePeloUsb("0x10c4", "0xea60"));

        // A DevKit generica ("esp32") nao tem LED_BUILTIN: o exemplo mais
        // simples do mundo nao compilava. A DOIT DevKit V1 tem (GPIO 2).
        Conferir("usb: a ESP32 DevKit V1 compila como DOIT ESP32 DEVKIT V1 (que tem LED_BUILTIN)",
            PlacaDeVerdade.FqbnDe("esp32-devkit-v1") == "esp32:esp32:esp32doit-devkit-v1",
            PlacaDeVerdade.FqbnDe("esp32-devkit-v1"));

        string colorido = PlacaDeVerdade.SemCores("\u001b[92mUsed library\u001b[0m DHT \u001b[2K\u001b[?25lok");
        Conferir("usb: o log do arduino-cli chega sem os codigos de cor", colorido == "Used library DHT ok", colorido);

        Conferir("usb: o monitor da placa abre no baud do Serial.begin do sketch",
            PlacaDeVerdade.BaudDoSketch("void setup() {\n  Serial . begin( 9600 );\n}", false) == 9600
            && PlacaDeVerdade.BaudDoSketch("void setup() {}", true) == 9600
            && PlacaDeVerdade.BaudDoSketch(null, false) == 115200,
            $"{PlacaDeVerdade.BaudDoSketch("Serial.begin(9600);", false)}");

        // ---- a escolha da porta, pela janela ----
        var janela = new MainWindow();
        try
        {
            var circuito = new Circuito();
            circuito.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0 });
            janela.TesteAbrir(circuito, "void setup() {}\nvoid loop() {}\n");

            var com3 = new PlacaDeVerdade.Porta("COM3", "Serial Port", null, null);
            var com5 = new PlacaDeVerdade.Porta("COM5", "Serial Port (USB)", "Arduino Uno", "arduino:avr:uno");
            var com9 = new PlacaDeVerdade.Porta("COM9", "Serial Port (USB)", "conversor CP210x", null);

            janela.TestePortaEscolhida = null;
            janela.TesteAplicarPortas([]);
            string vazio = janela.TesteRotuloDaUsb;
            janela.TesteAplicarPortas([com3]);
            string soCom3 = janela.TesteRotuloDaUsb;
            Conferir("usb: sem placa, o botao diz; uma porta que nao e placa conhecida nao e escolhida sozinha",
                vazio == Idioma.T("nenhuma placa na USB") && janela.TestePortaEscolhida is null
                && soCom3 == Idioma.T("{0} porta(s) — escolha uma", 1),
                $"{vazio} / {soCom3}");

            janela.TesteAplicarPortas([com3, com5]);
            Conferir("usb: a placa que acabou de ser ligada fica escolhida (como na Arduino IDE)",
                janela.TestePortaEscolhida == "COM5" && janela.TesteRotuloDaUsb == "COM5 — Arduino Uno",
                janela.TesteRotuloDaUsb);

            janela.TesteAplicarPortas([com3, com5, com9]);
            Conferir("usb: ligar outra placa nao tira a escolha da primeira",
                janela.TestePortaEscolhida == "COM5", janela.TestePortaEscolhida);

            janela.TesteAplicarPortas([com3, com9]);
            string desligada = janela.TesteRotuloDaUsb;
            janela.TesteAplicarPortas([com3, com5, com9]);
            Conferir("usb: a placa que sai da USB continua escolhida (desligada) e volta sozinha",
                desligada == Idioma.T("{0} (desligada)", "COM5") && janela.TesteRotuloDaUsb == "COM5 — Arduino Uno",
                $"{desligada} / {janela.TesteRotuloDaUsb}");

            // ---- o monitor da placa: a placa manda aos pedacos ----
            janela.TesteAcrescentarNaPlaca("ola ");
            janela.TesteAcrescentarNaPlaca("mundo\r\nlin");
            janela.TesteAcrescentarNaPlaca("ha 2\n");
            string log = janela.TesteLogDaPlaca;
            Conferir("usb: o monitor da placa junta os pedacos em linhas",
                log.EndsWith("ola mundo\nlinha 2\n"), log.Replace("\n", " | "));
        }
        finally
        {
            try { janela.Close(); } catch (InvalidOperationException) { }
        }

        // ---- a ESP32 DevKit com o compilador de verdade ----
        if (PlacaDeVerdade.Cli is null || !PlacaDeVerdade.Nucleos().Contains("esp32:esp32"))
        {
            saida.AppendLine("        (sem o núcleo esp32:esp32 no arduino-cli: a compilação da ESP32 ficou de fora)");
            return;
        }
        var outra = new MainWindow();
        try
        {
            var circuito = new Circuito();
            circuito.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            outra.TesteAbrir(circuito, """
                void setup() { pinMode(LED_BUILTIN, OUTPUT); Serial.begin(115200); }
                void loop() { digitalWrite(LED_BUILTIN, !digitalRead(LED_BUILTIN)); delay(500); }
                """);
            outra.TesteVerificar();
            bool terminou = EsperarPor(() => !outra.TesteTrabalhandoNaPlaca, 900_000);
            var certo = outra.TesteUltimoDaPlaca;
            saida.AppendLine($"        ESP32 compilou de verdade: {certo?.Ok} — {certo?.Memoria}");
            Conferir("usb: o pisca com LED_BUILTIN compila de verdade para a ESP32 DevKit V1",
                terminou && certo is { Ok: true }, certo?.Resumo + " / " + outra.TesteLogDaPlaca);
        }
        finally
        {
            try { outra.Close(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>
    /// O monitor serial da Arduino IDE no sentido de la para ca: o que se
    /// digita nele vai para entrada.txt e o simulador entrega ao sketch,
    /// como o monitor do proprio programa entrega.
    /// </summary>
    static void Monitor_DaIde()
    {
        saida.AppendLine("monitor serial da Arduino IDE para o sketch");

        // Quem roda o autoteste pode estar com o programa aberto: guarda e devolve.
        string serial = MonitorIde.Arquivo, entrada = MonitorIde.Entrada;
        string antesSerial = File.Exists(serial) ? File.ReadAllText(serial) : null;
        string antesEntrada = File.Exists(entrada) ? File.ReadAllText(entrada) : null;

        const string sketch = """
            void setup() {
              Serial.begin(9600);
            }
            void loop() {
              Serial.println("ola mundo");
              if (Serial.available()) {
                String s = Serial.readStringUntil('\n');
                s.trim();
                Serial.print("li: ");
                Serial.println(s);
              }
              delay(150);
            }
            """;
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true, Sketch = sketch });
        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(c, sketch);
            janela.TesteRodar();
            EsperarPor(() => janela.TesteSerial.Contains("ola mundo"), 15000);
            Conferir("monitor da IDE: rodar zera a entrada",
                File.Exists(entrada) && new FileInfo(entrada).Length == 0, entrada);

            // O cano do plugin escreve assim: anexando, com a IDE ainda escrevendo.
            using (var cano = new FileStream(entrada, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                cano.Write(System.Text.Encoding.UTF8.GetBytes("abc\n"));
            janela.TesteReceberDoMonitorDaIde();
            EsperarPor(() => janela.TesteSerial.Contains("li: abc"), 6000);
            Conferir("monitor da IDE: o que se digita na IDE chega no sketch (e aparece no monitor do simulador)",
                janela.TesteSerial.Contains("> abc") && janela.TesteSerial.Contains("li: abc"),
                string.Join(" / ", janela.TesteSerial.Split(Environment.NewLine).TakeLast(4)));

            janela.TesteAtualizarSerial();
            string paraAIde = File.ReadAllText(serial);
            Conferir("monitor da IDE: a resposta do sketch volta para a IDE, sem o carimbo de tempo",
                paraAIde.Contains("li: abc") && !paraAIde.Contains("ms] li: abc"),
                string.Join(" | ", paraAIde.Split('\n').TakeLast(3)));

            Conferir("monitor da IDE: o que ja foi entregue nao chega de novo", MonitorIde.LerEntrada() is null, "");
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
            try
            {
                if (antesSerial is null) File.Delete(serial); else File.WriteAllText(serial, antesSerial);
                if (antesEntrada is null) File.Delete(entrada); else File.WriteAllText(entrada, antesEntrada);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
