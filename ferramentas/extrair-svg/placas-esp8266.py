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
# O meandro dos ESP-12E/12F (e do ESP-14), tracado da foto em close de um
# ESP-12F (Wikimedia Commons, "Wemos LoLin NodeMCU V3 - AI-Thinker
# ESP8266MOD", 142 px/mm, retificada pelos furos das ilhas): a linha do meio
# da trilha, com a antena para cima. Comeca na via do pe (10,35; 6,33), corre
# para a esquerda, sobe, faz tres voltas e termina no braco longo de cima.
ANTENA_12 = [(10.35, 6.33), (3.27, 6.33), (3.27, 0.62), (4.40, 0.62), (4.40, 4.42), (5.57, 4.42), (5.57, 0.62),
             (6.72, 0.62), (6.72, 4.48), (7.90, 4.48), (7.90, 0.72), (9.10, 0.72), (9.10, 4.45), (10.27, 4.45),
             (10.27, 0.80), (14.45, 0.80)]


def meandro(d, X0, Y0, pts, cor, larg):
    d.poli([(X0 + x, Y0 + y) for x, y in pts], None, stroke=cor, sw=larg, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)


def ilha_furada(d, x, y, lado, comp=1.5, larg=1.2, furo=0.95, r=0.38):
    """A ilha dos ESP-12 da foto: o quadrado de ouro com o furo metalizado
    (0,76 mm) a 0,95 mm da borda, cheio de solda no fundo."""
    dx, dy = DENTRO[lado]
    w, h = (comp, larg) if dx else (larg, comp)
    d.ret_c(x + dx * comp / 2, y + dy * comp / 2, w, h, OURO, rx=0.08)
    hx, hy = x + dx * furo, y + dy * furo
    d.circ(hx, hy, r, '#9c7c2c')
    d.circ(hx, hy, r - 0.08, '#3b2e14')
    d.circ(hx - 0.07, hy - 0.08, r * 0.42, '#d8c48a', op=0.45)


def tampa_12(d, x, y, w, h, linhas, caixa=None):
    """A tampa de metal dos ESP-12 com a gravacao da foto, girada (lendo de
    baixo para cima, como no modulo de verdade), sem os logotipos.
    linhas: (texto, x da base, y do comeco, tamanho)."""
    d.ret(x + 0.15, y + 0.2, w, h, '#000000', rx=0.35, op=0.32)
    d.ret(x, y, w, h, 'url(#blindagem)', rx=0.35, stroke='#7f868d', sw=0.12)
    d.ret(x + 0.35, y + 0.35, w - 0.7, h - 0.7, None, rx=0.25, stroke='#b4bac0', sw=0.12)
    for t, bx, by, tam in linhas:
        d.texto(bx, by, t, tam, '#62676e', ang=-90, peso='bold')
    if caixa:
        d.ret(*caixa, None, rx=0.1, stroke='#62676e', sw=0.14)


GRAVACAO_12 = [('MODEL', 8.07, 20.75, 0.9), ('ESP8266MOD', 8.07, 15.95, 0.9), ('ISM 2.4GHZ', 10.55, 15.4, 0.9),
               ('PA  +25dBm', 11.7, 15.4, 0.9), ('802.11b/g/n', 12.85, 15.45, 0.9)]
GRAVACAO_14 = [('MODEL', 8.07, 20.75, 0.9), ('ESP-14 MOD', 8.07, 15.95, 0.9), ('STM8S003', 10.6, 15.2, 0.85),
               ('INSIDE', 11.65, 14.6, 0.85), ('802.11b/g/n', 12.85, 15.45, 0.9)]


def desenhar_12(d, X0, Y0, cor='preta', antena_exposta=True, com_flash=True, esq=ESQ_12, dir_=DIR_12, embaixo=FLASH_12E,
                gravacao=GRAVACAO_12, caixa_gravacao=None, com_led=True, larg_antena=0.57):
    """O modulo no quadro de 16 x 24 mm (o canto de cima a esquerda em X0, Y0),
    pela foto em close: a antena em meandro (de ouro exposto nos ESP-12E/F e
    no ESP-14; sob a mascara no ESP-12 original), a via do pe, as ilhas com o
    furo metalizado, o LED 0603 com o quadro de serigrafia a direita da
    antena e a tampa de metal (1,95 a 14,0 por 7,05 a 22,25)."""
    placa = E.Placa(d, cor)
    placa.contorno(X0, Y0, 16.0, 24.0, rx=0.2)
    if antena_exposta:
        meandro(d, X0, Y0, ANTENA_12, '#7a5c1a', larg_antena + 0.08)
        meandro(d, X0, Y0, ANTENA_12, OURO, larg_antena)
        d.circ(X0 + 10.35, Y0 + 6.33, 0.29, OURO)
    else:
        meandro(d, X0, Y0, ANTENA_12, placa.trilha_cor, larg_antena)
        d.circ(X0 + 10.35, Y0 + 6.33, 0.29, placa.trilha_cor)
    d.circ(X0 + 10.35, Y0 + 6.33, 0.11, '#1c1608')
    pinos = fila(esq, X0, Y0 + 8.33, 2.0, 'esq') + fila(dir_, X0 + 16.0, Y0 + 8.33, 2.0, 'dir')
    if com_flash:
        pinos += fila(embaixo, Y0 + 24.0, X0 + 3.0, 2.0, 'baixo')
    for _, x, y, lado in pinos:
        if lado == 'baixo':
            ilha_furada(d, x, y, lado, comp=1.5, larg=1.0, furo=0.9, r=0.34)
        else:
            ilha_furada(d, x, y, lado)
    xl, yl = X0 + 14.1, Y0 + 6.45
    if com_led:
        E.silk_ret(d, X0 + 12.6, Y0 + 5.75, 2.95, 1.4, 0.15)
        E.led_smd(d, xl, yl, AZUL_LED, '0603')
    tampa_12(d, X0 + 1.95, Y0 + 7.05, 12.05, 15.2, [(t, X0 + bx, Y0 + by, tam) for t, bx, by, tam in gravacao],
             caixa=None if not caixa_gravacao else (X0 + caixa_gravacao[0], Y0 + caixa_gravacao[1],
                                                     caixa_gravacao[2], caixa_gravacao[3]))
    return pinos, (xl, yl)


# O ESP-12 original (16 ilhas): a placa azul-petroleo e a antena sob a mascara
# (a foto do wiki do esp8266.com); o ESP-12E e o ESP-12F, a placa preta e a
# antena de ouro (as fotos do Commons: o close do 12F e o ESP-12E).
for id_peca, cor, exposta, flash in (('esp-12', 'petroleo', False, False), ('esp-12e', 'preta', True, True),
                                     ('esp-12f', 'preta', True, True)):
    L, H = E.caixa(16.4, 24.4)
    d = E.novo()
    pinos, (xl, yl) = desenhar_12(d, 0.2, 0.2, cor, exposta, flash)
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, catalogo(pinos, DESC_FLASH))
    led(id_peca, xl, yl, 'LedInvertido + 2', 'azul', r=0.8)

# O ESP-14: o mesmo quadro de 22 ilhas, mas os pinos de lado e de baixo sao do
# STM8S003 que vai dentro (a tabela da XESS); do ESP8266 so sai o GPIO0. A
# foto do wiki: a placa preta, a antena de ouro mais fina, sem o LED, e a
# tampa com "STM8S003 INSIDE" num quadro.
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
pinos, _ = desenhar_12(d, 0.2, 0.2, 'preta', True, True, esq=ESQ_14, dir_=DIR_14, embaixo=BAIXO_14,
                       gravacao=GRAVACAO_14, caixa_gravacao=(9.45, 9.2, 2.45, 6.5), com_led=False, larg_antena=0.42)
gravar('esp-14', d, L, H)
guardar('esp-14', L, H, catalogo(pinos, desc14))





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


# =====================================================================
#  os modulos tracados: o cobre de cima e as vias vetorizados do desenho da
#  placa (a l0l: esp8266-cad/tracado-l0l.json) ou da foto retificada (o azul
#  mais claro da mascara e o cobre: esp8266-cad/tracado-fotos.json), so os
#  vetores; as pecas e a serigrafia medidas na grade de 0,5 mm
# =====================================================================
TRACADO = json.load(io.open(os.path.join(AQUI, 'esp8266-cad', 'tracado-l0l.json'), encoding='utf-8'))
TRACADO.update({'foto:' + k: v for k, v in
                json.load(io.open(os.path.join(AQUI, 'esp8266-cad', 'tracado-fotos.json'), encoding='utf-8')).items()})


def cobre_tracado(d, placa, mod, X0, Y0):
    t = TRACADO[mod]
    for poli in t['cobre']:
        cmds = []
        for anel in poli:
            cmds += [('M', X0 + anel[0][0], Y0 + anel[0][1])] + [('L', X0 + x, Y0 + y) for x, y in anel[1:]] + [('Z',)]
        d.caminho(cmds, placa.trilha_cor)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
    for x, y in t['vias']:
        placa.via(X0 + x, Y0 + y, 0.6, 0.3)


def peca_tracada(d, X0, Y0, p, ilha=OURO):
    """Uma peca medida: a ilha exposta (ouro ou estanho) e o corpo por cima."""
    tipo, x, y = p[0], X0 + p[1], Y0 + p[2]
    if tipo == 'rc':                                   # ('rc', x, y, tam, R/C, vertical, distancia das ilhas)
        _, _, _, tam, rc, vertical, dist = p
        lado = {'0402': (0.55, 0.6), '0603': (0.8, 0.9), '0805': (1.0, 1.35)}[tam]
        for s in (-1, 1):
            if vertical:
                d.ret_c(x, y + s * dist / 2, lado[1], lado[0], ilha, rx=0.05)
            else:
                d.ret_c(x + s * dist / 2, y, lado[0], lado[1], ilha, rx=0.05)
        E.passivo(d, x, y, tam, rc, vertical=vertical)
    elif tipo == 'led':                                # ('led', x, y, cor, vertical)
        _, _, _, cor, vertical = p
        for s in (-1, 1):
            if vertical:
                d.ret_c(x, y + s * 0.75, 0.9, 0.8, ilha, rx=0.05)
            else:
                d.ret_c(x + s * 0.75, y, 0.8, 0.9, ilha, rx=0.05)
        E.led_smd(d, x, y, cor, '0603', vertical=vertical)
    elif tipo == 'ilha':                               # ('ilha', x, y, w, h): a ilha sem peca
        d.ret_c(x, y, p[3], p[4], ilha, rx=0.06)
        d.ret_c(x, y, p[3] * 0.6, p[4] * 0.6, '#c9ccd0', rx=0.1, op=0.7)
    elif tipo == 'cristal':                            # ('cristal', x, y, w, h, dx, dy, texto)
        _, _, _, w, h, dx, dy = p[:7]
        texto = p[7] if len(p) > 7 else '26.000'
        for sx in (-1, 1):
            for sy in (-1, 1):
                d.ret_c(x + sx * dx / 2, y + sy * dy / 2, 0.9 if w > h else 0.8, 0.8 if w > h else 0.9, ilha, rx=0.05)
        E.cristal_smd(d, x, y, w, h, '')
        if texto:
            d.texto(x, y, texto, min(w, h) * 0.28, '#4f565d', ang=-90 if h > w else 0, alinhar='center', peso='normal')
    elif tipo == 'soic':                               # ('soic', x, y, giro, passo, entre filas, linhas[, corpo])
        _, _, _, giro, passo, filas, linhas = p[:7]
        corpo = p[7] if len(p) > 7 else 3.9
        d.entrar(mover(x, y) * rodar(giro))
        for k in range(4):
            for s in (-1, 1):
                d.ret_c(-1.5 * passo + k * passo, s * filas / 2, 0.6, 1.5, ilha, rx=0.05)
        E.soic(d, 0, 0, 8, linhas, larg_corpo=corpo)
        d.sair()
    elif tipo == 'qfn':                                # ('qfn', x, y, linhas[, giro])
        giro = p[4] if len(p) > 4 else 0
        if giro:
            # a marcacao girada como no chip de verdade, lendo de cima para
            # baixo (89,99 graus: o texto nao e virado para "legivel"), a
            # primeira linha a direita; o ponto do pino 1 em cima a direita
            d.entrar(mover(x, y) * rodar(giro))
            E.qfn(d, 0, 0, 5.0, 8, [])
            d.sair()
            n = len(p[3])
            for k, t in enumerate(p[3]):
                d.texto(x + ((n - 1) / 2 - k) * 0.8, y, t, 0.55, E.MARCA_CI, ang=89.99, alinhar='center', peso='normal')
        else:
            E.qfn(d, x, y, 5.0, 8, list(p[3]))
    elif tipo == 'ceramica':                           # ('ceramica', x, y, comprimento, largura, vertical, ponto)
        _, _, _, comp, larg, vertical, ponto = p
        d.entrar(mover(x, y) * rodar(90 if vertical else 0))
        E.antena_ceramica(d, 0, 0, comp, larg)
        d.circ(ponto, 0, min(0.32, larg * 0.18), '#c0473c')
        d.sair()
    elif tipo == 'ufl':
        E.conector_ufl(d, x, y)


def modulo_tracado(id_peca, mod, Lm, Hm, cor, pinos_def, pecas_def, serigrafia=(), circulos=(), extra=None,
                   ilha=(1.0, 1.0, 0.6), acabamento=OURO, tht=None, borda_lga=None, depois=None):
    """tht: (furo, anel) para as ilhas de furo; borda_lga: as ilhas por baixo
    (nao aparecem de cima: so o pino). depois: funcao (d, X0, Y0) por cima."""
    L, H = E.caixa(Lm + 0.4, Hm + 0.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, cor)
    placa.contorno(X0, Y0, Lm, Hm, rx=0.15)
    cobre_tracado(d, placa, mod, X0, Y0)
    pinos = [(n, X0 + x, Y0 + y, lado) for n, x, y, lado in pinos_def]
    comp, larg, furo = ilha
    for nome, x, y, lado in pinos:
        if borda_lga:
            continue
        if tht:
            E.ilha_tht(d, x, y, tht[0], tht[1], quadrada=nome.split('.')[0] == 'GND', acabamento=acabamento)
        else:
            E.ilha_castelada(d, x, y, lado, comp=comp, larg=larg, furo=furo, acabamento=acabamento)
    # a serigrafia vai por baixo das pecas (o circulo do pino 1 fica sob a flash)
    for x1, y1, x2, y2 in serigrafia:
        E.silk_linha(d, X0 + x1, Y0 + y1, X0 + x2, Y0 + y2, 0.13)
    for x, y, r in circulos:
        d.circ(X0 + x, Y0 + y, r, None, E.SERIGRAFIA, 0.13)
    for p in pecas_def:
        peca_tracada(d, X0, Y0, p, ilha=ESTANHO if acabamento != OURO else OURO)
    if depois:
        depois(d, X0, Y0)
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, catalogo(pinos, extra))
    return d


# ---- ESP-01: tracado da foto de cima (Wikimedia Commons, "ESP8266 E-01
# (top)", retificada pelos quatro cantos; 14,3 x 24,8). A barra 2x4 soldada
# por baixo (de cima, as soldas), o ESP8266EX a esquerda e a flash SOIC-8
# larga (208 mil) a direita, o cristal 3225, os dois LEDs (o azul do TX e o
# vermelho de energia) com os resistores no canto de baixo e a antena de metal
# exposto: a moldura, o braco de cima ate a via e as tres voltas. A posicao
# dos pinos: o footprint do kicad-ESP8266 com a numeracao da XESS. ----
ANTENA_01 = [(1.92, 16.3), (1.92, 23.75), (3.04, 23.75), (3.04, 19.89), (4.11, 19.89), (4.11, 23.75), (5.25, 23.75),
             (5.25, 19.89), (6.54, 19.89), (6.54, 23.75), (7.76, 23.75), (7.76, 19.89), (8.89, 19.89), (8.89, 23.61),
             (13.07, 23.61)]


def esp01():
    L, H = E.caixa(14.7, 25.2)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'azul-marinho')
    placa.contorno(X0, Y0, 14.3, 24.8, rx=0.3)
    xs = [X0 + 3.34 + k * 2.54 for k in range(4)]
    y_fora, y_dentro = Y0 + 1.1, Y0 + 3.64
    fora = ['VCC', 'RST', 'CH_PD', 'TXD']
    dentro = ['RXD', 'GPIO0', 'GPIO2', 'GND']
    # o cobre que aparece pela mascara: o plano ao lado do chip, as descidas
    # aos pinos de cima e as vias
    placa.area(X0 + 2.57, Y0 + 4.93, 3.15, 2.4, rx=0.3)
    for x, y1, y2 in ((5.72, 4.45, 4.93), (6.25, 5.6, 7.4), (7.0, 4.45, 7.4)):
        placa.trilha([(X0 + x, Y0 + y1), (X0 + x, Y0 + y2)], 0.25)
    for vx, vy in ((0.35, 0.3), (0.45, 0.85), (1.0, 4.0), (4.0, 5.85), (4.1, 6.6), (1.45, 7.3), (0.8, 12.2), (0.85, 12.9),
                   (0.45, 14.3), (0.5, 15.2), (0.35, 16.2), (1.5, 1.4)):
        placa.via(X0 + vx, Y0 + vy)
    # a antena e o braco de cima (metal exposto) e a via na ponta do braco
    meandro(d, X0, Y0, ANTENA_01, '#8a7438', 0.58)
    meandro(d, X0, Y0, ANTENA_01, OURO, 0.5)
    meandro(d, X0, Y0, [(1.92, 17.7), (9.14, 17.7)], '#8a7438', 0.58)
    meandro(d, X0, Y0, [(1.92, 17.7), (9.14, 17.7)], OURO, 0.5)
    d.circ(X0 + 9.05, Y0 + 17.75, 0.12, '#1c1608')
    # as soldas da barra
    for n, x in zip(fora, xs):
        solda_pino(d, x, y_fora)
    for n, x in zip(dentro, xs):
        solda_pino(d, x, y_dentro)
    # as pecas
    qfn_esp(d, X0 + 4.78, Y0 + 10.22, ('ESP8266EX',))
    d.entrar(mover(X0 + 11.13, Y0 + 10.4))
    for k in range(4):
        for s in (-1, 1):
            d.ret_c(-1.905 + k * 1.27, s * 3.55, 0.6, 1.4, OURO, rx=0.05)
    E.soic(d, 0, 0, 8, ['25Q40', 'BTIG'], larg_corpo=5.3)
    d.sair()
    E.cristal_smd(d, X0 + 6.86, Y0 + 15.75, 3.2, 2.5, '26.000')
    for x, y, tam, tipo, vertical in ((0.65, 9.75, '0805', 'C', True), (0.95, 2.3, '0603', 'R', True),
                                      (2.15, 14.1, '0402', 'R', True), (2.85, 14.1, '0402', 'C', True),
                                      (4.25, 15.15, '0603', 'R', False), (4.25, 16.15, '0603', 'C', False),
                                      (1.0, 15.9, '0402', 'C', True), (1.7, 15.9, '0402', 'C', True),
                                      (9.7, 16.15, '0603', 'C', False), (7.05, 6.15, '0402', 'C', False),
                                      (11.35, 17.6, '0603', 'R', True), (12.5, 17.6, '0603', 'R', True)):
        E.passivo(d, X0 + x, Y0 + y, tam, tipo, vertical=vertical)
    E.led_smd(d, X0 + 10.3, Y0 + 17.6, AZUL_LED, '0603', vertical=True)
    E.led_smd(d, X0 + 13.6, Y0 + 17.6, VERMELHO_LED, '0603', vertical=True)
    led('esp-01', X0 + 13.6, Y0 + 17.6, 'LedDeEnergia', 'vermelho', r=0.8)
    led('esp-01', X0 + 10.3, Y0 + 17.6, 'LedTx', 'azul', r=0.8)
    gravar('esp-01', d, L, H)
    guardar('esp-01', L, H, catalogo([(n, x, y_fora, '') for n, x in zip(fora, xs)] +
                                     [(n, x, y_dentro, '') for n, x in zip(dentro, xs)]))


esp01()


# ---- ESP-03: tracado da foto de cima do wiki do esp8266.com (03-02),
# retificada pelos meios furos (12,2 entre as fileiras, o passo de 2,0 mm:
# 12,2 x 16,8), girada para a antena ficar em cima. O cobre sob a mascara e
# as vias vetorizados da foto; o ESP8266EX com a marcacao em pe, a flash SOIC-8
# em pe embaixo, o cristal 2520 ("T260"), a antena de ceramica (o ponto de
# alimentacao na direita) e as ilhas sem peca. As ilhas, estanhadas ----
modulo_tracado(
    'esp-03', 'foto:esp-03', 12.2, 16.8, 'azul-petroleo',
    fila(['VCC', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0'], 0.0, 3.55, 2.0, 'esq') +
    fila(['ANT', 'CH_PD', 'GPIO16', 'RXD', 'TXD', 'NC', 'GND'], 12.2, 3.55, 2.0, 'dir'),
    [('ceramica', 6.33, 0.98, 9.5, 1.85, False, 2.9),
     ('qfn', 5.22, 7.3, ('ESP8266EX', '292014', 'P0VV01'), 90),
     ('soic', 5.15, 13.8, 90, 1.27, 5.35, ['E5J805', '25Q40BT', 'AH1433']),
     ('cristal', 9.7, 10.87, 2.0, 2.5, 1.4, 1.7, 'T260'),
     ('rc', 6.25, 2.9, '0603', 'C', False, 1.5), ('rc', 9.9, 3.85, '0603', 'C', False, 1.5),
     ('rc', 9.95, 6.67, '0603', 'R', False, 1.5), ('rc', 9.95, 7.53, '0603', 'C', False, 1.5),
     ('rc', 10.04, 13.92, '0603', 'C', False, 1.5),
     ('ilha', 9.45, 4.75, 0.6, 0.6), ('ilha', 10.6, 4.75, 0.6, 0.6), ('ilha', 9.33, 5.8, 0.65, 0.6),
     ('ilha', 10.6, 5.8, 0.65, 0.6), ('ilha', 2.2, 3.85, 0.55, 0.55), ('ilha', 2.2, 4.9, 0.55, 0.55)],
    ilha=(1.15, 1.0, 0.7), acabamento='#a07b45')

def tampa(d, x, y, w, h, linhas, ang=-90.0):
    """A tampa de metal com a gravacao (texto, x da base, y do comeco,
    tamanho), sem os logotipos. ang=-90: le de baixo para cima (ESP-12);
    89,99: de cima para baixo (ESP-07)."""
    d.ret(x + 0.15, y + 0.2, w, h, '#000000', rx=0.35, op=0.32)
    d.ret(x, y, w, h, 'url(#blindagem)', rx=0.35, stroke='#7f868d', sw=0.12)
    d.ret(x + 0.35, y + 0.35, w - 0.7, h - 0.7, None, rx=0.25, stroke='#b4bac0', sw=0.12)
    for t, bx, by, tam in linhas:
        d.texto(bx, by, t, tam, '#62676e', ang=ang, peso='bold')


# ---- ESP-07: tracado da foto de cima do wiki do esp8266.com (07-02),
# retificada pelos cantos (16 x 22). A placa azul-petroleo, o U.FL no canto
# de cima, a antena de ceramica ao lado (o ponto de alimentacao na esquerda),
# o capacitor, os dois LEDs 0805 com o resistor no meio, as ilhas com o furo
# metalizado e a tampa com a gravacao lendo de cima para baixo ----
def esp07():
    L, H = E.caixa(16.4, 22.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'azul-cinza')
    placa.contorno(X0, Y0, 16.0, 22.0, rx=0.3)
    # o cobre sob a mascara: o plano de GND que aparece entre as ilhas, na
    # borda de baixo e em volta do U.FL; as trilhas dos LEDs e a do RF
    for k in range(7):
        y = 6.95 + k * 2.0
        placa.area(X0 + 0.3, Y0 + y + 0.18, 1.25, 0.66, rx=0.12)
        placa.area(X0 + 14.45, Y0 + y + 0.18, 1.25, 0.66, rx=0.12)
    placa.area(X0 + 0.3, Y0 + 21.35, 15.4, 0.45, rx=0.15)
    placa.area(X0 + 0.25, Y0 + 0.25, 0.5, 4.6, rx=0.15)
    placa.area(X0 + 3.95, Y0 + 3.0, 1.4, 0.6, rx=0.2)
    placa.trilha([(X0 + 3.55, Y0 + 2.45), (X0 + 4.55, Y0 + 2.45), (X0 + 5.35, Y0 + 1.75)], 0.45)
    placa.trilha([(X0 + 6.6, Y0 + 4.8), (X0 + 6.95, Y0 + 4.45)], 0.25)
    placa.trilha([(X0 + 9.15, Y0 + 4.4), (X0 + 10.15, Y0 + 4.4)], 0.25)
    placa.trilha([(X0 + 10.65, Y0 + 3.75), (X0 + 10.65, Y0 + 3.95), (X0 + 10.3, Y0 + 4.3)], 0.22)
    placa.trilha([(X0 + 12.2, Y0 + 4.3), (X0 + 12.55, Y0 + 4.3)], 0.25)
    for vx, vy in ((6.6, 4.8), (10.65, 3.75), (12.2, 4.3), (0.85, 4.2)):
        placa.via(X0 + vx, Y0 + vy)
    # as ilhas: o quadrado de ouro com o furo, nas duas laterais
    pinos = fila(ESQ_12, X0, Y0 + 6.45, 2.0, 'esq') + fila(DIR_12, X0 + 16.0, Y0 + 6.45, 2.0, 'dir')
    for _, x, y, lado in pinos:
        ilha_furada(d, x, y, lado, comp=1.4, larg=1.0, furo=0.95, r=0.34)
    # as pecas
    E.conector_ufl(d, X0 + 2.2, Y0 + 2.25)
    peca_tracada(d, X0, Y0, ('ceramica', 10.25, 1.72, 9.9, 1.75, False, -2.65))
    peca_tracada(d, X0, Y0, ('rc', 5.1, 4.1, '0603', 'C', False, 1.5))
    peca_tracada(d, X0, Y0, ('rc', 10.85, 4.4, '0603', 'R', False, 1.5))
    for x, cor in ((8.05, AZUL_LED), (13.6, VERMELHO_LED)):
        for s in (-1, 1):
            d.ret_c(X0 + x + s * 0.95, Y0 + 4.2, 0.9, 1.3, OURO, rx=0.05)
        E.led_smd(d, X0 + x, Y0 + 4.2, cor, '0805')
    led('esp-07', X0 + 8.05, Y0 + 4.2, 'LedInvertido + 2', 'azul', r=0.9)
    led('esp-07', X0 + 13.6, Y0 + 4.2, 'LedDeEnergia', 'vermelho', r=0.9)
    tampa(d, X0 + 2.0, Y0 + 5.1, 12.7, 16.2,
          [('MODEL', X0 + 9.28, Y0 + 6.25, 0.85), ('ESP8266MOD', X0 + 9.28, Y0 + 12.25, 0.85),
           ('ISM  2.4GHz', X0 + 6.62, Y0 + 12.3, 0.85), ('PA  +25dBm', X0 + 5.5, Y0 + 12.3, 0.85),
           ('802.11b/g/n', X0 + 4.02, Y0 + 12.2, 0.85)], ang=89.99)
    gravar('esp-07', d, L, H)
    guardar('esp-07', L, H, catalogo(pinos))


esp07()


# ---- ESP-13: o formato do WROOM-02 (18 x 20, 9 + 9 castelados de 1,5 mm). A
# antena: a do WROOM-02 tracada da foto com regua (Wikimedia Commons,
# "ESP-WROOM-02(ESP8266)", retificada pelos cantos), a mesma do ESP-13 da foto
# do wiki do esp8266.com, onde ela e de ouro exposto sobre a placa preta, e a
# tampa com a gravacao na horizontal ----
ANTENA_13 = [(1.0, 4.9), (1.0, 0.8), (3.2, 0.8), (3.2, 3.4), (5.9, 3.4), (5.9, 0.8), (8.2, 0.8), (8.2, 3.4), (10.6, 3.4),
             (10.6, 0.55), (15.15, 0.55), (15.15, 5.7)]
ESQ_13 = ['VCC', 'CH_PD', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0', 'GND']
DIR_13 = ['GND.2', 'GPIO16', 'ADC', 'RST', 'GPIO5', 'GND.3', 'TXD', 'RXD', 'GPIO4']      # de cima (18) para baixo (10)


def esp13():
    L, H = E.caixa(18.4, 20.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'preta')
    placa.contorno(X0, Y0, 18.0, 20.0, rx=0.2)
    for pts in (ANTENA_13, [(12.75, 0.55), (12.75, 5.7)]):
        meandro(d, X0, Y0, pts, '#7a5c1a', 0.58)
        meandro(d, X0, Y0, pts, OURO, 0.5)
    for x in (12.75, 15.0):
        d.circ(X0 + x, Y0 + 5.7, 0.3, OURO)
        d.circ(X0 + x, Y0 + 5.7, 0.12, '#1c1608')
    pinos = fila(ESQ_13, X0, Y0 + 7.0, 1.5, 'esq') + fila(DIR_13, X0 + 18.0, Y0 + 7.0, 1.5, 'dir')
    for _, x, y, lado in pinos:
        ilha_furada(d, x, y, lado, comp=1.2, larg=0.9, furo=0.62, r=0.27)
    tampa(d, X0 + 1.6, Y0 + 6.35, 14.8, 13.4,
          [('MODEL', X0 + 2.7, Y0 + 8.4, 0.8), ('ESP8266MOD', X0 + 7.2, Y0 + 13.6, 0.95),
           ('ISM  2.4GHz', X0 + 7.2, Y0 + 15.2, 0.85), ('PA  +25dBm', X0 + 7.2, Y0 + 16.6, 0.85),
           ('802.11b/g/n', X0 + 7.2, Y0 + 18.0, 0.85)], ang=0.0)
    gravar('esp-13', d, L, H)
    guardar('esp-13', L, H, catalogo(pinos))


esp13()


# ---- ESP-08: o de 14 ilhas, o unico com referencia (o desenho da placa na
# l0l, 17,0 x 16,0 com cota, e a foto do wiki do esp8266.com): a placa verde,
# 7 + 7 castelados de 2,0 mm em cima e embaixo (a pinagem do ESP-04) e a
# tampa de metal cobrindo quase tudo. O cobre do desenho aparece so na faixa
# entre as ilhas e a tampa ----
ESP08_CIMA = ['ANT', 'CH_PD', 'GPIO16', 'RXD', 'TXD', 'NC', 'GND']
ESP08_BAIXO = ['VCC', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0']


def esp08():
    L, H = E.caixa(17.4, 16.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'verde-escura')
    placa.contorno(X0, Y0, 17.0, 16.0, rx=0.2)
    cobre_tracado(d, placa, 'esp-08', X0 - 0.17, Y0 - 0.08)
    pinos = fila(ESP08_CIMA, Y0, X0 + 2.6, 2.0, 'cima') + fila(ESP08_BAIXO, Y0 + 16.0, X0 + 2.6, 2.0, 'baixo')
    for _, x, y, lado in pinos:
        E.ilha_castelada(d, x, y, lado, comp=1.25, larg=1.0, furo=0.7)
    tampa(d, X0 + 0.85, Y0 + 2.0, 15.3, 12.1,
          [('MODEL', X0 + 4.6, Y0 + 13.3, 0.85), ('ESP8266MOD', X0 + 5.9, Y0 + 13.3, 0.85),
           ('ISM  2.4GHz', X0 + 9.0, Y0 + 13.3, 0.85), ('PA  +25dBm', X0 + 10.3, Y0 + 13.3, 0.85),
           ('802.11b/g/n', X0 + 11.6, Y0 + 13.3, 0.85)])
    gravar('esp-08', d, L, H)
    guardar('esp-08', L, H, catalogo(pinos, {'ANT': 'A saída para a antena de fora (WIFI_ANT, não ligar)'}))


esp08()


# ---- ESP-04: o desenho da placa (14,75 x 12,67 pelo passo de 2,0 mm das
# ilhas; a cota da l0l diz 14,7 x 12,1), azul, 7 + 7 castelados em cima e
# embaixo, o QFN a esquerda, a flash girada (o pino 1 em cima a direita), o
# cristal 2520 em cima e os passivos medidos ----
modulo_tracado(
    'esp-04', 'esp-04', 14.75, 12.67, 'azul-esp',
    fila(['ANT', 'CH_PD', 'GPIO16', 'RXD', 'TXD', 'NC', 'GND'], 0.0, 1.48, 2.0, 'cima') +
    fila(['VCC', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO15', 'GPIO2', 'GPIO0'], 12.67, 1.5, 2.0, 'baixo'),
    [('qfn', 5.37, 7.18, ('ESP8266EX',)),
     ('soic', 11.81, 7.48, 180, 1.26, 5.22, ['25Q40']),
     ('cristal', 8.93, 2.74, 2.5, 2.0, 1.62, 1.22),
     ('rc', 1.85, 2.55, '0402', 'C', True, 0.93), ('rc', 2.80, 2.56, '0603', 'C', True, 1.32),
     ('rc', 3.74, 2.59, '0603', 'C', True, 1.32), ('rc', 4.60, 2.40, '0402', 'R', True, 0.91),
     ('rc', 5.48, 2.38, '0402', 'C', True, 0.95), ('rc', 11.88, 2.42, '0402', 'R', True, 0.9),
     ('rc', 0.82, 6.23, '0402', 'C', True, 1.01), ('rc', 2.25, 10.47, '0402', 'C', False, 1.01)],
    serigrafia=[(7.65, 1.27, 10.6, 1.27), (10.6, 1.27, 10.6, 2.35), (7.65, 3.89, 10.4, 3.89),
                (9.26, 5.37, 9.26, 9.59), (9.26, 5.37, 9.46, 5.37), (9.26, 9.59, 9.46, 9.59),
                (14.35, 5.43, 14.35, 9.59), (14.15, 5.43, 14.35, 5.43), (14.15, 9.59, 14.35, 9.59)],
    circulos=[(13.57, 6.53, 0.47)], ilha=(1.0, 1.0, 0.6))


# ---- ESP-10: o desenho da placa (l0l: 15,4 x 10,0 pelo contorno e pelo passo
# de 2,0 mm das ilhas; a cota de 14,2 mm vai so ate a faixa das ilhas) e a
# foto do wiki (10-01, retificada pelos dois furos e pelos chips) para as
# cores e o tipo das pecas: a placa vermelha, o ESP8266EX, a flash SOIC-8 com
# o pino 1 em cima a direita (o circulo da serigrafia), o cristal 2520, os
# dois furos metalizados e as cinco ilhas casteladas na direita ----
def furo_dourado(d, x, y, r=0.45):
    d.circ(x, y, r, OURO)
    d.circ(x, y, r * 0.62, '#5a4a20')
    d.circ(x - 0.06, y - 0.06, r * 0.3, '#d8c48a', op=0.5)


modulo_tracado(
    'esp-10', 'esp-10', 15.4, 10.0, 'vermelha',
    fila(['RST', 'VCC', 'TXD', 'RXD', 'GND'], 15.4, 1.18, 2.0, 'dir'),
    [('qfn', 5.3, 5.94, ('ESP8266EX', '292014', 'P0VV01')),
     ('soic', 11.47, 6.25, 180, 1.27, 5.25, ['ESJ805', '25Q40BT', 'AH1433'], 3.9),
     ('cristal', 8.43, 1.4, 2.5, 2.0, 1.65, 1.3, 'T260'),
     ('rc', 4.75, 1.6, '0402', 'R', False, 1.0), ('rc', 6.3, 1.15, '0402', 'C', True, 1.0),
     ('rc', 10.45, 0.95, '0402', 'C', True, 0.8), ('rc', 1.55, 7.3, '0603', 'C', True, 1.5)],
    serigrafia=[(7.0, 0.31, 9.92, 0.31), (7.0, 2.63, 9.92, 2.63),
                (8.95, 4.2, 8.95, 8.4), (8.95, 4.2, 9.15, 4.2), (8.95, 8.4, 9.15, 8.4),
                (14.05, 4.2, 14.05, 8.4), (13.85, 4.2, 14.05, 4.2), (13.85, 8.4, 14.05, 8.4),
                (14.35, 3.93, 14.35, 5.1), (13.9, 3.93, 14.35, 3.93)],
    circulos=[(13.24, 5.33, 0.43)], ilha=(0.85, 1.15, 0.75), acabamento=ESTANHO,
    depois=lambda d, X0, Y0: (furo_dourado(d, X0 + 1.09, Y0 + 1.05), furo_dourado(d, X0 + 12.36, Y0 + 1.57)))

# ---- ESP-11: a posicao das ilhas, dos furos e dos chips sai do desenho das
# ilhas no forum do esp8266.com (18,1 x 13,05 pelo passo de 1,27 mm dos furos);
# a cor, as pecas pequenas e as trilhas que aparecem pela mascara, da foto do
# wiki (11-01, retificada pelos furos, pela antena e pelos chips): a placa
# azul, a antena de ceramica em pe na esquerda (o ponto de alimentacao em
# cima), o cristal 2520, o ESP8266EX, a flash com o pino 1 em cima a direita e
# os oito furos estanhados de 1,27 mm na direita ----
ESP11_PINOS = ['GND', 'CH_PD', 'RST', 'RXD', 'TXD', 'GPIO0', 'GPIO1', 'VCC']


def esp11():
    L, H = E.caixa(18.5, 13.45)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'azul-claro')
    placa.contorno(X0, Y0, 18.1, 13.05, rx=0.2)

    def t(pts, larg=0.3):
        placa.trilha([(X0 + x, Y0 + y) for x, y in pts], larg)

    # as trilhas que se veem na foto
    t([(2.85, 2.6), (2.85, 7.0), (3.2, 7.4), (3.2, 9.7), (3.95, 10.5), (7.55, 10.55), (7.85, 10.85)], 0.45)
    t([(7.7, 11.3), (15.55, 11.3), (16.4, 10.85)])
    t([(7.95, 11.6), (15.75, 11.6), (16.9, 12.1)])
    t([(8.2, 11.9), (15.6, 11.9)])
    t([(3.3, 0.95), (5.6, 0.95)], 0.3)
    t([(11.0, 10.4), (11.0, 11.3)], 0.25)
    t([(12.3, 10.4), (12.3, 11.0)], 0.25)
    t([(15.0, 4.0), (16.2, 3.2)], 0.25)
    t([(15.0, 3.6), (15.8, 3.0), (16.4, 3.0)], 0.25)
    for vx, vy in ((3.0, 1.0), (5.3, 1.3), (6.6, 1.15), (3.8, 2.05), (5.6, 2.05), (7.0, 2.1), (14.2, 1.15), (15.2, 1.8),
                   (2.9, 3.0), (4.85, 4.1), (5.6, 4.6), (7.3, 4.15), (8.5, 3.8), (9.1, 4.1), (2.9, 4.5), (2.9, 5.0),
                   (4.5, 5.9), (2.6, 7.9), (2.6, 9.0), (4.0, 10.6), (6.2, 11.15), (14.15, 11.55), (2.85, 11.95),
                   (10.3, 8.4), (15.25, 0.4)):
        placa.via(X0 + vx, Y0 + vy)
    # os furos de 1,27 mm: a ilha oval estanhada ate a borda e o furo
    pinos = [(n, X0 + 17.25, Y0 + 1.96 + k * 1.27, 'dir') for k, n in enumerate(ESP11_PINOS)]
    for _, x, y, _ in pinos:
        d.ret_c(x + 0.38, y, 1.5, 1.05, ESTANHO, rx=0.5)
        d.circ(x, y, 0.33, '#7a5a26')
        d.circ(x, y, 0.22, '#3b2a12')
    # as pecas
    for p in (('ceramica', 1.5, 6.55, 9.8, 1.75, True, -3.3),
              ('cristal', 9.73, 1.85, 2.5, 2.0, 1.75, 1.2, 'T260'),
              ('qfn', 6.81, 6.9, ('ESP8266EX', '292014', 'P0VV01')),
              ('soic', 12.87, 7.07, 180, 1.27, 5.25, ['ESJ805', '25Q40BT', 'AH1433'], 3.9),
              ('rc', 4.65, 2.35, '0402', 'C', False, 0.9), ('rc', 7.05, 3.0, '0603', 'R', False, 1.4),
              ('rc', 8.55, 2.35, '0402', 'C', True, 0.9), ('rc', 12.65, 2.4, '0402', 'C', True, 0.9),
              ('rc', 3.85, 3.5, '0402', 'C', True, 0.9), ('rc', 4.6, 4.75, '0402', 'C', False, 0.9),
              ('rc', 4.15, 7.6, '0603', 'C', True, 1.4)):
        peca_tracada(d, X0, Y0, p, ilha=ESTANHO)
    gravar('esp-11', d, L, H)
    guardar('esp-11', L, H, catalogo(pinos))


esp11()


# ---- ESP-02: as ilhas do desenho da placa (l0l: 14,2 x 14,2, 4 + 4 castelados
# de 2,54 mm, o RST e o GND quadrados) e, da foto do wiki (02-01, retificada
# pelos castelados), a cor, as trilhas que aparecem pela mascara e a
# serigrafia: a placa vermelha, o U.FL no canto, o cristal 2520, o ESP8266EX,
# a flash com o pino 1 em cima a direita e o furo metalizado ----
def esp02():
    L, H = E.caixa(14.6, 14.6)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'vermelha')
    placa.contorno(X0, Y0, 14.2, 14.2, rx=0.2)
    cobre_tracado(d, placa, 'esp-02', X0 - 0.12, Y0 - 0.08)

    def t(pts, larg=0.32):
        placa.trilha([(X0 + x, Y0 + y) for x, y in pts], larg)

    # o cobre que se ve na foto: as faixas de GND da borda e as trilhas
    placa.area(X0 + 0.15, Y0 + 3.6, 0.45, 8.2, rx=0.15)
    placa.area(X0 + 0.15, Y0 + 13.55, 4.6, 0.5, rx=0.15)
    t([(6.7, 1.1), (11.75, 1.1), (12.05, 0.92), (13.2, 0.92)], 0.4)
    t([(2.0, 9.85), (2.9, 10.6), (2.9, 11.7), (2.6, 12.05), (0.9, 12.05), (0.55, 11.65), (0.55, 6.3)], 0.38)
    t([(6.4, 11.4), (6.9, 12.3), (12.4, 12.3), (12.75, 12.7)], 0.4)
    t([(13.05, 11.75), (13.05, 13.6)], 0.4)
    t([(9.2, 4.62), (13.3, 4.62)], 0.3)
    t([(7.4, 5.95), (8.6, 5.95)], 0.28)
    t([(8.5, 2.05), (10.0, 2.05)], 0.28)
    t([(4.95, 3.6), (5.8, 3.5)], 0.28)
    for vx, vy in ((5.15, 1.6), (6.6, 1.15), (4.6, 2.95), (5.2, 2.95), (3.05, 4.3), (0.45, 5.25), (6.8, 5.0),
                   (8.3, 5.65), (0.7, 6.75), (1.5, 7.35), (1.5, 8.6), (2.2, 11.5), (3.2, 12.1), (3.95, 12.1),
                   (11.3, 12.95), (0.8, 13.3), (3.15, 13.3), (12.95, 6.7), (7.7, 11.5)):
        placa.via(X0 + vx, Y0 + vy)
    # os castelados (de ouro, o primeiro quadrado em cada fila)
    pinos = (fila(['RST', 'GPIO2', 'GPIO0', 'GPIO15'], Y0, X0 + 5.75, 2.54, 'cima') +
             fila(['GND', 'RXD', 'TXD', 'VCC'], Y0 + 14.2, X0 + 5.8, 2.54, 'baixo'))
    for nome, x, y, lado in pinos:
        dy = 1 if lado == 'cima' else -1
        if nome in ('RST', 'GND'):
            d.ret_c(x, y + dy * 0.6, 1.5, 1.2, OURO, rx=0.05)
        else:
            d.circ(x, y + dy * 0.35, 0.75, OURO)
            d.ret_c(x, y + dy * 0.2, 1.5, 0.4, OURO)
        d.caminho([('M', x - 0.42, y), ('A', 0.42, 0, 0 if lado == 'cima' else 1, x + 0.42, y), ('Z',)], '#140a08')
    # a serigrafia da foto
    for txt, x, y, ang in (('C30', 11.2, 1.65, 89.99), ('C5', 0.75, 5.7, 0), ('R5', 3.95, 5.7, 0), ('C8', 5.55, 5.7, 0)):
        E.silk_texto(d, X0 + x, Y0 + y, txt, 0.72, ang=ang, alinhar=None, peso='bold')
    E.silk_ret(d, X0 + 0.8, Y0 + 0.55, 3.1, 3.05, 0.13, rx=0.3)
    for x1, y1, x2, y2 in ((6.8, 1.97, 9.6, 1.97), (6.8, 4.25, 9.6, 4.25), (8.8, 6.2, 8.8, 10.3), (13.9, 6.2, 13.9, 10.3)):
        E.silk_linha(d, X0 + x1, Y0 + y1, X0 + x2, Y0 + y2, 0.13)
    d.circ(X0 + 13.07, Y0 + 7.3, 0.5, None, E.SERIGRAFIA, 0.13)
    # as pecas
    E.conector_ufl(d, X0 + 2.35, Y0 + 2.05)
    for p in (('cristal', 8.2, 3.08, 2.5, 2.0, 1.6, 1.2, 'T260'),
              ('qfn', 5.2, 8.15, ('ESP8266EX', '292014', 'P0VV01')),
              ('soic', 11.37, 8.21, 180, 1.27, 5.18, ['25X40BVNIG', '1330'], 3.9),
              ('rc', 1.17, 4.45, '0402', 'C', False, 0.93), ('rc', 1.17, 5.35, '0402', 'C', False, 0.93),
              ('rc', 1.1, 6.42, '0402', 'C', False, 0.95), ('rc', 1.63, 8.65, '0402', 'C', True, 0.95),
              ('rc', 4.73, 3.85, '0402', 'R', False, 0.93), ('rc', 4.55, 4.65, '0402', 'C', False, 0.9),
              ('rc', 6.1, 3.12, '0402', 'C', True, 0.95), ('rc', 10.5, 3.1, '0402', 'C', True, 0.95)):
        peca_tracada(d, X0, Y0, p, ilha=ESTANHO)
    furo_dourado(d, X0 + 13.07, Y0 + 2.64)
    gravar('esp-02', d, L, H)
    guardar('esp-02', L, H, catalogo(pinos))


esp02()




def texto_girado(d, x, y, t, tam, cor, graus):
    """Texto centrado em (x, y) girado de verdade (o texto() deixa sempre
    legivel; a marcacao de um chip montado de ponta-cabeca fica de
    ponta-cabeca)."""
    X, Y = d.t(x, y)
    e = d.t.escala
    d.partes.append(f'<text transform="translate({pa.f(X)} {pa.f(Y)}) rotate({graus})" x="0" y="{pa.f(tam * 0.45 * e)}" '
                    f'font-family="Arial" font-weight="normal" font-size="{pa.f(tam * 1.25 * e)}" fill="{cor}" '
                    f'text-anchor="middle">{t}</text>\n')


# ---- ESP-05: tracado da foto do wiki (05-02, retificada pelos quatro cantos
# da placa; 14,2 x 14,2): a placa azul, a fileira de cinco furos de 2,54 mm em
# cima (a barra vai por baixo), o furo de GND sozinho no canto, a flash SOIC-8
# de 208 mil a esquerda, o ESP8266EX montado de ponta-cabeca, o cristal 3225,
# os passivos 0603, o U.FL no canto e o plano de GND que aparece pela mascara ----
def esp05():
    L, H = E.caixa(14.6, 14.6)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'azul-aco')
    placa.contorno(X0, Y0, 14.2, 14.2, rx=0.2)
    TRACADO['foto:esp-05']['vias'] = [[6.0, 8.95], [6.62, 9.98], [7.72, 10.0], [13.3, 3.35], [13.05, 6.78], [13.38, 7.25],
                                      [13.38, 7.8], [13.38, 8.6], [13.35, 9.95], [11.0, 10.25], [6.2, 13.6], [8.05, 13.62]]
    cobre_tracado(d, placa, 'foto:esp-05', X0, Y0)
    nomes = ['VCC', 'TXD', 'RXD', 'GND', 'RST']
    pinos = [(n, X0 + 2.02 + k * 2.54, Y0 + 1.15, 'cima') for k, n in enumerate(nomes)] + [('GND.2', X0 + 1.7, Y0 + 12.2, '')]
    for n, x, y, _ in pinos:
        E.ilha_tht(d, x, y, 0.95, 1.55, acabamento=OURO)
    for p in (('soic', 3.7, 6.15, 0, 1.27, 6.6, ['AH1433', '25Q40BT', 'ESJ805'], 5.3),
              ('cristal', 7.3, 11.5, 3.2, 2.5, 2.2, 1.7, ''),
              ('rc', 4.75, 11.25, '0603', 'R', True, 1.5), ('rc', 8.95, 11.2, '0603', 'R', True, 1.5),
              ('rc', 10.45, 10.75, '0603', 'R', False, 1.5), ('rc', 13.1, 5.65, '0603', 'C', True, 1.5)):
        peca_tracada(d, X0, Y0, p, ilha=ESTANHO)
    # o ESP8266EX de ponta-cabeca (a marcacao e o ponto do pino 1 embaixo a direita)
    cx, cy = X0 + 9.7, Y0 + 6.4
    d.entrar(mover(cx, cy) * rodar(180))
    E.qfn(d, 0, 0, 5.0, 8, [])
    d.sair()
    for k, t in enumerate(('ESP8266EX', '292014', 'P0VV01')):
        texto_girado(d, cx, cy + (1 - k) * 0.8, t, 0.55, E.MARCA_CI, 180)
    E.conector_ufl(d, X0 + 12.3, Y0 + 12.1)
    gravar('esp-05', d, L, H)
    guardar('esp-05', L, H, catalogo(pinos, {'GND.2': 'GND (o furo do canto)'}))


esp05()


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
    # a foto do wiki do esp8266.com (06-01): a placa azul-petroleo quase toda
    # coberta pela tampa, com a gravacao na horizontal
    placa = E.Placa(d, 'azul-petroleo')
    placa.contorno(X0, Y0, Lm, Hm, rx=0.2)
    pinos = pinos_lga(X0, Y0, Lm, Hm, [('esq', ['ANT', 'VCC', 'CH_PD'], 2.9), ('cima', ['GPIO16', 'TXD', 'RXD'], 2.8),
                                       ('dir', ['GPIO0', 'GPIO2', 'GPIO15'], 2.9),
                                       ('baixo', ['GPIO14', 'GPIO12', 'GPIO13'], 2.8)], 0.9)
    for _, x, y, lado in pinos:
        borda_lga(d, x, y, lado, comp=0.35, larg=0.9)
    tampa(d, X0 + 0.45, Y0 + 0.45, Lm - 0.9, Hm - 0.9,
          [('MODEL', X0 + 1.6, Y0 + 5.4, 0.85), ('ESP8266MOD', X0 + 7.2, Y0 + 7.2, 0.95),
           ('ISM  2.4GHz', X0 + 7.2, Y0 + 8.8, 0.85), ('PA  +25dBm', X0 + 7.2, Y0 + 10.2, 0.85),
           ('802.11b/g/n', X0 + 7.2, Y0 + 11.6, 0.85)], ang=0.0)
    gravar('esp-06', d, L, H)
    guardar('esp-06', L, H, catalogo(pinos, {'ANT': 'A saída para a antena de fora (WIFI_ANT, não ligar)'}))


esp06()




# ---- ESP-09: o desenho da placa (l0l: 10 x 10) e a foto do forum (a placa
# preta, sem blindagem): o cobre de cima e as vias do desenho, o ESP8266EX, a
# flash WSON-8 (passo de 0,5 mm) na direita, o cristal 2016 em pe no canto e
# os passivos 0402. As ilhas sao por baixo (LGA): de cima so aparece a ponta
# de cada uma na borda ----
def esp09():
    Lm, Hm = 10.0, 10.0
    L, H = E.caixa(Lm + 0.4, Hm + 0.4)
    X0, Y0 = 0.2, 0.2
    d = E.novo()
    placa = E.Placa(d, 'preta')
    placa.contorno(X0, Y0, Lm, Hm, rx=0.15)
    cobre_tracado(d, placa, 'esp-09', X0 - 0.02, Y0 - 0.02)
    pinos = pinos_lga(X0, Y0, Lm, Hm, [('esq', ['ANT', 'VCC', 'CH_PD'], 2.0), ('cima', ['RST', 'TXD', 'RXD'], 1.8),
                                       ('dir', ['GPIO0', 'GPIO2', 'GPIO15'], 1.8),
                                       ('baixo', ['GPIO14', 'GPIO12', 'GPIO13'], 1.8)], 0.55)
    for _, x, y, lado in pinos:
        borda_lga(d, x, y, lado, comp=0.35, larg=0.8)
    for p in (('qfn', 4.15, 5.38, ('ESP8266EX',)),
              ('cristal', 8.42, 1.8, 1.6, 2.0, 1.15, 1.4, ''),
              ('rc', 1.46, 0.69, '0402', 'C', False, 0.73), ('rc', 1.46, 1.5, '0402', 'C', False, 0.73),
              ('rc', 2.73, 1.12, '0402', 'C', True, 0.78), ('rc', 3.93, 1.07, '0402', 'R', True, 0.83),
              ('rc', 4.87, 1.08, '0402', 'C', True, 0.8), ('rc', 6.64, 1.22, '0402', 'C', True, 0.89),
              ('rc', 8.97, 3.96, '0402', 'C', False, 0.96)):
        peca_tracada(d, X0, Y0, p, ilha=ESTANHO)
    # a flash WSON-8 de 2 x 3 mm: as ilhas por baixo da borda, o corpo por cima
    fx, fy = X0 + 8.85, Y0 + 6.6
    for k in range(4):
        for sy in (-1, 1):
            d.ret_c(fx - 0.75 + k * 0.5, fy + sy * 1.45, 0.28, 0.4, ESTANHO, rx=0.04)
    E.sombra(d, fx - 1.0, fy - 1.35, 2.0, 2.7)
    d.ret_c(fx, fy, 2.0, 2.7, E.PRETO_CI, rx=0.1)
    d.ret_c(fx, fy, 2.0, 2.7, 'url(#luz)', rx=0.1)
    d.circ(fx + 0.6, fy - 0.95, 0.15, '#3a3c41')
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
        # o 10 uF da foto e um tantalo laranja (caixa A) no lugar do 0805
        w, h = (2.6, 1.5) if deitado else (1.5, 2.6)
        E.sombra(d, cx - w / 2, cy - h / 2, w, h)
        d.ret_c(cx, cy, w, h, '#d9822b', rx=0.1)
        d.ret_c(cx, cy - (h / 2 - 0.3) if not deitado else cy, w if not deitado else 0.45, 0.45 if not deitado else h,
                '#9c5414')
        d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.1)
    elif pad.startswith(('SMT_C_TANTALUM', 'SMT_DIODE_SOD_323')):
        # o tantalo B (o corpo amarelo, a faixa do + na ilha 1) e o diodo
        # SOD-323 (o corpo preto, a faixa do catodo na ilha 1)
        tantalo = pad.startswith('SMT_C_TANTALUM')
        comp_, larg_ = (3.5, 2.8) if tantalo else (1.7, 1.25)
        cor, faixa, lf = ('#dc9a2c', '#8f5715', 0.6) if tantalo else ('#202124', '#9ea3a8', 0.25)
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
        d.ret_c(cx, cy, w, h, 'url(#metal)', rx=0.2, stroke='#8d949b', sw=0.08)
        d.ret_c(cx, cy, w - 0.5, h - 0.5, '#e9ebed', rx=0.15)
        d.circ(cx, cy, 0.85, '#151618', stroke='#3b3d41', sw=0.1)
        d.circ(cx, cy, 0.85, 'url(#cupula)')
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
        d.texto(bx, tab['y'] - 0.9, 'LD33', 0.72, E.MARCA_CI, ang=90, alinhar='center', peso='normal')
        d.texto(bx + 0.9, tab['y'] - 0.9, 'K408', 0.6, E.MARCA_CI, ang=90, alinhar='center', peso='normal')
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
    # a cor e o acabamento da foto oficial (Commons, "NodeMCU DEVKIT 1.0",
    # retificada pelos quatro furos de fixacao): a placa grafite fosca e o anel
    # estanhado dos furos
    placa = E.Placa(d, 'grafite')
    furos = [(*local(i['x'], i['y']), i['furo'] / 2) for i in doc.ilhas if i['comp'] == 65535 and i['furo'] > 0]
    placa.contorno(X0, Y0, Lp, Hp, rx=2.286, furos=furos, anel=ESTANHO)

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
    _, (xl, yl) = desenhar_12(d, mx, my, 'preta', True, True)

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
