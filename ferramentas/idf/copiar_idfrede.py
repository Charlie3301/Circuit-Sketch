"""Copia a rede de alto nivel do ESP-IDF para nativo/idfrede: o esp_http_client, o
esp_http_server, o esp_tls, o tcp_transport, o esp_https_ota, o esp_https_server
e o esp-mqtt, os de verdade. Rodam sobre os sockets do lwIP do simulador
(arduino/src/cs_sockets.cpp) e o mbedTLS de verdade, e viram uma biblioteca
compilada so quando o programa usa (motor/Nativo/CompiladorNativo.cs).

Uso: python copiar_idfrede.py <pasta do esp-idf>

Os componentes sao da Espressif (Apache 2.0).
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'idfrede')

# (componente, pastas de codigo, pastas que so tem cabecalhos)
CODIGO = [
    ('esp_http_client', ['esp_http_client.c', 'lib/http_auth.c', 'lib/http_header.c', 'lib/http_utils.c'], ['lib/include']),
    ('tcp_transport', ['transport.c', 'transport_internal.c', 'transport_socks_proxy.c', 'transport_ssl.c', 'transport_ws.c'], ['private_include']),
    ('esp-tls', ['esp_tls.c', 'esp_tls_mbedtls.c', 'esp_tls_error_capture.c', 'esp_tls_platform_port.c', 'esp-tls-crypto/esp_tls_crypto.c'], ['private_include']),
    ('esp_http_server', ['src/httpd_main.c', 'src/httpd_parse.c', 'src/httpd_sess.c', 'src/httpd_txrx.c', 'src/httpd_uri.c', 'src/httpd_ws.c', 'src/util/ctrl_sock.c'], ['src', 'src/util', 'src/port/esp32']),
    ('esp_https_ota', ['src/esp_https_ota.c'], []),
    ('esp_https_server', ['src/https_server.c'], []),
    ('mqtt/esp-mqtt', ['mqtt_client.c', 'lib/mqtt_msg.c', 'lib/mqtt_outbox.c', 'lib/platform_esp32_idf.c'], ['lib/include']),
]
# Os cabecalhos publicos que ainda nao estao em nativo/idf.
PUBLICOS = [('esp-tls', 'esp_tls.h'), ('esp-tls', 'esp_tls_errors.h'), ('esp-tls/esp-tls-crypto', 'esp_tls_crypto.h'), ('mbedtls/esp_crt_bundle/include', 'esp_crt_bundle.h')]
# Um nome por arquivo: o de cada componente nao bate com o de outro.
MUDANCAS = {
    'esp_http_client.c': [],
}


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    comps = os.path.join(sys.argv[1], 'components')
    if not os.path.isfile(os.path.join(comps, 'mqtt', 'esp-mqtt', 'mqtt_client.c')):
        raise SystemExit('o submodulo esp-mqtt esta vazio (components/mqtt/esp-mqtt)')
    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    src = os.path.join(DESTINO, 'src')
    inc = os.path.join(DESTINO, 'include')
    os.makedirs(inc)
    privados = os.path.join(src, 'privados')
    os.makedirs(privados)
    fontes = 0
    for comp, arquivos, cabecalhos in CODIGO:
        base = os.path.join(comps, *comp.split('/'))
        pasta = comp.replace('/', '_').replace('-', '_')
        for rel in arquivos:
            alvo = os.path.join(src, pasta, os.path.basename(rel))
            os.makedirs(os.path.dirname(alvo), exist_ok=True)
            shutil.copyfile(os.path.join(base, *rel.split('/')), alvo)
            fontes += 1
        for rel in cabecalhos:
            origem = os.path.join(base, *rel.split('/'))
            for a in os.listdir(origem):
                if a.endswith('.h'):
                    shutil.copyfile(os.path.join(origem, a), os.path.join(privados, a))
    for pasta, nome in PUBLICOS:
        shutil.copyfile(os.path.join(comps, *pasta.split('/'), nome), os.path.join(inc, nome))
    shutil.copyfile(os.path.join(AQUI, 'cs_idfrede.c'), os.path.join(src, 'cs_idfrede.c'))
    shutil.copyfile(os.path.join(AQUI, 'cs_idfrede_compat.h'), os.path.join(privados, 'cs_idfrede_compat.h'))
    # Os que o codigo inclui por outro caminho.
    shutil.copyfile(os.path.join(comps, 'mbedtls', 'port', 'include', 'mbedtls', 'esp_debug.h'), os.path.join(inc, 'esp_debug.h'))
    os.makedirs(os.path.join(inc, 'mbedtls'), exist_ok=True)
    shutil.move(os.path.join(inc, 'esp_debug.h'), os.path.join(inc, 'mbedtls', 'esp_debug.h'))
    shutil.copyfile(os.path.join(comps, 'mbedtls', 'port', 'include', 'mbedtls', 'esp_mbedtls_dynamic.h'), os.path.join(inc, 'mbedtls', 'esp_mbedtls_dynamic.h'))
    shutil.copyfile(os.path.join(comps, 'mqtt', 'esp-mqtt', 'include', 'mqtt_supported_features.h'), os.path.join(privados, 'mqtt_supported_features.h'))
    os.makedirs(os.path.join(privados, 'include'), exist_ok=True)
    shutil.copyfile(os.path.join(comps, 'tcp_transport', 'include', 'esp_transport.h'), os.path.join(privados, 'include', 'esp_transport.h'))
    os.makedirs(os.path.join(inc, 'sys'), exist_ok=True)
    with open(os.path.join(inc, 'sys', 'random.h'), 'w', newline='\n') as f:
        f.write('/* O sys/random.h da newlib: o sorteio vem do esp_random. */\n#pragma once\n#include <stddef.h>\n#include "esp_random.h"\n'
                'static inline long getrandom(void *buf, size_t n, unsigned flags) { (void)flags; esp_fill_random(buf, n); return (long)n; }\n')
    print(f'idfrede: {fontes} fontes')


if __name__ == '__main__':
    main()
