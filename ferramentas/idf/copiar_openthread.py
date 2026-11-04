"""Copia o OpenThread do ESP-IDF (o submodulo components/openthread/openthread,
o fork da Espressif) e a biblioteca OpenThread do core para nativo/openthread.

As placas com o radio 802.15.4 (a C6 e a H2) usam a pilha de verdade: os
mesmos cabecalhos (openthread/*.h) e o mesmo codigo (src/core, src/cli), com a
configuracao do IDF. So a camada de plataforma e do simulador
(src/cs_openthread.cpp): o radio entre as placas da bancada, o relogio, o
sorteio e o armazenamento. Tudo isso vira uma biblioteca compilada so quando o
sketch usa Thread (motor/Nativo/CompiladorNativo.cs).

Uso: python copiar_openthread.py <pasta do esp-idf> <pasta do arduino-esp32>

O OpenThread e dos seus autores (BSD 3-Clause), os arquivos de configuracao
do IDF sao da Espressif (Apache 2.0) e a biblioteca do core e Apache 2.0.
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'openthread')

# O que o IDF compila para um dispositivo completo (o CMakeLists de components/openthread).
PASTAS_DE_CODIGO = ['core/api', 'core/backbone_router', 'core/border_router', 'core/coap', 'core/common', 'core/config', 'core/crypto', 'core/diags',
                    'core/instance', 'core/mac', 'core/meshcop', 'core/net', 'core/radio', 'core/thread', 'core/utils', 'cli', 'lib/platform', 'lib/url', 'lib/utils']
FORA = {'extension_example.cpp'}
# Da plataforma de exemplo: o que nao depende de chip.
DOS_UTILS = ['mac_frame.cpp', 'mac_frame.h', 'settings_ram.c', 'settings.h', 'code_utils.h', 'link_metrics.h']
CONFIGURACAO = ['openthread-core-esp32x-ftd-config.h', 'esp_openthread_common_macro.h']
ARDUINO = ['OThread', 'OThreadCLI', 'OThreadCLI_Util', 'OThreadCoAP', 'OThreadDNSSD', 'OThreadScan', 'OThreadUDP']


def copiar_arvore(origem, destino, extensoes):
    n = 0
    for pasta, _, arquivos in os.walk(origem):
        for a in arquivos:
            if a.endswith(extensoes) and a not in FORA:
                alvo = os.path.join(destino, os.path.relpath(pasta, origem), a)
                os.makedirs(os.path.dirname(alvo), exist_ok=True)
                shutil.copyfile(os.path.join(pasta, a), alvo)
                n += 1
    return n


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    comp = os.path.join(sys.argv[1], 'components', 'openthread')
    ot = os.path.join(comp, 'openthread')
    if not os.path.isdir(os.path.join(ot, 'src', 'core')):
        raise SystemExit(f'{ot}: o submodulo do OpenThread esta vazio (baixe o espressif/openthread na versao do IDF)')
    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    # Os cabecalhos publicos: o sketch ve estes em todas as placas ESP32.
    publicos = copiar_arvore(os.path.join(ot, 'include'), os.path.join(DESTINO, 'include'), ('.h',))
    for f in os.listdir(os.path.join(comp, 'include')):
        if f.startswith('esp_openthread'):
            shutil.copyfile(os.path.join(comp, 'include', f), os.path.join(DESTINO, 'include', f))
    # O codigo da pilha (e os cabecalhos internos dela).
    fontes = 0
    for pasta in PASTAS_DE_CODIGO:
        fontes += copiar_arvore(os.path.join(ot, 'src', *pasta.split('/')), os.path.join(DESTINO, 'src', *pasta.split('/')), ('.cpp', '.c', '.hpp', '.h'))
    copiar_arvore(os.path.join(ot, 'src', 'include'), os.path.join(DESTINO, 'src', 'include'), ('.h', '.hpp'))
    shutil.copyfile(os.path.join(ot, 'src', 'core', 'openthread-core-config.h'), os.path.join(DESTINO, 'src', 'core', 'openthread-core-config.h'))
    utils = os.path.join(ot, 'examples', 'platforms', 'utils')
    os.makedirs(os.path.join(DESTINO, 'src', 'utils'))
    for f in DOS_UTILS:
        shutil.copyfile(os.path.join(utils, f), os.path.join(DESTINO, 'src', 'utils', f))
    for f in CONFIGURACAO:
        shutil.copyfile(os.path.join(comp, 'private_include', f), os.path.join(DESTINO, 'src', f))
    # A biblioteca do core: o .h vale para o sketch, o .cpp entra na biblioteca.
    lib = os.path.join(sys.argv[2], 'libraries', 'OpenThread', 'src')
    os.makedirs(os.path.join(DESTINO, 'src', 'arduino'))
    for nome in ARDUINO:
        shutil.copyfile(os.path.join(lib, nome + '.h'), os.path.join(DESTINO, 'include', nome + '.h'))
        shutil.copyfile(os.path.join(lib, nome + '.cpp'), os.path.join(DESTINO, 'src', 'arduino', nome + '.cpp'))
    shutil.copyfile(os.path.join(ot, 'LICENSE'), os.path.join(DESTINO, 'LICENSE'))
    shutil.copyfile(os.path.join(AQUI, 'cs_openthread.cpp'), os.path.join(DESTINO, 'src', 'cs_openthread.cpp'))
    print(f'OpenThread: {publicos} cabecalhos publicos e {fontes} arquivos da pilha')


if __name__ == '__main__':
    main()
