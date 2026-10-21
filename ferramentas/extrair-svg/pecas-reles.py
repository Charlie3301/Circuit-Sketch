"""python ferramentas/extrair-svg/pecas-reles.py

Os modulos de rele, os azuis dos kits, com optoacoplador (Alpha 24;
redesenhados na Alpha 25 no estilo das placas, com as primitivas de
placas-arduino.py):

  - modulo-rele-1 : 1 canal (VCC, GND, IN; NO, COM, NC) -- os mesmos nomes
                    do "wokwi-relay-module";
  - modulo-rele-2 : 2 canais (GND, IN1, IN2, VCC; NO1 COM1 NC1, NO2 COM2 NC2);
  - modulo-rele-4 : 4 canais (GND, IN1..IN4, VCC; os quatro bornes).

Em cima, os bornes de parafuso (NO, COM, NC de cada canal); no meio, os
reles SRD-05VDC-SL-C (o cubo azul de 19 x 15,5 mm, com o que vem impresso
nele, sem a marca); embaixo, o optoacoplador 817, o transistor, o diodo de
roda livre, o LED de cada canal e o conector. Nos de 2 e 4 canais, o jumper
JD-VCC/VCC no canto (no lugar: a bobina tira do VCC). Todos os pinos ficam na
grade de 0,1" entre si. O que acende (a luz de energia e a de cada canal) o
editor desenha por cima.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f, mover, rodar = pa.Desenho, pa.T, pa.f, pa.mover, pa.rodar
SERIGRAFIA, ESTANHO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.PRETO_CI, pa.MARCA_CI
A = 96 / 25.4
PASSO = 2.54
PLACA, PLACA_BORDA = '#1d4b9c', '#0f2c61'
AZUL_RELE, AZUL_BORNE = '#2f7ad3', '#2b84dc'
RESISTOR = '#1e1f22'

pecas = []


def svg_de(d, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def pino_header(d, x, y):
    """O pino da barra, visto de cima (o plastico preto e a ponta dourada)."""
    d.ret_c(x, y, 2.4, 2.4, 'url(#plastico)', rx=0.3)
    d.ret_c(x, y, 0.84, 0.84, '#7a5c18')
    d.ret_c(x, y, 0.64, 0.64, 'url(#ouro)')


def dois(d, cx, cy, vertical, corpo, comp=2.0, larg=1.25, marca=None):
    """Um 0805 com os terminais estanhados."""
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w, h, corpo, rx=0.08)
    t = comp * 0.22
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')
    if marca:
        d.texto(cx, cy, marca, min(w, h) * 0.45, MARCA_CI, ang=90 if vertical else 0, alinhar='center',
                peso='normal')


def led_0805(d, cx, cy):
    """O LED SMD apagado: o corpo branco leitoso com o chip no meio (o editor acende por cima)."""
    d.ret_c(cx, cy, 2.0, 1.25, '#f2efe6', rx=0.12)
    d.ret_c(cx, cy, 1.1, 0.85, '#e7d9c7', rx=0.15)
    d.ret_c(cx, cy, 0.35, 0.35, '#b8a27a')


def borne(d, xs, y):
    """O borne de parafuso de 3 vias (KF301): o corpo azul, o degrau da entrada
    dos fios virado para a borda e os tres parafusos de fenda."""
    x0, x1 = xs[0] - 2.84, xs[-1] + 2.84
    d.ret(x0, y - 3.0, x1 - x0, 6.0, AZUL_BORNE, rx=0.4, stroke='#134a8c', sw=0.15)
    d.ret(x0, y - 3.0, x1 - x0, 6.0, 'url(#luz)', rx=0.4)
    d.ret(x0 + 0.15, y - 2.95, x1 - x0 - 0.3, 1.2, '#2370c4', rx=0.3)          # o degrau dos fios
    for x in xs:
        d.ret_c(x, y - 2.35, 2.6, 0.7, '#0f2d55', rx=0.15)                       # a boca do fio
    for xa, xb in zip(xs, xs[1:]):
        d.linha((xa + xb) / 2, y - 1.6, (xa + xb) / 2, y + 2.8, '#1b5fae', 0.2)  # as divisorias
    for x in xs:
        d.circ(x, y + 0.5, 1.75, '#1a4f92')                                        # o poco do parafuso
        d.circ(x, y + 0.5, 1.5, 'url(#lata)', stroke='#6d737c', sw=0.12)
        d.linha(x - 1.05, y + 0.85, x + 1.05, y + 0.15, '#4f555d', 0.38)          # a fenda


def rele_srd(d, x, y, larg=15.4, alt=19.0):
    """O SRD-05VDC-SL-C visto de cima, em pe (o comprido para baixo): o cubo azul,
    as duas marcas do molde e o que vem impresso (sem a marca do fabricante)."""
    d.ret(x, y, larg, alt, AZUL_RELE, rx=0.6, stroke='#174e96', sw=0.2)
    d.ret(x, y, larg, alt, 'url(#luz)', rx=0.6)
    d.ret(x + 0.5, y + 0.5, larg - 1.0, alt - 1.0, None, rx=0.45, stroke='#4f92e0', sw=0.1)
    # no sistema do texto: x ao longo dos 19 mm (de baixo para cima), y ao longo dos 15,4 (para a direita)
    d.entrar(mover(x + larg / 2, y + alt / 2) * rodar(-90))
    for cx, cy in ((7.3, -5.6), (0.2, -0.2)):                                      # as marcas do molde
        d.circ(cx, cy, 0.95, '#2a6dbf', stroke='#245fa8', sw=0.08)
    claro = '#e3ecf8'
    d.texto(0, 2.2, '10A 250VAC  10A 125VAC', 0.95, claro, alinhar='center', peso='normal')
    d.texto(0, 3.9, '10A  30VDC  10A  28VDC', 0.95, claro, alinhar='center', peso='normal')
    d.texto(0, 5.8, 'SRD-05VDC-SL-C', 1.3, claro, alinhar='center', peso='bold')
    d.sair()


def opto_817(d, x, y):
    """O optoacoplador 817 (DIP-4): as quatro pernas, o corpo preto, a bolinha do pino 1."""
    for dx in (1.0, 3.54):
        for yy in (y - 0.5, y + 3.4):
            d.ret(x + dx - 0.3, yy, 0.6, 0.5, 'url(#metal-h)')
    d.ret(x, y, 4.6, 3.4, PRETO_CI, rx=0.25)
    d.ret(x, y, 4.6, 3.4, 'url(#luz)', rx=0.25)
    d.circ(x + 0.7, y + 2.7, 0.3, '#3a3c41')
    d.texto(x + 2.3, y + 1.5, '817C', 0.7, MARCA_CI, alinhar='center', peso='normal')


def sot23(d, cx, cy, marca):
    """O transistor SOT-23: uma perna de um lado, duas do outro."""
    d.ret_c(cx, cy - 1.0, 0.45, 0.7, 'url(#metal-h)')
    for dx in (-0.95, 0.95):
        d.ret_c(cx + dx, cy + 1.0, 0.45, 0.7, 'url(#metal-h)')
    d.ret_c(cx, cy, 2.9, 1.4, PRETO_CI, rx=0.12)
    d.texto(cx, cy, marca, 0.55, MARCA_CI, alinhar='center', peso='normal')


def diodo_sma(d, cx, cy, marca):
    """O diodo de roda livre (SMA, o M7): os terminais, o corpo e a faixa do catodo."""
    for dx in (-1.55, 1.55):
        d.ret_c(cx + dx, cy, 0.8, 0.9, ESTANHO, rx=0.08)
    d.ret_c(cx, cy, 2.8, 1.5, PRETO_CI, rx=0.12)
    d.ret_c(cx + 1.1, cy, 0.3, 1.5, '#9aa1a8')
    d.texto(cx - 0.2, cy, marca, 0.6, MARCA_CI, alinhar='center', peso='normal')


def modulo(id_peca, canais):
    alt = 52.0
    passo_canal = 7 * PASSO                   # 17,78 mm de um rele ao outro (na grade)
    larg = {1: 26.0, 2: 40.0, 4: 76.0}[canais]
    x0 = 3.2                                  # a grade de 0,1" comeca aqui
    y_cabo = alt - 2.8                        # o conector embaixo
    y_borne = y_cabo - 18 * PASSO             # os parafusos em cima (na grade)

    def na_grade(x):
        return x0 + round((x - x0) / PASSO) * PASSO

    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.ret(0.3, 0.3, larg - 0.6, alt - 0.6, PLACA, rx=1.2, stroke=PLACA_BORDA, sw=0.3)
    d.ret(0.3, 0.3, larg - 0.6, alt - 0.6, 'url(#luz)', rx=1.2)
    # os furos de fixacao; nos de 2 e 4 canais o borne do canal 1 toma o canto
    # de cima, e os de cima descem para o vao entre os bornes e os reles
    cantos = [(2.0, 2.0), (larg - 2.0, 2.0)] if canais == 1 else [(1.75, 8.0), (larg - 1.75, 8.0)]
    for x, y, r in [(x, y, 1.6 if canais == 1 else 1.3) for x, y in cantos] + [(x, alt - 9.0, 1.6) for x in (2.0, larg - 2.0)]:
        d.circ(x, y, r, ESTANHO)
        d.circ(x, y, r * 0.72, '#0b1630')
    pinos = []
    luzes = []
    descricao_saida = {
        'NO': 'NO (normalmente aberto): liga no COM so com o rele ligado (a carga que o rele liga)',
        'COM': 'COM (comum): a chave do rele; vai para o NO ligado e para o NC desligado',
        'NC': 'NC (normalmente fechado): ligado no COM com o rele DESLIGADO',
    }
    for c in range(canais):
        centro = na_grade(larg / 2 - (canais - 1) * passo_canal / 2) + c * passo_canal
        sufixo = '' if canais == 1 else str(c + 1)
        # o borne de 3 vias (NO, COM, NC), passo de 5,08
        xs = [centro - 2 * PASSO, centro, centro + 2 * PASSO]
        borne(d, xs, y_borne)
        for nome, x in zip(['NO', 'COM', 'NC'], xs):
            d.texto(x, y_borne + 4.6, nome, 0.85, SERIGRAFIA, alinhar='center')
            pinos.append((nome + sufixo, x, y_borne, [],
                          descricao_saida[nome] + ('' if canais == 1 else f' -- canal {c + 1}')))
        rele_srd(d, centro - 7.7, y_borne + 6.0)
        # o optoacoplador, o transistor, o diodo de roda livre e os resistores
        y_opto = y_borne + 27.6
        opto_817(d, centro - 5.6, y_opto)
        sot23(d, centro + 2.1, y_opto + 1.2, 'J3Y')
        diodo_sma(d, centro + 5.8, y_opto + 1.2, 'M7')
        dois(d, centro - 3.3, y_opto + 5.4, False, RESISTOR, 1.6, 0.85, '102')
        dois(d, centro + 0.4, y_opto + 4.4, True, RESISTOR, 1.6, 0.85, '102')
        # o LED do canal (apagado; o editor acende)
        y_led = y_opto + 6.4
        led_0805(d, centro + 3.4, y_led)
        d.texto(centro + 3.4, y_led + 2.0, 'IN' + (sufixo or '') if canais > 1 else 'ON', 0.75, SERIGRAFIA,
                alinhar='center')
        luzes.append((centro + 3.4, y_led, f'canal{c + 1}'))

    # o conector (e a luz de energia ao lado)
    if canais == 1:
        nomes = ['VCC', 'GND', 'IN']
    else:
        nomes = ['GND'] + [f'IN{i + 1}' for i in range(canais)] + ['VCC']
    descricao_entrada = {
        'VCC': 'VCC: 5 V (o 5V da placa ou uma fonte). A bobina do rele puxa ~70 mA daqui por canal ligado',
        'GND': 'GND: o GND da placa (em comum com o VCC)',
    }
    for i, nome in enumerate(nomes):
        x = x0 + i * PASSO
        pino_header(d, x, y_cabo)
        d.texto(x, y_cabo - 2.0, nome, 0.75, SERIGRAFIA, ang=90 if len(nome) > 3 else 0,
                alinhar='center-left' if len(nome) > 3 else 'center')
        desc = descricao_entrada.get(nome) or (
            f'{nome}: o sinal do canal {nome[2:] or "1"}. Em LOW o rele liga (ativo em LOW, pelo optoacoplador: '
            'o pino so acende o LED de dentro, ~2 mA); em HIGH ou solto, desliga')
        pinos.append((nome, x, y_cabo, ['power'] if nome in ('VCC', 'GND') else [], desc))
    x_pwr = x0 + len(nomes) * PASSO + 2.4
    led_0805(d, x_pwr, y_cabo)
    d.texto(x_pwr, y_cabo - 2.0, 'PWR', 0.75, SERIGRAFIA, alinhar='center')
    luzes.insert(0, (x_pwr, y_cabo, 'energia'))
    if canais > 1:
        # o jumper JD-VCC/VCC (no lugar: a bobina tira do VCC)
        xj = larg - 3.2 - 2 * PASSO
        for k, rotulo in enumerate(['GND', 'VCC', 'JD']):
            pino_header(d, xj + k * PASSO, y_cabo)
            d.texto(xj + k * PASSO, y_cabo - 2.0, rotulo, 0.65, SERIGRAFIA, alinhar='center')
        d.ret(xj + PASSO - 1.3, y_cabo - 1.4, PASSO + 2.6, 2.8, '#e8c21c', rx=0.5, stroke='#8a7110', sw=0.2)
        d.ret(xj + PASSO - 0.6, y_cabo - 0.8, PASSO + 1.2, 1.6, '#c9a512', rx=0.3)
    d.texto(larg / 2, alt - 7.6, f'{canais} Relay Module', 1.0, SERIGRAFIA, alinhar='center')

    io.open(pa.BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg_de(d, larg, alt))
    pecas.append({
        'id': id_peca, 'largura': round(larg * A), 'altura': round(alt * A),
        'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                  for n, x, y, s, dsc in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg * A)}x{round(alt * A)} px, {len(pinos)} pinos')
    return [(round(x * A, 2), round(y * A, 2), chave) for x, y, chave in luzes]


luzes = {f'modulo-rele-{n}': modulo(f'modulo-rele-{n}', n) for n in (1, 2, 4)}

cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
# as luzes, em pixels da peca, para o EditorModulos.cs
for id_peca, lista_luzes in luzes.items():
    print(id_peca, lista_luzes)
