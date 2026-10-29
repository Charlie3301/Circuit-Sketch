"""Copia cabecalhos (e codigo portavel) do core arduino-esp32 para nativo/arduino.

O C/C++ compilado ve os cabecalhos de verdade do core da ESP32 -- as mesmas
declaracoes, tipos e macros da placa. As funcoes que mexem no hardware sao as
do simulador (nativo/arduino/src/cs_hal*.cpp); o codigo que so faz conta
(cbuf, base64, MacAddress, o gerenciador de perifericos...) vem junto, como
esta no core.

Alguns arquivos mudam (sempre com o aviso "Modificado (CircuitSketch)" no
topo, e a mudanca descrita em MUDANCAS abaixo):
  - esp32-hal-gpio.h: os modos de pino e de interrupcao (INPUT, OUTPUT,
    RISING...) sao os numeros do simulador (cs_placa.h), e nao os da placa;
  - esp32-hal.h: o analogWrite, o analogWriteFrequency e o
    analogWriteResolution sao os do Arduino.h (com as duas formas, a do
    core 2.x e a do 3.x);
  - esp_arduino_version.h: a versao vem do cs_placa.h, se ele disser.

Uso: python copiar_core.py <pasta do arduino-esp32>

O arduino-esp32 e da Espressif Systems e colaboradores: o core sob a LGPL 2.1
e a Apache License 2.0 (cada arquivo diz a sua); a versao usada fica em
nativo/arduino/VERSAO-ARDUINO-ESP32.
"""
import os
import re
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
INCLUDE = os.path.join(REPO, 'nativo', 'arduino', 'include')
SRC = os.path.join(REPO, 'nativo', 'arduino', 'src', 'arduino-esp32')

# Os cabecalhos do core que vem como estao (ou com a mudanca de MUDANCAS).
CABECALHOS = [
    'esp32-hal.h', 'esp32-hal-log.h', 'esp32-hal-matrix.h', 'esp32-hal-uart.h', 'esp32-hal-gpio.h', 'esp32-hal-ldo.h',
    'esp32-hal-touch.h', 'esp32-hal-touch-ng.h', 'esp32-hal-dac.h', 'esp32-hal-adc.h', 'esp32-hal-spi.h',
    'esp32-hal-i2c.h', 'esp32-hal-i2c-slave.h', 'esp32-hal-ledc.h', 'esp32-hal-rmt.h', 'esp32-hal-sigmadelta.h',
    'esp32-hal-bt.h', 'esp32-hal-psram.h', 'esp32-hal-rgb-led.h', 'esp32-hal-cpu.h', 'esp32-hal-hosted.h',
    'esp32-hal-periman.h', 'esp32-hal-bt-mem.h', 'esp32-hal-alloc-ble-mem.h', 'esp32-hal-alloc-bt-classic-mem.h',
    'esp_arduino_version.h', 'extra_attr.h', 'esp8266-compat.h', 'io_pin_remap.h', 'wiring_private.h',
    'stdlib_noniso.h', 'HashBuilder.h', 'HEXBuilder.h', 'MD5Builder.h', 'base64.h', 'cbuf.h', 'StreamString.h',
    'MacAddress.h', 'StringUtils.h', 'ColorFormat.h', 'FunctionalInterrupt.h', 'freertos_stats.h',
    'chip-debug-report.h', 'HardwareI2C.h',
]
# O codigo portavel (so conta, sem hardware) que vem junto. O StreamString.cpp
# nao: ele mexe por dentro na String do core oficial, que nao e a do
# simulador (o src/cs_streamstring.cpp faz o mesmo pela API publica).
FONTES = [
    'esp32-hal-periman.c', 'cbuf.cpp', 'base64.cpp', 'MacAddress.cpp', 'HEXBuilder.cpp',
    'MD5Builder.cpp', 'HashBuilder.cpp', 'StringUtils.cpp', 'ColorFormat.c', 'FunctionalInterrupt.cpp',
    'libb64/cdecode.c', 'libb64/cencode.c',
]
CABECALHOS_EXTRA = ['libb64/cdecode.h', 'libb64/cencode.h']

# Das bibliotecas que vem com o core (libraries/<nome>/src): o cabecalho vai
# para include/, o codigo para src/arduino-esp32/.
DAS_BIBLIOTECAS = [
    'Network/src/NetworkEvents.h', 'Network/src/NetworkEvents.cpp',
    'WiFi/src/WiFiType.h',
    'Hash/src/HMACBuilder.h', 'Hash/src/HMACBuilder.cpp', 'Hash/src/PBKDF2_HMACBuilder.h', 'Hash/src/PBKDF2_HMACBuilder.cpp',
    'Hash/src/SHA1Builder.h', 'Hash/src/SHA1Builder.cpp', 'Hash/src/SHA2Builder.h', 'Hash/src/SHA2Builder.cpp',
    'Hash/src/SHA3Builder.h', 'Hash/src/SHA3Builder.cpp',
    'SD/src/sd_defines.h', 'SD/src/sd_diskio.h', 'ESP_I2S/src/wav_header.h', 'ESP32/src/dummy.h',
    'DNSServer/src/DNSServer.h', 'DNSServer/src/DNSServer.cpp',
    'ArduinoOTA/src/ArduinoOTA.h', 'ArduinoOTA/src/ArduinoOTA.cpp',
    'HTTPUpdate/src/HTTPUpdate.h', 'HTTPUpdate/src/HTTPUpdate.cpp', 'HTTPUpdateServer/src/HTTPUpdateServer.h',
    'Update/src/HttpsOTAUpdate.h', 'Update/src/Updater_Signing.h',
    'ESP_NOW/src/ESP32_NOW.h', 'ESP_NOW/src/ESP32_NOW.cpp', 'ESP_NOW/src/ESP32_NOW_Serial.h', 'ESP_NOW/src/ESP32_NOW_Serial.cpp',
]

AVISO = '/* Modificado (CircuitSketch): {0} */\n'

# (arquivo, trocas [(de, para)], o que mudou)
MUDANCAS = {
    'esp32-hal-gpio.h': (
        [(r'\n#define (LOW|HIGH|INPUT|OUTPUT|PULLUP|INPUT_PULLUP|PULLDOWN|INPUT_PULLDOWN|OPEN_DRAIN|OUTPUT_OPEN_DRAIN|ANALOG|'
          r'DISABLED|RISING|FALLING|CHANGE|ONLOW|ONHIGH|ONLOW_WE|ONHIGH_WE)\s+(\S+)',
          r'\n#ifndef \1\n#define \1 \2\n#endif')],
        'os modos de pino e de interrupcao so valem se o cs_placa.h (os numeros do simulador) nao os definiu.'),
    'esp32-hal.h': (
        [(r'void analogWrite\(uint8_t pin, int value\);\n', ''),
         (r'void analogWriteFrequency\(uint8_t pin, uint32_t freq\);\n', ''),
         (r'void analogWriteResolution\(uint8_t pin, uint8_t bits\);\n', '')],
        'o analogWrite, o analogWriteFrequency e o analogWriteResolution sao os do Arduino.h (as formas do core 2.x e do 3.x).'),
    'esp_arduino_version.h': (
        [(r'\n#define (ESP_ARDUINO_VERSION_MAJOR|ESP_ARDUINO_VERSION_MINOR|ESP_ARDUINO_VERSION_PATCH|ESP_ARDUINO_VERSION)\s+(.+)',
          r'\n#ifndef \1\n#define \1 \2\n#endif')],
        'a versao vem do cs_placa.h, se ele disser.'),
    'ESP32_NOW.cpp': (
        [(r'#if CONFIG_ESP_HOSTED_ENABLED\n', r'#include "soc/soc_caps.h"\n#if !SOC_WIFI_SUPPORTED\n#elif CONFIG_ESP_HOSTED_ENABLED\n')],
        'sem WiFi no chip (o ESP32-H2), o arquivo fica vazio.'),
    'ESP32_NOW_Serial.cpp': (
        [(r'#if CONFIG_ESP_HOSTED_ENABLED\n', r'#include "soc/soc_caps.h"\n#if !SOC_WIFI_SUPPORTED\n#elif CONFIG_ESP_HOSTED_ENABLED\n')],
        'sem WiFi no chip (o ESP32-H2), o arquivo fica vazio.'),
}


def copiar(raiz, rel, destino, base=('cores', 'esp32')):
    origem = os.path.join(raiz, *base, rel)
    texto = open(origem, encoding='utf-8').read()
    nome = os.path.basename(rel)
    if nome in MUDANCAS:
        trocas, motivo = MUDANCAS[nome]
        for de, para in trocas:
            novo = re.sub(de, para, texto)
            if novo == texto:
                raise SystemExit(f'{nome}: a mudanca "{de}" nao achou o que trocar (o arquivo mudou no core?)')
            texto = novo
        texto = AVISO.format(motivo) + texto
    alvo = os.path.join(destino, rel if base[0] == 'cores' else os.path.basename(rel))
    os.makedirs(os.path.dirname(alvo), exist_ok=True)
    with open(alvo, 'w', encoding='utf-8', newline='\n') as f:
        f.write(texto)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    raiz = sys.argv[1]
    for h in CABECALHOS + CABECALHOS_EXTRA:
        copiar(raiz, h, INCLUDE)
    for c in FONTES:
        copiar(raiz, c, SRC)
    # O libb64 inclui os seus cabecalhos pela pasta dele.
    for h in CABECALHOS_EXTRA:
        copiar(raiz, h, SRC)
    for b in DAS_BIBLIOTECAS:
        copiar(raiz, b, INCLUDE if b.endswith('.h') else SRC, base=('libraries',))
    with open(os.path.join(REPO, 'nativo', 'arduino', 'VERSAO-ARDUINO-ESP32'), 'w', encoding='utf-8') as f:
        texto = open(os.path.join(raiz, 'cores', 'esp32', 'esp_arduino_version.h'), encoding='utf-8').read()
        partes = [re.search(r'#define ESP_ARDUINO_VERSION_%s\s+(\d+)' % p, texto).group(1) for p in ('MAJOR', 'MINOR', 'PATCH')]
        f.write('.'.join(partes) + '\n')
    print(f'{len(CABECALHOS) + len(CABECALHOS_EXTRA)} cabecalhos e {len(FONTES)} fontes do core, {len(DAS_BIBLIOTECAS)} das bibliotecas')


if __name__ == '__main__':
    main()
