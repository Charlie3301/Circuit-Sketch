namespace Simulador.App;

/// <summary>Os metodos das classes da placa que so existiam no arduino-esp32 de verdade.</summary>
public static partial class Autoteste
{
    static void Cpp_Api()
    {
        saida.AppendLine("C++ compilado: os metodos das classes do arduino-esp32");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("api: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        const string eeprom = """
            #include <EEPROM.h>
            void setup() {
              Serial.begin(115200);
              EEPROM.begin(64);
              Serial.print(EEPROM.writeInt(0, -7));
              Serial.print(EEPROM.writeFloat(4, 2.5f));
              Serial.print(EEPROM.writeBool(8, true));
              Serial.print(EEPROM.writeLong64(9, 1LL << 40));
              Serial.print(EEPROM.writeUShort(17, 513));
              EEPROM.commit();
              Serial.print('|');
              Serial.print(EEPROM.readInt(0));
              Serial.print(' ');
              Serial.print(EEPROM.readFloat(4), 1);
              Serial.print(' ');
              Serial.print(EEPROM.readBool(8));
              Serial.print(' ');
              Serial.print(EEPROM.readLong64(9) == (1LL << 40));
              Serial.print(' ');
              Serial.print(EEPROM.readUShort(17));
              Serial.print('|');
              // Fora da EEPROM: nada escrito, e o read devolve 0.
              Serial.print(EEPROM.writeInt(62, 1));
              Serial.print(EEPROM.readInt(62));
              Serial.print('|');
              uint8_t b[3] = {1, 2, 3}, l[3] = {0, 0, 0};
              Serial.print(EEPROM.writeBytes(30, b, 3));
              Serial.print(EEPROM.readBytes(30, l, 3));
              Serial.print(l[2]);
              Serial.print('|');
              // Os bytes em memoria so valem depois do commit().
              uint8_t *p = EEPROM.getDataPtr();
              p[40] = 99;
              Serial.print(EEPROM.isDirty());
              EEPROM.commit();
              Serial.print(EEPROM.read(40));
              Serial.print(EEPROM.isDirty());
              Serial.println();
            }
            void loop() { delay(100); }
            """;
        var (serial, erro, nativo, _) = RodarNativo(eeprom, 300);
        Conferir("api: a EEPROM le e escreve cada tipo, com a faixa e o getDataPtr da biblioteca",
            nativo && erro is null && serial == "44182|-7 2.5 1 1 513|00|333|1990", $"{nativo} {erro} | {serial}");

        const string classes = """
            #include <WiFi.h>
            #include <SPI.h>
            #include <Wire.h>
            #include <SD.h>
            #include <SD_MMC.h>
            #include <HTTPClient.h>
            #include <ESP_I2S.h>
            #include <AsyncUDP.h>
            #include <lwip/ip_addr.h>
            bool vazio(const File &f) { return !f && f.size() == 0 && f.position() == 0; }
            void setup() {
              Serial.begin(115200);
              WiFiServer servidor(80);
              Serial.print((bool)servidor);
              servidor.begin();
              Serial.print((bool)servidor);
              servidor.setNoDelay(true);
              Serial.print(servidor.getNoDelay());
              servidor.stop();
              Serial.print((bool)servidor);
              Serial.print('|');
              WiFiClient cliente;
              int opcao = 5;
              Serial.print(cliente.getOption(1, &opcao));
              Serial.print(opcao);
              Serial.print(cliente.setSocketOption(6, 1, &opcao, sizeof(opcao)));
              Serial.print('|');
              Serial.print(WiFi.STA.getInactiveTime());
              WiFi.STA.setInactiveTime(9);
              Serial.print(WiFi.STA.getInactiveTime());
              Serial.print(WiFi.globalIPv6() == IPAddress(IPv6));
              Serial.print(WiFi.STA.linkLocalIPv6() == IPAddress(IPv6));
              Serial.print('|');
              SPI.begin();
              SPI.setClockDivider(8);
              Serial.print(SPI.getClockDivider());
              Serial.print(Wire.getBusNum());
              Serial.print(Wire1.getBusNum());
              Serial.print('|');
              File f;
              Serial.print(vazio(f));
              Serial.print(strlen(f.path()));
              Serial.print(f.getNextFileName().length());
              Serial.print(f.seekDir(0));
              Serial.print('|');
              I2SClass i2s;
              Serial.print(i2s.setPort(I2S_NUM_1));
              Serial.print((int)i2s.getPort());
              Serial.print('|');
              Serial.print(SD_MMC.sectorSize());
              Serial.print(SD_MMC.setPowerChannel(1));
              Serial.println();
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, _) = RodarNativo(classes, 300);
        Conferir("api: servidor, opcoes de socket, IPv6, SPI, Wire, File, I2S e SD_MMC como no arduino-esp32",
            nativo && erro is null && serial == "0110|000|6911|801|1000|11|01", $"{nativo} {erro} | {serial}");

        const string ble = """
            #include <BLEDevice.h>
            #include <BLEServer.h>
            #include <BLEUtils.h>
            #include <BLE2902.h>
            #include <BLESecurity.h>
            class Seguranca : public BLESecurityCallbacks {
              uint32_t onPassKeyRequest() override { return 42; }
            };
            void setup() {
              Serial.begin(115200);
              Serial.print(BLEDevice::getInitialized());
              BLEDevice::init("Bancada");
              Serial.print(BLEDevice::getInitialized());
              Serial.print(BLEDevice::getDeviceName());
              Serial.print(BLEDevice::getBLEStackString());
              Serial.print('|');
              BLEServer *servidor = BLEDevice::createServer();
              Serial.print(BLEDevice::getServer() == servidor);
              BLEService *servico = servidor->createService("180f");
              Serial.print(servico->getServer() == servidor);
              BLECharacteristic *c = servico->createCharacteristic("2a19", BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
              Serial.print(c->getProperties());
              BLE2902 *d = new BLE2902();
              c->addDescriptor(d);
              Serial.print(d->getCharacteristic() == c);
              Serial.print('|');
              servico->start();
              BLEAdvertising *a = BLEDevice::getAdvertising();
              Serial.print(a->isAdvertising());
              a->start();
              Serial.print(a->isAdvertising());
              a->stop();
              Serial.print(a->isAdvertising());
              Serial.print('|');
              Serial.print(BLEUUID::fromString("0000180f-0000-1000-8000-00805f9b34fb").to16().toString());
              BLEAddress e("aa:bb:cc:dd:ee:ff");
              Serial.print(e.getNative()[5]);
              e.setType(1);
              Serial.print(e.getType());
              Serial.print('|');
              BLEDevice::whiteListAdd(e);
              Serial.print(BLEDevice::onWhiteList(e));
              BLEDevice::whiteListRemove(e);
              Serial.print(BLEDevice::onWhiteList(e));
              Serial.print('|');
              BLESecurity seguranca;
              seguranca.setCapability(0);
              BLESecurity::setPassKey(true, 654321);
              Serial.print(BLESecurity::getPassKey());
              BLEDevice::setSecurityCallbacks(new Seguranca());
              Serial.print('|');
              BLEAdvertisementData dados;
              dados.setName("ab");
              Serial.print(dados.getPayload().length());
              Serial.print('|');
              Serial.print(BLEUtils::characteristicPropertiesToString(0x12));
              Serial.println();
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, _) = RodarNativo(ble, 400);
        Conferir("api: a BLE tem os metodos da biblioteca (BLEDevice, BLESecurity, BLEUUID, BLEAddress, BLEUtils...)",
            nativo && erro is null && serial == "01BancadaBluedroid|11" + (1 | 4) + "1|010|180f2551|10|654321|4|broadcast: 0, read: 1, write_nr: 0, write: 0, notify: 1, indicate: 0, auth: 0",
            $"{nativo} {erro} | {serial}");

        // ---- a NimBLE-Arduino: as duas pontas, pelo radio da bancada ----
        const string servidorNimble = """
            #include <NimBLEDevice.h>
            NimBLECharacteristic *k;
            class Servidor : public NimBLEServerCallbacks {
              void onConnect(NimBLEServer *s, NimBLEConnInfo &info) override { Serial.println("conectou"); }
            };
            class Escrita : public NimBLECharacteristicCallbacks {
              void onWrite(NimBLECharacteristic *c, NimBLEConnInfo &info) override {
                Serial.println(("escreveu " + std::string(c->getValue())).c_str());
                c->notify(std::string("eco"));
              }
            };
            void setup() {
              Serial.begin(115200);
              NimBLEDevice::init("nim-servidor");
              NimBLEServer *s = NimBLEDevice::createServer();
              s->setCallbacks(new Servidor());
              NimBLEService *sv = s->createService("181A");
              k = sv->createCharacteristic("2A6E", NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::NOTIFY);
              k->setCallbacks(new Escrita());
              k->setValue(std::string("ola"));
              NimBLEAdvertising *a = NimBLEDevice::getAdvertising();
              a->setName("nim-servidor");
              a->addServiceUUID(sv->getUUID());
              a->start();
              Serial.println(a->isAdvertising() ? "anunciando" : "parado");
            }
            void loop() { delay(10); }
            """;
        const string clienteNimble = """
            #include <NimBLEDevice.h>
            const NimBLEAdvertisedDevice *alvo = nullptr;
            class Busca : public NimBLEScanCallbacks {
              void onResult(const NimBLEAdvertisedDevice *d) override {
                if (d->isAdvertisingService(NimBLEUUID("181A"))) alvo = d;
              }
            };
            void setup() {
              Serial.begin(115200);
              delay(500);
              NimBLEDevice::init("nim-cliente");
              NimBLEScan *scan = NimBLEDevice::getScan();
              scan->setScanCallbacks(new Busca());
              scan->setActiveScan(true);
              scan->start(1500);
              while (scan->isScanning()) delay(50);
              if (!alvo) { Serial.println("nao achou"); return; }
              Serial.println(("achou " + alvo->getName()).c_str());
              NimBLEClient *c = NimBLEDevice::createClient();
              if (!c->connect(alvo)) { Serial.println("nao conectou"); return; }
              NimBLERemoteCharacteristic *k = c->getService("181A")->getCharacteristic("2A6E");
              Serial.println(("leu " + std::string(k->readValue())).c_str());
              k->subscribe(true, [](NimBLERemoteCharacteristic *, uint8_t *d, size_t n, bool) {
                Serial.println(("notificou " + std::string((char *)d, n)).c_str());
              });
              k->writeValue(std::string("oi"), true);
              delay(300);
              Serial.println("fim");
            }
            void loop() { delay(10); }
            """;
        var (serA, serB, erroNim) = RodarDuasNativas(servidorNimble, clienteNimble, "esp32-devkit-v1", 6000, (a, b) => b.Contains("fim"));
        Conferir("api: a NimBLE-Arduino -- o servidor anuncia, o cliente acha pelo scan assincrono, conecta, le, escreve e recebe o notify",
            erroNim is null && serA?.Contains("anunciando") == true && serB?.Contains("achou nim-servidor") == true && serB.Contains("leu ola")
            && serA.Contains("conectou") && serA.Contains("escreveu oi") && serB.Contains("notificou eco"), $"{erroNim} | {serA} || {serB}");

        const string lwip = """
            #include <WiFi.h>
            #include <lwip/ip_addr.h>
            void setup() {
              Serial.begin(115200);
              ip_addr_t cru;
              IPAddress(192, 168, 4, 2).to_ip_addr_t(&cru);
              IPAddress de_volta;
              de_volta.from_ip_addr_t(&cru);
              Serial.print(de_volta.toString());
              Serial.print('|');
              IPAddress(IPv6, (const uint8_t *)"\xfe\x80\0\0\0\0\0\0\0\0\0\0\0\0\0\1").to_ip_addr_t(&cru);
              de_volta.from_ip_addr_t(&cru);
              Serial.print(de_volta.type() == IPv6);
              Serial.print(de_volta.addr_type());
              Serial.println();
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, _) = RodarNativo(lwip, 200);
        Conferir("api: o IPAddress vai e volta pelo ip_addr_t do lwIP, nos dois tipos",
            nativo && erro is null && serial == "192.168.4.2|12", $"{nativo} {erro} | {serial}");
    }
}
