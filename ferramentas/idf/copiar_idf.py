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

O --extra traz componentes da Espressif que nao vem no IDF e o core do
Arduino usa (o network_provisioning).

O ESP-IDF e da Espressif Systems, sob a Apache License 2.0; a versao usada
fica em nativo/idf/VERSAO.
"""
import os
import re
import shutil
import sys

CHIPS = ['esp32', 'esp32s2', 'esp32s3', 'esp32c3', 'esp32c6', 'esp32h2']
# A versao do sensor de toque de cada chip (SOC_TOUCH_SENSOR_VERSION).
VERSAO_DO_TOQUE = {'esp32': 1, 'esp32s2': 2, 'esp32s3': 2}
AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'idf')
DO_SIMULADOR = os.path.join(REPO, 'nativo', 'arduino', 'include')

# Os da linguagem e da libc (o compilador traz) e o FreeRTOS (o do simulador).
PULAR = re.compile(r'^(std\w*\.h|string\.h|strings\.h|limits\.h|math\.h|time\.h|errno\.h|assert\.h|ctype\.h|inttypes\.h|'
                   r'float\.h|stdio\.h|stdlib\.h|unistd\.h|fcntl\.h|signal\.h|setjmp\.h|locale\.h|wchar\.h|alloca\.h|'
                   r'sys/.*|machine/.*|freertos/(?!ringbuf\.h).*|FreeRTOS\.h|reent\.h|sdkconfig\.h|xtensa/.*|riscv/.*)$')
INCLUSAO = re.compile(r'^\s*#\s*include\s*[<"]([^>"]+)[>"]', re.M)


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
            if any(p in ('test', 'test_apps', 'tests', 'host_test', 'linux', 'mock', 'examples') for p in partes):
                dirnames[:] = []
                continue
            if [p for p in partes if p.startswith('esp32') and p != chip]:
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
            # E o include/local do WiFi (o esp_wifi_types_native.h).
            if base in ('include', 'register', 'deprecated', 'interface', 'public') or (base == chip and pai in ('include', 'esp_rom')) \
                    or (base == 'local' and partes[0] == 'esp_wifi'):
                (do_chip if chip in partes or versao else comuns).append(dirpath)


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


def copiar(idf, pedidos, extras=()):
    copiados = {c: set() for c in CHIPS}
    faltaram = set()
    for chip in CHIPS:
        mapa = indice(pastas_de_inclusao(idf, chip, extras))
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
            especifico = chip in rel.split('/') or any(re.fullmatch(r'hw_ver\d', p) for p in rel.split('/'))
            alvo = os.path.join(DESTINO, chip if especifico else 'comum', nome)
            os.makedirs(os.path.dirname(alvo), exist_ok=True)
            shutil.copyfile(origem, alvo)
            copiados[chip].add(nome)
            texto = open(origem, encoding='utf-8', errors='ignore').read()
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
    if resto[0] == '--lista':
        pedidos = [l.strip() for l in open(resto[1], encoding='utf-8') if l.strip() and not l.startswith('#')]
    else:
        pedidos = resto
    copiar(idf, pedidos, extras)
    versao = os.path.join(idf, 'version.txt')
    with open(os.path.join(DESTINO, 'VERSAO'), 'w', encoding='utf-8') as f:
        f.write(open(versao).read().strip() if os.path.isfile(versao) else os.path.basename(os.path.normpath(idf)))
        f.write('\n')


if __name__ == '__main__':
    main()
