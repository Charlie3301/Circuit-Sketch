# Linguagens do CircuitSketch

## Como funciona

Todas as linguagens produzem a **mesma árvore** e rodam no **mesmo
interpretador**. Cada uma é só uma porta de entrada.

Essa é a decisão que sustenta o projeto desde a Alpha 17, e ela tem um
motivo concreto: um interpretador por linguagem criaria dez verdades
diferentes sobre o que "acender o pino 13" significa, e elas
divergiriam na primeira correção feita em nove delas. Do jeito que
está, uma placa em Rust e outra em Ada na mesma bancada conversam pelo
mesmo barramento, porque as duas terminam na mesma `Maquina`.

O Assembly é a única exceção, e é uma exceção honesta: não há expressão
nem função em Assembly, há instrução e registrador. Ele tem montador e
executor próprios (`Montador.cs`, `Avr.cs`) e se liga ao resto pelo
mesmo lugar que todos — as portas de E/S.

---

## O que já roda (Alpha 18)

| Linguagem | Extensão | Ponto de entrada | Front-end |
|---|---|---|---|
| Arduino (C++) | `.ino` | `setup()` / `loop()` | `Lexer.cs` + `Parser.cs` |
| C | `.c` | `int main()` com laço | o mesmo do C++ |
| C# | `.cs` | `static void Main()` | o do C++, com adaptação de texto |
| MicroPython | `.py` | corpo do módulo + `while True:` | `LexerPython.cs` + `ParserPython.cs` |
| Rust | `.rs` | `fn main()` com `loop {}` | `LexerRust.cs` + `ParserRust.cs` |
| Ada | `.adb` | `procedure` com `loop` | `LexerPascal.cs` + `ParserPascal.cs` |
| SPARK | `.adb` | idem, com contratos | o mesmo, dialeto SPARK |
| Cowgol | `.cow` | `sub main() is` | o mesmo, dialeto Cowgol |
| Millipascal | `.mp` | `proc main begin` | o mesmo, dialeto Millipascal |
| Assembly (AVR) | `.S` | primeira instrução | `Montador.cs` + `Avr.cs` |

A linguagem é descoberta **pelo próprio código**, e não pela extensão:
o código mora dentro do `.sktch`, e a pessoa troca de linguagem sem
trocar de arquivo. O editor mostra no canto qual foi entendida, antes
de rodar.

### O que é traduzido entre elas

`digitalWrite`, `Digital_Write`, `digital_write` e `DigitalWrite` são a
mesma operação. O `Vocabulario.cs` achata o nome — tira sublinhado,
ignora caixa — e manda para a única implementação que existe.

A tradução **não vale** em C e C++: ali o nome é o que está escrito, e
traduzir trocaria uma função da pessoa pela do simulador sem avisar.

### Limites assumidos

Ditos em voz alta em vez de mal-feitos:

- **Rust**: empréstimo e posse não são verificados. Quem escreve Rust
  aqui está testando o *circuito*; um verificador de posse pela metade
  reprovaria programas corretos.
- **SPARK**: os contratos (`Pre`, `Post`, `Global`) são lidos e
  guardados, não provados. Provar é trabalho de uma ferramenta de
  prova. Recusá-los seria recusar SPARK inteiro.
- **Assembly**: o relógio anda por *instrução*, não por ciclo. Contar
  ciclo exigiria uma tabela de tempos por instrução e por família de
  AVR, e entregaria precisão que ninguém pediu no lugar de clareza que
  todo mundo precisa.
- **C#**: propriedade, evento, LINQ e genérico dão erro de sintaxe com
  a linha certa, em vez de rodar errado.
- **Cowgol e Millipascal**: são linguagens de nicho, com pouca
  documentação pública. O que está implementado é o núcleo delas —
  `sub`/`proc`, `var`, `loop`, `if`, `:=`. Um programa que use um canto
  raro da linguagem pode não passar, e nesse caso o erro aponta a
  linha.

---

## Para o futuro

Em ordem de **utilidade real** para quem vai usar isto, não de
facilidade.

### Alta prioridade

| Linguagem | Onde roda | Por que vale |
|---|---|---|
| **CircuitPython** | ESP32, RP2040, SAMD | É o MicroPython da Adafruit, com biblioteca própria (`board`, `digitalio`). O front-end já existe: falta o vocabulário. É o menor trabalho da lista com o maior retorno. |
| **TinyGo** | ESP32, RP2040, AVR | Go para microcontrolador. Cresceu muito e a sintaxe é regular — front-end de esforço parecido com o do Rust. |
| **Lua (NodeMCU)** | ESP8266, ESP32 | Foi *a* linguagem do ESP8266 antes do MicroPython, e há muito material de aula em Lua. Linguagem pequena e bem documentada. |
| **JavaScript (Espruino)** | ESP32, STM32, nRF52 | Porta de entrada para quem vem da web. Espruino é estável e tem API de pino simples (`digitalWrite`, `setInterval`). |

### Média prioridade

| Linguagem | Onde roda | Observação |
|---|---|---|
| **Zig** | qualquer | Sintaxe próxima do Rust; o front-end aproveitaria boa parte do `ParserRust`. |
| **Nim** | qualquer | Compila para C. Sintaxe por indentação — aproveitaria o `LexerPython`. |
| **Forth (Mecrisp)** | STM32, RP2040 | Tradição forte em embarcado e um interpretador de pilha é simples de escrever. O modelo de execução é *muito* diferente do resto, então não reaproveita nada. |
| **BASIC (Great Cow / PICAXE)** | AVR, PIC | Ainda é o que muita escola técnica usa como primeira linguagem. |
| **Assembly ARM Thumb** | STM32, RP2040 | O montador já existe; falta o conjunto de instruções e o mapa de registradores. É a continuação natural do AVR. |
| **Assembly Xtensa / RISC-V** | ESP32 | Mesma situação. O RISC-V é o mais simples dos três conjuntos. |

### Baixa prioridade

| Linguagem | Observação |
|---|---|
| **Swift Embedded** | Novo demais; a API de hardware ainda está mudando. |
| **D (BetterC)** | Praticamente não se usa em microcontrolador. |
| **Free Pascal / mikroPascal** | O `ParserPascal` daria conta com pouco trabalho, mas quase ninguém usa. |
| **Ladder / Texto Estruturado (IEC 61131-3)** | É de CLP, não de microcontrolador. Faria sentido só se o simulador ganhar um modo de automação industrial. |
| **Scratch / Blockly** | Não é texto: precisaria de um editor visual inteiro, que é um projeto à parte. |
| **XOD** | Mesma situação do Blockly. |
| **MATLAB / Simulink** | Geração de código, e não linguagem de escrever à mão. |

### O que cada uma custaria

Pela experiência das nove que já entraram:

- **Reaproveita front-end existente** (CircuitPython, Free Pascal,
  mikroPascal): um dia de trabalho, quase tudo é vocabulário.
- **Front-end novo de família conhecida** (TinyGo, Zig, Nim, Lua,
  JavaScript): lexer mais parser, na escala do `ParserRust` —
  aproximadamente mil linhas.
- **Modelo de execução diferente** (Forth, Assembly de outra
  arquitetura): montador ou máquina de pilha própria, mais o mapa de
  registradores. Escala do `Avr.cs`.
- **Editor visual** (Scratch, Blockly, XOD): projeto à parte, não é
  uma linguagem a mais.
