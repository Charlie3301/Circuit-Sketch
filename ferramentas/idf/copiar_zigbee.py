"""Copia a API do Zigbee da Espressif (os cabecalhos de esp-zigbee-lib e de
esp-zboss-lib, versao 1.6) e a biblioteca Zigbee do core para nativo/zigbee.

A pilha de verdade (a ZBOSS) e binaria e so roda no chip, entao nao vem: so os
cabecalhos, que sao abertos. As funcoes esp_zb_* sao as do simulador
(src/cs_zigbee.cpp), e a rede e entre as placas da bancada (motor/RedeZigbee.cs).
O codigo do core (ZigbeeCore, ZigbeeEP e os dispositivos em ep/) e o de verdade,
e vira uma biblioteca compilada so quando o sketch usa Zigbee.

Uso: python copiar_zigbee.py <pasta com esp-zigbee-lib e esp-zboss-lib> <pasta do arduino-esp32> <pasta do esp-idf>

O esp-zigbee-lib e da Espressif (Apache 2.0); os cabecalhos da ZBOSS sao da DSR
Corporation e da Espressif (licenca propria, ver LICENSE-zboss); a biblioteca do
core e Apache 2.0.
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'zigbee')


# A ZBOSS confere o tamanho das estruturas para um chip de 32 bits; no simulador
# os ponteiros tem 64.
MUDANCAS = {
    'zb_debug.h': ('#ifdef __cplusplus\n#define ZB_ASSERT_COMPILE_DECL(expr) __extension__ static_assert',
                   '#if defined(CS_NATIVO)\n#define ZB_ASSERT_COMPILE_DECL(expr)\n#elif defined(__cplusplus)\n#define ZB_ASSERT_COMPILE_DECL(expr) __extension__ static_assert'),
}


def copiar_arvore(origem, destino, extensoes):
    n = 0
    for pasta, _, arquivos in os.walk(origem):
        for a in arquivos:
            if a.endswith(extensoes):
                alvo = os.path.join(destino, os.path.relpath(pasta, origem), a)
                os.makedirs(os.path.dirname(alvo), exist_ok=True)
                shutil.copyfile(os.path.join(pasta, a), alvo)
                n += 1
    return n


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)
    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    inc = os.path.join(DESTINO, 'include')
    n = copiar_arvore(os.path.join(sys.argv[1], 'esp-zigbee-lib', 'include'), inc, ('.h',))
    n += copiar_arvore(os.path.join(sys.argv[1], 'esp-zboss-lib', 'include'), inc, ('.h',))
    shutil.copyfile(os.path.join(sys.argv[1], 'esp-zigbee-lib', 'LICENSE'), os.path.join(DESTINO, 'LICENSE-esp-zigbee-lib'))
    shutil.copyfile(os.path.join(sys.argv[1], 'esp-zboss-lib', 'LICENSE'), os.path.join(DESTINO, 'LICENSE-zboss'))
    # A biblioteca do core: os .h valem para o sketch, os .cpp entram na biblioteca.
    lib = os.path.join(sys.argv[2], 'libraries', 'Zigbee', 'src')
    m = copiar_arvore(lib, inc, ('.h',))
    # Os tipos do radio, que a API inclui (componente ieee802154 do IDF).
    shutil.copyfile(os.path.join(sys.argv[3], 'components', 'ieee802154', 'include', 'esp_ieee802154_types.h'), os.path.join(inc, 'esp_ieee802154_types.h'))
    fontes = copiar_arvore(lib, os.path.join(DESTINO, 'src', 'arduino'), ('.cpp',))
    if os.path.isfile(os.path.join(AQUI, 'cs_zigbee.cpp')):
        shutil.copyfile(os.path.join(AQUI, 'cs_zigbee.cpp'), os.path.join(DESTINO, 'src', 'cs_zigbee.cpp'))
    for nome, (antes, depois) in MUDANCAS.items():
        caminho = os.path.join(inc, nome)
        with open(caminho, encoding='utf-8', newline='') as f:
            texto = f.read()
        fim = '\r\n' if '\r\n' in texto else '\n'
        if antes.replace('\n', fim) not in texto:
            raise SystemExit(f'{nome}: o trecho a mudar nao esta mais la')
        with open(caminho, 'w', encoding='utf-8', newline='') as f:
            f.write(texto.replace(antes.replace('\n', fim), depois.replace('\n', fim)))
    print(f'Zigbee: {n} cabecalhos da API, {m} da biblioteca e {fontes} fontes')


if __name__ == '__main__':
    main()
