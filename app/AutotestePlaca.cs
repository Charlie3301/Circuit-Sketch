using System.IO;

namespace Simulador.App;

/// <summary>
/// A placa de verdade (Alpha 19): o visto (Verificar) e a seta (Mandar para
/// a placa) no canto da janela, com o compilador do Arduino que vem dentro
/// da Arduino IDE 2.
/// </summary>
public static partial class Autoteste
{
    static void Placa_DeVerdade()
    {
        saida.AppendLine("placa de verdade");

        // ---- toda placa do simulador sabe o nome dela no compilador ----
        var semNucleo = PerfilPlaca.Conhecidos.Where(p => PlacaDeVerdade.FqbnDe(p.Id) is null).Select(p => p.Id).ToList();
        Conferir("placa: toda placa tem o FQBN do núcleo do Arduino", semNucleo.Count == 0, string.Join(",", semNucleo));
        Conferir("placa: o núcleo sai do FQBN",
            PlacaDeVerdade.NucleoDe("esp32:esp32:esp32s3") == "esp32:esp32"
            && PlacaDeVerdade.NucleoDe("STMicroelectronics:stm32:GenF1:pnum=BLUEPILL_F103C8") == "STMicroelectronics:stm32",
            PlacaDeVerdade.NucleoDe("esp32:esp32:esp32s3"));

        // ---- as portas: o JSON do "board list" ----
        const string lista = """
            {
              "detected_ports": [
                { "port": { "address": "circuitsketch", "protocol": "circuitsketch", "protocol_label": "Simulador" } },
                { "port": { "address": "COM3", "protocol": "serial", "protocol_label": "Serial Port (USB)",
                            "properties": { "vid": "0x2341", "pid": "0x0043" } },
                  "matching_boards": [ { "name": "Arduino Uno", "fqbn": "arduino:avr:uno" } ] },
                { "port": { "address": "COM7", "protocol": "serial", "protocol_label": "Serial Port (USB)",
                            "properties": { "vid": "0x10C4", "pid": "0xEA60" } } }
              ]
            }
            """;
        var portas = PlacaDeVerdade.LerPortas(lista);
        saida.AppendLine($"        portas: {string.Join(" | ", portas.Select(p => $"{p.Endereco}={p.Placa}"))}");
        Conferir("placa: a porta do plugin do simulador fica de fora, as USB entram",
            portas.Count == 2 && portas[0].Endereco == "COM3" && portas[1].Endereco == "COM7",
            string.Join(",", portas.Select(p => p.Endereco)));
        Conferir("placa: o Uno reconhecido vem com o FQBN",
            portas[0].Placa == "Arduino Uno" && portas[0].Fqbn == "arduino:avr:uno", portas[0].ToString());
        Conferir("placa: a ESP32 generica aparece pelo conversor USB (CP210x)",
            portas[1].Placa?.Contains("CP210x") == true, portas[1].Placa);

        // ---- o que deu errado, do jeito que se entende ----
        string semBiblioteca = PlacaDeVerdade.Explicar(
            "C:\\x\\sketch.ino:1:10: fatal error: DHT.h: No such file or directory", null, "esp32:esp32:esp32");
        string ocupada = PlacaDeVerdade.Explicar("could not open port 'COM3': PermissionError(13, 'Access is denied.')",
                                                 "COM3", "arduino:avr:uno");
        string boot = PlacaDeVerdade.Explicar("A fatal error occurred: Failed to connect to ESP32: No serial data received.",
                                              "COM7", "esp32:esp32:esp32");
        string linha = PlacaDeVerdade.Explicar("C:\\x\\sketch\\sketch.ino:7:3: error: 'x' was not declared in this scope",
                                               null, "arduino:avr:uno");
        saida.AppendLine($"        sem biblioteca: {semBiblioteca}");
        Conferir("placa: biblioteca que falta na IDE aponta o nome dela", semBiblioteca.Contains("DHT sensor library"),
            semBiblioteca);
        Conferir("placa: porta ocupada manda fechar o monitor da IDE", ocupada.Contains("ocupada"), ocupada);
        Conferir("placa: a ESP32 que nao grava explica o botao BOOT", boot.Contains("BOOT"), boot);
        Conferir("placa: erro de compilacao diz a linha", linha.StartsWith("linha 7:"), linha);

        // ---- a pasta como a IDE espera ----
        string pasta = PlacaDeVerdade.PrepararPasta("meu projeto",
            "void setup() {}\nvoid loop() {}\n",
            new Dictionary<string, string> { ["config.h"] = "#define X 1", ["diagram.json"] = "{}" });
        Conferir("placa: a pasta tem nome/nome.ino e as abas, sem o diagrama",
            File.Exists(Path.Combine(pasta, "meu_projeto.ino")) && File.Exists(Path.Combine(pasta, "config.h"))
            && !File.Exists(Path.Combine(pasta, "diagram.json")),
            string.Join(",", Directory.GetFiles(pasta).Select(Path.GetFileName)));

        // ---- o compilador de verdade, pela janela ----
        if (PlacaDeVerdade.Cli is null || !PlacaDeVerdade.Nucleos().Contains("arduino:avr"))
        {
            saida.AppendLine("        (sem a Arduino IDE com o núcleo arduino:avr: a compilação de verdade ficou de fora)");
            return;
        }

        var janela = new MainWindow();
        try
        {
            var circuito = new Circuito();
            circuito.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0 });
            janela.TesteAbrir(circuito, """
                void setup() { pinMode(13, OUTPUT); }
                void loop() { digitalWrite(13, HIGH); delay(500); digitalWrite(13, LOW); delay(500); }
                """);
            janela.TesteVerificar();
            bool terminou = EsperarPor(() => !janela.TesteTrabalhandoNaPlaca, 240_000);
            var certo = janela.TesteUltimoDaPlaca;
            saida.AppendLine($"        compilou de verdade: {certo?.Ok} — {certo?.Memoria}");
            Conferir("placa: o visto compila com o compilador do Arduino e mostra a memoria",
                terminou && certo is { Ok: true } && certo.Memoria?.Contains("bytes") == true,
                certo?.Resumo + " / " + janela.TesteLogDaPlaca);

            // Um circuito novo: a placa guarda o codigo dela, e reabrir o
            // MESMO objeto manteria o sketch anterior.
            var outro = new Circuito();
            outro.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0 });
            janela.TesteAbrir(outro, "void setup() {\n  pinMode(13, OUTPUT);\n  naoExiste = 3;\n}\nvoid loop() {}\n");
            janela.TesteVerificar();
            terminou = EsperarPor(() => !janela.TesteTrabalhandoNaPlaca, 240_000);
            var errado = janela.TesteUltimoDaPlaca;
            saida.AppendLine($"        com erro: {errado?.Resumo}");
            Conferir("placa: o erro do compilador de verdade diz a linha do sketch",
                terminou && errado is { Ok: false } && errado.Resumo.StartsWith("linha 3:"), errado?.Resumo);
        }
        finally
        {
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }
}
