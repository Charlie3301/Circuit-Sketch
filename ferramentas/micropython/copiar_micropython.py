#!/usr/bin/env python3
"""Traz o MicroPython de verdade para nativo/micropython e gera os cabecalhos dele.

Uso: python -I ferramentas/micropython/copiar_micropython.py <pasta do micropython-1.x>

O que e nosso fica em vm/port, src/port e vm/mpconfigport.h, mphalport.h; o resto vem daqui.
Os .c viram src/, os .h viram vm/ (so a pilha ve: nao ha include/, o sketch nao tem o que ver). Os cabecalhos gerados (genhdr) normalmente
saem do make: aqui saem desta ferramenta, uma vez, para a pessoa nao precisar de Python.
Os .py das bibliotecas (micropython-lib e os desta pasta, em py/) viram texto em
src/port/cs_congelados.c: o import compila na hora, como os .py do sistema de arquivos.

O MicroPython e da MicroPython & CPython contributors, sob a licenca MIT (LICENSE).
"""
import concurrent.futures as cf
import os
import re
import shutil
import subprocess
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.dirname(os.path.dirname(AQUI))
DESTINO = os.path.join(RAIZ, 'nativo', 'micropython')
INC = os.path.join(DESTINO, 'vm')
SRC = os.path.join(DESTINO, 'src')

# O nucleo inteiro, menos o que so existe para outro processador ou para outra maquina.
FORA_DO_PY = re.compile(r'^(asm|emitinl|emitn|nlr(thumb|xtensa|powerpc|rv32|rv64|aarch64|mips|x86)|nativeglue)')
EXTMOD = ['machine_adc', 'machine_adc_block', 'machine_bitstream', 'machine_i2c', 'machine_mem', 'machine_pinbase', 'machine_pulse',
          'machine_pwm', 'machine_signal', 'machine_spi', 'machine_timer', 'machine_uart', 'machine_wdt', 'modasyncio', 'modbinascii',
          'modcryptolib', 'moddeflate', 'modframebuf', 'modhashlib', 'modheapq', 'modjson', 'modmachine', 'modmarshal', 'modnetwork',
          'modonewire', 'modtls_mbedtls', 'modos', 'modplatform', 'modrandom', 'modre', 'modselect', 'modtime', 'moductypes', 'modvfs', 'modwebsocket',
          'os_dupterm', 'vfs', 'vfs_reader', 'virtpin']
SHARED = ['runtime/interrupt_char.c', 'runtime/mpirq.c', 'runtime/pyexec.c', 'runtime/stdout_helpers.c',
          'runtime/sys_stdio_mphal.c', 'readline/readline.c', 'timeutils/timeutils.c', 'netutils/netutils.c']
LIB_C = ['uzlib', 're1.5', 'crypto-algorithms', 'oofatfs']
# As bibliotecas em Python que a placa traria congeladas, ou que a pessoa instalaria com o mip.
CONGELADOS = {
    'micropython-lib/micropython/drivers/led/neopixel/neopixel.py': 'neopixel',
    'micropython-lib/micropython/drivers/bus/onewire/onewire.py': 'onewire',
    'micropython-lib/micropython/drivers/sensor/ds18x20/ds18x20.py': 'ds18x20',
    'micropython-lib/micropython/drivers/display/ssd1306/ssd1306.py': 'ssd1306',
    'micropython-lib/micropython/drivers/storage/sdcard/sdcard.py': 'sdcard',
    'micropython-lib/micropython/drivers/radio/nrf24l01/nrf24l01.py': 'nrf24l01',
    'micropython-lib/micropython/umqtt.simple/umqtt/simple.py': 'umqtt/simple',
    'micropython-lib/micropython/umqtt.robust/umqtt/robust.py': 'umqtt/robust',
    'micropython-lib/micropython/net/ntptime/ntptime.py': 'ntptime',
    'micropython-lib/python-ecosys/requests/requests/__init__.py': 'requests/__init__',
    'micropython-lib/python-stdlib/base64/base64.py': 'base64',
    'micropython-lib/python-stdlib/logging/logging.py': 'logging',
    'micropython-lib/python-stdlib/datetime/datetime.py': 'datetime',
    'micropython-lib/python-stdlib/bisect/bisect.py': 'bisect',
    'micropython-lib/python-stdlib/copy/copy.py': 'copy',
    'micropython-lib/python-stdlib/functools/functools.py': 'functools',
    'micropython-lib/python-stdlib/itertools/itertools.py': 'itertools',
    'micropython-lib/python-stdlib/string/string.py': 'string',
    'micropython-lib/python-stdlib/types/types.py': 'types',
    'micropython-lib/python-stdlib/abc/abc.py': 'abc',
    'micropython-lib/python-stdlib/contextlib/contextlib.py': 'contextlib',
    'micropython-lib/python-stdlib/fnmatch/fnmatch.py': 'fnmatch',
    'micropython-lib/python-stdlib/operator/operator.py': 'operator',
    'micropython-lib/python-stdlib/stat/stat.py': 'stat',
    'micropython-lib/python-stdlib/textwrap/textwrap.py': 'textwrap',
}


def limpar(pasta):
    for nome in os.listdir(pasta):
        if nome in ('port', 'lwip') or nome in ('mpconfigport.h', 'mphalport.h', 'modnetwork.h', 'mdns.h'):
            continue
        caminho = os.path.join(pasta, nome)
        shutil.rmtree(caminho) if os.path.isdir(caminho) else os.remove(caminho)


def copiar(origem, destino):
    os.makedirs(os.path.dirname(destino), exist_ok=True)
    shutil.copyfile(origem, destino)


def main():
    mp = sys.argv[1]
    limpar(INC)
    limpar(SRC)
    for f in os.listdir(os.path.join(mp, 'py')):
        o = os.path.join(mp, 'py', f)
        if f.endswith('.h'):
            copiar(o, os.path.join(INC, 'py', f))
        elif f.endswith('.c') and not FORA_DO_PY.match(f):
            copiar(o, os.path.join(SRC, 'py', f))
    for d in ('extmod', 'shared'):
        for r, _, fs in os.walk(os.path.join(mp, d)):
            for f in fs:
                if f.endswith('.h'):
                    o = os.path.join(r, f)
                    copiar(o, os.path.join(INC, os.path.relpath(o, mp)))
    for m in EXTMOD:
        o = os.path.join(mp, 'extmod', m + '.c')
        if os.path.isfile(o):
            copiar(o, os.path.join(SRC, 'extmod', m + '.c'))
    for s in SHARED:
        copiar(os.path.join(mp, 'shared', s), os.path.join(SRC, 'shared', s))
    # Os que o modhashlib, o modre e o moddeflate incluem como .c: ficam em vm/lib.
    for d in LIB_C:
        for r, _, fs in os.walk(os.path.join(mp, 'lib', d)):
            for f in fs:
                if f.endswith(('.c', '.h')):
                    o = os.path.join(r, f)
                    copiar(o, os.path.join(INC, 'lib', os.path.relpath(o, os.path.join(mp, 'lib'))))
    # O SPI por software do extmod/machine_spi.c.
    copiar(os.path.join(mp, 'drivers', 'bus', 'spi.h'), os.path.join(INC, 'drivers', 'bus', 'spi.h'))
    copiar(os.path.join(mp, 'drivers', 'bus', 'softspi.c'), os.path.join(SRC, 'drivers', 'bus', 'softspi.c'))
    # Os sockets do port da ESP32: o lwip_* do simulador (cs_sockets.cpp) atende.
    copiar(os.path.join(mp, 'ports', 'esp32', 'modsocket.c'), os.path.join(SRC, 'esp32', 'modsocket.c'))
    # So na ESP32: a Pico nao tem o lwip do IDF (a rede dela e a do interpretador).
    soquete = os.path.join(SRC, 'esp32', 'modsocket.c')
    texto = open(soquete, encoding='utf-8').read()
    open(soquete, 'w', encoding='utf-8', newline='\n').write(
        '/* Modificado (CircuitSketch): so com o socket ligado (a ESP32). */\n#include "py/mpconfig.h"\n#if MICROPY_PY_SOCKET\n'
        + texto.rstrip() + '\n\n#endif /* MICROPY_PY_SOCKET */\n')
    congelar(mp)
    gerar(mp)
    copiar(os.path.join(mp, 'LICENSE'), os.path.join(DESTINO, 'LICENSE'))


def congelar(mp):
    nomes, textos = [], []
    pasta = os.path.join(mp, 'extmod', 'asyncio')
    for f in sorted(os.listdir(pasta)):
        if f.endswith('.py') and f != 'manifest.py':
            nomes.append('asyncio/' + f)
            textos.append(open(os.path.join(pasta, f), encoding='utf-8').read())
    for origem, nome in CONGELADOS.items():
        p = os.path.join(mp, 'lib', origem)
        if os.path.isfile(p):
            nomes.append(nome + '.py')
            textos.append(open(p, encoding='utf-8').read())
        else:
            print('faltou', origem)
    nossos = os.path.join(AQUI, 'py')
    for r, _, fs in os.walk(nossos):
        for f in sorted(fs):
            if f.endswith('.py'):
                nomes.append(os.path.relpath(os.path.join(r, f), nossos).replace(os.sep, '/'))
                textos.append(open(os.path.join(r, f), encoding='utf-8').read())
    textos = [t.replace('\r', '') for t in textos]
    with open(os.path.join(SRC, 'port', 'cs_congelados.c'), 'w', encoding='utf-8', newline='\n') as s:
        s.write('/* Gerado por ferramentas/micropython/copiar_micropython.py: as bibliotecas em Python. */\n#include <stdint.h>\n#include "py/frozenmod.h"\n')
        s.write('const char mp_frozen_names[] = ' + ''.join('"%s\\0"\n' % n for n in nomes) + '"\\0";\n')
        s.write('const uint32_t mp_frozen_str_sizes[] = {' + ','.join(str(len(t.encode())) for t in textos) + ',0};\n')
        s.write('const void *const mp_frozen_mpy_content[] = {0};\n')
        s.write('const char mp_frozen_str_content[] =\n')
        for t in textos:
            for i, linha in enumerate(t.split('\n')):
                fim = '\\n' if i < t.count('\n') else ''
                # Em bytes, por escape: o texto em UTF-8 fica igual ao tamanho de la de cima.
                corpo = ''.join(chr(c) if 32 <= c < 127 and chr(c) not in '"\\?' else '\\%03o' % c for c in linha.encode())
                s.write('"' + corpo + fim + '"\n')
            s.write('"\\0"\n')
        s.write(';\n')


def gerar(mp):
    zig = os.path.join(os.environ['LOCALAPPDATA'], 'CircuitSketch', 'zig', 'zig.exe')
    ambiente = {**os.environ, 'ZIG_GLOBAL_CACHE_DIR': os.path.join(os.environ['LOCALAPPDATA'], 'CircuitSketch', 'compilacao', 'zig')}
    gen = os.path.join(INC, 'genhdr')
    tmp = os.path.join(gen, '_tmp')
    os.makedirs(tmp, exist_ok=True)
    with open(os.path.join(gen, 'mpversion.h'), 'w', newline='\n') as v:
        v.write('#define MICROPY_GIT_TAG "v1.27.0"\n#define MICROPY_GIT_HASH "CircuitSketch"\n#define MICROPY_BUILD_DATE "2026-11-05"\n')
        v.write('#define MICROPY_VERSION_MAJOR 1\n#define MICROPY_VERSION_MINOR 27\n#define MICROPY_VERSION_MICRO 0\n#define MICROPY_VERSION_STRING "1.27.0"\n')
        v.write('#define MICROPY_FULL_VERSION_INFO "v1.27.0 on " MICROPY_BUILD_DATE "; " MICROPY_HW_BOARD_NAME " with " MICROPY_HW_MCU_NAME\n')
    # O primeiro passe nao tem as listas: comecam vazias.
    for nome in ('qstrdefs.generated.h', 'moduledefs.h', 'root_pointers.h'):
        open(os.path.join(gen, nome), 'w').close()
    fontes = [os.path.join(d, f) for d, _, fs in os.walk(SRC) for f in fs if f.endswith('.c') and f != 'cs_congelados.c']
    # A placa so importa aqui para os cabecalhos do IDF do modsocket.c: os nomes (qstr) nao mudam de chip.
    n = os.path.join(RAIZ, 'nativo')
    pastas = [os.path.join(n, 'arduino', 'include'), INC, os.path.join(INC, 'py'), os.path.join(INC, 'lib'), SRC,
              os.path.join(n, 'idf', 'esp32'), os.path.join(n, 'idf', 'comum'), os.path.join(n, 'idf', 'proximos'), os.path.join(n, 'mbedtls', 'include')]
    bases = ['-E', '-DNO_QSTR', '-w', '-target', 'x86_64-windows-gnu', '-DMBEDTLS_CONFIG_FILE="cs_mbedtls_config.h"', *[x for d in pastas for x in ('-I', d)]]

    def um(par):
        f, placa = par
        r = subprocess.run([zig, 'cc', *bases, '-I', os.path.join(tmp, placa), f], capture_output=True, text=True, errors='replace', env=ambiente)
        if r.returncode:
            print('pre-processar', f, ' '.join(l for l in r.stderr.splitlines() if 'error:' in l)[:300])
        return placa, r.stdout
    # Os nomes (qstr) das duas familias: a tabela e uma so.
    os.makedirs(os.path.join(tmp, 'esp32'), exist_ok=True)
    os.makedirs(os.path.join(tmp, 'rp2040'), exist_ok=True)
    open(os.path.join(tmp, 'esp32', 'cs_placa.h'), 'w', newline='\n').write('#define ESP32 1\n#define CONFIG_IDF_TARGET_ESP32 1\n')
    open(os.path.join(tmp, 'rp2040', 'cs_placa.h'), 'w', newline='\n').write('#define ARDUINO_ARCH_RP2040 1\n')
    with cf.ThreadPoolExecutor(8) as ex:
        saidas = list(ex.map(um, [(f, p) for f in fontes for p in ('esp32', 'rp2040')
                                  if not (p == 'rp2040' and os.path.basename(f) in ('modsocket.c', 'modtls_mbedtls.c', 'modcryptolib.c', 'modhashlib.c'))]))
    tudo = '\n'.join(t for _, t in saidas)
    # O modulo que so uma familia tem (o network da ESP32, o rp2 da Pico) entra so nela.
    registro = r'MP_REGISTER(?:_EXTENSIBLE)?_MODULE(?:_DELEGATION)?\(\s*MP_QSTR_(\w+)'
    por_familia = {f: set(re.findall(registro, '\n'.join(t for p, t in saidas if p == f))) for f in ('esp32', 'rp2040')}
    qstrs = sorted(set(re.findall(r'\bMP_QSTR_(\w+)', tudo)))
    qstrs = [q for q in qstrs if q != 'NULL']
    modulos = sorted(set(re.findall(r'MP_REGISTER(?:_EXTENSIBLE)?_MODULE(?:_DELEGATION)?\([^;]*\);', tudo)))
    raizes = sorted(set(re.findall(r'MP_REGISTER_ROOT_POINTER\([^;]*\);', tudo)))
    r = subprocess.run([zig, 'cc', '-E', '-DNO_QSTR', '-w', '-target', 'x86_64-windows-gnu', '-I', os.path.join(tmp, 'esp32'), '-I', INC, '-I', SRC, '-x', 'c', os.path.join(INC, 'py', 'qstrdefs.h')],
                       capture_output=True, text=True, env=ambiente)
    base = [l for l in r.stdout.splitlines() if l.startswith(('QCFG', 'Q('))]
    previo = os.path.join(tmp, 'qstrdefs.pre.h')
    with open(previo, 'w', newline='\n') as q:
        q.write('\n'.join(base) + '\n' + '\n'.join('Q(%s)' % n for n in qstrs) + '\n')

    def rodar(script, entrada, saida):
        r = subprocess.run([sys.executable, '-I', os.path.join(mp, 'py', script), entrada], capture_output=True, text=True)
        if r.returncode:
            raise SystemExit(r.stderr)
        open(os.path.join(gen, saida), 'w', newline='\n').write(r.stdout)
    rodar('makeqstrdata.py', previo, 'qstrdefs.generated.h')
    p = os.path.join(tmp, 'module.collected')
    open(p, 'w', newline='\n').write('\n'.join(modulos) + '\n')
    rodar('makemoduledefs.py', p, 'moduledefs.h')
    condicao = {'esp32': 'defined(ESP32)', 'rp2040': 'defined(ARDUINO_ARCH_RP2040)'}
    caminho = os.path.join(gen, 'moduledefs.h')
    texto = open(caminho, encoding='utf-8').read()
    for familia, outra in (('esp32', 'rp2040'), ('rp2040', 'esp32')):
        for nome in sorted(por_familia[familia] - por_familia[outra]):
            macro = 'MODULE_DEF_' + nome.upper()
            bloco = re.search(r'(?m)^extern [^\n]*\n#undef %s\n#define %s [^\n]*\n' % (macro, macro), texto)
            if bloco:
                texto = texto.replace(bloco.group(0), '#if %s\n%s#else\n#undef %s\n#define %s\n#endif\n' % (condicao[familia], bloco.group(0), macro, macro))
    open(caminho, 'w', newline='\n').write(texto)
    p = os.path.join(tmp, 'root.collected')
    open(p, 'w', newline='\n').write('\n'.join(raizes) + '\n')
    rodar('make_root_pointers.py', p, 'root_pointers.h')
    shutil.rmtree(tmp)
    print(len(qstrs), 'qstrs;', len(modulos), 'modulos;', len(raizes), 'raizes;', len(fontes), 'fontes')


if __name__ == '__main__':
    main()
