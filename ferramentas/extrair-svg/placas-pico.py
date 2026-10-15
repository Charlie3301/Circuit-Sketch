"""python ferramentas/extrair-svg/placas-pico.py

A Raspberry Pi Pico e a Pico W redesenhadas (Alpha 25), no estilo das outras
placas (as primitivas e a paleta de placas-arduino.py), na escala real
(21 x 51 mm, 96 px por polegada).

O projeto da placa e publico ("sem limitacoes", pico-cad/Pico-design-files-
LICENSE.txt), mas so no formato do Cadence Allegro, que nao da para ler. O
lugar e o tamanho de cada componente sairam entao do desenho vetorial da
placa que a Raspberry Pi publica nas figuras de pinagem da documentacao
(github.com/raspberrypi/documentation, CC BY-SA 4.0): pico_svg_inline.py
passa o SVG para cores em linha e pico_layout.py tira cada forma em mm
(pico-cad/pico.json e picow.json). O desenho de cada peca e nosso; a
serigrafia tambem (o logotipo e o nome "Raspberry Pi" sao marcas
registradas e ficam de fora; o modelo, "Pico", fica).

Os pinos nao mudam (os 40 da barra), e entram os 3 de depuracao (SWCLK, GND
e SWDIO): na ponta de baixo na Pico, no meio da placa na Pico W.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, mover, rodar, f = pa.Desenho, pa.T, pa.mover, pa.rodar, pa.f
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI, FURO = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI, pa.FURO
BASE = pa.BASE
A = 96 / 25.4
L_MM, H_MM = 21.0, 51.0

VERDE = '#2c8a45'
VERDE_BORDA = '#1d6331'
VERDE_ANTENA = '#58ad62'   # a ponta da Pico W, sem cobre por baixo (a antena)
IGNORAR = {'#ffffff', '#004e29', '#014f29', '#33865f', '#338661', '#aac1be', '#006837', '#0a6939',
           '#3d3f38', '#3e3f38', '#363a44', '#353b45'}
ESCUROS = {'#2a2d2e', '#2b2d2f', '#30312e', '#323230'}
CAQUI = {'#9a916c', '#958863', '#958763', '#99906c'}

PLACAS = {
    'pi-pico': dict(
        cad='pico.json', ano='2020', modelo='Pico',
        # os tres do SWD: o do meio no lugar do desenho, os outros a 2,54 mm (o passo do conector)
        swd=[(7.97, 50.15), (10.51, 50.15), (13.05, 50.15)], swd_borda=True,
        textos=[('1', 2.77, 1.6, 0), ('2', 2.77, 4.1, 0), ('39', 17.8, 4.1, 0), ('LED', 4.7, 5.85, 0),
                ('USB', 11.1, 5.85, 0), ('BOOTSEL', 4.3, 11.7, 90), ('DEBUG', 10.5, 47.6, 0)],
        nome=(4.05, 28.4), marcas=[((10.5, 25.5), ['RP2-B1', '20/21', 'P64M15.00']), ((6.65, 18.6), ['25Q16JV', 'IQ']),
                                   ((13.4, 10.4), ['6150B'])]),
    'pi-pico-w': dict(
        cad='picow.json', ano='2022', modelo='Pico W',
        swd=[(11.14, 31.25), (13.68, 31.25), (16.22, 31.25)], swd_borda=False, antena=43.4,
        textos=[('1', 2.68, 1.6, 0), ('2', 2.68, 4.1, 0), ('39', 17.75, 4.1, 0), ('LED', 4.64, 5.85, 0),
                ('USB', 10.36, 5.8, 0), ('BOOTSEL', 4.64, 12.25, 90), ('DEBUG', 14.24, 28.6, 0)],
        nome=(3.95, 30.3), marcas=[((10.9, 23.9), ['RP2-B2', '21/42', 'PAK107.00']), ((7.05, 16.96), ['25Q16JV', 'IQ']),
                                   ((13.1, 10.5), ['6150B'])]),
}

DEFS_EXTRA = '''  <linearGradient id="blindagem" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#eef0f2"/><stop offset="0.45" stop-color="#cfd3d7"/><stop offset="1" stop-color="#a9aeb4"/></linearGradient>
'''


def marca_de(marcas, cx, cy):
    """A marcacao do chip cujo centro e este (o RP2040, a flash, o RT6150)."""
    for (mx, my), linhas in marcas:
        if abs(cx - mx) < 0.9 and abs(cy - my) < 0.9:
            return linhas
    return None


def gerar(id_peca, cfg):
    dados = json.load(io.open(os.path.join(AQUI, 'pico-cad', cfg['cad']), encoding='utf-8'))
    W, H = dados['placa_mm']
    sx, sy = L_MM / W, H_MM / H                   # o desenho e a placa de 21 x 51 mm
    usb_topo = -1.35
    sobra_y = round(-usb_topo * A + 0.6, 1)
    d = Desenho(T(A, 0, 0, A, 0, sobra_y))
    catalogo = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}
    peca = catalogo[id_peca]

    # ---- a placa (cantos arredondados) ----
    d.ret(0, 0, L_MM, H_MM, VERDE, rx=0.7, stroke=VERDE_BORDA, sw=0.12)
    if cfg.get('antena'):
        d.ret(2.7, cfg['antena'], L_MM - 5.4, H_MM - cfg['antena'] - 0.15, VERDE_ANTENA, op=0.8)
    d.ret(0, 0, L_MM, H_MM, 'url(#luz)', rx=0.7)

    # ---- os 40 pinos: o furo e o meio-furo castelado na borda ----
    for q in peca['pinos']:
        x, y = q['x'] / A, q['y'] / A
        if q['nome'] in ('SWCLK', 'GND.8', 'SWDIO'):
            continue
        borda = 0 if x < L_MM / 2 else L_MM
        s = 1 if borda == 0 else -1
        d.ret(min(borda, borda + s * 2.45), y - 0.9, 2.45, 1.8, OURO, rx=0.3)
        d.circ(x, y, 0.85, OURO)
        d.circ(x, y, 0.5, FURO)
        d.caminho([('M', borda, y - 0.62), ('A', 0.62, 0, 1 if s > 0 else 0, borda, y + 0.62), ('Z',)], '#0d1014')
    # ---- os furos de fixacao (2,1 mm, com o anel dourado) ----
    for x, y in ((4.8, 2.0), (L_MM - 4.8, 2.0), (4.8, H_MM - 2.0), (L_MM - 4.8, H_MM - 2.0)):
        d.circ(x, y, 1.65, OURO)
        d.circ(x, y, 1.05, '#16181c')
    # ---- os pinos de depuracao ----
    for x, y in cfg['swd']:
        if cfg['swd_borda']:
            d.ret(x - 0.72, y - 1.05, 1.44, 2.1, OURO, rx=0.3)
            d.circ(x, y, 0.5, FURO)
            d.caminho([('M', x - 0.6, H_MM), ('A', 0.6, 0, 1, x + 0.6, H_MM), ('Z',)], '#0d1014')
        else:
            d.circ(x, y, 0.85, OURO)
            d.circ(x, y, 0.5, FURO)
    if not cfg['swd_borda']:
        (x0, y0), (x1, _) = cfg['swd'][0], cfg['swd'][-1]
        d.ret(x0 - 1.4, y0 - 1.15, x1 - x0 + 2.8, 2.3, None, stroke=SERIGRAFIA, sw=0.12)

    # ---- a serigrafia (antes das pecas) ----
    for t, x, y, ang in cfg['textos']:
        d.texto(x, y, t, 0.95 if len(t) > 2 else 1.0, SERIGRAFIA, ang=ang, ancora='middle', alinhar='center', peso='normal')
    nx, ny = cfg['nome']
    d.texto(nx, ny, f"{cfg['modelo']} ©{cfg['ano']}", 1.45, SERIGRAFIA, ang=90, ancora='middle', alinhar='center',
            peso='normal')

    # ---- as pecas, na ordem do desenho oficial ----
    centros = {}
    for e in dados['desenhos']:
        cor = e['cor']
        if cor is None or cor in IGNORAR:
            continue
        x0, y0, x1, y1 = e['x0'] * sx, e['y0'] * sy, e['x1'] * sx, e['y1'] * sy
        w, h = x1 - x0, y1 - y0
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        if w > 18 or h > 40:
            continue
        if w < 0.25 and h > 2 or h < 0.25 and w > 2:       # as linhas de chamada da figura
            continue
        if cor == '#dbddda' and y0 < 0 and w < 1.5:        # as abas do USB da figura (o nosso tem as dele)
            continue
        redondo = e['tipos'].startswith('cccc') and abs(w - h) < 0.2
        # as ilhas da barra e as da depuracao: ja desenhadas (a figura as tem por cima)
        perto_swd = any(abs(cx - sx_) < 1.3 and abs(cy - sy_) < 1.3 for sx_, sy_ in cfg['swd'])
        if cor in CAQUI and ((x0 < 2.6 or x1 > L_MM - 2.6) and h > 1.2 or perto_swd):
            continue
        if cor == '#dbddda' and any(x0 <= sx_ <= x1 and y0 <= sy_ <= y1 for sx_, sy_ in cfg['swd']):
            continue
        if cor in ('#fcee21', '#f8e935'):
            continue                                       # os furos ja foram
        if cor == '#dbddda':
            if y0 < 0.5 and w > 6:                        # o micro-USB, com a boca para cima
                d.ret(cx - 3.75, usb_topo, 7.5, 3.85 - usb_topo, 'url(#metal-h)', rx=0.25, stroke='#5d646c', sw=0.08)
                d.ret(cx - 3.2, usb_topo, 6.4, 0.55, '#5b6168')
                d.linha(cx - 3.1, 3.0, cx + 3.1, 3.0, '#8d949b', 0.1)
                for s in (-1, 1):
                    d.ret_c(cx + s * 3.0, usb_topo + 2.2, 0.9, 1.6, None, rx=0.2, stroke='#8d949b', sw=0.1)
                    d.ret_c(cx + s * 3.6, 3.3, 1.3, 1.1, ESTANHO, rx=0.15)
                centros['USB'] = (cx, cy)
            elif w * h > 60:                              # a blindagem do radio (Pico W)
                d.ret(x0, y0, w, h, 'url(#blindagem)', rx=0.35, stroke='#8c939b', sw=0.08)
                d.ret(x0 + 0.5, y0 + 0.5, w - 1.0, h - 1.0, None, rx=0.3, stroke='#b9bfc6', sw=0.08)
                for k in range(3):
                    d.circ(x0 + 1.1 + k * 0.7, y0 + 1.1, 0.18, '#9aa1a8')
            elif w * h > 4:                               # a tampa do cristal
                d.ret(x0, y0, w, h, 'url(#metal)', rx=0.3, stroke='#7c838b', sw=0.06)
                d.texto(cx, cy, '12.000', 0.55, '#4a4f56', ancora='middle', alinhar='center', peso='normal')
            else:                                         # a ilha estanhada sob a peca
                d.ret(x0, y0, w, h, ESTANHO, rx=0.05)
        elif cor in ESCUROS:
            if 'lclclclc' in e['tipos'] and w < 3 and h < 3:   # o indutor, de cantos redondos
                d.ret(x0, y0, w, h, '#3a3c40', rx=0.35)
                d.ret(x0, y0, w, h, 'url(#luz)', rx=0.35)
                d.texto(cx, cy, '2R2', 0.5, MARCA_CI, ang=90 if h > w else 0, ancora='middle', alinhar='center', peso='normal')
                continue
            d.ret(x0, y0, w, h, PRETO_CI, rx=min(w, h) * 0.08)
            d.ret(x0, y0, w, h, 'url(#luz)', rx=min(w, h) * 0.08)
            linhas = marca_de(cfg['marcas'], cx, cy)
            if linhas:
                tam = 0.75 if w > 5 else 0.42
                for k, t in enumerate(linhas):
                    d.texto(cx, cy + (k - (len(linhas) - 1) / 2) * tam * 1.45, t, tam, MARCA_CI, ancora='middle',
                            alinhar='center', peso='normal')
                if w > 5:
                    d.circ(x0 + 0.7, y0 + 0.7, 0.25, '#383a3f')
                    centros['RP2040'] = (cx, cy)
        elif cor in CAQUI:
            if redondo:                                    # os pontos de teste
                d.circ(cx, cy, w / 2, OURO)
            elif y0 > 48 and h > 1.5:                       # os de depuracao (ja desenhados)
                continue
            else:                                          # o corpo do capacitor, entre as ilhas
                d.ret(x0, y0, w, h, '#b7956a', rx=0.05)
                d.ret(x0, y0, w, h, 'url(#luz)', rx=0.05)
        elif cor in ('#bcbbba',):
            d.ret(x0, y0, w, h, '#8f9296', rx=0.05)
        elif cor in ('#349ad6',):
            d.ret(x0, y0, w, h, '#3d7fc0', rx=0.05)
            d.ret(x0, y0, w, h, 'url(#luz)', rx=0.05)
        elif cor in ('#ecf6fd', '#e9f5fc'):                # o LED
            d.ret(x0, y0, w, h, '#f3f1ea', rx=0.08)
            d.ret(x0, y0, 0.25, h, 'url(#metal)')
            d.ret(x1 - 0.25, y0, 0.25, h, 'url(#metal)')
            centros['LED'] = (cx, cy)
        elif cor in ('#33cc33', '#57af39'):
            d.circ(cx, cy, w / 2, '#5cd65a', op=0.75)
        elif cor in ('#cccccc', '#cdcbcb'):                # o BOOTSEL: a base e a tampa branca
            d.ret(x0, y0, w, h, 'url(#metal)', rx=0.35, stroke='#7c838b', sw=0.06)
            d.ret(x0 + 0.3, y0 + 0.3, w - 0.6, h - 0.6, '#f5f4ef', rx=0.6, stroke='#c9c6bc', sw=0.06)
            d.ret(x0 + 0.3, y0 + 0.3, w - 0.6, h - 0.6, 'url(#luz)', rx=0.6)
            centros['BOOTSEL'] = (cx, cy)
        else:
            print('  cor sem regra:', cor, round(x0, 2), round(y0, 2), round(w, 2), round(h, 2))

    L_px, A_px = peca['largura'], peca['altura']
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{L_px}px" height="{f(A_px + 2 * sobra_y)}px" '
           f'data-sobra="0 {f(sobra_y)}" viewBox="0 0 {L_px} {f(A_px + 2 * sobra_y)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n'
           + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'{id_peca}: {len(svg) // 1024} KB, sobra {sobra_y} px')
    for nome, (x, y) in centros.items():
        print(f'  {nome}: ({x * A:.1f}, {y * A:.1f}) px da caixa')
    return {n: (x * A, y * A) for n, (x, y) in centros.items()}


SWD = [('SWCLK', 'Depuracao SWD: o relogio (para a Raspberry Pi Debug Probe). O sketch nao usa este pino.'),
       ('GND.8', 'Terra (o do conector de depuracao).'),
       ('SWDIO', 'Depuracao SWD: os dados (para a Raspberry Pi Debug Probe). O sketch nao usa este pino.')]


def gravar_pinos_swd(id_peca, cfg):
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    peca = next(p for p in lista if p['id'] == id_peca)
    existentes = {q['nome']: q for q in peca['pinos']}
    for (nome, desc), (x, y) in zip(SWD, cfg['swd']):
        q = existentes.get(nome) or {'nome': nome}
        q.update({'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': ['power'] if nome.startswith('GND') else [],
                  'descricao': desc})
        if nome not in existentes:
            peca['pinos'].append(q)
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))


if __name__ == '__main__':
    for id_peca, cfg in PLACAS.items():
        gravar_pinos_swd(id_peca, cfg)
        gerar(id_peca, cfg)
