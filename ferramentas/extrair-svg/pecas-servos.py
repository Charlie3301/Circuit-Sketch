"""python ferramentas/extrair-svg/pecas-servos.py

Os tres servos desenhados como os de verdade (Alpha 25), vistos de cima,
com as medidas dos fabricantes:

  - servo        : o micro servo SG90 da TowerPro (22,7 x 12,2 mm, 32,3 mm
                   com as orelhas; os furos de fixacao a 27 mm um do outro),
                   na caixa azul translucida. E o "wokwi-servo": a caixa
                   (170 x 120 px) e os pinos continuam os do Wokwi.
  - servo-mg996r : o MG996R da TowerPro (40,7 x 19,7 mm), preto, com a
                   etiqueta roxa em cima e o estriado de metal.
  - servo-360    : o FS90R da Feetech, de rotacao continua (23,2 x 12,5 mm),
                   azul translucido.

Em todos, o eixo fica na ponta do cabo (o fio sai por baixo dele, como no
desenho dos fabricantes), o conector e o JR femea preto, com as janelas dos
contatos, e o fio e a fita marrom (GND), vermelha (V+) e laranja (PWM). O
braco (simples, duplo, cruz, estrela, disco ou nenhum) e desenhado pelo
editor por cima, girando no eixo (app/DesenhoDoBracoDeServo.cs,
EixosDosServos: os eixos impressos aqui).
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


def svg(l, a, conteudo):
    """O SVG da peca: a caixa em pixels inteiros (a mesma do pecas.json) e o
    desenho em milimetros dentro de um scale. Com a caixa em mm de numeros
    quebrados (o SG90 tem 44,979 x 31,750 mm) o SharpVectors desloca o
    desenho inteiro (10 px no SG90, medido pelo Pecas_ArteNoLugar)."""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{round(l * PX)}px" height="{round(a * PX)}px" viewBox="0 0 {round(l * PX)} {round(a * PX)}">\n'
            f'<g transform="scale({PX:.6f})">\n{conteudo}</g>\n</svg>\n')


def f(v):
    return f'{v:.3f}'


# ----------------------------------------------------------------- o cabo

FIOS = [('#6b3e1e', '#3a2010'), ('#c9302c', '#7a1a17'), ('#e8892a', '#8a4d12')]   # marrom, vermelho, laranja


def conector_jr(x_frente, ys, comprimento):
    """O conector JR femea (o do servo), deitado: a frente (onde entra o pino
    macho) virada para a esquerda, em x_frente; os fios saem por tras. De cima
    aparecem as janelas onde a trava de cada contato prende."""
    y0, y1 = ys[0] - PASSO / 2 - 0.22, ys[-1] + PASSO / 2 + 0.22
    x1 = x_frente + comprimento
    s = (f'  <!-- o conector JR femea -->\n'
         f'  <rect x="{f(x_frente)}" y="{f(y0)}" width="{f(comprimento)}" height="{f(y1 - y0)}" rx="0.45" '
         f'fill="#1c1c1f" stroke="#060607" stroke-width="0.22"/>\n'
         # o degrau da trava, na parte de tras
         f'  <rect x="{f(x1 - comprimento * 0.36)}" y="{f(y0 + 0.35)}" width="{f(comprimento * 0.36 - 0.35)}" height="{f(y1 - y0 - 0.7)}" '
         f'rx="0.3" fill="#242428"/>\n'
         # o brilho da quina de cima
         f'  <path d="M{f(x_frente + 0.5)} {f(y0 + 0.25)} H{f(x1 - 0.5)}" stroke="#3a3a40" stroke-width="0.18"/>\n')
    for y in ys:
        # a janela da trava e o contato de metal dentro dela
        s += (f'  <rect x="{f(x_frente + 1.0)}" y="{f(y - 0.75)}" width="2.2" height="1.5" rx="0.2" fill="#09090a"/>\n'
              f'  <rect x="{f(x_frente + 1.35)}" y="{f(y - 0.38)}" width="1.5" height="0.76" rx="0.15" fill="#b8a35a"/>\n'
              f'  <rect x="{f(x_frente + 1.35)}" y="{f(y - 0.38)}" width="1.5" height="0.22" fill="#e6d48c"/>\n'
              # a boca do contato, na frente
              f'  <rect x="{f(x_frente)}" y="{f(y - 0.45)}" width="0.4" height="0.9" fill="#050505"/>\n')
    return s


def fita(x_de, ys_de, x_ate, y_ate, separacao=0.95):
    """A fita de tres fios, do conector (um fio em cada pino) ate a saida do
    servo, onde chegam juntos."""
    s = '  <!-- a fita de tres fios -->\n'
    for (cor, borda), y_de, k in zip(FIOS, ys_de, (-1, 0, 1)):
        y_fim = y_ate + k * separacao
        meio = (x_de + x_ate) / 2
        d = f'M{f(x_de)} {f(y_de)} C{f(meio)} {f(y_de)} {f(meio)} {f(y_fim)} {f(x_ate)} {f(y_fim)}'
        s += (f'  <path d="{d}" fill="none" stroke="{borda}" stroke-width="1.0" stroke-linecap="butt"/>\n'
              f'  <path d="{d}" fill="none" stroke="{cor}" stroke-width="0.74" stroke-linecap="butt"/>\n'
              f'  <path d="{d}" fill="none" stroke="#ffffff" stroke-opacity="0.22" stroke-width="0.18" '
              f'transform="translate(0 -0.18)"/>\n')
    return s


# ------------------------------------------------------------- a caixa

def orelha(x_corpo, lado, y0, y1, comprimento, furos_y, raio_furo, distancia_furo, cor, borda, opacidade=1.0):
    """A orelha de fixacao numa ponta do corpo (lado -1 a esquerda, +1 a
    direita), com cada furo aberto num rasgo ate a borda (o U do fabricante)."""
    x_borda = x_corpo + lado * comprimento
    xa, xb = sorted((x_corpo, x_borda))
    # o contorno com os rasgos: um caminho so (evenodd tira os furos)
    d = f'M{f(xa)} {f(y0 + 0.4)} Q{f(xa)} {f(y0)} {f(xa + 0.4)} {f(y0)} H{f(xb - 0.4)} Q{f(xb)} {f(y0)} {f(xb)} {f(y0 + 0.4)} ' \
        f'V{f(y1 - 0.4)} Q{f(xb)} {f(y1)} {f(xb - 0.4)} {f(y1)} H{f(xa + 0.4)} Q{f(xa)} {f(y1)} {f(xa)} {f(y1 - 0.4)} Z'
    furos = ''
    for yf in furos_y:
        xf = x_corpo + lado * distancia_furo
        # o furo e o rasgo dele ate a ponta
        furos += (f'M{f(xf + raio_furo)} {f(yf)} A{f(raio_furo)} {f(raio_furo)} 0 1 0 {f(xf - raio_furo)} {f(yf)} '
                  f'A{f(raio_furo)} {f(raio_furo)} 0 1 0 {f(xf + raio_furo)} {f(yf)} Z ')
        xr0, xr1 = sorted((xf, x_borda + lado * 0.02))
        furos += f'M{f(xr0)} {f(yf - raio_furo * 0.55)} H{f(xr1)} V{f(yf + raio_furo * 0.55)} H{f(xr0)} Z '
    return (f'  <path d="{d} {furos}" fill="{cor}" fill-opacity="{opacidade}" fill-rule="evenodd" '
            f'stroke="{borda}" stroke-width="0.2"/>\n')


def estriado(ex, ey, r, dentes, cor, sombra, furo, cor_furo):
    """O eixo estriado (o spline onde o braco encaixa) e o furo do parafuso."""
    s = f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="{f(r)}" fill="{cor}" stroke="{sombra}" stroke-width="0.18"/>\n'
    # os dentes: tracos curtos na borda
    import math
    for k in range(dentes):
        a = 2 * math.pi * k / dentes
        x0, y0 = ex + (r - 0.42) * math.cos(a), ey + (r - 0.42) * math.sin(a)
        x1, y1 = ex + r * math.cos(a), ey + r * math.sin(a)
        s += f'  <path d="M{f(x0)} {f(y0)} L{f(x1)} {f(y1)}" stroke="{sombra}" stroke-width="0.14" stroke-opacity="0.8"/>\n'
    s += (f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="{f(r - 0.5)}" fill="none" stroke="#ffffff" stroke-opacity="0.35" stroke-width="0.15"/>\n'
          f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="{f(furo)}" fill="{cor_furo}"/>\n'
          f'  <circle cx="{f(ex - furo * 0.25)}" cy="{f(ey - furo * 0.25)}" r="{f(furo * 0.55)}" fill="#000000" fill-opacity="0.35"/>\n')
    return s


desc = {
    'GND': 'Fio marrom: GND (o mesmo GND da placa)',
    'V+': 'Fio vermelho: alimentacao, 4,8 a 6 V (o 5V/VIN da placa ou uma fonte; nunca um GPIO)',
    'PWM': 'Fio laranja: o sinal, um pulso a cada 20 ms (o GPIO do Servo.attach)',
}


def micro_servo(l, a, ys, x_conector, x0, cy, comprimento=22.7, largura=12.2, orelhas=4.8, furos=13.5,
                marca=None):
    """O micro servo de caixa azul translucida (SG90 e FS90R): o conector, a
    fita, o corpo com o ressalto do eixo na ponta do cabo, as orelhas e as
    engrenagens aparecendo pela tampa. Devolve o SVG do corpo e o eixo."""
    x1 = x0 + comprimento
    y0, y1 = cy - largura / 2, cy + largura / 2
    ex, ey = x0 + largura / 2 - 0.1, cy          # o ressalto encosta na ponta do cabo
    azul, azul_claro, azul_escuro, contorno = '#2a4bd0', '#5574ee', '#1b33a3', '#101f6e'
    s = conector_jr(x_conector, ys, 9.6)
    # a fita sai por baixo da orelha da ponta do cabo, debaixo do eixo
    s += fita(x_conector + 9.6, ys, x0 - orelhas + 1.2, cy + 1.6, separacao=0.95)
    s += f'  <!-- o corpo, de plastico azul translucido -->\n'
    s += orelha(x0, -1, y0 + 0.3, y1 - 0.3, orelhas, [cy], 1.05, (furos - comprimento / 2), azul_claro, contorno, 0.82)
    s += orelha(x1, +1, y0 + 0.3, y1 - 0.3, orelhas, [cy], 1.05, (furos - comprimento / 2), azul_claro, contorno, 0.82)
    s += (f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(comprimento)}" height="{f(largura)}" rx="0.9" fill="{azul}" '
          f'fill-opacity="0.94" stroke="{contorno}" stroke-width="0.25"/>\n'
          # a borda da tampa de cima e o brilho do plastico
          f'  <rect x="{f(x0 + 0.7)}" y="{f(y0 + 0.7)}" width="{f(comprimento - 1.4)}" height="{f(largura - 1.4)}" rx="0.6" '
          f'fill="none" stroke="{azul_claro}" stroke-opacity="0.55" stroke-width="0.25"/>\n'
          f'  <path d="M{f(x0 + 1.0)} {f(y0 + 0.45)} H{f(x1 - 1.0)}" stroke="#ffffff" stroke-opacity="0.35" stroke-width="0.25"/>\n')
    # as engrenagens e o motor, vistos pelo plastico (bem de leve)
    for (gx, gy, gr) in [(ex, ey, 5.0), (ex + 6.2, ey + 1.6, 3.4), (ex + 9.0, ey - 2.2, 2.6), (x1 - 4.4, cy, 3.6)]:
        s += f'  <circle cx="{f(gx)}" cy="{f(gy)}" r="{f(gr)}" fill="none" stroke="{azul_escuro}" stroke-opacity="0.45" stroke-width="0.3" stroke-dasharray="0.35 0.25"/>\n'
    s += f'  <circle cx="{f(x1 - 4.4)}" cy="{f(cy)}" r="2.4" fill="{azul_escuro}" fill-opacity="0.35"/>\n'
    # o ressalto do eixo em "fechadura": o grande e o pequeno ao lado
    s += (f'  <circle cx="{f(ex + 5.4)}" cy="{f(ey)}" r="2.9" fill="{azul}" stroke="{contorno}" stroke-width="0.22"/>\n'
          f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="5.4" fill="{azul}" stroke="{contorno}" stroke-width="0.25"/>\n'
          f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="4.6" fill="none" stroke="{azul_claro}" stroke-opacity="0.6" stroke-width="0.25"/>\n'
          f'  <path d="M{f(ex - 3.6)} {f(ey - 3.2)} A4.8 4.8 0 0 1 {f(ex + 2.4)} {f(ey - 4.3)}" fill="none" stroke="#ffffff" stroke-opacity="0.45" stroke-width="0.3"/>\n')
    s += estriado(ex, ey, 2.4, 21, '#f2f1ea', '#9b998e', 0.95, '#3b3b3e')
    if marca:
        # o nome em relevo na tampa, na outra ponta
        tx = x1 - 6.0
        s += (f'  <text x="{f(tx + 0.12)}" y="{f(cy + 1.0 + 0.12)}" fill="{azul_escuro}" font-size="2.3px" text-anchor="middle" '
              f'font-family="Arial" font-weight="bold" font-style="italic" transform="rotate(-90 {f(tx)} {f(cy)})">{marca}</text>\n'
              f'  <text x="{f(tx)}" y="{f(cy + 1.0)}" fill="{azul_claro}" font-size="2.3px" text-anchor="middle" '
              f'font-family="Arial" font-weight="bold" font-style="italic" transform="rotate(-90 {f(tx)} {f(cy)})">{marca}</text>\n')
    return s, ex, ey


# =====================================================================
#  SG90 (o "servo": a caixa e os pinos do wokwi-servo)
# =====================================================================
l, a = 170 / PX, 120 / PX
ys = [50 / PX, 59.5 / PX, 69 / PX]            # os pinos do Wokwi
corpo, ex, ey = micro_servo(l, a, ys, 0.0, 17.3, 60 / PX, marca='Tower Pro')
gravar_svg('servo', svg(l, a, corpo))
print(f'  SG90: eixo em ({ex * PX:.1f}, {ey * PX:.1f}) px')


# =====================================================================
#  FS90R (o servo 360)
# =====================================================================
l, a = 48.0, 24.0
ys = [a / 2 - PASSO, a / 2, a / 2 + PASSO]
corpo, ex, ey = micro_servo(l, a, ys, 0.3, 17.0, a / 2, comprimento=23.2, largura=12.5, orelhas=4.65, furos=13.75)
gravar_svg('servo-360', svg(l, a, corpo))
print(f'  FS90R: eixo em ({ex * PX:.2f}, {ey * PX:.2f}) px, {ex:.2f} x {ey:.2f} mm')
desc360 = dict(desc)
desc360['PWM'] = 'Fio laranja: o sinal. 1500 us para; 1000 us gira de um lado no maximo, 2000 us do outro'
guardar('servo-360', l, a, [(n, 1.5, y, ['power'] if n != 'PWM' else [], desc360[n]) for n, y in zip(['GND', 'V+', 'PWM'], ys)])


# =====================================================================
#  MG996R
# =====================================================================
l, a = 72.0, 26.0
ys = [a / 2 - PASSO, a / 2, a / 2 + PASSO]
cy = a / 2
comprimento, largura, orelhas = 40.7, 19.7, 6.65
x0 = 21.5
x1 = x0 + comprimento
y0, y1 = cy - largura / 2, cy + largura / 2
ex, ey = x0 + 10.0, cy                      # o eixo na ponta do cabo
preto, tampa, borda, brilho = '#1d1e21', '#26272b', '#09090a', '#3d3f45'
corpo = conector_jr(0.3, ys, 10.6)
corpo += fita(10.9, ys, x0 - orelhas + 1.5, cy + 3.0, separacao=1.0)
# as orelhas, de plastico grosso: dois furos com rasgo em cada uma (os furos a 49,5 x 10 mm)
for lado, xb in ((-1, x0), (1, x1)):
    corpo += orelha(xb, lado, y0 + 1.3, y1 - 1.3, orelhas, [cy - 5.0, cy + 5.0], 1.25, 49.5 / 2 - comprimento / 2, '#202125', borda)
corpo += (f'  <!-- o corpo preto e a tampa de cima -->\n'
          f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(comprimento)}" height="{f(largura)}" rx="1.1" fill="{preto}" stroke="{borda}" stroke-width="0.3"/>\n'
          f'  <rect x="{f(x0 + 0.9)}" y="{f(y0 + 0.9)}" width="{f(comprimento - 1.8)}" height="{f(largura - 1.8)}" rx="0.8" fill="{tampa}" stroke="#141416" stroke-width="0.25"/>\n'
          f'  <path d="M{f(x0 + 1.4)} {f(y0 + 1.15)} H{f(x1 - 1.4)}" stroke="{brilho}" stroke-width="0.25"/>\n')
# o ressalto em "fechadura" e o estriado de metal (25 dentes)
corpo += (f'  <circle cx="{f(ex + 7.8)}" cy="{f(ey)}" r="3.9" fill="{tampa}" stroke="{borda}" stroke-width="0.25"/>\n'
          f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="7.6" fill="#232428" stroke="{borda}" stroke-width="0.3"/>\n'
          f'  <circle cx="{f(ex)}" cy="{f(ey)}" r="6.6" fill="none" stroke="{brilho}" stroke-width="0.3"/>\n'
          f'  <path d="M{f(ex - 5.2)} {f(ey - 4.6)} A6.9 6.9 0 0 1 {f(ex + 3.2)} {f(ey - 6.1)}" fill="none" stroke="#55575e" stroke-width="0.35"/>\n')
corpo += estriado(ex, ey, 2.95, 25, '#c3c7cd', '#6d737c', 1.35, '#2a2c30')
# a etiqueta roxa, em cima, ao lado do eixo (o texto de atravessado)
lx, lw = ex + 12.6, 12.4
corpo += (f'  <!-- a etiqueta -->\n'
          f'  <rect x="{f(lx)}" y="{f(y0 + 2.6)}" width="{f(lw)}" height="{f(largura - 5.2)}" rx="0.5" fill="#5a2a86" stroke="#2e1247" stroke-width="0.2"/>\n'
          f'  <rect x="{f(lx + 0.5)}" y="{f(y0 + 3.1)}" width="{f(lw - 1.0)}" height="{f(largura - 6.2)}" rx="0.35" fill="none" stroke="#e7d7f5" stroke-width="0.18"/>\n')
tx = lx + lw / 2
for dy, texto, tam, peso in ((-3.9, 'Tower Pro', 2.0, 'bold'), (-0.6, 'MG996R', 3.1, 'bold'), (2.7, 'DIGI HI TORQUE', 1.25, 'normal')):
    corpo += (f'  <text x="{f(tx + dy)}" y="{f(cy)}" fill="#ffffff" font-size="{tam}px" text-anchor="middle" dominant-baseline="central" '
              f'font-family="Arial" font-weight="{peso}" transform="rotate(-90 {f(tx + dy)} {f(cy)})">{texto}</text>\n')
gravar_svg('servo-mg996r', svg(l, a, corpo))
print(f'  MG996R: eixo em ({ex * PX:.1f}, {ey * PX:.1f}) px')
guardar('servo-mg996r', l, a, [(n, 1.5, y, ['power'] if n != 'PWM' else [], desc[n]) for n, y in zip(['GND', 'V+', 'PWM'], ys)])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
