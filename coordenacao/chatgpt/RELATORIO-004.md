# RELATORIO-004 — Entradas e sensores

## Arquivos

- `app/AutotesteComportamentoEntradas.cs`: criado `Comportamento_EntradasESensores`, com 82 conferências para as 25 peças. USB, alimentação, terra e sinais; contatos no GND/pull-up; DHT e I2C com resistores de 4,7 kohm. Pinos validados pelo catálogo. Expectativas independentes de `Comportamento.Leitura/ValorReal`.
- `app/Autoteste.cs`: somente o registro depois de `SemFio_UmAUm`.
- `saida-004.txt`: resultado final; `saida-004-primeira.txt`: primeira execução preservada; `saida-004-build.txt`: última compilação; este relatório.

## Comandos e resultados

- `dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/chatgpt` (log via `Tee-Object`): duas compilações com **0 avisos, 0 erros**, em 28,49 s e 30,74 s.
- `.artefatos/chatgpt/bin/App/debug/CircuitSketch.exe --teste --so Comportamento_EntradasESensores --saida coordenacao/chatgpt/saida-004.txt`: primeira execução **64 passaram, 14 falharam**. Dois erros do teste: `InvalidOperationException: Pino inexistente: dip-switch-8.4a / esp32-devkit-v1.D16` e `InvalidOperationException: Pino inexistente: membrane-keypad.R3 / esp32-devkit-v1.D16`. Corrigidos: helper resolve o nome pelo número no catálogo (RX2/TX2), em vez de fabricar D16/D17. As seis situações de DIP/teclado passaram depois.
- Mesmo executável/argumentos, repetidos com `Start-Process -WindowStyle Hidden -PassThru -Wait`: **70 passaram, 12 falharam**, código de saída **1**. Suite inteira não executada.
- `git diff --check`: sem erros. `git diff --stat`, `git status --short`, `Get-Content`, `rg` e `ConvertFrom-Json` usados para leitura e conferência. Duas buscas com glob no argumento do `rg` deram `os error 123`; refeitas com `-g`. Uma leitura antecipada do log deu `PathNotFound`; lido após terminar a compilação. A primeira tentativa de gravar este relatório por comando PowerShell foi bloqueada pela política da ferramenta; substituída por `apply_patch`.

## Falhas finais — diferenças do simulador mantidas para o Claude

- **slide-switch (3)**: 2 é comum; esquerda fecha 1–2, direita fecha 2–3. Com 1 no GND e 3 em 3V3, deveria ler 0/1/0; leu 1/0/1. `AplicarEntradas` usa `Pressionado` conforme Giro, sem transportar o nível dos contatos. Fonte: [SPDT, pinagem e funcionamento](https://docs.wokwi.com/parts/wokwi-slide-switch).
- **flame-sensor (3)**: referência KY-026 da Joy-IT descreve HIGH ao detectar chama; esperado abaixo/acima/abaixo = 0/1/0, observado 1/0/1. Fonte: [KY-026, User note e código Arduino](https://sensorkit.joy-it.net/en/sensors/ky-026).
- **big-sound-sensor (3)**: KY-037 da Joy-IT define HIGH acima do limiar; esperado 0/1/0, observado 1/0/1. Fonte: [KY-037](https://sensorkit.joy-it.net/en/sensors/ky-037).
- **small-sound-sensor (3)**: mesma divergência para o KY-038. Fonte: [KY-038](https://sensorkit.joy-it.net/en/sensors/ky-038).

Os três sensores usam `DigitalInvertido = true` no simulador. Existem variantes comerciais com outra polaridade: confirmar a variante pretendida pelo catálogo antes de alterar a produção. As expectativas identificam a referência Joy-IT adotada, sem afirmar que todo módulo genérico tem essa polaridade. Nenhuma correção no simulador foi feita.

Linhas exatas finais:

```text
  FALHA slide-switch: esquerda, comum no GND -> 0  1
  FALHA slide-switch: direita, comum no 3V3 -> 1  0
  FALHA slide-switch: esquerda, comum no GND -> 0  1
  FALHA flame-sensor: estimulo abaixo do limiar (DOUT; AOUT=102) -> 0  1
  FALHA flame-sensor: estimulo acima do limiar (DOUT; AOUT=921) -> 1  0
  FALHA flame-sensor: estimulo abaixo do limiar (DOUT; AOUT=102) -> 0  1
  FALHA big-sound-sensor: estimulo abaixo do limiar (DOUT; AOUT=102) -> 0  1
  FALHA big-sound-sensor: estimulo acima do limiar (DOUT; AOUT=921) -> 1  0
  FALHA big-sound-sensor: estimulo abaixo do limiar (DOUT; AOUT=102) -> 0  1
  FALHA small-sound-sensor: estimulo abaixo do limiar (DOUT; AOUT=102) -> 0  1
  FALHA small-sound-sensor: estimulo acima do limiar (DOUT; AOUT=921) -> 1  0
  FALHA small-sound-sensor: estimulo abaixo do limiar (DOUT; AOUT=102) -> 0  1
70 passaram, 12 falharam
```

## Cobertura e limites

Botões: solto/apertado/solto. Potenciômetros: 25/50/75%. DIP: oito canais em 0x00/0x55/0xAA. Teclado: 1/D/solto e GPIOs de linha/coluna. Joystick: dois eixos e botão. Encoder: ciclo Gray, retorno e SW. Disco: 1/3/0 (zero = dez pulsos). Micro-switch: NO/NC e retorno. Inclinação: mudança de nível e retorno, sem fixar orientação mecânica absoluta. Demais sensores: três valores por peça.

Referências quantitativas: [NTC beta 3950](https://docs.wokwi.com/parts/wokwi-ntc-temperature-sensor), [LDR RL10/gamma](https://docs.wokwi.com/parts/wokwi-photoresistor-sensor), [HC-SR04, 58 us/cm](https://docs.wokwi.com/parts/wokwi-hc-sr04), [MQ2, DOUT LOW](https://docs.wokwi.com/parts/wokwi-gas-sensor), [MPU6050, 16384 LSB/g em ±2g](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6000-Datasheet.pdf), [HX711 e calibração](https://docs.wokwi.com/parts/wokwi-hx711), [KY-040](https://docs.wokwi.com/parts/wokwi-ky-040), [KY-020](https://sensorkit.joy-it.net/en/sensors/ky-020).

São testes de quadro e das grandezas entregues às bibliotecas, não de todo protocolo elétrico. DHT testa temperatura/umidade; MPU testa X; HX711 testa peso calibrado. Não cobrem checksum, clock serial, demais eixos, tara, debounce, retenção do PIR ou aquecimento do MQ2. Sonar confere eco após TRIG, sem validar o mínimo de 10 us. Som/chama/gás conferem DOUT; AOUT é registrado, sem conversão arbitrária para dB/ppm. O [KY-039](https://www.joy-it.net/en/products/SEN-KY039HS) tem saída analógica: o teste injeta três amostras de tensão pelo controle e confere o ADC, sem comprovar geração fisiológica de batimentos/BPM. A relação entre controle e amostras é convenção do simulador, não calibração clínica.

Estado inicial já continha `TAREFA-004.md`, `execucao-004.log` e `execucao-004.log.err` não rastreados; não foram editados. Nenhuma alteração de Git, desenho, motor ou tradução; nenhum download/instalação, acesso ao E: ou encerramento de processo do usuário.
