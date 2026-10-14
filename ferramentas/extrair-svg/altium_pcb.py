"""Leitor minimo do .PcbDoc do Altium (Alpha 25), so o que o desenho das
placas precisa: o contorno, os componentes, as ilhas, as trilhas, os arcos,
as vias e os textos. O arquivo e um OLE (precisa do pacote olefile); cada
fluxo "Xxx6/Data" e uma fila de registros: um byte de tipo e, para cada
sub-registro, um uint32 de tamanho e os dados. A ordem dos campos binarios e
a do importador do KiCad (altium_parser_pcb.cpp). As medidas internas sao em
1/10000 mil; aqui saem em milimetros, com o y para cima (como no Altium).
"""
import struct

import olefile

UNIDADE = 2.54e-6          # 1/10000 mil, em mm
TOP, BOTTOM, TOP_OVERLAY, BOTTOM_OVERLAY, TOP_SOLDER, MULTI = 1, 32, 33, 34, 37, 74


def _mil(t):
    return float(t.replace('mil', '')) * 0.0254


class Leitor:
    def __init__(self, dados):
        self.d, self.i = dados, 0

    def fim(self):
        return self.i >= len(self.d)

    def u8(self):
        v = self.d[self.i]; self.i += 1; return v

    def u32(self):
        v = struct.unpack_from('<I', self.d, self.i)[0]; self.i += 4; return v

    def sub(self):
        """Um sub-registro inteiro: (bytes)."""
        n = self.u32()
        b = self.d[self.i:self.i + n]
        self.i += n
        return b


def _i32(b, k):
    return struct.unpack_from('<i', b, k)[0] * UNIDADE


def _u16(b, k):
    return struct.unpack_from('<H', b, k)[0]


def _f64(b, k):
    return struct.unpack_from('<d', b, k)[0]


def _texto(b):
    n = b[0]
    return b[1:1 + n].decode('latin-1')


def registros_texto(dados):
    i = 0
    while i + 4 <= len(dados):
        n = int.from_bytes(dados[i:i + 4], 'little')
        i += 4
        bruto = dados[i:i + n].rstrip(b'\x00').decode('latin-1')
        i += n
        d = {}
        for par in bruto.split('|'):
            if '=' in par:
                k, v = par.split('=', 1)
                d[k] = v
        yield d


class PcbDoc:
    def __init__(self, arquivo):
        ole = olefile.OleFileIO(arquivo)
        ler = lambda nome: ole.openstream(nome).read() if ole.exists(nome) else b''

        # ---- o contorno da placa ----
        b = next(registros_texto(ler('Board6/Data')))
        self.contorno = []
        k = 0
        while f'VX{k}' in b:
            self.contorno.append((_mil(b[f'VX{k}']), _mil(b[f'VY{k}']), int(b.get(f'KIND{k}', '0')),
                                  _mil(b.get(f'CX{k}', '0mil')), _mil(b.get(f'CY{k}', '0mil')), _mil(b.get(f'R{k}', '0mil')),
                                  float(b.get(f'SA{k}', '0')), float(b.get(f'EA{k}', '0'))))
            k += 1

        # ---- os componentes ----
        self.componentes = []
        for c in registros_texto(ler('Components6/Data')):
            self.componentes.append(dict(
                nome=c.get('SOURCEDESIGNATOR', ''), lado=c.get('LAYER', 'TOP'),
                x=_mil(c.get('X', '0mil')), y=_mil(c.get('Y', '0mil')), giro=float(c.get('ROTATION', '0')),
                padrao=c.get('PATTERN', ''), descricao=c.get('SOURCEDESCRIPTION', '')))

        # ---- as ilhas ----
        self.ilhas = []
        r = Leitor(ler('Pads6/Data'))
        while not r.fim():
            if r.u8() != 2:
                break
            nome = _texto(r.sub())
            r.sub(); r.sub(); r.sub()
            s5 = r.sub()
            extra = r.sub() if not r.fim() and r.d[r.i] != 2 else b''
            self.ilhas.append(dict(
                nome=nome, camada=s5[0], comp=_u16(s5, 7),
                x=_i32(s5, 13), y=_i32(s5, 17), dx=_i32(s5, 21), dy=_i32(s5, 25),
                furo=_i32(s5, 45), forma=s5[49], giro=_f64(s5, 52), plated=s5[60] != 0))

        # ---- as trilhas e os arcos ----
        self.trilhas = []
        r = Leitor(ler('Tracks6/Data'))
        while not r.fim():
            if r.u8() != 4:
                break
            s = r.sub()
            self.trilhas.append(dict(camada=s[0], comp=_u16(s, 7), x1=_i32(s, 13), y1=_i32(s, 17),
                                     x2=_i32(s, 21), y2=_i32(s, 25), largura=_i32(s, 29)))
        self.arcos = []
        r = Leitor(ler('Arcs6/Data'))
        while not r.fim():
            if r.u8() != 1:
                break
            s = r.sub()
            self.arcos.append(dict(camada=s[0], comp=_u16(s, 7), cx=_i32(s, 13), cy=_i32(s, 17), raio=_i32(s, 21),
                                   a0=_f64(s, 25), a1=_f64(s, 33), largura=_i32(s, 41)))

        # ---- as vias ----
        self.vias = []
        r = Leitor(ler('Vias6/Data'))
        while not r.fim():
            if r.u8() != 3:
                break
            s = r.sub()
            self.vias.append(dict(x=_i32(s, 13), y=_i32(s, 17), diametro=_i32(s, 21), furo=_i32(s, 25)))

        # ---- os retangulos cheios ----
        self.cheios = []
        r = Leitor(ler('Fills6/Data'))
        while not r.fim():
            if r.u8() != 6:
                break
            s = r.sub()
            self.cheios.append(dict(camada=s[0], comp=_u16(s, 7), x1=_i32(s, 13), y1=_i32(s, 17),
                                    x2=_i32(s, 21), y2=_i32(s, 25), giro=_f64(s, 29)))

        # ---- os textos ----
        self.textos = []
        r = Leitor(ler('Texts6/Data'))
        while not r.fim():
            if r.u8() != 5:
                break
            s1 = r.sub()
            s2 = r.sub()
            self.textos.append(dict(camada=s1[0], comp=_u16(s1, 7), x=_i32(s1, 13), y=_i32(s1, 17),
                                    altura=_i32(s1, 21), giro=_f64(s1, 27), espelho=s1[35] != 0,
                                    traco=_i32(s1, 36), texto=_texto(s2) if s2 else ''))
