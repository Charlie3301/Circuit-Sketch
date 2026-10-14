"""Leitor minimo do .kicad_pcb (KiCad 5) para o desenho das placas (Alpha 25):
o contorno, os modulos (componentes) com as ilhas e a serigrafia, as trilhas
e os textos. Tudo sai em milimetros com o y para CIMA e o giro no sentido
anti-horario (como no Eagle e no Altium), para o mesmo desenho servir."""
import re

_token = re.compile(r'\(|\)|"(?:[^"\\]|\\.)*"|[^\s()]+')


def _ler(texto):
    pilha, atual = [], []
    for t in _token.findall(texto):
        if t == '(':
            pilha.append(atual)
            atual = []
        elif t == ')':
            feito = atual
            atual = pilha.pop()
            atual.append(feito)
        else:
            atual.append(t[1:-1].replace('\\"', '"') if t.startswith('"') else t)
    return atual[0]


def _filhos(no, nome):
    return [f for f in no[1:] if isinstance(f, list) and f and f[0] == nome]


def _um(no, nome):
    f = _filhos(no, nome)
    return f[0] if f else None


def _xy(no):
    """(at x y [giro]) -> (x, -y, giro): y para cima."""
    x, y = float(no[1]), float(no[2])
    return x, -y, float(no[3]) if len(no) > 3 else 0.0


class KicadPcb:
    def __init__(self, arquivo):
        raiz = _ler(open(arquivo, encoding='utf-8').read())

        def camada(no):
            c = _um(no, 'layer')
            return c[1] if c else ''

        def traco(no):
            w = _um(no, 'width')
            return float(w[1]) if w else 0.15

        def desenho(no):
            """Uma linha/arco/circulo/poligono (gr_* ou fp_*), y para cima."""
            tipo = no[0][3:]
            d = dict(tipo=tipo, camada=camada(no), largura=traco(no))
            if tipo in ('line', 'arc', 'circle'):
                s, e = _um(no, 'start') or _um(no, 'center'), _um(no, 'end')
                d.update(x1=float(s[1]), y1=-float(s[2]), x2=float(e[1]), y2=-float(e[2]))
                if tipo == 'arc':
                    d['angulo'] = float(_um(no, 'angle')[1])
            elif tipo == 'poly':
                d['pontos'] = [(float(p[1]), -float(p[2])) for p in _um(no, 'pts')[1:] if p[0] == 'xy']
            return d

        def texto(no):
            x, y, g = _xy(_um(no, 'at'))
            ef = _um(no, 'effects')
            fonte = _um(ef, 'font') if ef else None
            tam = _um(fonte, 'size') if fonte else None
            esp = _um(fonte, 'thickness') if fonte else None
            just = _um(ef, 'justify') if ef else None
            return dict(x=x, y=y, giro=g, camada=camada(no), altura=float(tam[1]) if tam else 1.0,
                        traco=float(esp[1]) if esp else 0.15, justifica=just[1:] if just else [],
                        oculto='hide' in no)

        self.desenhos = [desenho(n) for n in raiz[1:] if isinstance(n, list) and n and n[0] in ('gr_line', 'gr_arc', 'gr_circle', 'gr_poly')]
        self.textos = []
        for n in _filhos(raiz, 'gr_text'):
            t = texto(n)
            t['texto'] = n[1]
            self.textos.append(t)
        self.trilhas = []
        for n in _filhos(raiz, 'segment'):
            s, e = _um(n, 'start'), _um(n, 'end')
            self.trilhas.append(dict(x1=float(s[1]), y1=-float(s[2]), x2=float(e[1]), y2=-float(e[2]),
                                     largura=traco(n), camada=camada(n)))
        self.modulos = []
        for n in _filhos(raiz, 'module'):
            x, y, g = _xy(_um(n, 'at'))
            m = dict(nome=n[1], camada=camada(n), x=x, y=y, giro=g, ilhas=[], desenhos=[], textos=[], ref='', valor='')
            for f in n[1:]:
                if not isinstance(f, list) or not f:
                    continue
                if f[0] in ('fp_line', 'fp_arc', 'fp_circle', 'fp_poly'):
                    m['desenhos'].append(desenho(f))
                elif f[0] == 'fp_text':
                    t = texto(f)
                    t['tipo'], t['texto'] = f[1], f[2]
                    if f[1] == 'reference':
                        m['ref'] = f[2]
                    elif f[1] == 'value':
                        m['valor'] = f[2]
                    m['textos'].append(t)
                elif f[0] == 'pad':
                    px, py, pg = _xy(_um(f, 'at'))
                    tam = _um(f, 'size')
                    furo = _um(f, 'drill')
                    m['ilhas'].append(dict(nome=f[1], tipo=f[2], forma=f[3], x=px, y=py,
                                           giro=pg - g,           # o KiCad 5 grava o giro absoluto
                                           dx=float(tam[1]), dy=float(tam[2]),
                                           furo=float(furo[-1]) if furo and furo[-1] not in ('oval',) else 0.0))
            self.modulos.append(m)
