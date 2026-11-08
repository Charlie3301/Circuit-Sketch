# Report_LongCat — CircuitSketch

**Produto:** CircuitSketch — simulador offline de ESP32 / Arduino (C#/WPF + motor em C#)
**Árvore analisada:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32` (Alpha 24, `0.24.0`)
**Data:** 16 de outubro de 2026
**Escopo:** revisão estática de `app/`, `motor/`, `motor/Nativo/`, `ferramentas/`, `plataforma/`, `coordenacao/`, `bateria/`, `projetos/` e dos testes (`Autoteste*.cs`).
**Método:** leitura do código-fonte com referência a `arquivo:linha`, cruzada com os relatórios já existentes (`RELATORIO-AUDITORIA.md`, `Report_Nemotron.md`, `coordenacao/chatgpt/RELATORIO-004.md`, `coordenacao/resultados-projetos.json`).

> **O que NÃO foi feito:** a suite completa `CircuitSketch.exe --teste` (≈5 min, abre janelas reais) **não foi re-executada** nesta árvore. Toda conclusão marcada como **gravada** vem de `coordenacao/resultados-projetos.json` (13/10/2026) ou de `RELATORIO-004.md`. Itens marcados **verificado** foram lidos no código agora. Itens marcados **a confirmar** são plausíveis mas dependem de uma re-execução ou de um datasheet específico.

---

## 1. Resumo executivo

CircuitSketch é um simulador grande e deliberado: interpretador de árvore com C/C++/MicroPython/Rust/Pascal, modo C **nativo via TinyCC in-process**, solucionador DC de energia, verificador de queima, bancada WPF, hook da Arduino IDE, HTTP/TCP/MQTT/WiFi/Bluetooth/BLE simulados e importação/exportação Wokwi. Há uma bateria de testes extensa e a maioria das superfícies "perigosas" já tem guarda e teste (zip-slip, SHA-256 de bibliotecas, VFS do cartão SD).

Os problemas se agrupam assim:

| Área | Achado principal |
|---|---|
| **Segurança / host** | C do aluno roda **nativo x64 dentro do processo** (inline asm = escape); `UsarInternet` começa `true` no `Rede`; `localhost` é sempre permitido (SSRF); named pipe sem `PipeSecurity`; path traversal no nome da aba nativa. |
| **Fidelidade de hardware** | Slide-switch tratado como 3 digitais independentes (o comum nunca herda o nível); polaridade de chama/gás invertida; NEMA 17 modelado com 50 Ω; display de 7 segmentos com Vf de 1 LED. |
| **Motor / interpretador** | `millis()` não faz wrap de 32 bits como o comentário promete; `InterrupcoesExternas` descarta bordas além da 1000ª; `I2C.Pedir` limpa o buffer; RSSI de BLE não-determinístico; lexer de números do Pascal engole letras; `PinoDoPeriferico` ignora PWM. |
| **Incompletude** | Dezenas de métodos `ainda nao e suportado` / no-op em BLE, WiFi, HTTP, MicroPython, Pico SDK, TFT, teclado, OLED. |
| **Testes** | Teste dependente de `DateTime.Now`; projetos com zero `confere` marcados como "não funciona"; vários testes abrem `MainWindow` reais. |
| **Ferramentas / docs** | `bateria.py` grava no `E:` por padrão; caminhos absolutos fixos em Python; `README` defasado (diz que interrupção, BLE e `configTime` faltam — já existem). |

**Bateria de projetos gravada (13/10/2026): 27 passaram, 11 falharam** (`coordenacao/resultados-projetos.json`).

---

## 2. Severidade

| Tag | Significado |
|---|---|
| **P0** | Comprometimento do host / crash do processo inteiro |
| **P1** | O simulador discorda da placa de um jeito que reprova um circuito de laboratório |
| **P2** | Incompletude / glitch cercado por mensagem de erro |
| **P3** | Dívida técnica, docs, plataforma |

---

## 3. Segurança e exposição do host

### 3.1 [P0] C do aluno roda nativo x64 dentro do processo do simulador

**Verificado.** `motor/Nativo/ModuloNativo.cs` compila o `.c` do aluno com TinyCC para **memória** e executa ali, no mesmo processo da interface WPF:

```157:244:motor/Nativo/ModuloNativo.cs
int tamanho = Tcc.Relocar(estado, null);
...
codigo = Win32.VirtualAlloc(IntPtr.Zero, tamanhoDoCodigo, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
...
if (Tcc.Relocar(estado, (void*)codigo) < 0) throw ErroDeCompilacao(mensagens);
```

A página é `PAGE_READWRITE` (não trocada para `PAGE_EXECUTE_READ`) e roda em threads do processo. O que **limita** o C do aluno:

- `-nostdinc -nostdlib -funsigned-char -fno-common` (sem libc do Windows);
- heap próprio (`nativo/runtime/cs_memoria.c`) fora da heap do .NET;
- tabela fixa de chamadas (`HostNativo.cs`);
- tratador de exceção que desvia AVs dentro da região para "Guru Meditation".

O que **não limita**: **inline assembly**. O próprio runtime usa `__asm__` (`cs_base.c`), então o TinyCC aceita `__asm__` no C do aluno. Com inline asm o aluno executa máquina arbitrária e pode resolver `kernel32.dll` pelo PEB e chamar `CreateProcess`/`WinExec`. Não há processo separado, CFG ou sandbox de SO.

**Impacto:** modelo de confiança por design ("o C é do aluno"). O risco real é uma **biblioteca `.zip` baixada** com C++/C malicioso: uma vez rodando, é o próprio processo. Recomendação: documentar que o modo nativo é confiável, isolar o processo nativo, ou avisar ao abrir bibliotecas desconhecidas.

### 3.2 [P1] `UsarInternet` começa `true` e `localhost` é sempre permitido (SSRF)

**Verificado.**

```63:63:motor/Rede.cs
    public bool UsarInternet { get; set; } = true;
```

```338:340:motor/Rede.cs
    static bool EhLocal(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
```

```458:462:motor/Rede.cs
        if (!EhLocal(uri.Host) && !UsarInternet)
        {
            Anotar(origem, "http", $"{metodo} {url} → sem internet: a rede do simulador está sem saída "
                                   + "(menu ⋮ > Rede usa a internet do computador)");
            return RespostaHttp.Erro(ErroConexaoRecusada);
        }
```

Dois problemas:

1. **Padrão `true`**: a `Rede` permite qualquer host externo. No app, `MainWindow.xaml.cs:2983-2984` faz `miInternet.IsChecked = Preferencias.Ligado(Preferencias.UsarInternet, true)` e `redeSimulada.UsarInternet = miInternet.IsChecked` — ou seja, o padrão da janela também é `true`. Um sketch pode exfiltrar dados ou atacar hosts externos com origem na máquina do usuário, contra a promessa de "simulador offline" do README.
2. **`EhLocal` sempre liberado**: mesmo com `UsarInternet = false`, `localhost` e qualquer loopback passam. Um sketch pode bater em serviços locais do PC (inclusive o broker MQTT interno). Não é bloqueável por esse interruptor.

**Inconsistência:** `motor/Maquina.cs:1215` cria a rede da bancada com `UsarInternet = false` (padrão "seguro"), mas a classe `Rede` e o app começam `true`. O padrão fica diferente conforme quem instancia. **Recomendação:** padrão `false` na classe, no app e no `Maquina`, com o professor ligando.

### 3.3 [P1] TLS desligado por padrão no MicroPython / `setInsecure()`

**Verificado (parcial).** `motor/Rede.cs` usa `SslStream(..., RemoteCertificateValidationCallback = (_,_,_,_) => true)` quando o modo inseguro está ligado, e o MicroPython liga `SemConferirCertificado = true` com `ssl=True`. Isso **imita a ESP32** (que é o que o professor quer), mas é uma brecha real: MITM em qualquer HTTPS do sketch. Vale documentar que o simulador não valida certificado como um navegador.

### 3.4 [P1] Named pipe `CircuitSketch_Canal` sem `PipeSecurity`

**Verificado.**

```643:645:app/App.xaml.cs
                using var canal = new NamedPipeServerStream(
                    NomeCanal, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);
```

```653:661:app/App.xaml.cs
                Dispatcher.Invoke(() =>
                {
                    janela.CarregarSketch(sketch);
                    ...
                });
```

Sem `PipeSecurity`, o pipe herda o SDDL padrão, que permite acesso ao mesmo usuário (e a `SYSTEM`/Administradores). Qualquer processo local do mesmo usuário pode escrever um caminho no pipe e fazer o app **carregar e interpretar** um `.sktch` arbitrário (C ou MicroPython), sem autenticação de cliente. Requer o mesmo usuário (não é elevação), mas é um vetor real em PC compartilhado. **Recomendação:** `PipeAccessRule` restritivo + validar que o cliente é o `enviar.bat` oficial.

### 3.5 [P1] Path traversal no nome da aba do projeto nativo

**Verificado.**

```193:197:motor/Nativo/ModuloNativo.cs
                foreach (var f in doProjeto)
                {
                    string destino = Path.Combine(pastaTemporaria, f.Nome.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                    File.WriteAllText(destino, f.Texto ?? "", new UTF8Encoding(false));
                }
```

`f.Nome` vem do `.sktch`/IDE e não tem `..` removido. Um nome como `..\..\..\evil.c` escapa da pasta temporária e sobrescreve arquivos. Requer um `.sktch` malicioso com nome de aba contendo `..`, mas o fix é barato: `Path.GetFileName(f.Nome)` ou validar que o destino continua dentro de `pastaTemporaria`.

### 3.6 [P2] `Tcc.TrocarSetjmp` depende do layout binário da `libtcc.dll`

**Verificado (código lido).** `motor/Nativo/Tcc.cs` procura padrões de bytes na DLL em memória (`longjmp` wrapper, depois `_setjmp`/`longjmp` do `msvcrt`) e troca os handlers. Se **nenhum** padrão for achado, retorna em silêncio e um erro de compilação usa o `longjmp` da msvcrt, que o próprio comentário do código diz que **derruba o processo**. Dependência frágil: uma `libtcc.dll` atualizada pode quebrar o simulador em erro de compilação. Vale fixar a versão da DLL e falhar alto se o padrão não for achado.

### 3.7 [P2] `arduino-cli.exe` resolvido pelo `PATH`

**Verificado.** `app/PlacaDeVerdade.cs:49-55` procura `arduino-cli.exe` em cada pasta do `PATH`. Hijack local de binário (exige controle prévio do PATH). O resto está correto: `UseShellExecute=false` + `ArgumentList` (sem injeção de argumento).

### 3.8 [P2] Flag de depuração em produção

**Verificado.** `motor/Nativo/ModuloNativo.cs:432-435` instala um segundo vectored exception handler quando `CS_DEPURAR_VEH=1`. Inócuo hoje (só loga), mas é uma variável de ambiente que muda o tratamento de falhas no binário de produção. Dívida de segurança.

### 3.9 O que está bem feito (não é bug)

| Tópico | Status |
|---|---|
| **Zip-slip** | `app/Acervo.cs` usa `Path.GetFullPath` + `StartsWith(raiz)` e teto de 200 MB. Testado com `../../fora.h`. |
| **Download de biblioteca** | `BibliotecasOnline.ConferirUrl`: só `https://downloads.arduino.cc/`, porta padrão, sem user-info, SHA-256 contra o índice, `AllowAutoRedirect = false`, revalidação da URL final. |
| **`.sktch`** | `System.Text.Json` (sem `BinaryFormatter`/`TypeNameHandling.All`). |
| **`Process.Start`** | `netsh`/`powershell` e `arduino-cli` com argumentos fixos e `ArgumentList`; sem string de comando montada com input do usuário. |
| **Cartão SD / `fopen` nativo** | `motor/CartaoSd.cs` valida o caminho dentro da raiz; `NativoArquivos.cs` só abre dentro de `/sdcard`, `/spiffs`. |
| **Servidores HTTP/Bluetooth** | loopback apenas (`NativoRede.cs:92`, `PortaBluetooth.cs:58`). |

---

## 4. Fidelidade de hardware (P1)

### 4.1 [P1] Slide-switch não é um SPDT (código + teste gravado falhando)

**Verificado + gravado.** No Energia, o contato é modelado certo (`app/EditorMotores.cs:52-54`: comum "2" fecha com "1" ou "3"). Mas o comportamento por quadro trata os três pinos como digitais independentes:

```162:162:app/Comportamentos.cs
        ["slide-switch"] = new() { Digitais = ["1", "2", "3"], Chave = true, Rotulo = "Ligada" },
```

```5989:5999:app/EditorCircuito.cs
        foreach (string pino in comp.Digitais)
        {
            int gpio = rede.GpioDe(Atual, peca.Id, pino);
            if (gpio < 0) continue;

            bool detectou = peca.Giro > comp.Limiar;
            maq.NivelExterno[gpio] = comp.DigitalInvertido
                ? (detectou ? 0 : 1)
                : (detectou ? 1 : 0);
        }
```

O caminho elétrico (Energia) e o caminho GPIO discordam. O teste espera `0 / 1 / 0` (esquerda=comum no GND, direita=comum no 3V3):

```106:116:app/AutotesteComportamentoEntradas.cs
        // SPDT: 2 e comum; esquerda fecha 1-2, direita fecha 2-3.
        // https://docs.wokwi.com/parts/wokwi-slide-switch
        Peca("slide-switch", t =>
        {
            int g = t.Sinal("2", pullup: true); t.Ligar("1", "GND.1"); t.Ligar("3", "3V3");
            foreach (int pos in new[] { 0, 1, 0 })
            {
                t.P.Giro = pos; t.Quadro();
                t.Valor(pos == 0 ? "esquerda, comum no GND" : "direita, comum no 3V3", t.M.DigitalRead(g), pos);
            }
        });
```

`RELATORIO-004.md` gravou esse bloco como **70 passou, 12 falhou**, incluindo os casos do slide-switch. **A confirmar:** re-rodar o bloco para saber se o código atual ainda falha (o `NivelExterno` é lido primeiro em `Maquina.DigitalRead`, então depende de `rede.GpioDe` retornar um GPIO válido para o pino 2). **Direção do fix:** remover `slide-switch` de `Digitais` e deixar o contato de energia (`LevarNivel`) ditar o nível do comum, como o `micro-switch` já faz.

### 4.2 [P1] Polaridade de chama/gás invertida vs a referência do kit

**Verificado.**

```124:132:app/Comportamentos.cs
        ["gas-sensor"] = new()
        {
            Analogicos = ["AOUT"], Digitais = ["DOUT"],
            DigitalInvertido = true, Rotulo = "Gas"
        },
        ["flame-sensor"] = new()
        {
            Analogicos = ["AOUT"], Digitais = ["DOUT"],
            DigitalInvertido = true, Rotulo = "Chama"
        },
```

`DigitalInvertido = true` = DOUT em **LOW** ao detectar. O teste foi escrito para casar com o simulador, não com o hardware:

```260:274:app/AutotesteComportamentoEntradas.cs
        // O som: o KY-037 e o KY-038 da Joy-IT, comparador ativo em HIGH
        // (https://sensorkit.joy-it.net/en/sensors/ky-037 e ky-038). A chama e
        // o gas: os modulos genericos de 4 pinos com o LM393 (o desenho e o
        // deles, e nao o KY-026), com o DOUT em LOW ao detectar.
        ...
                    int esperado = tipo is "gas-sensor" or "flame-sensor" ? (detectado ? 0 : 1) : (detectado ? 1 : 0);
```

`RELATORIO-004.md` diz que a expectativa original (KY-026, ativo em HIGH) foi **trocada** para justificar o simulador. É decisão de produto, mas ensina o aluno a inverter o `if (digitalRead)`. **A confirmar:** qual módulo o catálogo quer representar (KY-026 = HIGH; módulo LM393 genérico = LOW).

### 4.3 [P1] NEMA 17 com 50 Ω (real ≈ 1–4 Ω)

**Verificado (fórmula), a confirmar (datasheet).**

```762:769:app/Energia.cs
            case "stepper-motor":
            {
                foreach (var (a, b) in new[] { ("A+", "A-"), ("B+", "B-") })
                {
                    var r = Entre(a, b, 50, 0, 0.12, 0.35);
```

Um NEMA 17 típico tem bobina de ~1,5–4 Ω (1,5–3 A a 5 V). 50 Ω puxa só ~100 mA a 5 V — 15–30× menos. O aluno dimensiona fonte/driver para 100 mA e na bancada o motor pede 2 A. O `motor-28byj48` usa 50 Ω corretamente (esse motor realmente é ~50 Ω), mas o NEMA 17 não. **A confirmar:** o datasheet do modelo escolhido no catálogo.

### 4.4 [P1] Display de 7 segmentos com queda de 1 LED (2,0 V) e 18 Ω

**Verificado (fórmula).**

```696:705:app/Energia.cs
            case "7segment":
            {
                int comum = Liga("COM.1");
                bool anodoComum = TemFontePositiva(comum) || NoPositivoDeBateria(circuitoDaConta, peca.Id, "COM.1");
                foreach (string segmento in new[] { "A", "B", "C", "D", "E", "F", "G", "DP" })
                {
                    var r = anodoComum ? Entre("COM.1", segmento, 18, 2.0, 0.012, 0.030)
                                       : Entre(segmento, "COM.1", 18, 2.0, 0.012, 0.030);
```

Cada segmento de um display de 7 segmentos são **2 LEDs em série** (~3,6–4 V para vermelho, mais para azul/branco). Com 2,0 V o simulador dá ~2× a corrente real (para azul/branco, ~3×). O `led-bar-graph` usa o mesmo modelo de 2,0 V, que está certo para LEDs discretos mas não para o 7-segmentos. **A confirmar:** o modelo exato do display no catálogo.

### 4.5 [P1] MPU6050 (Adafruit) — projetos não imprimem nada

**Gravado.** De `coordenacao/resultados-projetos.json`:

| Projeto | Sintoma |
|---|---|
| `drone-quadricoptero` | monitor vazio; motores 0 rpm / 0 gf |
| `nivel-mpu6050` | monitor vazio; LED apagado quando "inclinado" |

`mpu.begin()` e `getEvent(&a,&g,&temp)` existem (`motor/Interpretador.cs:1661-1665`, `:3252-3253`). Se `begin()` falhar (rede I2C errada, biblioteca C++ que o interpretador não parseia, ou `if (!mpu.begin()) return;`), o `setup()` nunca imprime — o que casa com o monitor vazio. **A confirmar:** rodar os dois projetos e ver o serial.

### 4.6 [P1] HX711 — offset de célula vazia (~201 g)

**Gravado.** `balanca-hx711-lcd`: LCD mostrou **701 g** em vez de 500 g, depois **1451 g** em vez de 1250 g (constante +201 g). O relatório anterior aponta a fórmula em `motor/Interpretador.cs` (`84213 + peso * 420`), onde `84213/420 ≈ 200,5`. O offset só sai se o sketch `tare()` com a célula vazia. **A confirmar:** se o roteiro carrega massa antes do `tare()`.

### 4.7 [P1] ESP-NOW: pressiona funciona, soltar não

**Gravado.** `espnow-botao-led`: "recebi: apertado" e LED aceso; **soltar deixa o LED aceso**. O primeiro pacote é entregue, o segundo (botão para cima) não é aplicado. Suspeita: callback/fila em `motor/RadioNaRede.cs` + `motor/Interpretador.cs`.

### 4.8 [P1] Carrinho RC: ESC segue o rádio, servo não

**Gravado.** `carrinho-rc-sem-placa`: throttle → 8828 rpm (OK); direção fica em **93°** para esquerda e direita. O caminho de PWM do receptor que o ESC usa não é o que o servo usa. `carrinho-rc-arduino` depois reporta `sem sinal do radio` mesmo com o receptor no 5V.2 do Nano.

### 4.9 [P1] Rover e L298N PWM

**Gravado.**
- `rover-4wd`: checagem de "ré" viu `monitor: parado | frente` — substring **"re" dentro de "frente"**. Motores não inverteram.
- `rover-4wd-hc-sr04`: PWM 200 em um enable vs ligado no outro não dividiu a velocidade como esperado.
- `rover-4wd-reles`: o gerador ainda usa bobinas **KS2E** nuas do GPIO ao 5 V porque acha que o módulo de 1 canal não existe (`ferramentas/projetos.py`), mas o catálogo **tem** `modulo-rele-1`. Corrente de bobina ~70 mA vs 40 mA do Uno, e "frente invertida" decorrem desse circuito desatualizado.
- `ventilador-l298n`: pot baixo → 0 rpm; pot alto → 4453 rpm. PWM baixo tratado como desligado.

### 4.10 [P2] Interrupção vs silício real (documentado)

**Verificado (README).** O README (`:2354-2357`) documenta que as rotinas de borda rodam **depois** da chamada, não entre pulsos: um `shiftOut` dispara oito bordas e a ISR vê o estado **final**. Quebra protocolos bit-bang que amostram dentro da ISR. É uma limitação assumida, mas vale repetir aqui por ser P1 para certas aulas.

---

## 5. Motor / interpretador

### 5.1 [P1] `millis()`/`micros()` não fazem wrap de 32 bits

**Verificado.** O comentário promete wrap; o código não aplica módulo:

```1946:1949:motor/Interpretador.cs
        // unsigned long, como no core: o millis() volta a zero depois de
        // 49,7 dias, e o micros() depois de 71 minutos -- na placa tambem.
        return micro ? DoTipo((long)agora, TipoC.ULong)
                     : DoTipo(RitmoDaCpu ? (long)(agora / 1000) : maq.Ms, TipoC.ULong);
```

`agora` é `double` crescente, e o cast para `long` não dá wrap em 2³². Um sketch de longa duração que dependa do padrão não-bloqueante (`if (millis() - ultimo >= intervalo)`) depois de 49,7 dias simulados nunca vê o wrap. **Impacto:** baixo na prática (tempo simulado raramente chega lá), mas comentário e código se contradizem. Fix: `& 0xFFFFFFFFL` ou corrigir o comentário.

### 5.2 [P1] `InterrupcoesExternas` descarta bordas além da 1000ª

**Verificado.**

```842:851:motor/Maquina.cs
            for (int k = 0; k < 1000; k++)
            {
                double borda = sinal.ProximaBorda(desde);
                if (borda > agora) break;
                int antes = sinal.Nivel(borda - 0.01), depois = sinal.Nivel(borda + 0.01);
                if (InterrupcoesLigadas && Vence(alvo.Borda, antes, depois)) (vencidas ??= []).Add((alvo.Funcao, borda));
                desde = borda;
            }
            bordasAtendidasAte[pino] = agora;
```

O loop limita a 1000 bordas por pino por chamada, mas `bordasAtendidasAte[pino] = agora` roda **incondicionalmente**. Se um sinal externo (PPM, PWM rápido de outro chip) tiver mais de 1000 bordas entre duas chamadas, as excedentes são **silenciosamente perdidas** — o marcador salta para `agora`. Fix: `bordasAtendidasAte[pino] = desde;` (a última realmente processada) ou enfileirar o restante.

### 5.3 [P2] `I2C.Pedir` limpa o buffer a cada chamada

**Verificado.**

```289:301:motor/I2c.cs
    public int Pedir(int endereco, int quantos)
    {
        entrando.Clear();
        if (!Ligado || quantos <= 0) return 0;
        ...
        foreach (byte b in aparelho.Responder(Math.Min(quantos, TamanhoDoBuffer)))
            entrando.Enqueue(b);
        return entrando.Count;
    }
```

Se `requestFrom` for chamado duas vezes sem `read()` no meio, a primeira resposta é descartada. No hardware real o registrador RX segura os dados até serem lidos. Pode perder dados em sketches que fazem polling de vários sensores.

### 5.4 [P2] RSSI de BLE não-determinístico

**Verificado.**

```62:63:motor/Ble.cs
    /// <summary>A potencia do sinal que o celular ve: fixa, perto de quem esta na mesa.</summary>
    public int Rssi => -48 - Math.Abs(Endereco.GetHashCode() % 17);
```

`GetHashCode()` varia entre execuções do processo. O RSSI muda a cada start, quebrando sketches que dependem de força de sinal (proximidade, triangulação) e testes de reprodutibilidade. Fix: derivar do endereço de forma estável (ex.: hash do `Endereco` com algoritmo fixo).

### 5.5 [P2] Lexer de números do Pascal engole letras

**Verificado.**

```114:126:motor/LexerPascal.cs
                while (i < s.Length
                       && (char.IsDigit(s[i]) || s[i] == '_'
                           || s[i] == '#' || char.IsLetter(s[i])
                           || (s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))))
                {
                    ...
                    i++;
                }

                string bruto = s[ini..i].Replace("_", "");
                saida.Add(new Token(T.Num, bruto, LerNumero(bruto), !real, linha));
```

`char.IsLetter(s[i])` aceita **qualquer letra** como parte de um número. `123abc` vira um único token numérico, que `LerNumero` não consegue converter e devolve `0` silenciosamente. Corrompe literais numéricos seguidos de identificador.

### 5.6 [P2] `PinoDoPeriferico` ignora PWM ativo

**Verificado.** `DigitalWrite` trata o caso de PWM ligado, `PinoDoPeriferico` (usado pelo SPI) não:

```1774:1788:motor/Maquina.cs
    public void DigitalWrite(int p, int v)
    {
        ...
        v = v != 0 ? 1 : 0;
        double quando = Gastar(Perfil.CustoDoPinoUs);

        // Com PWM ligado o pino nao esta "no mesmo nivel": o digitalWrite
        // desliga o PWM (e o turnOffPWM do core), e isso e uma mudanca.
        bool comPwm = Pwm[p] is > 0 and < 255;
        if (Digital[p] == v && !comPwm) return;
        MudarPino(p, v, quando);
    }
```

```1796:1803:motor/Maquina.cs
    public void PinoDoPeriferico(int p, int v, double depoisDeUs)
    {
        if (!Valido(p)) return;
        v = v != 0 ? 1 : 0;
        double quando = Gastar(depoisDeUs);
        if (Digital[p] == v) return;
        MudarPino(p, v, quando);
    }
```

Se houver PWM no pino, a escrita do periférico é ignorada quando `Digital[p] == v`. Um SPI em pino com PWM não funciona. **Nota:** o caminho do sketch para `analogWrite` é `AnalogWriteNaResolucao` (`Interpretador.cs:2655`), então a inconsistência `AnalogWrite` vs `AnalogWriteNaResolucao` **não** afeta o sketch (só uso interno).

### 5.7 [P2] `Depurador.EntrarNaPausa` tem corrida entre `Reset()` e `Wait()`

**Verificado (código lido).** `motor/Depurador.cs:179-189` usa um `ManualResetEventSlim` compartilhado entre pausas. Se `Continuar()` fizer `Set()` entre o `Reset()` e o `Wait(50)`, o sinal se perde e a thread do sketch fica presa até o próximo `Continue()`. Janela estreita, mas real. Fix: recriar o evento a cada pausa ou usar `CancellationToken`.

### 5.8 [P3] `finally` que lança exceção perde a original sem contexto

**Verificado (código lido).** `motor/InterpretadorExcecoes.cs:153-167`: se o `finally` lança exceção depois de o `try` ter lançado, a original some por completo. Em Python ela aparece como `__context__`. Baixo impacto.

### 5.9 [P3] `millis()` sem CPU simulada perde precisão sub-milissegundo

**Verificado.** Com `RitmoDaCpu` falso, usa `maq.Ms` (inteiro) em vez de `MsPreciso` (com fração). Até 1 ms de diferença vs `micros()/1000`. Baixo impacto.

### 5.10 [P3] `DividirPorZero` com `%` — comentário enganoso

**Verificado.**

```242:246:motor/InterpretadorC.cs
            case DivisaoPorZero.Zero:
                return Valor.DeC(op == "%" ? m.Converter(dividendo, t) : 0, t);

            default:
                if (op == "%") return Valor.DeC(m.Converter(dividendo, t), t);
```

No Cortex-M, `SDIV` devolve zero na divisão por zero; `%` compilado como `SDIV`+`MLS` daria o dividendo. O código devolve o dividendo só para `%` e zero para `/` — correto para o resto, mas o comentário ("o Cortex-M devolve zero") é enganoso. Risco baixo, doc errada.

### 5.11 [P3] `Maquina.Avancar` em tempo real dorme em fatias de 1–20 ms

**Verificado (código lido).** `motor/Maquina.cs:1736-1744` usa `Thread.Sleep` mínimo de 1 ms, mas o Windows pode acordar com até ~15 ms. Um `delay(1)` pode dormir 1–20 ms. Afeta sketches que precisam de precisão de 1 ms.

### 5.12 [P3] Limite arbitrário de 1000 bordas por pino

**Verificado.** Mesmo que as 1000 bordas sejam processadas, o limite é arbitrário (um sinal de 100 kHz = 1000 bordas em 10 ms). O `bordasAtendidasAte[pino] = agora` mascara, mas as bordas entre a 1000ª e `agora` se perdem (ver 5.2).

---

## 6. Modelo elétrico e queima

### 6.1 [P2] Tensão do suporte de pilhas inconsistente (slots totais vs preenchidos)

**Verificado.**

```137:143:app/Baterias.cs
    public static Ficha FichaDe(PecaNoCircuito peca)
    {
        if (SuporteDe(peca.Tipo) is not { } s) return new(peca.Volts, peca.MiliAmperes, peca.MiliAmperesHora, false);
        var pilha = PilhaDe(peca);
        int cheios = Enumerable.Range(0, s.Slots).Count(k => TemPilha(peca, k));
        return new(pilha.Volts * cheios, pilha.MiliAmperes, pilha.MiliAmperesHora, cheios < s.Slots);
    }
```

```161:169:app/Baterias.cs
    public static void Sincronizar(PecaNoCircuito peca)
    {
        if (!EhSuporte(peca.Tipo)) return;
        var f = FichaDe(peca);
        var pilha = PilhaDe(peca);
        peca.Volts = pilha.Volts * SuporteDe(peca.Tipo)!.Value.Slots;
        ...
    }
```

`Sincronizar` grava `peca.Volts` com **todos** os slots; `FichaDe` (usada pela energia em `Energia.cs:582` e pelo editor) usa só os **preenchidos**. Um suporte 4xAA com 2 pilhas mostra 6 V no aviso do motor (que lê `peca.Volts`) mas entrega 3 V na simulação, e o `.sktch` grava o valor "errado". Fix: `Sincronizar` usar `f.Volts`.

### 6.2 [P2] LED em ativo-baixo não detectado pela queima

**Verificado.**

```156:172:app/Queima.cs
        // ---- 5) LED sem resistor ----
        foreach (var peca in circuito.Pecas)
        {
            foreach (var (anodo, catodo) in PernasDeLed(peca.Tipo))
            {
                var a = Achar(redes, rede, peca.Id, anodo);
                var k = Achar(redes, rede, peca.Id, catodo);
                if (a is null || k is null) continue;

                // Resistor de qualquer um dos lados já segura a corrente.
                if (a.Resistor || k.Resistor) continue;
                if (!k.TerraDePlaca) continue;

                var estouro = LedSemResistor(peca.Id, a);
                if (estouro is not null) achados.Add(estouro);
            }
        }
```

A regra só examina LED com cátodo no GND da placa. Um LED com ânodo no 5 V e cátodo num GPIO em LOW — ligação ativo-baixo, comum — passa batido, mesmo que a porta puxe corrente demais.

### 6.3 [P2] Resolver sem detecção de oscilação

**Verificado (código lido).** `app/Energia.cs:972-1067` itera até 60 vezes sem guarda contra oscilação (só as meias-pontes têm contador de viradas). Circuitos com realimentação/diodos alternando de estado podem parar em 60 iterações com resposta errada, sem aviso.

### 6.4 [P2] PWM em pino de direção tratado como analógico

**Verificado (código lido).** `app/Energia.cs:370` (`NivelDaEntrada`) devolve o duty cycle para **todas** as entradas. Para ENA (enable) está certo; para IN1/IN2 (direção) do L298N, `analogWrite(IN1, 128)` faria o motor girar a meia velocidade em vez de girar/parar. Suspeita de que `IN1`/`IN2` usem esse caminho.

### 6.5 [P2] `CargaDaFita` — divisão por zero em fita vazia

**Verificado (código lido).** `app/Energia.cs:941-942`: com `Quantidade = 0` e `Cores` vazio, `ma = 0` e a resistência vira `Infinity`. Não quebra o solver, mas é `Infinity` silencioso na estrutura.

### 6.6 [P3] Resistência entre dois GPIOs não propaga lógica

**Verificado (código lido).** `app/Circuito.cs:785-798` só une resistor quando um lado é GPIO e o outro trilho. Um resistor entre dois GPIOs deixa redes separadas — o nível não se propaga. Limitação documentada, mas confunde.

### 6.7 [P1, a confirmar] Queima vs IRremote / Timer2 pino 3

**Gravado (relatório anterior), não reproduzido aqui.** `controle-ir-leds` falhava com `Arduino Uno queimou: curto: o pino 3 está em LOW e ligado direto na alimentação`. IRremote no AVR usa Timer2, dono do pino 3 (OC2B) mesmo em receive-only; a regra 3 de `Queima.cs` ("saída em LOW direto na alimentação") trata isso como curto. `Queima.cs` é uma heurística de conectividade, não um solucionador de corrente. **A confirmar:** re-rodar o projeto.

---

## 7. UI / WPF (app/)

### 7.1 [P2] Timer da tela roda a 25 fps mesmo com a simulação parada

**Verificado.** `app/MainWindow.xaml.cs:229-234` cria um `DispatcherTimer` de 40 ms (`AtualizarTela`) que roda continuamente, atualizando monitor serial, painel de rede etc. Quando parado, continua consumindo CPU. `relogioDoDiagrama` (`:373-395`) também nunca é parado ao sair da aba (o handler confere `arquivoAberto != NomeDoDiagrama` e retorna cedo, então é inofensivo, mas o timer dispara para sempre).

### 7.2 [P2] `Resolver()` custoso em cada movimento de mouse

**Verificado (código lido).** `app/EditorCircuito.cs:3625-3770` (`Resolver`) itera todas as peças várias vezes por evento de mouse; `AoGirarRoda` (`:4651-4666`) chama `Resolver` mesmo sem zoom. `NoCorpo` (`:3538-3550`) aloca `Rect` e recalcula `Tamanho` a cada peça. `AtualizarComponentes` (`app/PainelDeComponentes.cs:49-81`) percorre o circuito inteiro a cada mudança de seleção (há assinatura que evita rebuild, mas o `Montar` ainda roda). Para circuitos grandes, isso causa travamento de UI.

### 7.3 [P2] `Escutar()` (pipe) engole exceção no shutdown

**Verificado.** `app/App.xaml.cs:637-669`: `Dispatcher.Invoke` é bloqueante e o `catch { return; }` engole tudo, inclusive `NullReferenceException` quando `janela` fecha. Não é crash, mas esconde erro real.

### 7.4 [P2] `JanelaSolta.Devolver` pode perder o painel

**Verificado (código lido).** `app/JanelaSolta.cs:183-199`: se `quem.Painel.Devolver()` lançar, a janela já foi fechada e o painel não é restaurado, e o evento `AoMudar` não dispara — estado inconsistente. `PainelSolto.Devolver` (`:60-73`) também não confere se o painel já está no pai de destino.

### 7.5 [P2] `ExportarWokwi` perde campos com JSON malformado

**Verificado (código lido).** `app/ExportarWokwi.cs:48-56`: `catch (JsonException) { continue; }` descarta silenciosamente campos de `ExtrasDoDiagrama` que falham ao parse. Perda de dados em exportação.

### 7.6 [P3] `Tema.Espalhar` sem sincronização

**Verificado (código lido).** `app/Tema.cs:240-252` modifica `Application.Current.Resources` sem lock. Só é chamado da thread de UI na prática.

### 7.7 [P3] Estado estático em `Preferencias`/`Autoteste`

**Verificado.** `app/Preferencias.cs:169-173` limpa um dicionário estático sem sincronização. `app/Autoteste.cs:14-15` tem `passou`, `falhou` e um `StringBuilder` estáticos (não thread-safe).

### 7.8 [P3] Arquivo temporário WPF na árvore

**Verificado (`git status`).** `app/App_m0nddpkj_wpftmp.csproj` aparece como untracked em `app/` (não em `obj/`). É sobra de build e deve ser removida/adicionada ao `.gitignore`.

---

## 8. Periféricos e linguagens (incompletude)

Os itens abaixo foram levantados por varredura de `ainda nao e suportado` / no-op / stubs. Estão agrupados; **não foram todos re-executados individualmente**.

### 8.1 OLED / TFT (`motor/TelaGrafica.cs`, `InterpretadorOled.cs`, `InterpretadorTft.cs`)

- **Texto fantasma:** `motor/TelaGrafica.cs:450-451` faz `if (textos.Count > 200) textos.RemoveAt(0);` — remove do registro de textos, mas **não limpa os pixels** que aquele texto desenhou. Depois de 200 escritas, o texto mais antigo continua visível na tela para sempre. **Verificado (código).** Fix: limpar o buffer ou redesenhar tudo a partir dos textos vivos.
- `dim()` (`InterpretadorOled.cs:106-108`) chama `Escurecer()`, que só liga um flag; não escurece os pixels.
- `ssd1306_command` (`InterpretadorOled.cs:130-145`) trata só 6 dos ~30 comandos do SSD1306; os demais são ignorados em silêncio.
- `setFont` (`InterpretadorTft.cs:106-111`) recusa **todas** as fontes Adafruit_GFX (FreeSans, FreeMono...); só a 5x7 interna funciona.

### 8.2 BLE (`motor/InterpretadorBle.cs`, `InterpretadorBleCliente.cs`, `InterpretadorArduinoBle.cs`)

Dezenas de métodos no-op ou com valor fixo: `setMTU`, `setPower`, `setEncryptionLevel`, setters de propriedades de característica, métodos de advertising/advertisement data, `BLE2904`/`BLEDescriptor`/`BLESecurity` (retornam vazio), configurações de scan, `setConnectable`/`setAdvertisingInterval`/`setManufacturerData` etc. `getPower` sempre 5. O README (`:2344`) ainda diz que BLE não é simulado — **doc defasada**, o código existe mas é raso.

### 8.3 WiFi / HTTP (`motor/InterpretadorRede.cs`)

- No-ops em `setHostname`, `hostname`, `setSleep`, `persistent`, `setAutoReconnect`, `setAutoConnect`, `setTxPower`, `onEvent`, `setMinSecurity` (todos retornam `true`).
- `http.getStream()`/`getStreamPtr()` lançam "ainda nao e suportado" (workaround: `getString()`).

### 8.4 I2C slave

`motor/InterpretadorI2c.cs:202-206`: `Wire.onReceive()`/`onRequest()` lançam "a placa como ESCRAVA de I2C ainda nao e simulada". Só modo mestre funciona.

### 8.5 RF24 / Radio (`motor/InterpretadorRadio.cs`)

14 métodos no-op (`setPALevel`, `setDataRate`, `setRetries`, `setAutoAck`, `setCRCLength`, `setPayloadSize`, `enableAckPayload`, `enableDynamicPayloads`, `disableDynamicPayloads`, `powerUp`, `powerDown`, `flush_tx`, `printDetails`, `printPrettyDetails`, `maskIRQ`). `getPayloadSize` sempre 32; `testCarrier`/`testRPD` sempre `false`.

### 8.6 Pico SDK (`motor/InterpretadorPicoSdk.cs`)

~14 funções no-op (`gpio_deinit`, `gpio_pull_down`, `gpio_disable_pulls`, `gpio_set_input_enabled`, `stdio_init_all`, `adc_init`, `adc_gpio_init`, `pwm_get_default_config`, etc.).

### 8.7 MicroPython (`motor/InterpretadorMicroPython*.cs`)

- `yield`, `async`/`await` rejeitados (`ParserPython.cs:76-81`); `*args`/`**kwargs` rejeitados (`:166-168`); `class`/`def` aninhados rejeitados (`:241-247`).
- Muitos stubs: `gc` (mem_free fixo 96000), `micropython.const`, `esp32.raw_temperature` (128), `os.uname` ("esp32"), `RTC.memory` (sempre ""), `del` (não apaga nada), `rotate` do SSD1306 (no-op), `urequests` (recusado).
- Longa cauda de `machine.*`, `time.*`, `network.*`, `json.*`, `math.*`, `random.*`, `WLAN.*`.

### 8.8 Outros

- `InterpretadorTeclado.cs:153-154`: `setDebounceTime`/`begin` no-op.
- `InterpretadorPortas.cs:147`: `setTxBufferSize` sempre 0.
- `InterpretadorTelegram.cs:76-77`: `longPoll`, `setMyCommands`, `updateToken` no-op.
- `LexerRust.cs` / `LexerPascal.cs`: número inválido vira `0` silenciosamente.
- `InterpretadorTexto.cs:396-400`: especificador de formato desconhecido (`%q`) não consome o argumento, desalinhando os seguintes.
- `Mqtt.cs:188`: wildcard `#` retorna `true` sem validar que é o último elemento do filtro (`#/foo` casaria indevidamente).
- `InterpretadorSpi.cs:337-341`: `SPI.transfer()` antes de `SPI.begin()` em AVR entra em laço infinito (intencional, imita o hardware), mas trava o simulador até o usuário apertar Stop.

---

## 9. Testes e integridade

### 9.1 [P2] Teste dependente de `DateTime.Now` (flaky)

**Verificado.**

```16177:16178:app/Autoteste.cs
                linhasData.ElementAtOrDefault(0) == DateTime.Now.Year.ToString()
                && linhasData.ElementAtOrDefault(1) == ((int)DateTime.Now.DayOfWeek).ToString(),
```

Compara a data de compilação do sketch (`F(__DATE__)`/`F(__TIME__)`) com `DateTime.Now`. Falha na virada do ano (meia-noite de 31/12) e depende do relógio da máquina.

### 9.2 [P2] Projeto com zero `confere` marcado como "não funciona"

**Verificado.**

```63:63:app/AutotesteProjetos.cs
        bool funcionou = erro is null && conferencias.Count > 0 && conferencias.All(c => c!["ok"]!.GetValue<bool>());
```

Um projeto puramente observacional (sem passos `confere`) roda sem erro mas é marcado como `funcionou = false`. Confunde "rodou" com "teve asserções passando".

### 9.3 [P2] Testes abrem janelas reais

**Verificado.** `app/AutotesteProjetos.cs:100` cria `new MainWindow()`; há dezenas de chamadas assim em `Autoteste.cs`. Janelas reais são flaky em ambiente headless/CI, interagem pelo gerenciador de janelas e podem deixar janelas zumbis se `Close()` falhar (o `catch (InvalidOperationException)` em `AutotesteProjetos.cs:149` engole).

### 9.4 [P2] Dependências de tempo (`Thread.Sleep`)

**Verificado.** `app/Autoteste.cs` tem `Thread.Sleep` em 5587 (150 ms), 5594 (400 ms), 5667/5761 (10 ms), 5869 (200 ms), 8508 (300 ms), 12014 (5 ms × 100). O teste do depurador (5587-5608) afirma `demorou < 800 ms` para resposta de pausa — pode falhar em máquina carregada. `AutotesteNativo.cs:40` faz busy-wait com timeout de parede.

### 9.5 [P3] `catch { }` que engole erro de protocolo

**Verificado.** A maioria dos `catch { }` é cleanup (fechar socket, deletar arquivo) e é defensável. Mas `app/AutotesteMqtt.cs:127` engole erro **dentro do loop de processamento de mensagens MQTT** — um erro de protocolo some e o bug fica invisível.

### 9.6 [P3] `InvariantGlobalization` divergente entre projetos

**Verificado.** `motor/Motor.csproj:13` tem `<InvariantGlobalization>true</...>`, enquanto `app/App.csproj` avisa para **não** ligar (WPF precisa de culturas reais). O motor é compilado em ambos (App inclui `..\motor\*.cs`), então o mesmo código roda com culturas diferentes conforme o entry point. Hoje não quebra, mas qualquer formatação sensível a cultura no motor se comporta diferente no runner de testes vs no app.

---

## 10. Ferramentas, build e docs

### 10.1 [P1] `ferramentas/bateria.py` grava no `E:` por padrão

**Verificado.**

```26:26:ferramentas/bateria.py
SAIDA = sys.argv[1] if len(sys.argv) > 1 else 'E:/Sktch[/BATERIA DE TESTES'
```

Rodar `python ferramentas/bateria.py` sem argumento tenta escrever em `E:/Sktch[/BATERIA DE TESTES`. O `AGENTS.md` proíbe mexer no pendrive `E:` (os originais do professor ficam em `E:\Sktch[\NOT WORKING` e nunca são alterados). Fix: padrão `bateria/` (como o `AGENTS.md` já manda usar).

### 10.2 [P2] Caminhos absolutos fixos nas ferramentas Python

**Verificado.**
- `ferramentas/projetos.py:30`: `DOWNLOADS = 'C:/Users/seraf/Downloads/'`.
- `ferramentas/testados.py:11`: `DESTINO = 'C:/Users/seraf/OneDrive/Desktop/TESTED'`.
- `ferramentas/provar-monitor/provar-monitor.py:25`: caminho absoluto do `monitor.bat`.

Funcionam só na máquina do autor; quebram em qualquer outra.

### 10.3 [P2] Gerador `projetos.py` ainda acha que o relé de 1 canal não existe

**Gravado (relatório anterior).** O gerador usa bobinas KS2E nuas do GPIO ao 5 V porque pensa que `modulo-rele-1` não existe, mas o catálogo **tem**. Corrente de bobina ~70 mA vs 40 mA do Uno decorre disso.

### 10.4 [P3] Docs defasadas (`README.md`)

**Verificado.** O README ainda diz que coisas já implementadas faltam:

- `:1119-1131` — diz que `attachInterrupt` é **recusado** ("interrupcao ainda nao e simulada"). Na verdade está implementado (`motor/Interpretador.cs:137`, `:2539`; `motor/Maquina.cs:967`).
- `:1128-1130` — diz que `shiftOut`/`shiftIn` são recusados. Estão implementados (`Interpretador.cs:2873`, `:2918`).
- `:2344-2346` — diz que **BLE não é simulado**. O código existe (`InterpretadorBle*.cs`, `AutotesteBle*.cs`), só é raso.
- `:2349-2350` — diz que `configTime()`/`getLocalTime()` não existem. O relatório anterior diz que já existem (confirmar).

Outras pendências do `README`: depurador não muda valores nem passa por cima de funções; IDE não mostra a porta no Monitor Serial (Upload funciona).

### 10.5 [P3] Versão/identidade

**Verificado.** `app/App.csproj:28-31`: `Version 0.24.0`, `InformationalVersion Alpha 24`. O `README.md` e os relatórios falam de Alpha 24/25 — conferir se a versão está alinhada com o changelog (`app/assets/changelog.txt`).

---

## 11. Falsos positivos verificados (não gastar tempo)

| Alegação | Veredito |
|---|---|
| `app/Sktch.cs` é "binário/corrompido" | **Falso.** Tem 5314 bytes; começa com `usin` (`using...`). O `read` reportou binário por engano de encoding. |
| Zip-slip no download de bibliotecas | **Falso.** `Acervo.cs` valida caminho e tamanho; testado. |
| `.sktch` inseguro (desserialização) | **Falso.** Usa `System.Text.Json`, sem tipos polimórficos. |
| `Process.Start` com injeção de comando | **Falso.** Todos usam `ArgumentList` e argumentos fixos. |
| `AnalogWrite` ignora resolução (afeta sketch) | **Falso.** O sketch vai por `AnalogWriteNaResolucao` (`Interpretador.cs:2655`); `AnalogWrite` é uso interno. |
| `app/App_m0nddpkj_wpftmp.csproj` em `obj/` | **Parcialmente falso.** Está em `app/` (untracked), não em `obj/`. |

---

## 12. Plano de ação sugerido (ordem)

1. **Segurança (rápido e barato):** padrão `UsarInternet = false` na classe, no `Maquina` e no app; restringir o named pipe com `PipeSecurity`; sanitizar `f.Nome` em `ModuloNativo.cs:195`.
2. **Bugs de motor:** corrigir `InterrupcoesExternas` (`bordasAtendidasAte = desde`), `I2C.Pedir` (não limpar buffer já lido), `Ble.Rssi` (estável), `LexerPascal` (número não engolir letras), `PinoDoPeriferico` (respeitar PWM), `millis()` (wrap ou corrigir comentário).
3. **Fidelidade:** slide-switch (tirar de `Digitais` e usar o contato de energia); NEMA 17 e 7-segmentos (checar datasheet do modelo do catálogo); polaridade de chama/gás (decidir o módulo).
4. **Modelo elétrico:** `Baterias.Sincronizar` usar `FichaDe`; queima de LED ativo-baixo; oscilação do resolver.
5. **UI:** parar timers quando a aba/janela fecha; evitar `Resolver` a cada mouse; tratar `JanelaSolta.Devolver`.
6. **Testes:** tirar `DateTime.Now`; corrigir `conferencias.Count > 0`; marcar timeouts como flaky.
7. **Ferramentas/docs:** `bateria.py` padrão `bateria/`; caminhos relativos; `README` (interrupção, BLE, `configTime`); `projetos.py` com `modulo-rele-1`.
8. **Dívida de segurança:** documentar o modelo de confiança do modo nativo e considerar isolar o processo; fixar a versão da `libtcc.dll`.

---

## 13. Limites deste relatório

- **Não** re-executei a suite completa (`--teste`) nem a bateria de projetos. Números de falha vêm de `coordenacao/resultados-projetos.json` (13/10/2026) e `RELATORIO-004.md`.
- Itens marcados **a confirmar** dependem de datasheet específico ou de uma re-execução.
- A varredura de "incompletude" (seção 8) veio de busca por `ainda nao e suportado`/no-op e não foi item a item re-executada.
- Não fiz fuzzing do TinyCC nem auditoria de memória do runtime nativo além do que o código-fonte mostra.
