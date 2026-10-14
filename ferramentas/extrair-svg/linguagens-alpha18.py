"""python ferramentas/extrair-svg/linguagens-alpha18.py

Uma demo para cada linguagem nova da Alpha 18.

Todas fazem a MESMA coisa: piscam um LED ligado por fio, com resistor.
De proposito -- assim da para abrir duas lado a lado e ver o que muda de
uma linguagem para a outra, que e a unica coisa que interessa aqui. Um
exemplo diferente por linguagem esconderia a comparacao atras de
enfeite.

O Assembly e o unico preso a uma placa: ele e AVR, e so roda num
ATmega. As outras rodam em qualquer uma.
"""

# Aposentado na Alpha 19: as demos de pisca-pisca sairam do programa, e cada
# placa (e linguagem) ganhou um projeto proprio em demos-linguagens.py. O codigo
# fica aqui como historico; rodar este arquivo recriaria demos que nenhum
# menu lista.
raise SystemExit('aposentado: use ferramentas/extrair-svg/demos-linguagens.py')

import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PASTA = BASE + 'app/assets/demos/'


ADA = [
    '--  Pisca-pisca em Ada.',
    '--',
    '--  O ":=" atribui e o "=" compara. O laco "loop ... end loop"',
    '--  roda para sempre, que e o que todo firmware faz.',
    '',
    'with Ada.Text_IO; use Ada.Text_IO;',
    '',
    'procedure Pisca is',
    '   Led : constant Integer := 13;',
    'begin',
    '   Pin_Mode (Led, Output);',
    '   Put_Line ("Ada acordou");',
    '',
    '   loop',
    '      Digital_Write (Led, High);',
    '      Delay_Ms (500);',
    '      Digital_Write (Led, Low);',
    '      Delay_Ms (500);',
    '   end loop;',
    'end Pisca;',
]

SPARK = [
    '--  Pisca-pisca em SPARK.',
    '--',
    '--  SPARK e Ada com contratos: o "Pre" diz o que a sub-rotina exige',
    '--  e o "Post" o que ela garante. Aqui eles sao LIDOS e guardados --',
    '--  provar contrato e trabalho de uma ferramenta de prova, e nao de',
    '--  um simulador --, mas o programa roda com eles no lugar.',
    '',
    'pragma SPARK_Mode (On);',
    '',
    'procedure Pisca is',
    '   Led : constant Integer := 13;',
    '',
    '   procedure Acender (Pino : Integer)',
    '     with Pre => Pino >= 0;',
    '',
    '   procedure Acender (Pino : Integer) is',
    '   begin',
    '      Digital_Write (Pino, High);',
    '   end Acender;',
    '',
    'begin',
    '   Pin_Mode (Led, Output);',
    '',
    '   loop',
    '      Acender (Led);',
    '      Delay_Ms (400);',
    '      Digital_Write (Led, Low);',
    '      Delay_Ms (400);',
    '   end loop;',
    'end Pisca;',
]

COWGOL = [
    '# Pisca-pisca em Cowgol.',
    '#',
    '# Cowgol e uma linguagem de sistemas com jeito de Ada: "sub" no',
    '# lugar de "procedure", tipos com o tamanho escrito no nome',
    '# (uint8 sao oito bits) e comentario com cerquilha.',
    '',
    'sub main() is',
    '    var led: uint8 := 13;',
    '    var contador: uint16 := 0;',
    '',
    '    pin_mode(led, OUTPUT);',
    '',
    '    loop',
    '        digital_write(led, HIGH);',
    '        delay_ms(300);',
    '        digital_write(led, LOW);',
    '        delay_ms(300);',
    '',
    '        contador := contador + 1;',
    '    end loop;',
    'end sub;',
]

MILLIPASCAL = [
    '# Pisca-pisca em Millipascal.',
    '#',
    '# Millipascal e um Pascal enxuto: "proc" abre o procedimento,',
    '# "begin" e "end" delimitam, e "set" atribui.',
    '',
    'proc main',
    'begin',
    '    pin_mode(13, OUTPUT);',
    '',
    '    while true begin',
    '        digital_write(13, HIGH);',
    '        delay_ms(250);',
    '        digital_write(13, LOW);',
    '        delay_ms(250);',
    '    end',
    'end proc',
]

RUST = [
    '// Pisca-pisca em Rust.',
    '//',
    '// "#![no_std]" diz que nao ha sistema operacional embaixo, que e a',
    '// situacao de todo microcontrolador. "let mut" declara uma',
    '// variavel que pode mudar; sem o "mut" ela e so leitura.',
    '',
    '#![no_std]',
    '#![no_main]',
    '',
    'const LED: u8 = 13;',
    '',
    'fn main() {',
    '    pin_mode(LED, OUTPUT);',
    '    println!("Rust acordou");',
    '',
    '    let mut piscadas: u32 = 0;',
    '',
    '    loop {',
    '        digital_write(LED, HIGH);',
    '        delay_ms(200);',
    '        digital_write(LED, LOW);',
    '        delay_ms(200);',
    '',
    '        piscadas += 1;',
    '',
    '        if piscadas % 5 == 0 {',
    '            println!("ja piscou {} vezes", piscadas);',
    '        }',
    '    }',
    '}',
]

CSHARP = [
    '// Pisca-pisca em C#.',
    '//',
    '// E o jeito do .NET nanoFramework, que roda em ESP32: uma classe',
    '// com um Main, e o Console.WriteLine indo para o monitor serial.',
    '',
    'using System;',
    '',
    'public class Program',
    '{',
    '    public static void Main()',
    '    {',
    '        int led = 2;',
    '        int piscadas = 0;',
    '',
    '        PinMode(led, OUTPUT);',
    '        Console.WriteLine("C# acordou");',
    '',
    '        while (true)',
    '        {',
    '            DigitalWrite(led, HIGH);',
    '            Delay(250);',
    '            DigitalWrite(led, LOW);',
    '            Delay(250);',
    '',
    '            piscadas = piscadas + 1;',
    '        }',
    '    }',
    '}',
]

ASSEMBLY = [
    '; Pisca-pisca em Assembly AVR.',
    ';',
    '; Aqui nao ha digitalWrite: ha PORTA e BIT. No ATmega328P do',
    '; Arduino Uno, o bit 5 do PORTB E o pino 13 -- e por isso que o',
    '; pisca-pisca mais copiado do mundo mexe justamente nele.',
    ';',
    ';   DDRB  diz quais pinos sao saida',
    ';   PORTB diz o nivel de cada um',
    ';   SBI / CBI ligam e desligam UM bit',
    '',
    '.equ LED = 5              ; PB5, que e o pino 13',
    '',
    '    ldi r16, (1<<LED)     ; a mascara do bit 5',
    '    out DDRB, r16         ; PB5 vira saida',
    '',
    'principal:',
    '    sbi PORTB, LED        ; acende',
    '    rcall espera',
    '    cbi PORTB, LED        ; apaga',
    '    rcall espera',
    '    rjmp principal',
    '',
    '; Espera por contagem: e assim que se marca tempo sem timer.',
    ';',
    '; Tres lacos, um dentro do outro. Contando ciclo a ciclo -- dec',
    '; custa 1, brne custa 2 quando desvia e 1 quando nao --, uma',
    '; chamada "rcall espera" custa 3.916.867 ciclos. A 16 MHz isso e',
    '; 244,8 ms, e o LED pisca duas vezes por segundo, como na placa.',
    'espera:',
    '    ldi r20, 20',
    'nivel3:',
    '    ldi r18, 255',
    'nivel2:',
    '    ldi r17, 255',
    'nivel1:',
    '    dec r17',
    '    brne nivel1',
    '    dec r18',
    '    brne nivel2',
    '    dec r20',
    '    brne nivel3',
    '    ret',
]


# O nome do terra muda de placa para placa: quando ha mais de um GND,
# eles ganham sufixo -- "GND.1", "GND.2" -- para o netlist nao tratar os
# tres como o mesmo ponto de ligacao no desenho.
TERRA = {
    'arduino-uno': 'GND.1',
    'esp32-devkit-v1': 'GND.1',
}


# recurso, nome no menu, placa, tipo da peca, pino, codigo
DEMOS = [
    ('ada-pisca', 'Pisca-pisca em Ada', 'Arduino Uno', 'arduino-uno', '13', ADA),
    ('spark-pisca', 'Pisca-pisca em SPARK', 'Arduino Uno', 'arduino-uno', '13', SPARK),
    ('cowgol-pisca', 'Pisca-pisca em Cowgol', 'Arduino Uno', 'arduino-uno', '13', COWGOL),
    ('millipascal-pisca', 'Pisca-pisca em Millipascal', 'Arduino Uno', 'arduino-uno',
     '13', MILLIPASCAL),
    ('rust-pisca', 'Pisca-pisca em Rust', 'Arduino Uno', 'arduino-uno', '13', RUST),
    ('csharp-pisca', 'Pisca-pisca em C#', 'ESP32', 'esp32-devkit-v1', 'D2', CSHARP),

    # O Assembly e AVR: so roda num ATmega.
    ('assembly-pisca', 'Pisca-pisca em Assembly', 'Arduino Uno', 'arduino-uno',
     '13', ASSEMBLY),
]

for recurso, nome, placa, tipo, pino, codigo in DEMOS:
    projeto = {
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'demo do CircuitSketch',
        'descricao': [
            f'{nome}.',
            '',
            'Todas as demos de linguagem fazem a mesma coisa: piscam um LED',
            'ligado no pino com um resistor de 330 ohm. De proposito -- assim',
            'da para abrir duas lado a lado e ver o que muda de uma linguagem',
            'para a outra.',
            '',
            'O simulador descobre a linguagem pelo proprio codigo, e mostra',
            'qual entendeu no canto do editor.',
        ],
        'codigo': codigo,
        'circuito': {
            'Pecas': [
                {'Id': 'placa', 'Tipo': tipo, 'X': 120, 'Y': 60},
                {'Id': 'led', 'Tipo': 'led', 'X': 460, 'Y': 120, 'Cor': 'vermelho'},
                {'Id': 'r', 'Tipo': 'resistor', 'X': 460, 'Y': 260, 'Ohms': 330},
            ],
            'Fios': [
                {'DePeca': 'placa', 'DePino': pino,
                 'ParaPeca': 'led', 'ParaPino': 'A', 'Cor': '#00A651'},
                {'DePeca': 'led', 'DePino': 'C',
                 'ParaPeca': 'r', 'ParaPino': '1', 'Cor': '#1A1A1A'},
                {'DePeca': 'r', 'DePino': '2',
                 'ParaPeca': 'placa', 'ParaPino': TERRA.get(tipo, 'GND'),
                 'Cor': '#1A1A1A'},
            ],
        },
    }

    io.open(PASTA + recurso + '.sktch', 'w', encoding='utf-8', newline='').write(
        json.dumps(projeto, ensure_ascii=False, indent=1))

    print(f'{recurso}.sktch  ({nome})')

print('\n--- para o Recentes.cs ---')
for recurso, nome, placa, *_ in DEMOS:
    print(f'        new("{recurso}", "{nome}", "{placa}"),')
