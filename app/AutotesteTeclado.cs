namespace Simulador.App;

/// <summary>
/// O teclado matricial com a Keypad: a letra sai do mapa e dos pinos do
/// sketch, e nao da tecla impressa -- fio trocado e mapa diferente dao outra
/// letra, como na bancada. E o addEventListener com PRESSED, HOLD e RELEASED.
/// </summary>
public static partial class Autoteste
{
    static void Teclado_ComoNaBiblioteca()
    {
        saida.AppendLine("teclado matricial (Keypad)");

        string Sketch(string mapa, string linhas, string colunas, string extra = "") => $$"""
            #include <Keypad.h>
            const byte ROWS = 4;
            const byte COLS = 4;
            char hexaKeys[ROWS][COLS] = {{{mapa}}};
            byte rowPins[ROWS] = {{{linhas}}};
            byte colPins[COLS] = {{{colunas}}};
            Keypad customKeypad = Keypad(makeKeymap(hexaKeys), rowPins, colPins, ROWS, COLS);
            {{extra}}
            void setup() {
              Serial.begin(9600);
              {{(extra.Length > 0 ? "customKeypad.addEventListener(keypadEvent);" : "")}}
            }
            void loop() {
              char customKey = customKeypad.getKey();
              if (customKey) {
                Serial.print("tecla: ");
                Serial.println(customKey);
              }
            }
            """;

        const string padrao = "{'1','2','3','A'},{'4','5','6','B'},{'7','8','9','C'},{'*','0','#','D'}";
        const string letras = "{'a','b','c','d'},{'e','f','g','h'},{'i','j','k','l'},{'m','n','o','p'}";

        // Aperta as teclas e devolve o que o serial mostrou.
        string Apertar(string sketch, params (string Tecla, int Ms)[] teclas)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 0 };
            var teclado = new PecaNoCircuito { Tipo = "membrane-keypad", X = 500, Y = 0 };
            c.Pecas.AddRange([placa, teclado]);
            string[] pinosDoTeclado = ["R1", "R2", "R3", "R4", "C1", "C2", "C3", "C4"];
            string[] pinosDaPlaca = ["9", "8", "7", "6", "5", "4", "3", "2"];
            for (int i = 0; i < 8; i++)
                c.Fios.Add(new Fio { DePeca = teclado.Id, DePino = pinosDoTeclado[i], ParaPeca = placa.Id, ParaPino = pinosDaPlaca[i] });

            var janela = new MainWindow();
            janela.TesteAbrir(c, sketch);
            janela.TesteRodar();
            var ed = janela.TesteEditorCircuito;
            var def = Catalogo.Obter(teclado.Tipo);
            EsperarPor(() => false, 300);
            foreach (var (tecla, ms) in teclas)
            {
                var (x, y, _) = EditorCircuito.TeclasMatriciais.First(t => t.Tecla == tecla);
                ed.Apertar(Geometria.PosicaoLocal(teclado, def, x, y));
                EsperarPor(() => false, ms);
                ed.Soltar();
                EsperarPor(() => false, 250);
            }
            var linhas = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));
            janela.TesteParar();
            return string.Join(" | ", linhas);
        }

        string certo = Apertar(Sketch(padrao, "9, 8, 7, 6", "5, 4, 3, 2"), ("5", 200), ("D", 200));
        saida.AppendLine($"        ligado certo, 5 e D: {certo}");
        Conferir("keypad: ligado como o sketch diz, a tecla 5 da '5' e a D da 'D'",
            certo == "tecla: 5 | tecla: D", certo);

        string outroMapa = Apertar(Sketch(letras, "9, 8, 7, 6", "5, 4, 3, 2"), ("5", 200));
        Conferir("keypad: com outro mapa no sketch, a mesma tecla da a letra do mapa (a 5 e a linha 2, coluna 2: 'f')",
            outroMapa == "tecla: f", outroMapa);

        string trocados = Apertar(Sketch(padrao, "5, 4, 3, 2", "9, 8, 7, 6"), ("2", 200));
        Conferir("keypad: com as linhas e as colunas trocadas no sketch, a tecla 2 vira '4' (a varredura acha a transposta)",
            trocados == "tecla: 4", trocados);

        string eventos = Apertar(Sketch(padrao, "9, 8, 7, 6", "5, 4, 3, 2", """
            void keypadEvent(KeypadEvent key) {
              switch (customKeypad.getState()) {
                case PRESSED: Serial.print("apertou "); Serial.println(key); break;
                case HOLD: Serial.print("segurou "); Serial.println(key); break;
                case RELEASED: Serial.print("soltou "); Serial.println(key); break;
              }
            }
            """), ("7", 900));
        Conferir("keypad: o addEventListener recebe PRESSED, HOLD (depois de 500 ms) e RELEASED",
            eventos.Contains("apertou 7") && eventos.Contains("segurou 7") && eventos.Contains("soltou 7")
            && eventos.IndexOf("apertou", StringComparison.Ordinal) < eventos.IndexOf("segurou", StringComparison.Ordinal)
            && eventos.IndexOf("segurou", StringComparison.Ordinal) < eventos.IndexOf("soltou", StringComparison.Ordinal), eventos);
    }
}
