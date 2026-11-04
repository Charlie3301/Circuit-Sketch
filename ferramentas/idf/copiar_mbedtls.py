"""Copia o mbedTLS do ESP-IDF (o submodulo components/mbedtls/mbedtls, o fork
da Espressif) para nativo/mbedtls.

O C/C++ compilado das placas ESP32 usa a biblioteca de verdade: os mesmos
cabecalhos (mbedtls/*.h, psa/*.h) e o mesmo codigo (library/*.c), com a
configuracao do IDF (port/include/mbedtls/esp_config.h). O que na placa e o
acelerador de hardware (o AES, o SHA, o RSA do chip) fica com a versao em
software do proprio mbedTLS -- ver nativo/arduino/include/cs_mbedtls_config.h.

Uso: python copiar_mbedtls.py <pasta do esp-idf>

O mbedTLS e da Mbed TLS Contributors, sob a Apache License 2.0 (ou a GPL 2.0
ou posterior, a escolha); o esp_config.h e da Espressif, sob a Apache 2.0.
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'mbedtls')


def copiar_pasta(origem, destino, extensoes):
    n = 0
    for nome in sorted(os.listdir(origem)):
        if nome.endswith(extensoes):
            shutil.copyfile(os.path.join(origem, nome), os.path.join(destino, nome))
            n += 1
    return n


# O net_sockets da biblioteca fala com o Windows; o do IDF (port/net_sockets.c), com o lwIP.
def trocar_net_sockets(idf, destino):
    shutil.copyfile(os.path.join(idf, 'components', 'mbedtls', 'port', 'net_sockets.c'), os.path.join(destino, 'library', 'net_sockets.c'))


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    comp = os.path.join(sys.argv[1], 'components', 'mbedtls')
    mbed = os.path.join(comp, 'mbedtls')
    if not os.path.isfile(os.path.join(mbed, 'include', 'mbedtls', 'build_info.h')):
        raise SystemExit(f'{mbed}: o submodulo do mbedTLS esta vazio (baixe o espressif/mbedtls na versao do IDF)')
    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    n = 0
    for sub in ('mbedtls', 'psa'):
        os.makedirs(os.path.join(DESTINO, 'include', sub))
        n += copiar_pasta(os.path.join(mbed, 'include', sub), os.path.join(DESTINO, 'include', sub), ('.h',))
    shutil.copyfile(os.path.join(comp, 'port', 'include', 'mbedtls', 'esp_config.h'),
                    os.path.join(DESTINO, 'include', 'mbedtls', 'esp_config.h'))
    shutil.copyfile(os.path.join(comp, 'port', 'include', 'esp_mem.h'), os.path.join(DESTINO, 'include', 'esp_mem.h'))
    os.makedirs(os.path.join(DESTINO, 'library'))
    fontes = copiar_pasta(os.path.join(mbed, 'library'), os.path.join(DESTINO, 'library'), ('.c', '.h'))
    shutil.copyfile(os.path.join(mbed, 'LICENSE'), os.path.join(DESTINO, 'LICENSE'))
    trocar_net_sockets(sys.argv[1], DESTINO)
    with open(os.path.join(mbed, 'include', 'mbedtls', 'build_info.h'), encoding='utf-8') as f:
        versao = next(l.split('"')[1] for l in f if 'MBEDTLS_VERSION_STRING ' in l)
    with open(os.path.join(DESTINO, 'VERSAO'), 'w', encoding='utf-8') as f:
        f.write(versao + ' (o do ESP-IDF, components/mbedtls/mbedtls)\n')
    print(f'mbedTLS {versao}: {n + 2} cabecalhos e {fontes} arquivos da library')


if __name__ == '__main__':
    main()
