r"""
Prova do monitor serial: simulador <-> Arduino IDE.

Faz o papel da IDE. Abre o monitor.ps1, fala o protocolo do pluggable
monitor (HELLO / DESCRIBE / OPEN / CLOSE / QUIT), poe um socket TCP de
pe para receber o cano de dados e confere se o que o CircuitSketch
escreve em %APPDATA%\CircuitSketch\serial.txt chega la.

Fica fora do autoteste do programa porque nao e codigo nosso rodando:
sao dois processos de PowerShell e um socket, e isso precisa ser
provado de fora, do jeito que a IDE faz.

    python ferramentas/provar-monitor/provar-monitor.py

Cuidado: o teste sobrescreve o serial.txt. Feche o CircuitSketch antes,
ou rode com ele parado.
"""
import json
import os
import socket
import subprocess
import threading
import time

BAT = r'C:\Users\seraf\OneDrive\Desktop\SimuladorESP32\plataforma\tools\monitor.bat'
ARQ = os.path.join(os.environ['APPDATA'], 'CircuitSketch', 'serial.txt')
ENTRADA = os.path.join(os.environ['APPDATA'], 'CircuitSketch', 'entrada.txt')

os.makedirs(os.path.dirname(ARQ), exist_ok=True)
with open(ARQ, 'w', encoding='utf-8') as f:
    f.write('estado 0\nestado 1\n')
with open(ENTRADA, 'w', encoding='utf-8') as f:
    pass

servidor = socket.socket()
servidor.bind(('127.0.0.1', 0))
servidor.listen(1)
porta = servidor.getsockname()[1]

recebido = []


def aceitar():
    try:
        conexao, _ = servidor.accept()
        conexao.settimeout(8)
        fim = time.time() + 8
        mandou = False
        while time.time() < fim:
            dados = conexao.recv(4096)
            if not dados:
                break
            recebido.append(dados.decode('utf-8', 'replace'))
            # O caminho de volta: a pessoa digita no monitor da IDE.
            if not mandou:
                conexao.sendall('liga led\n'.encode('utf-8'))
                mandou = True
        conexao.close()
    except Exception:
        # Fechar o monitor derruba a conexao; e o esperado.
        pass


t = threading.Thread(target=aceitar, daemon=True)
t.start()

proc = subprocess.Popen(BAT, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                        text=True, encoding='utf-8', bufsize=1)


def mandar(cmd):
    proc.stdin.write(cmd + '\n')
    proc.stdin.flush()
    linha = proc.stdout.readline()
    print(f'  {cmd:<40} -> {linha.strip()[:150]}')
    return json.loads(linha) if linha.strip() else {}


print('protocolo:')
r1 = mandar('HELLO 1 "arduino-ide 2.3.2"')
r2 = mandar('DESCRIBE')
r3 = mandar(f'OPEN 127.0.0.1:{porta} circuitsketch')

# Espera o cano SUBIR antes de mexer no arquivo.
#
# Ele e um PowerShell novo: leva ~2 s para nascer e conectar. Sem esta
# espera o teste ja tinha trocado o conteudo quando ele chegou, e o
# resultado dependia de quao ocupada estava a maquina -- passava sozinho
# e falhava no meio de uma compilacao, que e o pior tipo de teste.
fim = time.time() + 20
while not recebido and time.time() < fim:
    time.sleep(0.2)

if not recebido:
    print('  (o cano nao mandou nada em 20s)')

# O simulador continua escrevendo enquanto o monitor esta aberto.
time.sleep(1.0)
with open(ARQ, 'a', encoding='utf-8') as f:
    f.write('estado 2\n')
time.sleep(1.0)

# Rodar de novo zera o arquivo: o monitor deve perceber e recomecar.
with open(ARQ, 'w', encoding='utf-8') as f:
    f.write('depois do reinicio\n')
time.sleep(1.5)

r4 = mandar('CLOSE')
r5 = mandar('QUIT')
try:
    proc.wait(timeout=6)
except subprocess.TimeoutExpired:
    proc.kill()
t.join(timeout=10)

texto = ''.join(recebido)
print()
print('recebido no socket:')
for l in texto.splitlines():
    print('   |', l)

falhas = []


def conferir(nome, ok, detalhe=''):
    print(('  OK    ' if ok else '  FALHA ') + nome + ('' if ok else '  ' + detalhe))
    if not ok:
        falhas.append(nome)


print()
conferir('HELLO responde protocolVersion 1',
         r1.get('eventType') == 'hello' and r1.get('protocolVersion') == 1, str(r1))
conferir('DESCRIBE traz os parametros da porta',
         r3 is not None and r2.get('eventType') == 'describe'
         and 'baudrate' in r2.get('port_description', {})
                              .get('configuration_parameters', {}), str(r2))
conferir('OPEN aceita', r3.get('eventType') == 'open' and not r3.get('error'), str(r3))
conferir('o que ja estava no arquivo chega',
         'estado 0' in texto and 'estado 1' in texto, repr(texto[:200]))
conferir('linha escrita com o monitor aberto chega', 'estado 2' in texto, repr(texto))
conferir('arquivo zerado e visto como reinicio',
         'board restarted' in texto and 'depois do reinicio' in texto, repr(texto))
with open(ENTRADA, encoding='utf-8') as f:
    digitado = f.read()
conferir('o que se digita no monitor da IDE chega em entrada.txt', digitado == 'liga led\n', repr(digitado))
conferir('CLOSE aceita', r4.get('eventType') == 'close' and not r4.get('error'), str(r4))
conferir('QUIT aceita', r5.get('eventType') == 'quit', str(r5))

print()
print('FALHOU: ' + ', '.join(falhas) if falhas else 'tudo passou')
