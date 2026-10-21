"""python ferramentas/extrair-svg/pecas-pcm5102a.py

O modulo PCM5102A (a plaquinha roxa GY-PCM5102), redesenhado na Alpha 25 na
escala real: 17 x 32 mm, de pe, como na foto do fabricante -- o P2 (fone) no
canto de cima, saindo pela borda da direita; a barra de 6 embaixo (SCK, BCK,
DIN, LCK, GND, VIN) e a de 9 na lateral (LROUT, AGND, ROUT, AGND, A3V3, FMT,
XSMT, DEMP, FLT: as saidas de linha e os quatro pinos de configuracao, que os
jumpers de solda do verso, H1 a H4, ja ligam no nivel de fabrica).

O lugar de cada peca foi medido nas fotos da frente e do verso do modulo
(a da frente esticada pela loja: endireitada pelos 15 furos, que caem no
passo de 2,54 mm). O desenho e nosso. O logotipo da Texas (BB) no chip nao e
desenhado.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f, rodar, mover = pa.Desenho, pa.T, pa.f, pa.rodar, pa.mover
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI
A = 96 / 25.4
L, H = 17.0, 32.0
SOBRA_X = 2.0                                   # o cano do P2, para fora da borda
ROXO, ROXO_BORDA = '#5b1f86', '#3d1260'
CAPACITOR, RESISTOR = '#b7956a', '#1e1f22'

LATERAL = ['LROUT', 'AGND', 'ROUT', 'AGND.2', 'A3V3', 'FMT', 'XSMT', 'DEMP', 'FLT']
BAIXO = ['SCK', 'BCK', 'DIN', 'LCK', 'GND', 'VIN']
X_LATERAL, Y0_LATERAL = 1.4, 3.64
X0_BAIXO, Y_BAIXO = 2.0, 30.5
DESCRICOES = {
    'LROUT': 'Saida de linha do canal esquerdo (a mesma do P2).',
    'ROUT': 'Saida de linha do canal direito (a mesma do P2).',
    'AGND': 'Terra analogico (o mesmo GND do modulo).',
    'A3V3': 'Os 3,3 V do regulador analogico do modulo (saida; nao alimentar por aqui).',
    'FMT': 'Formato do audio: LOW (de fabrica, jumper H4) e I2S; em HIGH o chip le left-justified e o som sai errado.',
    'XSMT': 'Mudo suave: HIGH (de fabrica, jumper H3) toca; em LOW (no GND, ou um pino em LOW) o modulo emudece.',
    'DEMP': 'De-enfase de 44,1 kHz: LOW de fabrica (jumper H2).',
    'FLT': 'Filtro digital: LOW (de fabrica, jumper H1) e o normal; HIGH e o de baixa latencia.',
    'SCK': 'Relogio do sistema: no GND (o jumper ao lado ja liga), o chip tira o relogio do BCK.',
    'BCK': 'Relogio de bit do I2S (o BCLK da placa).', 'DIN': 'Dados do I2S (o DOUT da placa).',
    'LCK': 'Canal esquerdo/direito do I2S (o LRCK, ou WS, da placa).',
    'GND': 'Terra.', 'VIN': 'Alimentacao: 3,3 a 5 V (os reguladores do modulo fazem os 3,3 V).',
}


def dois(d, cx, cy, vertical, corpo, comp=1.6, larg=0.85, marca=None):
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w, h, corpo, rx=0.06)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.06)
    t = comp * 0.22
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')
    if marca:
        d.texto(cx, cy, marca, min(w, h) * 0.42, MARCA_CI, ang=90 if vertical else 0, ancora='middle',
                alinhar='center', peso='normal')


def sot5(d, cx, cy):
    """O regulador SOT-23-5: duas pernas em cima, tres embaixo."""
    for x in (-0.95, 0.95):
        d.ret_c(cx + x, cy - 1.15, 0.45, 0.75, 'url(#metal-h)')
    for x in (-0.95, 0, 0.95):
        d.ret_c(cx + x, cy + 1.15, 0.45, 0.75, 'url(#metal-h)')
    d.ret_c(cx, cy, 2.9, 1.6, PRETO_CI, rx=0.1)
    d.ret_c(cx, cy, 2.9, 1.6, 'url(#luz)', rx=0.1)


def gerar():
    sobra_x = round(SOBRA_X * A + 0.6, 1)
    d = Desenho(T(A, 0, 0, A, sobra_x, 0))
    d.ret(0, 0, L, H, ROXO, rx=0.9, stroke=ROXO_BORDA, sw=0.12)
    d.ret(0, 0, L, H, 'url(#luz)', rx=0.9)

    # ---- a serigrafia ----
    for t, y in (('L', 3.5), ('G', 6.2), ('R', 9.0), ('G', 11.6)):
        d.texto(3.5, y, t, 1.25, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')
    for t, y in (('4', 18.0), ('3', 20.6), ('2', 23.3), ('1.', 25.5)):      # os jumpers H4..H1 (FMT..FLT)
        d.texto(2.65, y, t, 1.1, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')
    for k, t in enumerate(BAIXO):
        d.texto(X0_BAIXO + k * 2.54 + 0.3, 28.1, t, 1.1, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')
    d.texto(3.6, 1.4, 'LINE', 0.9, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')
    d.linha(4.5, 2.3, 4.5, 10.0, SERIGRAFIA, 0.15)

    # ---- os furos das duas barras ----
    for k in range(len(LATERAL)):
        pa.pad_tht(d, {'x': X_LATERAL, 'y': Y0_LATERAL + k * 2.54, 'drill': 1.0, 'diameter': 1.7})
    for k in range(len(BAIXO)):
        pa.pad_tht(d, {'x': X0_BAIXO + k * 2.54, 'y': Y_BAIXO, 'drill': 1.0, 'diameter': 1.7})

    # ---- o jumper de solda do SCK (ja fechado no GND) ----
    for x in (3.25, 3.65, 4.05):
        d.ret_c(x, 27.1, 0.25, 1.1, ESTANHO, rx=0.08)

    # ---- o P2 (fone) de 3,5 mm: o corpo preto, os contatos dourados e o cano para fora ----
    d.ret(17.0, 2.2, SOBRA_X, 5.8, '#16171a', rx=0.4)
    d.ret(17.0, 2.2, SOBRA_X, 5.8, 'url(#luz)', rx=0.4)
    d.ret(4.4, 0.6, 12.6, 9.8, '#1b1c20', rx=0.35, stroke='#0a0a0b', sw=0.08)
    d.ret(4.4, 0.6, 12.6, 9.8, 'url(#luz)', rx=0.35)
    d.poli([(5.0, 1.0), (9.6, 1.0), (9.6, 1.6), (7.4, 1.6), (7.4, 6.6), (5.0, 6.6)], 'url(#ouro)', '#8a6a1c', 0.06)
    d.ret_c(6.2, 3.8, 0.7, 1.8, '#c8a24c', rx=0.3)
    d.ret(4.8, 7.8, 1.2, 2.6, 'url(#metal)')
    for x in (10.8, 15.2):
        d.ret_c(x, 8.1, 1.0, 5.4, 'url(#ouro)', rx=0.15, stroke='#8a6a1c', sw=0.05)
        d.ret_c(x, 6.3, 1.6, 0.4, 'url(#ouro)')
    d.ret_c(14.85, 2.0, 1.1, 2.0, None, stroke='#c8a24c', sw=0.18)

    # ---- a fileira de 0603 embaixo do P2 (e o LED de energia, no canto) ----
    for x, corpo, marca in ((4.85, CAPACITOR, None), (6.05, RESISTOR, '471'), (7.4, RESISTOR, '471'),
                            (8.9, CAPACITOR, None), (11.1, CAPACITOR, None), (12.6, CAPACITOR, None),
                            (14.1, RESISTOR, '102')):
        dois(d, x, 12.9, True, corpo, 1.8, 0.85, marca)
    d.ret_c(15.5, 12.9, 0.85, 1.8, '#f2f1eb', rx=0.08)
    d.ret_c(15.5, 12.9, 0.55, 0.9, '#dfe9e2', rx=0.1)
    d.ret(15.075, 12.0, 0.85, 0.35, 'url(#metal)')
    d.ret(15.075, 13.45, 0.85, 0.35, 'url(#metal)')

    # ---- os capacitores dos reguladores e os dois SOT-23-5 ----
    for x, y, comp, larg in ((4.5, 15.0, 2.6, 1.6), (4.35, 16.75, 2.0, 1.1), (14.8, 15.3, 2.6, 1.6),
                             (14.8, 17.3, 2.0, 1.1), (4.35, 23.0, 2.0, 1.1), (14.8, 23.3, 2.0, 1.1)):
        dois(d, x, y, False, CAPACITOR, comp, larg)
    sot5(d, 4.4, 19.8)
    sot5(d, 14.9, 19.8)

    # ---- o PCM5102A (TSSOP-20), virado como na placa ----
    cx, cy = 9.7, 19.0
    for k in range(10):
        x = cx + (k - 4.5) * 0.65
        d.ret_c(x, cy - 2.6, 0.3, 0.9, 'url(#metal-h)')
        d.ret_c(x, cy + 2.6, 0.3, 0.9, 'url(#metal-h)')
    d.entrar(mover(cx, cy) * rodar(180))
    pa.ci(d, 6.5, 4.4, [('APFQ4', 0.62), ('39T', 0.62), ('PCM5102A', 0.75)], pino1=(-2.8, -1.6))
    d.sair()

    # ---- a rede de resistores (220) e os capacitores de baixo ----
    d.ret_c(7.85, 25.6, 3.2, 1.6, RESISTOR, rx=0.08)
    for k in range(4):
        x = 7.85 + (k - 1.5) * 0.8
        d.ret_c(x, 24.95, 0.45, 0.35, 'url(#metal)')
        d.ret_c(x, 26.25, 0.45, 0.35, 'url(#metal)')
    d.texto(7.85, 25.6, '220', 0.7, MARCA_CI, ancora='middle', alinhar='center', peso='normal')
    for x, comp, larg in ((10.35, 1.6, 0.9), (12.3, 1.8, 1.25), (13.45, 1.6, 0.85), (14.55, 1.8, 1.25)):
        dois(d, x, 25.6, True, CAPACITOR, comp, larg)

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(L * A + 2 * sobra_x)}px" '
           f'height="{f(H * A)}px" data-sobra="{f(sobra_x)} 0" viewBox="0 0 {f(L * A + 2 * sobra_x)} {f(H * A)}">\n'
           f'<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(pa.BASE + 'app/assets/pcm5102a.svg', 'w', encoding='utf-8', newline='').write(svg)

    # ---- os pinos no pecas.json ----
    cam = pa.BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    peca = next(p for p in lista if p['id'] == 'pcm5102a')
    antigos = {q['nome']: q for q in peca['pinos']}
    novos = []
    pos = [(n, X_LATERAL, Y0_LATERAL + k * 2.54) for k, n in enumerate(LATERAL)]
    pos += [(n, X0_BAIXO + k * 2.54, Y_BAIXO) for k, n in enumerate(BAIXO)]
    for nome, x, y in pos:
        q = dict(antigos.get(nome) or {'nome': nome, 'sinais': ['power'] if nome.split('.')[0] in ('AGND', 'A3V3') else []})
        q['x'], q['y'] = round(x * A, 2), round(y * A, 2)
        q['descricao'] = DESCRICOES[nome.split('.')[0]]
        novos.append(q)
    peca['largura'], peca['altura'], peca['pinos'] = round(L * A), round(H * A), novos
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f'pcm5102a: {round(L * A)}x{round(H * A)} px (sobra {sobra_x} px), {len(novos)} pinos; '
          f'LED em ({15.5 * A:.1f}, {12.9 * A:.1f}) px')


if __name__ == '__main__':
    gerar()
