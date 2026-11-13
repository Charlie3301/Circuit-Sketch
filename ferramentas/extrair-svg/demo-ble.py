"""python ferramentas/extrair-svg/demo-ble.py

Lampada BLE (Alpha 19): a ESP32-C3, que so tem o Bluetooth LE, conversando
com o celular -- o UART (Nordic UART Service) para os comandos em texto, e
a temperatura do DHT22 na caracteristica padrao 0x2A6E, com notificacao.
No simulador, o celular e a janela do celular.

O sketch mora em ble-lampada/ble-lampada.ino, como arquivo de verdade:
compila na Arduino IDE para a ESP32-C3.
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demos_comum import *   # noqa: E402,F403

AQUI = os.path.dirname(os.path.abspath(__file__))
sketch = io.open(os.path.join(AQUI, 'ble-lampada', 'ble-lampada.ino'), encoding='utf-8').read()

for trecho in ['#define DHT_PIN 4', '#define PIN_R 5', '#define PIN_G 6', '#define PIN_B 7']:
    assert trecho in sketch, trecho

TEMPERATURA_24 = (24 - (-40)) / 120

c = Circuito('placa', 'esp32-c3-devkit', 330, 90)

# O LED RGB de catodo comum, um resistor por cor.
c.peca('rgb', 'rgb-led', 110, 110, Nome='lamp')
for cor, pino, y, fio_cor in [('R', '5', 150, VERMELHO), ('G', '6', 175, VERDE), ('B', '7', 200, AZUL)]:
    c.peca('r' + cor.lower(), 'resistor', 190, y, Ohms=220)
    c.ligar('placa', pino, 'r' + cor.lower(), '2', fio_cor)
    c.ligar('r' + cor.lower(), '1', 'rgb', cor, fio_cor)
c.ligar('rgb', 'COM', 'placa', 'GND', PRETO)

# O sensor de temperatura.
c.peca('dht', 'dht22', 520, 110, Giro=TEMPERATURA_24)
c.ligar('dht', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('dht', 'SDA', 'placa', '4', AMARELO)
c.ligar('dht', 'GND', 'placa', 'GND.2', PRETO)

gravar('esp32c3-ble-lampada', sketch, c, '''
BLE lamp: the phone controls the ESP32-C3 over Bluetooth LE.

The C3 only has BLE -- classic Bluetooth, the one SerialBT uses, is only on
the ESP32 -- and it is what today's phone apps use. Two ways to talk:

- the UART (Nordic UART Service): send "red", "green", "blue", "white",
  "off" or "temp", and the board answers with a notification;
- the DHT22 temperature on the standard 0x2A6E characteristic: turn on
  notifications and it arrives every 2 seconds, in hundredths of a degree.

Press F5: the phone window opens by itself. Under Devices, connect to
"ESP32-C3 Lamp"; the conversation (UART) opens right away, and the
conversation button switches to the services and characteristics, like
in nRF Connect.
''')
