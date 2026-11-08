# CircuitSketch — auditoria independente (do zero)

**Data:** 16/10/2026
**Árvore:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32`
**Método:** leitura estática de `app/`, `motor/`, `plataforma/`, `ferramentas/`, `nativo/`, sem reaproveitar nenhum relatório anterior e sem rodar a suíte completa (que abre janelas reais).
**Tamanho medido:** 243 `.cs` (156 em `app/` ~69 mil linhas, 87 em `motor/` ~46 mil linhas, total ~115 mil linhas). Maiores: `app/Autoteste.cs` ~16.369, `app/EditorCircuito.cs` ~6.926, `app/MainWindow.xaml.cs` ~6.268, `motor/Interpretador.cs` ~3.737, `motor/Maquina.cs` ~2.113.

**Fluxo verificado:** `app/App.xaml.cs:AoIniciar` → mutex + named pipe → `MainWindow.CarregarSketch` (`app/MainWindow.xaml.cs:3990`) → `Compilador.CompilarProjeto` → `new Interpretador(prog, bancada.De(id))` em thread própria. `app/App.csproj` compila os fontes de `motor/` dentro do app (menos `Program.cs`). `.sktch` é JSON com `codigo` em linhas + `circuito` (`app/Sktch.cs`).

**Severidade usada aqui:**

| Tag | Significado |
|---|---|
| P0 | Compromete o host sem interação ou vaza dados sem ação do usuário |
| P1 | Simulação diverge da bancada, ou padrão inseguro para sala de aula |
| P2 | Falha contida com mensagem, glitch de UX, validação frouxa |
| P3 | Roadmap / plataforma / docs |

Nenhum P0 remoto foi encontrado. Há vários P1 de fidelidade e de padrão.

---

## 1. Vulnerabilidades e exposição do host

### 1.1 C nativo roda em processo, sem sandbox (P1)

- `motor/Nativo/ModuloNativo.cs:157-256` compila `.c` do aluno com TinyCC (`-nostdinc -nostdlib`), includes restritos a `nativo/include`, embrulha cada `.c` com `#undef _WIN32/_WIN64`, reloca para memória via `VirtualAlloc(RW)` e executa no próprio processo. Cada tarefa FreeRTOS vira thread Win32 real (`:496-514`).
- Mitigações reais: sem import de API Win32 por nome (`Tcc.cs:32,96` declara `AdicionarSimbolo` mas nada chama), arquivo vira VFS simulado, tabela `cs_host` é o canal pretendido (`HostNativo.cs:17-122`, ~120 entradas), falha de memória vira "Guru Meditation" via VEH (`:429-431`).
- Limite: ainda é código nativo no processo do WPF. `asm` inline, cast para ponteiro de função ou syscall direta não passam pela tabela `cs_home` nem pela flag `UsarInternet`. Leitura válida de memória do processo não gera exceção, então vaza silencioso. `Forcar()` suspende thread e reescreve RIP via `SetThreadContext`; VEH é process-wide e afeta a UI; `Tcc.cs:139-145` faz patch executável na `libtcc.dll` via `VirtualProtect`.
- Temporários em `%TEMP%\circuitsketch-nativo\{pid}-{guid}` (`:190-198`), apagados best-effort (`:758-762`). `motor/Program.cs:47` permite `--nativo <pasta>` trocar runtime/TCC (só CLI local).

### 1.2 Rede do sketch usa a internet do PC, padrão ligado (P1)

Verificado em `motor/Rede.cs:63`:

```csharp
public bool UsarInternet { get; set; } = true;
```

- `HTTPClient` → `HttpClient` real para URL arbitrária (`motor/Rede.cs:416-514`), `WiFiClient.connect()` → TCP real (`:535-577`, `ConexaoTcp.Real` em `:1473-1485`), MQTT externo real (`:617-755`), Telegram real em `api.telegram.org` (`motor/InterpretadorTelegram.cs:97-107`). Sem allow-list.
- `setInsecure()` desliga validação TLS (`motor/Rede.cs:399-406`, `:1479-1481`, `motor/Mqtt.cs:423-424`). Fiel ao hardware, abre MITM naquela conexão.
- Ponto positivo: `AllowAutoRedirect = false` (`motor/Rede.cs:390-394`), 3xx volta ao sketch sem seguir sozinho.
- `scanNetworks()` mistura SSIDs reais do rádio do PC (`netsh`, `RadioDoComputador.cs:115`) com as simuladas (`motor/Rede.cs:294-319`). Com internet ligada por padrão, o sketch pode exfiltrar nomes do ambiente. Há opt-out em configurações, mas o padrão é ligado (`app/Preferencias.cs:191`, `app/MainWindow.xaml.cs:2983-2984`).
- `MeioDoMac = SHA256(MachineName|UserName)` (`motor/Rede.cs:151-156`), estável por máquina e visível ao sketch (`motor/InterpretadorRede.cs:336-339`).
- Servidor do sketch abre sozinho no navegador real (`app/MainWindow.xaml.cs:5764-5765`, `Process.Start(url, UseShellExecute:true)` em `:5818`). Conteúdo servido é controlado pelo sketch. Ponte é só loopback (`motor/Rede.cs:1351-1358`), sem exposição LAN — verificado.
- Token de bot vai na URL e `Requisitar` loga `"{metodo} {url}"` (`motor/Rede.cs:447,501`): token fica no painel de rede local (só memória, 600 eventos) — P2.

### 1.3 Bibliotecas online: pinagem boa, raiz fraca (P1/P2)

- Positivo: `ConferirUrl` exige `https://downloads.arduino.cc`, porta padrão, sem userinfo (`BibliotecasOnline.cs:234-240`), rechecada após redirect (`:252`), tamanho exato + SHA-256 do índice (`:263-272`), `User-Agent: CircuitSketch` (`app/JanelaBibliotecas.cs:37-42`).
- P1: raiz de confiança é só o TLS do índice (`UrlDoIndice` em `BibliotecasOnline.cs:191`, sem pinning/assinatura). Índice comprometido = código de lib maliciosa, que depois executa no interpretador ou no TCC.
- Positivo: zip-slip barrado por `GetFullPath` + `StartsWith(raiz)` (`app/Acervo.cs:484-509`), teto 200 MB (`:486,504-505`).
- P2: `NomeLimpo` preserva `.` (`app/Acervo.cs:426-444`), então `library.properties` com `name=".."` faz `Combine(Pasta,"..")` = grava no pai de `bibliotecas` (`%APPDATA%\CircuitSketch`), com overwrite (`File.Copy overwrite:true` em `:454`). Vetor: `.zip` instalado à mão. `/` vira `_`, então o escape é de 1 nível.
- P2: `Instalar` copia `*.*` sem teto (`:448-455`, o `.zip` tem teto, pasta local não); download do índice sem teto (`BibliotecasOnline.cs:216-219`).

### 1.4 Named pipe sem ACL + auto-run (P1)

Verificado em `app/App.xaml.cs:21-22`:

```csharp
const string NomeMutex = "CircuitSketch_Mutex";
const string NomeCanal = "CircuitSketch_Canal";
```

Sem `PipeSecurity`, 1 instância (`:643-645`), `ReadLine` sem limite/validação e despacho para `CarregarSketch` na UI (`:637-669`). Qualquer processo do mesmo usuário envia um caminho e `MainWindow.xaml.cs:3990-4024` faz auto-`Rodar()` (`:4022-4023`) com internet ligada por padrão. Também dá para segurar a conexão e bloquear o "Verificar" da IDE (DoS local). Squatting do mutex antes do app faz o app real se comportar como 2ª instância e entregar o caminho ao pipe do atacante (vaza paths) — P2.

### 1.5 `.sktch` mapeia pastas do host sem aviso (P1)

`app/MainWindow.xaml.cs:3190-3205`: `ArquivosDoComputador(nome→caminho absoluto)` e `PastaDoComputador` ativam se `File/Directory.Exists` na máquina da vítima, sem prompt. Arquivo malicioso embute caminhos canônicos (`C:\Windows\...`); ao abrir + Rodar, o sketch lê via SD e exfiltra via HTTP. O JSON em si é seguro (`System.Text.Json`, checa `formato`/`versao` em `app/Sktch.cs`).

Contido e verificado: SD/pendrive são VFS em memória, escrita nunca toca o disco (`motor/CartaoSd.cs:55-65`), `Normalizar` + `NaPasta` barram `..` (`:68-81,482-483`); sketch não define `Disco` (só UI/CLI). Importador Wokwi usa `JsonDocument` com comentários/`,` final (`app/ImportarWokwi.cs:60-64`), sanitiza `id` com `:` (`:108-112`), ignora tipo desconhecido — nada acima de P2.

### 1.6 Processos, P/Invoke, arquivos (fatos)

- `Process.Start` só: (a) `arduino-cli.exe` com `ArgumentList`, sem shell (`app/PlacaDeVerdade.cs:351-368`, binário em dirs da IDE ou PATH — hijack de PATH possível, P2 local); (b) `netsh wlan show…` e `powershell Get-PnpDevice…` com args fixos, sem input do sketch (`RadioDoComputador.cs:115,213,262-296`); (c) abrir pasta/URL controladas pelo app com `UseShellExecute:true`.
- Zero `WebClient`, zero `Registry`. `HttpClient` só em `motor/Rede.cs:390,399` + bibliotecas. `File.Write/Delete` só em logs/prefs/recentes/temp/testes + `PlacaDeVerdade.PrepararPasta` (limpa só temp próprio, nome sanitizado `:215-237`).
- `DllImport`: `winmm` (áudio), `user32` (`app/JanelaSolta.cs:333`), `xinput` (joystick), `ws2_32!WSASetServiceW` (SDP Bluetooth, `PortaBluetooth.cs:419`), `kernel32/ntdll` do nativo. Rádio BT do PC é tocado por padrão em execução real (`app/MainWindow.xaml.cs:4710`), WiFi é só leitura via netsh.

---

## 2. Erros e bugs de simulação (fidelidade)

### 2.1 Solver `Energia` é DC nodal, não SPICE (P1 onde finge, P3 onde assume)

- Nodal `G·V=I` + Gauss com pivô (`app/Energia.cs:958-1109`). Diodo/LED como `R+Queda` iterativo (`:973-1067`). GPIO saída = fonte 30 Ω (`:501-504`). Reguladores em 2 ramos + `AtualizarConversores` (`app/EnergiaReguladores.cs:62-85,122-174`). NPN/PNP/MOSFET/opto: controle=diodo, canal=`R(v)` (`app/EnergiaSemicondutores.cs:35-130`). Ponte-H como transformador ideal (`app/Energia.cs:170-187,1014-1024`). PWM por média em `k+1` contas (`app/EnergiaPwm.cs:31-69`). Transitório Euler implícito (`app/EnergiaTransitoria.cs:40-77`).
- Sem AC: nenhuma fonte senoidal, impedância complexa ou fase. `cristal: yield break` (`app/EnergiaTransitoria.cs:43`). Oscilador, filtro, 555, piezo ressonante não simuláveis.
- Capacitor/indutor só com `passo>0`: fora do quadro, `C=aberto 1e9`, `L=só DCR` (`:40-61`). RC de temporização, debounce e acoplamento somem na visão de energia/divisor.
- Carga do capacitor usa média do PWM (`app/Energia.cs:96-97`). Ripple real × média diverge em filtro PWM→analógico.
- Entrada analógica ideal sem carga: `AplicarPelaTabela` escreve `maq.Analogico[gpio]` (`app/EditorCircuito.cs:5900-5907`); `AplicarDivisores` só para nó com resistor (`:5864-5888`). Divisor errado lê certo aqui e errado na bancada. Potenciômetro `Entre(VCC,GND,10k)` (`app/Energia.cs:861-867`) sempre consome, mesmo com cursor solto.
- GPIO 30 Ω fixo + leak `1e-9` para terra (`app/Energia.cs:504,1026`): flutuante vai a 0 em vez de indefinido; curto GPIO×trilho resolve sem queimar no solver — quem queima é `Queima`, camada separada.
- LED único `Ideal=12mA,Max=30mA,R=18Ω,Queda=2.0` (`:672-676`) para todas as cores: azul/branco com Vf ~3 V dá corrente errada. Limiares fixos `SemCorrente=0.0005`, `Ideal*0.15` (`:7,36-43,1153-1156`).

### 2.2 `Queima` é heurística topológica (P1/P2)

Declara em `app/Queima.cs:22-24`: "não há corrente nem tensão aqui, só nós". `Mapear()` marca `TerraDePlaca/TensaoDeTrilho/Resistor/Saidas` (`:411-488`).

- "Tem resistor" = resistor no mesmo nó (`:441`, checado como `a.Resistor||k.Resistor` em `:166`). Pull-up/divisor no mesmo net anistia LED sem resistor em série (falso negativo). RGB exige 3 resistores por perna — correto (`:531-549`).
- Curto de bateria `+`/`-` não pega: `EhTerra/EhAlimentacao` só conhecem `GND/VSS/AGND/3V3/5V/VIN/VCC/...` (`app/Circuito.cs:903-908`); `+`/`-` nunca viram `TerraDePlaca/TensaoDeTrilho`, e a regra 1 exige os dois (`app/Queima.cs:104-109`).
- Dedup esconde 2ª causa: `GroupBy(Peca).OrderByDescending(Queimou).First()` (`:269-271`).
- Limiar LED 4.5 V grosseiro (`:297-308`, `QuedaDoLed=2.0` em `:52`): pedagógico, não físico.
- Saída PWM conta como HIGH (`alto=Pwm>0||Digital==1` em `:477`, idem `app/Energia.cs:501`): `analogWrite(1)` em curto no GND já acusa como HIGH sólido.
- Com placa desligada `Queima` nem roda (`!Energizada→return` em `:95`).

### 2.3 Netlist e multi-placa (P1/P2)

Union-find `Registrar/Unir/Raiz` (`app/Circuito.cs:936-950`), furos por geometria `RaioDoFuro=4.5px` (`:802,810-869`).

- `GpioDe` perde placa: índice guarda só primeiro GPIO/raiz (`gpioPorRaiz.TryAdd`, `placaPorRaiz.TryAdd // primeira ganha` em `:1092,1101`). Quem usa `GpioDe` com `maq` única erra com 2 placas (`AplicarPelaTabela:5902,5946,5991`, `BobinaEnergizada:6060`). `NivelNaRede` usa `PinoDe` certo (`app/ChipsNoCircuito.cs:216-223`). Mitigação parcial: `MaquinaDaPeca→PlacasDaPeca→PlacaQueManda??placas[0]` (`app/EditorCircuito.cs:6466-6495`) + aviso `PecasEntreDuasPlacas` (`:6541-6562`). Sensor entre 2 placas alimenta só `placas[0]` sem aviso elétrico.
- `5V.2→5V` via `SemSufixo` (`app/Circuito.cs:916-922`): bom para Uno+shield, ruim porque duas fontes 5 V distintas viram mesmo nó ideal sem corrente de conflito.
- Digital forçado sem wired-OR: `Digitais[]` (`app/Comportamentos.cs:16,109-162`), `maq.NivelExterno[gpio]=...` por `Giro>Limiar` (`app/EditorCircuito.cs:5989-5999`); rele/chip sobrescrevem (`:2588,5612,5651-5702,6118-6121`), último vence, `-1=solto`. Conflito real vira nível arbitrário; só `Queima` regra 4 avisa.
- `FiosQuebrados()` só conta (`app/Circuito.cs:408-427`, comentário do LED em `D16` que DevKit não tem em `:400-407`). Editor pula fio sem aviso na hora; `Diagnostico` e `--wokwi` listam.
- Protoboard é geometria, não elétrica: 1 px tira perna do furo (`LimiarArraste=4` vs raio 4.5 em `app/EditorCircuito.cs:950-956,717-722`).

---

## 3. Linguagens incompletas (recusa honesta, mas incompleta)

71 `throw` com "ainda nao e suportado" em `motor/`. Padrão: `switch` de métodos conhecidos + `default: throw`.

- **Arduino/C++:** `SD.*` (`motor/Interpretador.cs:1608`, só `begin,exists,remove,mkdir,rmdir,open` em `:1521-1568`), `IrReceiver.*` (`:1837`, só NEC básico em `:1805-1833`), `Serial.*` genérico (`:3228`, portas em `InterpretadorPortas.cs`), `WiFi.*` (`motor/InterpretadorRede.cs:431`, lista longa de suportados em `:298-427`), `ESP.*` (`:547`), `http.getStream` manda usar `getString` (`:670`), `server.*`/`client.*` (`:807,949,1025,1237`), `SerialBT.*` (`:1333`), I2C escravo não simulado (`motor/InterpretadorI2c.cs:204`), TFT sem fontes Adafruit_GFX (`motor/InterpretadorTft.cs:105-109`).
- Suportado de verdade em C++: classes+herança, `struct`, `typedef`/`using`, `namespace`, `template` lido como `auto`, `enum`, construtor/destrutor, `ISR(TIMERx_vect)` parseia mas vira `__isr_*` que nada chama (`motor/Parser.cs:194-206`), `attachInterrupt/detachInterrupt/noInterrupts/interrupts`, `sizeof`, `for(:)`, `switch` com fallthrough, ponteiros (`&x` é o próprio `x`, `int* p`, `char**`, ponteiro de função), `#define` com/sem parâmetros + variádica (`motor/Preprocessador.cs:68-80,116-123`), tipos por chip (`int` 16 bits no AVR, divisão por zero por arquitetura em `motor/TiposC.cs:16-120`).
- Falta sem mensagem dedicada (cai em erro genérico): `goto`, `try/catch/throw` do C++, `asm`, `dynamic_cast`/RTTI, `static_assert` descartado (`motor/Parser.cs:150,1805`).
- **MicroPython:** `urequests` recusado (`motor/InterpretadorMicroPython.cs:278-280`), resto de `machine/time/network/json/math/random` (`:317,552,573,590,710,746`), `Timer` só `init,deinit,value` (`motor/InterpretadorMicroPythonTempo.cs:90`), `RTC` só `datetime,init` (`:202`), `SPI`/`I2C`/`SSD1306`/`UART` em listas fechadas, `WLAN`/`client MQTT` (`motor/InterpretadorPecasPython.cs:279,378`), `rp2040.*` (`motor/InterpretadorMemoria.cs:688`). `ParserPython.cs` recusa `yield/async/await` (`:76-81,232-234`), `*args/**kwargs` (`:167-168`), classe/def aninhada (`:241-247`). Sem ramo (erro genérico): decoradores `@`, `match` 3.10, walrus `:=`, `except*`.
- **BLE:** código existe (`InterpretadorBle.cs`, `InterpretadorArduinoBle.cs`, `InterpretadorBleCliente.cs`) mas quase todo método cai em `throw` (`:218,477`, `:391,542,544,622,672,837`, `:193,249,271,312,398,438,567`). Superfície real é pequena.
- **Rust/Pascal/C#/Pico/STM32/AVR:** Rust sem posse, `struct/enum/trait` com corpo descartado (`motor/ParserRust.cs:80-163`); Pascal com 4 dialetos mas sem genéricos/tasking/prova SPARK; C# via tradução textual que recusa propriedade/evento/LINQ/genérico (`motor/Compilador.cs:451-452`); Pico-SDK-C só GPIO/tempo/ADC/PWM parcial (`motor/InterpretadorPicoSdk.cs:33-188`); STM32 sem `HAL_*/LL_*`/DMA/CAN; Assembly AVR com ~50 mnemônicos e ciclos do datasheet (`motor/Avr.cs:1-30`, `motor/Montador.cs:437-517`), periféricos além de `PORT/TIMER/ADC` não mapeados.
- **Depurador:** não muda valores, não faz step-over, breakpoint não acompanha inserção de linha acima (README, ainda válido).

---

## 4. Glitches, erros engolidos e produto incompleto

### 4.1 Exceção engolida (P2, 1 P1 potencial)

Nenhum `TODO/FIXME/NotImplemented` em `app/*.cs`. 15 `catch {}` vazios, quase todos em IO/áudio/rede/teste:

`app/Recentes.cs:37,64`, `app/Preferencias.cs:40`, `app/MonitorIde.cs:45,62`, `app/Miniaturas.cs:84,103`, `app/Pecas.cs:220,612`, `app/JanelaAbertura.cs:436`, `app/MainWindow.xaml.cs:592`, `app/EditorCircuito.cs:3362`, `app/App.xaml.cs:42,630-634,663-666`, `app/Armazem.cs:48`, `app/Autoteste.cs:2858,8001,14066,14681`, `app/Buzina.cs:182,184,189`, `app/Falante.cs:150,152,155`, `app/AutotesteMqtt.cs:55,127,148`.

P1 potencial: `DispatcherUnhandledException→Handled=true+MessageBox` (`app/App.xaml.cs:193-204`); no `--passeio` só anota e segue (`app/Passeio.cs:48-56`) — teste pode ficar verde com exceção engolida.

### 4.2 Windows-only (P3, com P1 para lab misto)

`TargetFramework net10.0-windows`, `UseWPF true` (`app/App.csproj:5-6`). `DllImport winmm` (áudio), `xinput` (joystick em `app/Vinculos.cs:54,57`), `user32` (`app/JanelaSolta.cs:333`). `platform.txt` chama `cmd.exe/powershell/noop-cc.bat/porta.bat`. `CETCompat false` exigido pelo nativo (`:19`). Linux/macOS exigem Avalonia + `nucleo` sem `System.Windows.Point` + TCC por SO — roadmap, não bug.

### 4.3 Instância única, autosave, recentes, demos, captura (P2)

- 2º `.sktch`/upload com circuito diferente cai no circuito já aberto; `placaDaIde` só avisa (`app/App.xaml.cs:621-669`, `app/MainWindow.xaml.cs:3990-4024`).
- Autosave desligado de fábrica (`Preferencias SalvarSozinho=0` em `app/MainWindow.xaml.cs:3912`) e nunca cobre projeto novo sem caminho (`:3913`). Crash antes do 1º Ctrl+S perde tudo; só `PodeDescartar()` pergunta (`:3958-3969`).
- Recentes descartam inexistente (`app/Recentes.cs:31-35`): pendrive desplugado some sem rastro.
- Demos embutidas como `EmbeddedResource` (`app/App.csproj:170`); sufixo não achado retorna null (`app/Recentes.cs:239-252`); `Passeio.AbrirDemo` dá `throw` (`app/Passeio.cs:755-764`).
- Popup é outra janela: `CapturarMenu` remonta submenu com mesmo template (`app/App.xaml.cs:53-109`); quebra se template mudar. Foto com fundo magenta para flagrar transparência (`app/Passeio.cs:801-830`).
- Idioma trava em pt enquanto catálogos pela metade (`app/App.xaml.cs:187-191`). Contraste WCAG tem teste (`app/Autoteste.cs:13606-13659`), mas glitch visual ainda depende de olho.

### 4.4 Arduino IDE: Verificar × Upload (P1/P3)

Ambos passam por `ponte.ps1 -Modo compilar|gravar` (`plataforma/platform.txt:18,77`). Compilação é no-op (`noop-cc.bat` cria arquivo vazio, `:25-31`); tamanho é estimativa `piso+fator×bytes` (`:40-42`, `boards.txt:9-13`). Upload lê `circuitsketch-sketch.txt` anotado no build (`ponte.ps1:51-69`): **Upload sem Verificar antes morre `ERRO: Compile uma vez antes de gravar`** — P1 para aluno apressado. Porta fantasma via `pluggable_discovery→porta.bat` (`:52`); monitor só simulador→IDE via `%APPDATA%/serial.txt`, `Serial.read` ainda sem volta (`app/MonitorIde.cs:24-25`). Limites por placa em `boards.txt`, metade gerada por `--placas-da-ide`; assinaturas placeholder `(probably m328p/m2560/t85)` só enfeitam `simulador.gravador=avrdude|esptool` — P3.

### 4.5 Operacional

- `python ferramentas/bateria.py` sem arg grava em `E:` (originais do professor). Duas compilações na mesma pasta quebram (`--artifacts-path .artefatos/<agente>`). Instalador só na Alpha 26 (`ferramentas/instalador/gerar.ps1`, versão lida de `app/App.csproj:28-31`, atual `0.24.0`).

---

## 5. Ordem sugerida

1. Default `UsarInternet=false` em imagem de sala; pedir opt-in por sketch/projeto (P1).
2. ACL no pipe + token/handshake + não auto-rodar o que veio do pipe; validar `File.Exists` (P1).
3. Prompt ao abrir `.sktch` com pastas do host; nunca ativar `ArquivosDoComputador/PastaDoComputador` silencioso (P1).
4. `NomeLimpo("..")` + teto em pasta local e índice (P2, barato).
5. `Queima`: cobrir `+/−` de bateria, não anistiar LED por pull-up no mesmo net, mostrar 2ª causa (P1).
6. `GpioDe`/multi-placa: migrar restantes para `PinoDe` + `MaquinaDaPeca`, ou recusar 2ª placa com erro claro (P1).
7. ADC com carga + divisor a cada quadro; modelo de LED por cor; limiar PWM proporcional, não `Pwm>0` (P1/P2).
8. Docs: BLE, `Serial.read` IDE→simulador, `HAL_*`, Rust/Pascal, Assembly por ciclo vs instrução (P3, barato).
9. Regenerar projetos com módulo de relé de 1 canal em vez de KS2E cru GPIO→5 V; revisar `5V.2` em Nano (P2).
10. Autosave cobrir projeto novo (temp) + recentes lembrar desplugado + demos falharem com mensagem, não `throw` (P2).

---

## 6. Limites do método

Auditoria estática + conferência pontual de linhas acima. Não roda `CircuitSketch.exe --teste`, não fuzz TCC/HTTP, não mede CPU/RAM. Números de bateria de projetos citados por outros artefatos não foram revalidados aqui de propósito (pedido: do zero, sem reaproveitar relatório anterior).
