"""Separa a PARTE QUE SE MEXE de cada peca em um SVG proprio.

O potenciometro ja era assim: "<id>-corpo.svg" sem o botao e
"<id>-movel.svg" so com ele, e o editor gira o segundo por cima do
primeiro. Este script faz o mesmo para o resto das pecas que o professor
apontou como paradas -- chave deslizante, joystick, potenciometro
deslizante, encoder, disco de telefone e motor de passo.

Roda direto sobre app/assets: nao precisa do node nem dos wokwi-elements
(o extrair.mjs so sabe fazer isso para os ids que ele conhece, e alguns
desses elementos nem tem id no desenho).

Na Alpha 25 as entradas foram redesenhadas (pecas-entradas.py, que ja grava
o corpo e o movel de cada uma); aqui ficou so o motor de passo do Wokwi.

    python ferramentas/extrair-svg/animar.py
"""
import io
import re
import xml.etree.ElementTree as ET

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/app/assets/'
SVG = 'http://www.w3.org/2000/svg'

ET.register_namespace('', SVG)
ET.register_namespace('xlink', 'http://www.w3.org/1999/xlink')

# id do desenho -> ids dos elementos que se mexem
PARTES = {
    'stepper-motor': ['shaft-path', 'arrow-path'],
}


def ramo_tem(no, alvos):
    if no in alvos:
        return True
    return any(ramo_tem(f, alvos) for f in no)


def podar(no, alvos):
    """Deixa so o caminho ate os alvos -- e os defs, que todos usam."""
    manter = no in alvos
    for filho in list(no):
        if filho in alvos:
            manter = True
            continue
        if filho.tag == f'{{{SVG}}}defs':
            continue
        if podar(filho, alvos):
            manter = True
        else:
            no.remove(filho)
    return manter


def achar(raiz, ids):
    achados = []
    for no in raiz.iter():
        if no.get('id') in ids:
            achados.append(no)
    return achados


def tirar(no, alvos):
    for filho in list(no):
        if filho in alvos:
            no.remove(filho)
        else:
            tirar(filho, alvos)


def gravar(arvore, caminho):
    dados = ET.tostring(arvore.getroot(), encoding='unicode')
    # O ElementTree escreve ns0: quando o namespace padrao ja esta
    # registrado mas o documento traz prefixo; limpa o que sobra.
    dados = re.sub(r'\sxmlns:ns\d+="[^"]*"', '', dados)
    io.open(caminho, 'w', encoding='utf-8', newline='').write(dados)


for peca, ids in PARTES.items():
    texto = io.open(BASE + peca + '.svg', encoding='utf-8').read()

    corpo = ET.ElementTree(ET.fromstring(texto))
    alvos = achar(corpo.getroot(), ids)
    assert len(alvos) == len(ids), (peca, len(alvos))
    tirar(corpo.getroot(), alvos)
    gravar(corpo, BASE + peca + '-corpo.svg')

    movel = ET.ElementTree(ET.fromstring(texto))
    alvos = achar(movel.getroot(), ids)
    podar(movel.getroot(), alvos)
    gravar(movel, BASE + peca + '-movel.svg')

    print(f'{peca}: corpo + movel ({", ".join(ids)})')
