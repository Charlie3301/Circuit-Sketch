"""contorno-stl.py arquivo.zip membro.stl z saida.json

O contorno de uma placa a partir do STL dela: junta os triangulos horizontais
que estao no plano z (a face de cima da placa) e grava o poligono em mm --
o contorno de fora e os furos -- no mesmo eixo do STL (y para cima).

Foi assim que sairam os dois chassis que so existem como malha 3D:

    2wd-dasaki-placa.json    2wd_robot.zip  2wd_robot.stl                  z 48.4
    4wd-sb43201-placa.json   waston_mod_chassis_top.zip
                             waston_mod_chassis_top.stl                    z 3.0

(os arquivos de origem estao no LICENSE; o STL do 2WD tem 125 MB e por isso
fica de fora do repositorio -- o que entra e o contorno medido.)

Precisa de numpy e shapely.
"""
import json
import re
import struct
import sys
import zipfile

import numpy as np
from shapely.geometry import Polygon
from shapely.ops import unary_union


def ler(arquivo, membro):
    with zipfile.ZipFile(arquivo) as z:
        dados = z.read(membro)
    if dados[:5] == b'solid' and b'facet' in dados[:400]:
        nums = re.findall(rb'vertex\s+(\S+)\s+(\S+)\s+(\S+)', dados)
        return np.array(nums, dtype=np.float64).reshape(-1, 3, 3)
    n = struct.unpack('<I', dados[80:84])[0]
    reg = np.dtype([('n', '<f4', 3), ('v', '<f4', (3, 3)), ('a', '<u2')])
    return np.frombuffer(dados, dtype=reg, count=n, offset=84)['v'].astype(np.float64)


def main():
    v = ler(sys.argv[1], sys.argv[2])
    z = float(sys.argv[3])
    normal = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
    nz = normal[:, 2] / np.maximum(np.linalg.norm(normal, axis=1), 1e-12)
    no_plano = (np.abs(nz) > 0.999) & (np.abs(v[:, :, 2] - z).max(1) < 0.05)
    junto = unary_union([Polygon(t[:, :2]).buffer(0.001) for t in v[no_plano]]).buffer(-0.001)
    partes = list(junto.geoms) if junto.geom_type == 'MultiPolygon' else [junto]
    placa = max(partes, key=lambda p: p.area).simplify(0.02)
    saida = {
        'contorno': [[round(x, 3), round(y, 3)] for x, y in placa.exterior.coords],
        'furos': [[[round(x, 3), round(y, 3)] for x, y in furo.coords] for furo in placa.interiors],
    }
    with open(sys.argv[4], 'w') as f:
        json.dump(saida, f)
    print('bounds', placa.bounds, 'furos', len(placa.interiors))


if __name__ == '__main__':
    main()
