using System.IO;
namespace Simulador.App;

/// <summary>
/// O BLE da ESP32 (a biblioteca BLE do core 3.x): a placa como servidor
/// GATT, e o teste fazendo o papel do celular -- procura, conecta, le,
/// escreve, assina as notificacoes. Os sketches seguem os exemplos do
/// proprio core (Notify, UART, Server), e as regras de cada pilha
/// (Bluedroid na ESP32, NimBLE nas outras) sao as do codigo dele.
/// </summary>
public static partial class Autoteste
{
    static void Ble_ComoNoCore()
    {
        saida.AppendLine("BLE como no core");

        // O Rodar confere os #include antes de tudo: sem isto, todo sketch de
        // BLE abria o aviso de "falta biblioteca" (foi o passeio que pegou).
        var semDono = Acervo.SemDono("""
            #include <BLEDevice.h>
            #include <BLEServer.h>
            #include <BLEUtils.h>
            #include <BLE2902.h>
            #include <BLE2901.h>
            void setup() {}
            void loop() {}
            """);
        Conferir("ble: os cabecalhos da BLE sao conhecidos (sem o aviso de biblioteca faltando)",
            semDono.Count == 0, string.Join(", ", semDono));

        // ---- o Notify, na ESP32 (Bluedroid) ----
        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "ESP32-ble", """
                #include <BLEDevice.h>
                #include <BLEServer.h>
                #include <BLEUtils.h>
                #include <BLE2902.h>
                BLEServer *pServer = NULL;
                BLECharacteristic *pCharacteristic = NULL;
                bool deviceConnected = false;
                bool oldDeviceConnected = false;
                uint32_t value = 0;
                #define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
                #define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"
                class MyServerCallbacks : public BLEServerCallbacks {
                  void onConnect(BLEServer *pServer) { deviceConnected = true; Serial.println("conectou"); }
                  void onDisconnect(BLEServer *pServer) { deviceConnected = false; Serial.println("saiu"); }
                };
                void setup() {
                  Serial.begin(115200);
                  BLEDevice::init("ESP32");
                  pServer = BLEDevice::createServer();
                  pServer->setCallbacks(new MyServerCallbacks());
                  BLEService *pService = pServer->createService(SERVICE_UUID);
                  pCharacteristic = pService->createCharacteristic(CHARACTERISTIC_UUID,
                      BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE |
                      BLECharacteristic::PROPERTY_NOTIFY | BLECharacteristic::PROPERTY_INDICATE);
                  pCharacteristic->addDescriptor(new BLE2902());
                  pService->start();
                  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
                  pAdvertising->addServiceUUID(SERVICE_UUID);
                  pAdvertising->setScanResponse(false);
                  pAdvertising->setMinPreferred(0x0);
                  BLEDevice::startAdvertising();
                }
                void loop() {
                  if (deviceConnected) {
                    pCharacteristic->setValue((uint8_t *)&value, 4);
                    pCharacteristic->notify();
                    value++;
                    delay(500);
                  }
                  if (!deviceConnected && oldDeviceConnected) {
                    delay(500);
                    pServer->startAdvertising();
                    Serial.println("start advertising");
                    oldDeviceConnected = deviceConnected;
                  }
                  if (deviceConnected && !oldDeviceConnected) oldDeviceConnected = deviceConnected;
                }
                """);
            var chegaram = new List<byte[]>();
            rede.NotificouOCelular += (_, _, dados) => { lock (chegaram) chegaram.Add(dados); };

            interp.Setup();
            var anuncio = rede.AnunciosBle().FirstOrDefault();
            saida.AppendLine($"        anuncio: {anuncio?.Nome} {anuncio?.Endereco} "
                             + string.Join(",", anuncio?.UuidsAnunciados ?? []));
            Conferir("ble: a placa anuncia o nome e o servico",
                anuncio is { Nome: "ESP32" } && anuncio.UuidsAnunciados.Contains("4fafc201-1fb5-459e-8fcc-c5c9c331914b"),
                anuncio?.Nome ?? "nada anunciando");
            Conferir("ble: a ESP32 usa o Bluedroid, com o MAC do Bluetooth (o da placa + 2)",
                anuncio is { NimBle: false } && anuncio.Endereco.Split(':').Length == 6, anuncio?.Endereco);

            rede.ConectarBleDoCelular(anuncio);
            interp.Loop();
            var car = anuncio.Visiveis().Single();
            Conferir("ble: o celular conecta, o onConnect roda e o anuncio para",
                LinhasDoSerial(maq).Contains("conectou") && !anuncio.Anunciando, string.Join("|", LinhasDoSerial(maq)));

            interp.Loop();
            interp.Loop();
            Conferir("ble: no Bluedroid, com o 2902 e sem assinar, o notify() nao chega",
                chegaram.Count == 0, $"{chegaram.Count} chegaram");

            rede.AssinarDoCelular(car, true);
            for (int i = 0; i < 3; i++) interp.Loop();
            var valores = chegaram.Select(d => d.Length == 4 ? BitConverter.ToUInt32(d) : 999).ToList();
            saida.AppendLine($"        notificacoes: {string.Join(" ", valores)}");
            Conferir("ble: assinado, chegam os 4 bytes do uint32_t (little-endian), contando",
                valores.Count == 3 && valores[1] == valores[0] + 1 && valores[2] == valores[0] + 2,
                string.Join(" ", valores));

            byte[] lido = rede.LerDoCelular(car);
            Conferir("ble: o celular le o valor atual", lido is { Length: 4 }, lido is null ? "nulo" : Convert.ToHexString(lido));

            rede.DesconectarBleDoCelular();
            interp.Loop();
            interp.Loop();
            Conferir("ble: o celular sai, o onDisconnect roda e o sketch volta a anunciar",
                LinhasDoSerial(maq).Contains("saiu") && LinhasDoSerial(maq).Contains("start advertising")
                && rede.AnunciosBle().Contains(anuncio), string.Join("|", LinhasDoSerial(maq)));
        }

        // ---- o UART, na ESP32-S3 (NimBLE) ----
        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "S3-ble", """
                #include <BLEDevice.h>
                #include <BLEServer.h>
                #include <BLEUtils.h>
                #include <BLE2902.h>
                BLEServer *pServer = NULL;
                BLECharacteristic *pTxCharacteristic;
                bool deviceConnected = false;
                uint8_t txValue = 65;
                #define SERVICE_UUID           "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"
                #define CHARACTERISTIC_UUID_RX "6E400002-B5A3-F393-E0A9-E50E24DCCA9E"
                #define CHARACTERISTIC_UUID_TX "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"
                class MyServerCallbacks : public BLEServerCallbacks {
                  void onConnect(BLEServer *pServer) { deviceConnected = true; }
                  void onDisconnect(BLEServer *pServer) { deviceConnected = false; }
                };
                class MyCallbacks : public BLECharacteristicCallbacks {
                  void onWrite(BLECharacteristic *pCharacteristic) {
                    String rxValue = pCharacteristic->getValue();
                    if (rxValue.length() > 0) {
                      Serial.print("Received Value: ");
                      for (int i = 0; i < rxValue.length(); i++) Serial.print(rxValue[i]);
                      Serial.println();
                    }
                  }
                  void onWrite(BLECharacteristic *c, esp_ble_gatts_cb_param_t *param) {
                    Serial.println("nunca no NimBLE");
                  }
                };
                void setup() {
                  Serial.begin(115200);
                  BLEDevice::init("UART Service");
                  pServer = BLEDevice::createServer();
                  pServer->setCallbacks(new MyServerCallbacks());
                  BLEService *pService = pServer->createService(SERVICE_UUID);
                  pTxCharacteristic = pService->createCharacteristic(CHARACTERISTIC_UUID_TX, BLECharacteristic::PROPERTY_NOTIFY);
                  pTxCharacteristic->addDescriptor(new BLE2902());
                  BLECharacteristic *pRxCharacteristic = pService->createCharacteristic(CHARACTERISTIC_UUID_RX, BLECharacteristic::PROPERTY_WRITE);
                  pRxCharacteristic->setCallbacks(new MyCallbacks());
                  pService->start();
                  pServer->getAdvertising()->start();
                }
                void loop() {
                  if (deviceConnected) {
                    pTxCharacteristic->setValue(&txValue, 1);
                    pTxCharacteristic->notify();
                    txValue++;
                    delay(1000);
                  }
                }
                """, PerfilPlaca.De("esp32-s3-devkit"));
            var chegaram = new List<byte[]>();
            rede.NotificouOCelular += (_, _, dados) => { lock (chegaram) chegaram.Add(dados); };

            interp.Setup();
            var radio = rede.AnunciosBle().FirstOrDefault();
            Conferir("ble: a S3 usa o NimBLE", radio is { NimBle: true }, radio?.Nome ?? "nada anunciando");

            rede.ConectarBleDoCelular(radio);
            var rx = radio.Visiveis().First(c => c.Uuid == Ble.UartRx);
            var tx = radio.Visiveis().First(c => c.Uuid == Ble.UartTx);
            Conferir("ble: os UUID em maiusculas viram os do toString() (minusculas)",
                rx.Escreve && tx.Notifica && !tx.Le, $"{rx.Uuid} {tx.Uuid}");

            interp.Loop();
            interp.Loop();
            Conferir("ble: no NimBLE, sem assinar, o notify() nao chega (nem com o 2902)",
                chegaram.Count == 0, $"{chegaram.Count} chegaram");

            rede.AssinarDoCelular(tx, true);
            interp.Loop();
            interp.Loop();
            Conferir("ble: assinado, a notificacao chega com o byte do setValue(&txValue, 1)",
                chegaram.Count >= 1 && chegaram[0].Length == 1, string.Join(" ", chegaram.Select(Convert.ToHexString)));

            bool escreveu = rede.EscreverDoCelular(rx, System.Text.Encoding.UTF8.GetBytes("ola"));
            interp.Loop();
            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        serial: {string.Join(" | ", linhas)}");
            Conferir("ble: o que o celular escreve chega no onWrite (getValue() e String)",
                escreveu && linhas.Contains("Received Value: ola"), string.Join("|", linhas));
            Conferir("ble: o onWrite do Bluedroid (esp_ble_gatts_cb_param_t) nao roda no NimBLE, e o simulador avisa",
                !linhas.Contains("nunca no NimBLE") && maq.Avisos.Any(a => a.Contains("NimBLE")), string.Join(" / ", maq.Avisos));
            Conferir("ble: escrever numa caracteristica so de notificacao e recusado",
                !rede.EscreverDoCelular(tx, [1]), "aceitou");
        }

        // ---- o Server: ler e escrever o valor ----
        {
            var rede = new Rede { UsarInternet = false };
            var (_, interp) = MontarPlaca(rede, "ESP32-srv", """
                #include <BLEDevice.h>
                #include <BLEServer.h>
                void setup() {
                  if (!BLEDevice::init("BLE Server Example")) return;
                  BLEServer *pServer = BLEDevice::createServer();
                  BLEService *pService = pServer->createService("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
                  pServer->advertiseOnDisconnect(true);
                  BLECharacteristic *c = pService->createCharacteristic("beb5483e-36e1-4688-b7f5-ea07361b26a8",
                      BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE);
                  c->setValue("Hello World says Neil");
                  pService->start();
                  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
                  pAdvertising->addServiceUUID("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
                  BLEDevice::startAdvertising();
                }
                void loop() { delay(2000); }
                """);
            interp.Setup();
            var radio = rede.AnunciosBle().Single();
            rede.ConectarBleDoCelular(radio);
            var car = radio.Visiveis().Single();
            string texto = System.Text.Encoding.UTF8.GetString(rede.LerDoCelular(car) ?? []);
            Conferir("ble: o celular le o texto do setValue()", texto == "Hello World says Neil", texto);
            rede.EscreverDoCelular(car, System.Text.Encoding.UTF8.GetBytes("novo"));
            Conferir("ble: e escreve por cima", System.Text.Encoding.UTF8.GetString(rede.LerDoCelular(car)) == "novo", "");
            rede.DesconectarBleDoCelular();
            Conferir("ble: com advertiseOnDisconnect(true), volta a anunciar sozinho",
                rede.AnunciosBle().Contains(radio), "nao voltou");
        }

        // ---- a demo da Lampada BLE, na janela, com o celular ----
        {
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrirDemo("esp32c3-ble-lampada");
                janela.TesteRodar();
                var placa = janela.TesteEditorCircuito.Atual.Pecas.First(p => p.Tipo == "esp32-c3-devkit");
                var maq = janela.TesteMaquinaDa(placa.Id);
                var rede = maq.Rede;
                bool anunciou = EsperarPor(() => rede.AnunciosBle().Any(r => r.Nome == "ESP32-C3 Lampada"), 6000);
                Conferir("demo ble: a C3 anuncia a \"ESP32-C3 Lampada\"", anunciou,
                    string.Join(", ", rede.AnunciosBle().Select(r => r.Nome)));

                var celular = janela.TesteAbrirCelular();
                EsperarPor(() => false, 600);
                Conferir("demo ble: o celular abre e mostra a placa em Dispositivos",
                    celular is { IsLoaded: true }, "nao abriu");

                var notificacoes = new List<(string Uuid, byte[] Dados)>();
                rede.NotificouOCelular += (_, car, dados) => { lock (notificacoes) notificacoes.Add((car.Uuid, dados)); };
                var radio = rede.AnunciosBle().First();
                rede.ConectarBleDoCelular(radio);
                EsperarPor(() => LinhasDoSerial(maq).Contains("celular conectou"), 3000);

                var tx = radio.Visiveis().First(c => c.Uuid == Ble.UartTx);
                var rx = radio.Visiveis().First(c => c.Uuid == Ble.UartRx);
                var temp = radio.Visiveis().First(c => Ble.Curto(c.Uuid) == 0x2A6E);
                rede.AssinarDoCelular(tx, true);
                rede.AssinarDoCelular(temp, true);
                rede.EscreverDoCelular(rx, System.Text.Encoding.UTF8.GetBytes("Vermelho\n"));
                bool respondeu = EsperarPor(() =>
                {
                    lock (notificacoes)
                        return notificacoes.Any(n => n.Uuid == Ble.UartTx
                                                     && System.Text.Encoding.UTF8.GetString(n.Dados) == "LED vermelho\n");
                }, 4000);
                Conferir("demo ble: \"Vermelho\" pelo UART acende o vermelho e a placa responde por notificacao",
                    respondeu && maq.Digital[5] == 1 && maq.Digital[6] == 0,
                    $"R={maq.Digital[5]} G={maq.Digital[6]} B={maq.Digital[7]} / {string.Join(" | ", LinhasDoSerial(maq))}");

                bool mediu = EsperarPor(() =>
                {
                    lock (notificacoes) return notificacoes.Any(n => Ble.Curto(n.Uuid) == 0x2A6E);
                }, 4000);
                byte[] centesimos;
                lock (notificacoes) centesimos = notificacoes.FirstOrDefault(n => Ble.Curto(n.Uuid) == 0x2A6E).Dados;
                int graus = centesimos is { Length: 2 } ? BitConverter.ToInt16(centesimos) : -1;
                saida.AppendLine($"        temperatura notificada: {graus} centesimos");
                Conferir("demo ble: a 0x2A6E notifica os centesimos de grau num int16 (24 C = 2400)",
                    mediu && graus == 2400, graus.ToString());

                // A janela: a chave de notificacao de cada cartao mostra o que o
                // celular assinou de verdade (o modo conversa assina o TX sozinho,
                // depois de os cartoes ja estarem montados).
                celular?.MostrarPagina("servicos");
                var chaveTx = celular?.TesteChaveDe(tx);
                var chaveTemp = celular?.TesteChaveDe(temp);
                Conferir("celular: a chave de notificacao do cartao segue a assinatura de verdade",
                    chaveTx?.IsChecked == true && chaveTemp?.IsChecked == true,
                    $"TX {chaveTx?.IsChecked} / temperatura {chaveTemp?.IsChecked}");
                if (chaveTx is not null) chaveTx.IsChecked = false;
                bool desligou = !tx.Assinada;
                if (chaveTx is not null) chaveTx.IsChecked = true;
                Conferir("celular: desligar e religar a chave escreve no CCCD", desligou && tx.Assinada,
                    $"desligou {desligou}, assinada {tx.Assinada}");

                string linha = Ble.Mostrar(System.Text.Encoding.UTF8.GetBytes("LED vermelho\r\n"));
                Conferir("celular: o fim de linha do valor aparece escrito, numa linha so",
                    linha.StartsWith("\"LED vermelho\\r\\n\"") && !linha.Contains('\n'), linha);
                string grau = Ble.Mostrar([0x32, 0x35, 0xC2, 0xB0, 0x43]);   // "25 graus C" em UTF-8
                Conferir("celular: texto com acento aparece como texto, e nao so em hexa",
                    grau.StartsWith("\"25") && grau.EndsWith("C\"  0x3235C2B043"), grau);

                FotografarJanela(celular, Path.Combine(Path.GetTempPath(), "circuitsketch-teste-celular.png"));
                celular?.Close();
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }

        // ---- onde nao tem BLE ----
        {
            const string sketch = """
                #include <BLEDevice.h>
                void setup() { BLEDevice::init("x"); }
                void loop() {}
                """;
            var (naS2, erroS2) = RodarProjeto(sketch, "esp32-s2-devkit");
            var (noUno, erroUno) = RodarProjeto(sketch, "arduino-uno");
            Conferir("ble: a ESP32-S2 nao tem Bluetooth", erroS2 is not null && erroS2.Contains("S2"), erroS2 ?? naS2);
            Conferir("ble: o Uno tambem nao", erroUno is not null && erroUno.Contains("ESP32"), erroUno ?? noUno);
        }
    }
}
