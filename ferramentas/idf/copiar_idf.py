"""Copia cabecalhos do ESP-IDF (e tudo o que eles incluem) para nativo/idf.

O C/C++ compilado do simulador ve os cabecalhos de verdade do ESP-IDF -- os
tipos, as constantes e as declaracoes --, e as funcoes sao as do simulador
(nativo/arduino/src). Esta ferramenta traz um cabecalho pedido com a arvore
inteira de #include dele, chip por chip:

    nativo/idf/comum/<caminho>     o que e igual em todos os chips
    nativo/idf/<chip>/<caminho>    o que muda de chip para chip (soc/, hal/ do chip)

O que o simulador tem com o mesmo nome (nativo/arduino/include: o soc.h com
o REG_WRITE que vai ao simulador...) vale primeiro -- o compilador o acha
antes; o de verdade fica copiado atras dele, para o do simulador poder
inclui-lo (#include_next) e so acrescentar o que e dele.

Uso:
    python copiar_idf.py <pasta do esp-idf> [--extra <idf-extra-components>] <cabecalho> [<cabecalho> ...]
    python copiar_idf.py <pasta do esp-idf> [--extra <idf-extra-components>] --lista arquivo.txt
    python copiar_idf.py <pasta do esp-idf> --todos

O --todos traz todo cabecalho dos componentes de DA_PLACA: biblioteca que usa o hardware direto
inclui o hal/gpio_ll.h, o soc/rtc.h, o esp_private/gdma.h, e na placa eles sempre existem. A
pilha Bluetooth, o wpa_supplicant e o mbedTLS ficam de fora: tem nomes genericos demais
("os.h", "common/...") e o simulador tem os dele.

O --extra traz componentes da Espressif que nao vem no IDF e o core do
Arduino usa (o network_provisioning).

O ESP-IDF e da Espressif Systems, sob a Apache License 2.0; a versao usada
fica em nativo/idf/VERSAO.
"""
import os
import re
import shutil
import sys
import time

CHIPS = ['esp32', 'esp32s2', 'esp32s3', 'esp32c3', 'esp32c6', 'esp32h2']
# A versao do sensor de toque de cada chip (SOC_TOUCH_SENSOR_VERSION).
VERSAO_DO_TOQUE = {'esp32': 1, 'esp32s2': 2, 'esp32s3': 2}
# O controlador Bluetooth da S3 e o da C3: o IDF usa o bt/include/esp32c3 nas duas.
BT_DE_OUTRO_CHIP = {'esp32s3': 'esp32c3'}
AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'idf')
DO_SIMULADOR = os.path.join(REPO, 'nativo', 'arduino', 'include')

# Os da linguagem e da libc (o compilador traz) e o FreeRTOS (o do simulador).
PULAR = re.compile(r'^(pthread\.h|semaphore\.h|mqueue\.h|endian\.h|byteswap\.h|std\w*\.h|string\.h|strings\.h|limits\.h|math\.h|time\.h|errno\.h|assert\.h|ctype\.h|inttypes\.h|'
                   r'float\.h|stdio\.h|stdlib\.h|unistd\.h|fcntl\.h|signal\.h|setjmp\.h|locale\.h|wchar\.h|alloca\.h|'
                   r'sys/(?!socket\.h|ioctl\.h|poll\.h|select\.h|uio\.h).*|machine/.*|freertos/(?!ringbuf\.h).*|FreeRTOS\.h|reent\.h|sdkconfig\.h|xtensa/.*|riscv/.*)$')
INCLUSAO = re.compile(r'^\s*#\s*include\s*[<"]([^>"]+)[>"]', re.M)
INCLUSAO_SEGUINTE = re.compile(r'^\s*#\s*include_next\s*[<"]([^>"]+)[>"]', re.M)


def pastas_de_inclusao(idf, chip, extras=()):
    """As pastas onde o ESP-IDF procura os cabecalhos, as do chip primeiro."""
    do_chip, comuns = [], []
    for comp in [os.path.join(idf, 'components'), *extras]:
        _pastas_de(comp, chip, do_chip, comuns)
    return do_chip + comuns


def _pastas_de(comp, chip, do_chip, comuns):
    for nome in sorted(os.listdir(comp)):
        raiz = os.path.join(comp, nome)
        if not os.path.isdir(raiz):
            continue
        for dirpath, dirnames, _ in os.walk(raiz):
            rel = os.path.relpath(dirpath, comp).replace(os.sep, '/')
            partes = rel.split('/')
            # Pastas de teste, de exemplo e de outros chips ficam de fora.
            if any(p in ('test', 'test_apps', 'tests', 'host_test', 'linux', 'mock', 'examples', 'contrib', 'subproject') for p in partes):
                dirnames[:] = []
                continue
            emprestado = partes[0] == 'bt' and BT_DE_OUTRO_CHIP.get(chip) in partes
            if [p for p in partes if p.startswith('esp32') and p not in (chip, 'esp32xx')] and not emprestado:
                dirnames[:] = []
                continue
            # O driver do toque tem uma pasta por versao do sensor (hw_ver1 na
            # ESP32, hw_ver2 na S2/S3, hw_ver3 na P4): so a do chip vale.
            versao = [p for p in partes if re.fullmatch(r'hw_ver\d', p)]
            if versao and versao[0] != 'hw_ver%d' % VERSAO_DO_TOQUE.get(chip, 0):
                dirnames[:] = []
                continue
            base = partes[-1]
            pai = partes[-2] if len(partes) > 1 else ''
            # include/, register/, deprecated/ (os drivers antigos), o
            # include/<chip> do esp_rom (o "rom/ets_sys.h") e o esp_rom/<chip> (esp_rom_caps.h).
            # E o include/local do WiFi (o esp_wifi_types_native.h), e o http_parser
            # (o cabecalho na raiz do componente; o HTTP_Method.h do WebServer usa), e a
            # API do Bluedroid (bt/host/bluedroid/api/include/api: o esp_gap_bt_api.h...).
            if base in ('include', 'register', 'deprecated', 'interface', 'public') or (base == chip and pai in ('include', 'esp_rom')) \
                    or (base == 'local' and partes[0] == 'esp_wifi') or rel in ('http_parser', 'console', 'fatfs/vfs', 'fatfs/src', 'fatfs/diskio') \
                    or (base == 'api' and pai == 'include' and partes[0] == 'bt') or rel in ('newlib/platform_include', 'lwip/include/apps') \
                    or (partes[0] == 'protocomm' and pai == 'include' and base in ('common', 'security', 'transports', 'crypto')):
                (do_chip if chip in partes or versao or emprestado else comuns).append(dirpath)


def indice(pastas):
    """Cada caminho de #include -> o arquivo (o primeiro que aparece vale)."""
    achados = {}
    for p in pastas:
        for dirpath, _, files in os.walk(p):
            for f in files:
                if f.endswith(('.h', '.inc', '.hpp')):
                    caminho = os.path.join(dirpath, f)
                    achados.setdefault(os.path.relpath(caminho, p).replace(os.sep, '/'), caminho)
    return achados


def copiar_arquivo(origem, alvo):
    """Copia so o que mudou; o OneDrive as vezes segura o arquivo um instante (o Errno 22)."""
    dados = open(origem, 'rb').read()
    if os.path.isfile(alvo) and open(alvo, 'rb').read() == dados:
        return
    for tentativa in range(10):
        try:
            with open(alvo, 'wb') as f:
                f.write(dados)
            return
        except OSError:
            if tentativa == 9:
                raise
            time.sleep(0.5)


def todos_com_o_nome(pastas, nome):
    """Os arquivos com esse caminho de #include, na ordem das pastas."""
    achados = []
    for p in pastas:
        caminho = os.path.join(p, *nome.split('/'))
        if os.path.isfile(caminho) and caminho not in achados:
            achados.append(caminho)
    return achados


def copiar(idf, pedidos, extras=()):
    copiados = {c: set() for c in CHIPS}
    faltaram = set()
    for chip in CHIPS:
        pastas = pastas_de_inclusao(idf, chip, extras)
        mapa = indice(pastas)
        fila = list(pedidos)
        vistos = set()
        while fila:
            nome = fila.pop().replace('\\', '/')
            if nome in vistos or PULAR.match(nome):
                continue
            vistos.add(nome)
            origem = mapa.get(nome)
            if origem is None:
                faltaram.add(nome)
                continue
            rel = origem.replace(os.sep, '/')
            partes = rel.split('/')
            emprestado = BT_DE_OUTRO_CHIP.get(chip) in partes and '/bt/' in rel
            especifico = chip in partes or emprestado or any(re.fullmatch(r'hw_ver\d', p) for p in partes)
            alvo = os.path.join(DESTINO, chip if especifico else 'comum', nome)
            os.makedirs(os.path.dirname(alvo), exist_ok=True)
            copiar_arquivo(origem, alvo)
            copiados[chip].add(nome)
            texto = open(origem, encoding='utf-8', errors='ignore').read()
            # O #include_next de um wrapper do IDF (o lwip/sockets.h): a camada de
            # baixo vai para idf/proximos, que vem logo depois de idf/comum.
            for m in INCLUSAO_SEGUINTE.finditer(texto):
                seguinte = m.group(1)
                candidatos = [c for c in todos_com_o_nome(pastas, seguinte) if os.path.normcase(c) != os.path.normcase(origem)]
                if not candidatos:
                    faltaram.add(seguinte)
                    continue
                destino = os.path.join(DESTINO, 'proximos', *seguinte.split('/'))
                os.makedirs(os.path.dirname(destino), exist_ok=True)
                copiar_arquivo(candidatos[0], destino)
                texto_seguinte = open(candidatos[0], encoding='utf-8', errors='ignore').read()
                fila.extend(i.group(1) for i in INCLUSAO.finditer(texto_seguinte))
            for m in INCLUSAO.finditer(texto):
                inc = m.group(1)
                # "x.h" relativo a pasta do proprio arquivo.
                vizinho = os.path.normpath(os.path.join(os.path.dirname(nome), inc)).replace(os.sep, '/')
                if '"' in m.group(0) and os.path.isfile(os.path.join(os.path.dirname(origem), inc)):
                    fila.append(vizinho)
                else:
                    fila.append(inc)
    total = len(set().union(*copiados.values()))
    print(f'{total} cabecalhos copiados')
    if faltaram:
        print('nao achados (o simulador precisa ter):', ' '.join(sorted(faltaram)))


# O codigo do IDF que so faz conta e vem como esta (o core da placa o usa):
# vai para nativo/arduino/src/arduino-esp32/idf, compilado so nas ESP32.
FONTES = ['esp_common/src/esp_err_to_name.c', 'http_parser/http_parser.c', 'esp_hid/src/esp_hid_common.c', 'lwip/lwip/src/core/ipv4/ip4_addr.c', 'lwip/lwip/src/core/ipv6/ip6_addr.c',
          'console/commands.c', 'console/split_argv.c', 'console/argtable3/argtable3.h', 'console/argtable3/argtable3_private.h', 'console/argtable3/argtable3.c',
          'console/argtable3/arg_cmd.c', 'console/argtable3/arg_date.c', 'console/argtable3/arg_dbl.c', 'console/argtable3/arg_dstr.c',
          'console/argtable3/arg_end.c', 'console/argtable3/arg_file.c', 'console/argtable3/arg_hashtable.c', 'console/argtable3/arg_int.c',
          'console/argtable3/arg_lit.c', 'console/argtable3/arg_rem.c', 'console/argtable3/arg_rex.c', 'console/argtable3/arg_str.c',
          'console/argtable3/arg_utils.c',
          'esp_lcd/src/esp_lcd_panel_io.c', 'esp_lcd/src/esp_lcd_panel_ops.c', 'esp_lcd/src/esp_lcd_panel_ssd1306.c',
          'esp_lcd/src/esp_lcd_panel_st7789.c', 'esp_lcd/src/esp_lcd_panel_nt35510.c', 'esp_lcd/i2c/esp_lcd_panel_io_i2c_v2.c', 'esp_lcd/spi/esp_lcd_panel_io_spi.c', 'esp_lcd/interface/esp_lcd_panel_interface.h', 'esp_lcd/interface/esp_lcd_panel_io_interface.h']


# O codigo que vem de um --extra, procurado pelo fim do caminho.
FONTES_DOS_EXTRAS = ['esp_rainmaker/src/standard_types/esp_rmaker_standard_params.c',
                     'esp_rainmaker/src/standard_types/esp_rmaker_standard_devices.c']


def copiar_fontes(idf, extras=()):
    pasta = os.path.join(REPO, 'nativo', 'arduino', 'src', 'arduino-esp32', 'idf')
    os.makedirs(pasta, exist_ok=True)
    for f in FONTES:
        with open(os.path.join(idf, 'components', *f.split('/')), encoding='utf-8') as e:
            texto = e.read()
        # O strlcpy e o asprintf da newlib, que o Windows nao tem.
        if f.endswith(('/commands.c', '/esp_err_to_name.c')):
            texto = '/* Modificado (CircuitSketch): inclui o cs_posix.h. */\n#include "cs_posix.h"\n' + texto
        # O __containerof da newlib. E o que o esp_lcd_common.h traz para o SPI e so para
        # o DMA do LCD paralelo (descritores de 32 bits), que nao existe aqui.
        if f.startswith('esp_lcd/') and f.endswith('.c'):
            texto = texto.replace('#include "esp_private/gpio.h"\n', '').replace('#include "hal/gpio_ll.h"\n', '').replace('#include "hal/gpio_hal.h"\n', '')
            texto = texto.replace('#include "esp_lcd_common.h"\n', '')
            texto = texto.replace('gpio_ll_set_level(&GPIO, ', 'gpio_set_level(').replace('gpio_output_enable(', 'gpio_saida_ligada(').replace('gpio_output_disable(', 'gpio_saida_desligada(')
            texto = ('/* Modificado (CircuitSketch): sem o DMA e os registradores do GPIO. */\n#include <stddef.h>\n#include <stdint.h>\n'
                     '#define __containerof(p, t, m) ((t *)((char *)(p) - offsetof(t, m)))\n#define gpio_func_sel(p, f) ((void)0)\n#define gpio_saida_ligada(p) gpio_set_direction((p), GPIO_MODE_OUTPUT)\n#define gpio_saida_desligada(p) gpio_set_direction((p), GPIO_MODE_INPUT)\n'
                     'static inline void lcd_com_reverse_buffer_bytes(uint8_t *b, int i, int f) { while (i < f) { uint8_t x = b[i]; b[i++] = b[f]; b[f--] = x; } }\n') + texto
        copiar_texto = os.path.join(pasta, os.path.basename(f))
        with open(copiar_texto, 'w', encoding='utf-8', newline='\n') as s:
            s.write(texto)
    for f in FONTES_DOS_EXTRAS:
        for extra in extras:
            for dirpath, _, arquivos in os.walk(extra):
                caminho = os.path.join(dirpath, os.path.basename(f))
                if os.path.basename(f) in arquivos and caminho.replace(os.sep, '/').endswith(f):
                    shutil.copyfile(caminho, os.path.join(pasta, os.path.basename(f)))


DA_PLACA = ['app_update', 'bootloader_support', 'console', 'driver', 'efuse', 'esp_adc', 'esp_app_format', 'esp_blockdev',
            'esp_bootloader_format', 'esp_coex', 'esp_common', 'esp_driver_ana_cmpr', 'esp_driver_bitscrambler', 'esp_driver_cam',
            'esp_driver_dac', 'esp_driver_gpio', 'esp_driver_gptimer', 'esp_driver_i2c', 'esp_driver_i2s', 'esp_driver_i3c',
            'esp_driver_isp', 'esp_driver_jpeg', 'esp_driver_ledc', 'esp_driver_mcpwm', 'esp_driver_parlio', 'esp_driver_pcnt',
            'esp_driver_ppa', 'esp_driver_rmt', 'esp_driver_sdio', 'esp_driver_sdm', 'esp_driver_sdmmc', 'esp_driver_sdspi',
            'esp_driver_spi', 'esp_driver_touch_sens', 'esp_driver_tsens', 'esp_driver_twai', 'esp_driver_uart', 'esp_driver_usb_serial_jtag',
            'esp_eth', 'esp_event', 'esp_gdbstub', 'esp_hid', 'esp_http_client', 'esp_http_server', 'esp_https_ota', 'esp_https_server',
            'esp_hw_support', 'esp_lcd', 'esp_local_ctrl', 'esp_mm', 'esp_netif', 'esp_partition', 'esp_phy', 'esp_pm', 'esp_psram',
            'esp_ringbuf', 'esp_rom', 'esp_security', 'esp_system', 'esp_tee', 'esp_timer', 'esp_vfs_console', 'esp_wifi', 'espcoredump',
            'fatfs', 'hal', 'heap', 'http_parser', 'ieee802154', 'log', 'mqtt', 'nvs_flash', 'nvs_sec_provider', 'perfmon', 'riscv',
            'sdmmc', 'soc', 'spi_flash', 'tcp_transport', 'touch_element', 'ulp', 'unity', 'usb', 'vfs', 'wear_levelling', 'xtensa']


def unificar_rom():
    """No IDF, "rom/x.h" e "<chip>/rom/x.h" sao o mesmo arquivo, e a guarda dele vale para os dois.
    Copiados viram dois: quem inclui os dois (a GFX Library) definiria tudo duas vezes."""
    for chip in CHIPS:
        pasta = os.path.join(DESTINO, chip, 'rom')
        if not os.path.isdir(pasta):
            continue
        for nome in os.listdir(pasta):
            caminho = os.path.join(pasta, nome)
            original = os.path.join(DESTINO, chip, chip, 'rom', nome)
            if os.path.isfile(original) and open(caminho, 'rb').read() == open(original, 'rb').read():
                with open(caminho, 'w', encoding='utf-8', newline='\n') as f:
                    f.write('#include "%s/rom/%s"\n' % (chip, nome))


def todos_da_placa(idf):
    """Os caminhos de #include de todo cabecalho dos componentes de DA_PLACA, em todos os chips."""
    nomes = set()
    raiz = os.path.join(idf, 'components')
    for chip in CHIPS:
        pastas = [p for p in pastas_de_inclusao(idf, chip)
                  if os.path.relpath(p, raiz).replace(os.sep, '/').split('/')[0] in DA_PLACA]
        nomes |= set(indice(pastas))
    return sorted(nomes)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    idf = sys.argv[1]
    resto = sys.argv[2:]
    extras = []
    while resto and resto[0] == '--extra':
        extras.append(resto[1])
        resto = resto[2:]
    if resto[0] == '--todos':
        copiar(idf, todos_da_placa(idf), extras)
        unificar_rom()
        return
    if resto[0] == '--lista':
        pedidos = [l.strip() for l in open(resto[1], encoding='utf-8') if l.strip() and not l.startswith('#')]
    else:
        pedidos = resto
    copiar(idf, pedidos, extras)
    copiar_fontes(idf, extras)
    versao = os.path.join(idf, 'version.txt')
    with open(os.path.join(DESTINO, 'VERSAO'), 'w', encoding='utf-8') as f:
        f.write(open(versao).read().strip() if os.path.isfile(versao) else os.path.basename(os.path.normpath(idf)))
        f.write('\n')


if __name__ == '__main__':
    main()
