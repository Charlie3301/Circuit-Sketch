# Plano tecnico: Alpha 24, 25 e 26

Mantido pelo Claude (diretor). Cada item diz o que ja tem base, o que falta
e quem faz. Grafico (icones, SVG, telas, temas) e sempre do Claude.

## Alpha 24 — diagram.json, bateria funcional, correcoes de logica

Feito: diagram.json no formato do Wokwi; bateria funcional (placa sem o cabo
USB); reguladores de tensao (78xx, AMS1117, LM2596, MT3608).

**Base das correcoes de logica (pronta):**
- `projetos/`: 38 projetos reais (drone, carrinhos, rovers, reguladores,
  classicos), gerados por `ferramentas/projetos.py`, cada um com roteiro de
  teste em `projetos/roteiros.json`.
- Bloco `Projetos_Testados` (`app/AutotesteProjetos.cs`) roda os roteiros;
  `CS_PROJETO=<nome>` roda um so; `CS_RESULTADOS=<arquivo>` grava o JSON.
- `ferramentas/testados.py` separa em `Desktop\TESTED\WORKING` e `NOT WORKING`.

**Fluxo de cada correcao:** projeto falha -> diagnostico (A: o projeto
estava errado; B: o simulador difere da placa) -> o B vira correcao no
simulador -> o roteiro do projeto passa a ter `"esperado": "funciona"` e vira
teste de regressao.

**Primeira rodada (13/10/2026): 27 de 38.** Suspeitas de B, a confirmar
(ChatGPT, TAREFA-002):
- Adafruit_MPU6050 (drone, nivel-mpu6050): o sketch nao imprime nada.
- Servo ligado direto no receptor nao segue o canal (o ESC segue).
- Receptor no 5V.2 do Nano sem pulso (5V e 5V.2 sao o mesmo no?).
- controle-ir-leds: "curto no pino 3" que nao existe (a Queima ve um trilho?).
- Rele KS2E com a bobina entre o 5V e o pino: frente sai ao contrario.
- L298N: PWM 200 no ENB gira igual ao ENA no maximo.
- ESP-NOW: o segundo pacote (botao solto) nao apaga o LED.

Proximos projetos (Claude escreve; os agentes rodam): estereo/PCM5102A,
AAC/WMA/APE, MicroPython (try/except, class, with, lambda, Timer, RTC, SPI),
o modulo de rele de 1 canal (peca nova: falta no catalogo).

## Alpha 25 — 12 idiomas e graficos refeitos

**Base dos idiomas (pronta):** `motor/Idioma.cs` (`Idioma.T`), `{local:T}`
no XAML, catalogos em `motor/idiomas/`, `ferramentas/idiomas.py`, bloco
`Idiomas_Base`. O seletor nas Configuracoes so aparece com um catalogo
completo.

Escala: ~16 mil textos (11,6 mil na interface, 4,3 mil no motor, contando
algumas strings que nao sao de tela).

Migracao (em lotes, arquivo por arquivo):
1. Marcar as frases de um arquivo com `Idioma.T` / `{local:T}` (formatos no
   lugar de `$"..."`), rodar `ferramentas/idiomas.py`, rodar a suite.
   -> Antigravity (mecanico) e ChatGPT (arquivos com logica de texto).
2. Traduzir os catalogos -> ChatGPT, por idioma, revisado pelo Claude.
3. Os 12 idiomas: a confirmar com o usuario.

Graficos refeitos: so o Claude (pecas, icones, botoes, telas).

## Alpha 26 / Beta 1 — otimizacao, testes, Linux e macOS, lancamento

**O que prende ao Windows (levantamento de 13/10/2026):**
- A interface e WPF (so Windows): 41 arquivos de `app/` usam WPF; ~35 nao
  (energia, queima, catalogo, Wokwi, reguladores...).
- O modelo (`app/Circuito.cs`: PecaNoCircuito, Fio, Netlist, Geometria) usa
  `System.Windows.Point` -- o primeiro no a desatar.
- `motor/` ja e `net10.0` (multiplataforma), menos tres arquivos: o modo
  nativo (`Nativo/Tcc.cs`: libtcc.dll; `Nativo/CpuNativa.cs`: o tratador de
  excecao do Windows) e `PortaBluetooth.cs` (Winsock AF_BTH).

**Caminho proposto:**
1. Biblioteca `nucleo` (net10.0, sem WPF): o modelo e a logica que nao
   desenham (Circuito sem `System.Windows.Point`, Energia, Queima, Catalogo,
   Wokwi, reguladores, motores...). A WPF continua usando-a.
2. Interface nova em Avalonia (XAML parecido com o WPF; AvaloniaEdit no
   lugar do AvalonEdit; SVG pelo Avalonia.Svg), tela por tela, ate a WPF
   poder sair.
3. O modo nativo por sistema: libtcc compilada para Linux e macOS, e o
   tratador de falha por sinal (SIGSEGV) no lugar da excecao do Windows.
4. Bluetooth do computador por sistema (BlueZ, IOBluetooth) ou desligado.

Otimizacao e testes: medir antes (tempo de abrir, custo do quadro, voltas
por segundo do interpretador) e um comando unico de verificacao (compila,
suite, projetos, idiomas) para os agentes rodarem.

Instalador: so na Alpha 26, com tudo funcionando.
