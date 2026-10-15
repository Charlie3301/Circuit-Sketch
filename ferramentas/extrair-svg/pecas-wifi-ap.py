"""python ferramentas/extrair-svg/pecas-wifi-ap.py

O ponto de acesso WiFi (wifi-ap) redesenhado (Alpha 25): um roteador de casa
generico, de frente, sem marca -- o corpo preto, as duas antenas e o painel com
os quatro LEDs e os icones de cada um (energia, WiFi, clientes e internet).
A luz de cada LED e do simulador (EditorModulos.cs): a energia com a simulacao
rodando, o WiFi com a rede nomeada, os clientes com alguma placa conectada a
ela; a internet com o "Usar internet" ligado.
"""
import importlib.util
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f = pa.Desenho, pa.T, pa.f
A = 96 / 25.4
L, H = 40.0, 30.0           # a caixa da peca, em mm

DEFS = '''  <linearGradient id="corpo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3a3f46"/><stop offset="0.5" stop-color="#22262b"/><stop offset="1" stop-color="#15181b"/></linearGradient>
  <linearGradient id="painel" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2e3238"/><stop offset="1" stop-color="#1c1f23"/></linearGradient>
  <linearGradient id="antena" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#16191c"/><stop offset="0.45" stop-color="#3b4046"/><stop offset="1" stop-color="#121417"/></linearGradient>
  <linearGradient id="brilho" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0.22"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></linearGradient>
'''
ICONE = '#8b96a2'
LEDS = [7.6, 15.9, 24.1, 32.4]          # o x de cada LED; todos em y = 24.6


def gerar():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    # ---- as antenas (atras do corpo), com a junta articulada ----
    for x in (6.5, 33.5):
        d.ret(x - 1.1, 0.6, 2.2, 15.0, 'url(#antena)', rx=1.1)
        d.ret(x - 1.1, 0.6, 2.2, 15.0, None, rx=1.1, stroke='#0c0d0f', sw=0.12)
        d.ret(x - 1.35, 13.2, 2.7, 2.4, '#2a2e33', rx=0.5)
        d.linha(x - 0.6, 2.0, x - 0.6, 12.0, '#5c636b', 0.18, so=0.6)
    # ---- o corpo e os pes ----
    for x in (5.0, 35.0):
        d.ret(x - 2.2, 28.4, 4.4, 1.2, '#0f1113', rx=0.5)
    d.ret(1.5, 14.5, 37.0, 14.3, 'url(#corpo)', rx=2.2, stroke='#0d0f11', sw=0.2)
    d.ret(2.2, 15.0, 35.6, 4.2, 'url(#brilho)', rx=1.6)
    # as fendas de ventilacao em cima
    for k in range(9):
        x = 9.5 + k * 2.6
        d.ret(x, 16.4, 1.5, 0.45, '#0e1012', rx=0.2)
    # ---- o painel da frente: o icone sobre cada LED ----
    d.ret(3.2, 20.2, 33.6, 6.6, 'url(#painel)', rx=1.2, stroke='#3f454c', sw=0.08)
    # energia
    x = LEDS[0]
    d.caminho([('M', x - 0.95, 21.75), ('A', 1.25, 1, 0, x + 0.95, 21.75)], None, ICONE, 0.28, cap='round')
    d.linha(x, 20.9, x, 22.3, ICONE, 0.28)
    # WiFi
    x = LEDS[1]
    for r in (0.55, 1.05, 1.55):
        d.caminho([('M', x - r * 0.8, 22.9 - r * 0.6), ('A', r, 0, 1, x + r * 0.8, 22.9 - r * 0.6)], None, ICONE, 0.26, cap='round')
    d.circ(x, 22.9, 0.2, ICONE)
    # clientes: o notebook
    x = LEDS[2]
    d.ret(x - 1.0, 21.0, 2.0, 1.3, None, rx=0.15, stroke=ICONE, sw=0.24)
    d.linha(x - 1.35, 22.65, x + 1.35, 22.65, ICONE, 0.28)
    # internet: o globo
    x = LEDS[3]
    d.circ(x, 21.85, 0.95, None, ICONE, 0.24)
    d.linha(x - 0.95, 21.85, x + 0.95, 21.85, ICONE, 0.18)
    d.caminho([('M', x, 20.9), ('A', 1.3, 0, 1, x, 22.8)], None, ICONE, 0.18)
    d.caminho([('M', x, 20.9), ('A', 1.3, 0, 0, x, 22.8)], None, ICONE, 0.18)
    # ---- os LEDs apagados (a luz e do simulador) ----
    for x in LEDS:
        d.ret(x - 0.9, 24.25, 1.8, 0.7, '#0b0d0f', rx=0.35)
        d.ret(x - 0.7, 24.35, 1.4, 0.5, '#3a4048', rx=0.25)
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(L * A)}px" height="{f(H * A)}px" '
           f'viewBox="0 0 {f(L * A)} {f(H * A)}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    open(pa.BASE + 'app/assets/wifi-ap.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'wifi-ap: {round(L * A)}x{round(H * A)} px; LEDs em y={24.6 * A:.1f}: '
          + ', '.join(f'{x * A:.1f}' for x in LEDS))


if __name__ == '__main__':
    gerar()
