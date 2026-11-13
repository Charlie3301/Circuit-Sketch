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
Traffic light with three LEDs, on the ESP32.

What this demo shows:
- three digital outputs in sequence, each LED with its own resistor;
- the traffic light timing with delay() and the serial monitor following
  along.

Press F5 and follow the colors in the serial monitor.
''',
    'esp32-wifi-servidor': '''
Web server on the ESP32: the page turns the LEDs on and off.

What this demo shows:
- the board on WiFi serving a page with links, each on its own route
  (server.on) that lights an LED;
- the board's address and the bridge to this computer's browser.

Press F5 and open the address shown in the NETWORK panel.
''',
    'esp32-mqtt': '''
MQTT on the ESP32: publishes a count and obeys a command.

What this demo shows:
- PubSubClient connecting, subscribing to "class/led" and publishing to
  "class/sensor" every 3 s;
- without internet, CircuitSketch's own broker stands in for
  broker.hivemq.com.

Press F5 and, in the NETWORK panel, publish "on" or "off" to class/led.
''',
    'esp32-bluetooth': '''
Bluetooth control, on the ESP32.

What this demo shows:
- BluetoothSerial: the phone sends commands and the board answers;
- RGB LED, buzzer, DHT22 and LCD obeying what arrived.

Press F5, open the BLUETOOTH tab, connect to "ESP32-Room" and send
"help". A real phone connects too (see the panel).
''',
    'esp32-espnow': '''
ESP-NOW: two ESP32 boards talking without a router.

What this demo shows:
- the SENSOR board sends temperature, potentiometer and button every half
  second; the PANEL board shows it on the LCD, moves the servo and lights
  the LED;
- the way back: the panel's button lights the sensor's LED.

Press F5, turn the potentiometer and press both buttons.
''',
    'uno-analisador': '''
Logic analyzer on the Arduino Uno: five real signals, side by side.

What this demo shows:
- 976 Hz PWM, tone(), the servo's 50 Hz pulse, shiftOut and the serial
  port going out on TX -- each on its own analyzer channel;
- the analyzer's large window: zoom, pause, measurements and .vcd.

Press F5, select the analyzer and click "Open the analyzer".
''',
    'uno-botao': '''
Button and LED, on the Arduino Uno.

What this demo shows:
- the button with INPUT_PULLUP: released it reads HIGH, pressed it reads
  LOW -- that is why the test is "== LOW";
- the circuit built on the breadboard, with the LED and the resistor
  plugged in.

Press F5 and hold the button.
''',
    'mega-barra': '''
Scanning LED bar, on the Arduino Mega.

What this demo shows:
- ten outputs in a row in a for loop -- the Mega has pins to spare, no
  need to multiplex;
- the LED bar on the breadboard, sweeping back and forth.

Press F5.
''',
    'nano-potenciometro': '''
Potentiometer controlling an LED's brightness, on the Arduino Nano.

What this demo shows:
- analogRead (0 to 1023) turned into analogWrite (0 to 255) with map();
- PWM on pin 9, one of the Nano's PWM pins.

Press F5 and turn the potentiometer.
''',
    'rp2040-servo': '''
Servo driven by a potentiometer, on the Arduino Nano RP2040 Connect.

What this demo shows:
- Servo.h: the servo's angle follows the potentiometer reading;
- the RP2040 programmed with the Arduino core.

Press F5 and turn the potentiometer.
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
