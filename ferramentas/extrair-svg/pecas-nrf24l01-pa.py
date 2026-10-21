"""python ferramentas/extrair-svg/pecas-nrf24l01-pa.py

O nRF24L01+PA+LNA (Alpha 25): o modulo de radio com amplificador (RFX2401C)
e antena externa, na escala real -- 15,5 x 41,5 mm, de pe, com a mesma barra
2x4 do nRF24L01 em cima (GND no canto) e o conector SMA embaixo. A antena de
borracha (uns 11 cm) fica dobrada para cima na articulacao, como se usa na
bancada: de cima aparecem o cano do SMA, a base serrilhada e a ponta dela.

O lugar de cada peca foi medido numa foto do modulo (Wikimedia Commons,
"NRF24l01-pa-lna-duck.jpg", de Dmcole, CC BY-SA 3.0), endireitada pelos
quatro cantos da placa. O desenho e nosso.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))


def carregar(nome, arquivo):
    spec = importlib.util.spec_from_file_location(nome, os.path.join(AQUI, arquivo))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


pa = carregar('placas_arduino', 'placas-arduino.py')
nrf = carregar('pecas_nrf', 'pecas-nrf24l01.py')

Desenho, T, f = pa.Desenho, pa.T, pa.f
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI
A = 96 / 25.4
L, H = 15.5, 41.5
FORA = 17.9                                   # o SMA e a antena, abaixo da borda
ID = 'nrf24l01-pa-lna'

COLUNAS = [11.65, 9.11, 6.57, 4.03]           # da direita para a esquerda (GND, CE, SCK, MISO)
FILEIRAS = [1.27, 3.81]
TANTALO = '#e0a72e'

DEFS_EXTRA = '''  <radialGradient id="ponta" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#4a4d52"/><stop offset="0.7" stop-color="#1c1d20"/><stop offset="1" stop-color="#0c0c0e"/></radialGradient>
  <linearGradient id="borracha" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#101113"/><stop offset="0.45" stop-color="#3a3d42"/><stop offset="1" stop-color="#0e0f11"/></linearGradient>
'''


def tantalo(d, cx, cy, vertical):
    """O capacitor de tantalo (caixa B): o corpo amarelo, a faixa do + e os terminais."""
    w, h = (2.8, 3.5) if vertical else (3.5, 2.8)
    d.ret_c(cx, cy, w + 0.5, h + 0.5, None, rx=0.15, stroke=SERIGRAFIA, sw=0.12)
    d.ret_c(cx, cy, w, h, TANTALO, rx=0.15)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.15)
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, 0.6, '#b9842a')
        d.ret(cx - w / 2 + 0.4, cy + h / 2 - 0.45, w - 0.8, 0.45, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, 0.6, h, '#b9842a')
        d.ret(cx + w / 2 - 0.45, cy - h / 2 + 0.4, 0.45, h - 0.8, 'url(#metal)')


def gerar():
    sobra_y = round(FORA * A + 0.6, 1)
    d = Desenho(T(A, 0, 0, A, 0, sobra_y))
    d.ret(0, 0, L, H, nrf.MASCARA, rx=0.35, stroke=nrf.BORDA, sw=0.12)
    d.ret(0, 0, L, H, 'url(#luz)', rx=0.35)

    # ---- a barra 2x4 (a mesma do nRF24L01): a moldura e o pino 1 quadrado ----
    d.ret(2.65, 0.0, 10.4, 4.6, None, stroke=SERIGRAFIA, sw=0.18)
    d.ret(10.35, 0.0, 2.7, 2.4, None, stroke=SERIGRAFIA, sw=0.14)
    d.texto(1.6, 2.6, 'J1', 0.9, SERIGRAFIA, ang=-90, ancora='middle', alinhar='center', peso='normal')
    for k, x in enumerate(COLUNAS):
        for j, y in enumerate(FILEIRAS):
            if k == 0 and j == 0:
                d.ret_c(x, y, 1.6, 1.6, OURO, rx=0.1)
            else:
                d.circ(x, y, 0.82, OURO)
            d.circ(x, y, 0.68, 'url(#lata)', stroke='#7c838b', sw=0.05)
            d.ret_c(x, y, 0.64, 0.64, '#9aa1a8', rx=0.1)

    # ---- os tantalos, o cristal e o nRF24L01+ ----
    tantalo(d, 12.85, 11.8, False)
    tantalo(d, 1.75, 29.6, True)
    d.ret_c(3.2, 16.95, 3.6, 5.6, None, rx=0.2, stroke=SERIGRAFIA, sw=0.12)
    d.ret_c(3.2, 16.95, 3.2, 5.0, '#b8923e', rx=0.2)
    d.ret_c(3.2, 16.95, 2.8, 4.6, 'url(#metal)', rx=0.15)
    d.texto(3.2, 16.95, '16.000', 0.62, '#5a5f66', ang=90, ancora='middle', alinhar='center', peso='normal')
    for y in (14.9, 16.55, 18.2):
        nrf.dois(d, 5.65, y, False, '#1e1f22', 1.6, 0.85)
    for k in range(5):
        o = (k - 2) * 0.5
        for (x, y, w, h) in ((9.2 + o, 13.75, 0.25, 0.45), (9.2 + o, 18.05, 0.25, 0.45),
                             (7.05, 15.9 + o, 0.45, 0.25), (11.35, 15.9 + o, 0.45, 0.25)):
            d.ret_c(x, y, w, h, ESTANHO)
    pa.ci_em(d, 9.2, 15.9, 4.0, 4.0, [('1730AL', 0.42), ('24L01+', 0.55), ('NRF', 0.62)], pino1=(-1.45, 1.45))

    # ---- o PA/LNA (RFX2401C, QFN16 de 3 mm) e a rede de RF ----
    for k in range(4):
        o = (k - 1.5) * 0.5
        for (x, y, w, h) in ((10.1 + o, 29.3, 0.25, 0.4), (10.1 + o, 32.5, 0.25, 0.4),
                             (8.5, 30.9 + o, 0.4, 0.25), (11.7, 30.9 + o, 0.4, 0.25)):
            d.ret_c(x, y, w, h, ESTANHO)
    pa.ci_em(d, 10.1, 30.9, 3.0, 3.0, [('2401C', 0.45)], pino1=(-1.05, 1.05))
    rf = [(13.0, 15.4, False, '#b7956a'), (13.0, 17.4, False, '#1e1f22'), (14.2, 16.4, True, '#b7956a'),
          (7.0, 19.6, True, '#b7956a'), (8.3, 19.6, True, '#e9e6de'), (9.6, 19.6, True, '#b7956a'),
          (10.9, 19.6, True, '#8d8f93'), (8.3, 21.8, True, '#e9e6de'), (9.6, 21.8, True, '#b7956a'),
          (7.0, 21.8, True, '#1e1f22'), (14.6, 21.5, True, '#b7956a'), (1.3, 23.4, False, '#b7956a'),
          (8.0, 23.9, False, '#e9e6de'), (11.3, 23.4, False, '#8d8f93'), (13.0, 25.2, False, '#b7956a'),
          (9.1, 26.6, True, '#1e1f22'), (10.3, 26.6, True, '#e9e6de'), (3.9, 27.8, False, '#b7956a'),
          (5.9, 27.8, False, '#8d8f93'), (7.7, 27.8, False, '#b7956a'), (13.6, 27.6, True, '#e9e6de'),
          (14.6, 29.4, True, '#b7956a'), (13.3, 30.6, True, '#8d8f93'), (12.6, 33.0, False, '#e9e6de'),
          (8.4, 33.3, False, '#b7956a'), (7.4, 30.6, True, '#1e1f22')]
    for x, y, vertical, corpo in rf:
        nrf.dois(d, x, y, vertical, corpo, 1.4, 0.75)
    d.texto(5.65, 32.4, 'NRF24L01+PA+LNA', 0.66, SERIGRAFIA, ang=-90, ancora='middle', alinhar='center', peso='normal')
    d.texto(4.55, 34.2, 'Ver:3.1', 0.66, SERIGRAFIA, ang=-90, ancora='middle', alinhar='center', peso='normal')

    # ---- o SMA de borda: a base soldada, o cano rosqueado e a antena dobrada para cima ----
    for x in (5.2, 10.3):
        d.ret_c(x, 39.2, 1.6, 4.2, ESTANHO, rx=0.15)
    d.ret_c(7.75, 39.0, 6.4, 5.0, 'url(#ouro)', rx=0.25, stroke='#8a6a1c', sw=0.08)
    d.ret_c(7.75, 39.0, 5.0, 3.8, None, rx=0.2, stroke='#f6dc8c', sw=0.12)
    d.ret(5.55, H, 4.4, 3.6, 'url(#ouro)')
    for k in range(7):
        d.linha(5.55, H + 0.4 + k * 0.5, 9.95, H + 0.4 + k * 0.5, '#9c7a26', 0.12)
    d.ret(4.95, H + 3.6, 5.6, 2.0, '#c9a24c', rx=0.25)                 # a porca sextavada
    d.ret(4.95, H + 3.6, 5.6, 2.0, 'url(#luz)', rx=0.25)
    d.ret(4.6, H + 5.6, 6.3, 6.4, 'url(#borracha)', rx=0.6)            # a base serrilhada da antena
    for k in range(10):
        x = 4.9 + k * 0.62
        d.linha(x, H + 5.9, x, H + 11.7, '#2a2c30', 0.16)
    d.circ(7.75, H + 12.6, 4.6, 'url(#ponta)', stroke='#060607', sw=0.12)   # a antena, vista de ponta
    d.circ(7.75, H + 12.6, 3.2, None, '#2e3035', 0.18)
    d.circ(6.7, H + 11.5, 0.9, '#ffffff', op=0.08)

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(L * A)}px" '
           f'height="{f(H * A + 2 * sobra_y)}px" data-sobra="0 {f(sobra_y)}" '
           f'viewBox="0 0 {f(L * A)} {f(H * A + 2 * sobra_y)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n'
           + ''.join(d.partes) + '</svg>\n')
    io.open(pa.BASE + f'app/assets/{ID}.svg', 'w', encoding='utf-8', newline='').write(svg)

    # ---- a peca no pecas.json (os mesmos pinos do nRF24L01) ----
    cam = pa.BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    base = next(p for p in lista if p['id'] == 'nrf24l01')
    sinais = {q['nome']: q.get('sinais', []) for q in base['pinos']}
    pinos = []
    for (fora, dentro), x in zip(nrf.PINOS, COLUNAS):
        for nome, y in ((fora, FILEIRAS[0]), (dentro, FILEIRAS[1])):
            desc = nrf.DESCRICOES[nome]
            if nome == 'VCC':
                desc = 'Alimentacao: 1,9 a 3,6 V (no 3V3; 5 V queima). Transmitindo, o amplificador puxa ate ~115 mA.'
            pinos.append({'nome': nome, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': sinais.get(nome, []),
                          'descricao': desc})
    peca = {'id': ID, 'largura': round(L * A), 'altura': round(H * A), 'pinos': pinos, 'variantes': [], 'metades': False}
    lista = [p for p in lista if p['id'] != ID] + [peca]
    lista.sort(key=lambda p: p['id'])
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f'{ID}: {peca["largura"]}x{peca["altura"]} px (sobra {sobra_y} px), {len(pinos)} pinos')


if __name__ == '__main__':
    gerar()
