"""python ferramentas/extrair-svg/demos-linguagens.py

Uma demo por linguagem (Alpha 19).

Na Alpha 18 as sete faziam a mesma coisa -- um LED piscando --, e a ideia
era comparar as linguagens lado a lado. Mas sete pisca-piscas nao mostram
o que cada linguagem tem de proprio. Agora cada uma faz um projeto que
combina com ela:

  Ada          semaforo com sub-rotina
  SPARK        termostato com contratos (Pre)
  Cowgol       contador de apertos em binario
  Millipascal  LED respirando (PWM)
  Rust         sirene com giroflex
  C#           medidor de luz (ESP32, como o nanoFramework)
  Assembly     contador binario no PORTB, com um OUT so

O Assembly e o unico preso a uma placa: ele e AVR, e so roda num ATmega.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demos_comum import *   # noqa: E402,F403

DEMOS = []


def demo(recurso, nome, placa):
    DEMOS.append((recurso, nome, placa))


# =====================================================================
#  Ada: semaforo
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
c.led('vermelho', '10', 'GND.1', 'vermelho', 420, 20, VERMELHO)
c.led('amarelo', '9', 'GND.1', 'amarelo', 500, 20, AMARELO)
c.led('verde', '8', 'GND.1', 'verde', 580, 20, VERDE)

gravar('ada-semaforo', '''
--  Traffic light in Ada.
--
--  Three LEDs and a subprogram that lights only one at a time. ":="
--  assigns and "=" compares; the "loop ... end loop" runs forever, which is
--  what every firmware does.

with Ada.Text_IO; use Ada.Text_IO;

procedure Traffic_Light is
   Red    : constant Integer := 10;
   Yellow : constant Integer := 9;
   Green  : constant Integer := 8;
   Cycles : Integer := 0;

   procedure Light_Up (Pin : Integer; Ms : Integer) is
   begin
      Digital_Write (Red, Low);
      Digital_Write (Yellow, Low);
      Digital_Write (Green, Low);
      Digital_Write (Pin, High);
      Delay_Ms (Ms);
   end Light_Up;

begin
   Pin_Mode (Red, Output);
   Pin_Mode (Yellow, Output);
   Pin_Mode (Green, Output);
   Put_Line ("Traffic light in Ada");

   loop
      Light_Up (Red, 3000);
      Light_Up (Green, 3000);
      Light_Up (Yellow, 1000);
      Cycles := Cycles + 1;
      Put ("cycles completed: ");
      Put_Line (Cycles);
   end loop;
end Traffic_Light;
''', c, '''
Traffic light in Ada.

What this demo shows:
- a subprogram (procedure) with parameters, declared inside the program;
- typed variables and constants, and the firmware's endless loop.

The simulator detects the language from the code itself, and shows which
one it understood in the corner of the editor.
''')
demo('ada-semaforo', 'Semáforo em Ada', 'Arduino Uno')


# =====================================================================
#  SPARK: termostato com contratos
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
c.peca('pot', 'potentiometer', 420, 20, Giro=0.3)
c.ligar('pot', 'VCC', 'placa', '5V', VERMELHO)
c.ligar('pot', 'SIG', 'placa', 'A0', AMARELO)
c.ligar('pot', 'GND', 'placa', 'GND.2', PRETO)
c.led('aquecedor', '8', 'GND.1', 'vermelho', 540, 30, VERMELHO)

gravar('spark-termostato', '''
--  Thermostat with contracts, in SPARK.
--
--  SPARK is Ada with contracts: "Pre" states what the function requires
--  from its caller. A proof tool (GNATprove) checks that nobody breaks the
--  contract -- here it is read and kept, and the program runs with it in
--  place.
--
--  The potentiometer plays the sensor, from 0 to 50 degrees. The heater
--  (red LED) turns on below 18 and only turns off above 22: the gap -- the
--  hysteresis -- keeps it from switching on and off all the time.

pragma SPARK_Mode (On);

procedure Thermostat is
   Sensor  : constant Integer := A0;
   Heater  : constant Integer := 8;
   On_At   : constant Integer := 18;
   Off_At  : constant Integer := 22;
   Heating : Boolean := False;
   Degrees : Integer := 0;

   function To_Degrees (Reading : Integer) return Integer
     with Pre => Reading >= 0 and Reading <= 1023;

   function To_Degrees (Reading : Integer) return Integer is
   begin
      return Reading * 50 / 1023;
   end To_Degrees;

begin
   Pin_Mode (Heater, Output);

   loop
      Degrees := To_Degrees (Analog_Read (Sensor));

      if Degrees < On_At then
         Heating := True;
      elsif Degrees > Off_At then
         Heating := False;
      end if;

      if Heating then
         Digital_Write (Heater, High);
      else
         Digital_Write (Heater, Low);
      end if;

      Put ("temperature: ");
      Put (Degrees);
      if Heating then
         Put_Line (" C, heating");
      else
         Put_Line (" C");
      end if;

      Delay_Ms (500);
   end loop;
end Thermostat;
''', c, '''
Thermostat with contracts, in SPARK.

What this demo shows:
- a function with a contract (Pre) and Ada's if/elsif;
- hysteresis: the heater turns on below 18 degrees and only turns off
  above 22.

Press F5 and turn the potentiometer, which plays the sensor (0 to 50 C).
''')
demo('spark-termostato', 'Termostato com contratos em SPARK', 'Arduino Uno')


# =====================================================================
#  Cowgol: contador de apertos em binario
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
c.botao('botao', '2', 'GND.1', 440, 150)
for i, cor in enumerate(['vermelho', 'amarelo', 'verde', 'azul']):
    c.led(f'bit{i}', str(8 + i), 'GND.1', cor, 420 + i * 80, 20, VERDE)

gravar('cowgol-contador', '''
# Press counter in Cowgol.
#
# Each button press adds one, and the four LEDs show the count in binary
# (from 0 to 15). The serial monitor shows the number.
#
# Cowgol is a systems language in the style of Ada: "sub" instead of
# "procedure", types with their size in the name (uint8 is eight bits)
# and comments with a hash sign.

sub main() is
    var button: uint8 := 2;
    var count: uint8 := 0;
    var before: uint8 := 1;
    var now: uint8 := 1;
    var rest: uint8 := 0;
    var i: uint8 := 0;

    pin_mode(button, INPUT_PULLUP);
    while i < 4 loop
        pin_mode(8 + i, OUTPUT);
        i := i + 1;
    end loop;

    loop
        now := digital_read(button);
        if before == 1 and now == 0 then
            count := (count + 1) % 16;
            print("count: ");
            println(count);

            # One bit per LED: the remainder of dividing by 2 is the low bit.
            rest := count;
            i := 0;
            while i < 4 loop
                digital_write(8 + i, rest % 2);
                rest := rest / 2;
                i := i + 1;
            end loop;
        end if;
        before := now;
        delay_ms(20);
    end loop;
end sub;
''', c, '''
Press counter in Cowgol.

What this demo shows:
- variables with the size in the type (uint8), Cowgol's while and if;
- a number turning into binary on the LEDs, bit by bit, with the
  remainder of a division.

Press F5, then the button: the LEDs count from 0 to 15 in binary.
''')
demo('cowgol-contador', 'Contador de apertos em Cowgol', 'Arduino Uno')


# =====================================================================
#  Millipascal: LED respirando
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
c.led('led', '9', 'GND.1', 'azul', 460, 30, AZUL)

gravar('millipascal-respirar', '''
# Breathing LED in Millipascal.
#
# analogWrite goes from 0 to 255 and back: the LED on pin 9 (which has PWM)
# slowly brightens and dims, like breathing.
#
# Millipascal is a lean Pascal: "proc" opens the procedure, "begin" and
# "end" delimit the blocks, and "set" assigns.

proc main
var level:i32, change:i32
begin
    pin_mode(9, OUTPUT);
    set level = 0;
    set change = 5;

    while true begin
        analog_write(9, level);
        set level = level + change;
        if level >= 255 or level <= 0 begin
            set change = 0 - change;
        end
        delay_ms(30);
    end
end proc
''', c, '''
Breathing LED in Millipascal.

What this demo shows:
- typed variables, "set" to assign and blocks with begin/end;
- PWM with analogWrite on the Uno's pin 9.

Press F5 and watch the LED breathe.
''')
demo('millipascal-respirar', 'LED respirando em Millipascal', 'Arduino Uno')


# =====================================================================
#  Rust: sirene com giroflex
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
c.buzzer('buzzer', '8', 'GND.1', 440, 20)
c.led('giro_a', '12', 'GND.1', 'vermelho', 560, 20, VERMELHO)
c.led('giro_b', '13', 'GND.1', 'azul', 640, 20, AZUL)

gravar('rust-sirene', '''
// Siren in Rust.
//
// The buzzer sweeps up and down in frequency like an ambulance siren, and
// the two LEDs blink in turn, like the rotating beacon.
//
// "#![no_std]" says there is no operating system underneath -- the case on
// every microcontroller. "let mut" declares a variable that can change;
// without "mut" it is read-only.

#![no_std]
#![no_main]

const BUZZER: u8 = 8;
const LED_A: u8 = 12;
const LED_B: u8 = 13;

fn beacon(side: bool) {
    if side {
        digital_write(LED_A, HIGH);
        digital_write(LED_B, LOW);
    } else {
        digital_write(LED_A, LOW);
        digital_write(LED_B, HIGH);
    }
}

fn main() {
    pin_mode(LED_A, OUTPUT);
    pin_mode(LED_B, OUTPUT);
    println!("Siren on");

    let mut sweeps: u32 = 0;
    loop {
        let mut f: u32 = 400;
        while f < 1200 {
            tone(BUZZER, f);
            beacon(f % 200 < 100);
            delay_ms(10);
            f += 20;
        }
        while f > 400 {
            tone(BUZZER, f);
            beacon(f % 200 < 100);
            delay_ms(10);
            f -= 20;
        }
        sweeps += 1;
        println!("siren sweeps: {}", sweeps);
    }
}
''', c, '''
Siren with a rotating beacon, in Rust.

What this demo shows:
- a function with a bool parameter, and Rust's if/else and while;
- tone() sweeping the frequency and println! with "{}".

Press F5 and listen to the siren.
''')
demo('rust-sirene', 'Sirene em Rust', 'Arduino Uno')


# =====================================================================
#  C#: medidor de luz
# =====================================================================

c = Circuito('placa', 'esp32-devkit-v1', 60, 60)
c.peca('ldr', 'photoresistor-sensor', 260, 10, Giro=giro_lux(100))
c.ligar('ldr', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('ldr', 'GND', 'placa', 'GND.1', PRETO)
c.ligar('ldr', 'AO', 'placa', 'D34', AMARELO)
c.led('nivel1', 'D25', 'GND.2', 'amarelo', 330, 140, AMARELO)
c.led('nivel2', 'D26', 'GND.2', 'amarelo', 420, 140, AMARELO)
c.led('nivel3', 'D27', 'GND.2', 'amarelo', 510, 140, AMARELO)

gravar('csharp-medidor-de-luz', '''
// Light meter in C#.
//
// This is the .NET nanoFramework way, which runs on the ESP32: a class with
// a Main, and Console.WriteLine going to the serial monitor. The LDR
// module measures the light, and three LEDs show the level: none in the
// dark, three in sunlight.
//
// Like the real module: MORE light, LOWER the reading.

using System;

public class Program
{
    public static void Main()
    {
        int sensor = 34;
        int[] leds = { 25, 26, 27 };

        for (int i = 0; i < 3; i++) PinMode(leds[i], OUTPUT);
        Console.WriteLine("Light meter");

        while (true)
        {
            int reading = AnalogRead(sensor);       // near 0 in sunlight, 4095 in the dark
            int lit = 3 - reading * 4 / 4096;
            if (lit < 0) lit = 0;

            for (int i = 0; i < 3; i++)
            {
                if (i < lit) DigitalWrite(leds[i], HIGH);
                else DigitalWrite(leds[i], LOW);
            }

            Console.WriteLine($"reading {reading}, LEDs lit: {lit}");
            Delay(300);
        }
    }
}
''', c, '''
Light meter in C#.

What this demo shows:
- the .NET nanoFramework style on the ESP32: a class, a Main and
  Console.WriteLine with interpolated text ($"...{value}...");
- a C# array (int[]) and the light level turning into lit LEDs.

Press F5 and click the LDR module to change the light.
''')
demo('csharp-medidor-de-luz', 'Medidor de luz em C#', 'ESP32')


# =====================================================================
#  Assembly: contador binario no PORTB
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
for i, pino in enumerate(['8', '9', '10', '11', '12', '13']):
    c.led(f'bit{i}', pino, 'GND.1', 'vermelho', 380 + (5 - i) * 70, 20, VERMELHO)

gravar('assembly-contador', '''
; Binary counter in AVR Assembly.
;
; Six LEDs on pins 8 to 13 -- which on the ATmega328P are bits 0 to 5 of
; PORTB -- show a number that climbs from 0 to 63, one step every quarter
; of a second. The rightmost LED is bit 0.
;
;   DDRB  says which pins are outputs (here, the lower six)
;   PORTB takes the whole number at once: one OUT lights them all together
;   INC   adds one, and ANDI cuts off anything past six bits

    ldi r16, 0x3F         ; 0011 1111: PB0 to PB5 are outputs
    out DDRB, r16
    clr r17               ; the counter starts at zero

main_loop:
    out PORTB, r17        ; the number shows on the LEDs
    rcall wait
    inc r17
    andi r17, 0x3F        ; after 63 it wraps back to 0
    rjmp main_loop

; Busy-wait by counting, the same as the blink: three loops, one inside the
; other, that add up to about a quarter of a second at 16 MHz.
wait:
    ldi r20, 20
level3:
    ldi r18, 255
level2:
    ldi r19, 255
level1:
    dec r19
    brne level1
    dec r18
    brne level2
    dec r20
    brne level3
    ret
''', c, '''
Binary counter in AVR Assembly.

What this demo shows:
- the whole PORTB register at once: six pins change with a single OUT;
- INC, ANDI and the wait loop counted in ATmega328P cycles.

Press F5 and watch the six LEDs count from 0 to 63.
''')
demo('assembly-contador', 'Contador binário em Assembly', 'Arduino Uno')


apagar(['ada-pisca', 'spark-pisca', 'cowgol-pisca', 'millipascal-pisca', 'rust-pisca',
        'csharp-pisca', 'assembly-pisca'])

print(f'\n{len(DEMOS)} demos de linguagem')
print('\n--- para o Recentes.cs ---')
for recurso, nome, placa in DEMOS:
    print(f'        new("{recurso}", "{nome}", "{placa}", GrupoDasLinguagens),')
