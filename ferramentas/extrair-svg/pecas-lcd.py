"""python ferramentas/extrair-svg/pecas-lcd.py

Os displays de caracteres (HD44780), redesenhados na Alpha 25 pelos desenhos
de contorno dos fabricantes, no tamanho real e vistos de frente:

  - lcd1602 : o 1602A de 80 x 36 mm (o desenho do ADM1602K da Amotec, o da
              SparkFun): os quatro furos de 2,5 mm a 75 x 31 mm, a barra de 16
              pinos EM CIMA com o pino 1 a 8 mm da borda esquerda, a moldura
              de metal de 71,3 x 26,3 mm e o vidro de 64,5 x 16,4 mm. Os
              caracteres de 2,95 x 5,55 mm no passo de 3,55 x 5,95 mm (a area
              ativa de 56,2 x 11,5 mm, centrada).
  - lcd2004 : o 2004A de 98 x 60 mm (o desenho da EONE): os furos de 3 mm a
              93 x 55 mm, o pino 1 a 10 mm; a moldura de y 9,9 a 49,9 mm e o
              vidro de 76 x 25,2 mm. Os caracteres de 2,95 x 4,75 mm no passo
              de 3,55 x 5,35 mm (a area ativa de 70,4 x 20,8 mm).
  - lcd1602-i2c / lcd2004-i2c : o mesmo display com o adaptador I2C (o
              FC-113) soldado atras. Os 16 furos ficam com a solda do adaptador,
              e os 4 pinos dele (GND, VCC, SDA, SCL) sao desenhados em cima, a
              esquerda, no lugar dos pinos 1 a 4 -- a convencao do Wokwi: na
              peca de verdade o conector fica atras da placa, e de frente nao
              apareceria.

Duas cores para cada um (o modelo da peca): "verde", o STN amarelo-esverdeado
de letras escuras (o de fabrica), e "azul", o de letras brancas no azul. Os
pontos apagados de cada caractere (5 x 8) estao no desenho; os acesos o editor
pinta por cima, no lugar que sai daqui (app/DesenhoDosLcds.cs).
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
SERIGRAFIA, ESTANHO = pa.SERIGRAFIA, pa.ESTANHO
A = 96 / 25.4
PASSO = 2.54
PCB, PCB_BORDA = '#1f6a34', '#123f1f'

LCD16 = ['VSS', 'VDD', 'V0', 'RS', 'RW', 'E', 'D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'A', 'K']
DESC16 = {
    'VSS': 'GND', 'VDD': '5 V (o VCC do display)', 'V0': 'Contraste: o meio de um potenciometro de 10 kohm entre o 5 V e o GND',
    'RS': 'RS: LOW e comando, HIGH e caractere', 'RW': 'RW: no GND (so escrever)', 'E': 'Enable: o pulso que faz o display ler',
    'D0': 'Dado 0 (no modo de 4 bits fica solto)', 'D1': 'Dado 1 (no modo de 4 bits fica solto)',
    'D2': 'Dado 2 (no modo de 4 bits fica solto)', 'D3': 'Dado 3 (no modo de 4 bits fica solto)',
    'D4': 'Dado 4', 'D5': 'Dado 5', 'D6': 'Dado 6', 'D7': 'Dado 7',
    'A': 'Anodo da luz de fundo: 5 V (pelo resistor, se o modulo nao tiver)', 'K': 'Catodo da luz de fundo: GND',
}
I2C4 = [('GND', 'GND'), ('VCC', '5 V'), ('SDA', 'SDA do I2C (o D21 na ESP32, o A4 no Uno)'),
        ('SCL', 'SCL do I2C (o D22 na ESP32, o A5 no Uno)')]

#            placa,       furos (diam, x, y),        pino 1,  moldura (x0, y0, x1, y1),   vidro (x0, y0, x1, y1),   colunas, linhas, caractere (l, a), passo (x, y), ponto (passo x, passo y, l, a)
GEOMETRIA = {
    'lcd1602': dict(placa=(80.0, 36.0), furos=(2.5, 2.5, 2.5), pino1=(8.0, 2.5), moldura=(4.35, 4.85, 75.65, 31.15),
                    vidro=(7.75, 9.8, 72.25, 26.2), colunas=16, linhas=2, car=(2.95, 5.55), passo=(3.55, 5.95),
                    ponto=(0.60, 0.70, 0.55, 0.65)),
    'lcd2004': dict(placa=(98.0, 60.0), furos=(3.0, 2.5, 2.5), pino1=(10.0, 2.5), moldura=(1.9, 9.9, 96.1, 49.9),
                    vidro=(11.0, 17.4, 87.0, 42.6), colunas=20, linhas=4, car=(2.95, 4.75), passo=(3.55, 5.35),
                    ponto=(0.60, 0.60, 0.55, 0.55)),
}

#          vidro (claro, escuro), ponto apagado, ponto aceso, vidro sem luz, aceso sem luz
CORES = {
    'verde': ('#b5cc3e', '#93ad28', '#a4bd33', '#1c2a0a', '#7d8a2c', '#26300f'),
    'azul': ('#2f6cf0', '#1b47c2', '#2a63e4', '#eef4ff', '#0e1f4f', '#3a4c86'),
}

pecas = []
cs = []


def geometria_dos_caracteres(g):
    """O canto do primeiro caractere (a area ativa centrada no vidro)."""
    vx0, vy0, vx1, vy1 = g['vidro']
    largura = g['colunas'] * g['passo'][0] - (g['passo'][0] - g['car'][0])
    altura = g['linhas'] * g['passo'][1] - (g['passo'][1] - g['car'][1])
    return (vx0 + vx1) / 2 - largura / 2, (vy0 + vy1) / 2 - altura / 2, largura, altura


def desenhar(tipo, i2c, cor):
    g = GEOMETRIA[tipo]
    L, H = g['placa']
    claro, escuro, apagado, _, _, _ = CORES[cor]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.ret(0, 0, L, H, PCB, rx=0.6, stroke=PCB_BORDA, sw=0.15)
    d.ret(0, 0, L, H, 'url(#luz)', rx=0.6)
    diam, fx, fy = g['furos']
    for x in (fx, L - fx):                                            # os quatro furos de fixacao
        for y in (fy, H - fy):
            d.circ(x, y, diam / 2 + 0.6, ESTANHO)
            d.circ(x, y, diam / 2, '#0d1a10')
    # a barra de 16 furos (com a solda do adaptador, no de I2C)
    x1, y1 = g['pino1']
    for k in range(16):
        x = x1 + k * PASSO
        if k == 0:
            d.ret_c(x, y1, 1.8, 1.8, ESTANHO)
        else:
            d.circ(x, y1, 0.9, ESTANHO)
        if i2c and k >= 4:
            d.circ(x, y1, 0.62, '#dfe3e7')                              # o pino do adaptador, soldado
            d.circ(x - 0.15, y1 - 0.15, 0.22, '#ffffff', op=0.7)
        elif not i2c:
            d.circ(x, y1, 0.5, '#0d1a10')
    if not i2c:
        d.texto(x1, y1 + 1.9, '1', 0.9, SERIGRAFIA, alinhar='center')
        d.texto(x1 + 15 * PASSO, y1 + 1.9, '16', 0.9, SERIGRAFIA, alinhar='center')
    # a moldura de metal, com o friso de cima e o de baixo e as abas das pontas
    mx0, my0, mx1, my1 = g['moldura']
    d.ret(mx0, my0, mx1 - mx0, my1 - my0, '#b9bec4', rx=0.5, stroke='#6d737a', sw=0.12)
    d.ret(mx0, my0, mx1 - mx0, my1 - my0, 'url(#luz)', rx=0.5)
    for y in (my0 + 1.3, my1 - 1.3):
        d.ret(mx0 + 1.6, y - 0.35, mx1 - mx0 - 3.2, 0.7, '#9ca2a9', rx=0.35)
        d.ret(mx0 + 1.6, y - 0.35, mx1 - mx0 - 3.2, 0.25, '#e6e9ec', rx=0.12, op=0.8)
    for x in (mx0 + 0.6, mx1 - 0.6):                                   # as abas torcidas
        d.ret_c(x, (my0 + my1) / 2, 0.7, 2.2, '#8a9097', rx=0.2)
    # o vidro
    vx0, vy0, vx1, vy1 = g['vidro']
    d.ret(vx0 - 0.6, vy0 - 0.6, vx1 - vx0 + 1.2, vy1 - vy0 + 1.2, '#2a2d31', rx=0.4)        # a borda preta do vidro
    d.ret(vx0, vy0, vx1 - vx0, vy1 - vy0, escuro, rx=0.3)
    d.ret(vx0 + 0.3, vy0 + 0.3, vx1 - vx0 - 0.6, vy1 - vy0 - 0.6, claro, rx=0.25, op=0.9)
    # os pontos apagados de cada caractere (um padrao de 5 x 8 no passo do caractere)
    cx0, cy0, largura, altura = geometria_dos_caracteres(g)
    ppx, ppy, pl, ph = g['ponto']
    pw, phh = g['passo'][0] * A, g['passo'][1] * A
    pontos = ''.join(f'<rect x="{f(c * ppx * A)}" y="{f(r * ppy * A)}" width="{f(pl * A)}" height="{f(ph * A)}" fill="{apagado}"/>'
                     for r in range(8) for c in range(5))
    d += (f'<defs><pattern id="pontos-{tipo}" x="{f(cx0 * A)}" y="{f(cy0 * A)}" width="{f(pw)}" height="{f(phh)}" '
          f'patternUnits="userSpaceOnUse">{pontos}</pattern></defs>\n')
    d += (f'<rect x="{f(cx0 * A)}" y="{f(cy0 * A)}" width="{f(largura * A)}" height="{f(altura * A)}" '
          f'fill="url(#pontos-{tipo})"/>\n')
    # o reflexo do vidro
    d.ret(vx0 + 0.4, vy0 + 0.4, vx1 - vx0 - 0.8, (vy1 - vy0) * 0.18, '#ffffff', rx=0.2, op=0.08)
    # os terminais da luz de fundo, nas pontas da moldura
    for x in (mx1 + (L - mx1) / 2 if mx1 < L - 2 else None,):
        if x:
            for y in ((my0 + my1) / 2 - 2.6, (my0 + my1) / 2 + 2.6):
                d.circ(x, y, 0.7, ESTANHO)
    # o conector do adaptador I2C, no lugar dos pinos 1 a 4
    if i2c:
        for k in range(4):
            x = x1 + k * PASSO
            d.ret_c(x, y1, 2.4, 2.4, 'url(#plastico)', rx=0.3)
            d.ret_c(x, y1, 0.84, 0.84, '#7a5c18')
            d.ret_c(x, y1, 0.64, 0.64, 'url(#ouro)')
            d.texto(x, y1 + 2.2, I2C4[k][0], 0.75, SERIGRAFIA, alinhar='center')
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')


for tipo in GEOMETRIA:
    g = GEOMETRIA[tipo]
    L, H = g['placa']
    x1, y1 = g['pino1']
    for i2c in (False, True):
        id_peca = tipo + ('-i2c' if i2c else '')
        for cor in CORES:
            nome = id_peca if cor == 'verde' else f'{id_peca}-{cor}'
            io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(desenhar(tipo, i2c, cor))
        if i2c:
            pinos = [(n, x1 + k * PASSO, y1, ['power'] if n in ('GND', 'VCC') else [], dsc) for k, (n, dsc) in enumerate(I2C4)]
        else:
            pinos = [(n, x1 + k * PASSO, y1, ['power'] if n in ('VSS', 'VDD') else [], DESC16[n]) for k, n in enumerate(LCD16)]
        pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                      'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                                for n, x, y, s, dsc in pinos],
                      'variantes': [], 'metades': False})
        print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')
    cx0, cy0, _, _ = geometria_dos_caracteres(g)
    ppx, ppy, pl, ph = g['ponto']
    vx0, vy0, vx1, vy1 = g['vidro']
    cs.append(f'        ["{tipo}"] = new({g["colunas"]}, {g["linhas"]}, {cx0 * A:.2f}, {cy0 * A:.2f}, {g["passo"][0] * A:.3f}, '
              f'{g["passo"][1] * A:.3f}, {ppx * A:.3f}, {ppy * A:.3f}, {pl * A:.3f}, {ph * A:.3f}, '
              f'{vx0 * A:.2f}, {vy0 * A:.2f}, {(vx1 - vx0) * A:.2f}, {(vy1 - vy0) * A:.2f}),')

cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')

cores_cs = [f'        ["{cor}"] = ({", ".join("Cor(0x" + c[1:] + ")" for c in cs_)}),' for cor, cs_ in
            ((cor, (v[3], v[5], v[4])) for cor, v in CORES.items())]
linhas = [
    'namespace Simulador.App;',
    '',
    '// GERADO por ferramentas/extrair-svg/pecas-lcd.py -- nao edite a mao.',
    '//',
    '// Onde ficam os pontos de cada caractere dos LCDs (px da caixa): o editor',
    '// pinta os acesos por cima do vidro desenhado no SVG.',
    'static class DesenhoDosLcds',
    '{',
    '    /// <summary>A grade: colunas, linhas, o canto do primeiro caractere, o passo do caractere e o do ponto, o tamanho do ponto e o vidro.</summary>',
    '    public sealed record Grade(int Colunas, int Linhas, double X, double Y, double PassoX, double PassoY,',
    '                               double PontoPassoX, double PontoPassoY, double PontoLargura, double PontoAltura,',
    '                               double VidroX, double VidroY, double VidroLargura, double VidroAltura);',
    '',
    '    public static readonly Dictionary<string, Grade> Grades = new()',
    '    {',
    *cs,
    '    };',
    '',
    '    static System.Windows.Media.Color Cor(int rgb) => System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);',
    '',
    '    /// <summary>A cor do ponto aceso com a luz de fundo, sem ela, e a do vidro apagado, por modelo.</summary>',
    '    public static readonly Dictionary<string, (System.Windows.Media.Color Aceso, System.Windows.Media.Color AcesoSemLuz, System.Windows.Media.Color VidroSemLuz)> Cores = new()',
    '    {',
    *cores_cs,
    '    };',
    '}',
]
io.open(pa.BASE + 'app/DesenhoDosLcds.cs', 'w', encoding='utf-8', newline='').write('\n'.join(linhas) + '\n')
print('  app/DesenhoDosLcds.cs')
