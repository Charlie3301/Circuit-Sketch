namespace Simulador.App;

/// <summary>
/// Os selos de aviso no circuito: cada aviso vermelho de embaixo do codigo vira um
/// selo em cima da peca (ou do pino), varios ao mesmo tempo; o que queimou fica com
/// o selo vermelho ate a proxima rodada.
/// </summary>
public static partial class Autoteste
{
    static void Avisos_SelosNoCircuito()
    {
        saida.AppendLine("os selos de aviso no circuito");

        Conferir("selos: o texto curto vem do comeco do motivo (e o \"curto:\" leva o resto junto)",
            EditorCircuito.TextoDoSelo(new Estouro("x", "LED direto no pino 14, sem resistor. A porta de 3,3 V segura...", false))
                == "LED direto no pino 14, sem resistor"
            && EditorCircuito.TextoDoSelo(new Estouro("x", "curto: alimentação ligada direto no GND")) == "Curto: alimentação ligada direto no GND"
            && EditorCircuito.TextoDoSelo(new Estouro("x", "qualquer coisa", false) { Marca = "5 V numa porta de 3,3 V" }) == "5 V numa porta de 3,3 V",
            EditorCircuito.TextoDoSelo(new Estouro("x", "curto: alimentação ligada direto no GND")));

        // A ESP32 com tres erros diferentes: o LED sem resistor no D14, o DS18B20 sem
        // pull-up e o HC-SR04 no 3V3. E o Uno com um LED direto no 13 (esse queima).
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 40, Y = 40, Usb = true,
                                          Sketch = "void setup() { pinMode(14, OUTPUT); digitalWrite(14, HIGH); }\nvoid loop() { delay(50); }" });
        c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 600, Y = 40, Usb = true,
                                          Sketch = "void setup() { pinMode(13, OUTPUT); digitalWrite(13, HIGH); }\nvoid loop() { delay(50); }" });
        c.Pecas.Add(new PecaNoCircuito { Id = "led1", Tipo = "led", X = 300, Y = 60 });
        c.Pecas.Add(new PecaNoCircuito { Id = "led2", Tipo = "led", X = 900, Y = 300 });
        c.Pecas.Add(new PecaNoCircuito { Id = "ds", Tipo = "ds18b20", X = 300, Y = 220 });
        c.Pecas.Add(new PecaNoCircuito { Id = "hc", Tipo = "hc-sr04", X = 80, Y = 360 });
        void F(string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        F("esp", "D14", "led1", "A"); F("led1", "C", "esp", "GND.2");
        F("ds", "VCC", "esp", "3V3"); F("ds", "GND", "esp", "GND.1"); F("ds", "DQ", "esp", "D4");
        F("hc", "VCC", "esp", "3V3"); F("hc", "GND", "esp", "GND.1");
        F("uno", "13", "led2", "A"); F("led2", "C", "uno", "GND.1");

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(c, "void setup() { pinMode(14, OUTPUT); digitalWrite(14, HIGH); }\nvoid loop() { delay(50); }");
            janela.TesteRodar();
            var ed = janela.TesteEditorCircuito;
            EsperarPor(() => ed.AvisosNaTelaParaTeste.Count >= 4, 10000);
            var selos = ed.AvisosNaTelaParaTeste;
            string lista = string.Join(" | ", selos);
            saida.AppendLine("        selos: " + lista);
            Conferir("selos: varios avisos de tipos diferentes ao mesmo tempo, cada um na sua peca -- o LED sem resistor (na peca), "
                     + "o pull-up do DS18B20 (no DQ) e o HC-SR04 no 3V3 (no VCC)",
                selos.Any(s => s.Peca == "led1" && s.Pino is null && s.Marca.Contains("sem resistor"))
                && selos.Any(s => s.Peca == "ds" && s.Pino == "DQ" && s.Marca.Contains("pull-up"))
                && selos.Any(s => s.Peca == "hc" && s.Pino == "VCC"),
                lista);
            Conferir("selos: o LED que queimou no 5 V do Uno fica com o selo (vermelho) ate a proxima rodada",
                ed.EstaQueimada("led2") && selos.Any(s => s.Peca == "led2"), lista);

            // ---- o X: fecha o aviso, e ele nao volta nos quadros seguintes ----
            bool fechou = ed.FecharAvisoParaTeste("led1", null);
            EsperarPor(() => false, 600);       // alguns quadros da simulacao
            var depois = ed.AvisosNaTelaParaTeste;
            Conferir("selos: o X fecha aquele aviso (e so ele), e os quadros seguintes nao o trazem de volta",
                fechou && !depois.Any(s => s.Peca == "led1") && depois.Any(s => s.Peca == "ds") && depois.Any(s => s.Peca == "hc"),
                string.Join(" | ", depois));
            // ---- o projeto guarda o fechado: salvo, reaberto, ele continua fechado ----
            var guardados = ed.Atual.AvisosFechados?.ToList() ?? [];
            string arquivo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"circuitsketch-selos-{Guid.NewGuid():N}.sktch");
            List<string> reabertos;
            List<string> pelaAba;
            try
            {
                Sktch.Salvar(arquivo, ed.Atual, "void setup() {}\nvoid loop() {}");
                reabertos = Sktch.Abrir(arquivo).circuito.AvisosFechados ?? [];
                pelaAba = ImportarWokwi.Ler(ExportarWokwi.Gerar(ed.Atual), ed.Atual, estrito: true).Circuito.AvisosFechados ?? [];
            }
            finally { try { System.IO.File.Delete(arquivo); } catch { } }
            Conferir("selos: o fechado vai no .sktch (\"led1||LED direto no pino 14, sem resistor\"), volta ao reabrir, e a aba do diagram.json nao o perde",
                guardados.SequenceEqual(["led1||LED direto no pino 14, sem resistor"]) && reabertos.SequenceEqual(guardados)
                && pelaAba.SequenceEqual(guardados),
                $"guardados={string.Join(";", guardados)} reabertos={string.Join(";", reabertos)} aba={string.Join(";", pelaAba)}");

            ed.EsquecerAvisosFechados();
            EsperarPor(() => ed.AvisosNaTelaParaTeste.Any(s => s.Peca == "led1"), 3000);
            Conferir("selos: \"mostrar de novo os avisos fechados\" traz o aviso de volta",
                ed.AvisosNaTelaParaTeste.Any(s => s.Peca == "led1"), string.Join(" | ", ed.AvisosNaTelaParaTeste));

            // ---- a opcao das configuracoes: vem ligada, e desligada some com todos ----
            bool deFabrica = Preferencias.Ligado(Preferencias.SelosDeAviso, true);
            Preferencias.Definir(Preferencias.SelosDeAviso, false);
            EsperarPor(() => ed.AvisosNaTelaParaTeste.Count == 0, 3000);
            int comElaDesligada = ed.AvisosNaTelaParaTeste.Count;
            Preferencias.Definir(Preferencias.SelosDeAviso, true);
            EsperarPor(() => ed.AvisosNaTelaParaTeste.Count >= 4, 3000);
            Conferir("selos: a opcao \"Mostrar os avisos em cima das peças\" vem ligada; desligada, os selos somem; ligada, voltam",
                deFabrica && comElaDesligada == 0 && ed.AvisosNaTelaParaTeste.Count >= 4,
                $"de fabrica={deFabrica} desligada={comElaDesligada} ligada={ed.AvisosNaTelaParaTeste.Count}");

            janela.TesteParar();
            janela.TesteRodar();
            EsperarPor(() => ed.AvisosNaTelaParaTeste.Count >= 4, 10000);
            Conferir("selos: rodar de novo recomeca (o que queimou volta inteiro, e se o erro continua, o selo volta)",
                ed.AvisosNaTelaParaTeste.Any(s => s.Peca == "led1"), string.Join(" | ", ed.AvisosNaTelaParaTeste));
        }
        finally { janela.TesteParar(); }
    }
}
