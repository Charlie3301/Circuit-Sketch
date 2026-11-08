# Report_Nemotron — CircuitSketch: análise de bugs, vulnerabilidades, erros, falhas e itens incompletos

**Produto:** CircuitSketch — simulador offline de ESP32 / Arduino (C#/WPF + motor em C#)  
**Árvore:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32`  
**Data:** 16 de outubro de 2026  
**Autor:** agente de análise (este relatório)  
**Escopo:** revisão estática de `app/`, `motor/`, `ferramentas/`, `plataforma/`, `coordenacao/` e artefatos de teste conhecidos.

---

## 1. Resumo executivo

O CircuitSketch é um simulador grande e deliberado: interpretador árvore (tree-walking), modo C nativo via TinyCC (libtcc.dll) in-process, solucionador de energia DC (lumped), verificador de queima (`Queima`), bancada WPF, hook da Arduino IDE, broker MQTT interno, HTTP/TCP/MQTT/WiFi/Bluetooth simulados, múltiplas linguagens (C/Arduino, C++, MicroPython, Rust, Pascal) e suporte a Wokwi. Há uma bateria de testes extensa (`Autoteste.cs` + 38 projetos de teste).

Os problemas agrupam-se em quatro áreas:

| Área | O que foi encontrado |
|------|----------------------|
| **Fidelidade de hardware** | 11 de 38 projetos de teste falham (slide switch, MPU6050, HX711, IR/pino 3, ESP-NOW segundo pacote, servo do carrinho RC, rovers, L298N PWM, fan). |
| **Segurança / exposição do host** | Código C nativo roda como threads x64 do processo; HTTP de sketch usa internet do PC por padrão; named pipe aceita qualquer caminho de processo local; `HttpClient` da janela de bibliotecas online segue redirecionamentos. |
| **Incompletude de linguagem/API** | ~40+ lançamentos de "ainda não é suportado pelo simulador" espalhados por Serial, String, SD, HTTP, WiFi, server/client, BLE (quase tudo), I2C slave, EEPROM, Stepper, Keypad, TFT, MicroPython `async`/`urequests`/muitos `machine.*`/`network.*`, etc. |
| **Docs / roadmap defasados** | `README.md` ainda diz que BLE, `configTime` e interrupções faltam; `PLANO-ALPHAS.md` diz que o relé de 1 canal falta no catálogo (ele já existe). |

**Resultado da bateria de projetos (13/10/2026, `coordenacao/resultados-projetos.json`): 27 passaram, 11 falharam.**

---

## 2. Visão geral do projeto (contexto)

- `app/` — interface WPF, editor de circuito, editor de código, janelas, energia, queima, catálogo, autotestes (~159 arquivos .cs).
- `motor/` — interpretador, compilador, lexer/parser (C, Python, Pascal, Rust), placas AVR/ESP32/Pico/STM32, rede (WiFi/HTTP/TCP/MQTT/Bluetooth/BLE/ESP-NOW), modo nativo (TinyCC), EEPROM, SD, motores, servos, OLED/TFT, áudio (~78 arquivos .cs).
- `motor/Nativo/` — runtime C nativo, TCC, host calls, arquivos simulados, SPI, áudio, rede nativa.
- `ferramentas/` — geradores de bateria de testes e projetos (Python), instalador, extração de SVG, idiomas.
- `coordenacao/` — planos (PLANO-ALPHAS.md), relatórios de agentes (chatgpt, antigravity), resultados de projetos, verificações.
- `bateria/` — 38 projetos de teste gerados por `ferramentas/projetos.py`.

---

## 3. Bugs de hardware e incompatibilidades com a placa real (P1)

Estes são os que fazem o simulador discordar da placa física e/ou fazem projetos de teste falharem.

### 3.1 Slide switch não é um SPDT (código + teste falhando)

**O que acontece:** num módulo slide-switch real (Wokwi), o pino 2 é o comum; esquerda fecha 1–2, direita fecha 2–3. O modelo de **Energia** já modela isso corretamente (`app/EditorMotores.cs:53`, contato entre "2" e "1" ou "3"). Porém, o caminho de entrada por frame trata os pinos 1, 2 e 3 como três digitais independentes movidos por `Giro > 0.5` (`app/Comportamentos.cs:161`, `app/EditorCircuito.cs:5989-5998`). O pino comum nunca herda GND/3V3 dos contatos; os caminhos elétrico e GPIO discordam.

**Evidência:** `AutotesteComportamentoEntradas.cs` espera 0 / 1 / 0 para esquerda-direita-esquerda com 1=GND e 3=3V3. O relatório do ChatGPT registrou esse bloco como 70 passou, 12 falhou, incluindo os três casos do slide switch (`coordenacao/chatgpt/RELATORIO-004.md`).

**Correção esperada:** pular `Digitais` para `slide-switch` (e qualquer contato verdadeiro) e deixar `LevarNivel` / contatos de energia darem o nível ao GPIO, igual ao `micro-switch` já faz.

### 3.2 MPU6050 (Adafruit) — projetos não imprimem nada

**Projetos afetados:** `drone-quadricoptero`, `nivel-mpu6050` (ambos `funcionou: false` no JSON de resultados).

- `drone-quadricoptero`: monitor vazio; motores a 0 rpm / 0 gf.
- `nivel-mpu6050`: monitor vazio; LED fica apagado quando inclinado.

`mpu.begin()` está implementado (WHO_AM_I vs DS1307 em 0x68). `getEvent(&a,&g,&temp)` está implementado e converte LSB em m/s². O catálogo tem a grandeza apenas `mpu:ax`. Se `begin()` falhar (rede I2C errada, biblioteca C++ que o interpretador não parseia, ou `if (!mpu.begin()) return;`), o setup nunca imprime — o que combina com o monitor vazio.

`getEvent` também exige que o argumento após `&` seja uma variável simples (`EventoDoMpu` em `motor/Interpretador.cs`). Um membro ou temporário preenche o evento silenciosamente sem efeito.

### 3.3 HX711 — offset de célula vazia (~201 g)

**Projeto:** `balanca-hx711-lcd` (`funcionou: false`).

- 500 g → LCD mostra **701 g**.
- 1250 g → LCD mostra **1451 g**.
- Diferença constante de +201 g.

`motor/Interpretador.cs:3713-3719`:
```csharp
long ContagemDoHx711() => (long)Math.Round(84213 + maq.Leitura("hx711:peso") * 420);
```
`84213 / 420 ≈ 200,5`. Esse offset só é removido se o sketch der `tare()` na célula vazia. Se o roteiro já tem massa na célula quando `tare()` roda, ou nunca tares, toda leitura fica alta em ~201 g. O número combina com a fórmula, não com um erro aleatório de ADC.

### 3.4 Controle remoto IR — "queima" do pino 3 do Uno (falso-positivo da Queima)

**Projeto:** `controle-ir-leds` (`funcionou: false`).
```
Arduino Uno queimou: curto: o pino 3 está em LOW e ligado direto na alimentação
```
O IRremote no AVR usa o Timer2, que é dono do **pino 3** (OC2B) mesmo em sketches só de recepção. A `Queima` trata "GPIO LOW amarrado a um trilho de alimentação" como curto-circuito mortal. Num Uno real esse pino é uma saída de timer, não um LOW preso ao VCC. Resultado: o aluno vê uma queima que não aconteceria na bancada, e os LEDs nunca acendem no teste.

`Queima.cs` é honesto ao ser uma heurística de netlist, não um solucionador de corrente (`app/Queima.cs` cabeçalho). Este é precisamente o falso-positivo que ela foi feita para pegar — e ainda dispara em uma biblioteca comum.

### 3.5 ESP-NOW — pressiona funciona, soltar não

**Projeto:** `espnow-botao-led` (`funcionou: false`).
- "recebi: apertado" e LED acende; **soltar deixa o LED aceso**.
- O primeiro pacote é entregue; o segundo (botão solto) não é aplicado.

Provavelmente timing de callback/fila em `motor/RadioNaRede.cs` + `Interpretador.cs` (callbacks rodam depois da chamada, não entre pulsos — também documentado no README para `shiftOut`).

### 3.6 Carrinho RC — ESC segue o rádio, servo não

**Projetos:** `carrinho-rc-sem-placa`, `carrinho-rc-arduino` (ambos `funcionou: false`).
- `carrinho-rc-sem-placa`: acelerador → 8828 rpm (OK); direção fica em **93°** para esquerda e direita.
- `carrinho-rc-arduino`: reporta "sem sinal do rádio" mesmo com o receptor no Nano **5V.2**.

O trilho de cobre interno para `5V` / `5V.2` **é** intencional (`app/Catalogo.cs:394-397`). Suspeitas restantes: captura de pulso naquele GPIO, modelo do receptor, ou o sketch do Nano nunca vê `pulseIn`.

### 3.7 Rovers (P1)

| Projeto | Sintoma |
|---------|---------|
| `rover-4wd` | "re" cai dentro de "frente" (substring, tipo A). Motores também não inverteram para ré (tipo B). |
| `rover-4wd-hc-sr04` | PWM 200 num enable vs total no outro não dividiu a velocidade como esperado. Possível: PWM não chega no ENB em `EnergiaPwm`. |
| `rover-4wd-reles` | O gerador ainda usa bobinas KS2E nuas de GPIO a 5 V porque acha que o relé de 1 canal não existe (`ferramentas/projetos.py ~684`). O catálogo **tem** `modulo-rele-1`. Corrente da bobina ~70 mA vs 40 mA do Uno, e "frente sai ao contrário", vêm desse circuito desatualizado, não do novo módulo. |

### 3.8 Ventilador + L298N (`ventilador-l298n`) (`funcionou: false`)
- Pot baixo → 0 rpm; pot alto → 4453 rpm. PWM baixo é tratado como desligado em vez de lento. Possíveis causas: jumper de enable vs limiar de PWM, ou `analogWrite` não chegando no ENA em pequeno duty.

### 3.9 Timing de interrupção vs silício real (documentado)
> "Edge ISRs rodam depois da chamada que produziu as bordas, não entre pulsos. Um `shiftOut` dispara oito bordas, e a ISR vê o **estado final** do pino." (`README` / auditoria anterior)

Isso quebra protocolos bit-bang que amostram dentro da ISR.

### 3.10 Flame / gas — polaridade vs datasheet do kit (escolha de produto, armadilha de lab)
Módulos de som (KY-037/KY-038) foram alinhados ao Joy-IT (`DigitalInvertido = false`, HIGH acima do limiar). Flame e gas ficam ativo-BAIXO (`DigitalInvertido = true`), com comentário de que modelam placas LM393 genéricas, não KY-026. Joy-IT KY-026 é HIGH com chama. Um aluno copiando o datasheet inverte todo `if (digitalRead)`. O autotest **espera** a polaridade invertida, então a suite fica verde enquanto o kit da gaveta discorda.

---

## 4. Segurança e exposição do host

Contexto de sala de aula: a ameaça é um sketch do aluno, um `.zip` de biblioteca baixado, ou outro usuário no mesmo PC — não um atacante remoto anônimo. Os padrões ainda importam.

### 4.1 Código C nativo roda in-process como x64 (P0 se C não confiável estiver em escopo)

`motor/Nativo/ModuloNativo.cs` compila `.c` do aluno com TinyCC (`libtcc.dll`) **em memória** e executa em threads do host (uma tarefa FreeRTOS = uma thread do SO). Mitigações reais:
- `-nostdinc -nostdlib`, `#undef _WIN32` / `_WIN64` para bibliotecas não tomarem o caminho do Windows.
- E/S de arquivo é um VFS sobre o SD/simulado (`NativoArquivos.cs`), não `CreateFile` no PC.
- SEH / trampoline de exceção visa transformar ponteiros ruins em "Guru Meditation" em vez de matar o WPF.
- Chamadas ao host vão por uma tabela fixa `cs_host` (`HostNativo.cs`).

O que resta: ainda é **código nativo no processo do CircuitSketch**. Um `#include` de um header do Windows vazado, syscall inline, ou bug de codegen do TCC é uma fuga de sandbox a nível de processo. A história WASM do Wokwi é mais forte aqui. Para um laboratório fechado onde todo `.c` é do próprio aluno, é mais risco de integridade/trava do que RCE remoto.

O TCC tem estado global grudento; uma compilação falhada **recarrega a DLL** (`motor/Nativo/Tcc.cs`). Isso é uma correção de confiabilidade, não de segurança.

Arquivos envolvidos: `motor/Nativo/Tcc.cs`, `ModuloNativo.cs`, `HostNativo.cs`, `NativoArquivos.cs`, `NativoRede.cs`, `CpuNativa.cs` (tratador de exceção Windows).

### 4.2 HTTP de sketch usa a internet do computador (P1, ligado por padrão)

`motor/Rede.cs:63`:
```csharp
public bool UsarInternet { get; set; } = true;
```
Após roteamento bancada-a-bancada, `HTTPClient` do ESP32 vira um `System.Net.Http.HttpClient` real para URLs `http`/`https` arbitrárias (`Rede.Requisitar`). Há um toggle de menu ("Rede usa a internet do computador") e `setInsecure()` **desativa verificação de certificado TLS** (`ClienteInseguro`, `motor/Rede.cs:399-406`).

Impacto na escola: um sketch pode exfiltrar arquivos que o aluno colou num payload, atingir URLs da intranet, ou ignorar autenticidade HTTPS. Desligado por padrão combinaria com a proposta de "simulador offline" do `README.md`.

### 4.3 Janela de bibliotecas online: `HttpClient` segue redirecionamentos (P2)

`BibliotecasOnline.ConferirUrl` permite apenas `https://downloads.arduino.cc/` (porta default, sem userinfo), checa tamanho e **SHA-256**, e re-checa a URL **final** após a resposta. A extração de zip em `Acervo.Extrair` rejeita caminhos que saem do destino e limita o tamanho não compactado a 200 MB. O autotest instala um zip `../../fora.h` e espera recusa (`app/AutotesteCpp.cs`).

**Gap:** `JanelaBibliotecas` constrói um `HttpClient` default **com redirecionamentos habilitados**. Hosts intermediários numa cadeia 30x ainda veem a requisição; só a URL final é presa. `RELATORIO-003.md` já sinaliza `AllowAutoRedirect = false` como dever do chamador. `Rede` desativa redirecionamentos; a janela de bibliotecas não.

### 4.4 Named pipe carrega qualquer caminho que o remetente escrever (P2)

A segunda instância envia o caminho do sketch para `CircuitSketch_Canal`; a primeira chama `CarregarSketch` (`app/App.xaml.cs:621-669`). Sem auth, sem ACL. Num PC de laboratório compartilhado, outro processo local pode empurrar um caminho na janela aberta. Baixa sofisticação, apenas local.

Arquivo: `app/App.xaml.cs:621-669` (`EntregarParaInstanciaAberta`, `Escutar`). Pipe nomeado `CircuitSketch_Canal`, mutex `CircuitSketch_Mutex`.

### 4.5 Rádio do PC (`netsh` / PowerShell) (P2)

`RadioDoComputador` roda `netsh` e `Get-PnpDevice` para listar SSIDs reais. Pode ser desligado nas configurações e é forçado off no Autoteste. Quando ligado, o app aprende os nomes de WiFi da sala (privacidade) e gera processos do SO. Argumentos são fixos (sem shell controlado por sketch). `AGENTS.md` proíbe agentes de ligar Bluetooth; o próprio produto ainda pode consultá-lo.

### 4.6 O que parece assustador e está contido

| Tema | Status |
|------|--------|
| Zip-slip | Guard + teste unitário (`Acervo.Extrair`, `app/AutotesteCpp.cs`) |
| Download de biblioteca Arduino | Allow-list + SHA-256 |
| SD / `fopen` no modo nativo | Cartão simulado, não disco do host |
| Carregamento `.sktch` | `System.Text.Json` (não BinaryFormatter) |
| `catch { }` vazio | Maioria é UI `Owner`/`Close`/kill-timeout; `EntregarParaInstanciaAberta` engole erros de IPC |

---

## 5. Incompletude de linguagem e APIs (P2)

O interpretador **recusa** chamadas não suportadas com número de linha. Isso é melhor que no-ops silenciosos (o antigo bug de `attachInterrupt`). Ainda assim, um sketch aleatório do GitHub morre cedo.

### 5.1 Exemplos de lançamentos "ainda não é suportado" (contagem: ~40+ locais)

Foram encontrados 40+ ocorrências da string `"ainda não é suportado pelo simulador"` (ou variantes) em `motor/`. Principais:

| Área / Arquivo | O que falta |
|----------------|-------------|
| `motor/InterpretadorRede.cs` | `WiFi.*`, `ESP.*`, `IPAddress.*`, `http.*` (inclui `http.getStream()` — workaround: `getString()`), `server.*`, `client.*`, `SerialBT.*` |
| `motor/Interpretador.cs` | `SD.*`, `IrReceiver.*`, `String.*`, `Serial.*` |
| `motor/InterpretadorBle.cs` / `InterpretadorBleCliente.cs` / `InterpretadorArduinoBle.cs` | Quase toda a API BLE: `BLEDevice`, `BLEScan`, `BLEScanResults`, `BLEAdvertisedDevice`, `BLEClient`, `BLERemoteService`, `BLERemoteCharacteristic`, `BLEDescriptor`, `BLEService` |
| `motor/InterpretadorI2c.cs` | Modo escravo I2C |
| `motor/InterpretadorEeprom.cs` | Parte da API `EEPROM` |
| `motor/InterpretadorMotorDePasso.cs` | `Stepper.*` |
| `motor/InterpretadorTeclado.cs` | `Keypad.*` |
| `motor/InterpretadorTft.cs` | Fontes customizadas Adafruit_GFX (`FreeSans…`) |
| `motor/InterpretadorMicroPython.cs` | `machine.*`, `time.*`, `network.*`, `urequests`, `async`/`await` |
| `motor/InterpretadorMicroPythonPerifericos.cs` | `I2C.*`, `SSD1306.*`, `UART.*` (métodos parciais) |
| `motor/InterpretadorMicroPythonTempo.cs` | `Timer.*` (só init/deinit/value), `RTC.*` (só datetime/init), `SPI.*` |
| `motor/InterpretadorPecasPython.cs` | `WLAN.*`, `client.*` |
| `motor/InterpretadorSpi.cs` | Parte da API SPI |
| `motor/InterpretadorTelegram.cs` | `bot.*`, `preferences.*` |
| `motor/InterpretadorClasses.cs` | Classes aninhadas dentro de funções, construtor recursivo |
| `motor/ParserPython.cs` | Parâmetros `*args` / `**kwargs` |
| `motor/InterpretadorCiclos.cs` | `wdt_enable()` só em AVR |
| `motor/InterpretadorChip.cs` / `InterpretadorRadio.cs` | Partes de chip e rádio NRF24 |

Arquivo completo de stubs: ver resultado da busca anterior (~40 matches). Cada um lança `ErroDeExecucao` com número de linha, o que é honesto, mas deixa o simulador incompleto para "soltar qualquer sketch do GitHub".

### 5.2 Outras linguagens (Rust, Pascal)
Existem lexer/parser para **Rust** (`motor/LexerRust.cs`, `motor/ParserRust.cs`) e **Pascal** (`motor/LexerPascal.cs`, `motor/ParserPascal.cs`). Não têm cobertura de Arduino/MicroPython. Espera "não suportado" rapidamente.

### 5.3 Depurador (ainda preciso, README ainda é preciso)
- Não consegue alterar valores.
- Não consegue passo-sobre-funções.
- Breakpoints não acompanham linhas inseridas acima deles.

---

## 6. Limites do modelo elétrico (P2)

- `Energia` é uma rede **DC lumpada** (e PWM médio) com passes extras para pontes H, reguladores e USB-less de placas. Capacitores recebem um passo de tempo via `EnergiaTransitoria`; não é SPICE.
- `Queima` é uma **heurística de conectividade** (curtos, LED sem resistor, motor num GPIO). Tanto deixa passar danos reais quanto flagra pinos de timer/IR legais (ver 3.4).
- O core AVR documenta o que **não** modela: tempo de subida do pino, ruído, corrente analógica (`motor/Avr.cs`). Contagens de ciclos da ISA são levadas a sério; física analógica não.
- PWM em GPIO é médio sobre o período (`Energia.Calcular` → `EnergiaPwm`). Protocolos digitais rápidos que dependem da forma exata de onda alta/baixa de um enable de motor parecem uma tensão DC.

---

## 7. Resultados dos testes de projeto (evidência concreta)

Fonte: `coordenacao/resultados-projetos.json` (13/10/2026). Total: 38 projetos. **27 passaram, 11 falharam.**

### 7.1 Projetos que FALHARAM (11)

| # | Projeto | Título | Motivo principal (do JSON / auditoria) |
|---|---------|--------|----------------------------------------|
| 1 | `drone-quadricoptero` | Drone quadricóptero (ESP32, ESC, rádio FlySky) | Monitor vazio; motores 0 rpm / 0 gf (MPU6050 não imprime) |
| 2 | `carrinho-rc-sem-placa` | Carrinho RC (sem placa) | Servo fica em 93° para esquerda e direita |
| 3 | `carrinho-rc-arduino` | Carrinho RC com Arduino Nano | "sem sinal do rádio"; motor 0 rpm |
| 4 | `rover-4wd` | Rover 4WD (L298N) | Ré não inverte motores; substring "re" dentro de "frente" |
| 5 | `rover-4wd-hc-sr04` | Rover 4WD com HC-SR04 | Enable PWM não divide velocidade (ENA/ENB); sonar lê 99 cm em vez de 100 |
| 6 | `rover-4wd-reles` | Rover 4WD com relés | Usa KS2E nu em vez de `modulo-rele-1`; frente ao contrário; motores 0 rpm na ré |
| 7 | `controle-ir-leds` | Controle IR (IRremote, Uno) | "Queimou: pino 3 em LOW direto na alimentação" (falso-positivo da Queima) |
| 8 | `nivel-mpu6050` | Nível eletrônico MPU6050 | Monitor vazio; LED não acende ao inclinar |
| 9 | `ventilador-l298n` | Ventilador com L298N | Pot baixo → 0 rpm (PWM baixo tratado como off) |
| 10 | `balanca-hx711-lcd` | Balança HX711 + LCD | Offset +201 g (701 g em vez de 500; 1451 g em vez de 1250) |
| 11 | `espnow-botao-led` | Botão → LED por ESP-NOW | Segundo pacote (soltar) não apaga o LED |

### 7.2 Projetos que PASSARAM (27)
`fonte-7805-uno`, `7805-esquenta-com-servos`, `lm2596-esp32-medidor`, `mt3608-18650-uno`, `ams1117-lipo-1s-esp32`, `semaforo`, `semaforo-pedestre`, `botao-liga-desliga-esp32`, `dimmer-potenciometro`, `servo-potenciometro`, `sensor-de-re`, `termometro-dht22-lcd`, `alarme-pir`, `luz-noturna-ldr`, `contador-7-segmentos`, `relogio-ds1307-lcd`, `fechadura-teclado-servo`, `joystick-pan-tilt`, `anel-neopixel-esp32`, `oled-barra-potenciometro`, `motor-de-passo-28byj48`, `bluetooth-led-esp32`, `servidor-web-led`, `sono-profundo-botao`, `nrf24l01-duas-placas`, `lcd-paralelo`, `pico-micropython-botao`.

---

## 8. Glitches e armadilhas de UX

1. **Mutex / named pipe de instância única:** um segundo Verify da IDE envia o sketch pelo pipe. Se a primeira janela está num diálogo ou teste, o aluno acha que o Verify não fez nada (`catch` engole erros de IPC em `EntregarParaInstanciaAberta`).
2. **Fios quebrados costumavam sumir**; `FiosQuebrados()` agora avisa. Abrir projetos antigos com `D16` vs `RX2` ainda precisa desse banner.
3. **IR NEC:** segurar uma tecla costumava retriggerar a cada frame; agora é um código + repetição 0xFFFFFFFF. Sketches antigos que pesquisam a cada loop podem mudar o comportamento.
4. **Duas compilações na mesma pasta quebram o build** (`AGENTS.md`) — risco de agente/processo, não do aluno.
5. **`python ferramentas/bateria.py` sem argumento grava no `E:`** (originais do professor) — pegada operacional.
6. **Globais do TCC:** um erro de compilação envenena a próxima compilação a menos que a DLL seja recarregada (já codificado).
7. **Tela de abertura:** exceções são registradas em arquivo e não impedem o app de abrir (`app/App.xaml.cs:614-618`).

---

## 9. Documentação e roadmap defasados (P3)

| Item | Evidência |
|------|-----------|
| Interface só Windows | WPF; `app/Circuito.cs` usa `System.Windows.Point`. Linux/macOS = Alpha 26 (`PLANO-ALPHAS.md`) |
| Tratamento de exceção nativo | SEH do Windows; sinais POSIX não implementados |
| Bluetooth do PC | `PortaBluetooth.cs` Winsock `AF_BTH` |
| Upload da Arduino IDE | Verify funciona via postbuild; Upload precisa de uma ferramenta de serial falsa; a IDE não lista a porta (causa dentro da IDE) |
| 12 idiomas | Catalães incompletos são ocultados do seletor de configurações; Inglês 9/33 num lote (RELATORIO-003) |
| Instalador | Apenas Alpha 26 (`AGENTS.md`, `PLANO-ALPHAS.md`) |
| Relé de 1 canal no catálogo | **Já presente** (`app/assets/pecas.json`, `modulo-rele-1`). `PLANO-ALPHAS.md` e comentários de `projetos.py` ainda dizem que falta |
| PCM5102A / MAX98357A | Implementados e testados (`AutotesteAudioEstereo`, player DAP). Listados como "próximos projetos" do plano Alpha 24 |
| README diz BLE não simulado | Código e `AutotesteBle*.cs` existem; README está desatualizado (`motor/InterpretadorBle.cs` etc.) |
| README diz `configTime` e interrupções faltam | `attachInterrupt` já existe; `configTime` possivelmente também. README desatualizado |
| README diz `http.getStream()` falta | Ainda falta; workaround documentado é `getString()` |

---

## 10. Dívida de teste

| Fonte | Resultado | Confiabilidade |
|-------|-----------|----------------|
| `coordenacao/resultados-projetos.json` (13/10/2026) | 11/38 projetos falharam | Alta para esta árvore |
| `coordenacao/chatgpt/RELATORIO-004.md` | `Comportamento_EntradasESensores`: 70/12; slide switch + polaridade flame/som | Testes de flame/som foram reescritos para bater com o simulador; slide switch não |
| `coordenacao/chatgpt/RELATORIO-003.md` | Testes de biblioteca online 81/0 com HTTP falso; `idiomas --conferir` falhou | Alta |
| `coordenacao/antigravity/RELATORIO-002.md` | 46/46 em **outra worktree** após regenerar `projetos/` | Não é o status desta pasta |
| Suite completa `CircuitSketch.exe --teste` | Não re-executada aqui | — |

`Autoteste.cs` é enorme (16k+ linhas) mais muitos partials `Autoteste*.cs`. Cobertura é um ponto forte. As lacunas restantes são de **fidelidade** (a bancada bate com o datasheet?), não de "botões não testados".

---

## 11. Ordem sugerida de trabalho

1. **Slide switch** — parar de dirigir pinos 1/2/3 como digitais falsos; reutilizar o caminho de contato (`P1`, pequeno, teste já escrito).
2. **Caminho Adafruit MPU6050** — fazer `drone-quadricoptero` / `nivel-mpu6050` imprimir; se `begin()` falhar, mostrá-lo na barra serial (`P1`).
3. **HX711 `tare()` vs célula já carregada** em `balanca-hx711-lcd` (`P1`).
4. **Queima vs Timer2 / pino 3** no Uno com IRremote (`P1`).
5. **ESP-NOW segundo pacote** (`P1`).
6. **Canal do servo do carrinho RC** e receptor Nano (`P1`).
7. **Padrão `UsarInternet = false`** para imagem escolar (`P1` de política).
8. **`HttpClient` em `JanelaBibliotecas`:** `AllowAutoRedirect = false` (`P2`).
9. **Atualizar README / PLANO-ALPHAS** para que BLE, interrupções, `configTime`, módulo relé, PCM5102A batam com a árvore (`P3`, barato).
10. **Regenerar rover-reles** com `modulo-rele-1` em vez de KS2E guiado por GPIO (`P2`).

---

## 12. Limites deste relatório

Este relatório é uma **auditoria de código e artefatos**. Não afirma que todo bloco do Autoteste está verde hoje, e não faz fuzz do TCC nem do cliente HTTP. Números de falhas de projetos são de 13/10/2026 salvo indicação em contrário. As polaridades de flame/gas são uma decisão de produto, não exceção não capturada.

---

*Fim do relatório.*
