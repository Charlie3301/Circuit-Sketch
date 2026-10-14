namespace Simulador.App;

/// <summary>
/// O que a demo DOOM (Alpha 19, item 11) puxou do motor: o sizeof em
/// bytes, o vetor de duas dimensoes, o sono profundo com a memoria RTC, o
/// ESP.restart() e o texto do TFT em pixels.
/// </summary>
public static partial class Autoteste
{
    /// <summary>
    /// Roda como a placa roda: ESP.restart() e o despertar do sono profundo
    /// comecam uma rodada nova, do setup(). O que cada rodada escreveu no
    /// serial fica, em ordem.
    /// </summary>
    static (List<string> Serial, List<ReinicioDaPlaca> Reinicios, Maquina Maq, string Erro)
        RodarComReinicios(string sketch, string placa, long ms, Action<Maquina> preparar = null)
    {
        var serial = new List<string>();
        var reinicios = new List<ReinicioDaPlaca>();
        var maq = new Maquina { Perfil = PerfilPlaca.De(placa) ?? PerfilPlaca.Esp32 };
        preparar?.Invoke(maq);

        try
        {
            var prog = Compilador.CompilarProjeto(new Projeto
            {
                Sketch = sketch,
                Placa = placa,
                DoSimulador = Acervo.EhDoSimulador,
            });

            ReinicioDaPlaca origem = null;
            long gasto = 0;
            while (true)
            {
                maq.LimiteDoRelogioVirtual = Math.Max(1, ms - gasto);
                var interp = new Interpretador(prog, maq) { Origem = origem };
                try
                {
                    interp.Preparar();
                    interp.Setup();
                    long antes = maq.Ms;
                    while (interp.TemLoop && maq.Ms + gasto < ms && !maq.Parar)
                    {
                        interp.Loop();
                        if (maq.Ms == antes) maq.Avancar(1);
                        antes = maq.Ms;
                    }
                    break;
                }
                catch (ReinicioDaPlaca r)
                {
                    serial.AddRange(LinhasDoSerial(maq));
                    reinicios.Add(r);
                    gasto += maq.Ms;
                    if (gasto >= ms || reinicios.Count > 50) return (serial, reinicios, maq, null);
                    origem = r;
                    maq.Reiniciar();
                }
                catch (ExecucaoInterrompida) { break; }
            }
        }
        catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao)
        {
            serial.AddRange(LinhasDoSerial(maq));
            return (serial, reinicios, maq, e.Message);
        }

        serial.AddRange(LinhasDoSerial(maq));
        return (serial, reinicios, maq, null);
    }

    /// <summary>
    /// sizeof em BYTES, do jeito do compilador de cada placa, e o vetor de
    /// duas dimensoes que o mapa de um jogo usa.
    /// </summary>
    static void Motor_TamanhosEVetores()
    {
        saida.AppendLine("sizeof e vetores de duas dimensoes");

        const string medidas = """
            int pinos[] = {2, 3, 4};
            const char* nomes[] = {"ana", "bia"};
            int m[3][2];
            struct Leitura { uint8_t id; int valor; float media; char nome[10]; };
            struct Grande { double x; uint8_t y; };
            Leitura atual;
            int contar(int v[]) { return sizeof(v); }
            void setup() {
              Serial.begin(115200);
              Serial.println(sizeof(pinos) / sizeof(pinos[0]));
              Serial.println(sizeof(pinos));
              Serial.println(sizeof(int));
              Serial.println(sizeof(unsigned long));
              Serial.println(sizeof(nomes) / sizeof(nomes[0]));
              Serial.println(sizeof(m));
              Serial.println(sizeof(m) / sizeof(m[0]));
              Serial.println(sizeof(Leitura));
              Serial.println(sizeof(atual.nome));
              Serial.println(sizeof(struct Grande));
              Serial.println(contar(pinos));
              int soma = 0;
              for (int i = 0; i < sizeof(pinos) / sizeof(pinos[0]); i++) soma += pinos[i];
              Serial.println(soma);
            }
            void loop() {}
            """;

        var (naEsp, erroEsp) = RodarProjeto(medidas, "esp32-s3-devkit");
        var (noUno, erroUno) = RodarProjeto(medidas, "arduino-uno");
        saida.AppendLine($"        ESP32-S3: {erroEsp ?? naEsp}");
        saida.AppendLine($"        Uno     : {erroUno ?? noUno}");

        // itens | bytes | int | long | ponteiros | 2D | linhas | struct | campo | double | parametro | soma
        Conferir("sizeof: a conta sizeof(v)/sizeof(v[0]) da o numero de itens (antes dava zero)",
            naEsp?.StartsWith("3|") == true && noUno?.StartsWith("3|") == true, naEsp + " / " + noUno);
        Conferir("sizeof: na ESP32 o int tem 4 bytes, a struct ganha alinhamento e o double 8",
            naEsp == "3|12|4|4|2|24|3|24|10|16|4|9", naEsp ?? erroEsp);
        Conferir("sizeof: no Uno o int tem 2 bytes, a struct nao tem enchimento e o double tem 4",
            noUno == "3|6|2|4|2|12|3|17|10|5|2|9", noUno ?? erroUno);

        var (_, erroAviso) = RodarProjeto("int v[4];\nint f(int a[]) { return sizeof(a); }\n"
                                          + "void setup() { Serial.begin(9600); Serial.println(f(v)); }\nvoid loop() {}");
        Conferir("sizeof: vetor que entra como parametro e ponteiro (sem erro, como no compilador)",
            erroAviso is null, erroAviso);

        const string grade = """
            int n[3][2];
            int p[2][3] = {{1}, {2, 3}};
            char mapa[3][6] = {"#..#", "#.2#", ""};
            const char* linhas[2] = {"ab", "cd"};
            void setup() {
              Serial.begin(115200);
              n[2][1] = 7;
              Serial.println(n[2][1]);
              Serial.print(p[0][0]); Serial.print(p[0][1]); Serial.print(p[0][2]);
              Serial.print(p[1][0]); Serial.println(p[1][1]);
              Serial.println(mapa[1]);
              Serial.println(mapa[1][2]);
              mapa[1][2] = '3';
              Serial.println(mapa[1]);
              Serial.println(strlen(mapa[2]));
              Serial.println(linhas[1][0]);
              int local[2][2];
              local[1][1] = 5;
              Serial.println(local[1][1]);
            }
            void loop() {}
            """;
        var (grades, erroGrade) = RodarProjeto(grade, "esp32-s3-devkit");
        saida.AppendLine($"        grades: {erroGrade ?? grades}");
        Conferir("2D: vetor sem inicializador tem linhas x colunas lugares",
            grades?.StartsWith("7|") == true, grades ?? erroGrade);
        Conferir("2D: a linha curta completa com zeros, como no C",
            grades?.Split('|').ElementAtOrDefault(1) == "10023", grades ?? erroGrade);
        Conferir("2D: vetor de char com um texto por linha, e mapa[1] e a linha",
            grades == "7|10023|#.2#|2|#.3#|0|c|5", grades ?? erroGrade);

        var (_, erroGrande) = RodarProjeto("char m[2][4] = {\"abcd\", \"x\"};\nvoid setup() {}\nvoid loop() {}");
        saida.AppendLine($"        texto que nao cabe: {erroGrande}");
        Conferir("2D: o texto que nao cabe na linha (sem lugar para o \\0) e erro, como no compilador",
            erroGrande?.Contains("nao cabe") == true, erroGrande);
    }

    /// <summary>
    /// O sono profundo da ESP32: timer, pino, a memoria RTC que sobrevive,
    /// os pinos que soltam -- e o ESP.restart(), que e o mesmo recomeco.
    /// </summary>
    static void Motor_SonoProfundo()
    {
        saida.AppendLine("sono profundo e reinicio");

        const string porTimer = """
            #include "esp_sleep.h"
            RTC_DATA_ATTR int boots = 0;
            RTC_NOINIT_ATTR int semIniciar;
            int comum = 5;
            void setup() {
              Serial.begin(115200);
              boots++;
              comum++;
              if (esp_reset_reason() != ESP_RST_SW) semIniciar = 40;
              semIniciar++;
              Serial.printf("boot %d comum %d causa %d razao %d guardado %d\n",
                            boots, comum, esp_sleep_get_wakeup_cause(), esp_reset_reason(), semIniciar);
              pinMode(2, OUTPUT);
              digitalWrite(2, HIGH);
              delay(50);
              if (boots == 3) ESP.restart();
              esp_sleep_enable_timer_wakeup(500000ULL);
              esp_deep_sleep_start();
              Serial.println("nunca chega aqui");
            }
            void loop() {}
            """;

        var (serial, reinicios, _, erro) = RodarComReinicios(porTimer, "esp32-s3-devkit", 1500);
        saida.AppendLine($"        {erro ?? string.Join(" | ", serial)}");
        Conferir("sono: o sketch com RTC_DATA_ATTR e esp_deep_sleep_start roda", erro is null, erro);
        Conferir("sono: a RTC_DATA_ATTR sobrevive ao sono, a global comum volta ao valor do codigo",
            serial.Count >= 3 && serial[0].StartsWith("boot 1 comum 6") && serial[1].StartsWith("boot 2 comum 6")
            && serial[2].StartsWith("boot 3 comum 6"), string.Join(" | ", serial));
        Conferir("sono: acordar pelo timer da causa 4 e razao 8 (ESP_RST_DEEPSLEEP)",
            serial.Count >= 2 && serial[1].Contains("causa 4 razao 8"), string.Join(" | ", serial));
        Conferir("sono: nada depois do esp_deep_sleep_start roda",
            !serial.Any(l => l.Contains("nunca")), string.Join(" | ", serial));
        Conferir("reinicio: o ESP.restart() recomeca com razao 3 e a RTC_DATA_ATTR volta ao valor do codigo",
            serial.Count >= 4 && serial[3].StartsWith("boot 1 comum 6 causa 0 razao 3"),
            string.Join(" | ", serial));
        Conferir("reinicio: a RTC_NOINIT_ATTR sobrevive ao reinicio por software",
            serial.Count >= 4 && serial[2].EndsWith("guardado 41") && serial[3].EndsWith("guardado 42"),
            string.Join(" | ", serial));
        Conferir("reinicio: cada recomeco vem com o motivo",
            reinicios.Count >= 3 && reinicios[0].Razao == 8 && reinicios[2].Razao == 3,
            string.Join(",", reinicios.Select(r => r.Razao)));

        // ---- dormindo, os pinos soltam ----
        const string paraSempre = """
            void setup() {
              pinMode(2, OUTPUT);
              digitalWrite(2, HIGH);
              esp_deep_sleep_start();
            }
            void loop() {}
            """;
        var (_, _, maqDormindo, erroSempre) = RodarComReinicios(paraSempre, "esp32-devkit-v1", 300);
        saida.AppendLine($"        dormindo: {maqDormindo.Sono} / pino 2 em {maqDormindo.Digital[2]}");
        Conferir("sono: dormindo, o LED que o sketch acendeu apaga (o pino solta)",
            erroSempre is null && maqDormindo.Digital[2] == 0 && maqDormindo.Modo[2] != 1,
            erroSempre ?? $"digital {maqDormindo.Digital[2]} modo {maqDormindo.Modo[2]}");
        Conferir("sono: sem fonte de despertar a placa dorme para sempre, com aviso",
            maqDormindo.Sono is not null && maqDormindo.Avisos.Any(a => a.Contains("para sempre")),
            maqDormindo.Sono + " / " + string.Join(" ", maqDormindo.Avisos));

        // ---- o sono leve continua da linha seguinte ----
        const string leve = """
            void setup() {
              Serial.begin(115200);
              pinMode(2, OUTPUT);
              digitalWrite(2, HIGH);
              unsigned long antes = millis();
              esp_sleep_enable_timer_wakeup(200000);
              esp_light_sleep_start();
              Serial.printf("voltou depois de %lu ms, causa %d, pino %d\n",
                            millis() - antes, esp_sleep_get_wakeup_cause(), digitalRead(2));
            }
            void loop() {}
            """;
        var (serialLeve, reiniciosLeve, _, erroLeve) = RodarComReinicios(leve, "esp32-devkit-v1", 1000);
        saida.AppendLine($"        sono leve: {erroLeve ?? string.Join(" | ", serialLeve)}");
        Conferir("sono leve: a placa continua da linha seguinte, sem reiniciar, e o pino fica como estava",
            erroLeve is null && reiniciosLeve.Count == 0
            && serialLeve.Any(l => l.StartsWith("voltou depois de 20") && l.EndsWith("causa 4, pino 1")),
            erroLeve ?? string.Join(" | ", serialLeve));

        // ---- o que cada chip tem ----
        var (_, erroC3) = RodarProjeto("void setup() { esp_sleep_enable_ext0_wakeup(GPIO_NUM_2, 1); }\nvoid loop() {}",
                                       "esp32-c3-devkit");
        saida.AppendLine($"        ext0 na C3: {erroC3}");
        Conferir("sono: a ESP32-C3 nao tem ext0, e o erro diz o que usar",
            erroC3?.Contains("ESP32-C3") == true && erroC3.Contains("gpio_wakeup"), erroC3);

        var (foraDoRtc, erroRtc) = RodarProjeto(
            "void setup() { Serial.begin(115200); Serial.println(esp_sleep_enable_ext0_wakeup(GPIO_NUM_5, 0)); }\nvoid loop() {}",
            "esp32-devkit-v1");
        Conferir("sono: ext0 num GPIO que nao e do RTC devolve ESP_ERR_INVALID_ARG (258)",
            foraDoRtc == "258", erroRtc ?? foraDoRtc);

        var (_, erroUno) = RodarProjeto("RTC_DATA_ATTR int x = 0;\nvoid setup() {}\nvoid loop() {}", "arduino-uno");
        Conferir("sono: RTC_DATA_ATTR no Uno e erro (so a ESP32 tem memoria RTC)", erroUno is not null, "compilou");

        // ---- o ESP8266 so acorda com o D0 no RST ----
        const string esp8266 = """
            void setup() {
              Serial.begin(115200);
              Serial.println(ESP.getResetReason());
              ESP.deepSleep(300000);
            }
            void loop() {}
            """;
        var (semFio, _, maqSemFio, _) = RodarComReinicios(esp8266, "esp8266-nodemcu", 1500, m => m.D0NoReset = () => false);
        var (comFio, _, _, erroComFio) = RodarComReinicios(esp8266, "esp8266-nodemcu", 1000, m => m.D0NoReset = () => true);
        saida.AppendLine($"        ESP8266 sem fio: {string.Join(" | ", semFio)}; com fio: {erroComFio ?? string.Join(" | ", comFio)}");
        Conferir("ESP8266: sem o fio do D0 no RST, o deepSleep nao acorda (e avisa)",
            semFio.Count == 1 && maqSemFio.Avisos.Any(a => a.Contains("D0")), string.Join(" | ", semFio));
        Conferir("ESP8266: com o fio, acorda e o getResetReason() diz \"Deep-Sleep Wake\"",
            comFio.Count >= 2 && comFio[0] == "Power On" && comFio[1] == "Deep-Sleep Wake",
            erroComFio ?? string.Join(" | ", comFio));

        // ---- a chave de verdade, na janela ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-s3-devkit", X = 0, Y = 0 };
            var chave = new PecaNoCircuito { Tipo = "slide-switch", X = 300, Y = 60 };
            c.Pecas.AddRange([placa, chave]);
            c.Fios.Add(new Fio { DePeca = chave.Id, DePino = "2", ParaPeca = placa.Id, ParaPino = "1" });
            c.Fios.Add(new Fio { DePeca = chave.Id, DePino = "1", ParaPeca = placa.Id, ParaPino = "GND" });

            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, """
                    RTC_DATA_ATTR int vezes = 0;
                    void setup() {
                      Serial.begin(115200);
                      pinMode(1, INPUT_PULLUP);
                      vezes++;
                      Serial.printf("ligou %d causa %d\n", vezes, esp_sleep_get_wakeup_cause());
                      if (digitalRead(1) == HIGH) {
                        esp_sleep_enable_ext0_wakeup(GPIO_NUM_1, 0);
                        esp_deep_sleep_start();
                      }
                    }
                    void loop() { delay(10); }
                    """);
                janela.TesteRodar();
                var maquina = janela.TesteMaquinaDa(placa.Id);
                bool dormiu = EsperarPor(() => maquina.Sono is not null, 4000);
                saida.AppendLine($"        chave desligada: {string.Join(" | ", LinhasDoSerial(maquina))} / {maquina.Sono}");
                Conferir("chave: com a chave desligada, a placa vai dormir", dormiu, maquina.Sono);

                chave.Giro = 1;
                bool acordou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.StartsWith("ligou 2")), 4000);
                saida.AppendLine($"        chave ligada: {string.Join(" | ", LinhasDoSerial(maquina))}");
                Conferir("chave: ligar a chave acorda a placa pelo ext0 (causa 2), e a RTC lembra a contagem",
                    acordou && LinhasDoSerial(maquina).Contains("ligou 2 causa 2") && maquina.Sono is null,
                    string.Join(" | ", LinhasDoSerial(maquina)));
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
    }

    /// <summary>
    /// O texto do TFT em pixels, com a fonte da biblioteca: o fundo opaco
    /// apaga o que estava embaixo, o fillRect apaga o texto, a linha quebra
    /// no fim da tela. E a luz de fundo pelo pino LED.
    /// </summary>
    static void Tela_TftEmPixels()
    {
        saida.AppendLine("tft: texto em pixels e luz de fundo");

        static (Maquina Maq, string Erro) Desenhar(string corpo, string placa = "esp32-devkit-v1")
        {
            var maq = new Maquina { Perfil = PerfilPlaca.De(placa) ?? PerfilPlaca.Esp32 };
            maq.Reiniciar();
            try
            {
                var prog = Compilador.CompilarProjeto(new Projeto
                {
                    Sketch = "#include <Adafruit_ILI9341.h>\nAdafruit_ILI9341 tft(15, 2);\n"
                             + "void setup() {\n  Serial.begin(115200);\n  tft.begin();\n" + corpo + "\n}\nvoid loop() {}",
                    Placa = placa,
                    DoSimulador = Acervo.EhDoSimulador,
                });
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
                return (maq, null);
            }
            catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao) { return (maq, e.Message); }
        }

        static bool MesmaRegiao(TelaGrafica a, TelaGrafica b, int larg, int alt)
        {
            for (int y = 0; y < alt; y++)
                for (int x = 0; x < larg; x++)
                    if (a.CorEm(x, y) != b.CorEm(x, y)) return false;
            return true;
        }

        // ---- o numero novo por cima do velho, com fundo: como na placa ----
        {
            var (porCima, e1) = Desenhar("""
                  tft.fillScreen(ILI9341_BLACK);
                  tft.setTextColor(ILI9341_WHITE, ILI9341_BLACK);
                  tft.setTextSize(2);
                  tft.setCursor(0, 0);
                  tft.print(12345);
                  tft.setCursor(0, 0);
                  tft.print(7);
                """);
            var (direto, e2) = Desenhar("""
                  tft.fillScreen(ILI9341_BLACK);
                  tft.setTextColor(ILI9341_WHITE, ILI9341_BLACK);
                  tft.setTextSize(2);
                  tft.setCursor(0, 0);
                  tft.print("72345");
                """);
            Conferir("tft: o 7 com fundo por cima de 12345 da 72345, pixel a pixel (como no vidro)",
                e1 is null && e2 is null && MesmaRegiao(porCima.Tela, direto.Tela, 60, 16),
                e1 ?? e2 ?? "pixels diferentes");
        }

        // ---- o fillRect apaga o texto ----
        {
            var (maq, erro) = Desenhar("""
                  tft.fillScreen(ILI9341_BLACK);
                  tft.setCursor(0, 0);
                  tft.print("ABC");
                  tft.fillRect(0, 0, 18, 8, ILI9341_BLACK);
                """);
            bool limpo = true;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 18; x++)
                    if ((maq.Tela.CorEm(x, y) & 0xFFFFFF) != 0) limpo = false;
            Conferir("tft: o fillRect apaga o texto que estava ali", erro is null && limpo, erro ?? "sobrou texto");
        }

        // ---- quebra de linha, acento e linha de altura zero ----
        {
            var (maq, erro) = Desenhar("""
                  tft.fillScreen(ILI9341_BLACK);
                  tft.setCursor(0, 0);
                  for (int i = 0; i < 50; i++) tft.print('A');
                  Serial.println(tft.getCursorX());
                  Serial.println(tft.getCursorY());
                  tft.setCursor(0, 100);
                  tft.print("ç");
                  Serial.println(tft.getCursorX());
                  tft.drawFastVLine(200, 200, 0, ILI9341_RED);
                  Serial.println(tft.color565(255, 0, 0));
                """);
            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        cursor: {erro ?? string.Join(" | ", linhas)}");
            Conferir("tft: a linha comprida quebra no fim da tela (setTextWrap ligado)",
                erro is null && linhas.Count >= 2 && linhas[0] == "60" && linhas[1] == "8",
                erro ?? string.Join(" | ", linhas));
            Conferir("tft: o \"ç\" sao dois bytes e ocupa duas letras, como no vidro",
                linhas.ElementAtOrDefault(2) == "12", string.Join(" | ", linhas));
            Conferir("tft: drawFastVLine de altura zero nao pinta nada",
                (maq.Tela.CorEm(200, 200) & 0xFFFFFF) == 0 && (maq.Tela.CorEm(200, 199) & 0xFFFFFF) == 0,
                $"{maq.Tela.CorEm(200, 200):X8}");
            Conferir("tft: color565(255, 0, 0) e o vermelho da biblioteca",
                linhas.ElementAtOrDefault(3) == "63488", string.Join(" | ", linhas));
        }

        // ---- imagem, triangulo e comandos ----
        {
            var (maq, erro) = Desenhar("""
                  uint16_t img[4] = {0xF800, 0x07E0, 0x001F, 0xFFFF};
                  tft.fillScreen(ILI9341_BLACK);
                  tft.drawRGBBitmap(0, 0, img, 2, 2);
                  tft.fillTriangle(50, 50, 90, 50, 70, 80, ILI9341_GREEN);
                  tft.sendCommand(ILI9341_DISPOFF);
                """);
            Conferir("tft: drawRGBBitmap poe a imagem do vetor no lugar",
                erro is null && maq.Tela.CorEm(1, 1) == TelaGrafica.DeRgb565(0xFFFF)
                && maq.Tela.CorEm(0, 0) == TelaGrafica.DeRgb565(0xF800), erro ?? $"{maq.Tela.CorEm(1, 1):X8}");
            Conferir("tft: fillTriangle enche o triangulo",
                maq.Tela.CorEm(70, 60) == TelaGrafica.DeRgb565(0x07E0), $"{maq.Tela.CorEm(70, 60):X8}");
            Conferir("tft: sendCommand(ILI9341_DISPOFF) desliga a imagem do vidro",
                !maq.Tela.Mostrando, "continua mostrando");
        }

        // ---- as cores so existem com o #include, como no compilador ----
        {
            var (_, erroSem) = RodarProjeto("void setup() { int c = ILI9341_RED; }\nvoid loop() {}");
            Conferir("tft: ILI9341_RED sem o #include do display e erro",
                erroSem?.Contains("ILI9341_RED") == true, erroSem ?? "compilou");
        }

        // ---- a luz de fundo segue o pino LED ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var display = new PecaNoCircuito { Tipo = "ili9341", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, display]);
            c.Fios.Add(new Fio { DePeca = display.Id, DePino = "LED", ParaPeca = placa.Id, ParaPino = "D5" });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var rede = new Netlist(c);

            double Escuro(string corpo)
            {
                var (maq, _) = Desenhar("  pinMode(5, OUTPUT);\n  tft.fillScreen(ILI9341_WHITE);\n" + corpo);
                ed.RefletirEstado(maq, rede);
                return ed.TftParaTeste(display.Id).escuro;
            }

            double apagada = Escuro("  digitalWrite(5, LOW);");
            double acesa = Escuro("  digitalWrite(5, HIGH);");
            double meia = Escuro("  analogWrite(5, 128);");
            saida.AppendLine($"        luz: LOW={apagada:0.00} HIGH={acesa:0.00} PWM128={meia:0.00}");
            Conferir("tft: o pino LED em LOW apaga a luz de fundo", apagada > 0.99, $"{apagada:0.00}");
            Conferir("tft: o pino LED em HIGH acende", acesa < 0.01, $"{acesa:0.00}");
            Conferir("tft: PWM no pino LED da meia luz", meia is > 0.4 and < 0.6, $"{meia:0.00}");
        }
    }
}
