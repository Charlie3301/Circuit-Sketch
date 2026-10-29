using System.IO;

namespace Simulador.App;

/// <summary>
/// O que os projetos de ESP32 "de verdade" usam e o C++ compilado passou a
/// ter: o WebSocket (WebSocketsServer, e o WiFiServer com TCP de verdade), o
/// timer de hardware com os registradores de GPIO (o som feito na
/// interrupcao), o servo pelo LEDC a 50 Hz, o I2S, o fs::FS do cartao e a
/// PSRAM.
/// </summary>
public static partial class Autoteste
{
    static void Cpp_Perifericos()
    {
        saida.AppendLine("C++ compilado: WebSocket, timer e registradores, servo pelo LEDC, I2S, cartao e PSRAM");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("perifericos: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- o WebSocket: o navegador manda, a placa responde ----
        const string ws = """
            #include <WiFi.h>
            #include <WebSocketsServer.h>
            WebSocketsServer ws(8765);
            void evento(uint8_t num, WStype_t tipo, uint8_t *dados, size_t n) {
              if (tipo == WStype_CONNECTED) ws.sendTXT(num, "oi");
              if (tipo == WStype_TEXT) {
                String s = String((char *)dados);
                s.toUpperCase();
                ws.sendTXT(num, s);
              }
            }
            void setup() {
              Serial.begin(115200);
              WiFi.mode(WIFI_AP);
              WiFi.softAP("teste", "12345678");
              ws.begin();
              ws.onEvent(evento);
            }
            void loop() { ws.loop(); delay(2); }
            """;
        string ida = "", volta = "";
        var (serial, erro, nativo, _) = RodarNativo(ws, 2500, durante: maq =>
        {
            if (volta.Length > 0 || maq.Rede is null) return;
            var servidor = maq.Rede.Servidores.FirstOrDefault(s => s.Porta == 8765 && s.UrlLocal is not null);
            if (servidor is null || maq.Rede.Servidores.Count == 0) return;
            try
            {
                using var cliente = new System.Net.WebSockets.ClientWebSocket();
                var ate = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                cliente.ConnectAsync(new Uri(servidor.UrlLocal.Replace("http://", "ws://")), ate.Token).Wait();
                var buf = new byte[256];
                var r = cliente.ReceiveAsync(buf, ate.Token).Result;
                ida = System.Text.Encoding.UTF8.GetString(buf, 0, r.Count);
                cliente.SendAsync(System.Text.Encoding.UTF8.GetBytes("ola placa"), System.Net.WebSockets.WebSocketMessageType.Text, true, ate.Token).Wait();
                r = cliente.ReceiveAsync(buf, ate.Token).Result;
                volta = System.Text.Encoding.UTF8.GetString(buf, 0, r.Count);
            }
            catch (Exception e) { volta = "erro: " + e.GetBaseException().Message; }
        });
        Conferir("perifericos: WebSocketsServer -- o navegador conecta pela ponte (na mesma porta), recebe \"oi\" e a placa devolve o texto em maiusculas",
            nativo && erro is null && ida == "oi" && volta == "OLA PLACA", $"{nativo} {erro} | {ida} | {volta}");

        // ---- o timer de hardware e os registradores: o tom feito na interrupcao ----
        const string timer = """
            #include "soc/gpio_reg.h"
            hw_timer_t *t;
            volatile bool alto = false;
            void IRAM_ATTR tique() {
              alto = !alto;
              if (alto) REG_WRITE(GPIO_OUT_W1TS_REG, 1UL << 4);
              else REG_WRITE(GPIO_OUT_W1TC_REG, 1UL << 4);
            }
            void setup() {
              pinMode(4, OUTPUT);
              t = timerBegin(1000000);
              timerAttachInterrupt(t, &tique);
              timerAlarm(t, 625, true, 0);     // a cada 625 us: 800 Hz
            }
            void loop() { delay(10); }
            """;
        int hz = 0;
        (serial, erro, nativo, _) = RodarNativo(timer, 900, durante: maq => hz = Math.Max(hz, maq.Frequencia[4]));
        Conferir("perifericos: timerBegin/timerAttachInterrupt/timerAlarm e REG_WRITE no GPIO -- o pino chaveado a cada 625 us vira o tom de 800 Hz",
            nativo && erro is null && Math.Abs(hz - 800) <= 16, $"{nativo} {erro} | {hz} Hz");

        // ---- o servo pelo LEDC (50 Hz, 14 bits) ----
        const string servo = """
            void setup() {
              ledcAttach(18, 50, 14);
              ledcWrite(18, 1638);   // 2,0 ms de pulso
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, var m) = RodarNativo(servo, 300);
        Conferir("perifericos: o servo comandado pelo LEDC a 50 Hz (pulso de 2 ms) gira como o da biblioteca Servo",
            nativo && erro is null && m.AnguloServo[18] is >= 140 and <= 150, $"{nativo} {erro} | angulo {m.AnguloServo[18]}");

        // ---- o I2S: as amostras chegam a saida de audio ----
        const string i2s = """
            #include <ESP_I2S.h>
            I2SClass i2s;
            int16_t buf[1600];
            void setup() {
              Serial.begin(115200);
              i2s.setPins(26, 25, 22);
              Serial.println(i2s.begin(I2S_MODE_STD, 16000, I2S_DATA_BIT_WIDTH_16BIT, I2S_SLOT_MODE_STEREO) ? "I2S OK" : "falhou");
              for (int i = 0; i < 1600; i += 2) buf[i] = buf[i + 1] = (int16_t)(8000 * sin(i * 0.05));
              size_t n = i2s.write((uint8_t *)buf, sizeof(buf));
              Serial.println(n);
            }
            void loop() { delay(10); }
            """;
        int amostras = 0;
        bool pinos = false;
        (serial, erro, nativo, _) = RodarNativo(i2s, 400, durante: maq =>
        {
            if (maq.Audio is not { } a) return;
            amostras = Math.Max(amostras, a.Disponiveis);
            pinos = a.Bclk == 26 && a.Ws == 25 && a.Dout == 22;
        });
        Conferir("perifericos: I2SClass do core 3.x -- as amostras vao para a saida de audio nos pinos dados",
            nativo && erro is null && serial == "I2S OK|3200" && pinos && amostras >= 800, $"{nativo} {erro} | {serial} | {amostras} quadros");

        // ---- o cartao: fs::FS, o read em bloco, a PSRAM, o strlcpy ----
        const string cartao = """
            #include <FS.h>
            #include <SD.h>
            void escrever(fs::FS &fs) {
              File f = fs.open("/bin.dat", FILE_WRITE);
              for (int i = 0; i < 5000; i++) f.write((uint8_t)(i * 7));
              f.close();
            }
            void setup() {
              Serial.begin(115200);
              Serial.println(SD.begin(5) ? "montou" : "sem cartao");
              escrever(SD);
              File f = SD.open("/bin.dat");
              uint8_t *b = (uint8_t *)ps_malloc(5000);
              int n = f.read(b, 5000);
              bool certo = n == 5000;
              for (int i = 0; i < n; i++) certo &= b[i] == (uint8_t)(i * 7);
              char curto[6];
              strlcpy(curto, "abcdefgh", sizeof(curto));
              Serial.println(String(n) + " " + (certo ? "igual" : "diferente") + " " + curto + " " + (psramFound() ? "psram" : "-"));
              SD.end();
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(cartao, 2500, durante: maq => maq.Cartao.Presente = true);
        Conferir("perifericos: fs::FS (a funcao que recebe o SD), o read(buf, n) em bloco de um arquivo binario, ps_malloc e strlcpy",
            nativo && erro is null && serial == "montou|5000 igual abcde psram", $"{nativo} {erro} | {serial}");
    }
}
