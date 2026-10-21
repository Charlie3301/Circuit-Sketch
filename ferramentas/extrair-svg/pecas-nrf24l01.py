"""python ferramentas/extrair-svg/pecas-nrf24l01.py

O modulo de radio nRF24L01+ (o comum, preto, de antena na placa)
redesenhado (Alpha 25), na escala real: 15 x 29 mm, de pe, com a barra em
cima e a antena embaixo. O lugar de cada peca foi medido numa foto do modulo
(Wikimedia Commons, "NRF24l01 Nordic Semiconductor radio.JPG", de Dmcole,
CC BY-SA 3.0), endireitada pelos quatro cantos da placa. O desenho e nosso.

A barra 2x4 fica atravessada na ponta (4 pinos na largura, 2 fileiras), com
o pino 1 (GND, a ilha quadrada) no canto: na fileira de fora, da direita para
a esquerda, GND, CE, SCK e MISO; na de dentro, VCC, CSN, MOSI e IRQ.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f = pa.Desenho, pa.T, pa.f
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI
A = 96 / 25.4
L, H = 15.0, 29.0
MASCARA, BORDA = '#141516', '#050506'
COBRE = '#b8923e'                      # a antena: cobre estanhado sob a mascara

COLUNAS = [13.21, 10.67, 8.13, 5.59]   # da direita para a esquerda
FILEIRAS = [1.27, 3.81]
PINOS = [('GND', 'VCC'), ('CE', 'CSN'), ('SCK', 'MOSI'), ('MISO', 'IRQ')]
DESCRICOES = {
    'GND': 'Terra.', 'VCC': 'Alimentacao: 1,9 a 3,6 V (no 3V3; 5 V queima o modulo).',
    'CE': 'Chip Enable: liga a transmissao ou a recepcao.', 'CSN': 'Chip Select do SPI (ativo em LOW).',
    'SCK': 'Relogio do SPI.', 'MOSI': 'Dados do SPI, da placa para o radio.',
    'MISO': 'Dados do SPI, do radio para a placa.', 'IRQ': 'Interrupcao (ativa em LOW): chegou pacote ou terminou o envio.',
}


def dois(d, cx, cy, vertical, corpo, comp=2.0, larg=1.25):
    """Um 0805 (ou 0603) com os terminais estanhados."""
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w + 0.5, h + 0.5, None, rx=0.15, stroke=SERIGRAFIA, sw=0.12)
    d.ret_c(cx, cy, w, h, corpo, rx=0.08)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.08)
    t = comp * 0.22
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')


def gerar():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.ret(0, 0, L, H, MASCARA, rx=0.35, stroke=BORDA, sw=0.12)
    d.ret(0, 0, L, H, 'url(#luz)', rx=0.35)

    # ---- a antena (o "F" invertido em meandro, de cobre sob a mascara) ----
    meandro = [(11.7, 20.2), (11.7, 28.3), (10.3, 28.3), (10.3, 24.4), (8.9, 24.4), (8.9, 28.3), (7.5, 28.3),
               (7.5, 24.4), (6.0, 24.4), (6.0, 28.3), (4.4, 28.3), (4.4, 24.4)]
    d.poli(meandro, None, COBRE, 0.55, fechado=False, cap='round')
    d.linha(11.7, 22.4, 3.9, 22.4, COBRE, 0.55)
    d.circ(3.9, 22.4, 0.45, COBRE)
    d.linha(4.4, 28.3, 0.6, 28.3, COBRE, 0.55)

    # ---- a barra 2x4: a moldura da serigrafia e as ilhas (o pino 1 quadrado) ----
    d.ret(4.2, 0.0, 10.4, 5.05, None, stroke=SERIGRAFIA, sw=0.18)
    d.ret(12.0, 0.0, 2.6, 2.55, None, stroke=SERIGRAFIA, sw=0.14)
    for k, x in enumerate(COLUNAS):
        for j, y in enumerate(FILEIRAS):
            if k == 0 and j == 0:
                d.ret_c(x, y, 1.6, 1.6, OURO, rx=0.1)
            else:
                d.circ(x, y, 0.82, OURO)
            d.circ(x, y, 0.68, 'url(#lata)', stroke='#7c838b', sw=0.05)
            d.ret_c(x, y, 0.64, 0.64, '#9aa1a8', rx=0.1)

    # ---- o cristal de 16 MHz (HC-49/S), deitado pela borda ----
    for y in (5.6, 15.6):
        d.ret_c(2.45, y, 1.2, 1.0, ESTANHO)
    d.ret_c(2.45, 10.6, 4.65, 11.05, 'url(#metal)', rx=2.3, stroke='#7c838b', sw=0.08)
    d.ret_c(2.45, 10.6, 3.95, 10.35, None, rx=1.95, stroke='#c9ced3', sw=0.12)
    d.texto(2.45, 10.6, '16.000', 1.0, '#5a5f66', ang=90, ancora='middle', alinhar='center', peso='normal')

    # ---- o nRF24L01+ (QFN20 de 4 mm) e o resistor de 1 Mohm do cristal ----
    for k in range(5):
        o = (k - 2) * 0.5
        for (x, y, w, h) in ((8.8 + o, 8.95, 0.25, 0.45), (8.8 + o, 13.25, 0.25, 0.45),
                             (6.65, 11.1 + o, 0.45, 0.25), (10.95, 11.1 + o, 0.45, 0.25)):
            d.ret_c(x, y, w, h, ESTANHO)
    pa.ci_em(d, 8.8, 11.1, 4.0, 4.0, [('1726AK', 0.45), ('24L01+', 0.55), ('NRF', 0.62)], pino1=(-1.45, 1.45))
    dois(d, 5.55, 12.2, True, '#1e1f22')
    d.texto(5.55, 12.2, '105', 0.55, MARCA_CI, ang=90, ancora='middle', alinhar='center', peso='normal')

    # ---- os capacitores e o indutor da direita; os de cima e de baixo ----
    for y, corpo in ((7.4, '#b7956a'), (9.2, '#b7956a'), (11.2, '#b7956a'), (13.0, '#3a3c40')):
        dois(d, 13.7, y, False, corpo)
    dois(d, 2.0, 4.05, True, '#a98f78', 2.0, 1.25)
    dois(d, 2.0, 18.9, True, '#a98f78', 2.0, 1.25)

    # ---- a rede de RF (os 0603 entre o chip e a antena) ----
    for x, y, vertical, corpo in ((8.6, 14.9, False, '#e9e6de'), (7.25, 16.35, False, '#e9e6de'),
                                  (10.5, 16.35, False, '#e9e6de'), (4.95, 18.75, True, '#b7956a'),
                                  (6.55, 18.75, True, '#8d8f93'), (8.0, 18.75, True, '#b7956a'),
                                  (10.0, 18.75, True, '#8d8f93'), (11.4, 18.75, True, '#b7956a'),
                                  (13.7, 19.4, False, '#8d8f93')):
        dois(d, x, y, vertical, corpo, 1.6, 0.85)

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(L * A)}px" height="{f(H * A)}px" '
           f'viewBox="0 0 {f(L * A)} {f(H * A)}">\n<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(pa.BASE + 'app/assets/nrf24l01.svg', 'w', encoding='utf-8', newline='').write(svg)

    # ---- os pinos no pecas.json ----
    cam = pa.BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    peca = next(p for p in lista if p['id'] == 'nrf24l01')
    antigos = {q['nome']: q for q in peca['pinos']}
    novos = []
    for (fora, dentro), x in zip(PINOS, COLUNAS):
        for nome, y in ((fora, FILEIRAS[0]), (dentro, FILEIRAS[1])):
            q = dict(antigos[nome])
            q['x'], q['y'] = round(x * A, 2), round(y * A, 2)
            if not q.get('descricao'):
                q['descricao'] = DESCRICOES[nome]
            novos.append(q)
    peca['largura'], peca['altura'], peca['pinos'] = round(L * A), round(H * A), novos
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f'nrf24l01: {round(L * A)}x{round(H * A)} px, {len(novos)} pinos')


if __name__ == '__main__':
    gerar()
