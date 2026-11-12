namespace Simulador.App;

/// <summary>
/// O baud do monitor serial: segue o Serial.begin ate alguem escolher; no
/// baud errado o texto sai embaralhado nos dois sentidos, como na IDE; e a
/// escolha vai no arquivo do projeto, por placa.
/// </summary>
public static partial class Autoteste
{
    static void Monitor_Baud()
    {
        saida.AppendLine("o baud do monitor serial");
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
            var maq = janela.TesteMaquinaDa("uno");
            var peca = janela.TesteEditorCircuito.Atual.Achar("uno");
            string[] Ultimas(int n) => [.. janela.TesteSerial.Split(Environment.NewLine).TakeLast(n)];

            EsperarPor(() => janela.TesteSerial.Contains("ola mundo"), 15000);
            bool segue = janela.TesteSerial.Contains("ola mundo") && janela.TesteBaudDoMonitor == "9600 baud" && peca.BaudDoMonitor is null;
            janela.TesteEnviarNoMonitor("abc");
            EsperarPor(() => janela.TesteSerial.Contains("li: abc"), 6000);
            Conferir("monitor: sem escolha, segue o Serial.begin (9600 baud): o texto sai certo nos dois sentidos",
                segue && janela.TesteSerial.Contains("li: abc"), $"{janela.TesteBaudDoMonitor} | {string.Join(" / ", Ultimas(4))}");

            janela.EscolherBaud(peca, 115200);
            int antes = janela.TesteSerial.Split(Environment.NewLine).Length;
            EsperarPor(() => janela.TesteSerial.Split(Environment.NewLine).Length >= antes + 4, 6000);
            var depois = janela.TesteSerial.Split(Environment.NewLine).Skip(antes).ToList();
            janela.TesteEnviarNoMonitor("xyz");
            EsperarPor(() => LinhasDoSerial(maq).Any(l => l.Contains("li: xyz")), 1200);
            bool lixo = depois.Count >= 3 && depois.All(l => !l.Contains("ola mundo")) && janela.TesteBaudDoMonitor == "115200 baud";
            bool naoChegou = !LinhasDoSerial(maq).Any(l => l.Contains("li: xyz"));
            Conferir("monitor a 115200 com o sketch a 9600: o texto sai embaralhado, e o que se digita chega trocado no sketch",
                lixo && naoChegou, $"{janela.TesteBaudDoMonitor} | {string.Join(" / ", depois.Take(3))}");

            string json = janela.TesteEditorCircuito.Atual.ParaJson();
            var volta = Circuito.DeJson(json).Achar("uno");
            Conferir("monitor: o baud escolhido fica na placa e vai no arquivo do projeto",
                peca.BaudDoMonitor == 115200 && json.Contains("\"BaudDoMonitor\": 115200") && volta?.BaudDoMonitor == 115200,
                $"{peca.BaudDoMonitor} / {volta?.BaudDoMonitor}");

            janela.EscolherBaud(peca, null);
            janela.TesteEnviarNoMonitor("de novo");
            // O painel anda no ritmo da tela: espera a resposta chegar nele, e uma linha depois dela.
            EsperarPor(() => janela.TesteSerial.Split(Environment.NewLine).SkipWhile(l => !l.Contains("li: de novo")).Count() >= 2, 6000);
            var finais = janela.TesteSerial.Split(Environment.NewLine).SkipWhile(l => !l.Contains("li: de novo")).ToList();
            Conferir("monitor: voltando a seguir o Serial.begin, o texto volta a sair certo",
                peca.BaudDoMonitor is null && finais.Count >= 2 && finais.Skip(1).All(l => l.Contains("ola mundo") || l.Contains("li: ")),
                string.Join(" / ", finais.Take(3)));
        }
        finally { janela.TesteParar(); }
    }
}
