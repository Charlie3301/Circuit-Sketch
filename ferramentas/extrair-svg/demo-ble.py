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

for trecho in ['#define PINO_DHT 4', '#define PINO_R 5', '#define PINO_G 6', '#define PINO_B 7']:
    assert trecho in sketch, trecho

TEMPERATURA_24 = (24 - (-40)) / 120

c = Circuito('placa', 'esp32-c3-devkit', 330, 90)

# O LED RGB de catodo comum, um resistor por cor.
c.peca('rgb', 'rgb-led', 110, 110, Nome='lâmpada')
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
Lampada BLE: o celular controla a ESP32-C3 pelo Bluetooth LE.

A C3 so tem o BLE -- o Bluetooth classico do SerialBT e so da ESP32 --, e
e ele que os apps de celular de hoje usam. Dois jeitos de conversar:

- o UART (Nordic UART Service): mande "vermelho", "verde", "azul",
  "branco", "apagar" ou "temp", e a placa responde por notificacao;
- a temperatura do DHT22 na caracteristica padrao 0x2A6E: ligue as
  notificacoes e ela chega a cada 2 segundos, em centesimos de grau.

Aperte F5: a janela do celular abre sozinha. Em Dispositivos, conecte na
"ESP32-C3 Lampada"; a conversa (UART) ja abre, e o botao da conversa
troca para os servicos e as caracteristicas, como no nRF Connect.
''')
