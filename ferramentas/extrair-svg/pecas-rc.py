"""python ferramentas/extrair-svg/pecas-rc.py

As pecas de aeromodelo da Alpha 23:

  - esc : o ESC de 30 A (o controlador do motor brushless), no termo-retratil
          azul. Na esquerda os tres fios do motor (A, B, C); na direita os dois
          da bateria (BAT+ vermelho, BAT- preto) e o cabo de servo (SINAL,
          5V do BEC, GND), como na peca de verdade.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def gravar_svg(nome, svg):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, larg_mm, alt_mm, pinos):
    pecas.append({
        'id': id_peca,
        'largura': round(larg_mm * PX),
        'altura': round(alt_mm * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2),
                   'sinais': s, 'descricao': d}
                  for n, x, y, s, d in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg_mm * PX)}x{round(alt_mm * PX)} px, {len(pinos)} pinos')


def svg_completo(l, a, corpo):
    return ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
            + f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">' + chr(10)
            + corpo + chr(10) + '</svg>' + chr(10))


def fio(d, cor, borda, largura=0.9):
    return (f'  <path d="{d}" fill="none" stroke="{borda}" stroke-width="{largura + 0.35:.2f}" stroke-linecap="round"/>\n'
            f'  <path d="{d}" fill="none" stroke="{cor}" stroke-width="{largura:.2f}" stroke-linecap="round"/>\n')


def ponta(x, y):
    return (f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.75" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
            f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.32" fill="#2a2a2a"/>\n')


def texto(x, y, t, tam=1.2, cor='#f2f2f2', ancora='middle'):
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}"\n'
            f'        font-family="Verdana" font-weight="bold">{t}</text>\n')


# =====================================================================
#  ESC 30 A
# =====================================================================
l, a = 66.0, 30.0
x0, x1, y0, y1 = 14.0, 52.0, 6.0, 24.0       # o corpo no termo-retratil
xs_motor = 1.5
ys_motor = [a / 2 - PASSO * 2, a / 2, a / 2 + PASSO * 2]    # A, B, C (5,08 mm)
x_dir = l - 1.5
ys_bat = [6.0, 6.0 + 2 * PASSO]                              # BAT+, BAT-
ys_servo = [18.7, 18.7 + PASSO, 18.7 + 2 * PASSO]           # SINAL, 5V, GND

corpo = ''
for y, letra in zip(ys_motor, 'ABC'):
    corpo += fio(f'M{x0:.2f} {a / 2 + (y - a / 2) * 0.3:.2f} C{x0 - 6:.2f} {a / 2 + (y - a / 2) * 0.3:.2f} {xs_motor + 5:.2f} {y:.2f} {xs_motor:.2f} {y:.2f}',
                 '#2b2d31', '#7a808a', 1.2)
for y, (cor, borda) in zip(ys_bat, [('#d23b33', '#7d1f1a'), ('#2b2d31', '#7a808a')]):
    corpo += fio(f'M{x1:.2f} {9.0 + (y - 6.0) * 0.4:.2f} C{x1 + 6:.2f} {9.0 + (y - 6.0) * 0.4:.2f} {x_dir - 5:.2f} {y:.2f} {x_dir:.2f} {y:.2f}',
                 cor, borda, 1.3)
for y, (cor, borda) in zip(ys_servo, [('#f2f2ee', '#8f8f8a'), ('#d23b33', '#7d1f1a'), ('#7a4a2a', '#3d2515')]):
    corpo += fio(f'M{x1:.2f} {20.0 + (y - 18.7) * 0.4:.2f} C{x1 + 6:.2f} {20.0 + (y - 18.7) * 0.4:.2f} {x_dir - 5:.2f} {y:.2f} {x_dir:.2f} {y:.2f}',
                 cor, borda, 0.7)
for y in ys_motor:
    corpo += ponta(xs_motor, y)
for y in ys_bat + ys_servo:
    corpo += ponta(x_dir, y)
corpo += f'''  <rect x="{x0}" y="{y0}" width="{x1 - x0}" height="{y1 - y0}" rx="2.4" fill="#1f5fb4" stroke="#123e7a" stroke-width="0.35"/>
  <rect x="{x0 + 1.2:.2f}" y="{y0 + 1.0:.2f}" width="{x1 - x0 - 2.4:.2f}" height="2.0" rx="1.0" fill="#ffffff" fill-opacity="0.12"/>
  <rect x="{x0 + 6.0:.2f}" y="{y0 + 4.2:.2f}" width="{x1 - x0 - 12.0:.2f}" height="9.6" rx="0.8" fill="#f2f2ee"/>
{texto((x0 + x1) / 2, y0 + 9.6, "30A", 3.4, "#1f5fb4")}{texto((x0 + x1) / 2, y0 + 12.6, "BRUSHLESS ESC", 1.25, "#1f5fb4")}{texto(x0 + 2.4, a / 2 - 6.6, "A B C", 1.0, "#cfe0f7", "start")}{texto(x1 - 1.0, 4.6, "BAT", 1.1, "#cfe0f7", "end")}{texto(x1 - 1.0, 27.4, "SERVO", 1.1, "#cfe0f7", "end")}'''
for y, n in zip(ys_motor, 'ABC'):
    corpo += texto(xs_motor + 1.6, y - 1.0, n, 1.1, '#e6ecf5', 'start')
for y, n in zip(ys_bat, ['+', '-']):
    corpo += texto(x_dir - 1.8, y - 0.9, n, 1.5, '#e6ecf5', 'end')
for y, n in zip(ys_servo, ['S', '5V', 'G']):
    corpo += texto(x_dir - 1.8, y + 0.4, n, 0.9, '#e6ecf5', 'end')
gravar_svg('esc', f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">\n{corpo}\n</svg>\n')

desc = {
    'A': 'Fase A do motor brushless (os tres fios nao tem ordem: trocar dois inverte o giro)',
    'B': 'Fase B do motor brushless', 'C': 'Fase C do motor brushless',
    'BAT+': 'Bateria + (LiPo de 2S a 4S: 7,4 a 14,8 V)',
    'BAT-': 'Bateria - (e o mesmo GND do cabo de servo)',
    'SINAL': 'Fio branco: o sinal de servo (1000 us parado, 2000 us maximo), de um GPIO ou do receptor',
    '5V': 'Fio vermelho: o BEC, SAIDA de 5 V (2 A) que alimenta o receptor -- ou a placa, pelo VIN',
    'GND': 'Fio preto/marrom: GND (o mesmo da bateria; ligue no GND da placa)',
}
guardar('esc', l, a,
        [(n, xs_motor, y, [], desc[n]) for n, y in zip('ABC', ys_motor)]
        + [(n, x_dir, y, ['power'], desc[n]) for n, y in zip(['BAT+', 'BAT-'], ys_bat)]
        + [(n, x_dir, y, ['power'] if n != 'SINAL' else [], desc[n]) for n, y in zip(['SINAL', '5V', 'GND'], ys_servo)])


# =====================================================================
#  Receptor FlySky FS-iA6B
# =====================================================================
l, a = 46.0, 28.0
nomes = ['CH1', 'CH2', 'CH3', 'CH4', 'CH5', 'CH6', 'PPM', 'IBUS', 'VCC', 'GND']
x_p = l - 1.6
ys = [1.6 + i * PASSO for i in range(len(nomes))]
corpo = f'''  <!-- as duas antenas -->
  <path d="M3.0 6.0 C-1.0 4.0 -0.5 1.0 1.5 0.6" fill="none" stroke="#1b1b1d" stroke-width="0.7"/>
  <path d="M3.0 20.0 C-1.0 22.0 -0.5 26.0 1.5 27.2" fill="none" stroke="#1b1b1d" stroke-width="0.7"/>
  <rect x="3.0" y="2.0" width="{x_p - 7.0:.2f}" height="24.0" rx="1.6" fill="#232428" stroke="#0b0b0c" stroke-width="0.35"/>
  <rect x="4.4" y="3.4" width="{x_p - 9.8:.2f}" height="21.2" rx="1.0" fill="#2c2d32"/>
''' + texto((3.0 + x_p - 4.0) / 2, 11.6, "FLYSKY", 2.0, "#e6e6e6") + texto((3.0 + x_p - 4.0) / 2, 14.6, "FS-iA6B", 1.7, "#b9bec6")     + texto((3.0 + x_p - 4.0) / 2, 17.4, "2.4GHz  AFHDS 2A", 1.0, "#8e949c") + f'''  <!-- o LED (o editor acende: fixo ligado ao controle, piscando sem ele) -->
  <circle cx="8.0" cy="22.6" r="0.9" fill="#5a1414" stroke="#2a0b0b" stroke-width="0.2"/>
  <rect x="{x_p - 4.0:.2f}" y="0.4" width="5.6" height="{a - 0.8:.2f}" rx="0.5" fill="#1b1b1d"/>
'''
for n, y in zip(nomes, ys):
    corpo += (f'  <rect x="{x_p - 0.4:.2f}" y="{y - 0.4:.2f}" width="0.8" height="0.8" fill="#d9b44a"/>' + chr(10)
              + texto(x_p - 1.6, y + 0.4, n, 0.95, '#e6ecf5', 'end'))
gravar_svg('receptor-rc', svg_completo(l, a, corpo))
desc_rx = {f'CH{i}': f'Canal {i}: pulso de servo, 1000 a 2000 us a cada 20 ms ({ch})' for i, ch in
           enumerate(['aileron, o manche direito para os lados', 'profundor, o manche direito para cima e baixo',
                      'acelerador, o manche esquerdo para cima e baixo', 'leme, o manche esquerdo para os lados',
                      'chave SWA', 'chave SWB'], 1)}
desc_rx |= {
    'PPM': 'Os 8 canais num fio so (PPM): um pulso por canal, o quadro a cada 22,5 ms',
    'IBUS': 'iBUS: os 14 canais por serial (115200 baud, um quadro de 32 bytes a cada 7 ms) -- num RX da placa',
    'VCC': 'Alimentacao, 4 a 6,5 V: o BEC do ESC, ou o 5V da placa',
    'GND': 'GND (o mesmo da placa e dos servos)',
}
guardar('receptor-rc', l, a, [(n, x_p, y, ['power'] if n in ('VCC', 'GND') else [], desc_rx[n]) for n, y in zip(nomes, ys)])


# =====================================================================
#  Controle FlySky FS-i6 (o transmissor, em meia escala)
# =====================================================================
l, a = 90.0, 66.0
corpo = f'''  <!-- a antena -->
  <rect x="{l / 2 - 1.2:.2f}" y="0.3" width="2.4" height="7.0" rx="1.0" fill="#1b1b1d"/>
  <path d="M6.0 10.0 Q6.0 5.0 14.0 5.0 H{l - 14.0:.2f} Q{l - 6.0:.2f} 5.0 {l - 6.0:.2f} 10.0 V48.0 Q{l - 6.0:.2f} 54.0 {l - 16.0:.2f} 60.0 Q{l / 2:.2f} 66.0 16.0 60.0 Q6.0 54.0 6.0 48.0 Z" fill="#2a2b2f" stroke="#0b0b0c" stroke-width="0.4"/>
  <!-- a tela -->
  <rect x="{l / 2 - 12.5:.2f}" y="9.0" width="25.0" height="15.0" rx="1.0" fill="#1b1b1d"/>
  <rect x="{l / 2 - 11.5:.2f}" y="10.0" width="23.0" height="13.0" rx="0.6" fill="#3a6ea5"/>
  <!-- os pocos dos manches -->
  <circle cx="24.0" cy="38.0" r="12.5" fill="#1b1b1d" stroke="#0b0b0c" stroke-width="0.3"/>
  <circle cx="{l - 24.0:.2f}" cy="38.0" r="12.5" fill="#1b1b1d" stroke="#0b0b0c" stroke-width="0.3"/>
  <circle cx="24.0" cy="38.0" r="9.5" fill="#25262a" stroke="#3a3c41" stroke-width="0.3"/>
  <circle cx="{l - 24.0:.2f}" cy="38.0" r="9.5" fill="#25262a" stroke="#3a3c41" stroke-width="0.3"/>
  <!-- as chaves SWA/SWB (as alavancas o editor desenha) e o liga -->
  <rect x="9.0" y="7.0" width="6.0" height="5.0" rx="0.8" fill="#1b1b1d"/>
  <rect x="{l - 15.0:.2f}" y="7.0" width="6.0" height="5.0" rx="0.8" fill="#1b1b1d"/>
  <rect x="{l / 2 - 5.0:.2f}" y="54.0" width="10.0" height="5.0" rx="0.8" fill="#1b1b1d"/>
''' + texto(12.0, 15.4, "SWA", 1.3, "#b9bec6") + texto(l - 12.0, 15.4, "SWB", 1.3, "#b9bec6")     + texto(l / 2, 62.6, "POWER", 1.3, "#b9bec6") + texto(24.0, 54.2, "THR / RUD", 1.2, "#8e949c")     + texto(l - 24.0, 54.2, "AIL / ELE", 1.2, "#8e949c") + texto(l / 2, 31.0, "FS-i6", 2.4, "#e6e6e6")
gravar_svg('radio-controle', svg_completo(l, a, corpo))
guardar('radio-controle', l, a, [])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
