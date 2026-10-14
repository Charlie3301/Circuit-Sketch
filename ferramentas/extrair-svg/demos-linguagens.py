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
--  Semaforo em Ada.
--
--  Tres LEDs e uma sub-rotina que acende um so de cada vez. O ":=" atribui
--  e o "=" compara; o laco "loop ... end loop" roda para sempre, que e o
--  que todo firmware faz.

with Ada.Text_IO; use Ada.Text_IO;

procedure Semaforo is
   Vermelho : constant Integer := 10;
   Amarelo  : constant Integer := 9;
   Verde    : constant Integer := 8;
   Ciclos   : Integer := 0;

   procedure Acender (Pino : Integer; Tempo : Integer) is
   begin
      Digital_Write (Vermelho, Low);
      Digital_Write (Amarelo, Low);
      Digital_Write (Verde, Low);
      Digital_Write (Pino, High);
      Delay_Ms (Tempo);
   end Acender;

begin
   Pin_Mode (Vermelho, Output);
   Pin_Mode (Amarelo, Output);
   Pin_Mode (Verde, Output);
   Put_Line ("Semaforo em Ada");

   loop
      Acender (Vermelho, 3000);
      Acender (Verde, 3000);
      Acender (Amarelo, 1000);
      Ciclos := Ciclos + 1;
      Put ("ciclos completos: ");
      Put_Line (Ciclos);
   end loop;
end Semaforo;
''', c, '''
Semaforo em Ada.

O que esta demo mostra:
- uma sub-rotina (procedure) com parametros, declarada dentro do programa;
- variavel e constante com tipo, e o laco infinito do firmware.

O simulador descobre a linguagem pelo proprio codigo, e mostra qual
entendeu no canto do editor.
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
--  Termostato com contratos, em SPARK.
--
--  SPARK e Ada com contratos: o "Pre" diz o que a funcao exige de quem a
--  chama. Uma ferramenta de prova (o GNATprove) confere que ninguem quebra
--  o contrato -- aqui ele e lido e guardado, e o programa roda com ele no
--  lugar.
--
--  O potenciometro faz o papel do sensor, de 0 a 50 graus. O aquecedor
--  (LED vermelho) liga abaixo de 18 e so desliga acima de 22: a folga -- a
--  histerese -- evita que ele fique ligando e desligando.

pragma SPARK_Mode (On);

procedure Termostato is
   Sensor     : constant Integer := A0;
   Aquecedor  : constant Integer := 8;
   Liga_Em    : constant Integer := 18;
   Desliga_Em : constant Integer := 22;
   Ligado     : Boolean := False;
   Graus      : Integer := 0;

   function Para_Graus (Leitura : Integer) return Integer
     with Pre => Leitura >= 0 and Leitura <= 1023;

   function Para_Graus (Leitura : Integer) return Integer is
   begin
      return Leitura * 50 / 1023;
   end Para_Graus;

begin
   Pin_Mode (Aquecedor, Output);

   loop
      Graus := Para_Graus (Analog_Read (Sensor));

      if Graus < Liga_Em then
         Ligado := True;
      elsif Graus > Desliga_Em then
         Ligado := False;
      end if;

      if Ligado then
         Digital_Write (Aquecedor, High);
      else
         Digital_Write (Aquecedor, Low);
      end if;

      Put ("temperatura: ");
      Put (Graus);
      if Ligado then
         Put_Line (" C, aquecendo");
      else
         Put_Line (" C");
      end if;

      Delay_Ms (500);
   end loop;
end Termostato;
''', c, '''
Termostato com contratos, em SPARK.

O que esta demo mostra:
- uma funcao com contrato (Pre) e o if/elsif do Ada;
- histerese: o aquecedor liga abaixo de 18 graus e so desliga acima de 22.

Aperte F5 e gire o potenciometro, que faz o papel do sensor (0 a 50 C).
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
# Contador de apertos em Cowgol.
#
# Cada aperto do botao soma um, e os quatro LEDs mostram a contagem em
# binario (de 0 a 15). O monitor serial mostra o numero.
#
# Cowgol e uma linguagem de sistemas com jeito de Ada: "sub" no lugar de
# "procedure", tipos com o tamanho escrito no nome (uint8 sao oito bits)
# e comentario com cerquilha.

sub main() is
    var botao: uint8 := 2;
    var contagem: uint8 := 0;
    var antes: uint8 := 1;
    var agora: uint8 := 1;
    var resto: uint8 := 0;
    var i: uint8 := 0;

    pin_mode(botao, INPUT_PULLUP);
    while i < 4 loop
        pin_mode(8 + i, OUTPUT);
        i := i + 1;
    end loop;

    loop
        agora := digital_read(botao);
        if antes == 1 and agora == 0 then
            contagem := (contagem + 1) % 16;
            print("contagem: ");
            println(contagem);

            # Um bit por LED: o resto da divisao por 2 e o bit de baixo.
            resto := contagem;
            i := 0;
            while i < 4 loop
                digital_write(8 + i, resto % 2);
                resto := resto / 2;
                i := i + 1;
            end loop;
        end if;
        antes := agora;
        delay_ms(20);
    end loop;
end sub;
''', c, '''
Contador de apertos em Cowgol.

O que esta demo mostra:
- variaveis com o tamanho no tipo (uint8), while e if do Cowgol;
- um numero virando binario nos LEDs, bit a bit, com o resto da divisao.

Aperte F5 e o botao: os LEDs contam de 0 a 15 em binario.
''')
demo('cowgol-contador', 'Contador de apertos em Cowgol', 'Arduino Uno')


# =====================================================================
#  Millipascal: LED respirando
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
c.led('led', '9', 'GND.1', 'azul', 460, 30, AZUL)

gravar('millipascal-respirar', '''
# LED respirando em Millipascal.
#
# O analogWrite vai de 0 a 255 e volta: o LED no pino 9 (que tem PWM)
# acende e apaga devagar, como quem respira.
#
# Millipascal e um Pascal enxuto: "proc" abre o procedimento, "begin" e
# "end" delimitam os blocos, e "set" atribui.

proc main
var brilho:i32, passo:i32
begin
    pin_mode(9, OUTPUT);
    set brilho = 0;
    set passo = 5;

    while true begin
        analog_write(9, brilho);
        set brilho = brilho + passo;
        if brilho >= 255 or brilho <= 0 begin
            set passo = 0 - passo;
        end
        delay_ms(30);
    end
end proc
''', c, '''
LED respirando em Millipascal.

O que esta demo mostra:
- variaveis com tipo, "set" para atribuir e blocos com begin/end;
- PWM com analogWrite no pino 9 do Uno.

Aperte F5 e veja o LED respirar.
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
// Sirene em Rust.
//
// O buzzer sobe e desce de frequencia como a sirene de uma ambulancia, e
// os dois LEDs piscam alternados, como o giroflex.
//
// "#![no_std]" diz que nao ha sistema operacional embaixo -- a situacao de
// todo microcontrolador. "let mut" declara uma variavel que pode mudar;
// sem o "mut" ela e so leitura.

#![no_std]
#![no_main]

const BUZZER: u8 = 8;
const LED_A: u8 = 12;
const LED_B: u8 = 13;

fn giroflex(lado: bool) {
    if lado {
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
    println!("Sirene ligada");

    let mut voltas: u32 = 0;
    loop {
        let mut f: u32 = 400;
        while f < 1200 {
            tone(BUZZER, f);
            giroflex(f % 200 < 100);
            delay_ms(10);
            f += 20;
        }
        while f > 400 {
            tone(BUZZER, f);
            giroflex(f % 200 < 100);
            delay_ms(10);
            f -= 20;
        }
        voltas += 1;
        println!("voltas da sirene: {}", voltas);
    }
}
''', c, '''
Sirene com giroflex, em Rust.

O que esta demo mostra:
- funcao com parametro bool, if/else e while do Rust;
- tone() varrendo a frequencia e o println! com "{}".

Aperte F5 e escute a sirene.
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
// Medidor de luz em C#.
//
// E o jeito do .NET nanoFramework, que roda na ESP32: uma classe com um
// Main, e o Console.WriteLine indo para o monitor serial. O modulo de LDR
// mede a luz, e tres LEDs mostram o nivel: nenhum no escuro, tres no sol.
//
// Como o modulo de verdade: MAIS luz, MENOR a leitura.

using System;

public class Program
{
    public static void Main()
    {
        int sensor = 34;
        int[] leds = { 25, 26, 27 };

        for (int i = 0; i < 3; i++) PinMode(leds[i], OUTPUT);
        Console.WriteLine("Medidor de luz");

        while (true)
        {
            int leitura = AnalogRead(sensor);       // perto de 0 no sol, de 4095 no escuro
            int acesos = 3 - leitura * 4 / 4096;
            if (acesos < 0) acesos = 0;

            for (int i = 0; i < 3; i++)
            {
                if (i < acesos) DigitalWrite(leds[i], HIGH);
                else DigitalWrite(leds[i], LOW);
            }

            Console.WriteLine($"leitura {leitura}, LEDs acesos: {acesos}");
            Delay(300);
        }
    }
}
''', c, '''
Medidor de luz em C#.

O que esta demo mostra:
- o estilo do .NET nanoFramework na ESP32: uma classe, um Main e o
  Console.WriteLine com texto interpolado ($"...{valor}...");
- um vetor do C# (int[]) e o nivel de luz virando LEDs acesos.

Aperte F5 e clique no modulo de LDR para mudar a luz.
''')
demo('csharp-medidor-de-luz', 'Medidor de luz em C#', 'ESP32')


# =====================================================================
#  Assembly: contador binario no PORTB
# =====================================================================

c = Circuito('placa', 'arduino-uno', 60, 160)
for i, pino in enumerate(['8', '9', '10', '11', '12', '13']):
    c.led(f'bit{i}', pino, 'GND.1', 'vermelho', 380 + (5 - i) * 70, 20, VERMELHO)

gravar('assembly-contador', '''
; Contador binario em Assembly AVR.
;
; Seis LEDs nos pinos 8 a 13 -- que no ATmega328P sao os bits 0 a 5 do
; PORTB -- mostram um numero que sobe de 0 a 63, um a cada quarto de
; segundo. O LED da direita e o bit 0.
;
;   DDRB  diz quais pinos sao saida (aqui, os seis de baixo)
;   PORTB recebe o numero inteiro de uma vez: um OUT acende todos juntos
;   INC   soma um, e o ANDI corta o que passa de seis bits

    ldi r16, 0x3F         ; 0011 1111: PB0 a PB5 sao saida
    out DDRB, r16
    clr r17               ; o contador comeca em zero

principal:
    out PORTB, r17        ; o numero aparece nos LEDs
    rcall espera
    inc r17
    andi r17, 0x3F        ; depois do 63 volta para 0
    rjmp principal

; Espera por contagem, a mesma do pisca-pisca: tres lacos, um dentro do
; outro, que a 16 MHz somam perto de um quarto de segundo.
espera:
    ldi r20, 20
nivel3:
    ldi r18, 255
nivel2:
    ldi r19, 255
nivel1:
    dec r19
    brne nivel1
    dec r18
    brne nivel2
    dec r20
    brne nivel3
    ret
''', c, '''
Contador binario em Assembly AVR.

O que esta demo mostra:
- o registrador PORTB inteiro de uma vez: seis pinos mudam num OUT so;
- INC, ANDI e o laco de espera contado em ciclos do ATmega328P.

Aperte F5 e veja os seis LEDs contarem de 0 a 63.
''')
demo('assembly-contador', 'Contador binário em Assembly', 'Arduino Uno')


apagar(['ada-pisca', 'spark-pisca', 'cowgol-pisca', 'millipascal-pisca', 'rust-pisca',
        'csharp-pisca', 'assembly-pisca'])

print(f'\n{len(DEMOS)} demos de linguagem')
print('\n--- para o Recentes.cs ---')
for recurso, nome, placa in DEMOS:
    print(f'        new("{recurso}", "{nome}", "{placa}", GrupoDasLinguagens),')
