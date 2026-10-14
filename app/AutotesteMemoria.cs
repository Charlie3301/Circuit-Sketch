namespace Simulador.App;

/// <summary>
/// A RAM da placa: a heap que o malloc, a String e os radios gastam, com os
/// numeros de cada core -- e o que acontece quando ela acaba.
/// </summary>
public static partial class Autoteste
{
    static void Motor_MemoriaDaPlaca()
    {
        saida.AppendLine("a RAM da placa");

        // ---- a String no Uno: toda String tem bloco, e ele e exato ----
        const string textoNoUno = """
            int freeRam() {
              extern int __heap_start, *__brkval;
              int v;
              return (int)&v - (__brkval == 0 ? (int)&__heap_start : (int)__brkval);
            }
            void setup() {
              Serial.begin(9600);
              int r0 = freeRam();
              String a = "hello";
              int r1 = freeRam();
              String b;
              for (int i = 0; i < 400; i++) b += "x";
              int r2 = freeRam();
              char *p = (char*)malloc(300);
              int r3 = freeRam();
              free(p);
              String c;
              for (int i = 0; i < 2000; i++) c += "y";
              Serial.print("hello: "); Serial.println(r0 - r1);
              Serial.print("b: "); Serial.println(b.length());
              Serial.print("bloco: "); Serial.println(r1 - r2);
              Serial.print("malloc: "); Serial.println(r2 - r3);
              Serial.print("c: "); Serial.println(c.length());
            }
            void loop() {}
            """;
        var (uno, _, avisosUno, _) = RodarNaCpu(textoNoUno, "arduino-uno", 10_000);
        long hello = NumeroDaLinha(uno, "hello:"), bloco = NumeroDaLinha(uno, "bloco:");
        long cUno = NumeroDaLinha(uno, "c:"), mallocUno = NumeroDaLinha(uno, "malloc:");
        saida.AppendLine($"        uno: \"hello\" {hello} B, 400 letras {bloco} B, malloc(300) {mallocUno} B, a String que cresce parou em {cUno}");
        Conferir("uno: String(\"hello\") gasta 8 bytes da heap (6 do texto com o '\\0' e 2 do malloc)", hello == 8, hello.ToString());
        Conferir("uno: 400 += de uma letra dao 400 letras num bloco de 403 bytes (o realloc exato do AVR)",
            NumeroDaLinha(uno, "b:") == 400 && bloco == 403, $"{NumeroDaLinha(uno, "b:")} / {bloco}");
        Conferir("uno: malloc(300) gasta 302 bytes", mallocUno == 302, mallocUno.ToString());
        Conferir("uno: sem heap, o += para de crescer a String -- sem erro, como na placa -- e o simulador avisa",
            cUno is > 1000 and < 2000 && avisosUno.Contains("a String precisou"), $"{cUno} / {avisosUno}");

        // ---- a String na ESP32: o texto curto nem toca a heap ----
        const string textoNaEsp = """
            #include <WiFi.h>
            void usar() {
              String local = "um texto comprido o bastante para a heap";
              Serial.print("");
            }
            void setup() {
              Serial.begin(115200);
              long h0 = ESP.getFreeHeap();
              String curta = "curta";
              long h1 = ESP.getFreeHeap();
              String longa = "um texto bem maior que catorze";
              long h2 = ESP.getFreeHeap();
              for (int i = 0; i < 100; i++) usar();
              long h3 = ESP.getFreeHeap();
              String gigante;
              bool coube = gigante.reserve(70000);
              void *p = malloc(100000);
              long h4 = ESP.getFreeHeap();
              void *q = malloc(400000);
              free(p);
              long h5 = ESP.getFreeHeap();
              WiFi.begin("Wokwi-GUEST", "");
              long h6 = ESP.getFreeHeap();
              WiFi.disconnect(true);
              long h7 = ESP.getFreeHeap();
              Serial.print("curta: "); Serial.println(h0 - h1);
              Serial.print("longa: "); Serial.println(h1 - h2);
              Serial.print("vazou: "); Serial.println(h2 - h3);
              Serial.print("reserve: "); Serial.println(coube ? 1 : 0);
              Serial.print("malloc: "); Serial.println(h3 - h4);
              Serial.print("nulo: "); Serial.println(q == NULL ? 1 : 0);
              Serial.print("free: "); Serial.println(h3 - h5);
              Serial.print("wifi: "); Serial.println(h5 - h6);
              Serial.print("desligado: "); Serial.println(h5 - h7);
            }
            void loop() {}
            """;
        var (esp, _, _, _) = RodarNaCpu(textoNaEsp, "esp32-devkit-v1", 10_000);
        long wifi = NumeroDaLinha(esp, "wifi:");
        saida.AppendLine($"        esp32: String curta {NumeroDaLinha(esp, "curta:")} B, 30 letras {NumeroDaLinha(esp, "longa:")} B, "
                         + $"malloc(100000) {NumeroDaLinha(esp, "malloc:")} B, WiFi {wifi} B");
        Conferir("esp32: a String de ate 13 letras mora dentro do objeto (o SSO): a heap nao muda",
            NumeroDaLinha(esp, "curta:") == 0, NumeroDaLinha(esp, "curta:").ToString());
        Conferir("esp32: a de 30 letras pede (30 + 16) arredondado em 16 = 32 bytes, mais os 8 do bloco",
            NumeroDaLinha(esp, "longa:") == 40, NumeroDaLinha(esp, "longa:").ToString());
        Conferir("esp32: a String local volta para a heap quando a funcao acaba (100 chamadas, nada vazou)",
            NumeroDaLinha(esp, "vazou:") == 0, NumeroDaLinha(esp, "vazou:").ToString());
        Conferir("esp32: uma String nao passa de 65535 bytes sem a PSRAM (o reserve da false)",
            NumeroDaLinha(esp, "reserve:") == 0, NumeroDaLinha(esp, "reserve:").ToString());
        Conferir("esp32: malloc(100000) gasta 100008 bytes; malloc(400000) da NULL; o free devolve tudo",
            NumeroDaLinha(esp, "malloc:") == 100_008 && NumeroDaLinha(esp, "nulo:") == 1 && NumeroDaLinha(esp, "free:") == 0,
            $"{NumeroDaLinha(esp, "malloc:")} / {NumeroDaLinha(esp, "nulo:")} / {NumeroDaLinha(esp, "free:")}");
        Conferir("esp32: o WiFi.begin() tira ~50 KB da heap, e o disconnect(true) devolve",
            wifi is > 40_000 and < 60_000 && NumeroDaLinha(esp, "desligado:") == 0, $"{wifi} / {NumeroDaLinha(esp, "desligado:")}");

        // ---- a S3 com PSRAM: o bloco grande vai para ela ----
        const string psram = """
            void setup() {
              Serial.begin(115200);
              long h0 = ESP.getFreeHeap(), p0 = ESP.getFreePsram();
              void *grande = malloc(100000);
              long h1 = ESP.getFreeHeap(), p1 = ESP.getFreePsram();
              void *pequeno = malloc(1000);
              long h2 = ESP.getFreeHeap();
              Serial.print("interna: "); Serial.println(h0 - h1);
              Serial.print("psram: "); Serial.println(p0 - p1);
              Serial.print("pequeno: "); Serial.println(h1 - h2);
            }
            void loop() {}
            """;
        var (s3, _, _, _) = RodarNaCpu(psram, "esp32-s3-devkit", 10_000);
        Conferir("esp32-s3 com PSRAM: malloc(100000) vai para a PSRAM (a RAM interna nao muda); malloc(1000) fica na interna",
            NumeroDaLinha(s3, "interna:") == 0 && NumeroDaLinha(s3, "psram:") == 100_008 && NumeroDaLinha(s3, "pequeno:") == 1_008,
            $"{NumeroDaLinha(s3, "interna:")} / {NumeroDaLinha(s3, "psram:")} / {NumeroDaLinha(s3, "pequeno:")}");

        // ---- o BLE e a heap cheia ----
        const string ble = """
            #include <BLEDevice.h>
            void setup() {
              Serial.begin(115200);
              long h0 = ESP.getFreeHeap();
              BLEDevice::init("sonda");
              Serial.print("ble: "); Serial.println(h0 - (long)ESP.getFreeHeap());
            }
            void loop() {}
            """;
        long bleEsp = NumeroDaLinha(RodarNaCpu(ble, "esp32-devkit-v1", 10_000).Serial, "ble:");
        long bleS3 = NumeroDaLinha(RodarNaCpu(ble, "esp32-s3-devkit", 10_000).Serial, "ble:");
        saida.AppendLine($"        BLEDevice::init: esp32 (Bluedroid) {bleEsp} B, s3 (NimBLE) {bleS3} B");
        Conferir("o BLE pesa mais na ESP32 (Bluedroid) que na S3 (NimBLE)",
            bleEsp is > 50_000 and < 100_000 && bleS3 is > 20_000 and < 50_000, $"{bleEsp} / {bleS3}");

        const string semHeap = """
            #include <WiFi.h>
            void setup() {
              Serial.begin(115200);
              int n = 0;
              while (malloc(10000) != NULL) n++;
              Serial.print("blocos: "); Serial.println(n);
              Serial.print("begin: "); Serial.println(WiFi.begin("Wokwi-GUEST", ""));
            }
            void loop() {}
            """;
        var (cheia, _, avisosCheia, _) = RodarNaCpu(semHeap, "esp32-devkit-v1", 10_000);
        Conferir("esp32 com a heap cheia: o WiFi.begin() nao sobe (WL_CONNECT_FAILED) e o simulador diz por que",
            NumeroDaLinha(cheia, "blocos:") > 20 && NumeroDaLinha(cheia, "begin:") == 4 && avisosCheia.Contains("o WiFi precisa"),
            $"{NumeroDaLinha(cheia, "blocos:")} / {NumeroDaLinha(cheia, "begin:")} / {avisosCheia}");
    }
}
