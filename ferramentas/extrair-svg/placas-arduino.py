"""python ferramentas/extrair-svg/placas-arduino.py [placa ...]

As placas Arduino desenhadas a partir dos arquivos de projeto oficiais
(Alpha 25): o .brd do Eagle de cada uma (ferramentas/extrair-svg/arduino-cad,
da Arduino, CC BY-SA 4.0 -- ver License.txt la) da a posicao de cada
componente, cada trilha, cada ilha e cada letra da serigrafia. Daqui sai:

  - o contorno da placa com os furos de fixacao vazados;
  - as trilhas de cobre, vistas por baixo da mascara (um tom mais claro);
  - as vias, as ilhas e os furos;
  - a serigrafia (os nomes dos pinos, DIGITAL, ANALOG IN, POWER...);
  - o corpo de cada componente, no tamanho do encapsulamento real: o
    ATmega328P no soquete, o ATmega16U2, o cristal, o ressonador, os
    reguladores, os capacitores, os resistores, os LEDs, o botao de reset,
    os conectores (USB-B, P4) e as barras de pinos.

Tudo sai em coordenadas finais, sem grupo com transform: o SharpVectors
desloca o desenho inteiro quando ha grupo girado dentro de grupo espelhado
(o Uno saia 20 px para a direita e 10 para baixo).

Os pinos da peca NAO mudam (os do Wokwi, no pecas.json): a escala e o lugar
da placa saem de um ajuste por minimos quadrados entre cada pino e o furo da
barra embaixo dele. O que passa da caixa (o USB-B sai 6,35 mm da placa) fica
numa margem simetrica do SVG (data-sobra); o editor desenha a peca com essa
sobra para fora da caixa, sem mexer no giro nem nos pinos.

O logotipo e a marca "Arduino" nao sao desenhados (o proprio arquivo avisa
que sao marcas registradas): ficam os nomes dos pinos e o modelo da placa.

Imprime, para o C#, onde ficam o botao de reset e os LEDs (ON, L, TX, RX).
"""
import io
import json
import math
import sys
import xml.etree.ElementTree as ET

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
CAD = BASE + 'ferramentas/extrair-svg/arduino-cad/'


def f(v):
    t = f'{v:.3f}'
    return t.rstrip('0').rstrip('.') if '.' in t else t


def num(e, k, padrao=0.0):
    v = e.get(k)
    return float(v) if v is not None else padrao


def giro(s):
    """'MR90' -> (espelhado, angulo)."""
    if not s:
        return False, 0.0
    return 'M' in s, float(s.replace('M', '').replace('S', '').replace('R', '') or 0)


# =====================================================================
#  a transformacao afim (x, y) -> (a x + c y + e, b x + d y + f)
# =====================================================================
class T:
    def __init__(self, a=1.0, b=0.0, c=0.0, d=1.0, e=0.0, f_=0.0):
        self.m = (a, b, c, d, e, f_)

    def __mul__(self, o):
        """self depois de o: (self * o)(p) = self(o(p))."""
        a, b, c, d, e, f_ = self.m
        A, B, C, D, E, F = o.m
        return T(a * A + c * B, b * A + d * B, a * C + c * D, b * C + d * D,
                 a * E + c * F + e, b * E + d * F + f_)

    def __call__(self, x, y):
        a, b, c, d, e, f_ = self.m
        return a * x + c * y + e, b * x + d * y + f_

    @property
    def escala(self):
        a, b, c, d, _, _ = self.m
        return math.sqrt(abs(a * d - b * c))

    @property
    def espelha(self):
        a, b, c, d, _, _ = self.m
        return a * d - b * c < 0

    @property
    def angulo(self):
        """O giro visual (graus, sentido horario na tela) do eixo x local."""
        a, b, _, _, _, _ = self.m
        return math.degrees(math.atan2(b, a))


def mover(x, y):
    return T(e=x, f_=y)


def rodar(ang):
    r = math.radians(ang)
    return T(math.cos(r), math.sin(r), -math.sin(r), math.cos(r))


def espelhar_x():
    return T(a=-1.0)


# =====================================================================
#  o desenho, sempre em coordenadas finais
# =====================================================================
class Desenho:
    def __init__(self, t):
        self.partes = []
        self.pilha = [t]

    @property
    def t(self):
        return self.pilha[-1]

    def entrar(self, t):
        self.pilha.append(self.t * t)

    def sair(self):
        self.pilha.pop()

    def __iadd__(self, s):
        self.partes.append(s)
        return self

    @staticmethod
    def _estilo(fill, stroke=None, sw=0.0, op=None, extra=''):
        s = f' fill="{fill or "none"}"'
        if op is not None:
            s += f' fill-opacity="{op}"'
        if stroke:
            s += f' stroke="{stroke}" stroke-width="{f(sw)}"'
        return s + extra

    # ---- primitivas, em coordenadas locais ----
    def caminho(self, cmds, fill=None, stroke=None, sw=0.0, op=None, cap=None, so=None):
        """cmds: ('M', x, y) ('L', x, y) ('A', r, grande, sentido, x, y) ('Z',)."""
        t = self.t
        out = []
        for c in cmds:
            if c[0] in 'ML':
                x, y = t(c[1], c[2]); out.append(f'{c[0]}{f(x)} {f(y)}')
            elif c[0] == 'A':
                r, grande, sentido, x, y = c[1:]
                X, Y = t(x, y)
                # o sentido do arco inverte quando a transformacao espelha
                s = sentido if not t.espelha else 1 - sentido
                R = r * t.escala
                out.append(f'A{f(R)} {f(R)} 0 {grande} {s} {f(X)} {f(Y)}')
            elif c[0] == 'Z':
                out.append('Z')
        extra = f' stroke-linecap="{cap}"' if cap else ''
        if so:
            extra += f' stroke-opacity="{so}"'
        self.partes.append(f'<path d="{" ".join(out)}"{self._estilo(fill, stroke, sw * self.t.escala, op, extra)}/>\n')

    def poli(self, pts, fill=None, stroke=None, sw=0.0, op=None, fechado=True, cap=None):
        cmds = [('M', *pts[0])] + [('L', *p) for p in pts[1:]] + ([('Z',)] if fechado else [])
        self.caminho(cmds, fill, stroke, sw, op, cap)

    def ret(self, x, y, w, h, fill=None, rx=0.0, stroke=None, sw=0.0, op=None):
        t = self.t
        a, b, c, d, _, _ = t.m
        # alinhado aos eixos na tela (giro multiplo de 90): sai um rect
        if abs(b) < 1e-9 and abs(c) < 1e-9 or abs(a) < 1e-9 and abs(d) < 1e-9:
            xs, ys = zip(t(x, y), t(x + w, y + h))
            X, Y, W, H = min(xs), min(ys), abs(xs[1] - xs[0]), abs(ys[1] - ys[0])
            R = min(rx * t.escala, W / 2, H / 2)
            self.partes.append(f'<rect x="{f(X)}" y="{f(Y)}" width="{f(W)}" height="{f(H)}"'
                               + (f' rx="{f(R)}"' if R > 0 else '') + self._estilo(fill, stroke, sw * t.escala, op) + '/>\n')
        else:
            self.poli([(x, y), (x + w, y), (x + w, y + h), (x, y + h)], fill, stroke, sw, op)

    def ret_c(self, cx, cy, w, h, fill=None, rx=0.0, stroke=None, sw=0.0, op=None):
        self.ret(cx - w / 2, cy - h / 2, w, h, fill, rx, stroke, sw, op)

    def circ(self, cx, cy, r, fill=None, stroke=None, sw=0.0, op=None):
        X, Y = self.t(cx, cy)
        self.partes.append(f'<circle cx="{f(X)}" cy="{f(Y)}" r="{f(r * self.t.escala)}"'
                           + self._estilo(fill, stroke, sw * self.t.escala, op) + '/>\n')

    def linha(self, x1, y1, x2, y2, cor, larg, curva=0.0, so=None, cap='round'):
        cmds = [('M', x1, y1)]
        if curva:
            corda = math.hypot(x2 - x1, y2 - y1)
            r = corda / (2 * math.sin(math.radians(abs(curva)) / 2))
            cmds.append(('A', r, 1 if abs(curva) > 180 else 0, 1 if curva > 0 else 0, x2, y2))
        else:
            cmds.append(('L', x2, y2))
        self.caminho(cmds, None, cor, larg, cap=cap, so=so)

    def texto(self, x, y, t, tam, cor, ang=0.0, ancora='start', peso='bold', alinhar=None, eagle=True):
        """Texto. Com eagle=True, segue o Eagle: (x, y) e o canto de baixo da
        primeira letra, o giro e no sentido anti-horario do espaco local
        (y para cima) e o texto sempre fica legivel (de baixo ou da direita)."""
        dy = 0.0
        if alinhar:
            vert, hor = (alinhar.split('-') + ['center'])[:2] if '-' in alinhar else ('center', 'center') if alinhar == 'center' else ('bottom', alinhar)
            ancora = {'left': 'start', 'center': 'middle', 'right': 'end'}.get(hor, 'start')
            dy = {'top': tam, 'center': tam / 2, 'bottom': 0}.get(vert, 0)
        # o eixo do texto e o "para cima" dele, na tela
        tt = self.t * rodar(ang)
        X, Y = self.t(x, y)
        ux, uy = tt.m[0], tt.m[1]                 # a direcao da escrita
        visual = math.degrees(math.atan2(uy, ux))  # graus, horario na tela (y para baixo)
        visual = (visual + 180) % 360 - 180      # (-180, 180]
        # Legivel como no Eagle: da esquerda para a direita, ou o vertical
        # lendo de baixo para cima (-90). O resto vira 180 graus, com a ancora
        # do outro lado.
        if visual > 90 - 1e-6 or visual < -90 - 1e-6:
            visual = visual - 180 if visual > 0 else visual + 180
            ancora = {'start': 'end', 'end': 'start'}.get(ancora, ancora)
            dy = tam - dy
        # dy e no sentido do "para baixo" do texto ja virado (o Eagle mede para cima a partir da base)
        e = self.t.escala
        t = t.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('  ', '\u00a0\u00a0')
        self.partes.append(
            f'<text transform="translate({f(X)} {f(Y)}) rotate({f(visual)})" x="0" y="{f(dy * e)}" '
            f'font-family="Arial" font-weight="{peso}" font-size="{f(tam * 1.25 * e)}" fill="{cor}" '
            f'text-anchor="{ancora}">{t}</text>\n')


# =====================================================================
#  o arquivo do Eagle
# =====================================================================
class Placa:
    def __init__(self, arquivo):
        raiz = ET.parse(arquivo).getroot()
        self.b = raiz.find('drawing/board')
        self.pacotes = {}
        for lib in self.b.find('libraries'):
            for pk in lib.find('packages'):
                self.pacotes[(lib.get('name'), pk.get('name'))] = pk
        self.elementos = []
        for e in self.b.find('elements'):
            esp, ang = giro(e.get('rot'))
            self.elementos.append(dict(
                nome=e.get('name'), pacote=e.get('package'), valor=e.get('value') or '',
                x=num(e, 'x'), y=num(e, 'y'), giro=ang, espelho=esp,
                pk=self.pacotes[(e.get('library'), e.get('package'))]))
        self.plain = list(self.b.find('plain'))
        self.sinais = list(self.b.find('signals'))

    @staticmethod
    def t_do(el):
        t = mover(el['x'], el['y']) * rodar(el['giro'])
        return t * espelhar_x() if el['espelho'] else t

    def furos_de_barra(self):
        for el in self.elementos:
            if not el['pacote'].upper().startswith(('1X', '2X', 'HEAD')):
                continue
            t = self.t_do(el)
            for p in el['pk'].findall('pad'):
                yield el, t(num(p, 'x'), num(p, 'y'))


def ajustar(placa, pinos, chute):
    """x_px = a*x + tx, y_px = -a*y + ty, pelo par pino -> furo mais perto."""
    return ajustar_pontos([p for _, p in placa.furos_de_barra()], pinos, chute)


def ajustar_pontos(furos, pinos, chute):
    a, tx, ty = chute
    for _ in range(3):
        pares = []
        for pino in pinos:
            q = min(furos, key=lambda q: (a * q[0] + tx - pino['x']) ** 2 + (-a * q[1] + ty - pino['y']) ** 2)
            pares.append((q, (pino['x'], pino['y'])))
        n = len(pares)
        sx = sum(q[0] for q, _ in pares); sy = sum(q[1] for q, _ in pares)
        su = sum(p[0] for _, p in pares); sv = sum(p[1] for _, p in pares)
        s2 = sum(q[0] ** 2 + q[1] ** 2 for q, _ in pares)
        sxu = sum(q[0] * p[0] - q[1] * p[1] for q, p in pares)
        a = (sxu - (sx * su - sy * sv) / n) / (s2 - (sx * sx + sy * sy) / n)
        tx = (su - a * sx) / n
        ty = (sv + a * sy) / n
    erros = [math.hypot(a * q[0] + tx - p[0], -a * q[1] + ty - p[1]) for q, p in pares]
    return (a, tx, ty), max(erros), sum(erros) / len(erros)


# =====================================================================
#  as cores
# =====================================================================
MASCARA = '#1b6aa8'
MASCARA_ESCURA = '#145a91'
TRILHA = '#2a80c2'
SERIGRAFIA = '#f2f5f7'
OURO = '#d8b45a'
ESTANHO = '#cfd3d8'
FURO = '#0c1016'
PRETO_CI = '#1f2023'
MARCA_CI = '#c3c6cb'

DEFS = '''  <linearGradient id="luz" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0.16"/><stop offset="0.45" stop-color="#ffffff" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.22"/></linearGradient>
  <linearGradient id="metal" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f4f6f8"/><stop offset="0.5" stop-color="#b9bfc6"/><stop offset="1" stop-color="#868d95"/></linearGradient>
  <linearGradient id="metal-h" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#8a9199"/><stop offset="0.35" stop-color="#eef1f4"/><stop offset="0.7" stop-color="#b3b9c0"/><stop offset="1" stop-color="#7c838b"/></linearGradient>
  <radialGradient id="lata" cx="0.4" cy="0.38" r="0.66"><stop offset="0" stop-color="#f6f8fa"/><stop offset="0.6" stop-color="#c3c9cf"/><stop offset="1" stop-color="#8c939b"/></radialGradient>
  <linearGradient id="plastico" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3a3c40"/><stop offset="1" stop-color="#16171a"/></linearGradient>
  <linearGradient id="ouro" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f4dc8c"/><stop offset="1" stop-color="#b88a2c"/></linearGradient>
'''


# =====================================================================
#  as ilhas
# =====================================================================
def pad_tht(d, p):
    x, y, drill = num(p, 'x'), num(p, 'y'), num(p, 'drill')
    diam = num(p, 'diameter') or max(drill + 0.6, drill * 1.6)
    forma = p.get('shape') or 'round'
    r = diam / 2
    _, ang = giro(p.get('rot'))
    d.entrar(mover(x, y) * rodar(ang))
    if forma == 'square':
        d.ret_c(0, 0, diam, diam, OURO)
    elif forma == 'octagon':
        k = r * 0.4142
        d.poli([(r, k), (k, r), (-k, r), (-r, k), (-r, -k), (-k, -r), (k, -r), (r, -k)], OURO)
    elif forma == 'long':
        d.ret_c(0, 0, 2 * diam, diam, OURO, rx=r)
    else:
        d.circ(0, 0, r, OURO)
    d.circ(0, 0, drill / 2, FURO)
    d.sair()


def pad_smd(d, p, cor):
    x, y, dx, dy = num(p, 'x'), num(p, 'y'), num(p, 'dx'), num(p, 'dy')
    _, ang = giro(p.get('rot'))
    d.entrar(mover(x, y) * rodar(ang))
    d.ret_c(0, 0, dx, dy, cor, rx=min(dx, dy) / 2 * num(p, 'roundness') / 100)
    d.sair()


# =====================================================================
#  os corpos dos componentes (em mm, no espaco do pacote)
# =====================================================================
def pads_smd(pk):
    return [(num(p, 'x'), num(p, 'y')) for p in pk.findall('smd')]


def chip2(d, pk, comp, larg, corpo, terminal=True, frac=0.2, marca=None, cor_marca='#e8e8e8', banda=None):
    """Dois terminais (resistor, capacitor, LED, diodo...), no eixo dos dois pads."""
    (x1, y1), (x2, y2) = pads_smd(pk)[:2]
    d.entrar(mover((x1 + x2) / 2, (y1 + y2) / 2) * rodar(math.degrees(math.atan2(y2 - y1, x2 - x1))))
    t = comp * frac
    d.ret_c(0, 0, comp, larg, corpo, rx=larg * 0.08)
    d.ret_c(0, 0, comp, larg, 'url(#luz)', rx=larg * 0.08)
    if terminal:
        d.ret(-comp / 2, -larg / 2, t, larg, 'url(#metal)')
        d.ret(comp / 2 - t, -larg / 2, t, larg, 'url(#metal)')
    if banda is not None:
        bx = banda * (comp / 2 - t - comp * 0.1)
        d.ret_c(bx, 0, comp * 0.12, larg, '#3a3a3a' if corpo != '#202124' else '#9ea3a8')
    if marca:
        d.texto(0, 0, marca, larg * 0.38, cor_marca, ancora='middle', alinhar='center', peso='normal')
    d.sair()


def ci(d, comp, larg, linhas, cor=PRETO_CI, pino1=None):
    """O corpo de um CI na origem, com a marcacao (linhas de cima para baixo)."""
    d.ret_c(0, 0, comp, larg, cor, rx=min(comp, larg) * 0.05)
    d.ret_c(0, 0, comp, larg, 'url(#luz)', rx=min(comp, larg) * 0.05)
    if pino1:
        d.circ(pino1[0], pino1[1], min(comp, larg) * 0.07, '#383a3f')
    n = len(linhas)
    passo = max(t for _, t in linhas) * 1.3 if linhas else 0
    for k, (texto, tam) in enumerate(linhas):
        yy = (n - 1) / 2 * passo - k * passo
        d.texto(0, yy, texto, tam, MARCA_CI, ancora='middle', alinhar='center', peso='normal')


def pernas_smd(d, pk, meia_x, meia_y):
    """As pernas de estanho entre o corpo (meia_x, meia_y) e o pad."""
    for p in pk.findall('smd'):
        x, y, dx, dy = num(p, 'x'), num(p, 'y'), num(p, 'dx'), num(p, 'dy')
        _, ang = giro(p.get('rot'))
        if ang % 180:
            dx, dy = dy, dx
        if abs(y) > meia_y:              # em cima ou embaixo
            w = dx * 0.55
            y0 = math.copysign(meia_y, y)
            fim = y + math.copysign(dy * 0.3, y)
            d.ret(x - w / 2, min(y0, fim), w, abs(fim - y0), 'url(#metal-h)')
        elif abs(x) > meia_x:            # dos lados
            w = dy * 0.55
            x0 = math.copysign(meia_x, x)
            fim = x + math.copysign(dx * 0.3, x)
            d.ret(min(x0, fim), y - w / 2, abs(fim - x0), w, 'url(#metal)')


def barra(d, pk, femea):
    """A barra femea (preta, a boca quadrada de cada pino) ou a macho (base e pontas douradas)."""
    pads = [(num(p, 'x'), num(p, 'y')) for p in pk.findall('pad')]
    xs, ys = [p[0] for p in pads], [p[1] for p in pads]
    x0, x1, y0, y1 = min(xs) - 1.27, max(xs) + 1.27, min(ys) - 1.27, max(ys) + 1.27
    if femea:
        d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#plastico)', rx=0.25, stroke='#0a0a0b', sw=0.08)
        for x, y in pads:
            d.ret_c(x, y, 1.24, 1.24, '#0a0a0a')
            d.ret_c(x, y, 0.62, 0.62, '#1a1a1b')
            d.ret_c(x, y + 0.15, 0.62, 0.22, '#b8913a', op=0.6)
    else:
        d.ret(x0 + 0.05, y0 + 0.05, x1 - x0 - 0.1, y1 - y0 - 0.1, 'url(#plastico)', rx=0.2)
        for x, y in pads:
            d.ret_c(x, y, 0.84, 0.84, '#7a5c18')
            d.ret_c(x, y, 0.62, 0.62, 'url(#ouro)')


def corpo(d, el):
    pk, nome, valor = el['pk'], el['pacote'].upper().replace('_', '-'), el['valor']
    if nome.endswith('SOT223'):
        nome = 'SOT223'
    if 'LED' in nome and '0805' in nome:
        nome = 'CHIP-LED0805'
    if nome == '0805-NO':
        nome = '0805-R' if not valor.upper().endswith('F') else '0805-C'
    if valor.endswith('NM'):
        return
    if nome.startswith(('1X', '2X')):
        if not SEM_BARRA:
            barra(d, pk, 'F' in valor.split()[0] if valor else False)
    elif nome.startswith('HEAD'):
        for pad in pk.findall('pad'):
            x, y = num(pad, 'x'), num(pad, 'y')
            d.circ(x, y, 0.82, 'url(#lata)', stroke='#7c838b', sw=0.06)
            d.ret_c(x, y, 0.64, 0.64, '#9aa1a8', rx=0.1)
    elif nome == 'DIL28-3':
        # o soquete de 28 pinos e o ATmega328P em cima
        d.ret_c(0, 0, 37.8, 10.1, '#26272a', rx=0.3, stroke='#0b0b0c', sw=0.1)
        for k in range(14):
            x = -16.51 + k * 2.54
            for y in (-3.81, 3.81):
                d.ret_c(x, y, 1.1, 1.1, '#121314', rx=0.1)
                d.ret_c(x, y * 0.9, 0.6, 0.6, 'url(#metal-h)')
        d.ret_c(0, 0, 36.6, 2.0, '#1c1d20')
        d.caminho([('M', -18.9, -0.9), ('A', 0.9, 0, 1, -18.9, 0.9), ('Z',)], '#141516')
        ci(d, 35.3, 6.4, [('ATMEGA328P-PU', 1.25), ('1923  AR3', 0.8)], pino1=(-15.8, -2.0))
        d.caminho([('M', -17.65, -1.0), ('A', 1.0, 0, 1, -17.65, 1.0), ('Z',)], '#141518')
    elif nome == 'PN61729':
        # o USB-B: a carcaca de metal, da boca (-10,35) ate o fundo (5,6)
        d.ret(-6.0, -10.35, 12.0, 16.0, 'url(#metal-h)', rx=0.3, stroke='#5d646c', sw=0.1)
        d.ret(-5.6, -10.35, 11.2, 1.1, '#7c838b')
        d.ret(-6.0, -10.35, 12.0, 0.35, '#4d535a')
        d.linha(-4.6, -6.5, 4.6, -6.5, '#8d949b', 0.12)
        d.linha(-4.6, 1.5, 4.6, 1.5, '#8d949b', 0.12)
        d.ret_c(0, -4.2, 2.8, 2.8, None, rx=0.3, stroke='#959ca3', sw=0.12)
        d.ret(-4.9, 3.2, 9.8, 2.0, '#a6adb4', rx=0.2)
    elif nome.startswith('POWERSUPPLY'):
        # o conector P4: o cilindro preto deitado, a boca em +y
        d.ret(-5.08, -6.35, 8.99, 13.49, 'url(#plastico)', rx=0.4, stroke='#060607', sw=0.1)
        d.ret(-4.6, -0.4, 8.0, 7.4, '#2a2b2f', rx=2.4)
        d.ret(-3.4, -0.2, 5.6, 7.2, '#33353a', rx=1.6)
        d.linha(-5.08, 6.6, 3.91, 6.6, '#4a4c52', 0.3)
        d.ret(-2.2, -5.8, 4.6, 4.0, '#1a1b1e')
    elif nome == 'TS42':
        # o reset: a base, a tampa de aco e o botao vermelho
        d.ret_c(0, 0, 5.9, 4.2, '#d8d6cf', rx=0.2, stroke='#8e8b82', sw=0.06)
        d.ret_c(0, 0, 5.3, 3.6, 'url(#metal)', rx=0.25, stroke='#7c838b', sw=0.06)
        d.circ(0, 0, 1.45, '#7a1010')
        d.circ(0, 0, 1.25, '#c21d1d')
        d.circ(-0.35, 0.35, 0.45, '#ff8a8a', op=0.45)
    elif nome == 'QS':
        d.ret_c(0, 0, 11.4, 4.6, 'url(#metal)', rx=2.3, stroke='#6f767e', sw=0.08)
        d.ret_c(0, 0, 10.4, 3.6, None, rx=1.8, stroke='#9aa1a8', sw=0.12)
        d.texto(0, 0, valor.replace('MHz', '.000'), 0.95, '#4a4f56', ancora='middle', alinhar='center')
    elif nome == 'RESONATOR':
        d.ret_c(0, 0, 3.2, 1.3, '#d9d4c3', rx=0.1, stroke='#8f8a7a', sw=0.05)
        d.ret_c(0, 0, 2.6, 0.9, '#c4bfad', rx=0.08)
    elif nome == 'PANASONIC-D':
        # o eletrolitico de SMD: a base chanfrada (lado do +), a lata e a marca do -
        d.poli([(-3.25, 3.25), (1.55, 3.25), (3.25, 1.55), (3.25, -1.55), (1.55, -3.25), (-3.25, -3.25)], '#1d1e21')
        d.circ(0, 0, 3.05, 'url(#lata)', stroke='#6c737b', sw=0.08)
        d.caminho([('M', -2.05, 2.25), ('A', 3.05, 0, 1, -2.05, -2.25), ('Z',)], '#1d1e22', op=0.88)
        d.linha(-1.6, 0, 1.6, 0, '#9aa1a8', 0.14)
        d.linha(0, -1.6, 0, 1.6, '#9aa1a8', 0.14)
        d.texto(1.6, 0, valor.replace('uF', '').replace('u', ''), 0.75, '#3a3f45', ang=90, ancora='middle', alinhar='center')
    elif nome == 'TQFP100':
        # o ATmega2560: 14 x 14 mm, 25 pernas de 0,5 mm em cada lado
        pernas_smd(d, pk, 7.0, 7.0)
        ci(d, 14.0, 14.0, [('ATMEGA2560', 1.3), ('16AU', 1.0), ('1921', 0.85)], pino1=(-5.8, 5.8))
    elif nome == 'VQFN32':
        pernas_smd(d, pk, 2.5, 2.5)
        ci(d, 5.0, 5.0, [('MEGA328P', 0.62), ('-MU', 0.5), ('1932', 0.45)], pino1=(-1.9, 1.9))
    elif nome == 'USB-MINI-B-2':
        # o mini-USB: a carcaca de metal, a boca na borda da placa (+y)
        d.ret(-2.25, -1.4, 7.7, 9.0, 'url(#metal-h)', rx=0.3, stroke='#5d646c', sw=0.08)
        d.ret(-2.0, 6.7, 7.2, 0.9, '#5b6168')
        d.linha(-1.6, 2.9, 4.8, 2.9, '#8d949b', 0.1)
        d.ret_c(1.6, 1.0, 2.2, 1.4, None, rx=0.2, stroke='#959ca3', sw=0.1)
    elif nome == 'SMD-157SW':
        # o reset do Nano: a base clara, a tampa de aco e o botao
        d.ret_c(2.0, 0, 3.2, 2.7, '#e6e2d6', rx=0.15, stroke='#8e8b82', sw=0.05)
        d.ret_c(2.0, 0, 2.6, 2.2, 'url(#metal)', rx=0.2)
        d.circ(2.0, 0, 0.85, '#f1efe8', stroke='#9a978e', sw=0.06)
    elif nome == 'QFN16-3MM':
        # o MAX98357A: o QFN de 3 mm, preto, sem pernas
        ci(d, 3.0, 3.0, [('AKK', 0.5)], pino1=(-1.05, 1.05))
    elif nome.startswith('TERMBLOCK-1X2'):
        # o borne azul montado: cobre o contorno da serigrafia; os parafusos
        # em cima de cada pino, e as bocas dos fios na frente (o lado -y)
        xs = [v for w in pk.findall('wire') if w.get('layer') == '21' for v in (num(w, 'x1'), num(w, 'x2'))]
        ys = [v for w in pk.findall('wire') if w.get('layer') == '21' for v in (num(w, 'y1'), num(w, 'y2'))]
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        d.ret(x0, y0, x1 - x0, y1 - y0, '#2f7fd6', rx=0.3, stroke='#1d5aa0', sw=0.1)
        d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#luz)', rx=0.3)
        d.ret(x0 + 0.2, y0 + 0.1, x1 - x0 - 0.4, 1.4, '#2569b8', rx=0.2)
        for pad in pk.findall('pad'):
            x, y = num(pad, 'x'), num(pad, 'y')
            d.ret_c(x, y0 + 0.75, 2.3, 1.0, '#0f1114', rx=0.2)
            d.ret_c(x, y0 + 0.75, 1.6, 0.5, 'url(#metal)', rx=0.1)
            d.circ(x, y, 1.25, 'url(#lata)', stroke='#6d747b', sw=0.08)
            d.ret_c(x, y, 0.35, 1.9, '#5b6168', rx=0.1)
    elif nome == 'MLF32':
        pernas_smd(d, pk, 2.5, 2.5)
        ci(d, 5.0, 5.0, [('MEGA16U2', 0.62), ('-MU', 0.5), ('1925', 0.45)], pino1=(-1.9, 1.9))
    elif nome == 'SOT223':
        pernas_smd(d, pk, 3.25, 1.75)
        ci(d, 6.5, 3.5, [('1117-50', 0.75)])
    elif nome == 'MSOP08':
        pernas_smd(d, pk, 1.5, 1.5)
        ci(d, 3.0, 3.0, [('358', 0.6)], pino1=(-1.0, -1.0))
    elif nome in ('SOT-23', 'SOT23-DBV'):
        pernas_smd(d, pk, 1.45, 0.8)
        ci(d, 2.9, 1.6, [('340' if nome == 'SOT-23' else 'L33', 0.55)])
    elif nome == 'CAY16':
        xs = [x for x, _ in pads_smd(pk)]
        ys = [y for _, y in pads_smd(pk)]
        vertical = max(ys) - min(ys) > max(xs) - min(xs)
        d.entrar(rodar(90 if vertical else 0))
        d.ret_c(0, 0, 3.2, 1.6, '#1c1c1e', rx=0.08)
        for k in range(4):
            for y in (-0.65, 0.65):
                d.ret_c(-1.2 + k * 0.8, y, 0.44, 0.3, 'url(#metal)')
        d.sair()
    elif nome.startswith('C0603') or nome.startswith('CT/CN0603'):
        chip2(d, pk, 1.6, 0.8, '#b7956a' if nome.startswith('C0603') else '#8f9296')
    elif nome.startswith('R0603'):
        codigo = {'1M': '105', '10K': '103', '1K': '102', '22R': '220'}.get(valor.upper(), '')
        chip2(d, pk, 1.6, 0.8, '#1c1c1e', marca=codigo)
    elif nome == 'CHIP-LED0805':
        cor = {'GREEN': '#62d26a', 'YELLOW': '#ffcf3d', 'RED': '#ff4a3d', 'BLUE': '#4aa3ff'}.get(valor.upper(), '#ffcf3d')
        chip2(d, pk, 2.0, 1.25, '#f3f1ea')
        (x1, y1), (x2, y2) = pads_smd(pk)[:2]
        cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
        d.ret_c(cx, cy, 0.9, 0.9, cor, rx=0.15, op=0.55)
        d.ret_c(cx, cy, 0.3, 0.3, '#7d6a3a')
    elif nome == '0805':
        chip2(d, pk, 2.0, 1.25, '#25262a')
    elif nome == 'L1812':
        chip2(d, pk, 4.5, 3.2, '#c39b35', marca='050', cor_marca='#5a4510')
    elif nome == 'SMB':
        catodo = 1 if [p.get('name') for p in pk.findall('smd')][1] == 'C' else -1
        chip2(d, pk, 4.4, 3.6, '#202124', frac=0.12, banda=catodo, marca='M7', cor_marca='#cfd3d8')
    elif nome == '0805-R':
        chip2(d, pk, 2.0, 1.25, '#1c1c1e', marca={'10K': '1002', '1K': '1001', '4.7K': '4701'}.get(valor.upper(), ''))
    elif nome == '0805-C':
        chip2(d, pk, 2.0, 1.25, '#b7956a')
    elif nome == 'SOIC8-150MIL':
        xs, ys = [x for x, _ in pads_smd(pk)], [y for _, y in pads_smd(pk)]
        deitado = max(xs) - min(xs) > max(ys) - min(ys)
        cw, ch = (4.9, 3.9) if not deitado else (3.9, 4.9)
        pernas_smd(d, pk, cw / 2, ch / 2)
        ci(d, cw, ch, [(valor or 'DS1307', 0.78), ('1606', 0.55), ('B49B', 0.5)], pino1=(-cw / 2 + 0.6, -ch / 2 + 0.6))
    elif nome == 'CRYSTAL-8X3.8':
        # o cristal de 32,768 kHz de SMD (o MC-306): o corpo preto e as quatro pernas
        xs, ys = [x for x, _ in pads_smd(pk)], [y for _, y in pads_smd(pk)]
        deitado = max(xs) - min(xs) > max(ys) - min(ys)
        cw, ch = (8.0, 3.8) if deitado else (3.8, 8.0)
        for x, y in pads_smd(pk):
            d.ret_c(x, y, 1.2 if deitado else 1.6, 1.6 if deitado else 1.2, 'url(#metal)', rx=0.1)
        d.ret_c(0, 0, cw, ch, PRETO_CI, rx=0.35)
        d.ret_c(0, 0, cw, ch, 'url(#luz)', rx=0.35)
        d.texto(0, 0, '32.768', 0.75, MARCA_CI, ang=0 if deitado else 90, ancora='middle', alinhar='center', peso='normal')
    elif nome == 'CR1220':
        # o suporte da pilha CR1220: a ilha dourada do negativo e a mola de aco por cima
        ps = pads_smd(pk)
        cx, cy = sum(x for x, _ in ps) / len(ps), sum(y for _, y in ps) / len(ps)
        abas = sorted(ps, key=lambda q: -math.hypot(q[0] - cx, q[1] - cy))[:2]
        vert = abs(abas[0][1] - abas[1][1]) > abs(abas[0][0] - abas[1][0])
        for x, y in abas:
            d.ret_c(x, y, 3.6 if vert else 2.6, 2.6 if vert else 3.6, 'url(#metal)', rx=0.2, stroke='#7c838b', sw=0.06)
        d.circ(cx, cy, 5.0, '#c9a24a')
        w, h = (12.4, 13.6) if vert else (13.6, 12.4)
        d.ret_c(cx, cy, w, h, 'url(#metal)', rx=2.2, stroke='#6f767e', sw=0.08)
        d.circ(cx, cy, 4.6, '#d8b45a', stroke='#8a6d2a', sw=0.08)
        d.circ(cx, cy, 4.6, 'url(#luz)')
        for k in (-1, 1):
            if vert:
                d.ret_c(cx, cy + k * 2.3, 2.2, 1.6, 'url(#metal)', rx=0.3, stroke='#7c838b', sw=0.06)
            else:
                d.ret_c(cx + k * 2.3, cy, 1.6, 2.2, 'url(#metal)', rx=0.3, stroke='#7c838b', sw=0.06)
        d.texto(cx - (4.2 if not vert else 0), cy - (0 if not vert else 4.2), '+', 1.2, '#5a6068', ancora='middle', alinhar='center')
    elif nome == 'MICRO-SD-SOCKET-PP':
        # o soquete push-push de microSD: a carcaca de aco e a boca do cartao
        ps = pads_smd(pk)
        xs, ys = [x for x, _ in ps], [y for _, y in ps]
        cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
        w, h = max(xs) - min(xs) + 0.6, max(ys) - min(ys) + 0.6
        for x, y in ps:
            d.ret_c(x, y, 1.0, 1.0, ESTANHO, rx=0.1)
        d.ret_c(cx, cy, w - 1.2, h - 1.0, 'url(#metal)', rx=0.4, stroke='#6f767e', sw=0.08)
        for k in range(3):
            d.linha(cx - (w - 2.4) / 2, cy - h / 4 + k * h / 4, cx + (w - 2.4) / 2, cy - h / 4 + k * h / 4, '#9aa1a8', 0.12)
        d.ret_c(cx, cy, w * 0.42, h * 0.22, None, rx=0.3, stroke='#8d949b', sw=0.1)
    elif nome in ('SSOP24', 'HTSSOP16'):
        # o TB6612FNG (SSOP24, 7,8 x 5,6 mm) e o DRV8833 (HTSSOP16, 5,0 x 4,4 mm):
        # o corpo no meio das duas fileiras de pernas de 0,65 mm
        xs, ys = [x for x, _ in pads_smd(pk)], [y for _, y in pads_smd(pk)]
        deitado = max(xs) - min(xs) > max(ys) - min(ys)
        comp, larg = (7.8, 5.6) if nome == 'SSOP24' else (5.0, 4.4)
        cw, ch = (comp, larg) if deitado else (larg, comp)
        pernas_smd(d, pk, cw / 2, ch / 2)
        linhas = [('TB6612', 0.7), ('FNG', 0.6), ('5K215', 0.5)] if nome == 'SSOP24' else [('DRV8833', 0.55), ('TI 79Z', 0.45)]
        d.entrar(rodar(0 if deitado else 90))
        ci(d, comp, larg, linhas, pino1=(-comp / 2 + 0.55, -larg / 2 + 0.55))
        d.sair()
    elif nome == 'EIA3528':
        # o tantalo de 10 uF: o corpo laranja-amarelo e a faixa do + (o pad 1)
        chip2(d, pk, 3.5, 2.8, '#d9822b', terminal=False, banda=-1, marca='106', cor_marca='#5a3410')
    elif nome == '0402':
        chip2(d, pk, 1.0, 0.5, '#b7956a')
    elif nome == '-1206':
        chip2(d, pk, 3.2, 1.6, '#1c1c1e', marca='R200')
    elif nome == 'SOT23-WIDE':
        pernas_smd(d, pk, 1.45, 0.8)
        ci(d, 2.9, 1.6, [('415U', 0.5)])
    elif nome == 'MINIMELF':
        catodo = 1 if [p.get('name') for p in pk.findall('smd')][1] == 'C' else -1
        chip2(d, pk, 3.5, 1.5, '#c8553a', frac=0.12, banda=catodo)


# =====================================================================
#  uma placa inteira
# =====================================================================
MARCAS = ('arduino', 'tm', 'made in', 'italy', 'www.', 'designed by', 'tinker it', 'adafruit', 'sparkfun')

# As placas de outros fabricantes (o DS1307 da Adafruit, o microSD da
# SparkFun) vem so com os furos, sem a barra de pinos soldada.
SEM_BARRA = False


def eh_marca(t):
    tl = (t or '').strip().lower()
    return any(tl == m or tl.startswith(m) for m in MARCAS)


def seg_de_fio(w, tw=None):
    return (num(w, 'x1'), num(w, 'y1'), num(w, 'x2'), num(w, 'y2'), num(w, 'curve'))


def pinos_reais(placa, peca, cfg):
    """Os pinos no furo de verdade, na escala real (96 px por polegada), com
    a caixa comecando 4 mm antes da placa (como a do Uno no Wokwi). E para
    a placa cujos pinos do Wokwi nao cabem numa placa de verdade (os da Mega
    sao achatados na horizontal). Os nomes saem da ordem dos pads de cada
    barra; o resto de cada pino (sinais, descricao) fica como estava."""
    a = 96 / 25.4
    antes = cfg.get('antes_mm', 4)          # a caixa comeca antes da placa (4 mm no Uno; 0 nos modulos)
    xs = [num(w, k) for w in placa.plain if w.tag == 'wire' and w.get('layer') == '20' for k in ('x1', 'x2')]
    ys = [num(w, k) for w in placa.plain if w.tag == 'wire' and w.get('layer') == '20' for k in ('y1', 'y2')]
    tx, ty = a * (antes - min(xs)), a * max(ys)
    largura, altura = round(a * (max(xs) - min(xs) + antes)), round(a * (max(ys) - min(ys)))
    antigos = {q['nome']: q for q in peca['pinos']}
    novos = []
    for el in placa.elementos:
        nomes = cfg['pinos_reais'].get(el['nome'])
        if not nomes:
            continue
        t = Placa.t_do(el)
        for pad in el['pk'].findall('pad'):
            nome = nomes.get(pad.get('name')) if isinstance(nomes, dict) else nomes[int(pad.get('name')) - 1]
            if not nome:
                continue
            x, y = t(num(pad, 'x'), num(pad, 'y'))
            q = dict(antigos[nome])
            q['x'], q['y'] = round(a * x + tx, 2), round(-a * y + ty, 2)
            novos.append(q)
    # os dos ICSP vem depois (pinos_extra): ficam como estao
    extras = {n for v in cfg.get('pinos_extra', {}).values() for n in v}
    novos += [dict(antigos[n]) for n in antigos if n in extras]
    faltam = set(antigos) - {q['nome'] for q in novos}
    assert not faltam, f'pinos sem furo: {sorted(faltam)}'
    ordem = list(antigos)
    novos.sort(key=lambda q: ordem.index(q['nome']))
    maior = max(((q['nome'], q['x'] - antigos[q['nome']]['x'], q['y'] - antigos[q['nome']]['y']) for q in novos),
                key=lambda d: abs(d[1]) + abs(d[2]))
    print(f'  pinos no furo real: caixa {largura}x{altura} (era {peca["largura"]}x{peca["altura"]}); '
          f'o que mais andou: {maior[0]} ({maior[1]:+.1f}, {maior[2]:+.1f}) px')
    peca['largura'], peca['altura'], peca['pinos'] = largura, altura, novos
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    lista = [peca if q['id'] == peca['id'] else q for q in lista]
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    return a, tx, ty


# Os dois ICSP: o do chip principal (os mesmos MISO, SCK, MOSI, RESET, 5V e
# GND da barra) e o do ATmega16U2, o chip do USB (o 5V e o GND valem; os
# outros quatro vao nele, e o sketch nao os usa).
DESCRICOES_DO_ICSP = ['MISO', '5V', 'SCK', 'MOSI', 'RESET', 'GND']


def pinos_extra(placa, peca, extras, a, tx, ty):
    """Poe (ou acerta) no pecas.json os pinos dos ICSP, no furo de cada um."""
    existentes = {q['nome']: q for q in peca['pinos']}
    for el in placa.elementos:
        nomes = extras.get(el['nome'])
        if not nomes:
            continue
        do16u2 = el['nome'] == 'ICSP1'
        tt = Placa.t_do(el)
        for pad in el['pk'].findall('pad'):
            k = int(pad.get('name')) - 1
            nome = nomes[k]
            x, y = tt(num(pad, 'x'), num(pad, 'y'))
            base = nome.split('.')[0]
            sinal = DESCRICOES_DO_ICSP[k]
            if do16u2 and sinal not in ('5V', 'GND'):
                desc = f'ICSP do ATmega16U2 (o chip do USB): {sinal}. Serve para regravar o 16U2; o sketch nao usa este pino.'
            elif sinal in ('5V', 'GND'):
                desc = f'ICSP: {sinal} (o mesmo da barra).'
            else:
                desc = f'ICSP: {sinal} (o mesmo pino {base} da barra).'
            q = existentes.get(nome) or {'nome': nome, 'variantes': None}
            q.update({'nome': nome, 'x': round(a * x + tx, 2), 'y': round(-a * y + ty, 2),
                      'sinais': (existentes.get(base, {}).get('sinais') if base in existentes else ['power'] if sinal in ('5V', 'GND') else []),
                      'descricao': desc})
            q.pop('variantes', None)
            if nome not in existentes:
                peca['pinos'].append(q)
                existentes[nome] = q
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    lista = [peca if q['id'] == peca['id'] else q for q in lista]
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f"  pinos dos ICSP: {', '.join(n for v in extras.values() for n in v)}")


def atualizar_pinos(peca, reais):
    """Grava no pecas.json os pinos novos (por nome), mantendo o resto de cada um."""
    antes = {q['nome']: (q['x'], q['y']) for q in peca['pinos']}
    faltam = set(antes) - set(reais)
    assert not faltam, f'pinos sem furo: {sorted(faltam)}'
    for q in peca['pinos']:
        q['x'], q['y'] = reais[q['nome']]
    maior = max(peca['pinos'], key=lambda q: abs(q['x'] - antes[q['nome']][0]) + abs(q['y'] - antes[q['nome']][1]))
    print(f"  pinos no furo real; o que mais andou: {maior['nome']} ({maior['x'] - antes[maior['nome']][0]:+.1f}, "
          f"{maior['y'] - antes[maior['nome']][1]:+.1f}) px")
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    lista = [peca if q['id'] == peca['id'] else q for q in lista]
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))


def ajustar_girada(placa, peca, cfg):
    """A placa girada de 90 graus (o DS1307: a barra embaixo no arquivo, a
    esquerda na peca): X = a*y + tx, Y = a*x + ty, na escala real. O
    deslocamento sai da media entre cada pino e o furo da barra na mesma ordem."""
    a = 96 / 25.4
    el = next(e for e in placa.elementos if e['nome'] == cfg['barra'])
    t = Placa.t_do(el)
    furos = sorted((t(num(p, 'x'), num(p, 'y')) for p in el['pk'].findall('pad')), key=lambda q: q[0])
    pinos = sorted(peca['pinos'], key=lambda q: q['y'])
    tx = sum(q['x'] - a * f_[1] for q, f_ in zip(pinos, furos)) / len(pinos)
    ty = sum(q['y'] - a * f_[0] for q, f_ in zip(pinos, furos)) / len(pinos)
    erro = max(math.hypot(a * f_[1] + tx - q['x'], a * f_[0] + ty - q['y']) for q, f_ in zip(pinos, furos))
    print(f'{id_peca_atual}: girada 90 graus, escala real, desvio dos pinos max {erro:.2f} px')
    return a, tx, ty


id_peca_atual = ''


def gerar(id_peca, cfg):
    global SEM_BARRA, id_peca_atual
    id_peca_atual = id_peca
    SEM_BARRA = cfg.get('sem_barra', False)
    masc, masc_escura, trilha = cfg.get('cores', (MASCARA, MASCARA_ESCURA, TRILHA))
    fora = set(cfg.get('sem_elementos', ()))
    placa = Placa(CAD + cfg['brd'])
    placa.elementos = [e for e in placa.elementos if e['nome'] not in fora]
    catalogo = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}
    peca = catalogo[id_peca]
    girada = cfg.get('girar90', False)
    if girada:
        a, tx, ty = ajustar_girada(placa, peca, cfg)
    elif 'pinos_reais' in cfg:
        a, tx, ty = pinos_reais(placa, peca, cfg)
        print(f'{id_peca}: escala real {a:.4f} px/mm')
    else:
        (a, tx, ty), erro_max, erro_medio = ajustar(placa, peca['pinos'], cfg['chute'])
        print(f'{id_peca}: escala {a:.4f} px/mm, desvio dos pinos max {erro_max:.2f} px, medio {erro_medio:.2f} px')
    if 'pinos_extra' in cfg:
        pinos_extra(placa, peca, cfg['pinos_extra'], a, tx, ty)
    L, A = peca['largura'], peca['altura']
    m = cfg.get('margem', 0)          # a sobra, igual dos dois lados (px)
    d = Desenho(T(0, a, a, 0, tx + m, ty) if girada else T(a, 0, 0, -a, tx + m, ty))

    # ---- a placa: a mascara, com os furos de fixacao vazados ----
    segs = [seg_de_fio(w) for w in placa.plain if w.tag == 'wire' and w.get('layer') == '20']
    cmds = []
    while segs:
        x1, y1, x2, y2, c = segs.pop(0)
        cmds.append(('M', x1, y1))
        inicio, fim = (x1, y1), (x2, y2)
        pedaco = [(x1, y1, x2, y2, c)]
        while True:
            for i, (a1, b1, a2, b2, cc) in enumerate(segs):
                if math.hypot(a1 - fim[0], b1 - fim[1]) < 0.02:
                    pedaco.append((a1, b1, a2, b2, cc)); fim = (a2, b2); segs.pop(i); break
                if math.hypot(a2 - fim[0], b2 - fim[1]) < 0.02:
                    pedaco.append((a2, b2, a1, b1, -cc)); fim = (a1, b1); segs.pop(i); break
            else:
                break
        for (p1, q1, p2, q2, cc) in pedaco:
            if cc:
                r = math.hypot(p2 - p1, q2 - q1) / (2 * math.sin(math.radians(abs(cc)) / 2))
                cmds.append(('A', r, 1 if abs(cc) > 180 else 0, 1 if cc > 0 else 0, p2, q2))
            else:
                cmds.append(('L', p2, q2))
        cmds.append(('Z',))
    furos = [(num(h, 'x'), num(h, 'y'), num(h, 'drill') / 2) for h in placa.plain if h.tag == 'hole']
    for x, y, r in furos:
        cmds += [('M', x - r, y), ('A', r, 1, 0, x + r, y), ('A', r, 1, 0, x - r, y), ('Z',)]
    d.caminho(cmds, masc, masc_escura, 0.25)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
    for x, y, r in furos:
        d.circ(x, y, r + 0.55, None, OURO, 0.5)

    # ---- as trilhas de cobre por baixo da mascara, e as vias ----
    for sig in placa.sinais:
        for w in sig.findall('wire'):
            if w.get('layer') == '1':
                x1, y1, x2, y2, c = seg_de_fio(w)
                d.linha(x1, y1, x2, y2, trilha, max(num(w, 'width'), 0.1), c)
    for sig in placa.sinais:
        for v in sig.findall('via'):
            x, y, dr = num(v, 'x'), num(v, 'y'), num(v, 'drill')
            d.circ(x, y, (num(v, 'diameter') or dr + 0.5) / 2, trilha)
            d.circ(x, y, dr / 2, masc_escura)

    # ---- as ilhas (as dos componentes, estanhadas; as soltas, douradas) ----
    for el in placa.elementos:
        if el['espelho']:
            continue
        d.entrar(Placa.t_do(el))
        fiducial = el['pacote'].startswith(('FIDUCIA', 'FD-'))
        if fiducial:
            d.circ(0, 0, 1.0, masc_escura)
        for p in el['pk'].findall('smd'):
            if p.get('layer') == '1':
                pad_smd(d, p, OURO if el['pacote'] == 'SJ' or fiducial else ESTANHO)
        for p in el['pk'].findall('pad'):
            pad_tht(d, p)
        d.sair()

    # ---- a serigrafia: a dos pacotes e a solta na placa ----
    def silk(e):
        if e.tag == 'wire':
            x1, y1, x2, y2, c = seg_de_fio(e)
            d.linha(x1, y1, x2, y2, SERIGRAFIA, max(num(e, 'width'), 0.08), c)
        elif e.tag == 'circle':
            d.circ(num(e, 'x'), num(e, 'y'), num(e, 'radius'), None, SERIGRAFIA, max(num(e, 'width'), 0.08))
        elif e.tag == 'rectangle':
            x1, y1, x2, y2 = num(e, 'x1'), num(e, 'y1'), num(e, 'x2'), num(e, 'y2')
            _, ang = giro(e.get('rot'))
            d.entrar(mover((x1 + x2) / 2, (y1 + y2) / 2) * rodar(ang))
            d.ret_c(0, 0, abs(x2 - x1), abs(y2 - y1), SERIGRAFIA)
            d.sair()

    for el in placa.elementos:
        if el['espelho'] or el['pacote'] == 'FRAME':
            continue
        d.entrar(Placa.t_do(el))
        for e in el['pk']:
            if e.get('layer') == '21' and e.tag in ('wire', 'circle', 'rectangle'):
                silk(e)
        d.sair()
    textos_49 = cfg.get('textos_49', ('L', 'ON'))
    for e in placa.plain:
        camada = e.get('layer')
        if e.tag in ('wire', 'circle', 'rectangle') and camada in ('21', '25'):
            silk(e)
        elif e.tag == 'text' and (camada in ('21', '25') or (camada == '49' and (e.text or '').strip() in textos_49)):
            t = (e.text or '').strip()
            x, y = num(e, 'x'), num(e, 'y')
            esp, ang = giro(e.get('rot'))
            if not t or eh_marca(t) or (not cfg.get('texto_negativo') and (x < 0 or y < 0)) or '\n' in t or esp \
                    or t in cfg.get('excluir', ()):
                continue
            d.texto(x, y, t, num(e, 'size'), SERIGRAFIA, ang, alinhar=e.get('align'))
    # os textos de mais de uma linha, no lugar medido na foto (o MAX98357A)
    for t, x, y, tam in cfg.get('textos_extra', ()):
        d.texto(x, y, t, tam, SERIGRAFIA)

    # ---- os corpos dos componentes ----
    centros = {}
    for el in placa.elementos:
        if el['espelho']:
            continue
        d.entrar(Placa.t_do(el))
        corpo(d, el)
        d.sair()
        centros[el['nome']] = (a * el['y'] + tx, a * el['x'] + ty) if girada else (a * el['x'] + tx, -a * el['y'] + ty)

    largura_svg = L + 2 * m
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{largura_svg}px" height="{A}px" data-sobra="{m} 0" '
           f'viewBox="0 0 {largura_svg} {A}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'  {len(svg) // 1024} KB; margem {m} px')
    for nome in cfg.get('marcos', []):
        if nome in centros:
            x, y = centros[nome]
            print(f'  {nome}: ({x:.1f}, {y:.1f}) px da caixa')
    return a



# =====================================================================
#  a placa do Altium (o Nano RP2040 Connect)
# =====================================================================
# As cores do Nano RP2040 Connect: a mascara azul-marinho e o ouro (ENIG).
MARINHO = '#1f3559'
MARINHO_ESCURO = '#16284a'
TRILHA_MARINHO = '#2a4673'


def gerar_altium(id_peca, cfg):
    sys.path.insert(0, BASE + 'ferramentas/extrair-svg')
    import altium_pcb as A
    doc = A.PcbDoc(CAD + cfg['pcbdoc'])
    catalogo = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}
    peca = catalogo[id_peca]
    furos = [(i['x'], i['y']) for i in doc.ilhas if i['camada'] == A.MULTI and i['comp'] != 65535]
    (a, tx, ty), erro_max, erro_medio = ajustar_pontos(furos, peca['pinos'], cfg['chute'])
    print(f'{id_peca}: escala {a:.4f} px/mm, desvio dos pinos max {erro_max:.2f} px, medio {erro_medio:.2f} px')
    L, A_ = peca['largura'], peca['altura']
    m = cfg.get('margem', 0)
    d = Desenho(T(a, 0, 0, -a, tx + m, ty))

    # ---- a placa, com os quatro furos de fixacao ----
    cmds = [('M', *doc.contorno[0][:2])] + [('L', *v[:2]) for v in doc.contorno[1:]] + [('Z',)]
    fixacao = [(i['x'], i['y'], i['furo'] / 2) for i in doc.ilhas if i['comp'] == 65535 and i['furo'] > 0]
    for x, y, r in fixacao:
        cmds += [('M', x - r, y), ('A', r, 1, 0, x + r, y), ('A', r, 1, 0, x - r, y), ('Z',)]
    d.caminho(cmds, MARINHO, MARINHO_ESCURO, 0.2)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)

    # ---- as trilhas do lado de cima, e as vias ----
    for tr in doc.trilhas:
        if tr['camada'] == A.TOP:
            d.linha(tr['x1'], tr['y1'], tr['x2'], tr['y2'], TRILHA_MARINHO, max(tr['largura'], 0.08))
    for ar in doc.arcos:
        if ar['camada'] == A.TOP:
            a0, a1 = math.radians(ar['a0']), math.radians(ar['a1'])
            p0 = (ar['cx'] + ar['raio'] * math.cos(a0), ar['cy'] + ar['raio'] * math.sin(a0))
            p1 = (ar['cx'] + ar['raio'] * math.cos(a1), ar['cy'] + ar['raio'] * math.sin(a1))
            varre = (ar['a1'] - ar['a0']) % 360
            d.caminho([('M', *p0), ('A', ar['raio'], 1 if varre > 180 else 0, 1, *p1)], None, TRILHA_MARINHO,
                      max(ar['largura'], 0.08), cap='round')
    for v in doc.vias:
        d.circ(v['x'], v['y'], v['diametro'] / 2, TRILHA_MARINHO)
        d.circ(v['x'], v['y'], v['furo'] / 2, MARINHO_ESCURO)

    # ---- as ilhas (douradas: o Nano RP2040 e ENIG) ----
    for il in doc.ilhas:
        if il['camada'] not in (A.TOP, A.MULTI) or il['comp'] == 65535:
            continue
        d.entrar(mover(il['x'], il['y']) * rodar(il['giro']))
        dx, dy = il['dx'], il['dy']
        if il['forma'] == 2:
            d.ret_c(0, 0, dx, dy, OURO)
        else:
            d.ret_c(0, 0, dx, dy, OURO, rx=min(dx, dy) / 2)
        if il['furo'] > 0:
            d.circ(0, 0, il['furo'] / 2, FURO)
        d.sair()
    for x, y, r in fixacao:
        d.circ(x, y, r + 0.25, None, OURO, 0.5)

    # ---- a serigrafia: os pontos e a marca branca dos pinos GND ----
    for tx_ in doc.textos:
        if tx_['camada'] == A.TOP_OVERLAY and tx_['comp'] == 65535 and tx_['texto'].strip() == '.':
            d.circ(tx_['x'], tx_['y'], 0.12, SERIGRAFIA)
    for nome_gnd in cfg.get('gnd', ()):
        q = next(q for q in peca['pinos'] if q['nome'] == nome_gnd)
        x, y = (q['x'] - tx) / a, -(q['y'] - ty) / a
        d.ret_c(x, y, 1.9, 3.3, None, rx=0.15, stroke=SERIGRAFIA, sw=0.18)

    # ---- os corpos ----
    comps = doc.componentes
    for k, c in enumerate(comps):
        if c['lado'] != 'TOP':
            continue
        pads = [il for il in doc.ilhas if il['comp'] == k]
        d.entrar(mover(c['x'], c['y']) * rodar(c['giro']))
        locais = []
        for il in pads:
            ang = math.radians(-c['giro'])
            dx, dy = il['x'] - c['x'], il['y'] - c['y']
            locais.append((dx * math.cos(ang) - dy * math.sin(ang), dx * math.sin(ang) + dy * math.cos(ang)))
        corpo_altium(d, c, locais)
        d.sair()

    largura_svg = L + 2 * m
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{largura_svg}px" height="{A_}px" data-sobra="{m} 0" '
           f'viewBox="0 0 {largura_svg} {A_}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'  {len(svg) // 1024} KB; margem {m} px')
    for nome in cfg.get('marcos', []):
        c = next((c for c in comps if c['nome'] == nome), None)
        if c:
            print(f'  {nome}: ({a * c["x"] + tx:.1f}, {-a * c["y"] + ty:.1f}) px da caixa')


def caixa_dos(locais, minimo=0.2):
    xs, ys = [p[0] for p in locais] or [0], [p[1] for p in locais] or [0]
    return (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, max(max(xs) - min(xs), minimo), max(max(ys) - min(ys), minimo)


def dois_terminais(d, locais, comp, larg, corpo, marca=None, cor_marca='#e8e8e8', terminal=True, banda=None, frac=0.22):
    (x1, y1), (x2, y2) = locais[:2]
    d.entrar(mover((x1 + x2) / 2, (y1 + y2) / 2) * rodar(math.degrees(math.atan2(y2 - y1, x2 - x1))))
    d.ret_c(0, 0, comp, larg, corpo, rx=larg * 0.08)
    d.ret_c(0, 0, comp, larg, 'url(#luz)', rx=larg * 0.08)
    if terminal:
        t = comp * frac
        d.ret(-comp / 2, -larg / 2, t, larg, 'url(#metal)')
        d.ret(comp / 2 - t, -larg / 2, t, larg, 'url(#metal)')
    if banda is not None:
        d.ret_c(banda * comp * 0.3, 0, comp * 0.12, larg, '#9ea3a8')
    if marca:
        d.texto(0, 0, marca, larg * 0.4, cor_marca, ancora='middle', alinhar='center', peso='normal')
    d.sair()


def corpo_altium(d, c, locais):
    padrao, desc = c['padrao'].upper(), c['descricao'].upper()
    cx, cy, w, h = caixa_dos(locais)
    if padrao.startswith(('CAP-', 'RES-', 'L-0603')) and len(locais) >= 2:
        tam = {'0201': (0.6, 0.3), '0402': (1.0, 0.5), '0603': (1.6, 0.8), '0805': (2.0, 1.25)}[padrao[-4:]]
        cor = '#1c1c1e' if padrao.startswith(('RES', 'L-')) else '#b7956a'
        dois_terminais(d, locais, *tam, cor)
    elif padrao.startswith('LEDC1608'):
        cor = '#62d26a' if 'GREEN' in desc else '#ff9a2e'
        dois_terminais(d, locais, 1.6, 0.8, '#f3f1ea')
        d.ret_c(cx, cy, 0.5, 0.5, cor, rx=0.1, op=0.6)
    elif padrao.startswith('MAKK2016'):
        dois_terminais(d, locais, 2.0, 1.6, '#3a3b3f', marca='3R3', cor_marca='#c3c6cb', frac=0.15)
    elif padrao.startswith('SOD123'):
        dois_terminais(d, locais, 2.8, 1.8, '#202124', banda=1, frac=0.1)
    elif padrao.startswith('SMLP34RGB'):
        d.ret_c(cx, cy, 1.6, 1.5, '#f1efe8', rx=0.1)
        for k, cor in enumerate(('#ff4a3d', '#5cd65a', '#4aa3ff')):
            d.ret_c(cx - 0.35 + k * 0.35, cy, 0.22, 0.22, cor, op=0.8)
    elif padrao.startswith('NINA'):
        # o modulo u-blox: a placa dele, a blindagem de metal com a etiqueta e a antena
        # (no pacote, a antena fica do lado -x: na placa, na ponta de fora)
        d.ret_c(cx, cy, 14.0, 10.0, '#26413a', rx=0.3)
        d.ret_c(cx + 1.6, cy, 10.4, 9.4, 'url(#metal)', rx=0.4, stroke='#7c838b', sw=0.08)
        d.ret_c(cx + 1.6, cy, 8.4, 7.6, '#c9ccd1', rx=0.3)
        d.texto(cx + 1.6, cy + 0.9, 'NINA-W102', 0.85, '#4a4f56', ancora='middle', alinhar='center')
        d.texto(cx + 1.6, cy - 0.6, 'u-blox', 0.7, '#4a4f56', ancora='middle', alinhar='center', peso='normal')
        d.ret_c(cx - 5.4, cy, 2.6, 9.4, '#dfe1e3', rx=0.2)
        d.ret_c(cx - 5.4, cy - 0.3, 0.5, 7.0, OURO)
    elif padrao.startswith('QFN40P700X700'):
        ci_em(d, cx, cy, 7.0, 7.0, [('RP2-B1', 0.9), ('20/48', 0.6), ('P65F44.00', 0.55)], pino1=(-2.8, 2.8))
    elif padrao.startswith('PSON127P600'):
        ci_em(d, cx, cy, 6.0, 5.0, [('AT25SF128A', 0.6), ('MHB', 0.5)], pino1=(-2.4, 1.9))
    elif padrao.startswith('VFLGA14'):
        ci_em(d, cx, cy, 3.0, 2.5, [('DSOX', 0.45)], pino1=(-1.1, 0.9))
    elif padrao.startswith('HCLGA'):
        ci_em(d, cx, cy, 4.0, 3.0, [], cor='#2a2b2f')
        d.circ(cx - 0.6, cy, 0.45, OURO)
        d.circ(cx - 0.6, cy, 0.22, '#1a1b1e')
    elif padrao.startswith('DFN120P120'):
        ci_em(d, cx, cy, 1.6, 1.2, [], cor='#c3c6cb')
    elif padrao.startswith('UDFN8'):
        ci_em(d, cx, cy, 3.0, 2.0, [('608', 0.5)])
    elif padrao.startswith('QFN50P150X200'):
        ci_em(d, cx, cy, 2.0, 1.5, [])
    elif padrao.startswith(('SOT523', 'SOT190')):
        ci_em(d, cx, cy, 1.6 if padrao.startswith('SOT523') else 2.9, 0.8 if padrao.startswith('SOT523') else 1.3, [])
    elif padrao.startswith('U.FL'):
        pass                        # o conector da antena externa vem sem montar (so as ilhas)
    elif padrao.startswith('C&K_PTS820'):
        d.ret_c(cx, cy, 4.2, 3.2, '#ece9e0', rx=0.2, stroke='#9a978e', sw=0.06)
        d.circ(cx, cy, 1.05, '#f7f6f1', stroke='#b5b2a8', sw=0.08)
    elif padrao.startswith('ZX62'):
        # o micro-USB: a carcaca de metal, a boca na borda da placa
        d.ret_c(cx - 0.6, cy, 5.8, 7.6, 'url(#metal-h)', rx=0.3, stroke='#5d646c', sw=0.08)
        d.ret_c(cx - 3.2, cy, 0.7, 7.0, '#5b6168')
        d.linha(cx - 0.6, cy - 3.0, cx - 0.6, cy + 3.0, '#8d949b', 0.1)
    elif padrao.startswith('FIDUCIAL'):
        d.circ(0, 0, 1.0, MARINHO_ESCURO)
        d.circ(0, 0, 0.5, OURO)


def ci_em(d, cx, cy, comp, larg, linhas, cor=PRETO_CI, pino1=None):
    d.entrar(mover(cx, cy))
    ci(d, comp, larg, linhas, cor=cor, pino1=pino1)
    d.sair()


# =====================================================================
#  a placa do KiCad (a Franzininho DIY)
# =====================================================================
# A Franzininho DIY e de fenolite, face simples: o cobre fica embaixo (aparece
# de leve pelo papelao), em cima so os furos com as pernas e a serigrafia.
FENOLITE = '#c9a765'
FENOLITE_ESCURA = '#a8874a'
COBRE_POR_BAIXO = '#9a7434'


def gerar_kicad(id_peca, cfg):
    sys.path.insert(0, BASE + 'ferramentas/extrair-svg')
    import kicad_pcb as K
    doc = K.KicadPcb(CAD + cfg['kicad'])
    catalogo = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}
    peca = catalogo[id_peca]

    def t_do(mod):
        return mover(mod['x'], mod['y']) * rodar(mod['giro'])

    bordas = [g for g in doc.desenhos if g['camada'] == 'Edge.Cuts']
    xs = [v for g in bordas for v in (g['x1'], g['x2'])]
    ys = [v for g in bordas for v in (g['y1'], g['y2'])]
    if 'pinos_reais' in cfg:
        # Os pinos do Wokwi nao batem com a placa de verdade (a barra de 3
        # tem 2,7 mm de passo): ficam no furo real, na escala real, com a
        # placa no mesmo canto da caixa.
        a = 96 / 25.4
        tx, ty = cfg['canto'][0] - a * min(xs), cfg['canto'][1] + a * max(ys)
        reais = {}
        for mod in doc.modulos:
            nomes = cfg['pinos_reais'].get(mod['ref'])
            if not nomes:
                continue
            tt = t_do(mod)
            for il in mod['ilhas']:
                x, y = tt(il['x'], il['y'])
                reais[nomes[int(il['nome']) - 1]] = (round(a * x + tx, 2), round(-a * y + ty, 2))
        atualizar_pinos(peca, reais)
        print(f'{id_peca}: escala real {a:.4f} px/mm')
    else:
        furos = []
        for mod in doc.modulos:
            if mod['nome'].split(':')[-1].startswith(('1X', 'M0')):
                tt = t_do(mod)
                furos += [tt(il['x'], il['y']) for il in mod['ilhas']]
        (a, tx, ty), erro_max, erro_medio = ajustar_pontos(furos, peca['pinos'], cfg['chute'])
        print(f'{id_peca}: escala {a:.4f} px/mm, desvio dos pinos max {erro_max:.2f} px, medio {erro_medio:.2f} px')
    L, A_ = peca['largura'], peca['altura']
    m = cfg.get('margem', 0)
    d = Desenho(T(a, 0, 0, -a, tx + m, ty))

    # ---- a placa de fenolite, com os furos de fixacao ----
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    cmds = [('M', x0, y0), ('L', x1, y0), ('L', x1, y1), ('L', x0, y1), ('Z',)]
    fixacao = [(t_do(mod)(0, 0), mod['ilhas'][0]['furo'] / 2) for mod in doc.modulos
               if mod['ref'].startswith('@HOLE') and mod['ilhas']]
    for (x, y), r in fixacao:
        cmds += [('M', x - r, y), ('A', r, 1, 0, x + r, y), ('A', r, 1, 0, x - r, y), ('Z',)]
    d.caminho(cmds, FENOLITE, FENOLITE_ESCURA, 0.3)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)

    # ---- o cobre de baixo, aparecendo pelo papelao ----
    for tr in doc.trilhas:
        if tr['camada'] == 'Bottom':
            d.linha(tr['x1'], tr['y1'], tr['x2'], tr['y2'], COBRE_POR_BAIXO, tr['largura'], so=0.45)

    # ---- os furos dos componentes (face simples: em cima, so o furo) ----
    for mod in doc.modulos:
        if mod['camada'] != 'Top' or mod['ref'].startswith('@HOLE'):
            continue
        d.entrar(t_do(mod))
        for il in mod['ilhas']:
            if il['furo'] > 0:
                d.circ(il['x'], il['y'], il['furo'] / 2 + 0.2, FENOLITE_ESCURA)
                d.circ(il['x'], il['y'], il['furo'] / 2, '#3a2a12')
        d.sair()

    # ---- a serigrafia ----
    def silk(g):
        if g['tipo'] == 'line':
            d.linha(g['x1'], g['y1'], g['x2'], g['y2'], SERIGRAFIA, g['largura'])
        elif g['tipo'] == 'circle':
            r = math.hypot(g['x2'] - g['x1'], g['y2'] - g['y1'])
            d.circ(g['x1'], g['y1'], r, None, SERIGRAFIA, g['largura'])
        elif g['tipo'] == 'arc':
            # o centro, o ponto de partida e o angulo (y para cima: o sentido inverte)
            cx, cy, sx, sy, ang = g['x1'], g['y1'], g['x2'], g['y2'], -g['angulo']
            r = math.hypot(sx - cx, sy - cy)
            a0 = math.atan2(sy - cy, sx - cx)
            a1 = a0 + math.radians(ang)
            ex, ey = cx + r * math.cos(a1), cy + r * math.sin(a1)
            d.caminho([('M', sx, sy), ('A', r, 1 if abs(ang) > 180 else 0, 1 if ang > 0 else 0, ex, ey)],
                      None, SERIGRAFIA, g['largura'], cap='round')
        elif g['tipo'] == 'poly':
            d.poli(g['pontos'], SERIGRAFIA)

    for g in doc.desenhos:
        if g['camada'] == 'F.SilkS':
            silk(g)
    for mod in doc.modulos:
        if mod['camada'] != 'Top':
            continue
        d.entrar(t_do(mod))
        for g in mod['desenhos']:
            if g['camada'] == 'F.SilkS':
                silk(g)
        d.sair()
    for tx_ in doc.textos:
        if tx_['camada'] != 'F.SilkS':
            continue
        j = tx_['justifica']
        hor = 'left' if 'left' in j else 'right' if 'right' in j else 'center'
        vert = 'top' if 'top' in j else 'bottom' if 'bottom' in j else 'center'
        d.texto(tx_['x'], tx_['y'], tx_['texto'], tx_['altura'], SERIGRAFIA, tx_['giro'],
                alinhar='center' if (hor, vert) == ('center', 'center') else f'{vert}-{hor}')

    # ---- os corpos ----
    for mod in doc.modulos:
        if mod['camada'] != 'Top':
            continue
        d.entrar(t_do(mod))
        corpo_kicad(d, mod)
        d.sair()

    largura_svg = L + 2 * m
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{largura_svg}px" height="{A_}px" data-sobra="{m} 0" '
           f'viewBox="0 0 {largura_svg} {A_}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'  {len(svg) // 1024} KB; margem {m} px')
    for nome in cfg.get('marcos', []):
        mod = next((mm for mm in doc.modulos if mm['ref'] == nome), None)
        if mod:
            x, y = t_do(mod)(*cfg.get('centros', {}).get(nome, (0, 0)))
            print(f'  {nome}: ({a * x + tx:.1f}, {-a * y + ty:.1f}) px da caixa')


CORES_DE_FAIXA = ['#1a1a1a', '#7a4a1e', '#d0312d', '#f07c1e', '#f2d22e', '#2e9a3e', '#2a62c9', '#7a3fb0', '#8c8c8c', '#f2f2f2']


def faixas(valor):
    """As faixas de um resistor de 4 faixas pelo valor ('68R', '1K5', '1K')."""
    v = valor.upper().replace('R', '.').replace('K', 'e3').replace('M', 'e6')
    if 'E3' in v.upper() or 'E6' in v.upper():
        base, mult = valor.upper().split('K' if 'K' in valor.upper() else 'M')
        n = float((base or '0') + '.' + (mult or '0')) * (1e3 if 'K' in valor.upper() else 1e6)
    else:
        n = float(valor.upper().replace('R', '') or 0)
    exp = 0
    while n >= 100:
        n /= 10; exp += 1
    while n < 10 and n > 0:
        n *= 10; exp -= 1
    d1, d2 = int(n // 10), int(round(n % 10))
    return [CORES_DE_FAIXA[d1], CORES_DE_FAIXA[d2], CORES_DE_FAIXA[exp] if exp >= 0 else '#c9a43a', '#c9a43a']


def axial(d, mod, comp, larg, corpo, bandas=(), catodo=None):
    """Um componente axial deitado entre os dois furos: as pernas e o corpo."""
    (x1, y1), (x2, y2) = [(il['x'], il['y']) for il in mod['ilhas'][:2]]
    ang = math.degrees(math.atan2(y2 - y1, x2 - x1))
    vao = math.hypot(x2 - x1, y2 - y1)
    d.entrar(mover((x1 + x2) / 2, (y1 + y2) / 2) * rodar(ang))
    d.linha(-vao / 2, 0, vao / 2, 0, '#b9bec5', 0.5)
    d.ret_c(0, 0, comp, larg, corpo, rx=larg * 0.4)
    d.ret_c(0, 0, comp, larg, 'url(#luz)', rx=larg * 0.4)
    for k, (x, w, cor) in enumerate(bandas):
        d.ret_c(x, 0, w, larg * 0.98, cor)
    if catodo is not None:
        d.ret_c(catodo * comp * 0.32, 0, comp * 0.1, larg * 0.98, '#1a1a1a')
    d.sair()


def corpo_kicad(d, mod):
    nome, valor, ref = mod['nome'].split(':')[-1], mod['valor'], mod['ref']
    if nome == 'USB-A-H':
        # o plugue USB-A macho: a carcaca de metal para fora da placa, com as janelas da trava
        d.ret(-17.8, -6.0, 14.8, 12.0, 'url(#metal-h)', rx=0.3, stroke='#5d646c', sw=0.1)
        d.ret(-17.8, -6.0, 0.6, 12.0, '#7c838b')
        for y in (-3.6, 2.0):
            d.ret(-14.6, y, 2.4, 1.6, '#3a3d42', rx=0.15)
        d.ret(-3.0, -6.0, 3.9, 12.0, '#26272b', rx=0.2)
        for il in mod['ilhas']:
            if il['furo'] and il['dx'] < 2:
                d.circ(il['x'], il['y'], 0.35, '#c9ced4')
    elif nome.startswith('DIP-8'):
        # o soquete torneado e o ATtiny85
        d.ret_c(0 + 3.81, -3.81, 10.2, 10.0, '#26272a', rx=0.3)
        pads = [(il['x'], il['y']) for il in mod['ilhas']]
        for x, y in pads:
            d.circ(x, y, 0.75, 'url(#metal)')
            d.circ(x, y, 0.35, '#2a2b2e')
        cx, cy = sum(x for x, _ in pads) / 8, sum(y for _, y in pads) / 8
        d.entrar(mover(cx, cy) * rodar(90))
        ci(d, 9.4, 6.35, [('ATTINY85', 1.0), ('20PU', 0.75)], pino1=(-3.6, -2.2))
        d.caminho([('M', -4.7, -0.9), ('A', 0.9, 0, 1, -4.7, 0.9), ('Z',)], '#141518')
        d.sair()
    elif nome.startswith('TO-92'):
        pads = [(il['x'], il['y']) for il in mod['ilhas']]
        cx = sum(x for x, _ in pads) / len(pads)
        d.caminho([('M', cx - 2.4, -0.6), ('L', cx + 2.4, -0.6), ('A', 2.45, 0, 1, cx - 2.4, -0.6), ('Z',)], '#1d1e21')
        d.caminho([('M', cx - 2.4, -0.6), ('L', cx + 2.4, -0.6), ('A', 2.45, 0, 1, cx - 2.4, -0.6), ('Z',)], 'url(#luz)')
        d.texto(cx, 0.6, '78L05', 0.8, MARCA_CI, ancora='middle', alinhar='center', peso='normal')
    elif nome.startswith('CP_Radial'):
        (x1, y1), (x2, y2) = [(il['x'], il['y']) for il in mod['ilhas'][:2]]
        cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
        d.circ(cx, cy, 2.55, '#1b1c20')
        d.circ(cx, cy, 2.0, 'url(#lata)')
        d.linha(cx - 1.2, cy, cx + 1.2, cy, '#8a9199', 0.15)
        d.linha(cx, cy - 1.2, cx, cy + 1.2, '#8a9199', 0.15)
        # a faixa do negativo, do lado do segundo furo
        ang = math.degrees(math.atan2(y2 - y1, x2 - x1))
        d.entrar(mover(cx, cy) * rodar(ang))
        d.caminho([('M', 1.55, -1.8), ('A', 2.4, 0, 1, 1.55, 1.8), ('L', 2.1, 0), ('Z',)], '#e8e8e8', op=0.85)
        d.sair()
    elif nome == 'AXIAL-0.4':
        f = faixas(valor)
        axial(d, mod, 6.0, 2.3, '#d9c08a', [(-1.9, 0.5, f[0]), (-1.1, 0.5, f[1]), (-0.3, 0.5, f[2]), (1.7, 0.5, f[3])])
    elif nome.startswith('DIODE-1N4148'):
        axial(d, mod, 3.8, 1.7, '#d2583a', catodo=1)
    elif nome.startswith('DIODE-BZT52'):
        axial(d, mod, 3.8, 1.7, '#d2583a', catodo=-1)
    elif nome.startswith('C025'):
        (x1, y1), (x2, y2) = [(il['x'], il['y']) for il in mod['ilhas'][:2]]
        d.entrar(mover((x1 + x2) / 2, (y1 + y2) / 2) * rodar(math.degrees(math.atan2(y2 - y1, x2 - x1))))
        d.ret_c(0, 0, 5.0, 2.4, '#e07a2a', rx=1.2)
        d.ret_c(0, 0, 5.0, 2.4, 'url(#luz)', rx=1.2)
        d.texto(0, 0, '104', 0.9, '#5a2e0c', ancora='middle', alinhar='center')
        d.sair()
    elif nome == 'LED3MM':
        (x1, y1), (x2, y2) = [(il['x'], il['y']) for il in mod['ilhas'][:2]]
        cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
        cor = '#3fd25a' if ref == 'ON' else '#f2d43a'
        d.circ(cx, cy, 1.75, cor, op=0.5, stroke=cor, sw=0.15)
        d.circ(cx, cy, 1.45, cor, op=0.75)
        d.circ(cx - 0.45, cy + 0.45, 0.45, '#ffffff', op=0.5)
    elif nome == 'TAC-SWITCH-PTH':
        pads = [(il['x'], il['y']) for il in mod['ilhas']]
        cx, cy = sum(x for x, _ in pads) / len(pads), sum(y for _, y in pads) / len(pads)
        d.ret_c(cx, cy, 6.0, 6.0, '#2a2b2f', rx=0.3)
        d.ret_c(cx, cy, 6.0, 6.0, 'url(#luz)', rx=0.3)
        for x, y in pads:
            d.ret_c(x, y, 1.0, 1.6, 'url(#metal)', rx=0.1)
        d.circ(cx, cy, 1.75, '#141416', stroke='#3a3c40', sw=0.2)
        for k in range(4):
            d.circ(cx + (1 if k % 2 else -1) * 2.2, cy + (1 if k // 2 else -1) * 2.2, 0.35, '#3a3c40')
    elif nome in ('1X08_ROUND', '1X03'):
        pads = [(il['x'], il['y']) for il in mod['ilhas']]
        xs, ys = [x for x, _ in pads], [y for _, y in pads]
        d.ret(min(xs) - 1.27, min(ys) - 1.27, max(xs) - min(xs) + 2.54, max(ys) - min(ys) + 2.54, 'url(#plastico)', rx=0.2)
        for x, y in pads:
            d.ret_c(x, y, 0.84, 0.84, '#5c6168')
            d.ret_c(x, y, 0.62, 0.62, 'url(#metal)')


# =====================================================================
#  o ATtiny85 sozinho (DIP-8), para espetar na protoboard
# =====================================================================
def gerar_attiny85(id_peca, cfg):
    """O ATtiny85-20PU de verdade, visto de cima: o corpo de 9,27 x 6,35 mm,
    as oito pernas com o ombro largo saindo do corpo, o chanfro do pino 1 e
    a marcacao. As fileiras a 7,62 mm (as colunas E e F da protoboard): o
    desenho antigo tinha 7,2 mm e nao caia nos furos. Os pinos ficam na
    ponta de cada perna; a altura (o passo de 2,54 mm) nao muda."""
    catalogo = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}
    peca = catalogo[id_peca]
    a = 96 / 25.4
    L, A_ = peca['largura'], peca['altura']
    cx = L / 2 / a                                    # o meio da caixa, em mm
    ys = sorted({q['y'] / a for q in peca['pinos']})  # as quatro alturas, em mm
    cy = (ys[0] + ys[-1]) / 2
    xe, xd = cx - 3.81, cx + 3.81
    reais = {}
    for q in peca['pinos']:
        reais[q['nome']] = (round((xe if q['x'] < L / 2 else xd) * a, 2), q['y'])
    atualizar_pinos(peca, reais)

    d = Desenho(T(a, 0, 0, -a, 0, 0) * T(1, 0, 0, -1, 0, 0))   # mm com y para baixo
    # as pernas: o ombro largo junto do corpo e a ponta fina ate o pino
    for y in ys:
        for lado in (-1, 1):
            borda = cx + lado * 3.175
            ponta = cx + lado * 3.81
            d.ret(min(borda, borda + lado * 0.55), y - 0.76, 0.55, 1.52, 'url(#metal)')
            d.ret(min(borda + lado * 0.55, ponta + lado * 0.35), y - 0.25, abs(ponta + lado * 0.35 - borda - lado * 0.55), 0.5, 'url(#metal)')
    # o corpo de epoxi, o chanfro do pino 1 e a bolinha
    topo = cy - 9.27 / 2
    d.ret(cx - 3.175, topo, 6.35, 9.27, PRETO_CI, rx=0.35)
    d.ret(cx - 3.175, topo, 6.35, 9.27, 'url(#luz)', rx=0.35)
    d.ret(cx - 2.9, topo + 0.25, 5.8, 8.77, None, rx=0.3, stroke='#2c2e33', sw=0.12)
    d.caminho([('M', cx - 0.9, topo), ('A', 0.9, 0, 0, cx + 0.9, topo), ('Z',)], '#121316')
    d.circ(cx - 2.0, topo + 1.3, 0.38, '#2f3136')
    for texto, dx, tam in (('ATTINY85', -0.55, 0.95), ('20PU', 0.75, 0.75), ('1932', 1.85, 0.6)):
        d.texto(cx + dx, cy, texto, tam, MARCA_CI, ang=90, ancora='middle', alinhar='center', peso='normal')

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{L}px" height="{A_}px" '
           f'viewBox="0 0 {L} {A_}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'{id_peca}: {len(svg) // 1024} KB')


PLACAS = {
    'arduino-uno': dict(brd='UNO-TH_Rev3e.brd', chute=(3.7751, 4 * 3.7751, 53.34 * 3.7751), margem=10,
                        marcos=['RESET', 'ON', 'L', 'TX', 'RX'],
                        pinos_extra={'ICSP': ['12.2', '5V.2', '13.2', '11.2', 'RESET.2', 'GND.4'],
                                     'ICSP1': ['MISO2', '5V.3', 'SCK2', 'MOSI2', 'RESET2', 'GND.5']}),
    'arduino-mega': dict(brd='MEGA2560_Rev3e.brd', margem=10,
                         marcos=['RESET', 'ON', 'L', 'TX', 'RX'], excluir=('PC1', 'PC2'),
                         pinos_extra={'ICSP': ['50.2', '5V.3', '52.2', '51.2', 'RESET.2', 'GND.6'],
                                      'ICSP1': ['MISO2', '5V.4', 'SCK2', 'MOSI2', 'RESET2', 'GND.7']},
                         pinos_reais={
                             'JP6': ['SCL', 'SDA', 'AREF', 'GND.1', '13', '12', '11', '10', '9', '8'],
                             'PWML': ['0', '1', '2', '3', '4', '5', '6', '7'],
                             'COMMUNICATION': ['21', '20', '19', '18', '17', '16', '15', '14'],
                             'XIO': ['5V.1', '5V.2'] + [str(n + 19) for n in range(3, 35)] + ['GND.4', 'GND.5'],
                             'POWER': [None, 'IOREF', 'RESET', '3.3V', '5V', 'GND.2', 'GND.3', 'VIN'],
                             'ADCL': [f'A{n}' for n in range(8)],
                             'ADCH': [f'A{n}' for n in range(8, 16)],
                         }),
    'arduino-nano': dict(brd='NanoV3.3.brd', chute=(3.764, 3.764 * 1.4 + 0.5, 3.764 * 17.78), margem=0,
                         marcos=['SW1', 'PWR', 'L', 'TX', 'RX']),
    'nano-rp2040-connect': dict(pcbdoc='Nano_RP2040_Connect.PcbDoc', chute=(3.769, 4.0, 3.769 * 17.78), margem=0,
                                marcos=['PB1', 'DL1', 'DL2', 'DL3'], gnd=('GND.1', 'GND.2')),
    'franzininho': dict(kicad='Franzininho.kicad_pcb', canto=(48.0, 0.0), margem=8, marcos=['RESET', 'ON', 'LED'],
                        pinos_reais={'JP3': ['PB0', 'PB1', 'PB2', 'PB3', 'PB5', 'PB4', 'VCC.1', 'GND.1'],
                                     'J1': ['VCC.2', 'GND.2', 'VIN']}),
    'attiny85': dict(a_mao=True),
    # Outros fabricantes, mesmo caminho (Alpha 25): o RTC DS1307 da Adafruit
    # (#3296, CC BY-SA 3.0) e o microSD da SparkFun (BOB-00544, CC BY-SA 4.0);
    # os logotipos (marcas registradas) ficam de fora.
    'ds1307': dict(brd='../outros-cad/DS1307_RevB.brd', girar90=True, barra='JP2', sem_barra=True,
                   cores=('#1f63c0', '#174f9c', '#2c72cf'), sem_elementos=('U$2', 'U$3', 'U$5'),
                   marcos=['B1', 'U1', 'Q1']),
    # O amplificador I2S da Adafruit (produto 3006, Alpha 25): o borne
    # montado em cima (o - a esquerda, o + a direita, como na placa).
    'max98357a': dict(brd='../modulos-cad/MAX98357_Breakout.brd', antes_mm=0, sem_barra=True,
                      pinos_reais={'JP1': ['VIN', 'GND', 'SD', 'GAIN', 'DIN', 'BCLK', 'LRC'], 'X1': ['OUT-', 'OUT+']},
                      cores=('#1f63c0', '#174f9c', '#2c72cf'), sem_elementos=('U$14', 'U$15'), marcos=['U1'],
                      textos_extra=[('MAX', 0.55, 8.74, 1.143), ('98357A', 0.55, 7.28, 1.143), ('I2S Amp.', 0.55, 5.83, 1.143)]),
    # Os drivers de motor de placa aberta (Alpha 25): o TB6612FNG da SparkFun
    # (ROB-14451/09457, CC BY-SA 4.0) -- o mesmo desenho das placas vermelhas
    # que se vendem -- e o DRV8833 da Adafruit (produto 3297, CC BY-SA 3.0),
    # com o borne de 3,5 mm do VMotor. Os logotipos ficam de fora.
    'tb6612fng': dict(brd='../motores-cad/SparkFun_Motor_Driver-TB6612FNG_v11.brd', antes_mm=0, sem_barra=True,
                      pinos_reais={'JP1': ['GND.3', 'BO1', 'BO2', 'AO2', 'AO1', 'GND.2', 'VCC', 'VM'],
                                   'JP2': ['PWMA', 'AIN2', 'AIN1', 'STBY', 'BIN1', 'BIN2', 'PWMB', 'GND']},
                      cores=('#c8102e', '#9a0c22', '#d8283f'), sem_elementos=('LOGO1', 'LOGO2', 'LOGO4', 'FRAME1'),
                      marcos=['U1']),
    'drv8833': dict(brd='../motores-cad/Adafruit_DRV8833.brd', antes_mm=0, sem_barra=True, texto_negativo=True,
                    pinos_reais={'JP5': ['VM', 'GND', 'FLT', 'BIN1', 'BIN2', 'SLP', 'AIN2', 'AIN1'],
                                 'JP2': ['AO2', 'AO1'], 'JP3': ['BO1', 'BO2'], 'JP4': ['AS', 'BS'], 'J1': ['VM.2', 'GND.2']},
                    cores=('#1f63c0', '#174f9c', '#2c72cf'), sem_elementos=('U$17',), marcos=['U1']),
    'microsd-card': dict(brd='../outros-cad/SparkFun_microSD_Breakout.brd', chute=(3.78, 0.0, 77.0), sem_barra=True,
                         cores=('#c8102e', '#9a0c22', '#d8283f'), sem_elementos=('U$1',), marcos=['U1']),
}

if __name__ == '__main__':
    for id_peca in (sys.argv[1:] or list(PLACAS)):
        cfg = PLACAS[id_peca]
        (gerar_altium if 'pcbdoc' in cfg else gerar_kicad if 'kicad' in cfg
         else gerar_attiny85 if 'a_mao' in cfg else gerar)(id_peca, cfg)
