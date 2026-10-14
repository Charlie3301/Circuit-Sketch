namespace Simulador.App;

/// <summary>
/// O BLE entre placas: uma ESP32 servidor e outra procurando, conectando,
/// lendo, escrevendo e recebendo notificacoes -- os exemplos BLE_server e
/// BLE_client do core 3.x, quase sem mudanca. E a ArduinoBLE (a da Nano
/// RP2040 Connect) dos dois lados.
/// </summary>
public static partial class Autoteste
{
    /// <summary>Roda o setup() e o loop() de uma placa numa thread, em tempo real, como o app faz.</summary>
    static Thread RodarPlacaEmThread(Maquina maq, Interpretador interp)
    {
        // Em tempo real, como no app: no virtual o delay() nao espera, e a
        // varredura de 5 s de uma placa acaba antes de a outra anunciar.
        maq.TempoReal = true;
        maq.IniciarRelogio();
        var t = new Thread(() =>
        {
            try
            {
                interp.Setup();
                while (!maq.Parar) interp.Loop();
            }
            catch (ReinicioDaPlaca r) { lock (maq.Trava) maq.Avisos.Add("REINICIOU: " + r.Message); }
            catch (ExecucaoInterrompida) { }
            catch (Exception e) { lock (maq.Trava) maq.Avisos.Add("ERRO: " + e.Message); }
        }, 256 * 1024 * 1024) { IsBackground = true };
        t.Start();
        return t;
    }

    static string SerialDe(Maquina maq) => string.Join(" | ", LinhasDoSerial(maq));

    const string ServidorBle = """
        #include <BLEDevice.h>
        #include <BLEServer.h>
        #include <BLE2902.h>
        #define SERVICE_UUID "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
        #define CHAR_UUID    "beb5483e-36e1-4688-b7f5-ea07361b26a8"
        BLECharacteristic *pCharacteristic;
        bool conectado = false;
        int contagem = 0;
        class Servidor : public BLEServerCallbacks {
          void onConnect(BLEServer *s) { conectado = true; Serial.println("cliente conectou"); }
          void onDisconnect(BLEServer *s) { conectado = false; Serial.println("cliente saiu"); }
        };
        class Escrita : public BLECharacteristicCallbacks {
          void onWrite(BLECharacteristic *c) { Serial.print("recebi: "); Serial.println(c->getValue()); }
        };
        void setup() {
          Serial.begin(115200);
          BLEDevice::init("Sensor");
          BLEServer *pServer = BLEDevice::createServer();
          pServer->setCallbacks(new Servidor());
          BLEService *pService = pServer->createService(SERVICE_UUID);
          pCharacteristic = pService->createCharacteristic(CHAR_UUID,
              BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_NOTIFY);
          pCharacteristic->addDescriptor(new BLE2902());
          pCharacteristic->setCallbacks(new Escrita());
          pCharacteristic->setValue("ola");
          pService->start();
          BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
          pAdvertising->addServiceUUID(SERVICE_UUID);
          BLEDevice::startAdvertising();
          Serial.println("anunciando");
        }
        void loop() {
          if (conectado) {
            contagem++;
            pCharacteristic->setValue(String(contagem));
            pCharacteristic->notify();
          }
          delay(300);
        }
        """;

    const string ClienteBle = """
        #include <BLEDevice.h>
        static BLEUUID serviceUUID("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
        static BLEUUID charUUID("beb5483e-36e1-4688-b7f5-ea07361b26a8");
        static boolean doConnect = false;
        static boolean connected = false;
        static BLERemoteCharacteristic *pRemoteCharacteristic;
        static BLEAdvertisedDevice *myDevice;

        static void notifyCallback(BLERemoteCharacteristic *pBLERemoteCharacteristic, uint8_t *pData, size_t length, bool isNotify) {
          Serial.print("notificou: ");
          Serial.write(pData, length);
          Serial.println();
        }

        class MyClientCallback : public BLEClientCallbacks {
          void onConnect(BLEClient *pclient) {}
          void onDisconnect(BLEClient *pclient) {
            connected = false;
            Serial.println("onDisconnect");
          }
        };

        bool connectToServer() {
          BLEClient *pClient = BLEDevice::createClient();
          pClient->setClientCallbacks(new MyClientCallback());
          pClient->connect(myDevice);
          BLERemoteService *pRemoteService = pClient->getService(serviceUUID);
          if (pRemoteService == nullptr) {
            pClient->disconnect();
            return false;
          }
          pRemoteCharacteristic = pRemoteService->getCharacteristic(charUUID);
          if (pRemoteCharacteristic == nullptr) {
            pClient->disconnect();
            return false;
          }
          if (pRemoteCharacteristic->canRead()) {
            String value = pRemoteCharacteristic->readValue();
            Serial.print("valor lido: ");
            Serial.println(value.c_str());
          }
          if (pRemoteCharacteristic->canNotify()) pRemoteCharacteristic->registerForNotify(notifyCallback);
          connected = true;
          return true;
        }

        class MyAdvertisedDeviceCallbacks : public BLEAdvertisedDeviceCallbacks {
          void onResult(BLEAdvertisedDevice advertisedDevice) {
            Serial.print("achei: ");
            Serial.println(advertisedDevice.toString().c_str());
            if (advertisedDevice.haveServiceUUID() && advertisedDevice.isAdvertisingService(serviceUUID)) {
              BLEDevice::getScan()->stop();
              myDevice = new BLEAdvertisedDevice(advertisedDevice);
              doConnect = true;
            }
          }
        };

        void setup() {
          Serial.begin(115200);
          BLEDevice::init("");
          BLEScan *pBLEScan = BLEDevice::getScan();
          pBLEScan->setAdvertisedDeviceCallbacks(new MyAdvertisedDeviceCallbacks());
          pBLEScan->setInterval(1349);
          pBLEScan->setWindow(449);
          pBLEScan->setActiveScan(true);
          pBLEScan->start(5, false);
        }

        void loop() {
          if (doConnect == true) {
            if (connectToServer()) Serial.println("conectado ao servidor");
            else Serial.println("nao conectou");
            doConnect = false;
          }
          if (connected) {
            String newValue = "tempo " + String(millis() / 1000);
            pRemoteCharacteristic->writeValue(newValue.c_str(), newValue.length());
          }
          delay(1000);
        }
        """;

    /// <summary>Uma placa com o sketch compilado como o app compila (o Fonte diz qual biblioteca BLE ele usa).</summary>
    static (Maquina Maq, Interpretador Interp) MontarPlacaCompilada(Rede rede, string nome, string codigo, string placa)
    {
        var maq = new Maquina { Perfil = PerfilPlaca.De(placa) };
        maq.Reiniciar();
        maq.Nome = nome;
        maq.Rede = rede;
        var prog = Compilador.CompilarProjeto(new Projeto { Sketch = codigo, Placa = placa, DoSimulador = Acervo.EhDoSimulador });
        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        return (maq, interp);
    }

    const string PerifericoLed = """
        #include <ArduinoBLE.h>
        BLEService ledService("19B10000-E8F2-537E-4F6C-D104768A1214");
        BLEByteCharacteristic switchCharacteristic("19B10001-E8F2-537E-4F6C-D104768A1214", BLERead | BLEWrite);
        const int ledPin = LED_BUILTIN;
        void setup() {
          Serial.begin(9600);
          pinMode(ledPin, OUTPUT);
          if (!BLE.begin()) {
            Serial.println("starting Bluetooth Low Energy module failed!");
            while (1);
          }
          BLE.setLocalName("LED");
          BLE.setAdvertisedService(ledService);
          ledService.addCharacteristic(switchCharacteristic);
          BLE.addService(ledService);
          switchCharacteristic.writeValue(0);
          BLE.advertise();
          Serial.println("BLE LED Peripheral");
        }
        void loop() {
          BLEDevice central = BLE.central();
          if (central) {
            Serial.print("Connected to central: ");
            Serial.println(central.address());
            while (central.connected()) {
              if (switchCharacteristic.written()) {
                if (switchCharacteristic.value()) {
                  Serial.println("LED on");
                  digitalWrite(ledPin, HIGH);
                } else {
                  Serial.println("LED off");
                  digitalWrite(ledPin, LOW);
                }
              }
            }
            Serial.print("Disconnected from central: ");
            Serial.println(central.address());
          }
        }
        """;

    const string CentralLed = """
        #include <ArduinoBLE.h>
        void setup() {
          Serial.begin(9600);
          BLE.begin();
          Serial.println("BLE Central - LED control");
          BLE.scanForUuid("19b10000-e8f2-537e-4f6c-d104768a1214");
        }
        void controlLed(BLEDevice peripheral) {
          Serial.println("Connecting ...");
          if (peripheral.connect()) {
            Serial.println("Connected");
          } else {
            Serial.println("Failed to connect!");
            return;
          }
          if (peripheral.discoverAttributes()) {
            Serial.println("Attributes discovered");
          } else {
            Serial.println("Attribute discovery failed!");
            peripheral.disconnect();
            return;
          }
          BLECharacteristic ledCharacteristic = peripheral.characteristic("19b10001-e8f2-537e-4f6c-d104768a1214");
          if (!ledCharacteristic) {
            Serial.println("Peripheral does not have LED characteristic!");
            peripheral.disconnect();
            return;
          } else if (!ledCharacteristic.canWrite()) {
            Serial.println("Peripheral does not have a writable LED characteristic!");
            peripheral.disconnect();
            return;
          }
          for (int vezes = 0; vezes < 2 && peripheral.connected(); vezes++) {
            ledCharacteristic.writeValue((byte)0x01);
            delay(400);
            ledCharacteristic.writeValue((byte)0x00);
            delay(400);
          }
          peripheral.disconnect();
          Serial.println("Peripheral disconnected");
        }
        void loop() {
          BLEDevice peripheral = BLE.available();
          if (peripheral) {
            Serial.print("Found ");
            Serial.print(peripheral.address());
            Serial.print(" '");
            Serial.print(peripheral.localName());
            Serial.print("' ");
            Serial.print(peripheral.advertisedServiceUuid());
            Serial.println();
            if (peripheral.localName() != "LED") return;
            BLE.stopScan();
            controlLed(peripheral);
            delay(100000);
          }
        }
        """;

    const string PerifericoNano = """
        #include <ArduinoBLE.h>
        BLEService servico("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
        BLEStringCharacteristic texto("beb5483e-36e1-4688-b7f5-ea07361b26a8", BLERead | BLEWrite | BLENotify, 20);
        int contagem = 0;
        unsigned long ultimo = 0;
        void escreveu(BLEDevice central, BLECharacteristic c) {
          Serial.print("recebi: ");
          Serial.println(texto.value());
        }
        void conectou(BLEDevice central) {
          Serial.print("conectou: ");
          Serial.println(central.address());
        }
        void setup() {
          Serial.begin(115200);
          BLE.begin();
          BLE.setLocalName("Nano");
          BLE.setAdvertisedService(servico);
          servico.addCharacteristic(texto);
          BLE.addService(servico);
          texto.writeValue("ola");
          texto.setEventHandler(BLEWritten, escreveu);
          BLE.setEventHandler(BLEConnected, conectou);
          BLE.advertise();
        }
        void loop() {
          BLE.poll();
          // Notifica so depois de alguem assinar: antes disso, o valor fica o "ola".
          if (texto.subscribed() && millis() - ultimo > 300) {
            ultimo = millis();
            contagem++;
            texto.writeValue(String(contagem));
          }
        }
        """;

    static void Ble_ArduinoBle()
    {
        saida.AppendLine("ArduinoBLE (Nano RP2040 Connect)");

        // ---- o exemplo LED: uma Nano periferico, a outra central ----
        {
            var rede = new Rede { UsarInternet = false };
            var (periferico, interpPeriferico) = MontarPlacaCompilada(rede, "Periferico", PerifericoLed, "nano-rp2040-connect");
            var (central, interpCentral) = MontarPlacaCompilada(rede, "Central", CentralLed, "nano-rp2040-connect");
            var t1 = RodarPlacaEmThread(periferico, interpPeriferico);
            var t2 = RodarPlacaEmThread(central, interpCentral);
            int led = periferico.Perfil.LedInterno;

            bool achou = EsperarPor(() => LinhasDoSerial(central).Any(l => l.StartsWith("Found ") && l.Contains("'LED'")), 6000);
            bool acendeu = EsperarPor(() => LinhasDoSerial(periferico).Contains("LED on"), 5000);
            bool pinoAceso = acendeu && periferico.Eventos.Any(e => e.Pino == led && e.Valor == 1);
            bool saiu = EsperarPor(() => LinhasDoSerial(periferico).Any(l => l.StartsWith("Disconnected from central: ")), 6000);
            string doPeriferico = SerialDe(periferico), doCentral = SerialDe(central);
            periferico.Parar = central.Parar = true;
            t1.Join(3000);
            t2.Join(3000);
            string avisos;
            lock (central.Trava) avisos = string.Join(" / ", central.Avisos.Concat(periferico.Avisos));

            saida.AppendLine($"        periferico: {doPeriferico}");
            saida.AppendLine($"        central: {doCentral}");
            if (avisos.Length > 0) saida.AppendLine($"        avisos: {avisos}");
            Conferir("arduinoble: a central acha o periferico pelo UUID, com o nome e o servico anunciados",
                achou && doCentral.Contains("'LED' 19b10000-e8f2-537e-4f6c-d104768a1214"), doCentral);
            Conferir("arduinoble: conecta, descobre os atributos e acha a caracteristica gravavel",
                doCentral.Contains("Connected") && doCentral.Contains("Attributes discovered") && !doCentral.Contains("does not have"), doCentral);
            Conferir("arduinoble: o BLE.central() do periferico ve a central, e o written()/value() recebem o byte",
                doPeriferico.Contains("Connected to central: ") && acendeu && doPeriferico.Contains("LED off"), doPeriferico);
            Conferir("arduinoble: o LED_BUILTIN acende pelo BLE", pinoAceso, $"pino {led}");
            Conferir("arduinoble: o disconnect() da central sai do while (central.connected()) do periferico -- uma vez so",
                saiu && doPeriferico.Split(" | ").Count(l => l.StartsWith("Connected to central")) == 1, doPeriferico);
            Conferir("arduinoble: sem erro no caminho", !avisos.Contains("ERRO"), avisos);
        }

        // ---- a Nano periferico, uma ESP32 cliente (a biblioteca BLE do core) ----
        {
            var rede = new Rede { UsarInternet = false };
            var (nano, interpNano) = MontarPlacaCompilada(rede, "Nano", PerifericoNano, "nano-rp2040-connect");
            var (esp, interpEsp) = MontarPlacaCompilada(rede, "ESP32", ClienteBle, "esp32-devkit-v1");
            var t1 = RodarPlacaEmThread(nano, interpNano);
            var t2 = RodarPlacaEmThread(esp, interpEsp);

            bool conectou = EsperarPor(() => LinhasDoSerial(esp).Contains("conectado ao servidor"), 8000);
            bool notificou = EsperarPor(() => LinhasDoSerial(esp).Count(l => l.StartsWith("notificou: ")) >= 2, 5000);
            bool recebeu = EsperarPor(() => LinhasDoSerial(nano).Any(l => l.StartsWith("recebi: tempo ")), 5000);
            string doNano = SerialDe(nano), daEsp = SerialDe(esp);
            nano.Parar = esp.Parar = true;
            t1.Join(3000);
            t2.Join(3000);

            saida.AppendLine($"        nano: {doNano}");
            saida.AppendLine($"        esp32: {(daEsp.Length > 400 ? daEsp[..400] + "..." : daEsp)}");
            Conferir("arduinoble + esp32: a ESP32 acha a Nano, conecta e le o valor (\"ola\")",
                conectou && daEsp.Contains("achei: Name: Nano") && daEsp.Contains("valor lido: ola"), daEsp);
            Conferir("arduinoble + esp32: o BLEConnected da Nano roda com o endereco da ESP32",
                doNano.Contains("conectou: 24:0a:c4:"), doNano);
            Conferir("arduinoble + esp32: o writeValue() da Nano notifica quem assinou (a ESP32)", notificou, daEsp);
            Conferir("arduinoble + esp32: o que a ESP32 escreve chega no BLEWritten da Nano", recebeu, doNano);
        }

        // ---- o celular no periferico da ArduinoBLE ----
        {
            var rede = new Rede { UsarInternet = false };
            var (periferico, interpPeriferico) = MontarPlacaCompilada(rede, "Periferico", PerifericoLed, "nano-rp2040-connect");
            var t1 = RodarPlacaEmThread(periferico, interpPeriferico);
            RadioBle radio = null;
            EsperarPor(() => (radio = rede.AnunciosBle().FirstOrDefault(r => r.Nome == "LED")) is not null, 4000);
            bool conectou = radio is not null && rede.ConectarBleDoCelular(radio);
            var caracteristica = radio?.Visiveis().FirstOrDefault(c => c.Uuid == "19b10001-e8f2-537e-4f6c-d104768a1214");
            EsperarPor(() => LinhasDoSerial(periferico).Any(l => l.StartsWith("Connected to central: ")), 3000);
            bool escreveu = caracteristica is not null && rede.EscreverDoCelular(caracteristica, [1]);
            bool acendeu = EsperarPor(() => LinhasDoSerial(periferico).Contains("LED on"), 3000);
            rede.DesconectarBleDoCelular();
            bool saiu = EsperarPor(() => LinhasDoSerial(periferico).Any(l => l.StartsWith("Disconnected from central: 5c:e9:1e")), 3000);
            periferico.Parar = true;
            t1.Join(3000);
            Conferir("arduinoble + celular: o celular acha a Nano, conecta, escreve 1 e o LED acende; ao sair, o periferico ve",
                conectou && escreveu && acendeu && saiu, SerialDe(periferico));
        }
    }

    static void Ble_PlacaComPlaca()
    {
        saida.AppendLine("BLE entre placas");

        var rede = new Rede { UsarInternet = false };
        var (servidor, interpServidor) = MontarPlaca(rede, "Servidor", ServidorBle);
        var (cliente, interpCliente) = MontarPlaca(rede, "Cliente", ClienteBle);
        var t1 = RodarPlacaEmThread(servidor, interpServidor);
        var t2 = RodarPlacaEmThread(cliente, interpCliente);

        bool conectou = EsperarPor(() => LinhasDoSerial(cliente).Contains("conectado ao servidor"), 8000);
        bool notificou = EsperarPor(() => LinhasDoSerial(cliente).Count(l => l.StartsWith("notificou: ")) >= 2, 5000);
        bool recebeu = EsperarPor(() => LinhasDoSerial(servidor).Any(l => l.StartsWith("recebi: tempo ")), 5000);
        string doCliente = SerialDe(cliente), doServidor = SerialDe(servidor);

        // O servidor sai do ar (reiniciou, parou): o cliente fica sabendo.
        servidor.Parar = true;
        t1.Join(3000);
        rede.EsquecerBle(servidor);
        bool caiu = EsperarPor(() => LinhasDoSerial(cliente).Contains("onDisconnect"), 4000);

        cliente.Parar = true;
        t2.Join(3000);
        string avisos;
        lock (cliente.Trava) avisos = string.Join(" / ", cliente.Avisos.Concat(servidor.Avisos));

        saida.AppendLine($"        cliente: {doCliente}");
        saida.AppendLine($"        servidor: {doServidor}");
        if (avisos.Length > 0) saida.AppendLine($"        avisos: {avisos}");

        Conferir("ble entre placas: a varredura acha o servidor, com o nome, o servico e o rssi (o toString do core)",
            doCliente.Contains("achei: Name: Sensor, Address: ") && doCliente.Contains("serviceUUID: 4fafc201-1fb5-459e-8fcc-c5c9c331914b")
            && doCliente.Contains(", rssi: -"), doCliente);
        Conferir("ble entre placas: o cliente conecta, e o onConnect do servidor roda",
            conectou && doServidor.Contains("cliente conectou"), $"{doCliente} // {doServidor}");
        Conferir("ble entre placas: o readValue() le o valor do servidor (\"ola\")",
            doCliente.Contains("valor lido: ola"), doCliente);
        Conferir("ble entre placas: o notify() do servidor chega no callback do registerForNotify()",
            notificou, doCliente);
        Conferir("ble entre placas: o writeValue() do cliente chega no onWrite() do servidor",
            recebeu, doServidor);
        Conferir("ble entre placas: o servidor saindo do ar chama o onDisconnect() do cliente",
            caiu, SerialDe(cliente));
        Conferir("ble entre placas: nada de erro no caminho",
            !avisos.Contains("ERRO"), avisos);

        // ---- o core 3.x: o start() devolve ponteiro ----
        var rede2 = new Rede { UsarInternet = false };
        var (velho, interpVelho) = MontarPlaca(rede2, "Velho", """
            #include <BLEDevice.h>
            void setup() {
              BLEDevice::init("");
              BLEScan *pBLEScan = BLEDevice::getScan();
              BLEScanResults foundDevices = pBLEScan->start(1, false);
            }
            void loop() {}
            """);
        string erroDoCore2 = null;
        try { interpVelho.Setup(); }
        catch (ErroDeExecucao e) { erroDoCore2 = e.Message; }
        Conferir("ble: o \"BLEScanResults x = start()\" do core 2.x da o erro do compilador do 3.x",
            erroDoCore2?.Contains("BLEScanResults*") == true, erroDoCore2 ?? "rodou");
        velho.Parar = true;
    }
}
