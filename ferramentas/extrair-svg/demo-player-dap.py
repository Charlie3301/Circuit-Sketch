"""python ferramentas/extrair-svg/demo-player-dap.py

PLAYER DE MUSICA (DAP): um tocador de musica no estilo do Surfans F20 numa
ESP32-S3 com PSRAM, feito pela biblioteca PlayerDAP (bibliotecas/PlayerDAP,
em C, que roda no modo nativo do simulador).

  - tela ILI9341 em pe (240x320) e o cartao microSD no mesmo SPI;
  - um pendrive no USB OTG da S3 (D- no GPIO19, D+ no GPIO20), lido como
    um segundo cartao: clicar nele tira e poe de volta;
  - a roda com clique (KY-040) e cinco botoes: volume +, volume -,
    voltar/repetir, MODE e pausa;
  - a chave de liga/desliga (a placa dorme e acorda por ela);
  - a bateria de litio medida por um divisor de 100k + 100k no ADC;
  - o som pelo I2S num MAX98357A e num alto-falante.

As musicas NAO vem junto: o cartao leva so o LEIAME.TXT, que explica como
usar uma pasta de musica do computador como cartao.

O sketch mora em player-dap/player-dap.ino, como arquivo de verdade:
compila na Arduino IDE com a biblioteca instalada.
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demos_comum import *   # noqa: E402,F403

AQUI = os.path.dirname(os.path.abspath(__file__))
sketch = io.open(os.path.join(AQUI, 'player-dap', 'player-dap.ino'), encoding='utf-8').read()

# Os pinos do sketch e os do circuito tem que ser os mesmos.
for trecho in ['dap_barramento(11, 13, 12)', 'dap_tela(10, 9, 8, 7)', 'dap_cartao(14)',
               'dap_som(40, 41, 42, 96000)', 'dap_botoes(17, 18, 21, 38, 39)', 'dap_roda(4, 5, 6)',
               'POWER = 1', 'BATTERY = 2', 'GPIO_NUM_1']:
    assert trecho in sketch, trecho

LEIAME = '''MUSIC PLAYER: where the music lives

This card has no music yet. Double-click it and use "Use a folder from
the computer..." to choose your music folder: everything in it, with the
subfolders, shows up on the card. Nothing is copied, and the folder never
changes: what the player saves (playlists, favorites, where it stopped)
stays on the project's card.

The flash drive also takes a folder. Click the flash drive to remove it
and plug it back with the player on: the library is read again.

Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus, ALAC (.m4a), WavPack, DSF and
DFF. The playlists are the .m3u and .m3u8 files in the /Playlists folder
(the ones from the computer work).
'''

# ---------------------------------------------------------------------
#  O circuito
# ---------------------------------------------------------------------
BX, BY = 520, 160                     # a ESP32-S3 (96 x 214)
c = Circuito('placa', 'esp32-s3-devkit', BX, BY)
# No simulador o player fica na USB ("cabo ligado"): a bateria do circuito e
# so medida pelo divisor -- na placa de verdade ela alimenta por um carregador.
c.pecas[0]['Usb'] = True

# A tela em pe: o player manda o MADCTL 0x48 (retrato), e a peca fica sem giro.
c.peca('tela', 'ili9341', 40, 110, Nome='display 240x320 (ILI9341)')
for pino, gpio, cor in [('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO),
                        ('CS', '10', LARANJA), ('RST', '8', BRANCO), ('D/C', '9', ROXO),
                        ('MOSI', '11', AZUL), ('SCK', '12', AMARELO), ('LED', '7', VERDE),
                        ('MISO', '13', CINZA)]:
    c.ligar('tela', pino, 'placa', gpio, cor)

# O cartao divide o SPI com a tela.
c.peca('cartao', 'microsd-card', 380, 420, Nome='microSD card', Arquivos={'README.TXT': LEIAME})
for pino, gpio, cor in [('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO),
                        ('CS', '14', LARANJA), ('SCK', '12', AMARELO),
                        ('DI', '11', AZUL), ('DO', '13', CINZA)]:
    c.ligar('cartao', pino, 'placa', gpio, cor)

# A roda: as setas do teclado giram um clique, o Enter e o clique dela.
c.peca('roda', 'ky-040', 310, 230, Nome='wheel (KY-040)',
       Teclas={'mais': 't:Down|g:Baixo', 'menos': 't:Up|g:Cima', 'apertar': 't:Return|g:A'})
for pino, gpio, cor in [('CLK', '4', AZUL), ('DT', '5', VERDE), ('SW', '6', LARANJA),
                        ('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO)]:
    c.ligar('roda', pino, 'placa', gpio, cor)

# A chave de liga/desliga: o comum no GPIO 1, o lado da direita (3) no GND,
# e o resistor de 10k para o 3V3 (o pull-up que vale tambem com a placa
# dormindo). Alavanca a direita (como a do Wokwi: o 2 no 3) = ligada.
c.peca('chave', 'slide-switch', 420, 40, Giro=1, Nome='on / off',
       Teclas={'alternar': 't:F2'})
c.ligar('chave', '2', 'placa', '1', VERDE)
c.ligar('chave', '3', 'placa', 'GND', PRETO)
c.peca('r_chave', 'resistor', 430, 120, Ohms=10000)
c.ligar('r_chave', '1', 'placa', '3V3', VERMELHO)
c.ligar('r_chave', '2', 'placa', '1', VERDE)

# A bateria de litio, medida pela metade: 100k + 100k no GPIO 2 (ADC1).
c.peca('bateria', 'bateria', 160, 10, Volts=3.9, MiliAmperes=2000, MiliAmperesHora=2000,
       Nome='lithium battery (3.7 V)')
c.peca('r_bat1', 'resistor', 250, 110, Ohms=100000)
c.peca('r_bat2', 'resistor', 330, 150, Ohms=100000)
c.ligar('bateria', '+', 'r_bat1', '1', VERMELHO)
c.ligar('r_bat1', '2', 'placa', '2', AMARELO)
c.ligar('r_bat2', '1', 'placa', '2', AMARELO)
c.ligar('r_bat2', '2', 'placa', 'GND', PRETO)
c.ligar('bateria', '-', 'placa', 'GND', PRETO)

# O pendrive no USB OTG da S3.
c.peca('pendrive', 'pendrive-usb', 660, 204, Nome='flash drive (USB)', Teclas={'alternar': 't:F3'})
for pino, gpio, cor in [('VBUS', '5V', VERMELHO), ('D-', '19', BRANCO),
                        ('D+', '20', VERDE), ('GND', 'GND.2', PRETO)]:
    c.ligar('pendrive', pino, 'placa', gpio, cor)


def botao(id, pino, y, nome, teclas):
    """Botao para o GND, com as pernas da esquerda olhando para a placa."""
    c.peca(id, 'pushbutton', 680, y, Nome=nome, Teclas={'apertar': teclas})
    c.ligar('placa', pino, id, '1.l', AZUL)
    c.ligar(id, '2.l', 'placa', 'GND.2', PRETO)


botao('vol_mais', '17', 40, 'volume +', 't:PageUp|g:RB')
botao('vol_menos', '18', 110, 'volume −', 't:PageDown|g:LB')
botao('voltar', '21', 290, 'back / repeat', 't:Back|g:B')
botao('modo', '38', 350, 'MODE', 't:M|g:Y')
botao('pausa', '39', 410, 'pause ▶❚❚', 't:Space|g:Start')

# O som: o I2S no MAX98357A e o alto-falante no borne dele.
c.peca('amplificador', 'max98357a', 560, 500, Nome='I2S amplifier')
for pino, gpio, cor in [('BCLK', '40', AMARELO), ('LRC', '41', LARANJA), ('DIN', '42', AZUL),
                        ('VIN', '5V', VERMELHO), ('GND', 'GND.2', PRETO)]:
    c.ligar('amplificador', pino, 'placa', gpio, cor)
c.peca('falante', 'alto-falante', 680, 470, Nome='8 Ω speaker')
c.ligar('amplificador', 'OUT+', 'falante', '+', VERMELHO)
c.ligar('amplificador', 'OUT-', 'falante', '-', PRETO)

gravar('esp32s3-player-dap', sketch, c, '''
MUSIC PLAYER (DAP): a player in the style of the Surfans F20, on an
ESP32-S3.

The PlayerDAP library, in C, runs in native mode: it plays MP3, FLAC, WAV,
AIFF, OGG Vorbis, Opus, ALAC, WavPack and DSD, reads the tags and sorts by
artist, album, genre, album artist, year, composer and folder. It makes
playlists, keeps favorites, has a five-band equalizer and five play modes
(all, repeat once, repeat always, shuffle, shuffle the list).

BEFORE RUNNING: the music does not come with it. Double-click the card and
use "Use a folder from the computer..." to choose your music folder
(nothing is copied). The flash drive also takes a folder; click it to
remove it and plug it back with the player on.

Press F5 and click the circuit. The arrows go up and down (the wheel),
Enter is OK (the wheel click), Backspace goes back (held: repeat), M is
MODE (held: favorite), Space pauses, Page Up and Page Down change the
volume. F2 is the power switch and F3 removes and plugs in the flash
drive. Change the battery voltage to watch the charge change (below 3.3 V
the player turns off).

On the real board: ESP32-S3 N8R8 or N16R8, "OPI PSRAM", a partition with
3 MB of app, and the library from bibliotecas/PlayerDAP in the Arduino
IDE's libraries folder. The flash drive needs Espressif's usb_host_msc
component and 5 V on VBUS; the battery powers the board through a charger
with a regulator.
''')
