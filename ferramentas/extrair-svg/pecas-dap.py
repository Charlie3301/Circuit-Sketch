"""python ferramentas/extrair-svg/pecas-dap.py

As pecas do player (DAP):

  - pendrive-usb : um modulo de USB-A femea (VBUS, D-, D+, GND) com um
                   pendrive encaixado. Ligado no USB OTG da ESP32-S3 (D- no
                   GPIO19, D+ no GPIO20, VBUS no 5V), o sketch le o pendrive
                   como le o cartao. Os pinos ficam na esquerda, porque o USB
                   da S3 esta na fileira da direita da placa. Clicar no
                   pendrive tira e poe de volta: o desenho e partido em
                   "-corpo" (o modulo) e "-movel" (o pendrive, que desliza
                   para fora).

O desenho e simples de proposito, como o das pecas de som da Alpha 19.
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


# =====================================================================
#  Pendrive num modulo USB-A femea
# =====================================================================
lpen, apen = 34.0, 15.0
x_pinos = 2.4
nomes_pen = ['VBUS', 'D-', 'D+', 'GND']
ys_pen = [apen / 2 + (i - 1.5) * PASSO for i in range(4)]
x_carcaca, l_carcaca = 9.8, 13.6
curso_mm = 9.0

cabeca = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lpen}mm" height="{apen}mm" viewBox="0 0 {lpen} {apen}">'''

pads_pen = '\n'.join(
    f'  <circle cx="{x_pinos}" cy="{y:.2f}" r="0.85" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
    f'  <circle cx="{x_pinos}" cy="{y:.2f}" r="0.4" fill="#2a2a2a"/>'
    for y in ys_pen)
rotulos_pen = '\n'.join(
    f'  <text x="{x_pinos + 1.4:.2f}" y="{y + 0.45:.2f}" fill="#e6ecf5" font-size="1.25px"\n'
    f'        font-family="Verdana">{n}</text>'
    for n, y in zip(nomes_pen, ys_pen))

# o modulo: a placa azul, a carcaca do conector e os pinos
corpo = f'''
  <!-- a placa -->
  <rect x="0.4" y="0.4" width="{x_carcaca + l_carcaca + 0.8:.2f}" height="{apen - 0.8:.2f}" rx="1.1" fill="#1d4f91" stroke="#123461" stroke-width="0.3"/>
  <!-- a carcaca de metal do USB-A femea, com a boca para a direita -->
  <rect x="{x_carcaca}" y="1.6" width="{l_carcaca}" height="{apen - 3.2:.2f}" rx="0.6" fill="#aeb4bd" stroke="#6d737c" stroke-width="0.3"/>
  <rect x="{x_carcaca + l_carcaca - 1.2:.2f}" y="1.6" width="1.2" height="{apen - 3.2:.2f}" fill="#8b919a"/>
  <path d="M{x_carcaca + 1.2:.2f} 3.2 H{x_carcaca + l_carcaca - 2.4:.2f} M{x_carcaca + 1.2:.2f} {apen - 3.2:.2f} H{x_carcaca + l_carcaca - 2.4:.2f}" stroke="#c9cfd7" stroke-width="0.35"/>
  <text x="{x_carcaca + l_carcaca / 2 - 0.6:.2f}" y="{apen / 2 + 0.55:.2f}" fill="#5d636c" font-size="1.6px" text-anchor="middle"
        font-family="Verdana" font-weight="bold">USB</text>
{pads_pen}
{rotulos_pen}'''

# o pendrive: a lingueta de metal (que entra na carcaca) e o corpo preto
xb = x_carcaca + l_carcaca - 2.2          # onde o corpo comeca
movel = f'''
  <!-- a lingueta de metal: encaixado, ela fica dentro da carcaca -->
  <rect x="{xb - 7.4:.2f}" y="3.0" width="8.0" height="9.0" rx="0.4" fill="#c4cad2" stroke="#7d838c" stroke-width="0.25"/>
  <rect x="{xb - 4.4:.2f}" y="5.0" width="1.6" height="1.4" fill="#4d535c"/>
  <rect x="{xb - 4.4:.2f}" y="8.6" width="1.6" height="1.4" fill="#4d535c"/>
  <!-- o corpo -->
  <rect x="{xb:.2f}" y="2.2" width="12.4" height="10.6" rx="1.8" fill="#23262c" stroke="#0f1013" stroke-width="0.3"/>
  <rect x="{xb + 1.0:.2f}" y="3.2" width="10.4" height="8.6" rx="1.2" fill="#2e3239"/>
  <circle cx="{xb + 11.2:.2f}" cy="7.5" r="0.55" fill="#3b8cff"/>
  <text x="{xb + 5.6:.2f}" y="8.0" fill="#e6ecf5" font-size="1.7px" text-anchor="middle"
        font-family="Verdana" font-weight="bold">16 GB</text>'''

gravar_svg('pendrive-usb', f'{cabeca}\n{corpo}\n{movel}\n</svg>\n')
gravar_svg('pendrive-usb-corpo', f'{cabeca}\n{corpo}\n</svg>\n')
gravar_svg('pendrive-usb-movel', f'{cabeca}\n{movel}\n</svg>\n')

descricoes_pen = {
    'VBUS': 'Os 5 V do pendrive: no 5V da placa (na DevKitC-1, o da porta USB)',
    'D-': 'Dados USB, negativo: no GPIO19 da ESP32-S3 (o USB OTG dela)',
    'D+': 'Dados USB, positivo: no GPIO20 da ESP32-S3',
    'GND': 'Terra',
}
guardar('pendrive-usb', lpen, apen, [
    (n, x_pinos, y, (['power'] if n in ('VBUS', 'GND') else []), descricoes_pen[n])
    for n, y in zip(nomes_pen, ys_pen)
])
print(f'  o pendrive (clique): centro {(xb + 6.2) * PX:.1f},{7.5 * PX:.1f} px, raio {5.4 * PX:.1f} px; '
      f'tirado anda {curso_mm * PX:.1f} px')


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
