# TAREFA-004 — Comportamento de cada peça: entradas e sensores

Leia o `AGENTS.md` antes. Trabalhe na sua pasta
(`C:\Users\seraf\CircuitSketch-agentes\chatgpt`). Antes de começar, a sua pasta
precisa estar com o `main` mais novo: o Claude ja atualizou.

## Contexto

O Claude criou a varredura `app/AutotesteVarredura.cs` (leia inteira): cada
pino de cada placa e cada peça ligada pino a pino. Ela confere que tudo LIGA e
nao trava. Falta conferir que cada peça FAZ o que a de verdade faz. Esta tarefa
e o primeiro lote: as categorias **Entrada** e **Sensores** do
`Catalogo.Categorizar` (`app/Catalogo.cs`):

- Entrada: pushbutton, pushbutton-6mm, potentiometer, slide-potentiometer,
  slide-switch, dip-switch-8, membrane-keypad, analog-joystick, ky-040,
  rotary-dialer, tilt-switch, micro-switch.
- Sensores: hc-sr04, dht22, dht11, photoresistor-sensor,
  ntc-temperature-sensor, pir-motion-sensor, flame-sensor, gas-sensor,
  heart-beat-sensor, big-sound-sensor, small-sound-sensor, mpu6050, hx711.

## O que fazer

Criar `app/AutotesteComportamentoEntradas.cs` com o bloco
`Comportamento_EntradasESensores`. Para CADA peça da lista:

1. Monte a ligação de verdade (a do datasheet/tutorial): a peça numa
   `esp32-devkit-v1` com `Usb = true` (ou num `arduino-uno`, se a peça e de
   5 V), VCC/GND e o sinal num GPIO. Use os nomes de pino do catálogo
   (`Catalogo.Obter(tipo).Pinos`).
2. Mexa no controle da peça em 2 ou 3 posições (`peca.Giro`, `Giro2`, `Bits`,
   `ed.BotoesPressionados`, `ed.TeclaMatricialParaTeste(...)`, conforme a
   peça — veja como o `EditorCircuito.AplicarEntradas` e
   `app/Comportamentos.cs` tratam cada uma) e rode o quadro com
   `ed.AplicarEntradas(Bancada.DeUmaSo(maq), rede)` (veja os testes prontos em
   `AutotesteVarredura.cs`, `AutotesteModulosDeRele.cs` e `Autoteste.cs`).
3. Confira o que a placa ve: `maq.DigitalRead`, `maq.AnalogRead`,
   `maq.Leitura("...")`, `maq.Leituras`, o pulso do sonar, etc. — com o valor
   que a peça de verdade daria (ex.: potenciometro em 25% → ~25% do ADC;
   botao apertado com INPUT_PULLUP → LOW; HC-SR04 a 100 cm → ~5800 us de eco;
   DHT22 a 30 °C → a leitura de temperatura 30).
4. Um `Conferir(...)` por (peça, situação), com o nome no formato
   `"<tipo>: <situacao> -> <esperado>"` e o `visto` com o valor lido.
5. Registre o bloco com uma linha `Bloco("Comportamento_EntradasESensores", Comportamento_EntradasESensores);`
   em `app/Autoteste.cs`, logo depois da linha do `SemFio_UmAUm`.

**Se o simulador fizer diferente da peça de verdade, NAO conserte o
simulador.** Deixe o teste com o valor CERTO (o da peça real), deixe-o
falhar, e descreva no relatório: a peça, o que a real faz (com a fonte: o
datasheet ou a biblioteca), o que o simulador fez. O Claude corrige o
simulador.

## Arquivos que voce pode criar/alterar

- criar `app/AutotesteComportamentoEntradas.cs`
- `app/Autoteste.cs`: so a linha do `Bloco(...)`
- `coordenacao/chatgpt/RELATORIO-004.md` e `coordenacao/chatgpt/saida-004*.txt`

Nada mais (nem `app/` fora destes, nem `motor/`, nem desenho).

## Como conferir

```
dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/chatgpt
.artefatos\chatgpt\bin\App\debug\CircuitSketch.exe --teste --so Comportamento_EntradasESensores --saida coordenacao\chatgpt\saida-004.txt
```

Relatorio: a ultima linha ("N passaram, M falharam"), as linhas FALHA, e para
cada falha se e erro do teste (consertado) ou diferenca do simulador (com a
explicacao). Seja enxuto: o seu limite de uso e curto.
