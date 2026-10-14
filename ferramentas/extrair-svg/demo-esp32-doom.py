"""python ferramentas/extrair-svg/demo-esp32-doom.py

ESP32-DOOM (Alpha 19): o DOOM de verdade -- o motor PrBoom 2.5.0 do projeto
ESP32-DOOM, em C -- numa ESP32-S3 com PSRAM. A biblioteca mora em
bibliotecas/ESP32-DOOM (GPL, vai com o codigo-fonte) e roda no modo nativo
do simulador.

O circuito e o da BASE ZERO (display, cartao, sete botoes e a chave nos
mesmos pinos), com o buzzer trocado por um amplificador I2S MAX98357A e um
alto-falante. O WAD NAO vem junto: e da id Software. O cartao leva so o
LEIAME.TXT, que explica como pôr o DOOM1.WAD do computador.

O sketch mora em esp32-doom/esp32-doom.ino, como arquivo de verdade: compila
na Arduino IDE com a biblioteca instalada.
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demos_comum import *   # noqa: E402,F403

AQUI = os.path.dirname(os.path.abspath(__file__))
JOGO = os.path.join(AQUI, 'esp32-doom')


def ler(nome):
    return io.open(os.path.join(JOGO, nome), encoding='utf-8').read()


sketch = ler('esp32-doom.ino')

# Os pinos do sketch e os do circuito tem que ser os mesmos.
for trecho in ['doom_barramento(11, 13, 12)', 'doom_tela(10, 9, 8, 7)', 'doom_cartao(14)',
               'doom_botoes(4, 5, 6, 15, 16, 17, 18)', 'doom_chave_correr(1)', 'doom_som(40, 41, 42)']:
    assert trecho in sketch, trecho

# ---------------------------------------------------------------------
#  O circuito: o da BASE ZERO, com som de verdade
# ---------------------------------------------------------------------
BX, BY = 400, 60                      # a ESP32-S3 (96 x 214)
c = Circuito('placa', 'esp32-s3-devkit', BX, BY)

# O display deitado: o spi_lcd.c manda o MADCTL 0x28 (o setRotation(1) da
# Adafruit), e a peca vai girada 270 graus, como na BASE ZERO.
VX, VY = 49, BY + 45
c.peca('tela', 'ili9341', VX + 58.5, VY - 58.5, Rotacao=270, Nome='display 320x240 (ILI9341)')
for pino, gpio, cor in [('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO),
                        ('CS', '10', LARANJA), ('RST', '8', BRANCO), ('D/C', '9', ROXO),
                        ('MOSI', '11', AZUL), ('SCK', '12', AMARELO), ('LED', '7', VERDE),
                        ('MISO', '13', CINZA)]:
    c.ligar('tela', pino, 'placa', gpio, cor)

# O cartao divide o SPI com o display. O WAD entra pela janela do cartao.
c.peca('cartao', 'microsd-card', 262, BY + 245, Nome='cartão: DOOM1.WAD',
       Arquivos={'LEIAME.TXT': ler('LEIAME.TXT')})
for pino, gpio, cor in [('VCC', '3V3', VERMELHO), ('GND', 'GND', PRETO),
                        ('CS', '14', LARANJA), ('SCK', '12', AMARELO),
                        ('DI', '11', AZUL), ('DO', '13', CINZA)]:
    c.ligar('cartao', pino, 'placa', gpio, cor)


def botao(id, pino, gnd, x, y, nome, teclas, lado):
    """Botao para o GND, com as pernas do lado que olha para a placa."""
    c.peca(id, 'pushbutton', x, y, Nome=nome, Teclas={'apertar': teclas})
    c.ligar('placa', pino, id, '1.' + lado, AZUL)
    c.ligar(id, '2.' + lado, 'placa', gnd, PRETO)


# As teclas de sempre do DOOM no PC: setas, Ctrl atira, Espaco usa, Esc e o
# menu. O "atirar" tambem e o OK dos menus (o Enter).
botao('cima', '4', 'GND', 112, BY + 262, '↑ andar', 't:Up|g:Cima', 'r')
botao('esquerda', '6', 'GND', 40, BY + 308, '← virar', 't:Left|g:Esquerda', 'r')
botao('direita', '15', 'GND', 184, BY + 308, '→ virar', 't:Right|g:Direita', 'r')
botao('baixo', '5', 'GND', 112, BY + 354, '↓ voltar', 't:Down|g:Baixo', 'r')
botao('menu', '16', 'GND', 262, BY + 360, 'MENU (Esc)', 't:Escape|g:Start', 'r')
botao('atirar', '17', 'GND.2', 540, BY + 20, 'A: atirar / OK (Ctrl)', 't:LeftCtrl|g:A', 'l')
botao('usar', '18', 'GND.2', 540, BY + 90, 'B: usar (Espaço)', 't:Space|g:B', 'l')

# O som: o I2S da placa no MAX98357A, e o alto-falante no borne dele. O
# amplificador se alimenta do 5V (o VIN dele aceita de 2,5 a 5,5 V).
c.peca('amplificador', 'max98357a', 548, BY + 158, Nome='amplificador I2S')
for pino, gpio, cor in [('BCLK', '40', AMARELO), ('LRC', '41', LARANJA), ('DIN', '42', AZUL),
                        ('VIN', '5V', VERMELHO), ('GND', 'GND.2', PRETO)]:
    c.ligar('amplificador', pino, 'placa', gpio, cor)
c.peca('falante', 'alto-falante', 640, BY + 120, Nome='alto-falante 8 Ω')
c.ligar('amplificador', 'OUT+', 'falante', '+', VERMELHO)
c.ligar('amplificador', 'OUT-', 'falante', '-', PRETO)

# A chave do correr: o comum no GPIO 1 e um lado no GND. Comeca desligada
# (o pino fica no pull-up, em HIGH); ligada, o jogo corre sem o Shift.
c.peca('chave', 'slide-switch', 330, BY - 44, Giro=0, Nome='correr (Caps Lock)',
       Teclas={'alternar': 't:CapsLock|g:Back'})
c.ligar('chave', '2', 'placa', '1', VERDE)
c.ligar('chave', '1', 'placa', 'GND', PRETO)

gravar('esp32s3-doom', sketch, c, '''
ESP32-DOOM: o DOOM de verdade (motor PrBoom 2.5.0) numa ESP32-S3.

O jogo e a biblioteca ESP32-DOOM, em C, com o codigo junto (GPL). No
simulador ela roda no modo nativo: o C e compilado de verdade, com o
ESP-IDF do simulador por baixo -- tarefas do FreeRTOS, o SPI do display e
do cartao, o I2S do som e o servidor HTTP do ESP-IDF.

ANTES DE RODAR: o cartao precisa do DOOM1.WAD (o shareware serve). Clique
duas vezes no cartao e use "Carregar do computador". O WAD nao vem junto:
e da id Software. DOOM e marca da id Software.

Aperte F5 e clique no circuito. Setas andam, Ctrl atira (e e o OK dos
menus), Espaco abre as portas, Esc abre o menu e o Caps Lock liga o
correr. Um controle de video game tambem vale. A tela tambem vai para o
navegador: o painel Rede mostra o endereco.

Na placa de verdade: ESP32-S3 N8R8, PSRAM "OPI PSRAM", particao "Huge
APP", e a biblioteca de bibliotecas/ESP32-DOOM na pasta libraries da
Arduino IDE.
''')
