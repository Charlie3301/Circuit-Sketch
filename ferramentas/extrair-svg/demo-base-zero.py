"""python ferramentas/extrair-svg/demo-base-zero.py

BASE ZERO (Alpha 19, item 11): tiro em primeira pessoa no estilo do DOOM
(1993), feito com raycasting, numa ESP32-S3 com display ILI9341, sete
botoes, cartao microSD com as fases, buzzer e uma chave liga/desliga de
verdade -- desligada, a placa entra em sono profundo.

O sketch e as fases moram em base-zero/, como arquivos de verdade: o .ino
compila na Arduino IDE sem mudar nada, e as fases vao para o cartao da
demo. Este gerador confere cada fase (16x16, borda fechada, uma largada,
saida alcancavel) antes de gravar o .sktch.

O nome e outro de proposito: DOOM e marca registrada. A demo e "no estilo
do DOOM", e e assim que ela se apresenta.
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demos_comum import *   # noqa: E402,F403

AQUI = os.path.dirname(os.path.abspath(__file__))
JOGO = os.path.join(AQUI, 'base-zero')


def ler(nome):
    return io.open(os.path.join(JOGO, nome), encoding='utf-8').read()


# ---------------------------------------------------------------------
#  As fases
# ---------------------------------------------------------------------

BORDA = set('#BMX')          # porta na beira levaria para fora do mapa
CAMINHO = set('.PRVAD')      # chao, largada, robo, item e porta (o B abre)


def conferir_fase(nome, texto):
    linhas = [linha.rstrip('\r') for linha in texto.split('\n') if linha.strip()]
    titulo, mapa = linhas[0], linhas[1:]
    assert len(mapa) == 16, (nome, 'linhas', len(mapa))
    for y, linha in enumerate(mapa):
        assert len(linha) == 16, (nome, 'linha', y, len(linha), linha)
        for x, c in enumerate(linha):
            assert c in '#BMDX.PRVA', (nome, 'letra', x, y, c)
            if x in (0, 15) or y in (0, 15):
                assert c in BORDA, (nome, 'borda aberta', x, y, c)

    largadas = [(x, y) for y, l in enumerate(mapa) for x, c in enumerate(l) if c == 'P']
    saidas = [(x, y) for y, l in enumerate(mapa) for x, c in enumerate(l) if c == 'X']
    assert len(largadas) == 1, (nome, 'largadas', largadas)
    assert saidas, (nome, 'sem saida')

    vistos = {largadas[0]}
    fila = [largadas[0]]
    while fila:
        x, y = fila.pop()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < 16 and 0 <= ny < 16 and (nx, ny) not in vistos and mapa[ny][nx] in CAMINHO:
                vistos.add((nx, ny))
                fila.append((nx, ny))
    alcanca = any((sx + dx, sy + dy) in vistos
                  for sx, sy in saidas for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
    assert alcanca, (nome, 'a saida nao e alcancavel da largada')

    robos = sum(linha.count('R') for linha in mapa)
    itens = sum(linha.count('V') + linha.count('A') for linha in mapa)
    print(f'  {nome}: "{titulo}" -- {robos} robos, {itens} itens, saida alcancavel')
    return '\n'.join([titulo] + mapa) + '\n'


fases = {nome: conferir_fase(nome, ler(nome)) for nome in ('E1M1.TXT', 'E1M2.TXT')}

# A fase que esta no codigo tem que ser a mesma do cartao: sem cartao, o
# jogo e o mesmo.
sketch = ler('base-zero.ino')
interna = [linha.split('"')[1] for linha in sketch.split('const char* FASE_INTERNA[TAM] = {')[1]
           .split('};')[0].strip().split('\n')]
assert interna == fases['E1M1.TXT'].split('\n')[1:17], 'a fase do codigo e a E1M1.TXT estao diferentes'

# ---------------------------------------------------------------------
#  O circuito
# ---------------------------------------------------------------------

# O desenho: a placa no meio; o display na esquerda, com o conector virado
# para os GPIO 7 a 13 dela; o cartao embaixo, perto do 11 ao 14; o
# direcional e o START na esquerda, o A e o B na direita, como num console.
BX, BY = 400, 60                      # a ESP32-S3 (96 x 214)
c = Circuito('placa', 'esp32-s3-devkit', BX, BY)

# O display deitado: setRotation(1) no sketch e a peca girada 270 graus.
# O giro e em volta do centro (176 x 293), entao o canto de cima da peca
# girada fica em (VX, VY) com X = VX + 58,5 e Y = VY - 58,5.
VX, VY = 49, BY + 45
c.peca('tela', 'ili9341', VX + 58.5, VY - 58.5, Rotacao=270, Nome='display 320x240 (ILI9341)')
for pino, gpio, cor in [('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO),
                        ('CS', '10', LARANJA), ('RST', '8', BRANCO), ('D/C', '9', ROXO),
                        ('MOSI', '11', AZUL), ('SCK', '12', AMARELO), ('LED', '7', VERDE),
                        ('MISO', '13', CINZA)]:
    c.ligar('tela', pino, 'placa', gpio, cor)

# O cartao divide o SPI com o display; o CS e so dele.
c.peca('cartao', 'microsd-card', 262, BY + 245, Nome='fases do jogo',
       Arquivos={**fases, 'LEIAME.TXT': ler('LEIAME.TXT')})
for pino, gpio, cor in [('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO),
                        ('CS', '14', LARANJA), ('SCK', '12', AMARELO),
                        ('DI', '11', AZUL), ('DO', '13', CINZA)]:
    c.ligar('cartao', pino, 'placa', gpio, cor)


def botao(id, pino, gnd, x, y, nome, teclas, lado):
    """Botao para o GND, com as pernas do lado que olha para a placa."""
    c.peca(id, 'pushbutton', x, y, Nome=nome, Teclas={'apertar': teclas})
    c.ligar('placa', pino, id, '1.' + lado, AZUL)
    c.ligar(id, '2.' + lado, 'placa', gnd, PRETO)


# Cada botao vale pela tecla e pelo controle de video game.
botao('cima', '4', 'GND', 112, BY + 262, '↑ andar', 't:Up|g:Cima', 'r')
botao('esquerda', '6', 'GND', 40, BY + 308, '← virar', 't:Left|g:Esquerda', 'r')
botao('direita', '15', 'GND', 184, BY + 308, '→ virar', 't:Right|g:Direita', 'r')
botao('baixo', '5', 'GND', 112, BY + 354, '↓ voltar', 't:Down|g:Baixo', 'r')
botao('start', '16', 'GND', 262, BY + 360, 'START (Enter)', 't:Return|g:Start', 'r')
botao('atirar', '17', 'GND.2', 540, BY + 20, 'A: atirar (Espaço)', 't:Space|g:A', 'l')
botao('abrir', '18', 'GND.2', 540, BY + 90, 'B: abrir (E)', 't:E|g:B', 'l')

c.buzzer('buzzer', '21', 'GND.2', 540, BY + 150)
c.pecas[-1]['Nome'] = 'som'

# A chave liga/desliga: o comum no GPIO 1 (que o RTC enxerga, e e por ele
# que o ext0 acorda a placa) e um lado no GND. Comeca ligada.
c.peca('chave', 'slide-switch', 330, BY - 44, Giro=1, Nome='liga/desliga (L)',
       Teclas={'alternar': 't:L|g:Back'})
c.ligar('chave', '2', 'placa', '1', VERDE)
c.ligar('chave', '3', 'placa', 'GND', PRETO)   # o lado da direita: alavanca a direita (o 2 no 3, como no Wokwi) = ligada

gravar('esp32s3-base-zero', sketch, c, '''
BASE ZERO: tiro em primeira pessoa no estilo do DOOM (1993), na ESP32-S3.

O que esta demo mostra:
- raycasting: 160 raios por quadro, o DDA andando no mapa quadrado a
  quadrado, e o z-buffer que esconde os robos atras das paredes;
- o display ILI9341 com a Adafruit_GFX: so as colunas que mudaram vao
  pelo SPI, que e o que da 30 quadros por segundo na placa de verdade;
- as fases no cartao microSD (/E1M1.TXT e /E1M2.TXT): mude as paredes
  pela peca do cartao e rode de novo -- o LEIAME.TXT explica as letras;
- a chave liga/desliga de verdade: desligada, a ESP32 apaga a tela e
  dorme (sono profundo); ligada, o ext0 acorda a placa, e a memoria RTC
  devolve a fase e os pontos.

Aperte F5, clique no circuito e aperte Enter (START). As setas andam,
Espaco atira, E abre as portas e o painel verde da saida, L liga e
desliga. Um controle de video game tambem vale.
''')

print('\n--- para o Recentes.cs ---')
print('        new("esp32s3-base-zero", "BASE ZERO: jogo 3D no estilo DOOM", "ESP32-S3 DevKit"),')
