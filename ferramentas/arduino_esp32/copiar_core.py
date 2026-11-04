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
    # O USB (S2 e S3): o codigo do core sobre o TinyUSB (as funcoes tud_* sao as do simulador, src/cs_tinyusb.cpp).
    'USB.h', 'USBCDC.h', 'USBMSC.h', 'FirmwareMSC.h', 'firmware_msc_fat.h', 'esp32-hal-tinyusb.h', 'HWCDC.h', 'USBHost.h',
]
# O codigo portavel (so conta, sem hardware) que vem junto. O StreamString.cpp
# nao: ele mexe por dentro na String do core oficial, que nao e a do
# simulador (o src/cs_streamstring.cpp faz o mesmo pela API publica).
FONTES = [
    'esp32-hal-periman.c', 'cbuf.cpp', 'base64.cpp', 'MacAddress.cpp', 'HEXBuilder.cpp',
    'MD5Builder.cpp', 'HashBuilder.cpp', 'StringUtils.cpp', 'ColorFormat.c', 'FunctionalInterrupt.cpp',
    'libb64/cdecode.c', 'libb64/cencode.c',
    'USB.cpp', 'USBCDC.cpp', 'USBMSC.cpp', 'FirmwareMSC.cpp', 'firmware_msc_fat.c',
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
    'WebServer/src/WebServer.h', 'WebServer/src/WebServer.cpp', 'WebServer/src/Parsing.cpp', 'WebServer/src/HTTP_Method.h',
    'WebServer/src/Uri.h', 'WebServer/src/Middlewares.h', 'WebServer/src/uri/UriBraces.h', 'WebServer/src/uri/UriGlob.h',
    'WebServer/src/uri/UriRegex.h', 'WebServer/src/detail/RequestHandler.h', 'WebServer/src/detail/RequestHandlersImpl.h',
    'WebServer/src/detail/mimetable.h', 'WebServer/src/detail/mimetable.cpp', 'WebServer/src/middleware/Middleware.h',
    'WebServer/src/middleware/MiddlewareChain.cpp', 'WebServer/src/middleware/AuthenticationMiddleware.cpp',
    'WebServer/src/middleware/CorsMiddleware.cpp', 'WebServer/src/middleware/LoggingMiddleware.cpp',
    'NetworkClientSecure/src/NetworkClientSecure.h', 'NetworkClientSecure/src/ssl_client.h',
    'NetworkClientSecure/src/WiFiClientSecure.h',
    'BLE/src/RTOS.h', 'BLE/src/FreeRTOS.cpp', 'BLE/src/GeneralUtils.h', 'BLE/src/GeneralUtils.cpp', 'BLE/src/BLEExceptions.h',
    'BLE/src/BLEExceptions.cpp', 'BLE/src/HIDTypes.h', 'BLE/src/HIDKeyboardTypes.h', 'BLE/src/BLEValue.h', 'BLE/src/BLEValue.cpp',
    'BluetoothSerial/src/BTAddress.h', 'BluetoothSerial/src/BTAddress.cpp', 'BluetoothSerial/src/BTAdvertisedDevice.h',
    'BluetoothSerial/src/BTAdvertisedDeviceSet.cpp', 'BluetoothSerial/src/BTScan.h', 'BluetoothSerial/src/BTScanResultsSet.cpp',
    'BLE/src/BLEBeacon.h', 'BLE/src/BLEBeacon.cpp', 'BLE/src/BLEEddystoneTLM.h', 'BLE/src/BLEEddystoneTLM.cpp',
    'BLE/src/BLEEddystoneURL.h', 'BLE/src/BLEEddystoneURL.cpp', 'BLE/src/BLEHIDDevice.h', 'BLE/src/BLEHIDDevice.cpp',    # O RainMaker (o esp_rmaker por baixo e o src/cs_rainmaker.cpp; a nuvem e o motor/RainMaker.cs).
    'RainMaker/src/RMaker.h', 'RainMaker/src/RMaker.cpp', 'RainMaker/src/RMakerDevice.h', 'RainMaker/src/RMakerDevice.cpp',
    'RainMaker/src/RMakerNode.h', 'RainMaker/src/RMakerNode.cpp', 'RainMaker/src/RMakerParam.h', 'RainMaker/src/RMakerParam.cpp',
    'RainMaker/src/RMakerQR.h', 'RainMaker/src/RMakerQR.cpp', 'RainMaker/src/RMakerType.h', 'RainMaker/src/RMakerType.cpp',
    'RainMaker/src/RMakerUtils.h', 'RainMaker/src/RMakerUtils.cpp',
    'Ethernet/src/ETH.h', 'Console/src/Console.h', 'Console/src/Console.cpp',
    'ESP_HostedOTA/src/ESP_HostedOTA.h', 'ESP_HostedOTA/src/ESP_HostedOTA.cpp', 'ESP_Video/src/ESP_Video.h', 'ESP_Video/src/ESP_Video.cpp',
    'Insights/src/Insights.h', 'RainMaker/src/AppInsights.h', 'PPP/src/PPP.h', 'WiFiProv/src/WiFiProv.h',
    'ESP_SR/src/ESP_SR.h', 'ESP_SR/src/ESP_SR.cpp', 'ESP_SR/src/esp32-hal-sr.h', 'USB/src/USBMSCFS.h',
    # A biblioteca USB (o USBHost* so os cabecalhos: o host USB precisa do driver do IDF; o
    # USBMSCFS, do FatFS, fica de fora).
    'USB/src/USBAudioCard.cpp', 'USB/src/USBAudioCard.h', 'USB/src/USBAudioCardDescriptors.h',
    'USB/src/USBHID.cpp', 'USB/src/USBHID.h', 'USB/src/USBHIDConsumerControl.cpp',
    'USB/src/USBHIDConsumerControl.h', 'USB/src/USBHIDGamepad.cpp', 'USB/src/USBHIDGamepad.h',
    'USB/src/USBHIDKeyboard.cpp', 'USB/src/USBHIDKeyboard.h', 'USB/src/USBHIDMouse.cpp',
    'USB/src/USBHIDMouse.h', 'USB/src/USBHIDReportMapParse.c', 'USB/src/USBHIDReportMapParse.h',
    'USB/src/USBHIDSystemControl.cpp', 'USB/src/USBHIDSystemControl.h', 'USB/src/USBHIDVendor.cpp',
    'USB/src/USBHIDVendor.h', 'USB/src/USBHostHID.h', 'USB/src/USBHostHIDGamepad.h',
    'USB/src/USBHostHIDKeyboard.h', 'USB/src/USBHostHIDKeyboardDecode.h', 'USB/src/USBHostHIDMouse.h',
    'USB/src/USBHostHIDReportMapDump.h', 'USB/src/USBHostMSC.h', 'USB/src/USBHostSerial.h',
    'USB/src/USBMIDI.cpp', 'USB/src/USBMIDI.h', 'USB/src/USBVendor.cpp', 'USB/src/USBVendor.h',
    'USB/src/keyboardLayout/KeyboardLayout.h', 'USB/src/keyboardLayout/KeyboardLayout_da_DK.cpp', 'USB/src/keyboardLayout/KeyboardLayout_de_DE.cpp',
    'USB/src/keyboardLayout/KeyboardLayout_en_US.cpp', 'USB/src/keyboardLayout/KeyboardLayout_es_ES.cpp', 'USB/src/keyboardLayout/KeyboardLayout_fr_CH.cpp',
    'USB/src/keyboardLayout/KeyboardLayout_fr_FR.cpp', 'USB/src/keyboardLayout/KeyboardLayout_hu_HU.cpp', 'USB/src/keyboardLayout/KeyboardLayout_it_IT.cpp',
    'USB/src/keyboardLayout/KeyboardLayout_ja_JP.cpp', 'USB/src/keyboardLayout/KeyboardLayout_pt_BR.cpp', 'USB/src/keyboardLayout/KeyboardLayout_pt_PT.cpp',
    'USB/src/keyboardLayout/KeyboardLayout_sv_SE.cpp', 'USB/src/keyboardLayout/Keyboard_da_DK.h', 'USB/src/keyboardLayout/Keyboard_de_DE.h',
    'USB/src/keyboardLayout/Keyboard_es_ES.h', 'USB/src/keyboardLayout/Keyboard_fr_CH.h', 'USB/src/keyboardLayout/Keyboard_fr_FR.h',
    'USB/src/keyboardLayout/Keyboard_hu_HU.h', 'USB/src/keyboardLayout/Keyboard_it_IT.h', 'USB/src/keyboardLayout/Keyboard_ja_JP.h',
    'USB/src/keyboardLayout/Keyboard_pt_BR.h', 'USB/src/keyboardLayout/Keyboard_pt_PT.h', 'USB/src/keyboardLayout/Keyboard_sv_SE.h',
]
# O que vai para outro lugar: o WebServer.h e o WiFiClientSecure.h de verdade
# ficam atras dos do include/, que os escolhem nas placas com o core da ESP32
# (as outras usam os do simulador).
RENOMEAR = {'WebServer/src/WebServer.h': 'arduino-esp32/WebServer.h',
            'NetworkClientSecure/src/WiFiClientSecure.h': 'arduino-esp32/WiFiClientSecure.h'}
# Os cabecalhos que o codigo inclui pela pasta dele vao tambem para o src.
TAMBEM_NO_SRC = ['WebServer/src/detail/mimetable.h', 'WebServer/src/middleware/Middleware.h', 'USB/src/keyboardLayout/KeyboardLayout.h']

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
    'WebServer.cpp': (
        [(r'\(p = index\(decoded, \':\'\)\)', r"(p = strchr(decoded, ':'))")],
        'o index() do POSIX (o strings.h da newlib) e o strchr, que o Windows tem.'),
    'UriRegex.h': (
        [(r' \| std::regex_constants::__polynomial', '')],
        'o __polynomial e do libstdc++ (o GCC da placa); o libc++ do simulador nao tem.'),
    'ssl_client.h': (
        [(r'#ifndef ARD_SSL_H\n', r'#include "IPAddress.h"\n#ifndef ARD_SSL_H\n')],
        'o IPAddress vinha pelo Arduino.h do core; o do simulador nao o inclui.'),
    'RequestHandler.h': (
        [(r'#ifndef REQUESTHANDLER_H\n', r'#include "WebServer.h"\n#ifndef REQUESTHANDLER_H\n')],
        'incluido sozinho (o WebServer.h inclui este no meio dele): traz o WebServer antes.'),
    'WiFiProv.h': (
        [(r'#if SOC_WIFI_SUPPORTED && CONFIG_NETWORK_PROV_NETWORK_TYPE_WIFI', r'#if SOC_WIFI_SUPPORTED')],
        'a configuracao do provisionamento nao esta no sdkconfig copiado; vale em todo chip com WiFi.'),
    'ESP_Video.h': (
        [(r'#if \(CONFIG_ESP_VIDEO_ENABLE_MIPI_CSI_VIDEO_DEVICE \|\| CONFIG_ESP_VIDEO_ENABLE_DVP_VIDEO_DEVICE\)', r'#if 0')],
        'a camera (V4L2, o componente esp_video) nao existe no simulador: o cabecalho fica sem a classe.'),
    'ESP_Video.cpp': (
        [(r'#if \(CONFIG_ESP_VIDEO_ENABLE_MIPI_CSI_VIDEO_DEVICE \|\| CONFIG_ESP_VIDEO_ENABLE_DVP_VIDEO_DEVICE\)', r'#if 0')],
        'a camera (V4L2, o componente esp_video) nao existe no simulador.'),
    'FreeRTOS.cpp': (
        [(r'\(uint32_t\)m_semaphore\)', '(uint32_t)(uintptr_t)m_semaphore)')],
        'o ponteiro tem 64 bits no computador: vira inteiro pelo uintptr_t (o endereco mostrado e o mesmo pedaco baixo).'),
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
    alvo = os.path.join(destino, rel if base[0] == 'cores' else RENOMEAR.get(rel, rel.split('/src/', 1)[1]))
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
    for b in TAMBEM_NO_SRC:
        copiar(raiz, b, SRC, base=('libraries',))
    with open(os.path.join(REPO, 'nativo', 'arduino', 'VERSAO-ARDUINO-ESP32'), 'w', encoding='utf-8') as f:
        texto = open(os.path.join(raiz, 'cores', 'esp32', 'esp_arduino_version.h'), encoding='utf-8').read()
        partes = [re.search(r'#define ESP_ARDUINO_VERSION_%s\s+(\d+)' % p, texto).group(1) for p in ('MAJOR', 'MINOR', 'PATCH')]
        f.write('.'.join(partes) + '\n')
    print(f'{len(CABECALHOS) + len(CABECALHOS_EXTRA)} cabecalhos e {len(FONTES)} fontes do core, {len(DAS_BIBLIOTECAS)} das bibliotecas')


if __name__ == '__main__':
    main()
