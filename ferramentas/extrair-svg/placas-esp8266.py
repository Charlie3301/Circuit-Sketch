"""python ferramentas/extrair-svg/placas-esp8266.py

As placas ESP8266 da Alpha 25, no padrao de qualidade (estilo_a25.py) e com a
pinagem de verdade de cada modulo:

  - esp8266-nodemcu: o projeto aberto da NodeMCU DevKit V1.0 (Altium, MIT, em
    esp8266-cad/): o contorno, as trilhas, as vias, as ilhas, a serigrafia e a
    posicao de cada componente saem do arquivo; o ESP-12E por cima e o mesmo
    desenho do modulo solto;
  - os modulos da Ai-Thinker, com as ilhas nas posicoes dos footprints do
    kicad-ESP8266 (J. Dunmire, CC BY-SA 4.0: ESP-01, 03, 06, 07, 12, 12E e
    13/WROOM-02), a numeracao das tabelas da XESS, e as medidas, a pinagem e a
    posicao das pecas dos desenhos com cota do guia "ESP8266 modules hardware
    guide" (l0l.org.uk), do wiki do esp8266.com e das fotos de cada modulo:
    o ESP-01 de 14,3 x 24,8 com a barra 2x4 soldada por baixo, os castelados
    de 2,0 mm (03, 04, 07, 08, 10, 12, 12E, 12F, 14), o de 1,5 mm (13), os de
    ilha por baixo (06, 09), os de 2,54 mm (02, 05) e o de 1,27 mm (11).

O que muda da Alpha 18: cada modulo tem so os pinos que existem nele (o ESP-03
nao tem RST nem GPIO4/5; o ESP-11 so tem GPIO0 e GPIO1; os pinos de lado do
ESP-14 sao do STM8S003 que vai junto, e do ESP8266 so sai o GPIO0), na ordem
e na posicao fisica.
"""
import importlib.util
import io
import json
import math
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
_spec = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(E)

A = E.A
pa = E.pa
mover, rodar, T = E.mover, E.rodar, E.T
OURO, ESTANHO = E.OURO, E.ESTANHO
AZUL_LED, VERMELHO_LED = '#3b8cff', '#ff3b30'
pecas = []
leds = []           # (peca, x px, y px, r px, codigo, cor) para o EditorCircuito.cs
botoes = []         # (peca, x px, y px, r px, nome)


def gravar(nome, d, L, H):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(E.svg(d, L, H))


def guardar(id_peca, L, H, pinos):
    assert abs(L * A - round(L * A)) < 1e-6 and abs(H * A - round(H * A)) < 1e-6, (id_peca, L * A, H * A)
    nomes = [p[0] for p in pinos]
    assert len(nomes) == len(set(nomes)), (id_peca, nomes)
    for n, x, y, *_ in pinos:
        assert 0 < x < L and 0 < y < H, (id_peca, n, x, y)
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')


def led(peca, x, y, codigo, cor, r=0.55):
    leds.append((peca, x * A, y * A, r * A, codigo, cor))


# =====================================================================
#  os nomes e as descricoes dos pinos
# =====================================================================
DESC = {
    'VCC': (['power'], 'Alimentação: 3,3 V (não aguenta 5 V)'),
    'GND': (['power'], 'GND'),
    'RST': ([], 'Reset (ativo em LOW): solto ou em HIGH, roda'),
    'CH_PD': ([], 'Liga o chip (EN/CH_PD): precisa estar em HIGH para funcionar'),
    'ADC': (['analog'], 'Entrada analógica (TOUT): 0 a 1 V'),
    'TXD': (['usart'], 'Serial: TX (GPIO1)'),
    'RXD': (['usart'], 'Serial: RX (GPIO3)'),
    'GPIO0': (['pwm'], 'GPIO0: em LOW no boot entra no modo de gravação'),
    'GPIO1': (['usart'], 'GPIO1 (o TX da serial)'),
    'GPIO2': (['pwm'], 'GPIO2: precisa estar em HIGH no boot'),
    'GPIO4': (['i2c', 'pwm'], 'GPIO4 (o SDA de costume)'),
    'GPIO5': (['i2c', 'pwm'], 'GPIO5 (o SCL de costume)'),
    'GPIO12': (['spi', 'pwm'], 'GPIO12 (MISO da HSPI)'),
    'GPIO13': (['spi', 'pwm'], 'GPIO13 (MOSI da HSPI)'),
    'GPIO14': (['spi', 'pwm'], 'GPIO14 (SCK da HSPI)'),
    'GPIO15': (['spi', 'pwm'], 'GPIO15 (CS da HSPI): precisa estar em LOW no boot'),
    'GPIO16': ([], 'GPIO16 (o que acorda do deep sleep, ligado ao RST)'),
    'ANT': ([], 'A saída para a antena de fora (não ligar)'),
    'NC': ([], 'Não ligado'),
}


def desc(nome):
    base = nome.split('.')[0]
    return DESC.get(base, ([], base))


def catalogo(pinos, extra=None):
    """(nome, x, y, lado) -> (nome, x, y, sinais, descricao)."""
    extra = extra or {}
    out = []
    for nome, x, y, _ in pinos:
        s, t = desc(nome)
        out.append((nome, x, y, s, extra.get(nome, t)))
    return out


# os pinos da flash dos de 22 ilhas (o nome do pino e o GPIO)
FLASH_12E = ['GPIO11', 'GPIO7', 'GPIO9', 'GPIO10', 'GPIO8', 'GPIO6']
DESC_FLASH = {'GPIO11': 'CS0 da flash (GPIO11): a flash usa, não ligue',
              'GPIO7': 'MISO da flash (GPIO7): a flash usa, não ligue',
              'GPIO9': 'GPIO9 (SD_D2 da flash): a flash usa, não ligue',
              'GPIO10': 'GPIO10 (SD_D3 da flash)',
              'GPIO8': 'MOSI da flash (GPIO8): a flash usa, não ligue',
              'GPIO6': 'SCLK da flash (GPIO6): a flash usa, não ligue'}
ESQ_12 = ['RST', 'ADC', 'CH_PD', 'GPIO16', 'GPIO14', 'GPIO12', 'GPIO13', 'VCC']           # de cima para baixo
DIR_12 = ['TXD', 'RXD', 'GPIO5', 'GPIO4', 'GPIO0', 'GPIO2', 'GPIO15', 'GND']             # de cima para baixo

DENTRO = {'esq': (1, 0), 'dir': (-1, 0), 'cima': (0, 1), 'baixo': (0, -1)}


# =====================================================================
#  as pecas comuns
# =====================================================================
def castelado(d, x, y, lado, comp=1.5, larg=1.0, furo=0.62):
    E.ilha_castelada(d, x, y, lado, comp=comp, larg=larg, furo=furo)


def solda_pino(d, x, y, quadrada=False, anel=1.7):
    """O pino da barra soldado por baixo, visto de cima: a ilha de estanho, o
    morrinho de solda e a ponta quadrada do pino."""
    if quadrada:
        d.ret_c(x, y, anel, anel, ESTANHO, rx=0.1)
    else:
        d.circ(x, y, anel / 2, ESTANHO)
    d.circ(x, y, anel * 0.36, '#c4c8cc')
    d.circ(x, y, anel * 0.36, 'url(#cupula)')
    d.ret_c(x, y, 0.62, 0.62, '#a9adb2', rx=0.06)
    d.ret_c(x, y, 0.62, 0.62, 'url(#luz)', rx=0.06)


def rota(a, b, eixo):
    """O caminho de a ate b com uma dobra de 45 graus, comecando pelo eixo
    ('x' ou 'y'): o reto e depois a diagonal (ou a diagonal e depois o reto,
    quando o outro eixo anda mais)."""
    (x1, y1), (x2, y2) = a, b
    dx, dy = x2 - x1, y2 - y1
    if eixo == 'y':
        if abs(dy) >= abs(dx):
            return [(x1, y1), (x1, y2 - math.copysign(abs(dx), dy)), (x2, y2)]
        return [(x1, y1), (x1 + math.copysign(abs(dy), dx), y2), (x2, y2)]
    if abs(dx) >= abs(dy):
        return [(x1, y1), (x2 - math.copysign(abs(dy), dx), y1), (x2, y2)]
    return [(x1, y1), (x2, y1 + math.copysign(abs(dx), dy)), (x2, y2)]


def leque(placa, pinos, cx, cy, meio, comp=1.5, larg=0.25):
    """As trilhas das ilhas ate o chip (o QFN em cx, cy de meio lado `meio`):
    cada lado da placa chega num lado do chip, na ordem, 0,5 mm entre uma e
    outra: um trecho reto, a diagonal e o reto final ate o pino. Quando nao
    cabe, a trilha desce numa via."""
    por_lado = {}
    for nome, x, y, lado in pinos:
        if lado in DENTRO:
            por_lado.setdefault(lado, []).append((x, y))
    for lado, pts in por_lado.items():
        dx, dy = DENTRO[lado]
        horizontal = lado in ('cima', 'baixo')          # a fila anda em x
        pts.sort(key=lambda p: p[0] if horizontal else p[1])
        n = len(pts)

        def ponto(ao_longo, fundo):
            return (ao_longo, fundo) if horizontal else (fundo, ao_longo)

        for k, (x, y) in enumerate(pts):
            ao_longo = x if horizontal else y
            borda = y if horizontal else x
            sentido = dy if horizontal else dx
            alvo = (cx if horizontal else cy) + (k - (n - 1) / 2) * 0.5
            f0 = borda + sentido * comp                       # a ponta de dentro da ilha
            f_fim = (cy if horizontal else cx) - sentido * (meio + 0.3)
            total = abs(f_fim - f0)
            desvio = alvo - ao_longo
            if abs(desvio) + 0.5 > total:
                passo = max(total - 0.5, 0.3)
                fim = ponto(ao_longo + math.copysign(min(passo, abs(desvio)), desvio), f0 + sentido * (0.5 + passo))
                placa.trilha([ponto(ao_longo, f0), ponto(ao_longo, f0 + sentido * 0.5), fim], larg)
                placa.via(*fim)
                continue
            placa.trilha([ponto(ao_longo, f0), ponto(ao_longo, f0 + sentido * 0.5),
                          ponto(alvo, f0 + sentido * (0.5 + abs(desvio))), ponto(alvo, f_fim)], larg)


def qfn_esp(d, cx, cy, linhas=('ESP8266EX',)):
    E.qfn(d, cx, cy, 5.0, 8, list(linhas))


def antena_ceramica(d, cx, cy, comp, larg, vertical=False):
    """A antena de ceramica (o bloco creme com a ponta de alimentacao marcada)."""
    if vertical:
        d.entrar(mover(cx, cy) * rodar(90))
        cx, cy = 0, 0
    E.antena_ceramica(d, cx, cy, comp, larg)
    d.circ(cx + comp / 2 - 1.2, cy, min(0.32, larg * 0.2), '#b8433a')
    if vertical:
        d.sair()


def antena_12(placa, d, x, y, w, h):
    """A antena de trilha das ESP-12 e da ESP-13 (o F invertido em meandro),
    debaixo da mascara: o pe na esquerda e o zigue-zague ate a direita."""
    cor = placa.trilha_cor
    larg = 0.5
    base = y + h - 0.5
    topo = y + 0.6
    d.linha(x + 0.9, base, x + 0.9, topo, cor, larg, cap='butt')
    d.linha(x + 2.1, base, x + 2.1, topo + 1.5, cor, larg, cap='butt')
    d.linha(x + 0.65, topo, x + 3.6, topo, cor, larg, cap='butt')
    pts = [(x + 3.6, topo)]
    xx = x + 3.6
    n = 7
    passo = (w - 4.4) / n
    for k in range(n):
        y_fim = base - 1.3 if k % 2 == 0 else topo
        pts.append((xx, y_fim))
        xx += passo
        pts.append((xx, y_fim))
    d.poli(pts, None, stroke=cor, sw=larg, fechado=False, cap='butt')
    d.linha(x + 0.4, base, x + 2.6, base, cor, larg, cap='butt')


def blindagem_esp(d, x, y, w, h, modelo, linhas=None):
    linhas = linhas or [('ESP8266MOD', 1.0), (modelo, 0.8), ('ISM 2.4GHz', 0.7), ('PA +25dBm', 0.7), ('802.11b/g/n', 0.7)]
    E.blindagem(d, x, y, w, h, linhas)


def fila(nomes, x, y0, passo, lado):
    """Uma fila de ilhas: x fixo (esq/dir, y0 e o primeiro y) ou y fixo
    (cima/baixo: x e o y da borda, y0 o primeiro x)."""
    if lado in ('esq', 'dir'):
        return [(n, x, y0 + k * passo, lado) for k, n in enumerate(nomes)]
    return [(n, y0 + k * passo, x, lado) for k, n in enumerate(nomes)]


# =====================================================================
#  o quadro de 16 x 24 (ESP-12, 12E, 12F e ESP-14)
# =====================================================================
def desenhar_12(d, X0, Y0, modelo, com_flash, esq=ESQ_12, dir_=DIR_12, embaixo=FLASH_12E, gravacao=None, com_led=True):
    """O modulo no quadro de 16 x 24 mm (o canto de cima a esquerda em X0, Y0):
    a antena de trilha em cima, a blindagem, as ilhas casteladas de 2,0 mm e
    o LED azul (o do GPIO2) a direita, entre a antena e a blindagem."""
    placa = E.Placa(d, 'preta')
    placa.contorno(X0, Y0, 16.0, 24.0, rx=0.3)
    antena_12(placa, d, X0 + 0.6, Y0 + 0.4, 14.6, 5.9)
    pinos = fila(esq, X0, Y0 + 8.4, 2.0, 'esq') + fila(dir_, X0 + 16.0, Y0 + 8.4, 2.0, 'dir')
    if com_flash:
        pinos += fila(embaixo, Y0 + 24.0, X0 + 3.0, 2.0, 'baixo')
    for _, x, y, lado in pinos:
        castelado(d, x, y, lado)
    alto = 14.6 if com_flash else 15.4
    blindagem_esp(d, X0 + 2.0, Y0 + 7.6, 12.0, alto, modelo, gravacao)
    xl, yl = X0 + 13.4, Y0 + 6.75
    if com_led:
        E.passivo(d, X0 + 11.4, yl, '0402', 'R')
        E.led_smd(d, xl, yl, AZUL_LED, '0603')
    return pinos, (xl, yl)


for id_peca, modelo, flash in (('esp-12', 'ESP-12', False), ('esp-12e', 'ESP-12E', True), ('esp-12f', 'ESP-12F', True)):
    L, H = E.caixa(16.4, 24.4)
    d = E.novo()
    pinos, (xl, yl) = desenhar_12(d, 0.2, 0.2, modelo, flash)
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, catalogo(pinos, DESC_FLASH))
    led(id_peca, xl, yl, 'LedInvertido + 2', 'azul', r=0.8)

# O ESP-14: o mesmo quadro de 22 ilhas, mas os pinos de lado e de baixo sao do
# STM8S003 que vai dentro (a tabela da XESS); do ESP8266 so sai o GPIO0.
ESQ_14 = ['PC5', 'PC6', 'PC7', 'SWIM', 'PD3', 'PD4', 'NRST', 'GND']
DIR_14 = ['PC4', 'PC3', 'PB4', 'PB5', 'PA3', 'VCC', 'VDD', 'VCAP']                   # de cima (22) para baixo (15)
BAIXO_14 = ['PD2', 'PD5', 'PD6', 'PA1', 'PA2', 'GPIO0']
stm8 = 'Pino do STM8S003 de dentro do ESP-14 (o microcontrolador que conversa com o ESP8266 pela serial)'
desc14 = {n: stm8 for n in ESQ_14 + DIR_14 + BAIXO_14}
desc14.update({'GND': 'GND', 'VCC': 'Alimentação do ESP8266 (E_VDD): 3,3 V', 'VDD': 'Alimentação do STM8S003 (M_VDD): 3,3 V',
               'VCAP': 'O capacitor do regulador do STM8S003', 'NRST': 'Reset do STM8S003',
               'SWIM': 'A programação do STM8S003 (SWIM)',
               'GPIO0': 'O único pino do ESP8266 para fora (E_GPIO0): em LOW no boot, grava'})
L, H = E.caixa(16.4, 24.4)
d = E.novo()
pinos, _ = desenhar_12(d, 0.2, 0.2, 'ESP-14', True, esq=ESQ_14, dir_=DIR_14, embaixo=BAIXO_14,
                       gravacao=[('ESP8266MOD', 1.0), ('ESP-14', 0.8), ('STM8S003 INSIDE', 0.7), ('802.11b/g/n', 0.7)],
                       com_led=False)
gravar('esp-14', d, L, H)
guardar('esp-14', L, H, catalogo(pinos, desc14))


# =====================================================================
#  o ESP-07 (16 x 22, azul, antena ceramica e U.FL) e o ESP-08 (16 x 18)
# =====================================================================
def modulo_07(id_peca, modelo, cor, H_mod, com_antena):
    L, H = E.caixa(16.4, H_mod + 0.4)
    X0, Y0 = 0.2, 0.2
    y1 = H_mod - 15.6                        # as ilhas param a 1,6 mm da borda de baixo
    d = E.novo()
    placa = E.Placa(d, cor)
    placa.contorno(X0, Y0, 16.0, H_mod, rx=0.3)
    pinos = fila(ESQ_12, X0, Y0 + y1, 2.0, 'esq') + fila(DIR_12, X0 + 16.0, Y0 + y1, 2.0, 'dir')
    yb = Y0 + y1 - 0.8
    if com_antena:
        # a foto do ESP-07 v2: o U.FL no canto, a ceramica no resto da faixa de cima
        placa.trilha([(X0 + 3.3, Y0 + 2.4), (X0 + 5.4, Y0 + 2.4)], 0.4)
        placa.trilha([(X0 + 1.85, Y0 + 3.9), (X0 + 1.85, Y0 + 4.8), (X0 + 3.0, yb)], 0.4)
        for vx in (6.0, 9.2, 12.6):
            placa.via(X0 + vx, Y0 + 3.4)
        antena_ceramica(d, X0 + 10.0, Y0 + 1.9, 9.4, 1.8)
        E.conector_ufl(d, X0 + 1.85, Y0 + 2.4)
        E.passivo(d, X0 + 4.4, Y0 + 4.4, '0402', 'C')
        E.passivo(d, X0 + 10.6, Y0 + 4.4, '0402', 'R')
        E.passivo(d, X0 + 12.2, Y0 + 4.4, '0402', 'R')
        E.led_smd(d, X0 + 7.7, Y0 + 4.4, AZUL_LED, '0603')
        E.led_smd(d, X0 + 14.0, Y0 + 4.2, VERMELHO_LED, '0603')
        led(id_peca, X0 + 7.7, Y0 + 4.4, 'LedInvertido + 2', 'azul', r=0.8)
        led(id_peca, X0 + 14.0, Y0 + 4.2, 'LedDeEnergia', 'vermelho', r=0.8)
    else:
        pinos.append(('ANT', X0 + 8.0, Y0, 'cima'))                 # a ilha da antena de fora
        placa.trilha([(X0 + 8.0, Y0 + 1.5), (X0 + 8.0, yb)], 0.4)
    for _, x, y, lado in pinos:
        castelado(d, x, y, lado)
    blindagem_esp(d, X0 + 2.0, yb, 12.0, Y0 + H_mod - 0.7 - yb, modelo)
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, catalogo(pinos))


modulo_07('esp-07', 'ESP-07', 'azul-esp', 22.0, True)
modulo_07('esp-08', 'ESP-08', 'preta', 18.0, False)

# =====================================================================
#  o ESP-13 (o do WROOM-02: 18 x 20, 18 ilhas de 1,5 mm)
# =====================================================================
L, H = E.caixa(18.4, 20.4)
X0, Y0 = 0.2, 0.2
ESQ_13 = ['VCC', 'CH_PD', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0', 'GND']
DIR_13 = ['GND.2', 'GPIO16', 'ADC', 'RST', 'GPIO5', 'GND.3', 'TXD', 'RXD', 'GPIO4']      # de cima (18) para baixo (10)
d = E.novo()
placa = E.Placa(d, 'preta')
placa.contorno(X0, Y0, 18.0, 20.0, rx=0.3)
antena_12(placa, d, X0 + 0.8, Y0 + 0.4, 16.4, 5.6)
pinos = fila(ESQ_13, X0, Y0 + 7.0, 1.5, 'esq') + fila(DIR_13, X0 + 18.0, Y0 + 7.0, 1.5, 'dir')
for _, x, y, lado in pinos:
    castelado(d, x, y, lado, comp=1.3, larg=0.85, furo=0.55)
blindagem_esp(d, X0 + 1.8, Y0 + 6.3, 14.4, 13.1, 'ESP-13')
gravar('esp-13', d, L, H)
guardar('esp-13', L, H, catalogo(pinos))


# =====================================================================
#  os sem blindagem: a placa, as trilhas, as ilhas e as pecas por cima
# =====================================================================
def modulo_aberto(Lm, Hm, cor, pinos_def, chip, flash, cristal, pecas_soltas=(), vias=(),
                  antes=None, depois=None, tht=None, comp=1.5, larg=1.0, furo=0.62):
    """chip: (cx, cy) do ESP8266EX; flash: (cx, cy, vertical) do SOIC-8;
    cristal: (cx, cy, vertical); pecas_soltas: (x, y, tam, tipo, vertical);
    tht: (furo, anel) para as ilhas de furo; antes/depois: funcoes
    (d, placa, X0, Y0) que desenham por baixo / por cima das pecas."""
    L, H = E.caixa(Lm + 0.4, Hm + 0.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, cor)
    placa.contorno(X0, Y0, Lm, Hm, rx=0.3)
    pinos = [(n, X0 + x, Y0 + y, lado) for n, x, y, lado in pinos_def]
    cx, cy = X0 + chip[0], Y0 + chip[1]
    # o cobre: o leque das ilhas ate o chip, a flash e o cristal ligados, as vias
    leque(placa, pinos, cx, cy, 2.5, comp=comp if tht is None else tht[1] / 2)
    fx, fy, _ = flash
    placa.trilha(rota((X0 + fx, Y0 + fy), (cx, cy), 'x'), 0.25)
    kx, ky, _ = cristal
    placa.trilha(rota((X0 + kx, Y0 + ky), (cx, cy), 'y'), 0.3)
    for vx, vy in vias:
        placa.via(X0 + vx, Y0 + vy)
    if antes:
        antes(d, placa, X0, Y0)
    # as ilhas
    for nome, x, y, lado in pinos:
        if tht:
            E.ilha_tht(d, x, y, tht[0], tht[1], quadrada=nome.split('.')[0] == 'GND')
        else:
            castelado(d, x, y, lado, comp=comp, larg=larg, furo=furo)
    # as pecas
    qfn_esp(d, cx, cy)
    E.soic(d, X0 + fx, Y0 + fy, 8, ['25Q80'], vertical=flash[2])
    E.cristal_smd(d, X0 + kx, Y0 + ky, vertical=cristal[2])
    for x, y, tam, tipo, vertical in pecas_soltas:
        E.passivo(d, X0 + x, Y0 + y, tam, tipo, vertical=vertical)
    if depois:
        depois(d, placa, X0, Y0)
    return d, L, H, pinos


def fechar(id_peca, d, L, H, pinos, extra=None):
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, catalogo(pinos, extra))


# ---- ESP-01: 14,3 x 24,8, azul; a barra 2x4 soldada por baixo (de cima, as
# soldas), a flash e o ESP8266EX lado a lado, o cristal, os dois LEDs (o
# vermelho da energia, o azul do TX) e a antena de trilha embaixo ----
def esp01():
    L, H = E.caixa(14.7, 25.2)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'azul-esp')
    placa.contorno(X0, Y0, 14.3, 24.8, rx=0.3)
    xs = [X0 + 3.6 + k * 2.54 for k in range(4)]
    y_fora, y_dentro = Y0 + 1.1, Y0 + 1.1 + 2.54
    fora = ['VCC', 'RST', 'CH_PD', 'TXD']
    dentro = ['RXD', 'GPIO0', 'GPIO2', 'GND']
    cx, cy = X0 + 9.75, Y0 + 10.05
    # o cobre: a fila de dentro desce ao chip (as duas da esquerda numa via),
    # a de fora passa entre as ilhas e desce numa via
    for k, x in enumerate(xs):
        if k < 2:
            placa.trilha([(x, y_dentro), (x, y_dentro + 1.6)], 0.25)
            placa.via(x, y_dentro + 1.6)
        else:
            alvo = cx - 0.25 + (k - 2) * 0.5
            placa.trilha(rota((x, y_dentro), (alvo, cy - 2.8), 'y'), 0.25)
        placa.trilha([(x, y_fora), (x + 1.27, y_fora + 1.27), (x + 1.27, Y0 + 5.4)], 0.2)
        placa.via(x + 1.27, Y0 + 5.4)
    for k in range(4):                                                     # a flash ate o chip
        y = Y0 + 8.6 + k * 0.5
        placa.trilha([(X0 + 5.6, Y0 + 8.4 + k * 0.6), (X0 + 6.3, y), (cx - 2.8, y)], 0.2)
    placa.trilha(rota((X0 + 6.6, Y0 + 15.5), (cx - 1.0, cy + 2.8), 'x'), 0.25)
    for vx, vy in ((13.6, 2.3), (13.6, 12.3), (13.0, 12.9), (6.5, 12.9), (8.4, 14.1), (3.3, 14.9), (2.1, 16.3),
                   (9.8, 16.0), (11.6, 16.4)):
        placa.via(X0 + vx, Y0 + vy)
    # a antena (debaixo da mascara): o braco de cima, o pe na direita, a linha
    # de baixo e o meandro
    cor = placa.trilha_cor
    d.linha(X0 + 5.5, Y0 + 17.7, X0 + 13.0, Y0 + 17.7, cor, 0.5, cap='butt')
    d.linha(X0 + 13.0, Y0 + 14.8, X0 + 13.0, Y0 + 23.6, cor, 0.5, cap='butt')
    d.linha(X0 + 1.6, Y0 + 23.6, X0 + 13.25, Y0 + 23.6, cor, 0.5, cap='butt')
    pts = [(X0 + 12.2, Y0 + 23.6)]
    x = X0 + 12.2
    for k in range(7):
        y_a, y_b = (Y0 + 23.0, Y0 + 19.8) if k % 2 == 0 else (Y0 + 19.8, Y0 + 23.0)
        pts += [(x, y_b), (x - 1.1, y_b)]
        x -= 1.1
    d.poli(pts, None, stroke=cor, sw=0.5, fechado=False, cap='butt')
    # as soldas da barra
    for n, x in zip(fora, xs):
        solda_pino(d, x, y_fora)
    for n, x in zip(dentro, xs):
        solda_pino(d, x, y_dentro, quadrada=n in ('RXD', 'GND'))
    # as pecas
    E.soic(d, X0 + 3.3, Y0 + 10.35, 8, ['25Q40', 'B1'])
    qfn_esp(d, cx, cy, ('ESP8266EX', '30201'))
    E.cristal_smd(d, X0 + 6.6, Y0 + 15.5)
    for x, y, tam, tipo, vertical in ((13.7, 9.4, '0402', 'C', True), (4.3, 15.6, '0402', 'C', True),
                                      (8.7, 15.5, '0402', 'C', True), (10.2, 14.7, '0402', 'R', False),
                                      (11.7, 14.3, '0402', 'C', False), (11.7, 15.3, '0402', 'C', False),
                                      (13.8, 14.7, '0402', 'C', True)):
        E.passivo(d, X0 + x, Y0 + y, tam, tipo, vertical=vertical)
    E.led_smd(d, X0 + 1.1, Y0 + 17.3, VERMELHO_LED, '0603', vertical=True)
    E.passivo(d, X0 + 2.15, Y0 + 17.3, '0402', 'R', vertical=True)
    E.passivo(d, X0 + 3.25, Y0 + 17.3, '0402', 'R', vertical=True)
    E.led_smd(d, X0 + 4.3, Y0 + 17.3, AZUL_LED, '0603', vertical=True)
    led('esp-01', X0 + 1.1, Y0 + 17.3, 'LedDeEnergia', 'vermelho', r=0.8)
    led('esp-01', X0 + 4.3, Y0 + 17.3, 'LedTx', 'azul', r=0.8)
    gravar('esp-01', d, L, H)
    guardar('esp-01', L, H, catalogo([(n, x, y_fora, '') for n, x in zip(fora, xs)] +
                                     [(n, x, y_dentro, '') for n, x in zip(dentro, xs)]))


esp01()

# ---- ESP-02: 14,2 x 14,2, vermelho, oito castelados de 2,54 mm (o desenho
# da placa na l0l): o U.FL no canto, o cristal em cima, a flash na direita ----
d, L, H, pinos = modulo_aberto(
    14.2, 14.2, 'vermelha',
    fila(['RST', 'GPIO2', 'GPIO0', 'GPIO15'], 0.0, 5.6, 2.54, 'cima') +
    fila(['GND', 'RXD', 'TXD', 'VCC'], 14.2, 5.6, 2.54, 'baixo'),
    chip=(5.1, 8.0), flash=(11.35, 7.8, False), cristal=(7.9, 2.6, False),
    pecas_soltas=((1.2, 4.9, '0402', 'C', True), (2.2, 4.9, '0402', 'C', True), (1.4, 11.8, '0402', 'R', False),
                  (3.0, 12.3, '0402', 'C', False), (8.9, 12.0, '0402', 'R', True)),
    vias=((1.4, 7.0), (1.3, 9.2), (4.3, 4.4), (9.3, 4.6), (9.9, 11.8), (6.8, 12.6), (13.4, 3.2)),
    antes=lambda d, placa, X0, Y0: placa.trilha([(X0 + 2.0, Y0 + 3.3), (X0 + 2.0, Y0 + 4.9), (X0 + 3.1, Y0 + 6.0)], 0.4),
    depois=lambda d, placa, X0, Y0: E.conector_ufl(d, X0 + 2.0, Y0 + 1.8), comp=1.2, larg=1.1, furo=0.9)
fechar('esp-02', d, L, H, pinos)

# ---- ESP-03: 12,2 x 17,3, azul, a ceramica em cima, 7 + 7 castelados de 2,0 mm ----
d, L, H, pinos = modulo_aberto(
    12.2, 17.3, 'azul-esp',
    fila(['VCC', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0'], 0.0, 4.05, 2.0, 'esq') +
    fila(['ANT', 'CH_PD', 'GPIO16', 'RXD', 'TXD', 'NC', 'GND'], 12.2, 4.05, 2.0, 'dir'),
    chip=(4.95, 7.85), flash=(4.9, 14.4, True), cristal=(9.5, 11.5, True),
    pecas_soltas=((5.9, 3.4, '0402', 'C', False), (1.75, 4.8, '0402', 'C', True), (8.95, 5.8, '0402', 'C', True),
                  (10.4, 5.8, '0402', 'C', True), (9.9, 7.15, '0402', 'R', False), (9.9, 8.0, '0402', 'C', False),
                  (10.35, 14.9, '0402', 'C', True)),
    vias=((3.7, 3.8), (2.4, 4.3), (6.3, 4.5), (7.8, 5.4), (8.65, 9.2), (9.45, 9.6), (10.1, 9.4), (2.0, 10.9),
          (2.8, 11.2), (4.3, 11.3), (8.2, 16.4)),
    antes=lambda d, placa, X0, Y0: placa.trilha([(X0 + 10.7, Y0 + 2.0), (X0 + 10.7, Y0 + 4.05)], 0.4),
    depois=lambda d, placa, X0, Y0: antena_ceramica(d, X0 + 5.95, Y0 + 1.4, 9.9, 1.45))
fechar('esp-03', d, L, H, pinos)

# ---- ESP-04: 14,7 x 12,1, azul, sem antena, 7 + 7 castelados em cima e embaixo ----
d, L, H, pinos = modulo_aberto(
    14.7, 12.1, 'azul-esp',
    fila(['ANT', 'CH_PD', 'GPIO16', 'RXD', 'TXD', 'NC', 'GND'], 0.0, 1.35, 2.0, 'cima') +
    fila(['VCC', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0'], 12.1, 1.35, 2.0, 'baixo'),
    chip=(5.1, 6.5), flash=(11.5, 7.0, False), cristal=(8.75, 2.9, False),
    pecas_soltas=((1.3, 4.6, '0402', 'C', True), (1.3, 6.4, '0402', 'C', True), (1.3, 8.2, '0402', 'R', True),
                  (11.0, 3.3, '0402', 'C', True), (12.3, 3.3, '0402', 'C', True)),
    vias=((2.2, 3.6), (8.2, 4.8), (8.6, 8.6), (2.4, 9.7), (13.5, 5.4), (13.6, 9.6)),
    comp=1.2, larg=0.9)
fechar('esp-04', d, L, H, pinos)

# ---- ESP-05: 14,2 x 14,2, azul, cinco furos de 2,54 mm em cima e o furo de
# GND do canto (a foto da l0l), o U.FL embaixo a direita ----
d, L, H, pinos = modulo_aberto(
    14.2, 14.2, 'azul-esp',
    [(n, 1.9 + k * 2.54, 1.5, 'cima') for k, n in enumerate(['VCC', 'TXD', 'RXD', 'GND', 'RST'])] +
    [('GND.2', 2.0, 12.4, '')],
    chip=(9.6, 6.0), flash=(4.0, 7.6, False), cristal=(8.4, 11.4, False),
    pecas_soltas=((12.9, 3.6, '0402', 'C', True), (12.9, 8.2, '0402', 'C', True), (6.0, 11.4, '0402', 'R', True),
                  (10.6, 9.4, '0402', 'L', False)),
    vias=((6.6, 3.7), (12.6, 6.0), (6.9, 9.5), (3.5, 10.6), (11.0, 13.3)),
    antes=lambda d, placa, X0, Y0: placa.trilha([(X0 + 12.0, Y0 + 10.4), (X0 + 11.4, Y0 + 8.6)], 0.4),
    depois=lambda d, placa, X0, Y0: E.conector_ufl(d, X0 + 12.0, Y0 + 11.9), tht=(0.9, 1.6))
fechar('esp-05', d, L, H, pinos, {'GND.2': 'GND (o furo do canto)'})

# ---- ESP-10: 14,2 x 10, vermelho, cinco castelados de 2,0 mm na direita ----
d, L, H, pinos = modulo_aberto(
    14.2, 10.0, 'vermelha',
    fila(['RST', 'VCC', 'TXD', 'RXD', 'GND'], 14.2, 1.0, 2.0, 'dir'),
    chip=(4.7, 4.75), flash=(10.4, 6.1, False), cristal=(7.7, 1.5, False),
    pecas_soltas=((0.9, 1.4, '0402', 'C', True), (0.9, 8.6, '0402', 'C', False), (10.6, 1.0, '0402', 'R', False),
                  (12.0, 1.0, '0402', 'C', False)),
    vias=((1.0, 3.2), (1.2, 6.0), (8.2, 3.6), (8.6, 9.1), (3.0, 9.2)),
    comp=1.2, larg=0.9)
fechar('esp-10', d, L, H, pinos)

# ---- ESP-11: 17,3 x 12,1 (o wiki do esp8266.com e o Tindie; a proporcao do
# desenho da placa bate com essa, nao com os 19,3 x 13 da tabela da l0l),
# vermelho, a ceramica em pe na esquerda, oito furos de 1,27 mm na direita
# (o desenho da placa no forum do esp8266.com). O TXD e o "GPIO1" sao o
# mesmo pino do chip (o U0TXD): duas ilhas no mesmo fio ----
d, L, H, pinos = modulo_aberto(
    17.3, 12.1, 'vermelha',
    fila(['GND', 'CH_PD', 'RST', 'RXD', 'TXD', 'GPIO0', 'GPIO1', 'VCC'], 16.2, 1.6, 1.27, 'dir'),
    chip=(5.4, 6.1), flash=(12.2, 6.9, False), cristal=(9.4, 1.6, False),
    pecas_soltas=((3.9, 1.2, '0402', 'C', False), (3.6, 11.0, '0402', 'C', False), (9.2, 10.9, '0402', 'R', True),
                  (14.6, 1.6, '0402', 'C', True), (2.9, 2.6, '0402', 'L', True)),
    vias=((3.6, 2.4), (3.2, 9.4), (8.6, 3.5), (8.9, 9.4), (11.0, 10.9), (14.8, 10.6)),
    depois=lambda d, placa, X0, Y0: antena_ceramica(d, X0 + 1.3, Y0 + 6.05, 9.6, 1.6, vertical=True),
    tht=(0.6, 0.95))
fechar('esp-11', d, L, H, pinos)


# =====================================================================
#  os de ilha por baixo: o ESP-06 (blindado) e o ESP-09 (10 x 10)
# =====================================================================
def borda_lga(d, x, y, lado, comp=0.7, larg=0.9):
    """A ponta da ilha de baixo que aparece na borda, vista de cima."""
    dx, dy = DENTRO[lado]
    if dx:
        d.ret_c(x + dx * comp / 2, y, comp, larg, OURO, rx=0.08)
    else:
        d.ret_c(x, y + dy * comp / 2, larg, comp, OURO, rx=0.08)


def pinos_lga(X0, Y0, Lm, Hm, lados, canto):
    cx, cy = X0 + Lm / 2, Y0 + Hm / 2
    pinos = []
    for lado, nomes, passo in lados:
        for k, n in enumerate(nomes):
            o = (k - 1) * passo
            pinos.append((n, *{'esq': (X0, cy + o), 'dir': (X0 + Lm, cy + o),
                               'cima': (cx + o, Y0), 'baixo': (cx + o, Y0 + Hm)}[lado], lado))
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        pinos.append(('GND' if k == 0 else f'GND.{k + 1}', cx + sx * Lm / 2, cy + sy * (Hm / 2 - canto),
                      'esq' if sx < 0 else 'dir'))
    return pinos


def esp06():
    Lm, Hm = 16.3, 13.1
    L, H = E.caixa(Lm + 0.4, Hm + 0.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'preta')
    placa.contorno(X0, Y0, Lm, Hm, rx=0.3)
    pinos = pinos_lga(X0, Y0, Lm, Hm, [('esq', ['ANT', 'VCC', 'CH_PD'], 2.9), ('cima', ['GPIO16', 'TXD', 'RXD'], 2.8),
                                       ('dir', ['GPIO0', 'GPIO2', 'GPIO15'], 2.9),
                                       ('baixo', ['GPIO14', 'GPIO12', 'GPIO13'], 2.8)], 0.9)
    for _, x, y, lado in pinos:
        borda_lga(d, x, y, lado)
    blindagem_esp(d, X0 + 0.9, Y0 + 0.9, Lm - 1.8, Hm - 1.8, 'ESP-06')
    gravar('esp-06', d, L, H)
    guardar('esp-06', L, H, catalogo(pinos, {'ANT': 'A saída para a antena de fora (WIFI_ANT, não ligar)'}))


esp06()


def esp09():
    Lm, Hm = 10.0, 10.0
    L, H = E.caixa(Lm + 0.4, Hm + 0.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'azul-esp')
    placa.contorno(X0, Y0, Lm, Hm, rx=0.3)
    pinos = pinos_lga(X0, Y0, Lm, Hm, [('esq', ['ANT', 'VCC', 'CH_PD'], 2.0), ('cima', ['RST', 'TXD', 'RXD'], 1.8),
                                       ('dir', ['GPIO0', 'GPIO2', 'GPIO15'], 1.8),
                                       ('baixo', ['GPIO14', 'GPIO12', 'GPIO13'], 1.8)], 0.55)
    cx, cy = X0 + 4.0, Y0 + 5.35
    leque(placa, [p for p in pinos if not p[0].startswith('GND')], cx, cy, 2.5, comp=0.7, larg=0.2)
    for vx, vy in ((1.2, 2.0), (7.4, 1.0), (8.8, 3.7), (8.6, 8.4), (1.3, 8.6), (6.9, 9.0)):
        placa.via(X0 + vx, Y0 + vy, 0.5, 0.25)
    for _, x, y, lado in pinos:
        borda_lga(d, x, y, lado, comp=0.6, larg=0.8)
    qfn_esp(d, cx, cy)
    E.cristal_smd(d, X0 + 8.1, Y0 + 1.6, 2.0, 1.6, '')
    E.soic(d, X0 + 8.35, Y0 + 5.6, 8, [], vertical=True, passo=0.5, larg_corpo=2.0)
    for x, y, vertical in ((1.0, 4.4, True), (6.1, 1.2, False), (8.3, 9.0, False), (4.6, 9.0, False)):
        E.passivo(d, X0 + x, Y0 + y, '0402', 'C', vertical=vertical)
    gravar('esp-09', d, L, H)
    guardar('esp-09', L, H, catalogo(pinos, {'ANT': 'A saída para a antena de fora (WIFI_ANT, não ligar)'}))


esp09()


# =====================================================================
#  a NodeMCU DevKit V1.0, do projeto do Altium
# =====================================================================
NODEMCU_DESC = {
    'A0': (['analog'], 'Entrada analógica (ADC): 0 a 3,3 V (o divisor da placa leva para 0 a 1 V)'),
    'RSV': ([], 'Reservado (não ligado)'), 'RSV.2': ([], 'Reservado (não ligado)'),
    'SD3': ([], 'GPIO10 (SD_D3 da flash)'), 'SD2': ([], 'GPIO9 (SD_D2 da flash): a flash usa, não ligue'),
    'SD1': ([], 'GPIO8 (MOSI da flash): a flash usa, não ligue'),
    'CMD': ([], 'GPIO11 (CS0 da flash): a flash usa, não ligue'),
    'SD0': ([], 'GPIO7 (MISO da flash): a flash usa, não ligue'),
    'CLK': ([], 'GPIO6 (SCLK da flash): a flash usa, não ligue'),
    'EN': ([], 'Liga o chip (EN/CH_PD): solto, a placa segura em HIGH'),
    'RST': ([], 'Reset (ativo em LOW): o mesmo do botão RST'),
    'VIN': (['power'], 'Entrada de 5 V (o regulador da placa faz os 3,3 V)'),
    'D0': ([], 'D0 = GPIO16 (o LED azul da placa, aceso em LOW; sem PWM nem interrupção)'),
    'D1': (['i2c', 'pwm'], 'D1 = GPIO5 (o SCL de costume)'),
    'D2': (['i2c', 'pwm'], 'D2 = GPIO4 (o SDA de costume)'),
    'D3': (['pwm'], 'D3 = GPIO0 (o botão FLASH): em LOW no boot, grava'),
    'D4': (['pwm'], 'D4 = GPIO2 (o LED azul do ESP-12E, aceso em LOW): HIGH no boot'),
    'D5': (['spi', 'pwm'], 'D5 = GPIO14 (SCK da HSPI)'),
    'D6': (['spi', 'pwm'], 'D6 = GPIO12 (MISO da HSPI)'),
    'D7': (['spi', 'pwm'], 'D7 = GPIO13 (MOSI da HSPI)'),
    'D8': (['spi', 'pwm'], 'D8 = GPIO15 (CS da HSPI): LOW no boot'),
    'RX': (['usart'], 'RX = GPIO3 (a serial do USB)'),
    'TX': (['usart'], 'TX = GPIO1 (a serial do USB)'),
}


def corpo_nodemcu(d, doc, k, c):
    """O corpo de um componente da NodeMCU, no quadro do CAD (y para cima)."""
    pads = [il for il in doc.ilhas if il['comp'] == k and il['dx'] > 0]
    pxs, pys = [p['x'] for p in pads], [p['y'] for p in pads]
    cx, cy = (min(pxs) + max(pxs)) / 2, (min(pys) + max(pys)) / 2
    deitado = (max(pxs) - min(pxs)) >= (max(pys) - min(pys))
    pad = c['padrao'].upper()
    if pad.startswith(('SMT_C_0402', 'SMT_R_0402')):
        E.passivo(d, cx, cy, '0402', 'R' if '_R_' in pad else 'C', vertical=not deitado)
    elif pad.startswith('SMT_C_0805'):
        E.passivo(d, cx, cy, '0805', 'C', vertical=not deitado)
    elif pad.startswith(('SMT_C_TANTALUM', 'SMT_DIODE_SOD_323')):
        # o tantalo B (o corpo amarelo, a faixa do + na ilha 1) e o diodo
        # SOD-323 (o corpo preto, a faixa do catodo na ilha 1)
        tantalo = pad.startswith('SMT_C_TANTALUM')
        comp_, larg_ = (3.5, 2.8) if tantalo else (1.7, 1.25)
        cor, faixa, lf = ('#d6a419', '#8a5a12', 0.6) if tantalo else ('#202124', '#9ea3a8', 0.25)
        w, h = (comp_, larg_) if deitado else (larg_, comp_)
        E.sombra(d, cx - w / 2, cy - h / 2, w, h)
        d.ret_c(cx, cy, w, h, cor, rx=0.1)
        p1 = next(p for p in pads if p['nome'] == '1')
        o = comp_ / 2 - lf / 2 - 0.15
        if deitado:
            d.ret_c(cx + (o if p1['x'] > cx else -o), cy, lf, h, faixa)
        else:
            d.ret_c(cx, cy + (o if p1['y'] > cy else -o), w, lf, faixa)
        d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.1)
    elif pad.startswith('SMT_LED_0603'):
        E.led_smd(d, cx, cy, AZUL_LED, '0603', vertical=not deitado)
    elif pad.startswith('SMT_SW_PTS_820'):
        w, h = (4.2, 3.2) if deitado else (3.2, 4.2)
        E.sombra(d, cx - w / 2, cy - h / 2, w, h)
        d.ret_c(cx, cy, w, h, '#ece9e0', rx=0.25, stroke='#9a978e', sw=0.06)
        d.circ(cx, cy, 1.05, '#f7f6f1', stroke='#b5b2a8', sw=0.08)
        d.circ(cx, cy, 1.05, 'url(#cupula)')
    elif pad.startswith('USB_MICRO'):
        # o micro-USB: a carcaca de metal com a boca na borda de baixo
        y0, y1 = 52.45, 57.75
        E.sombra(d, cx - 3.8, y0, 7.6, y1 - y0)
        for sx in (-1, 1):
            d.ret_c(cx + sx * 3.65, 54.6, 1.3, 1.7, 'url(#metal)', rx=0.15)
        d.ret(cx - 3.8, y0, 7.6, y1 - y0, 'url(#metal-h)', rx=0.3, stroke='#5d646c', sw=0.08)
        d.ret(cx - 3.3, y0, 6.6, 0.6, '#3d4248')
        d.linha(cx - 3.0, 56.6, cx + 3.0, 56.6, '#8d949b', 0.1)
    elif pad.startswith('CP2102'):
        centro = max(pads, key=lambda p: p['dx'])                       # a ilha de baixo do QFN
        E.qfn(d, centro['x'], centro['y'], 5.0, 7, ['CP2102'])
    elif pad.startswith('SOT-223'):
        # o regulador de 3,3 V: os tres pinos, a aba larga e o corpo entre eles
        tab = max(pads, key=lambda p: p['dx'])
        pequenos = [p for p in pads if p is not tab]
        bx = (pequenos[0]['x'] + tab['x']) / 2
        E.sombra(d, bx - 1.75, tab['y'] - 3.25, 3.5, 6.5)
        d.ret_c(tab['x'] - 0.3, tab['y'], 1.4, 3.0, ESTANHO)
        for p in pequenos:
            d.ret_c(p['x'] + 0.5, p['y'], 1.4, 0.7, ESTANHO)
        d.ret_c(bx, tab['y'], 3.5, 6.5, E.PRETO_CI, rx=0.12)
        d.ret_c(bx, tab['y'], 3.5, 6.5, 'url(#luz)', rx=0.12)
        d.texto(bx, tab['y'], 'NCP1117', 0.62, E.MARCA_CI, ang=90, alinhar='center', peso='normal')
    elif pad.startswith('SMT_TRIODE_NPN'):
        # o S8050 no SOT-23 (a marcacao J3Y), o pino unico para o lado da ilha 3
        p3 = next(p for p in pads if p['nome'] == '3')
        E.sot23(d, cx, cy, 'J3Y', giro=0 if p3['y'] < cy else 180)


def nodemcu():
    import altium_pcb as AP
    doc = AP.PcbDoc(os.path.join(AQUI, 'esp8266-cad', 'NODEMCU_DEVKIT_V1.0.PcbDoc'))
    xs = [v[0] for v in doc.contorno]
    ys = [v[1] for v in doc.contorno]
    xmin, ymax = min(xs), max(ys)
    Lp, Hp = max(xs) - xmin, ymax - min(ys)                 # 25,4 x 48,26
    L, H = E.caixa(Lp + 0.4, Hp + 0.4)
    X0, Y0 = (L - Lp) / 2, (H - Hp) / 2

    def local(x, y):
        return X0 + x - xmin, Y0 + ymax - y

    cad = mover(X0 - xmin, Y0 + ymax) * T(1, 0, 0, -1, 0, 0)      # o quadro do CAD (y para cima)
    nomes = [c['nome'] for c in doc.componentes]
    cabecas = {nomes.index('J1'), nomes.index('J2')}
    d = E.novo()
    placa = E.Placa(d, 'preta')
    furos = [(*local(i['x'], i['y']), i['furo'] / 2) for i in doc.ilhas if i['comp'] == 65535 and i['furo'] > 0]
    placa.contorno(X0, Y0, Lp, Hp, rx=2.286, furos=furos)

    d.entrar(cad)
    # o cobre de cima, as vias e as ilhas (ENIG: douradas)
    for tr in doc.trilhas:
        if tr['camada'] == AP.TOP:
            d.linha(tr['x1'], tr['y1'], tr['x2'], tr['y2'], placa.trilha_cor, max(tr['largura'], 0.12))
    for v in doc.vias:
        placa.via(v['x'], v['y'], v['diametro'], v['furo'])
    for il in doc.ilhas:
        if il['comp'] == 65535 or il['comp'] in cabecas or il['camada'] not in (AP.TOP, AP.MULTI) or il['dx'] <= 0:
            continue
        d.entrar(mover(il['x'], il['y']) * rodar(il['giro']))
        d.ret_c(0, 0, il['dx'], il['dy'], OURO, rx=0.05 if il['forma'] == 2 else min(il['dx'], il['dy']) / 2)
        if il['furo'] > 0:
            d.circ(0, 0, il['furo'] / 2, E.FURO)
        d.sair()
    # a serigrafia: os contornos e os rotulos (os de baixo do modulo ficam escondidos)
    for tr in doc.trilhas:
        if tr['camada'] == AP.TOP_OVERLAY:
            d.linha(tr['x1'], tr['y1'], tr['x2'], tr['y2'], E.SERIGRAFIA, max(tr['largura'], 0.12))
    for ar in doc.arcos:
        if ar['camada'] != AP.TOP_OVERLAY:
            continue
        if ar['raio'] < 0.2:                                        # o ponto do pino 1
            d.circ(ar['cx'], ar['cy'], ar['raio'] + 0.05, E.SERIGRAFIA)
            continue
        a0, a1 = ar['a0'], ar['a1'] if ar['a1'] > ar['a0'] else ar['a1'] + 360
        n = max(4, int((a1 - a0) / 15))
        pts = [(ar['cx'] + ar['raio'] * math.cos(math.radians(a0 + (a1 - a0) * j / n)),
                ar['cy'] + ar['raio'] * math.sin(math.radians(a0 + (a1 - a0) * j / n))) for j in range(n + 1)]
        d.poli(pts, None, stroke=E.SERIGRAFIA, sw=max(ar['largura'], 0.12), fechado=False, cap='round')
    rotulos = {'RST', 'FLASH', '3V3', 'GND', 'TX', 'RX', 'Vin', 'A0', 'RSV', 'SD3', 'SD2', 'SD1', 'CMD', 'SD0', 'CLK',
               'EN', 'D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'D8'}
    # os y das ilhas das barras: o rotulo de cada pino fica centrado nele
    ys_barras = sorted({round(il['y'], 3) for il in doc.ilhas if il['comp'] in cabecas})
    for t in doc.textos:
        if t['camada'] == AP.TOP_OVERLAY and t['comp'] == 65535 and t['texto'].strip() in rotulos:
            # a fonte de traco do Altium e fina e estreita: a fonte normal de
            # 0,8 mm, centrada no pino, deixa o mesmo respiro entre os rotulos
            texto = t['texto'].strip()
            y_pino = min(ys_barras, key=lambda y: abs(y - t['y']))
            if t['giro'] % 180 == 90 and abs(y_pino - t['y']) < 2.0:
                d.texto(t['x'], y_pino, texto, 0.8, E.SERIGRAFIA, ang=t['giro'] % 360, alinhar='bottom-center',
                        peso='normal')
            else:
                d.texto(t['x'], t['y'], texto, 1.0, E.SERIGRAFIA, ang=t['giro'] % 360, peso='normal')
    # os corpos
    for k, c in enumerate(doc.componentes):
        if c['lado'] == 'TOP' and c['nome'] != 'U1':
            corpo_nodemcu(d, doc, k, c)
    d.sair()

    # as soldas das duas barras (as barras vao por baixo da placa)
    pinos_cad = {}
    for k in sorted(cabecas):
        for il in doc.ilhas:
            if il['comp'] == k:
                pinos_cad[(nomes[k], int(il['nome']))] = (il['x'], il['y'], il['forma'] == 2)
    for x, y, quadrada in pinos_cad.values():
        solda_pino(d, *local(x, y), quadrada=quadrada)

    # o ESP-12E, com a mesma cara do modulo solto: a borda esquerda a 0,7 mm
    # da ilha 1, o topo 8,4 mm acima dela (o footprint ESP_12_E_L)
    k_u1 = nomes.index('U1')
    pads_u1 = [il for il in doc.ilhas if il['comp'] == k_u1]
    p1 = next(p for p in pads_u1 if p['nome'] == '1')
    mx, my = local(p1['x'] - 0.7, p1['y'] + 8.4)
    _, (xl, yl) = desenhar_12(d, mx, my, 'ESP-12E', True)

    gravar('esp8266-nodemcu', d, L, H)
    esq = ['A0', 'RSV', 'RSV.2', 'SD3', 'SD2', 'SD1', 'CMD', 'SD0', 'CLK', 'GND', '3V3', 'EN', 'RST', 'GND.2', 'VIN']
    dir_ = ['D0', 'D1', 'D2', 'D3', 'D4', '3V3.2', 'GND.3', 'D5', 'D6', 'D7', 'D8', 'RX', 'TX', 'GND.4', '3V3.3']
    pinos = []
    for j, lista in (('J1', esq), ('J2', dir_)):
        for k, n in enumerate(lista, 1):
            x, y = local(*pinos_cad[(j, k)][:2])
            base = n.split('.')[0]
            if base == 'GND':
                s, t = ['power'], 'GND'
            elif base == '3V3':
                s, t = ['power'], 'Saída de 3,3 V do regulador da placa'
            else:
                s, t = NODEMCU_DESC[n]
            pinos.append((n, x, y, s, t))
    guardar('esp8266-nodemcu', L, H, pinos)
    # os LEDs (o do modulo no GPIO2, o da placa no GPIO16) e os dois botoes
    led('esp8266-nodemcu', xl, yl, 'LedInvertido + 2', 'azul', r=0.8)
    l1 = doc.componentes[nomes.index('LED1')]
    led('esp8266-nodemcu', *local(l1['x'], l1['y']), 'LedInvertido + 16', 'azul', r=0.8)
    for nome_c, rot in (('S1', 'RST'), ('S2', 'FLASH')):
        s = doc.componentes[nomes.index(nome_c)]
        x, y = local(s['x'], s['y'])
        botoes.append(('esp8266-nodemcu', x * A, y * A, 1.3 * A, rot))


nodemcu()

# =====================================================================
print('\nleds (EditorCircuito.LedsPorPlaca):')
for p, x, y, r, cod, cor in leds:
    print(f'  {p}: ({x:.1f}, {y:.1f}, {r:.1f}, {cod}, {cor})')
print('botoes (EditorCircuito.BotoesPorPlaca):')
for p, x, y, r, nome in botoes:
    print(f'  {p}: ({x:.1f}, {y:.1f}, {r:.1f}, "{nome}")')

cam = pa.BASE + 'app/assets/pecas.json'
dados = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
dados = [p for p in dados if p['id'] not in novos] + pecas
dados.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(dados)} pecas')
