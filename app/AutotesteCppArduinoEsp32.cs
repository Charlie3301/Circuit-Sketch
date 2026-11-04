using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.IO;

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

        // ---- o WebServer de verdade: o navegador do computador pede, a placa responde ----
        const string web = """
            #include <WiFi.h>
            #include <WebServer.h>
            #include <uri/UriBraces.h>
            #include <uri/UriGlob.h>
            #include <uri/UriRegex.h>
            #include <LittleFS.h>
            #include <Middlewares.h>
            WebServer server(80);
            CorsMiddleware cors;
            void setup() {
              Serial.begin(115200);
              WiFi.begin("CircuitSketch-WiFi", "");
              while (WiFi.status() != WL_CONNECTED) delay(20);
              LittleFS.begin(true);
              File f = LittleFS.open("/estilo.css", FILE_WRITE);
              f.print("body{color:red}");
              f.close();
              cors.setOrigin("*");
              server.collectAllHeaders();
              server.addMiddleware(&cors);
              server.on("/", []() { server.send(200, "text/plain", "raiz"); });
              server.on(UriBraces("/led/{}"), []() { server.send(200, "text/plain", "led " + server.pathArg(0)); });
              server.on(UriRegex("^\\/sensor\\/([0-9]+)$"), []() { server.send(200, "text/plain", "sensor " + server.pathArg(0)); });
              server.on(UriGlob("/arq/*.txt"), []() { server.send(200, "text/plain", "glob " + server.uri()); });
              server.on("/soma", HTTP_POST, []() { server.send(200, "text/plain", String(server.arg("a").toInt() + server.arg("b").toInt())); });
              server.on("/seg", []() {
                if (!server.authenticate("adm", "123")) return server.requestAuthentication();
                server.send(200, "text/plain", "entrou");
              });
              server.serveStatic("/static", LittleFS, "/");
              server.onNotFound([]() { server.send(404, "text/plain", "nao achei " + server.uri()); });
              server.begin();
              Serial.println("pronto");
            }
            void loop() {
              server.handleClient();
              delay(2);
            }
            """;
        var respostas = new List<string>();
        bool pediu = false;
        (serial, erro, nativo, _) = RodarNativo(web, 6000, durante: (Maquina maq) =>
        {
            if (pediu || maq.Rede?.Servidores.FirstOrDefault()?.UrlLocal is not { } url || !maq.Serial.Any(l => l.Contains("pronto"))) return;
            pediu = true;
            try
            {
                using var navegador = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                string Pedir(HttpRequestMessage pedido)
                {
                    // A pagina de outro site (o CorsMiddleware so responde a quem manda o Origin).
                    pedido.Headers.Add("Origin", "http://exemplo.com");
                    var r = navegador.Send(pedido);
                    string corpo = r.Content.ReadAsStringAsync().Result;
                    string tipo = r.Content.Headers.ContentType?.MediaType ?? "";
                    string cors = r.Headers.TryGetValues("Access-Control-Allow-Origin", out var o) ? " cors=" + string.Join(",", o) : "";
                    return $"{(int)r.StatusCode} {tipo} {corpo}{cors}";
                }
                string Get(string caminho) => Pedir(new HttpRequestMessage(HttpMethod.Get, url + caminho.TrimStart('/')));
                respostas.Add(Get("/"));
                respostas.Add(Get("/led/7"));
                respostas.Add(Get("/sensor/42"));
                respostas.Add(Get("/arq/notas.txt"));
                respostas.Add(Pedir(new HttpRequestMessage(HttpMethod.Post, url + "soma")
                {
                    Content = new FormUrlEncodedContent([new("a", "2"), new("b", "3")]),
                }));
                respostas.Add(Get("/static/estilo.css"));
                respostas.Add(Get("/seg"));
                var comSenha = new HttpRequestMessage(HttpMethod.Get, url + "seg");
                comSenha.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String("adm:123"u8.ToArray()));
                respostas.Add(Pedir(comSenha));
                respostas.Add(Get("/x"));
            }
            catch (Exception e) { respostas.Add("erro: " + e.GetBaseException().Message); }
        });
        string todas = string.Join(" | ", respostas);
        Conferir("arduino-esp32: WebServer de verdade -- rotas, UriBraces/UriRegex/UriGlob, POST com formulario, serveStatic do LittleFS, CorsMiddleware, Basic auth e 404",
            nativo && erro is null
            && todas == "200 text/plain raiz cors=* | 200 text/plain led 7 cors=* | 200 text/plain sensor 42 cors=* | 200 text/plain glob /arq/notas.txt cors=* "
                      + "| 200 text/plain 5 cors=* | 200 text/css body{color:red} cors=* | 401 text/html  cors=* | 200 text/plain entrou cors=* | 404 text/plain nao achei /x cors=*",
            $"{nativo} {erro} | {serial} | {todas}");

        // ---- o WiFiClientSecure (NetworkClientSecure) e o mbedTLS de verdade ----
        using var chave = System.Security.Cryptography.RSA.Create(2048);
        var pedidoDoCertificado = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost", chave, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var provisorio = pedidoDoCertificado.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        using var certificado = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            provisorio.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        int portaTls = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        var servidorTls = Task.Run(() =>
        {
            try
            {
                using var tcp = ouvinte.AcceptTcpClient();
                using var ssl = new System.Net.Security.SslStream(tcp.GetStream());
                ssl.AuthenticateAsServer(certificado);
                using var leitor = new StreamReader(ssl);
                string linha = leitor.ReadLine();
                ssl.Write(System.Text.Encoding.ASCII.GetBytes("eco:" + linha + "\n"));
                ssl.Flush();
                Thread.Sleep(300);
            }
            catch { }
        });
        const string tls = """
            #include <WiFi.h>
            #include <WiFiClientSecure.h>
            #include "mbedtls/md.h"
            WiFiClientSecure cliente;
            void setup() {
              Serial.begin(115200);
              WiFi.begin("CircuitSketch-WiFi", "");
              while (WiFi.status() != WL_CONNECTED) delay(20);
              Serial.println(cliente.connect("127.0.0.1", PORTA) ? "conectou sem CA" : "recusou sem CA");
              cliente.setInsecure();
              if (!cliente.connect("127.0.0.1", PORTA)) { Serial.println("falhou"); return; }
              uint8_t sha[32];
              cliente.getFingerprintSHA256(sha);
              char hex[65];
              for (int i = 0; i < 32; i++) sprintf(hex + 2 * i, "%02x", sha[i]);
              Serial.println(hex);
              char dn[128];
              mbedtls_x509_dn_gets(dn, sizeof(dn), &cliente.getPeerCertificate()->subject);
              Serial.println(dn);
              Serial.println(cliente.verify(hex, "localhost") ? "verify ok" : "verify falhou");
              cliente.print("ola\n");
              unsigned long ate = millis() + 2000;
              while (!cliente.available() && millis() < ate) delay(5);
              Serial.println(cliente.readStringUntil('\n'));
              cliente.stop();
              unsigned char hmac[32];
              const char *segredo = "chave";
              mbedtls_md_context_t ctx;
              mbedtls_md_init(&ctx);
              mbedtls_md_setup(&ctx, mbedtls_md_info_from_type(MBEDTLS_MD_SHA256), 1);
              mbedtls_md_hmac_starts(&ctx, (const unsigned char *)segredo, strlen(segredo));
              mbedtls_md_hmac_update(&ctx, (const unsigned char *)"mensagem", 8);
              mbedtls_md_hmac_finish(&ctx, hmac);
              mbedtls_md_free(&ctx);
              for (int i = 0; i < 8; i++) Serial.printf("%02x", hmac[i]);
              Serial.println();
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(tls.Replace("PORTA", portaTls.ToString()), 4000);
        ouvinte.Stop();
        servidorTls.Wait(2000);
        string impressaoDoCertificado = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(certificado.RawData));
        string hmacEsperado = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData("chave"u8, "mensagem"u8))[..16];
        var linhasTls = (serial ?? "").Split('|');
        Conferir("arduino-esp32: WiFiClientSecure -- sem CA nem setInsecure recusa (como o start_ssl_client); com setInsecure conecta por TLS e troca dados",
            nativo && erro is null && linhasTls.Contains("recusou sem CA") && linhasTls.Contains("eco:ola"), $"{nativo} {erro} | {serial}");
        Conferir("arduino-esp32: WiFiClientSecure -- getFingerprintSHA256, getPeerCertificate (o x509 do mbedTLS) e verify(impressao, \"localhost\")",
            linhasTls.Contains(impressaoDoCertificado) && linhasTls.Contains("CN=localhost") && linhasTls.Contains("verify ok"),
            $"esperava {impressaoDoCertificado} | {serial}");
        Conferir("mbedTLS: mbedtls_md HMAC-SHA256 (a biblioteca de verdade, compilada para a placa)",
            linhasTls.LastOrDefault() == hmacEsperado, $"esperava {hmacEsperado} | {serial}");

        // ---- o Bluetooth classico: o discover acha a outra placa e conecta pelo endereco ----
        const string busca = """
            #include <BluetoothSerial.h>
            BluetoothSerial SerialBT;
            void setup() {
              Serial.begin(115200);
              SerialBT.begin("procura", true);
              BTScanResults *r = SerialBT.discover(1000);
              Serial.println(r->getCount());
              for (int i = 0; i < r->getCount(); i++) {
                BTAdvertisedDevice *d = r->getDevice(i);
                Serial.println(String(d->getName().c_str()) + " " + d->getAddress().toString());
                if (d->getName() == "alvo") Serial.println(SerialBT.connect(d->getAddress()) ? "conectou" : "nao conectou");
              }
            }
            void loop() { delay(10); }
            """;
        Maquina outra = null;
        (serial, erro, nativo, _) = RodarNativo(busca, 2500, antes: (_, maq) =>
        {
            outra = new Maquina { Perfil = PerfilPlaca.Esp32, Nome = "outra", Rede = maq.Rede };
            maq.Rede.IniciarBluetooth(outra, "alvo", false);
        });
        string mac = outra?.Rede.Wifi(outra).Mac.ToLowerInvariant();
        Conferir("arduino-esp32: BluetoothSerial.discover -- o BTScanResults (o do core) acha a outra placa pelo nome e endereco, e o connect(BTAddress) conecta",
            nativo && erro is null && serial == $"1|alvo {mac}|conectou", $"{nativo} {erro} | {serial} | mac {mac}");

        // ---- o iBeacon: o BLEBeacon do core le o anuncio de outra placa e monta o da placa ----
        const string farol = """
            #include <BLEDevice.h>
            #include <BLEBeacon.h>
            void setup() {
              Serial.begin(115200);
              BLEDevice::init("leitor");
              BLEScan *scan = BLEDevice::getScan();
              scan->setActiveScan(true);
              BLEScanResults *r = scan->start(1, false);
              for (int i = 0; i < r->getCount(); i++) {
                BLEAdvertisedDevice d = r->getDevice(i);
                if (!d.haveManufacturerData()) continue;
                String m = d.getManufacturerData();
                if (m.length() == 25 && (uint8_t)m[0] == 0x4C && (uint8_t)m[2] == 0x02) {
                  BLEBeacon b;
                  b.setData(m);
                  Serial.printf("ibeacon %s %d %d %d\n", b.getProximityUUID().toString().c_str(), b.getMajor(), b.getMinor(), b.getSignalPower());
                }
              }
              BLEBeacon meu;
              meu.setManufacturerId(0x4C00);
              meu.setProximityUUID(BLEUUID("8ec76ea3-6668-48da-9866-75be8bc86f4d"));
              meu.setMajor(7);
              meu.setMinor(9);
              meu.setSignalPower(-59);
              BLEAdvertisementData dados;
              dados.setFlags(0x04);
              String ad = "";
              ad += (char)26;
              ad += (char)0xFF;
              ad += meu.getData();
              dados.addData(ad);
              BLEAdvertising *anuncio = BLEDevice::getAdvertising();
              anuncio->setAdvertisementData(dados);
              anuncio->start();
              Serial.println("anunciando");
            }
            void loop() { delay(10); }
            """;
        Maquina vizinha = null;
        string noAr = null;
        (serial, erro, nativo, _) = RodarNativo(farol, 3000, antes: (_, maq) =>
        {
            vizinha = new Maquina { Perfil = PerfilPlaca.Esp32, Nome = "farol", Rede = maq.Rede };
            var radio = maq.Rede.IniciarBle(vizinha, "farol");
            // iBeacon: 4C00 (Apple), 02 15, o UUID, major 0x0102, minor 0x0304, -59 dBm.
            radio.FabricanteHex = "4c000215" + "e2c56db5dffb48d2b060d0f5a71096e0" + "0102" + "0304" + "c5";
            maq.Rede.AnunciarBle(radio, true);
        }, durante: maq =>
        {
            if (noAr is null && vizinha is not null && maq.Serial.Any(l => l.Contains("anunciando")))
                noAr = maq.Rede.AnunciosBleVistosDe(vizinha).FirstOrDefault()?.FabricanteHex;
        });
        Conferir("arduino-esp32: BLEBeacon -- o anuncio iBeacon de outra placa chega com os dados do fabricante, e o BLEBeacon le UUID, major, minor e potencia",
            nativo && erro is null && serial?.Contains("ibeacon e2c56db5-dffb-48d2-b060-d0f5a71096e0 513 1027 -59") == true, $"{nativo} {erro} | {serial}");
        // O getMajor()/getMinor() do core devolvem os bytes como estao (o exemplo
        // BLE_Beacon_Scanner aplica o ENDIAN_CHANGE_U16), e o setProximityUUID poe o
        // UUID na ordem do Bluedroid (do byte menos significativo): como na placa.
        Conferir("arduino-esp32: BLEBeacon + addData -- o iBeacon da placa vai ao ar com os 25 bytes do fabricante",
            noAr == "4c000215" + "4d6fc88bbe756698da486866a36ec78e" + "0007" + "0009" + "c5", $"no ar: {noAr}");

        // ---- o USB da S3: o USBHIDKeyboard, o USBHIDMouse e o USBMIDI do core, pelo TinyUSB ----
        const string usb = """
            #include "USB.h"
            #include "USBHIDKeyboard.h"
            #include "USBHIDMouse.h"
            #include "USBMIDI.h"
            USBHIDKeyboard Teclado;
            USBHIDMouse Mouse;
            USBMIDI MIDI;
            void setup() {
              Serial.begin(115200);
              Teclado.begin();
              Mouse.begin();
              MIDI.begin();
              USB.begin();
              delay(100);
              Teclado.print("Ola, USB!\n");
              Mouse.move(10, -5);
              Mouse.click();
              MIDI.noteOn(60, 100, 1);
              Serial.println("pronto");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, var placaUsb) = RodarNativo(usb, 2500, placa: "esp32-s3-devkit");
        var eventosUsb = placaUsb.Usb.Eventos;
        Conferir("arduino-esp32: USB da S3 -- a placa aparece no computador (HID e MIDI) e o USBHIDKeyboard digita \"Ola, USB!\" (o relatorio HID de verdade)",
            nativo && erro is null && placaUsb.Usb.Conectada && placaUsb.Usb.Interfaces.Contains("HID") && placaUsb.Usb.Interfaces.Contains("MIDI")
            && placaUsb.Usb.Digitado == "Ola, USB!\n", $"{nativo} {erro} | {serial} | {placaUsb.Usb.Interfaces} | {placaUsb.Usb.Digitado} | {string.Join(" / ", eventosUsb)}");
        Conferir("arduino-esp32: USB da S3 -- o USBHIDMouse anda (10,-5) e clica, e o USBMIDI manda a nota C4",
            placaUsb.Usb.MouseX == 10 && placaUsb.Usb.MouseY == -5 && placaUsb.Usb.Cliques == 1 && eventosUsb.Any(e => e.StartsWith("midi nota C4 ligou")),
            string.Join(" / ", eventosUsb));

        // ---- o Ethernet: o ETH.h de verdade, com o cabo na rede da bancada e o WiFi desligado ----
        const string cabo = """
            #include <ETH.h>
            #include <WiFi.h>
            #include <WebServer.h>
            volatile bool pronto = false;
            WebServer server(80);
            void aoEvento(arduino_event_id_t e, arduino_event_info_t info) {
              switch (e) {
                case ARDUINO_EVENT_ETH_START: Serial.println("start"); break;
                case ARDUINO_EVENT_ETH_CONNECTED: Serial.println("conectou"); break;
                case ARDUINO_EVENT_ETH_GOT_IP:
                  Serial.println(String("ip ") + IPAddress(info.got_ip.ip_info.ip.addr).toString());
                  pronto = true;
                  break;
                default: break;
              }
            }
            void setup() {
              Serial.begin(115200);
              Network.onEvent(aoEvento);
              ETH.begin(ETH_PHY_W5500, 1, 5, 4, 2, SPI2_HOST, 18, 19, 23);
              while (!pronto) delay(10);
              Serial.println(ETH.linkUp() && ETH.localIP() != IPAddress() ? "enlace ok" : "sem enlace");
              Serial.println(WiFi.status() == WL_CONNECTED ? "wifi ligado" : "wifi desligado");
              server.on("/", []() { server.send(200, "text/plain", "pelo cabo"); });
              server.begin();
              Serial.println("pronto");
            }
            void loop() {
              server.handleClient();
              delay(2);
            }
            """;
        string paginaPeloCabo = null;
        (serial, erro, nativo, _) = RodarNativo(cabo, 4000, durante: (Maquina maq) =>
        {
            if (paginaPeloCabo is not null || maq.Rede?.Servidores.FirstOrDefault()?.UrlLocal is not { } url || !maq.Serial.Any(l => l.Contains("pronto"))) return;
            try
            {
                using var navegador = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                paginaPeloCabo = navegador.GetStringAsync(url).Result;
            }
            catch (Exception e) { paginaPeloCabo = "erro: " + e.GetBaseException().Message; }
        });
        var linhasCabo = (serial ?? "").Split('|');
        Conferir("arduino-esp32: ETH -- ETH_START, ETH_CONNECTED e ETH_GOT_IP na ordem da placa, com o enlace e o IP, e o WiFi continua desligado",
            nativo && erro is null && linhasCabo.Length >= 5 && linhasCabo[0] == "start" && linhasCabo[1] == "conectou"
            && linhasCabo[2].StartsWith("ip 10.0.0.", StringComparison.Ordinal) && linhasCabo[3] == "enlace ok" && linhasCabo[4] == "wifi desligado",
            $"{nativo} {erro} | {serial}");
        Conferir("arduino-esp32: ETH -- o navegador do computador abre a pagina do WebServer pelo cabo",
            paginaPeloCabo == "pelo cabo", $"{paginaPeloCabo} | {serial}");

        // ---- os sockets do lwIP: um programa do IDF, como os exemplos tcp_server e udp_server ----
        const string sockets = """
            #include <string.h>
            #include <unistd.h>
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            #include "esp_wifi.h"
            #include "esp_event.h"
            #include "esp_netif.h"
            #include "lwip/sockets.h"
            #include "lwip/netdb.h"
            #include "lwip/err.h"

            static volatile bool temIp = false;
            static void aoEvento(void *arg, esp_event_base_t base, int32_t id, void *data)
            {
                if (base == WIFI_EVENT && id == WIFI_EVENT_STA_START) esp_wifi_connect();
                else if (base == IP_EVENT && id == IP_EVENT_STA_GOT_IP) temIp = true;
            }

            void app_main(void)
            {
                esp_netif_init();
                esp_event_loop_create_default();
                esp_netif_create_default_wifi_sta();
                wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
                esp_wifi_init(&cfg);
                esp_event_handler_instance_register(ESP_EVENT_ANY_BASE, ESP_EVENT_ANY_ID, &aoEvento, NULL, NULL);
                wifi_config_t wc = {};
                strcpy((char *)wc.sta.ssid, "CircuitSketch-WiFi");
                esp_wifi_set_mode(WIFI_MODE_STA);
                esp_wifi_set_config(WIFI_IF_STA, &wc);
                esp_wifi_start();
                while (!temIp) vTaskDelay(10 / portTICK_PERIOD_MS);

                // O servidor TCP, como o tcp_server do IDF: atende o programa do computador.
                int ouvinte = socket(AF_INET, SOCK_STREAM, IPPROTO_IP);
                struct sockaddr_in local = {};
                local.sin_family = AF_INET;
                local.sin_addr.s_addr = htonl(INADDR_ANY);
                local.sin_port = htons(3333);
                int um = 1;
                setsockopt(ouvinte, SOL_SOCKET, SO_REUSEADDR, &um, sizeof(um));
                bind(ouvinte, (struct sockaddr *)&local, sizeof(local));
                listen(ouvinte, 1);

                // O UDP, como o udp_server: a mesma porta recebe e responde.
                int udp = socket(AF_INET, SOCK_DGRAM, IPPROTO_IP);
                struct sockaddr_in localUdp = {};
                localUdp.sin_family = AF_INET;
                localUdp.sin_addr.s_addr = htonl(INADDR_ANY);
                localUdp.sin_port = htons(3334);
                bind(udp, (struct sockaddr *)&localUdp, sizeof(localUdp));

                struct in_addr a;
                inet_aton("192.168.1.20", &a);
                printf("%s %08x\n", inet_ntoa(a), (unsigned)ntohl(a.s_addr));
                printf("pronto\n");

                struct timeval espera = {};
                espera.tv_sec = 3;
                setsockopt(udp, SOL_SOCKET, SO_RCVTIMEO, &espera, sizeof(espera));
                char buf[64];
                struct sockaddr_in quem;
                socklen_t tam = sizeof(quem);
                int n = recvfrom(udp, buf, sizeof(buf) - 1, 0, (struct sockaddr *)&quem, &tam);
                if (n > 0) {
                    buf[n] = 0;
                    printf("udp %s de %s\n", buf, inet_ntoa(quem.sin_addr));
                    sendto(udp, "pong", 4, 0, (struct sockaddr *)&quem, tam);
                }
                close(udp);

                struct sockaddr_in cliente;
                socklen_t tamc = sizeof(cliente);
                int s = accept(ouvinte, (struct sockaddr *)&cliente, &tamc);
                n = recv(s, buf, sizeof(buf) - 1, 0);
                if (n > 0) {
                    buf[n] = 0;
                    printf("tcp %s\n", buf);
                    send(s, buf, n, 0);
                }
                shutdown(s, 0);
                close(s);
                close(ouvinte);

                // O cliente: o nome da placa no DNS da bancada e um servidor que nao existe.
                struct addrinfo dicas = {};
                dicas.ai_family = AF_INET;
                dicas.ai_socktype = SOCK_STREAM;
                struct addrinfo *res = NULL;
                int r = getaddrinfo("10.0.0.2", "80", &dicas, &res);
                printf("getaddrinfo %d %d\n", r, res ? ntohs(((struct sockaddr_in *)res->ai_addr)->sin_port) : -1);
                if (res) freeaddrinfo(res);
                int c = socket(AF_INET, SOCK_STREAM, 0);
                struct sockaddr_in alvo = {};
                alvo.sin_family = AF_INET;
                alvo.sin_addr.s_addr = inet_addr("10.0.0.99");
                alvo.sin_port = htons(80);
                printf("connect %d errno %s\n", connect(c, (struct sockaddr *)&alvo, sizeof(alvo)), errno == ECONNREFUSED ? "recusado" : "outro");
                close(c);
                printf("fim\n");
            }
            """;
        string doTcp = "", doUdp = "";
        bool conversou = false;
        (serial, erro, nativo, _) = RodarNativo(sockets, 6000, durante: (Maquina maq) =>
        {
            if (conversou || !maq.Serial.Any(l => l.Contains("pronto"))) return;
            conversou = true;
            try
            {
                using var pcUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                pcUdp.Client.ReceiveTimeout = 3000;
                pcUdp.Send("ping"u8.ToArray(), 4, new IPEndPoint(IPAddress.Loopback, 3334));
                IPEndPoint de = null;
                doUdp = System.Text.Encoding.ASCII.GetString(pcUdp.Receive(ref de));
                string url = maq.Rede.Servidores.First(s => s.UrlLocal is not null).UrlLocal;
                using var pc = new TcpClient();
                var fim = new Uri(url);
                pc.Connect(fim.Host, fim.Port);
                pc.ReceiveTimeout = 3000;
                pc.GetStream().Write("ola"u8);
                var b = new byte[16];
                int lidos = pc.GetStream().Read(b);
                doTcp = System.Text.Encoding.ASCII.GetString(b, 0, lidos);
            }
            catch (Exception e) { doTcp += " erro: " + e.GetBaseException().Message; }
        });
        Conferir("ESP-IDF: sockets do lwIP -- socket/bind/recvfrom/sendto (UDP) e socket/bind/listen/accept/recv/send/close (TCP) com um programa do computador",
            nativo && erro is null && doUdp == "pong" && doTcp == "ola" && serial?.Contains("udp ping de ") == true && serial.Contains("tcp ola"),
            $"{nativo} {erro} | udp {doUdp} | tcp {doTcp} | {serial}");
        Conferir("ESP-IDF: inet_aton/inet_ntoa, getaddrinfo e o connect recusado (errno ECONNREFUSED), como no lwIP",
            serial?.Contains("192.168.1.20 c0a80114") == true && serial.Contains("getaddrinfo 0 80") && serial.Contains("connect -1 errno recusado") && serial.Contains("fim"),
            serial);

        // ---- as bibliotecas rapidas: o Console de verdade, o Insights, o PPP e o ESP_SR sem o que eles precisam ----
        const string rapidos = """
            #include <Console.h>
            #include <Insights.h>
            #include <PPP.h>
            #include <ESP_SR.h>
            int cmdSoma(int argc, char **argv) {
              if (argc != 3) return 1;
              Serial.println(String("soma ") + (atoi(argv[1]) + atoi(argv[2])));
              return 0;
            }
            void setup() {
              Serial.begin(115200);
              Console.begin();
              Console.addCmd("soma", "soma dois numeros", cmdSoma);
              int r = Console.run("soma 2 3");
              Serial.println(String("run ") + r);
              Serial.println(String("desconhecido ") + (Console.run("naoexiste") != 0 ? "erro" : "ok"));
              Serial.println(Insights.begin("") ? "insights sem chave ligou" : "insights sem chave recusou");
              Insights.begin("chave");
              Insights.metrics.addInt("t", "k", "rotulo", "x.y");
              Serial.println(String("metrica ") + Insights.metrics.setInt("k", 7) + " " + Insights.metrics.setInt("outra", 7));
              PPP.setPins(17, 16);
              Serial.println(PPP.begin(PPP_MODEM_SIM800) ? "ppp ligou" : "ppp sem modem");
              Serial.println(PPP.connected() ? "ppp conectado" : "ppp desconectado");
              Serial.println("fim");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(rapidos, 2000, placa: "esp32-s3-devkit");
        Conferir("arduino-esp32: Console -- o comando registrado roda (o esp_console e o argtable3 do IDF), e o desconhecido da erro",
            nativo && erro is null && serial?.Contains("soma 5|run 0") == true && serial.Contains("desconhecido erro"), $"{nativo} {erro} | {serial}");
        Conferir("arduino-esp32: Insights sem chave recusa, e a metrica so grava se foi registrada; PPP sem modem no circuito nao liga",
            serial?.Contains("insights sem chave recusou") == true && serial.Contains("metrica 1 0") && serial.Contains("ppp sem modem") && serial.Contains("ppp desconectado") && serial.Contains("fim"),
            serial);
        // ---- o RainMaker: o exemplo da lampada; a nuvem e o aplicativo sao a bancada ----
        const string rainmaker = """
            #include <WiFi.h>
            #include "RMaker.h"
            LightBulb *lampada;
            void aoEscrever(Device *device, Param *param, const param_val_t val, void *priv, write_ctx_t *ctx) {
              Serial.println(String(device->getDeviceName()) + " " + param->getParamName() + " " + esp_rmaker_device_cb_src_to_str(ctx->src) + " " + (val.val.b ? "ligou" : "desligou"));
              param->updateAndReport(val);
            }
            void aoEvento(void *arg, esp_event_base_t base, int32_t id, void *data) {
              if (base == RMAKER_EVENT && id == RMAKER_EVENT_STARTED) Serial.println("rainmaker iniciou");
            }
            void setup() {
              Serial.begin(115200);
              WiFi.begin("CircuitSketch-WiFi", "");
              while (WiFi.status() != WL_CONNECTED) delay(20);
              Node no = RMaker.initNode("Casa");
              lampada = new LightBulb("Luz");
              lampada->addCb(aoEscrever);
              lampada->addBrightnessParam(40);
              no.addDevice(*lampada);
              esp_event_handler_register(RMAKER_EVENT, ESP_EVENT_ANY_ID, &aoEvento, NULL);
              RMaker.enableOTA(OTA_USING_TOPICS);
              RMaker.enableSchedule();
              RMaker.start();
              Serial.println(no.getNodeInfo()->name);
              lampada->updateAndReportParam(ESP_RMAKER_DEF_BRIGHTNESS_NAME, 75);
              Serial.println("pronto");
            }
            void loop() { delay(10); }
            """;
        bool escreveu = false;
        (serial, erro, nativo, var placaRm) = RodarNativo(rainmaker, 4000, durante: (Maquina maq) =>
        {
            if (escreveu || !maq.RainMaker.Conectada || !maq.Serial.Any(l => l.Contains("pronto"))) return;
            escreveu = true;
            maq.RainMaker.Escrever("Luz", "Power", "true");
        });
        Conferir("arduino-esp32: RainMaker -- o no e a LightBulb do core; com a placa na rede o evento STARTED sai, e a escrita do aplicativo chega no callback com a origem Cloud",
            nativo && erro is null && serial?.Contains("Casa") == true && serial.Contains("rainmaker iniciou") && serial.Contains("Luz Power Cloud ligou"),
            $"{nativo} {erro} | {serial}");
        Conferir("arduino-esp32: RainMaker -- updateAndReport chega na nuvem da bancada (Brightness 75, Power true)",
            placaRm.RainMaker.Valor("Luz", "Brightness") == "75" && placaRm.RainMaker.Valor("Luz", "Power") == "true",
            $"Brightness {placaRm.RainMaker.Valor("Luz", "Brightness")} Power {placaRm.RainMaker.Valor("Luz", "Power")}");
    }
}
