namespace Simulador.App;

/// <summary>
/// A conta do C com os tipos de cada placa: o int de 16 bits do Uno, o char
/// com e sem sinal, o double que e float no AVR, a divisao por zero de cada
/// chip, e as funcoes do Arduino.h com o tipo que cada core da a elas.
///
/// Os valores esperados sao os da placa de verdade (o C que o compilador de
/// cada uma gera); antes o simulador fazia tudo num double sem tipo, e o
/// "1000 * 60 * 5" que da errado no Uno dava certo aqui.
/// </summary>
public static partial class Autoteste
{
    static void Motor_ContaDoC()
    {
        saida.AppendLine("a conta do C, com os tipos da placa");

        const string sketch = """
            void setup() {
              Serial.begin(115200);
              int x = 32767; x = x + 1;
              Serial.println(x);
              unsigned long espera = 1000 * 60 * 5;
              Serial.println(espera);
              Serial.println(1000UL * 60 * 5);
              byte b = 255; b++;
              Serial.println(b);
              char c = 200;
              Serial.println((int)c);
              int leitura = 1023;
              Serial.println(leitura * 100);
              unsigned int u = 0; u = u - 1;
              Serial.println(u);
              int negativo = -1; unsigned int um = 1;
              Serial.println(negativo < um ? "menor" : "nao");
              Serial.println(1 << 15);
              Serial.println(round(2.5));
              Serial.println(floor(2.7));
              Serial.println(map(0.5, 0, 1, 0, 100));
              unsigned short us = 65535;
              Serial.println(us + 1);
              byte soma = 250; soma += 10;
              Serial.println(soma);
              float f = 0.5; f++;
              Serial.println(f);
              char letra = 'A';
              Serial.println(letra + 1);
              Serial.println((char)(letra + 1));
              unsigned long antes = 100, depois = 200;
              Serial.println(antes - depois);
            }
            void loop() {}
            """;

        var (uno, e1) = RodarProjeto(sketch, "arduino-uno");
        var (esp, e2) = RodarProjeto(sketch, "esp32-devkit-v1");
        saida.AppendLine($"        uno   -> {uno ?? e1}");
        saida.AppendLine($"        esp32 -> {esp ?? e2}");

        string[] noUno = (uno ?? "").Split('|');
        string[] naEsp = (esp ?? "").Split('|');
        string U(int i) => i < noUno.Length ? noUno[i] : "?";
        string E(int i) => i < naEsp.Length ? naEsp[i] : "?";

        Conferir("uno: int de 16 bits estoura (32767 + 1 = -32768)", U(0) == "-32768", U(0));
        Conferir("esp32: int de 32 bits nao estoura ali", E(0) == "32768", E(0));
        Conferir("uno: 1000 * 60 * 5 estoura no int ANTES de virar unsigned long, como na placa",
            U(1) == "4294939616" && U(2) == "300000", $"{U(1)} / {U(2)}");
        Conferir("esp32: a mesma conta da 300000", E(1) == "300000" && E(2) == "300000", $"{E(1)} / {E(2)}");
        Conferir("byte 255 mais um da 0", U(3) == "0" && E(3) == "0", $"{U(3)} / {E(3)}");
        Conferir("char tem sinal no AVR (200 vira -56) e nao tem na ESP32", U(4) == "-56" && E(4) == "200", $"{U(4)} / {E(4)}");
        Conferir("uno: analogRead de 1023 vezes 100 estoura no int", U(5) == "-28772" && E(5) == "102300", $"{U(5)} / {E(5)}");
        Conferir("unsigned int 0 menos 1 da o maximo do tipo de cada placa",
            U(6) == "65535" && E(6) == "4294967295", $"{U(6)} / {E(6)}");
        Conferir("-1 < 1u e falso (o -1 vira unsigned), nas duas", U(7) == "nao" && E(7) == "nao", $"{U(7)} / {E(7)}");
        Conferir("1 << 15 e -32768 no Uno e 32768 na ESP32", U(8) == "-32768" && E(8) == "32768", $"{U(8)} / {E(8)}");
        Conferir("round() e a macro do AVR (long) e o std::round da ESP32 (double)",
            U(9) == "3" && E(9) == "3.00", $"{U(9)} / {E(9)}");
        Conferir("floor() devolve double: imprime 2.00", U(10) == "2.00" && E(10) == "2.00", $"{U(10)} / {E(10)}");
        Conferir("map() e de long: map(0.5, 0, 1, 0, 100) da 0", U(11) == "0" && E(11) == "0", $"{U(11)} / {E(11)}");
        Conferir("unsigned short vira unsigned int no AVR (65535 + 1 = 0) e int na ESP32",
            U(12) == "0" && E(12) == "65536", $"{U(12)} / {E(12)}");
        Conferir("byte += 10 dobra em 8 bits (250 + 10 = 4)", U(13) == "4" && E(13) == "4", $"{U(13)} / {E(13)}");
        Conferir("float++ continua float (0.5 vira 1.50)", U(14) == "1.50" && E(14) == "1.50", $"{U(14)} / {E(14)}");
        Conferir("char + 1 e int (66); (char) volta a ser letra", U(15) == "66" && U(16) == "B", $"{U(15)} / {U(16)}");
        Conferir("unsigned long 100 - 200 da a volta, como o millis() depende",
            U(17) == "4294967196" && E(17) == "4294967196", $"{U(17)} / {E(17)}");

        // ---- a divisao inteira por zero de cada chip ----
        const string porZero = """
            void setup() {
              Serial.begin(115200);
              int zero = 0;
              int a = 7, b = -7;
              Serial.println(a / zero);
              Serial.println(b / zero);
              Serial.println(a % zero);
              float r = 1.0 / zero;
              Serial.println(r);
            }
            void loop() {}
            """;
        var (divUno, eu) = RodarProjeto(porZero, "arduino-uno");
        var (divC3, ec) = RodarProjeto(porZero, "esp32-c3-devkit");
        var (divStm, es) = RodarProjeto(porZero, "stm32-bluepill");
        var (divEsp, ee) = RodarProjeto(porZero, "esp32-devkit-v1");
        var (div8266, e8) = RodarProjeto(porZero, "esp8266-nodemcu");
        saida.AppendLine($"        divisao por zero: uno {divUno ?? eu} / c3 {divC3 ?? ec} / stm32 {divStm ?? es}");
        Conferir("uno: 7/0 da -1, -7/0 da 1, 7%0 da 7 (a rotina do avr-gcc); o real vira inf",
            divUno == "-1|1|7|inf", divUno ?? eu);
        Conferir("esp32-c3 (RISC-V): x/0 da -1 sempre, sem excecao", divC3 == "-1|-1|7|inf", divC3 ?? ec);
        Conferir("stm32 (Cortex-M): x/0 da 0", divStm == "0|0|7|inf", divStm ?? es);
        Conferir("esp32: divisao por zero e o Guru Meditation, e a placa reinicia",
            divEsp?.Contains("Core  1 panic'ed (IntegerDivideByZero)") == true && ee?.StartsWith("reiniciou") == true,
            $"{divEsp} / {ee}");
        Conferir("esp8266: divisao por zero e a Exception (6), e a placa reinicia",
            div8266?.Contains("Exception (6):") == true && e8?.StartsWith("reiniciou") == true, $"{div8266} / {e8}");

        // ---- o que nao compila ----
        var (_, resto) = RodarProjeto("void setup() { float f = 5.5; Serial.println(f % 2); }\nvoid loop() {}\n");
        Conferir("'%' com float nao compila, e o erro diz por que", resto?.Contains("invalid operands") == true
            && resto.Contains("fmod"), resto);

        const string maxMisturado = "void setup() { long a = 5; Serial.println(max(a, 3)); }\nvoid loop() {}\n";
        var (_, naoCompila) = RodarProjeto(maxMisturado, "esp32-devkit-v1");
        var (noUnoCompila, eMax) = RodarProjeto(maxMisturado, "arduino-uno");
        Conferir("esp32: max(long, int) nao compila (std::max exige o mesmo tipo); no Uno a macro aceita",
            naoCompila?.Contains("no matching function for call to 'max(long int, int)'") == true && noUnoCompila == "5",
            $"{naoCompila} / {noUnoCompila ?? eMax}");
    }
}
