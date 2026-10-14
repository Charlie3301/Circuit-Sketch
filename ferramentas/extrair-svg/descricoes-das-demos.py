"""python ferramentas/extrair-svg/descricoes-das-demos.py

A descricao das demos que vieram antes da Alpha 19 (Alpha 19).

As demos novas ja nascem com o texto que aparece embaixo do codigo -- o que
a demo mostra e o que fazer com ela. As antigas abriam com a caixa vazia.
Este script poe o texto nelas sem mexer em mais nada do arquivo: o circuito
e o codigo ficam exatamente como estao.
"""
import io
import json

PASTA = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/app/assets/demos/'

DESCRICOES = {
    'esp32-semaforo': '''
Semaforo com tres LEDs, na ESP32.

O que esta demo mostra:
- tres saidas digitais em sequencia, cada LED com o seu resistor;
- os tempos do semaforo com delay() e o monitor serial acompanhando.

Aperte F5 e acompanhe as cores no monitor serial.
''',
    'esp32-wifi-servidor': '''
Servidor web na ESP32: a pagina liga e desliga os LEDs.

O que esta demo mostra:
- a placa no WiFi servindo uma pagina com links, cada um numa rota
  (server.on) que acende um LED;
- o endereco da placa e a ponte para o navegador deste computador.

Aperte F5 e abra o endereco que aparece no painel REDE.
''',
    'esp32-mqtt': '''
MQTT na ESP32: publica uma contagem e obedece a um comando.

O que esta demo mostra:
- a PubSubClient conectando, assinando "aula/led" e publicando em
  "aula/sensor" a cada 3 s;
- sem internet, o broker do proprio CircuitSketch faz o papel do
  broker.hivemq.com.

Aperte F5 e, no painel REDE, publique "on" ou "off" em aula/led.
''',
    'esp32-bluetooth': '''
Controle por Bluetooth, na ESP32.

O que esta demo mostra:
- o BluetoothSerial: o celular manda comandos e a placa responde;
- LED RGB, buzzer, DHT22 e LCD obedecendo ao que chegou.

Aperte F5, abra a aba BLUETOOTH, conecte na "ESP32-Sala" e mande
"ajuda". Um celular de verdade tambem conecta (veja o painel).
''',
    'esp32-espnow': '''
ESP-NOW: duas ESP32 conversando sem roteador.

O que esta demo mostra:
- a placa SENSOR manda temperatura, potenciometro e botao a cada meio
  segundo; a PAINEL mostra no LCD, move o servo e acende o LED;
- o caminho de volta: o botao do painel acende o LED do sensor.

Aperte F5, gire o potenciometro e aperte os dois botoes.
''',
    'uno-analisador': '''
Analisador logico no Arduino Uno: cinco sinais de verdade, lado a lado.

O que esta demo mostra:
- PWM de 976 Hz, tone(), o pulso de 50 Hz do servo, o shiftOut e a
  serial saindo pelo TX -- cada um num canal do analisador;
- a janela grande do analisador: zoom, pausa, medida e .vcd.

Aperte F5, selecione o analisador e clique em "Abrir o analisador".
''',
    'uno-botao': '''
Botao e LED, no Arduino Uno.

O que esta demo mostra:
- o botao com INPUT_PULLUP: solto le HIGH, apertado le LOW -- por isso o
  teste e "== LOW";
- o circuito montado na protoboard, com o LED e o resistor espetados.

Aperte F5 e segure o botao.
''',
    'mega-barra': '''
Barra de LEDs em varredura, no Arduino Mega.

O que esta demo mostra:
- dez saidas seguidas num laco for -- o Mega tem pinos de sobra, sem
  precisar multiplexar;
- a barra de LEDs na protoboard, indo e voltando.

Aperte F5.
''',
    'nano-potenciometro': '''
Potenciometro controlando o brilho de um LED, no Arduino Nano.

O que esta demo mostra:
- analogRead (0 a 1023) virando analogWrite (0 a 255) com map();
- PWM no pino 9, que e um dos que tem PWM no Nano.

Aperte F5 e gire o potenciometro.
''',
    'rp2040-servo': '''
Servo comandado por potenciometro, na Arduino Nano RP2040 Connect.

O que esta demo mostra:
- a Servo.h: o angulo do servo segue a leitura do potenciometro;
- o RP2040 programado pelo core da Arduino.

Aperte F5 e gire o potenciometro.
''',
}

for recurso, texto in DESCRICOES.items():
    caminho = PASTA + recurso + '.sktch'
    projeto = json.loads(io.open(caminho, encoding='utf-8').read())

    # A descricao entra logo depois do "gerado", como nas demos novas.
    novo = {}
    for chave, valor in projeto.items():
        if chave == 'descricao':
            continue
        novo[chave] = valor
        if chave == 'gerado':
            novo['descricao'] = texto.strip('\n').split('\n')
    if 'descricao' not in novo:
        novo['descricao'] = texto.strip('\n').split('\n')

    io.open(caminho, 'w', encoding='utf-8', newline='').write(
        json.dumps(novo, ensure_ascii=False, indent=1))
    print(f'{recurso}.sktch')
