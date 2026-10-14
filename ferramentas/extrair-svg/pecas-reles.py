"""python ferramentas/extrair-svg/pecas-reles.py

Os modulos de rele (Alpha 24), os azuis dos kits, com optoacoplador:

  - modulo-rele-1 : 1 canal (VCC, GND, IN; NO, COM, NC) -- os mesmos nomes
                    do "wokwi-relay-module";
  - modulo-rele-2 : 2 canais (GND, IN1, IN2, VCC; NO1 COM1 NC1, NO2 COM2 NC2);
  - modulo-rele-4 : 4 canais (GND, IN1..IN4, VCC; os quatro bornes).

Em cima, os bornes (NO, COM, NC de cada canal); no meio, os reles
SRD-05VDC-SL-C; embaixo, o optoacoplador, o LED de cada canal e o conector.
Nos de 2 e 4 canais, o jumper JD-VCC/VCC no canto (no lugar: a bobina tira
do VCC). Todos os pinos ficam na grade de 0,1" entre si. O que acende (a
luz de energia e a de cada canal) o editor desenha por cima.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def gravar_svg(nome, svg):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def svg(larg, alt, corpo):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{larg}mm" height="{alt}mm" viewBox="0 0 {larg} {alt}">\n{corpo}\n</svg>\n')


def texto(x, y, t, tam=1.3, cor='#f2f2f2', ancora='middle', peso='bold', girar=0):
    rot = f' transform="rotate({girar} {x:.2f} {y:.2f})"' if girar else ''
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}"\n'
            f'        font-family="Verdana" font-weight="{peso}"{rot}>{t}</text>\n')


def pino_macho(x, y):
    return (f'  <rect x="{x - 1.2:.2f}" y="{y - 1.2:.2f}" width="2.4" height="2.4" fill="#1b1b1d"/>\n'
            f'  <rect x="{x - 0.38:.2f}" y="{y - 0.38:.2f}" width="0.76" height="0.76" fill="#d9b44a"/>\n')


def parafuso(cx, cy):
    return (f'  <circle cx="{cx:.2f}" cy="{cy:.2f}" r="1.75" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.25"/>\n'
            f'  <path d="M{cx - 1.1:.2f} {cy:.2f} H{cx + 1.1:.2f}" stroke="#5a6068" stroke-width="0.45"/>\n')


def rele(x, y, larg=15.4, alt=19.0):
    """O SRD-05VDC-SL-C visto de cima: o cubo azul com o que vem escrito nele."""
    cx = x + larg / 2
    s = (f'  <rect x="{x:.2f}" y="{y:.2f}" width="{larg}" height="{alt}" rx="0.7" fill="#2b74d6" stroke="#123f80" stroke-width="0.3"/>\n'
         f'  <rect x="{x + 0.6:.2f}" y="{y + 0.6:.2f}" width="{larg - 1.2:.2f}" height="1.4" rx="0.4" fill="#ffffff" opacity="0.16"/>\n')
    s += texto(cx, y + 4.2, 'SONGLE', 1.5, '#e8eef8')
    s += texto(cx, y + 7.6, 'SRD-05VDC-SL-C', 1.05, '#e8eef8')
    s += texto(cx, y + 10.4, '10A 250VAC', 0.95, '#d7e2f4', peso='normal')
    s += texto(cx, y + 12.4, '10A 30VDC', 0.95, '#d7e2f4', peso='normal')
    s += texto(cx, y + 15.4, 'CE', 1.0, '#d7e2f4', peso='normal')
    return s


def modulo(id_peca, canais):
    alt = 52.0
    passo_canal = 7 * PASSO                   # 17,78 mm de um rele ao outro (na grade)
    larg = {1: 26.0, 2: 40.0, 4: 76.0}[canais]
    x0 = 3.2                                  # a grade de 0,1" comeca aqui
    y_cabo = alt - 2.8                        # o conector embaixo
    y_borne = y_cabo - 18 * PASSO             # os parafusos em cima (na grade)

    def na_grade(x):
        return x0 + round((x - x0) / PASSO) * PASSO

    corpo = (f'  <rect x="0.3" y="0.3" width="{larg - 0.6}" height="{alt - 0.6}" rx="1.2" fill="#1d4b9c" stroke="#0f2c61" stroke-width="0.3"/>\n'
             + ''.join(f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="1.2" fill="#0b1630"/>\n'
                       for x in (2.0, larg - 2.0) for y in (2.0, alt - 9.0)))
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
        corpo += (f'  <rect x="{xs[0] - 2.84:.2f}" y="{y_borne - 3.0:.2f}" width="{xs[2] - xs[0] + 5.68:.2f}" height="6.0" rx="0.5" '
                  f'fill="#1f5fb4" stroke="#0f2f5c" stroke-width="0.25"/>\n')
        for nome, x in zip(['NO', 'COM', 'NC'], xs):
            corpo += parafuso(x, y_borne)
            corpo += texto(x, y_borne + 4.6, nome, 0.95)
            pinos.append((nome + sufixo, x, y_borne, [],
                          descricao_saida[nome] + ('' if canais == 1 else f' -- canal {c + 1}')))
        # o rele
        corpo += rele(centro - 7.7, y_borne + 6.0)
        # o optoacoplador (DIP-4), o transistor e o diodo de roda livre
        y_opto = y_borne + 27.6
        corpo += (f'  <rect x="{centro - 5.6:.2f}" y="{y_opto:.2f}" width="4.6" height="3.4" rx="0.3" fill="#1b1b1d"/>\n'
                  f'  <circle cx="{centro - 4.9:.2f}" cy="{y_opto + 0.7:.2f}" r="0.3" fill="#555"/>\n'
                  f'  <rect x="{centro + 1.0:.2f}" y="{y_opto + 0.2:.2f}" width="2.6" height="2.4" rx="0.3" fill="#1b1b1d"/>\n'
                  f'  <rect x="{centro + 4.2:.2f}" y="{y_opto + 0.6:.2f}" width="2.8" height="1.4" rx="0.3" fill="#1b1b1d"/>\n'
                  f'  <rect x="{centro + 6.4:.2f}" y="{y_opto + 0.6:.2f}" width="0.5" height="1.4" fill="#c9ccd1"/>\n')
        corpo += texto(centro - 3.3, y_opto + 5.0, '817C', 0.8, '#bfc6d2', peso='normal')
        # o LED do canal (apagado; o editor acende)
        y_led = y_opto + 6.4
        corpo += (f'  <circle cx="{centro + 3.4:.2f}" cy="{y_led:.2f}" r="0.9" fill="#5a1414" stroke="#2a0b0b" stroke-width="0.2"/>\n')
        corpo += texto(centro + 3.4, y_led + 2.4, 'IN' + (sufixo or '') if canais > 1 else 'ON', 0.8)
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
        corpo += pino_macho(x, y_cabo)
        corpo += texto(x, y_cabo - 2.0, nome, 0.8, girar=-90 if len(nome) > 3 else 0)
        desc = descricao_entrada.get(nome) or (
            f'{nome}: o sinal do canal {nome[2:] or "1"}. Em LOW o rele liga (ativo em LOW, pelo optoacoplador: '
            'o pino so acende o LED de dentro, ~2 mA); em HIGH ou solto, desliga')
        pinos.append((nome, x, y_cabo, ['power'] if nome in ('VCC', 'GND') else [], desc))
    x_pwr = x0 + len(nomes) * PASSO + 2.4
    corpo += f'  <circle cx="{x_pwr:.2f}" cy="{y_cabo:.2f}" r="0.9" fill="#5a1414" stroke="#2a0b0b" stroke-width="0.2"/>\n'
    corpo += texto(x_pwr, y_cabo - 2.0, 'PWR', 0.8)
    luzes.insert(0, (x_pwr, y_cabo, 'energia'))
    if canais > 1:
        # o jumper JD-VCC/VCC (no lugar: a bobina tira do VCC)
        xj = larg - 3.2 - 2 * PASSO
        for k, rotulo in enumerate(['GND', 'VCC', 'JD']):
            corpo += pino_macho(xj + k * PASSO, y_cabo)
            corpo += texto(xj + k * PASSO, y_cabo - 2.0, rotulo, 0.7)
        corpo += f'  <rect x="{xj + PASSO - 1.3:.2f}" y="{y_cabo - 1.4:.2f}" width="{PASSO + 2.6:.2f}" height="2.8" rx="0.5" fill="#e8c21c" stroke="#8a7110" stroke-width="0.2"/>\n'
    corpo += texto(larg / 2, alt - 7.6, f'{canais} Relay Module' if canais > 1 else '1 Relay Module', 1.1, '#cfe0ff')

    gravar_svg(id_peca, svg(larg, alt, corpo))
    pecas.append({
        'id': id_peca, 'largura': round(larg * PX), 'altura': round(alt * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2), 'sinais': s, 'descricao': d}
                  for n, x, y, s, d in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg * PX)}x{round(alt * PX)} px, {len(pinos)} pinos')
    return [(round(x * PX, 2), round(y * PX, 2), chave) for x, y, chave in luzes]


luzes = {f'modulo-rele-{n}': modulo(f'modulo-rele-{n}', n) for n in (1, 2, 4)}

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'catalogo: {len(d)} pecas')
# as luzes, em pixels da peca, para o EditorModulos.cs
for id_peca, lista in luzes.items():
    print(id_peca, lista)
