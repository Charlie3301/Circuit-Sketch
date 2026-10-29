using System.Net;
using System.Net.Sockets;

namespace Simulador.App;

/// <summary>
/// O que vem com o core da ESP32 (o arduino-esp32 da Espressif) no C++
/// compilado: os cabecalhos todos (a lista oficial em
/// nativo/arduino/cabecalhos-arduino-esp32.txt) e o comportamento de cada
/// familia -- o UDP, o NTP, o WiFiMulti, o Ticker, os sistemas de arquivos...
/// </summary>
public static partial class Autoteste
{
    static void Cpp_ArduinoEsp32()
    {
        saida.AppendLine("C++ compilado: o que vem com o core da ESP32 (arduino-esp32)");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("arduino-esp32: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- o UDP: o NTP pela rede (a hora do computador) e o PC falando com a placa ----
        const string udp = """
            #include <WiFi.h>
            #include <WiFiUdp.h>
            WiFiUDP ntp, eco;
            byte pacote[48];
            void setup() {
              Serial.begin(115200);
              WiFi.begin("rede", "");
              while (WiFi.status() != WL_CONNECTED) delay(50);
              eco.begin(4210);
              ntp.begin(2390);
              memset(pacote, 0, 48);
              pacote[0] = 0b11100011;
              ntp.beginPacket("pool.ntp.org", 123);
              ntp.write(pacote, 48);
              ntp.endPacket();
              unsigned long ate = millis() + 2000;
              while (!ntp.parsePacket() && millis() < ate) delay(10);
              ntp.read(pacote, 48);
              unsigned long segundos = (unsigned long)pacote[40] << 24 | (unsigned long)pacote[41] << 16 | (unsigned long)pacote[42] << 8 | pacote[43];
              Serial.println(segundos - 2208988800UL);
            }
            void loop() {
              int n = eco.parsePacket();
              if (n) {
                char t[64] = {0};
                eco.read(t, 63);
                eco.beginPacket(eco.remoteIP(), eco.remotePort());
                eco.print("eco:");
                eco.print(t);
                eco.endPacket();
              }
              delay(5);
            }
            """;
        string resposta = "";
        bool mandou = false;
        var (serial, erro, nativo, _) = RodarNativo(udp, 3000, durante: maq =>
        {
            if (mandou || maq.Rede is null || !maq.Serial.Any()) return;
            mandou = true;
            try
            {
                using var pc = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                pc.Client.ReceiveTimeout = 2000;
                pc.Send("ola"u8.ToArray(), 3, new IPEndPoint(IPAddress.Loopback, 4210));
                IPEndPoint de = null;
                resposta = System.Text.Encoding.ASCII.GetString(pc.Receive(ref de));
            }
            catch (Exception e) { resposta = "erro: " + e.Message; }
        });
        long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool horaCerta = long.TryParse(serial?.Split('|').LastOrDefault(), out long hora) && Math.Abs(hora - agora) < 60;
        Conferir("arduino-esp32: WiFiUDP -- o pedido NTP ao pool.ntp.org volta com a hora (a rede responde com o relogio do computador)",
            nativo && erro is null && horaCerta, $"{nativo} {erro} | {serial} | agora {agora}");
        Conferir("arduino-esp32: WiFiUDP -- um programa do computador manda para a porta da placa (127.0.0.1) e recebe a resposta",
            nativo && resposta == "eco:ola", $"{resposta}");

        // ---- os eventos do WiFi (a tarefa arduino_events) e o nome da placa na rede ----
        const string eventos = """
            #include <WiFi.h>
            volatile bool pegouIp = false;
            void aoEvento(WiFiEvent_t evento, WiFiEventInfo_t info) {
              switch (evento) {
                case ARDUINO_EVENT_WIFI_STA_START: Serial.println("start"); break;
                case ARDUINO_EVENT_WIFI_STA_CONNECTED: Serial.println(String("conectou ") + (char *)info.wifi_sta_connected.ssid); break;
                case ARDUINO_EVENT_WIFI_STA_GOT_IP:
                  Serial.println(String("ip ") + IPAddress(info.got_ip.ip_info.ip.addr).toString());
                  pegouIp = true;
                  break;
                default: break;
              }
            }
            void setup() {
              Serial.begin(115200);
              WiFi.onEvent(aoEvento);
              WiFi.setHostname("minhaplaca");
              WiFi.begin("CircuitSketch-WiFi", "");
              while (!pegouIp) delay(20);
              IPAddress achado;
              Serial.println(WiFi.hostByName("minhaplaca", achado) && achado == WiFi.localIP() ? "nome ok" : "nome errado");
              Serial.println(WiFi.STA.localIP() == WiFi.localIP() && WiFi.subnetCIDR() == 24 ? "sta ok" : "sta errado");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(eventos, 3000);
        var linhas = (serial ?? "").Split('|');
        Conferir("arduino-esp32: WiFi.onEvent -- STA_START, STA_CONNECTED (com o SSID) e STA_GOT_IP (com o IP) chegam na ordem da placa",
            nativo && erro is null && linhas.Length >= 3 && linhas[0] == "start" && linhas[1] == "conectou CircuitSketch-WiFi"
            && linhas[2].StartsWith("ip 10.0.0.", StringComparison.Ordinal), $"{nativo} {erro} | {serial}");
        Conferir("arduino-esp32: WiFi.hostByName acha a placa pelo nome que ela deu (setHostname), e o WiFi.STA do 3.x responde",
            nativo && serial?.Contains("nome ok|sta ok") == true, serial);

        // ---- o LittleFS na flash: a placa nova nao monta sem formatar ----
        const string flash = """
            #include <LittleFS.h>
            #include <SPIFFS.h>
            void setup() {
              Serial.begin(115200);
              Serial.println(LittleFS.begin() ? "montou" : "sem formatar");
              Serial.println(LittleFS.begin(true) ? "formatou" : "falhou");
              File f = LittleFS.open("/config.txt", FILE_WRITE);
              f.print("ssid=casa");
              f.close();
              LittleFS.rename("/config.txt", "/cfg.txt");
              f = LittleFS.open("/cfg.txt");
              String s = f.readString();
              f.close();
              Serial.println(s + " " + (LittleFS.exists("/config.txt") ? "velho" : "renomeou") + " "
                             + (LittleFS.usedBytes() == 4096 ? "4096" : String((unsigned long)LittleFS.usedBytes()))
                             + " " + String((unsigned long)LittleFS.totalBytes()));
              Serial.println(SPIFFS.begin() && SPIFFS.exists("/cfg.txt") ? "a mesma flash" : "outra");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(flash, 1500);
        Conferir("arduino-esp32: LittleFS -- begin() da placa nova falha, begin(true) formata, grava/le/renomeia na flash e mede 1 bloco usado de 0x160000",
            nativo && erro is null && serial == "sem formatar|formatou|ssid=casa renomeou 4096 1441792|a mesma flash", $"{nativo} {erro} | {serial}");

        // ---- o Ticker e o WiFiMulti ----
        const string ticker = """
            #include <WiFi.h>
            #include <WiFiMulti.h>
            #include <Ticker.h>
            Ticker t, umaVez;
            volatile int batidas = 0;
            volatile bool disparou = false;
            WiFiMulti multi;
            void sete(int x) { disparou = x == 7; }
            void setup() {
              Serial.begin(115200);
              t.attach_ms(20, []() { batidas++; });
              umaVez.once_ms(50, sete, 7);
              delay(205);
              t.detach();
              int vistas = batidas;
              delay(60);
              Serial.println(String(vistas >= 9 && vistas <= 11 ? "ticker ok" : "ticker " + String(vistas)) + (batidas == vistas ? " parou" : " seguiu")
                             + (disparou ? " uma vez" : ""));
              int n = WiFi.scanNetworks();
              multi.addAP("rede-que-nao-existe", "x");
              if (n > 0) multi.addAP(WiFi.SSID(0).c_str(), "");
              Serial.println(multi.run() == WL_CONNECTED ? "conectou na que existe" : "nao conectou");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(ticker, 3000);
        Conferir("arduino-esp32: Ticker (attach_ms a cada 20 ms, once_ms com argumento, detach) e WiFiMulti escolhendo a rede da varredura",
            nativo && erro is null && serial == "ticker ok parou uma vez|conectou na que existe", $"{nativo} {erro} | {serial}");

        // ---- o Update (OTA) e o Hash: o MD5 confere, o byte magico errado e recusado ----
        const string update = """
            #include <Update.h>
            #include <MD5Builder.h>
            #include <SHA2Builder.h>
            uint8_t img[2048];
            void setup() {
              Serial.begin(115200);
              for (int i = 0; i < 2048; i++) img[i] = i * 7;
              img[0] = 0xE9;
              MD5Builder md5;
              md5.begin();
              md5.add(img, 2048);
              md5.calculate();
              Update.begin(2048);
              Update.setMD5(md5.toString().c_str());
              Update.write(img, 1024);
              Update.write(img + 1024, 1024);
              Serial.println(Update.end() ? "ok" : Update.errorString());
              img[0] = 0;
              Update.begin(2048);
              Update.write(img, 1024);
              Serial.println(Update.errorString());
              SHA256Builder sha;
              sha.begin();
              sha.add("abc");
              sha.calculate();
              Serial.println(sha.toString().substring(0, 16));
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(update, 1500);
        Conferir("arduino-esp32: Update -- a imagem com o MD5 certo fecha (end), a sem o byte magico 0xE9 e recusada; o SHA256Builder do Hash",
            nativo && erro is null && serial?.Contains("|ok|") == true && serial.Contains("Wrong Magic Byte|ba7816bf8f01cfea"), $"{nativo} {erro} | {serial}");

        // ---- o mDNS: a placa responde por "nome.local" ----
        const string mdns = """
            #include <WiFi.h>
            #include <ESPmDNS.h>
            void setup() {
              Serial.begin(115200);
              WiFi.begin("CircuitSketch-WiFi", "");
              while (WiFi.status() != WL_CONNECTED) delay(20);
              Serial.println(MDNS.begin("sensor") ? "mdns" : "falhou");
              MDNS.addService("http", "tcp", 80);
              IPAddress ip;
              Serial.println(WiFi.hostByName("sensor.local", ip) && ip == WiFi.localIP() ? "achou" : "nao achou");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(mdns, 2000);
        Conferir("arduino-esp32: ESPmDNS -- MDNS.begin(\"sensor\") e o hostByName(\"sensor.local\") acha a placa",
            nativo && erro is null && serial == "mdns|achou", $"{nativo} {erro} | {serial}");

        // ---- o WiFi pela API do ESP-IDF (o exemplo "station") ----
        const string estacao = """
            #include <string.h>
            #include "freertos/FreeRTOS.h"
            #include "freertos/event_groups.h"
            #include "esp_wifi.h"
            #include "esp_event.h"
            #include "esp_log.h"
            #include "nvs_flash.h"
            #include "esp_netif.h"

            static EventGroupHandle_t grupo;
            static const char *TAG = "wifi station";

            static void handler(void *arg, esp_event_base_t base, int32_t id, void *data)
            {
                if (base == WIFI_EVENT && id == WIFI_EVENT_STA_START) {
                    esp_wifi_connect();
                } else if (base == IP_EVENT && id == IP_EVENT_STA_GOT_IP) {
                    ip_event_got_ip_t *e = (ip_event_got_ip_t *)data;
                    ESP_LOGI(TAG, "got ip:" IPSTR, IP2STR(&e->ip_info.ip));
                    xEventGroupSetBits(grupo, BIT0);
                }
            }

            void app_main(void)
            {
                ESP_ERROR_CHECK(nvs_flash_init());
                grupo = xEventGroupCreate();
                ESP_ERROR_CHECK(esp_netif_init());
                ESP_ERROR_CHECK(esp_event_loop_create_default());
                esp_netif_create_default_wifi_sta();
                wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
                ESP_ERROR_CHECK(esp_wifi_init(&cfg));
                ESP_ERROR_CHECK(esp_event_handler_instance_register(WIFI_EVENT, ESP_EVENT_ANY_ID, &handler, NULL, NULL));
                ESP_ERROR_CHECK(esp_event_handler_instance_register(IP_EVENT, IP_EVENT_STA_GOT_IP, &handler, NULL, NULL));
                wifi_config_t wc = {};
                strcpy((char *)wc.sta.ssid, "CircuitSketch-WiFi");
                ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
                ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_STA, &wc));
                ESP_ERROR_CHECK(esp_wifi_start());
                EventBits_t b = xEventGroupWaitBits(grupo, BIT0, pdFALSE, pdFALSE, pdMS_TO_TICKS(3000));
                printf("%s\n", (b & BIT0) ? "conectou" : "nao conectou");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(estacao, 3000);
        string semTempo = System.Text.RegularExpressions.Regex.Replace(serial ?? "", @"\(\d+\)", "(t)");
        Conferir("ESP-IDF: o exemplo station -- esp_wifi_init/set_config/start, o WIFI_EVENT_STA_START conecta e o IP_EVENT_STA_GOT_IP traz o IP",
            nativo && erro is null && semTempo.Contains("I (t) wifi station: got ip:10.0.0.") && semTempo.EndsWith("|conectou", StringComparison.Ordinal),
            $"{nativo} {erro} | {serial}");
    }
}
