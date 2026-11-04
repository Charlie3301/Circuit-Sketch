"""Copia os cabecalhos do TinyUSB (o fork da Espressif, o que o core da ESP32
usa no USB da S2 e da S3) para nativo/tinyusb/include.

O codigo USB do core (USB.cpp, USBCDC.cpp, a biblioteca USB: USBHID,
USBHIDKeyboard, USBMIDI...) e o de verdade e inclui o TinyUSB; as funcoes
do TinyUSB que ele chama (tud_hid_n_report, tud_cdc_n_write...) sao as do
simulador (nativo/arduino/src/cs_tinyusb.cpp), que leva o que a placa manda
pelo USB para o computador da bancada. O tusb_config.h e o do core (o
componente arduino_tinyusb do esp32-arduino-lib-builder), com as opcoes do
sdkconfig.

Uso: python copiar_tinyusb.py <pasta do tinyusb> <tusb_config.h do arduino_tinyusb>

O TinyUSB e de Ha Thach e colaboradores, sob a licenca MIT.
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'tinyusb')
# O portable (os controladores de cada chip) e o typec ficam de fora: o "controlador" e o simulador.
FORA = ('portable', 'typec')


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    src = os.path.join(sys.argv[1], 'src')
    if not os.path.isfile(os.path.join(src, 'tusb.h')):
        raise SystemExit(f'{src}: nao e o src do TinyUSB')
    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    n = 0
    for pasta, dirs, arquivos in os.walk(src):
        rel = os.path.relpath(pasta, src)
        if rel.split(os.sep)[0] in FORA:
            dirs[:] = []
            continue
        for a in arquivos:
            if a.endswith('.h'):
                alvo = os.path.join(DESTINO, 'include', rel, a)
                os.makedirs(os.path.dirname(alvo), exist_ok=True)
                shutil.copyfile(os.path.join(pasta, a), alvo)
                n += 1
    shutil.copyfile(sys.argv[2], os.path.join(DESTINO, 'include', 'tusb_config.h'))
    shutil.copyfile(os.path.join(sys.argv[1], 'LICENSE'), os.path.join(DESTINO, 'LICENSE'))
    # A versao vem do nome da pasta (o "tinyusb-0.21.0.2" do arquivo da tag).
    versao = os.path.basename(os.path.normpath(sys.argv[1])).replace('tinyusb-', '')
    with open(os.path.join(DESTINO, 'VERSAO'), 'w', encoding='utf-8') as f:
        f.write(versao + ' (espressif/tinyusb)\n')
    print(f'TinyUSB: {n + 1} cabecalhos')


if __name__ == '__main__':
    main()
