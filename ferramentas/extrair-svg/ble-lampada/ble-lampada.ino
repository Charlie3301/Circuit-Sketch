// BLE lamp: the phone controls the ESP32-C3 over Bluetooth LE.
//
// The C3 only has BLE (classic Bluetooth, the one SerialBT uses, is only on
// the ESP32), and it is the way to talk to today's phone apps. Two ways to
// talk:
//
//   - over UART (the Nordic UART service, which every BLE terminal app
//     knows): send "red", "green", "blue", "white", "off" or "temp", and
//     the board answers with a notification;
//   - over the temperature characteristic (0x2A6E, the Bluetooth
//     standard): turn on notifications and the temperature arrives every
//     2 seconds, in hundredths of a degree.
//
// In the simulator, the phone is the phone window (Bluetooth panel > Open
// the phone). On the board, any BLE app: nRF Connect, "Serial Bluetooth
// Terminal" (BLE mode), LightBlue.

#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>
#include <DHT.h>

#define DHT_PIN 4
#define PIN_R 5
#define PIN_G 6
#define PIN_B 7

#define UART_SERVICE "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"
#define UART_RX      "6E400002-B5A3-F393-E0A9-E50E24DCCA9E"  // the phone writes here
#define UART_TX      "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"  // the board notifies here

DHT dht(DHT_PIN, DHT22);
BLECharacteristic *uartTx;
BLECharacteristic *temperature;
bool connected = false;
String command = "";
unsigned long lastReading = 0;

void color(int r, int g, int b) {
  digitalWrite(PIN_R, r);
  digitalWrite(PIN_G, g);
  digitalWrite(PIN_B, b);
}

void reply(String text) {
  if (!connected) return;
  uartTx->setValue(text + "\n");
  uartTx->notify();
}

class OnConnection : public BLEServerCallbacks {
  void onConnect(BLEServer *server) {
    connected = true;
    Serial.println("phone connected");
  }
  void onDisconnect(BLEServer *server) {
    connected = false;
    Serial.println("phone left");
    BLEDevice::startAdvertising();  // shows up for the phone again
  }
};

class OnWrite : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic *c) {
    // This runs in the Bluetooth task: only store it, and loop() handles it.
    command = c->getValue();
    command.trim();
    command.toLowerCase();
  }
};

void setup() {
  Serial.begin(115200);
  pinMode(PIN_R, OUTPUT);
  pinMode(PIN_G, OUTPUT);
  pinMode(PIN_B, OUTPUT);
  dht.begin();

  BLEDevice::init("ESP32-C3 Lamp");
  BLEServer *server = BLEDevice::createServer();
  server->setCallbacks(new OnConnection());

  // The UART: the text conversation.
  BLEService *uart = server->createService(UART_SERVICE);
  uartTx = uart->createCharacteristic(UART_TX, BLECharacteristic::PROPERTY_NOTIFY);
  uartTx->addDescriptor(new BLE2902());
  BLECharacteristic *uartRx = uart->createCharacteristic(UART_RX, BLECharacteristic::PROPERTY_WRITE);
  uartRx->setCallbacks(new OnWrite());
  uart->start();

  // The temperature, the standard Bluetooth way (service 0x181A).
  BLEService *environment = server->createService(BLEUUID((uint16_t)0x181A));
  temperature = environment->createCharacteristic(BLEUUID((uint16_t)0x2A6E),
                                                  BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
  temperature->addDescriptor(new BLE2902());
  environment->start();

  BLEAdvertising *advertising = BLEDevice::getAdvertising();
  advertising->addServiceUUID(UART_SERVICE);
  advertising->setScanResponse(true);
  BLEDevice::startAdvertising();
  Serial.println("advertising: look for \"ESP32-C3 Lamp\" on the phone");
}

void loop() {
  if (command.length() > 0) {
    String c = command;
    command = "";
    Serial.println("command: " + c);
    if (c == "red") { color(HIGH, LOW, LOW); reply("LED red"); }
    else if (c == "green") { color(LOW, HIGH, LOW); reply("LED green"); }
    else if (c == "blue") { color(LOW, LOW, HIGH); reply("LED blue"); }
    else if (c == "white") { color(HIGH, HIGH, HIGH); reply("LED white"); }
    else if (c == "off") { color(LOW, LOW, LOW); reply("LED off"); }
    else if (c == "temp") reply("temperature: " + String(dht.readTemperature(), 1) + " C");
    else reply("unknown '" + c + "'. Send: red, green, blue, white, off or temp");
  }

  // Every 2 s, the temperature on the standard characteristic: hundredths
  // of a degree in an int16, which is the 0x2A6E format.
  if (millis() - lastReading >= 2000) {
    lastReading = millis();
    int16_t hundredths = (int16_t)(dht.readTemperature() * 100);
    temperature->setValue((uint8_t *)&hundredths, 2);
    if (connected) temperature->notify();
  }
  delay(20);
}
