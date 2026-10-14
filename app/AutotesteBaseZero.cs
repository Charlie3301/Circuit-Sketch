namespace Simulador.App;

/// <summary>
/// A demo BASE ZERO (Alpha 19, item 11) e o C++ que ela puxou do motor:
/// struct preenchida entre chaves, referencia local, o nome do vetor como
/// ponteiro, toCharArray, abs de float, o File que diz se abriu.
/// </summary>
public static partial class Autoteste
{
    static void Motor_CppDoJogo()
    {
        saida.AppendLine("o C++ que o jogo usa");

        const string sketch = """
            #include <SD.h>
            #include <Adafruit_ILI9341.h>
            #include "driver/rtc_io.h"
            struct Coisa { float x, y; char tipo; int vida; bool ativa; unsigned long quando; };
            Coisa coisas[3];
            const char* KIT[2] = {"ab", "cd"};
            const char* ROBO[2] = {"xy", "zw"};
            int nums[3] = {1, 2, 3};
            int outros[3] = {7, 8, 9};
            int x = 5;
            char nome[8];
            Adafruit_ILI9341 tft(10, 9, 8);
            void setup() {
              Serial.begin(115200);
              coisas[1] = {1.5f, 2.5f, 'R', 2, true, 0};
              Coisa &c = coisas[1];
              c.vida--;
              Coisa copia = coisas[1];
              copia.vida = 99;
              Serial.printf("%d %c %d\n", coisas[1].vida, coisas[1].tipo, copia.vida);
              int &r = x;
              r = 9;
              int &e = nums[2];
              e += 10;
              Serial.printf("%d %d\n", x, nums[2]);
              bool kit = false;
              const char** arte = kit ? KIT : ROBO;
              int* p = nums;
              Serial.printf("%c %s %d\n", arte[1][1], KIT[0], p[1]);
              p = outros;
              Serial.println(p[0]);
              String t = "E1M1: HANGAR";
              t.toCharArray(nome, sizeof(nome));
              Serial.println(nome);
              String a = "A";
              Serial.println(a.charAt(0) + 1);
              Serial.println(abs(-2.5));
              Serial.println(constrain(0.5, 0.0, 1.0));
              Serial.println(atan2f(1.0f, 1.0f) * 4);
              rtc_gpio_pullup_en((gpio_num_t)1);
              SD.begin(5);
              File f = SD.open("/nao-existe.txt");
              if (!f) Serial.println("sem arquivo");
              tft.begin();
              tft.setCursor(0, 0);
              tft.printf("%3d%%", 7);
              Serial.println(tft.getCursorX());
            }
            void loop() {}
            """;

        var (serial, erro) = RodarProjeto(sketch, "esp32-s3-devkit");
        saida.AppendLine($"        {erro ?? serial}");
        var linhas = serial?.Split('|') ?? [];

        Conferir("c++: struct preenchida entre chaves, e a referencia mexe no item do vetor (a copia nao)",
            erro is null && linhas.ElementAtOrDefault(0) == "1 R 99", erro ?? serial);
        Conferir("c++: referencia local de numero e de item de vetor escreve no original",
            linhas.ElementAtOrDefault(1) == "9 13", serial);
        Conferir("c++: o nome do vetor vale como ponteiro (ternario, int* p = nums)",
            linhas.ElementAtOrDefault(2) == "w ab 2" && linhas.ElementAtOrDefault(3) == "7", serial);
        Conferir("c++: toCharArray corta no tamanho do vetor, com o \\0",
            linhas.ElementAtOrDefault(4) == "E1M1: H", serial);
        Conferir("c++: charAt devolve char, e 'A' + 1 e 66",
            linhas.ElementAtOrDefault(5) == "66", serial);
        Conferir("c++: abs e constrain de float continuam float",
            linhas.ElementAtOrDefault(6) == "2.50" && linhas.ElementAtOrDefault(7) == "0.50", serial);
        Conferir("c++: atan2f e as outras funcoes em float do math.h",
            linhas.ElementAtOrDefault(8) == "3.14", serial);
        Conferir("c++: File que nao abriu e falso no if (!f)",
            linhas.ElementAtOrDefault(9) == "sem arquivo", serial);
        Conferir("c++: tft.printf escreve formatado e anda o cursor",
            linhas.ElementAtOrDefault(10) == "24", serial);
    }

    /// <summary>
    /// A demo inteira, na janela: le a fase do cartao, desenha o 3D, e a
    /// chave desliga (sono profundo, luz apagada) e liga de novo (a memoria
    /// RTC traz a fase de volta).
    /// </summary>
    static void Demo_BaseZero()
    {
        saida.AppendLine("demo BASE ZERO");

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrirDemo("esp32s3-base-zero");
            var pecas = janela.TesteEditorCircuito.Atual.Pecas;
            var chave = pecas.First(p => p.Tipo == "slide-switch");
            var tela = pecas.First(p => p.Tipo == "ili9341");
            var placa = pecas.First(p => p.Tipo == "esp32-s3-devkit");

            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            bool ligou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.StartsWith("BASE ZERO")), 6000);
            saida.AppendLine($"        ligou: {string.Join(" | ", LinhasDoSerial(maquina))}");
            Conferir("base zero: o jogo liga e acha o cartao", ligou
                && LinhasDoSerial(maquina).Any(l => l.Contains("cartao: sim")), string.Join(" | ", LinhasDoSerial(maquina)));

            // O START por um instante, como um dedo.
            janela.TesteEditorCircuito.BotoesPressionados.Add("start");
            EsperarPor(() => false, 300);
            janela.TesteEditorCircuito.BotoesPressionados.Remove("start");
            bool comecou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains("E1M1: HANGAR (do cartao)")), 6000);
            Conferir("base zero: o START comeca a fase lida do cartao", comecou,
                string.Join(" | ", LinhasDoSerial(maquina)));

            // Um quadro do 3D tem teto, paredes em tons, chao, arma e painel:
            // muitas cores, e nao uma tela de uma cor so.
            EsperarPor(() => (maquina.Tela?.Pixels.Distinct().Count() ?? 0) > 12, 8000);
            int cores = maquina.Tela?.Pixels.Distinct().Count() ?? 0;
            saida.AppendLine($"        cores no display: {cores}");
            Conferir("base zero: o 3D chega no display", cores > 12, $"{cores} cores");

            // Desligar a chave: a placa dorme e a luz da tela apaga.
            chave.Giro = 0;
            bool dormiu = EsperarPor(() => maquina.Sono is not null, 6000);
            EsperarPor(() => false, 300);
            double escuro = janela.TesteEditorCircuito.TftParaTeste(tela.Id).escuro;
            saida.AppendLine($"        desligada: {maquina.Sono} / luz apagada {escuro:0.00}");
            Conferir("base zero: a chave desligada poe a placa em sono profundo", dormiu, maquina.Sono);
            Conferir("base zero: dormindo, a luz de fundo do display apaga", escuro > 0.99, $"{escuro:0.00}");

            // Ligar de novo: o ext0 acorda, e a RTC traz a fase.
            chave.Giro = 1;
            bool voltou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.StartsWith("acordou pela chave")), 6000);
            saida.AppendLine($"        ligada de novo: {string.Join(" | ", LinhasDoSerial(maquina))}");
            Conferir("base zero: ligar a chave acorda a placa e volta para a fase guardada na RTC",
                voltou && LinhasDoSerial(maquina).Any(l => l.Contains("voltando para a fase 1")),
                string.Join(" | ", LinhasDoSerial(maquina)));
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }
}
