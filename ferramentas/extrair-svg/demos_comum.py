"""O que os geradores de demo dividem: cores de fio, fios e o gravador.

Cada peca de circuito entra por aqui com as MESMAS regras de bancada que o
autoteste confere: LED sempre com resistor, atuador alimentado pelo trilho
e nao por um pino, terra de volta para a placa.
"""
import io
import json
import os

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PASTA = BASE + 'app/assets/demos/'

PRETO = '#1A1A1A'
VERMELHO = '#D6202A'
VERDE = '#00A651'
AZUL = '#1F6FEB'
AMARELO = '#F2C300'
LARANJA = '#F28C28'
ROXO = '#8E44AD'
BRANCO = '#E8E8E8'
CINZA = '#8A8A8A'
MARROM = '#8B5A2B'


def fio(de, pino_de, para, pino_para, cor):
    return {'DePeca': de, 'DePino': pino_de, 'ParaPeca': para, 'ParaPino': pino_para,
            'Cor': cor, 'Estilo': 'solto'}


class Circuito:
    """As pecas e os fios de uma demo, com atalhos para o que se repete."""

    def __init__(self, placa, tipo, x=60, y=60):
        self.placa = placa
        self.pecas = [{'Id': placa, 'Tipo': tipo, 'X': x, 'Y': y}]
        self.fios = []

    def peca(self, id, tipo, x, y, **extra):
        self.pecas.append({'Id': id, 'Tipo': tipo, 'X': x, 'Y': y, **extra})
        return id

    def ligar(self, de, pino_de, para, pino_para, cor):
        self.fios.append(fio(de, pino_de, para, pino_para, cor))

    def led(self, id, pino, gnd, cor_led, x, y, cor_fio, ohms=220, placa=None):
        """LED que acende com HIGH: pino -> resistor -> anodo; catodo -> GND."""
        placa = placa or self.placa
        self.peca(id, 'led', x, y, Cor=cor_led)
        self.peca('r_' + id, 'resistor', x - 72, y + 46, Ohms=ohms)
        self.ligar(placa, pino, 'r_' + id, '1', cor_fio)
        self.ligar('r_' + id, '2', id, 'A', cor_fio)
        self.ligar(id, 'C', placa, gnd, PRETO)

    def led_ativo_baixo(self, id, pino, vcc, cor_led, x, y, cor_fio, ohms=330, placa=None):
        """LED que acende com LOW: VCC -> resistor -> anodo; catodo -> pino."""
        placa = placa or self.placa
        self.peca(id, 'led', x, y, Cor=cor_led)
        self.peca('r_' + id, 'resistor', x - 72, y + 46, Ohms=ohms)
        self.ligar(placa, vcc, 'r_' + id, '1', VERMELHO)
        self.ligar('r_' + id, '2', id, 'A', VERMELHO)
        self.ligar(id, 'C', placa, pino, cor_fio)

    def botao(self, id, pino, gnd, x, y, cor_fio=AZUL, placa=None):
        """Botao para o GND: o sketch usa INPUT_PULLUP e le LOW apertado."""
        placa = placa or self.placa
        self.peca(id, 'pushbutton', x, y)
        self.ligar(placa, pino, id, '1.l', cor_fio)
        self.ligar(id, '2.l', placa, gnd, PRETO)

    def buzzer(self, id, pino, gnd, x, y, placa=None):
        placa = placa or self.placa
        self.peca(id, 'buzzer', x, y)
        # o 2 e o + (a perna comprida, o fio vermelho do Wokwi), o 1 e o -
        self.ligar(placa, pino, id, '2', LARANJA)
        self.ligar(id, '1', placa, gnd, PRETO)


def gravar(recurso, codigo, circuito, descricao, codigos_por_placa=None):
    """Grava o .sktch. Com mais de uma placa, cada uma leva o seu codigo."""
    pecas = circuito.pecas
    if codigos_por_placa:
        for p in pecas:
            if p['Id'] in codigos_por_placa:
                p['Codigo'] = codigos_por_placa[p['Id']].strip('\n').split('\n')

    projeto = {
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'demo do CircuitSketch',
        'descricao': descricao.strip('\n').split('\n'),
        'codigo': codigo.strip('\n').split('\n'),
        'circuito': {'Pecas': pecas, 'Fios': circuito.fios},
    }
    io.open(PASTA + recurso + '.sktch', 'w', encoding='utf-8', newline='').write(
        json.dumps(projeto, ensure_ascii=False, indent=1))
    print(f'{recurso}.sktch')


def apagar(recursos):
    """Tira do programa as demos que foram substituidas."""
    for recurso in recursos:
        caminho = PASTA + recurso + '.sktch'
        if os.path.exists(caminho):
            os.remove(caminho)
            print(f'apagada: {recurso}.sktch')


# A temperatura do DHT22 vai de -40 a 80 C, e a do DHT11 de 0 a 50; o
# controle da peca vai de 0 a 1.
def giro_dht22(celsius):
    return (celsius + 40) / 120


def giro_dht11(celsius):
    return celsius / 50


# O NTC vai de -24 a 80 C; o ultrassom, de 0 a 400 cm; a balanca, de 0 a 5 kg.
def giro_ntc(celsius):
    return (celsius + 24) / 104


def giro_distancia(cm):
    return cm / 400


def giro_peso(gramas):
    return gramas / 5000


def giro_lux(lux):
    """O modulo de LDR anda em decadas: 0,1 lux no 0 e 100 mil no 1."""
    import math
    return (math.log10(lux) + 1) / 6


# A umidade do DHT e 30 % + 50 % x Giro2.
def giro2_umidade(porcento):
    return (porcento - 30) / 50
