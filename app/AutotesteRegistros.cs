namespace Simulador.App;

/// <summary>
/// Os registradores de E/S do AVR no C++: PORTx/DDRx/PINx mexendo nos pinos
/// da placa pela tabela do chip.
/// </summary>
public static partial class Autoteste
{
    static void Avr_Registradores()
    {
        saida.AppendLine("registradores do AVR");

        // O blink de registrador: o LED do Uno e o PB5 (pino 13).
        var (maq, serial, erro) = RodarComPinos("""
            void setup() {
              Serial.begin(9600);
              DDRB |= _BV(PB5);
              PORTB |= (1 << PB5);
              Serial.println(digitalRead(13));
              PINB = _BV(PB5);
              Serial.println(digitalRead(13));
              DDRD &= ~(1 << DDD2);
              PORTD |= (1 << PORTD2);
              Serial.println((PIND >> PIND2) & 1);
              Serial.println(DDRB, BIN);
            }
            void loop() {}
            """, "arduino-uno", 500);
        string s = string.Join(" | ", serial);
        saida.AppendLine($"        uno: {s} {erro}");
        Conferir("avr: DDRB/PORTB acendem o pino 13 (PB5) e escrever 1 no PINB troca o bit",
            erro is null && serial.Count >= 2 && serial[0] == "1" && serial[1] == "0", $"{s} {erro}");
        Conferir("avr: PORTD numa entrada liga o pull-up, e o PIND le o pino",
            serial.Count >= 3 && serial[2] == "1" && maq?.Modo[2] == 2, s);
        Conferir("avr: ler o DDRB devolve os bits de direcao", serial.Count >= 4 && serial[3] == "100000", s);

        // No Mega, o PB7 e o pino 13; o PORTA e 22..29.
        var (mega, _, erroMega) = RodarComPinos("""
            void setup() {
              DDRA = 0xFF;
              PORTA = 0b00000101;
              DDRB |= _BV(PB7);
              PORTB |= _BV(PB7);
            }
            void loop() {}
            """, "arduino-mega", 500);
        Conferir("avr: no Mega o PORTA sai nos pinos 22..29 e o PB7 e o 13",
            erroMega is null && mega?.Digital[22] == 1 && mega.Digital[23] == 0 && mega.Digital[24] == 1 && mega.Digital[13] == 1,
            erroMega ?? $"22={mega?.Digital[22]} 23={mega?.Digital[23]} 24={mega?.Digital[24]} 13={mega?.Digital[13]}");

        // Na ESP32 nao existe PORTB: o erro do compilador.
        var (_, _, erroEsp) = RodarComPinos("void setup() { PORTB = 1; }\nvoid loop() {}", "esp32-devkit-v1", 500);
        Conferir("avr: PORTB numa ESP32 nao compila", erroEsp is not null, erroEsp ?? "rodou");
    }
}
