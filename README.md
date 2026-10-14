# CircuitSketch

Simulador leve e offline dos componentes que a turma usa, integrado à
Arduino IDE. Substitui o Wokwi, cujo gargalo é a fila de compilação
compartilhada no servidor deles.

## Estado atual: integração com a IDE **PROVADA**

Testado em 09/09/2026 com arduino-cli 1.5.1 (o embutido na Arduino IDE 2.x).

### O que funciona

| Item | Resultado |
|---|---|
| Board aparece na IDE | ✅ `simulador:esp32:simesp32` |
| Instalação sem internet | ✅ cópia manual da pasta |
| Compilação no-op | ✅ exit code 0, ~0,2s |
| `{build.source.path}` entrega o sketch | ✅ caminho correto e acessível |
| Lançar o simulador sem travar a IDE | ✅ via `Start-Process` |
| Independe do core do ESP32 | ✅ plataforma autossuficiente |

### O que NÃO funciona (e por quê)

**O botão Upload (→) não pode ser usado.** O arduino-cli 1.5.1 valida a
porta contra o pluggable discovery antes de rodar a receita de upload:

    Error getting port metadata: port not found: SIMULADOR serial

Sem porta selecionada ele fica esperando indefinidamente; com porta
inventada, o erro acima. Para usar o Upload seria preciso escrever uma
ferramenta de discovery (executável falando JSON por stdin/stdout) que
anuncie uma porta falsa.

**Decisão: usar o botão Verificar (✓)**, via `recipe.hooks.postbuild`.
Não precisa de porta, não precisa de discovery, e já está funcionando.
O discovery fica como melhoria futura, se valer a pena.

## Armadilhas encontradas (não repetir)

1. **`copy /y nul "{object_file}"` não serve como compilador falso.**
   O arduino-cli **anexa** flags próprias na receita (`-MMD -MF <dep>`)
   durante a detecção de bibliotecas, e o `copy` quebra com argumento
   desconhecido. Por isso existe `tools/noop-cc.bat`: ele cria o arquivo
   de saída, ignora flags desconhecidas e cria o arquivo de dependência
   quando o `-MF` é pedido.

2. **`start /b` mata o filho junto com o pai**, e `start` sem `/b`
   trava esperando o console. Só `Start-Process` (PowerShell) ou
   `Process.Start` (C#) destacam de verdade.

## Instalação

Feche a Arduino IDE e rode:

    powershell -ExecutionPolicy Bypass -File instalar.ps1

Instala em `Documentos\Arduino\hardware\simulador\esp32`. Instalação
manual: sem internet, sem Boards Manager, sem URL extra nas Preferências
(uma URL a menos para a IDE tentar buscar ao iniciar — importante numa
rede congestionada, onde ela trava esperando timeout).

## Uso

1. Arduino IDE → Ferramentas → Placa → **ESP32 Simulador (Turma)**
2. Escrever o sketch
3. Clicar em **Verificar (✓)** — o simulador abre com o código

## Estrutura

    plataforma/
      boards.txt              define a board
      platform.txt            receitas no-op + hook postbuild
      cores/simulador/        Arduino.h mínimo (só pro pré-processador)
      tools/
        noop-cc.bat           compilador falso
        enviar.bat            ponte IDE -> simulador
    sketches-teste/           casos reais da turma (golden tests)
    instalar.ps1

## Marco 2: motor de execução **FUNCIONANDO**

`motor/` — projeto C# (.NET 10) que roda um `.ino` de verdade sem
compilar C++. Lexer → parser de descida recursiva → interpretador
tree-walking.

    cd motor
    dotnet run -- ..\sketches-teste\semaforo 8000

### O que já roda

`#define` · variáveis globais e locais · vetores · funções do aluno com
parâmetro e retorno · `if/else` · `while` · `for` · `break`/`continue` ·
ternário · operadores compostos (`+=`, `++`) · precedência completa ·
divisão inteira vs. real com semântica de C · `String` em `Serial.print`

Funções: `pinMode` `digitalWrite` `digitalRead` `analogRead` `millis`
`micros` `delay` `map` `constrain` `abs` `min` `max` `sqrt` `pow`
`random` `tone`/`noTone` · `Serial.begin/print/println`

Constantes: `HIGH` `LOW` `INPUT` `OUTPUT` `INPUT_PULLUP` `LED_BUILTIN`

### Decisões que se provaram certas

**Relógio virtual.** `delay()` adianta o tempo em vez de dormir. A
execução fica instantânea e **determinística** — é o que transforma os
sketches da turma em golden tests automáticos.

**Interpretador em vez de transpilar.** `delay()` bloqueante saiu de
graça, e o contador de passos dá proteção contra `while(1)` sem custo
extra.

### Bugs encontrados pelo teste de tortura

1. `#define` era descartado pelo lexer. Agora há expansão de macro
   (só object-like; `#define MAX(a,b)` segue fora).
2. `int a = 1, b = 2;` criava um `CmdBloco`, que abre escopo novo — as
   variáveis sumiam na mesma linha. Virou `CmdVarios`, no mesmo escopo.

### Mensagens de erro

    ERRO DE SINTAXE: linha 3: esperava ';' mas encontrei 'pinMode'
    ERRO DE EXECUCAO: linha 4: loop preso: passou de 2000000 passos
      sem sair. Falta um delay() ou uma condicao de saida?
    ERRO DE EXECUCAO: linha 2: 'WiFi.begin()' nao e suportado pelo
      simulador. A v1 cobre LED, botao, potenciometro, buzzer, LCD e
      Serial -- rede (WiFi/Bluetooth) esta fora de escopo.

## Marco 3: aplicativo WPF **FUNCIONANDO**

`app/` — janela WPF que consome a `Maquina`. Publicada como executável
único, sem instalador e sem runtime para instalar:

    dotnet publish app -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

### Medido nesta máquina

| | |
|---|---|
| RAM (privada) | 61–68 MB |
| CPU, sketch `semaforo` | 8,3% de um núcleo |
| CPU, sketch `motor_lcd` | 12,2% de um núcleo |
| Executável | 131 MB (runtime .NET embutido) |
| Processos | 1 (instância única via mutex + named pipe) |

O WorkingSet aparece como ~180 MB no Gerenciador de Tarefas, mas conta
páginas compartilhadas do runtime; o custo real é a memória privada.

### Como a cadeia fecha

1. Aluno escreve o sketch na Arduino IDE
2. Clica em **Verificar (✓)**
3. `recipe.hooks.postbuild` chama `enviar.bat` com o caminho do sketch
4. `enviar.bat` lê `caminho.txt` e dispara o executável via `Start-Process`
5. Já aberto? O named pipe entrega o caminho para a janela existente

### Bugs encontrados montando o app

1. **`InvariantGlobalization=true` quebra o WPF.** Copiei do projeto do
   motor, onde é inofensivo (console). No WPF o cache de fontes precisa
   de culturas reais: todo `TextBlock` estourava com
   `CultureNotFoundException` e a janela morria sem deixar rastro. Foi o
   log de exceções, adicionado minutos antes, que revelou.
2. **Proteção de CPU que não protegia.** Eu cedia CPU só quando o relógio
   não avançava — mas em tempo real o cronômetro sempre avança, então
   nunca cedia e um sketch `millis()` girava a 100%. O certo é medir se a
   volta custou menos de 1 ms.
3. **Repintura cega a 30 fps** custava ~31% de CPU: pincéis recriados a
   cada quadro e 40 rótulos repintados sempre. Com detecção de mudança,
   caiu para 8–12%.

## Marco 4: componentes gráficos importados

Os seis componentes usam arte real dos **wokwi-elements** (MIT), não
desenhos improvisados. `ferramentas/extrair-svg` renderiza cada web
component com jsdom e serializa o SVG para `app/assets`:

    cd ferramentas/extrair-svg && npm install && npm run build

Os SVGs vão **embutidos no assembly** (`EmbeddedResource`), então o
executável continua sendo um arquivo só.

### Por que não Fritzing

O `fritzing-parts` **não tem ESP32**. São 96 resultados para
esp/nodemcu/wemos e todos são da família ESP8266 anterior. Como a placa
é justamente a peça central, a fonte passou a ser os wokwi-elements —
que têm o DevKit V1 e os outros cinco no mesmo estilo visual.

### Estados como arquivos separados

LED aceso/apagado (3 cores) e botão solto/pressionado são 12 SVGs
distintos. Trocar a imagem é mais barato e muito mais robusto do que
manipular o SVG em tempo de execução. O PWM entra como opacidade sobre
a imagem acesa, então `analogWrite` acende em meio-termo de verdade.

O LCD é a moldura em SVG mais 32 células de texto posicionadas na
geometria real do display — lida do próprio SVG (`pattern` em
x=12.45 y=12.55, célula 3.55 x 5.95).

### Bugs do extrator (encontrados olhando a tela)

1. **Nome do recurso embutido usa o RootNamespace, não o AssemblyName.**
   Eu procurava `SimuladorESP32.assets.*` e o recurso era
   `Simulador.App.assets.*`. Todas as peças voltavam null e sumiam da
   tela **em silêncio**. Agora a busca é por sufixo e falha vai para o
   log.
2. **O primeiro `<svg>` do shadow root não é o componente.** No buzzer
   era um ícone de nota musical oculto. Passou a escolher o maior SVG
   visível.
3. **O CSS do shadow root se perde ao serializar só o `<svg>`.** Os
   `<text>` do potenciômetro dependiam de `svg text { font-size: 1px }`
   e sem isso renderizavam gigantes e sobrepostos. O extrator agora
   inlina font-size/fill/font-family como atributo.

## Marco 6: editor de circuito

O painel de peças fixas virou **editor de verdade**, no modelo do Wokwi.

### O que dá para fazer

- **Arrastar** qualquer peça pelo canvas (encaixe na grade de 0,1"; Alt solta o encaixe)
- **Ligar fios** clicando em um pino e depois no outro
- **Escolher a cor do fio** entre 8 cores na barra
- **Protoboard** com trilhos e colunas que conduzem de verdade
- **Resistores** e mais 49 componentes
- **Adicionar componente** pelo botão + ou tecla A, com busca e categorias
- **Zoom** (roda), **pan** (botão do meio/direito), **ajustar** (F)
- **Desfazer/refazer**, **copiar/colar**, **excluir**
- **Salvar/abrir** o circuito em JSON

### Atalhos

`A` adicionar · `F5` rodar/parar · `F` ajustar · `+`/`-` zoom · `G` grade ·
`Del` excluir · `Ctrl+Z` desfazer · `Ctrl+Y` refazer · `Ctrl+C`/`Ctrl+V`
copiar/colar · `Ctrl+S`/`Ctrl+O` salvar/abrir · `Ctrl+M` serial ·
`Alt+Enter` tela cheia · `?` ajuda

### O netlist: o fio conduz de verdade

`Circuito.cs` resolve a conectividade com union-find sobre os pinos. Fios
unem nós; colunas e trilhos da protoboard unem nós; as duas pernas do
resistor são o mesmo nó. Depois basta perguntar em qual GPIO um pino foi
parar.

Consequência: **o LED acende porque o fio chega no GPIO certo**, não
porque um dropdown disse. Trocar o fio de D13 para D14 muda o
comportamento sem tocar no código.

### Catálogo completo

`ferramentas/extrair-svg` agora extrai as **50 peças** dos wokwi-elements
e gera `pecas.json` com tamanho e a posição de cada um dos **468 pinos** —
é o que permite ligar fio em pino. A protoboard não existe nos
wokwi-elements e é sintetizada em `Catalogo.cs`: como o furo segue passo
fixo, sai mais barato calcular do que desenhar 700 elementos.

A detecção de pino no canvas é **matemática**, não por elemento visual,
pelo mesmo motivo.

### Bug encontrado na verificação

`<EmbeddedResource Include="assets\*.svg" />` não pegava o `pecas.json`.
O catálogo ficava só com a protoboard sintética e **todas as outras peças
sumiam da tela** — de novo uma falha silenciosa de recurso embutido, a
segunda deste tipo no projeto.

## Marco 5: buzzer com som e potenciômetro que gira

**Buzzer** (`Buzina.cs`): gera um WAV senoidal em memória e toca em laço
com o `SoundPlayer` do .NET — sem biblioteca de áudio. O buffer contém
um número **inteiro de ciclos**, então o fim casa com o começo e o laço
não estala a cada repetição.

**Potenciômetro**: o botão girava pela CSS var `--knob-angle`, que
nenhum renderizador de SVG estático resolve. Em vez de gerar um arquivo
por ângulo, o extrator corta a peça em duas — `-corpo` e `-movel` — e o
app empilha as duas imagens, girando só a móvel com um
`RotateTransform` (-135° a +135°, origem 10,8 do viewBox). Rotação
contínua com 2 arquivos.

### Modo de captura

    SimuladorESP32.exe <sketch> --captura tela.png [--pot 3100] [--apertar]

Salva a janela em PNG. Existe porque avaliar a tela por dedução em vez
de olhar foi exatamente o erro que deixou todas as peças invisíveis por
um bom tempo. `--pot` e `--apertar` mexem nos controles **depois** que o
sketch começou a rodar, já que `Rodar()` chama `Reiniciar()` e zera os
pinos.

### Marco 7: apertar botão de módulo

A queixa era "não consigo apertar os botões" — mas o BOOT e o EN da ESP32
funcionavam. Essa assimetria era a pista.

### O que estava errado

Apertar e arrastar eram o **mesmo clique**. O BOOT/EN sai cedo no
`Apertar`, sem tocar em arraste; o botão de módulo caía no caminho comum
e virava `arrastando = peça` + `CaptureMouse()`.

Segurar um botão, então, arrastava o componente. E o arraste é grudado na
grade de 4,8 px: o primeiro movimento da mão já pulava uma célula inteira.
Medido no autoteste, **6 px de mão deslocavam a peça 9,8 px** — mais que
o dobro do raio do furo (`RaioDoFuro = 4,5`). As pernas saíam dos furos,
`Arrastou` refazia o netlist sem elas, `PrimeiroGpio` devolvia −1 e o
`maq.Pressionado[gpio]` nunca era escrito. Ao soltar, `EncaixarNosFuros`
punha tudo de volta no lugar — sem deixar nenhum rastro de que algo tinha
acontecido.

Por isso o botão ligado por **fio** funcionava no teste e o da
**protoboard** não: fio é ligado por `peça:pino`, furo é ligado por
posição.

### A correção

Camada nova `CapaBotao`, acima do corpo: a **capa** aperta, o resto do
corpo arrasta — como no Wokwi. O aperto sai cedo, igual ao BOOT/EN.

As medidas da capa vieram do próprio SVG (`class="button-active-circle"`
nos botões, `id="knob"` no joystick e no encoder), convertidas pela mesma
escala uniforme do desenho. O KY-040 mostrou por que não dá para chutar:
o knob dele **não fica no centro da caixa**, fica em (36,7; 28,3) de
116×70. O primeiro teste que escrevi sondava o centro da caixa e acusou
falha — o teste estava errado, não o código.

Junto vieram três coisas que o mesmo caminho escondia:

- **Limiar de arraste de 4 px.** Antes qualquer movimento de 0,01 px já
  arrastava. Protege todos os componentes, não só os botões.
- **Apertar um joystick trocava o desenho dele por um botão.** O código
  usava `"pushbutton-solto"` fixo para os quatro tipos apertáveis, e só o
  pushbutton grande tem essa variante. Agora só troca quando existe
  `<tipo>-pressionado`.
- **Cursor de mão** onde aperta (capa e BOOT/EN). Sem marca ninguém
  descobre que aquele ponto é diferente do resto.

### Os handlers de mouse viraram adaptadores

`AoApertar`/`AoMover`/`AoSoltar` agora só traduzem o evento do WPF e
chamam `Apertar(Point)`, `Mover(Point, bool)` e `Soltar()`. O autoteste
exercita **esses** métodos, não uma cópia deles.

Foi o que faltou da primeira vez: o teste antigo conferia só o
`Resolver`, e o `Resolver` estava certo — 105 de 121 pontos do botão
respondiam. Provava que o ponteiro acertava o botão, e não provava nada
sobre o aperto chegar na `Maquina`.

Desligando a camada nova, o teste novo fica vermelho em 14 pontos. Foi
assim que confirmei que ele tem dentes.

## Marco 8: o LED não encostava nos furos

A queixa: "os LEDs não ficam nos pinos, ficam um pouco acima".

### O tamanho natural do SVG não é a viewport

O SharpVectors entrega o desenho já posicionado dentro da viewport
declarada, mas o tamanho natural da imagem é o retângulo do **conteúdo
pintado**, não o da viewport. Quando os dois batem, `Stretch.Uniform`
não muda nada — é o caso da maioria das peças.

O LED tem `viewBox="-10 -5 35,456 39,618"`: dez unidades de folga à
esquerda e cinco em cima, reservadas para o halo. O conteúdo dele mede
22,6x34,1 numa caixa de 40x50. O `Uniform` reencaixava esse conteúdo na
caixa: esticava o desenho em 1,46x e recentralizava. As pernas saíam de
onde `PosicaoPino` diz que os pinos estão.

Medindo o catálogo inteiro, **19 das 49 peças** estavam fora de esquadro:

```
analog-joystick   desenho 183x257  caixa 103x120
arduino-nano      desenho 335x280  caixa 170x67
rgb-led           desenho 31x50 deslocado (17;13)   caixa 42x73
led               desenho 22x34 deslocado (11;8)    caixa 40x50
```

**Correção:** um retângulo transparente do tamanho da viewport traz o
limite de volta para o lugar, e um recorte reproduz o `overflow:hidden`
que o navegador aplica no SVG. A viewport sai do próprio arquivo —
`width`/`height` (com mm, cm, pt...) e, na falta deles, o `viewBox`.

Agora toda peça mede `0;0 largura x altura`, e o `Uniform` virou
identidade.

### O arduino-nano: `font-size="200%"`

Com o recorte, o Nano sumiu da tela — sobraram duas setas brancas
gigantes. Andando na árvore de desenho, a raiz tinha
`T=3,78;0;0;3,78;7,94;**202,45**`: uma translação de 202 px em Y.

A causa estava em dois glifos do silkscreen:

```xml
<text x="5.6" y="-30.7" font-size="200%">↓</text>
<text x="7.6" y="-30.7" font-size="200%">↑</text>
```

`200%` só faz sentido com alguém de quem herdar. O `<svg>` sai do shadow
root sem esse alguém, e o renderizador cai no padrão de 16px — glifos de
32 unidades numa peça de 17,8 de altura. O contorno do desenho estourava
para 280 px, o SharpVectors normalizava empurrando tudo 202 px para
baixo, e a placa inteira ficava fora da viewport.

O extrator era o culpado: ele copia a regra CSS `text { font-size: 1px }`
para cada `<text>` **que ainda não tem o atributo** — e esses dois já
tinham `200%`, então ficaram sem base. Agora ele resolve o valor relativo
contra a regra (`200%` de `1px` = `2px`), e o mesmo cálculo foi aplicado
no arquivo já gerado.

Isso valia só para o Nano, mas era invisível até alguém olhar: antes do
recorte, o `Uniform` encolhia tudo para caber e a placa aparecia
minúscula com as setas do lado.

### Duas guardas novas

- **`Svgs_EnquadradosNaCaixa`** — todo desenho tem que começar no canto
  da caixa e manter a proporção dela. Varre o catálogo inteiro (49 peças
  com SVG). Desligando o enquadramento, acusa 19.
- **`Svgs_SemFonteRelativa`** — nenhum dos 64 arquivos embutidos pode ter
  `font-size` em `%`, `em` ou `rem`.

As duas medem o arquivo, não a tela. É o tipo de defeito que passa
despercebido numa captura e salta num número.

## Marco 9: o canal da protoboard tinha 0,2"

A pergunta: "uma ESP é desse tamanho mesmo? Ocupando de uma ponta à
outra?"

A ESP32 estava certa. Medindo o catálogo:

```
ESP32 DevKit V1: 25,48 mm entre as fileiras de pino = 10,03 passos = 1"
```

É a medida real da placa — ela é grande mesmo, e ocupar quase toda a
protoboard é a reclamação clássica da DevKit V1.

O errado era a **protoboard**. A área central tinha 10 passos de A a J
(canal de 0,2" entre E e F), quando o real são 11 (canal de 0,3"). Com
10, a ESP32 cobria exatamente as dez linhas e não sobrava nenhuma para
ligar fio; na bancada sobra uma.

Duas medidas independentes confirmam os 0,3":

- **Peças de 0,3" no próprio catálogo.** O `led-bar-graph` é um DIP-20 e
  mede 2,98 passos entre as fileiras; o relé `ks2e-m-dc5`, 2,87; o
  `pushbutton-6mm`, 2,92 no eixo x — é assim que um botão tátil de 6 mm
  monta a cavalo no canal de verdade. Nenhuma delas montava certo antes.
- **Altura da placa.** Com 19 linhas dá 53,3 mm, contra 50,8 mm de antes;
  uma 830 real tem ~54,5 mm.

### O que isso custou

O `pushbutton` grande do Wokwi tem 0,2" entre as fileiras de perna e
deixou de atravessar o canal. Ele continua funcionando — nosso modelo de
botão não é elétrico, basta uma perna alcançar o GPIO — mas o arranjo
"a cavalo no canal" não é mais possível para ele. Quem atravessa agora é
o de 6 mm girado 90°, que é o que corresponde ao componente real.

Foi uma escolha consciente, não um efeito colateral: fidelidade física
acima de compatibilidade com uma pegada estilizada.

### Guarda nova

**`Protoboard_CanalDeTresDecimos`** afere as três coisas de uma vez: o
canal mede 0,3" de E a F, a área central tem 11 passos de A a J, a ESP32
cabe deixando linha livre, e um encapsulamento de 0,3" (o relé) chega
mesmo à metade de baixo.

```
E->F = 3 passos (7,6 mm), A->J = 11 passos
ESP32: 10,03 passos entre as fileiras (25,5 mm)
rele 0,3": perna de baixo -> [F20,G20,H20,I20,J20]
```

## Marco 10: auditoria das medidas

Depois do canal da protoboard, a pergunta natural: as outras placas e os
componentes estão certos?

### O que decide isso

Não é o tamanho da caixa — é se **o pino cai no furo**. Passo errado não
aparece numa peça de três pinos; aparece na ponta de uma fileira de
quinze, onde o erro já acumulou. O limite é o raio do furo: 4,5 px
(1,19 mm).

Ajustando cada fileira à melhor grade de 0,1" (só movendo a origem, com
cada pino indo para a casa mais próxima), as 30 peças de três pinos ou
mais ficam assim:

```
arduino-mega    3,64 px (0,96 mm)
arduino-uno     2,35 px (0,62 mm)
rgb-led         0,97 px (0,26 mm)
ks2e-m-dc5      0,67 px (0,18 mm)
limite          4,50 px (1,19 mm)
```

**Nenhuma peça fora.** Os espaçamentos de 2,38 a 2,65 mm que aparecem
peça a peça são ruído de arredondamento do extrator e não acumulam.

Os dois primeiros da lista não são defeito: são o vão proposital de
0,16" entre os pinos 7 e 8 do Uno, que o Mega herda. Medido: a fileira
de 18 pinos do Uno ocupa 17,55 passos em vez de 17 — os 0,55 de sobra
são exatamente esse vão. O simulador reproduz até o desalinhamento
famoso do Arduino.

### As placas dev

```
esp32-devkit-v1   28,3 x 54,0 mm   fileiras 25,48 mm = 10,03 passos (1")
arduino-uno       72,5 x 53,4 mm   fileiras 48,29 mm = 19,01 passos
arduino-nano      45,0 x 17,7 mm   fileiras 15,24 mm =  6,00 passos (0,6")
nano-rp2040       44,5 x 18,0 mm   fileiras 17,59 mm =  6,93 passos (0,7")
arduino-mega     102,7 x 50,8 mm
franzininho       64,0 x 29,9 mm   (pinos PB0-PB5: ATtiny85)
```

ESP32, Uno e Nano batem com as placas reais, incluindo as folgas de
borda coerentes (a ESP32 sobra 1,4 mm de cada lado da caixa para os
pinos do header, o Nano 1,2 mm).

Duas coisas ficaram registradas, sem correção:

- **`nano-rp2040-connect`**: 6,93 passos entre fileiras numa caixa de
  18,0 mm deixa 0,2 mm de borda — os pinos ficam na beirada do desenho.
  E discorda do Nano clássico (6,00) apesar de ser o mesmo formato. O
  desvio de 0,18 mm não tira o pino do furo, então espeta normalmente;
  é cosmético.
- **`arduino-mega`**: caixa de 50,8 mm de altura contra 53,3 mm da placa
  real, e os headers ficam a 18,4 passos em vez dos 19 do Uno — não é
  consistente com shield. Como ninguém espeta um Mega em protoboard, não
  muda nada na prática.

Nos dois casos a pegada vem do elemento do Wokwi. Mexer nela
desalinharia os pinos do desenho, que é a mesma armadilha do pushbutton:
não vale trocar um erro cosmético por um erro visível.

### Guarda nova

**`Pecas_NaGradeDeUmDecimo`** mede o catálogo inteiro contra a grade de
0,1" e afere as fileiras das três placas de protoboard. É a rede que
pega uma extração ruim antes de alguém descobrir montando o circuito.

## Marco 11: pegadas, mini de 170 e som

### As duas pegadas erradas

**`nano-rp2040-connect`** era um defeito de verdade, com correção limpa.
A pinInfo do Wokwi punha as fileiras a 6,93 passos numa caixa de 18,0 mm
— 0,2 mm de borda, impossível para um header. O próprio SVG entregou a
resposta: os furos de fixação estão em `cy="5.52"` e `cy="62.4"`, e o
pattern dos pads tem centro nos mesmos 5,5 e 62,5. Ou seja, o desenho
sempre esteve a **6 passos**; só a pinInfo foi posta no contorno da
placa. Corrigido no catálogo, os pinos caíram em cima dos pads — sem
tocar no desenho.

**`arduino-mega`** exigiu mexer no desenho. Escalar tudo em y não servia:
quebrava o passo do bloco de 36 pinos da ponta, o que **prova que a
escala vertical já estava certa**. O erro era o header de baixo desenhado
1,86 mm alto demais e a placa 2,65 mm curta. Tomando o Uno como
referência — os dois levam o mesmo shield — as contas fecharam exatas:

```
header de baixo  184,5 -> 191,5 px   (igual ao Uno)
altura da placa    192 -> 202 px     (igual ao Uno)
vão entre headers  18,28 -> 19,01 passos
```

Guarda nova: `Uno e Mega: mesmo vão entre headers`.

### Protoboard de 170 pontos

17 colunas × 10 linhas, **sem trilho de alimentação**, 45,7 × 38,1 mm —
a mini colorida de bancada. O layout de linhas virou um `Formato` por
placa em vez de constantes fixas, porque a mini sobe as linhas (não há
trilho em cima) e é bem mais baixa.

Vem em seis cores (branca, vermelha, azul, verde, amarela, preta),
escolhidas na tab do objeto. Furo e rótulo trocam para tom claro quando o
corpo é escuro, senão a preta ficava ilegível.

O canal continua com 0,3" e a área central com 11 passos: é a mesma
geometria das grandes, só que curta.

### O buzzer

Dois defeitos, os dois audíveis.

**Afinação.** O período era `TaxaAmostragem / hz` em divisão **inteira**:

```
pedido   real     erro
  1500   1520,7  +1,4%
  3000   3150,0  +5,0%
  5000   5512,5  +10,3%
```

Dez por cento a 5 kHz é quase um tom e meio de erro.

**Estalo.** Trocar de frequência trocava o `Stream` e chamava
`PlayLooping()` de novo. O sketch de motor varre `freqBase` de 80 a
400 Hz conforme o RPM — reiniciava o tocador dezenas de vezes por
segundo, e o que se ouvia era estalo em cima de estalo.

Agora o áudio sai por `waveOut` (winmm, que todo Windows tem — nenhuma
dependência nova) em blocos de 20 ms, com um fio de fundo enchendo os
blocos a partir da frequência atual e **guardando a fase** de um bloco
para o outro. Mudar de tom virou curva contínua. O ganho entra e sai em
rampa de 4 ms pelo mesmo motivo.

O timbre virou quadrada com harmônicas ímpares limitadas abaixo de
Nyquist: um piezo não é senoide, é a ímpar que dá o "bip". Medido,
médio/pico = 0,81 (senoide 0,64 · quadrada 1,00).

Calado por três segundos, o fio fecha o dispositivo e morre — o alvo
são os PCs fracos do laboratório, e o próximo `tone()` reabre em 1 ms.

### Medir em vez de escutar

O gerador de onda saiu da `Buzina` para uma classe própria justamente
para o autoteste medir a onda sem abrir placa de som:

```
  100 Hz pedido ->   100,0 Hz medido (0,00%)
 5000 Hz pedido ->  5000,0 Hz medido (0,00%)
maior salto dentro do bloco 652, na emenda 200->400 Hz 1284
médio/pico = 0,81
```

A primeira versão do teste acusou 0,7% de erro a 100 Hz. Era a **medida**
que quantizava: contar cruzamentos de zero numa janela fixa dá número
inteiro de ciclos. Medindo entre o primeiro e o último cruzamento, o erro
sumiu. Terceira vez neste projeto que o teste estava errado e o código
certo.

E `--audio <arquivo>` toca uma escala e relata se a placa de som
respondeu, para conferir uma máquina do laboratório sem depender de
alguém descrever o que ouviu.

**Único componente que emite som é o buzzer.** Os `sound-sensor` são
microfones — entrada, não saída — e já funcionavam pela tabela de
comportamentos.

## Marco 12: fios no laço, três traçados e menu enxuto

### Fios entram no laço

O laço só pegava peças. Agora pega fios também, e a cor, o traçado e o
Del valem para todos os selecionados de uma vez — antes era um clique
por fio.

Testar se um fio está dentro do retângulo não podia ser só amostrar
pontos: um fio de 700 px vira 25 amostras a cada 30 px, e um laço
estreito passa entre duas delas. É recorte de Liang-Barsky em cada
segmento do traçado.

### Três traçados, por fio

O que se chamava "fio reto" era na verdade um traçado **em L**. Linha
direta não existia. Agora são três, guardados por fio:

- **solto** — curva que cai entre as pontas (o de sempre)
- **reto** — linha direta, ponta a ponta
- **degrau** — em L, como o Wokwi (o antigo "reto")

Dá para misturar os três no mesmo circuito. O menu muda todos de uma vez
e define o padrão dos próximos; a tab do fio muda só os selecionados.

### Menu: de 20 linhas para 9

Saiu tudo que já tinha outro caminho:

| Saiu | Por quê |
|---|---|
| Copiar, Colar, Excluir, Girar 90°, Espelhar | a tab do objeto faz os cinco |
| Resistência do resistor... | a tab do resistor tem campo próprio |
| Aproximar, Afastar | a roda do mouse faz melhor |

Todos os atalhos continuam listados em "Ajuda e atalhos", que ganhou as
linhas do laço de fios e do traçado. As quatro operações de arquivo
foram para um submenu "Arquivo". Ficou só o que não tem outro caminho:
monitor serial, desfazer/refazer, traçado dos fios, as três de vista,
arquivo e ajuda.

`JanelaResistor.cs` virou código morto e foi removido — 4,5 KB.

### O autoteste não trava mais

Uma exceção dentro de um teste subia até o handler global, que abre um
`MessageBox`. Num teste sem janela ninguém clica, e a suíte ficava
pendurada em vez de acusar o erro — custou dois timeouts até eu
perceber. Agora cada teste roda dentro de um `Bloco` que transforma
exceção em FALHA com o tipo e a mensagem.

Foi assim que descobri que **a ESP32 não tem pino D16**: meu teste ligava
um fio nele, o fio ficava inválido, e o laço "perdia" um dos três.
Terceira vez que o teste estava errado e o código certo.

## Marco 13: as tabs estavam ilegíveis

Os botões de texto da tab caíam no estilo padrão do WPF, que tem fundo
claro. Como a janela é escura e manda uma `Foreground` clara por
herança, sobrava **texto claro em fundo claro**. Agora há um estilo
`AcaoObj` no tema da janela, e o botão ativo fica azul com borda branca.

Procurando o resto do problema, apareceram mais três coisas:

- **O "Ω" do resistor era `#333333` sobre `#2D2D30`** — contraste de
  1,1:1, ou seja, invisível. Provavelmente escrito quando a barra ainda
  era clara.
- **A marca da cor do LED estava invertida**: a cor *escolhida* ganhava
  contorno quase preto e as outras contorno claro. Agora é branco na
  escolhida, como nos quadros do fio.
- Os rótulos pequenos subiram de `#666` para `#9A9A9A`.

### A chave DIP não tinha como ser acionada

Conferindo tab por tab, a Chave DIP 8 só mostrava "16 pinos".
`AlternarChaveDip` existia no editor e `AplicarEntradas` já lia os bits —
**mas nada na tela chamava**. O componente era simulado e o aluno não
tinha como ligar nenhuma das oito. Agora a tab tem as 8 chaves, azuis
quando ligadas.

`--fio <n>` seleciona um fio na captura, que é a única forma de a tab do
fio aparecer numa tela sem ninguém clicando.

## Marco 14: limpeza, desempenho e três bugs

### O quadro custava 91% de um núcleo

Medido num circuito de aula — protoboard de 830, ESP32 e 15 componentes,
~1746 nós na rede:

```
antes:   3,66 ms por quadro  (91% de um núcleo a 25 fps)
depois:  0,01 ms por quadro  (0,2%)
```

`GpioDe` montava o dicionário de placas **e varria todos os nós** a cada
chamada — e ele é chamado várias vezes por peça, 25 vezes por segundo. O
netlist não muda depois de construído (mexer no circuito cria um novo),
então agora ele indexa uma vez o GPIO que cada raiz alcança, e a consulta
vira `Raiz()` mais um dicionário.

Isso importa mais aqui do que em qualquer outro lugar: a máquina em que
medi é rápida, e o alvo do projeto são os PCs fracos do laboratório.

### Três bugs, achados pela varredura de código morto

Um método sem chamador costuma ser lixo. Duas vezes foi sintoma:

- **`DefinirGiro2` não tinha um único chamador.** O eixo vertical do
  joystick era simulado — `AplicarEntradas` já lia `Giro2` — mas nada na
  tela o mudava. O VERT ficava parado no meio para sempre. A tab agora
  tem os dois eixos e um botão "centrar".
- **O KY-040 não aparecia uma vez sequer na `MainWindow`.** Só respondia
  à roda do mouse; quem não soubesse do atalho não tinha como girá-lo.
  Ganhou slider com a leitura em passos.
- **A quadratura do encoder estava errada.** A sequência era
  `11 → 00 → 10 → 01`: mudava os dois sinais de uma vez em metade dos
  passos. Um encoder real nunca faz isso, e é justamente na borda de um
  sinal que o sketch lê o outro para saber o sentido. Agora é o código
  Gray de dois bits, `00 → 10 → 11 → 01`, com o teste conferindo que
  **nunca** mudam os dois juntos.

### O log de erro estava mentindo

Toda vez que a janela de peças abria, ela registrava "recurso embutido
não encontrado" para cada protoboard — duas entradas por placa. São
desenhadas em código e nunca tiveram SVG; a janela já tratava o caso, mas
usava `Obter`, que registra falha. Quem só *tenta* um nome agora usa
`Talvez`, que é calada. O log voltou a ser útil.

### Código morto removido

`ChegaNoTerra`, `Circuito.Clonar`, `SelecionarPeca`, `Pecas.Led` e duas
constantes órfãs da `Buzina` (sobraram quando o gerador de onda saiu para
uma classe própria). `waveOutPrepareHeader` agora tem o retorno conferido
— bloco que não prepara não toca.

Rodei o analisador completo (`latest-all`) uma vez: 626 avisos, quase
todos regras de design de biblioteca que não se aplicam a um aplicativo
único. Deixei desligado e corrigi à mão os que apontavam defeito real.

## Marco 15: copiar leva os fios, e o formato .sktch

### Copiar e colar era pela metade

Levava só as peças. Duplicar um trecho pronto obrigava a religar tudo à
mão — e o trecho colado nem era o mesmo, porque a cópia listava os campos
um a um e três ficaram de fora: `Giro`, `Giro2` e `Bits`. Colar um
potenciômetro perdia a posição; colar uma chave DIP perdia as oito
chaves.

Agora `PecaNoCircuito.Copia()` é o único lugar que enumera os campos, e
copiar guarda também os fios **cujas duas pontas estão na seleção** — um
fio que sai para fora viraria uma segunda ligação no mesmo pino de lá, que
quase nunca é o que se quer. Os fios copiados são guardados por índice, e
não por Id, então colar duas vezes continua funcionando.

### O formato .sktch

Circuito e código no mesmo arquivo. Antes eram duas metades soltas — um
`.json` e um `.ino` — e nada dizia que uma era da outra. Numa turma isso
vira aluno entregando o circuito de um exercício com o código de outro.

É JSON por baixo (nada de parser próprio para depurar), com duas escolhas
para quem abrir num editor de texto:

```json
{
  "formato": "sktch",
  "versao": 1,
  "gerado": "2026-09-10 06:50",
  "codigo": [
    "// Pisca-pisca de tres LEDs no semaforo.",
    "const int VERMELHO = 14;",
    ...
  ],
  "circuito": { "Pecas": [...], "Fios": [...] }
}
```

- **O código vai em linhas**, não como uma string escapada. Dá para ler
  direto, e o diff de duas versões mostra a linha que mudou em vez de um
  paredão de `
`.
- **O cabeçalho se identifica.** Abrir um arquivo errado falha dizendo o
  que era; um `.sktch` de versão futura é recusado com o motivo, em vez
  de carregar pela metade.

`Abrir` continua aceitando o `.json` de circuito antigo — ninguém perde o
que já tinha salvo.

### O aviso de não salvo agora conta o código

Ele comparava só o circuito: mexer no código e fechar a janela não
avisava nada. Agora a assinatura do projeto inclui as duas partes.

## Marco 16: CircuitSketch, e um furo por terminal

### O programa virou CircuitSketch

Nome novo no título da janela, no executável (`CircuitSketch.exe`), na
placa que aparece na Arduino IDE (`ESP32 (CircuitSketch)`), no mutex de
instância única e no log de erro (`circuitsketch-erros.log`).

O `enviar.bat` lê o caminho do executável de `caminho.txt`, que o
`instalar.ps1` escreve — então **é preciso rodar o instalador de novo**,
senão a IDE ainda procura o `SimuladorESP32.exe`, que não existe mais.

### Dois objetos não cabem no mesmo furo

O encaixe pegava o furo mais **perto**, ocupado ou não. Largar um
resistor em cima de um LED punha os dois no mesmo furo, sem nada avisar —
e na tela o resistor ficava por cima do LED.

Agora ele pega o mais perto que esteja **livre**; se não houver nenhum ao
alcance, não encaixa e a peça fica solta onde foi largada. Medido:

```
resistor 1 ocupa      [B10,B16]
resistor 2 em cima -> [B11,B17]     (escorregou para a coluna livre)
fio já em B10      -> [B11,B17]     (ponta de fio também ocupa)
```

Ponta de fio conta como terminal, que é o que ela é na bancada. Como a
coluna tem cinco furos, ocupar um deixa os outros quatro livres — não
atrapalha ligar mais coisa na mesma coluna.

A busca virou grade indexada em vez de varrer os ~1700 furos por pino,
porque isso roda a cada movimento do mouse durante o arraste: **0,23 ms
por movimento**.

Circuitos salvos antes da regra continuam abrindo como estavam — a regra
vale na hora de encaixar. Basta arrastar a peça sobreposta um pouco para
ela ir para um furo livre.

## Marco 17: colar no ponteiro e menu plano

### Colar onde o mouse está

Colava com um deslocamento fixo, longe de onde a pessoa estava olhando.
Agora o **centro do grupo** vai para o ponteiro, encaixado na grade — sem
isso a peça colada nunca casaria com os furos, já que o ponteiro cai em
qualquer lugar. As peças guardam as distâncias entre si.

Com o ponteiro fora do desenho, volta ao deslocamento de antes, que pelo
menos não empilha em cima do original.

### Os submenus estavam ilegíveis

Mesma armadilha das tabs, num lugar novo: o WPF abre o popup do submenu
com o **fundo claro do sistema**, e a janela escura manda uma `Foreground`
clara por herança. Texto claro em fundo claro.

Em vez de brigar com os recursos de tema do WPF, tirei os submenus: o
menu ficou **plano**, com as quatro operações de arquivo **no topo** — 
salvar agora é um clique, não dois. E o `MenuItem` ganhou template
próprio, com cor, marca de ativo e atalho explícitos, para não depender
mais do tema do sistema.

### Fotografar o menu

O menu é um popup em janela própria e não entra na captura da janela
principal — e é exatamente nele que os problemas de tema aparecem.
`--menu <png>` abre o menu e renderiza o controle, que é a única forma de
conferir isso sem alguém olhando a tela.

## Marco 18: o Upload funciona, e a saída é a de sempre

O botão Upload terminava em erro e o código chegava no simulador assim
mesmo — quem o abria era o hook de postbuild, que roda na compilação.
Eram **três** problemas empilhados, e cada um só aparecia depois de
resolver o anterior.

### 1. O argumento errado

```
tools.enviar.upload.pattern=... enviar.bat UPLOAD "{build.source.path}" ...
```

O `enviar.bat` lia `%1` como a pasta do sketch. No Upload, `%1` é a
palavra `UPLOAD` — ele não achava a pasta e saía com código 1.

### 2. Faltava a porta

Corrigido o argumento, o Upload nem chegava lá: a Arduino IDE exige uma
porta e com a placa fantasma não há nenhuma. Testado no arduino-cli:

```
Error getting port metadata: port not found: COM99 serial
```

A solução é uma **pluggable discovery**: um processo que fala um
protocolo de texto/JSON na entrada e saída padrão e anuncia portas.
`tools/porta.ps1` anuncia uma só, que não existe em lugar nenhum — e é
esse o ponto: dá à IDE algo para selecionar.

### 3. A ferramenta não era resolvida

Com a porta aparecendo em `board list`, o upload ainda dizia *"A
programmer is required to upload"*. Faltava o que toda plataforma real
declara junto do pattern:

```
tools.enviar.upload.protocol=circuitsketch
tools.enviar.upload.params.verbose=
tools.enviar.upload.params.quiet=
```

### E `{build.source.path}` não existe no upload

Ela só é resolvida na compilação; no upload chegava literal, entre
chaves. Agora o postbuild anota o caminho num arquivo dentro da pasta de
build e o upload lê de lá.

### A saída

A IDE não inventa as linhas de memória: ela roda `recipe.size.pattern` e
passa a saída pelas `recipe.size.regex`. Sem isso o build terminava sem
informação nenhuma. Rodando pelo arduino-cli de verdade:

```
Sketch uses 209749 bytes (16%) of program storage space. Maximum is 1310720 bytes.
Global variables use 14237 bytes (4%) of dynamic memory, leaving 313443 bytes ...
CircuitSketch - alvo simulado (nenhuma placa e usada)
Chip: ESP32-D0WD-V3 . 240 MHz . dual core . flash 4MB
Configuring flash size...
Compressed 209749 bytes to 117459...
Writing at 0x00014447... (8 %)
...
Wrote 209749 bytes (117459 compressed) at 0x00010000 in 0.8 seconds (effective 1132 kbit/s)...
Hash of data verified.
```

Os números saem do **tamanho do fonte**, que é uma medida real: mais
código, mais flash. Não é o que o compilador da ESP32 produziria, e por
isso o cabeçalho diz que o alvo é simulado em vez de fingir sondagem de
chip. A conta mora num arquivo só (`medida.ps1`), compartilhada entre o
passo de tamanho e o de gravação, para os dois nunca divergirem.

### Como conferi

`arduino-cli` 1.5.1 num sketchbook temporário, sem tocar na instalação
da IDE: `board listall` mostra a placa, `board list` mostra a porta
virtual, e `compile --upload` fecha com **exit 0** e abre o
CircuitSketch com o sketch.

## Marco 19: as seis placas na Arduino IDE

A IDE oferecia só a ESP32, mas o simulador entende seis perfis. Agora
todas aparecem, cada uma com o que a torna diferente:

```
ESP32 (CircuitSketch)                 1310720 / 327680   esptool
Arduino Uno (CircuitSketch)             32256 /   2048   avrdude
Arduino Mega 2560 (CircuitSketch)      253952 /   8192   avrdude
Arduino Nano (CircuitSketch)            30720 /   2048   avrdude
Nano RP2040 Connect (CircuitSketch)  16777216 / 270336   esptool
Franzininho DIY (CircuitSketch)          6586 /    512   avrdude
```

### Os limites são os reais

É com eles que a IDE calcula a porcentagem, então errar aqui faria o
aluno ver um número que não bate com a bancada. O mesmo sketch, em três
placas:

```
ESP32         249576 bytes (19%) de 1310720
Arduino Uno     7838 bytes (24%) de   32256
Franzininho     6454 bytes (97%) de    6586
```

Os 97% do Franzininho não são enfeite: esse sketch realmente não caberia
num ATtiny85, e ver isso antes de montar o circuito é metade da lição.

### Cada família mostra a sua gravação

As AVR mostram avrdude, com a barra de `#` atualizada por retorno de
carro; as de 32 bits mostram esptool, com as linhas de `Writing at 0x...`.
Mostrar a errada seria tão estranho quanto não mostrar nada.

Nenhuma das duas finge sondagem de hardware: não há chip na porta, então
o cabeçalho diz que o alvo é simulado em vez de inventar revisão de
silício e MAC.

### O piso de memória é por placa

Um sketch vazio ocupa ~197 KB numa ESP32 e ~444 bytes num Uno — a mesma
conta não serve para as duas. `boards.txt` passa o piso e o fator de cada
placa para `tamanho.ps1`, e a conta mora num arquivo só, compartilhada
com a gravação, para os dois passos nunca divergirem.

### O `enviar.bat` virou `ponte.ps1`

Passar dez valores de placa por argumento posicional em lote seria pedir
para errar aspas — e foi um argumento posicional que causou o bug do
Upload em primeiro lugar. O `platform.txt` agora chama PowerShell direto,
com parâmetros nomeados.

## Marco 20: recentes e demos por placa

### Projetos recentes

Submenu ao lado de "Abrir sketch .ino", com os oito últimos projetos.
Some da lista o que foi apagado do disco — apontar para um arquivo que
não existe só serviria para dar erro ao clicar.

Fica em `%APPDATA%\CircuitSketch`, não junto do executável: nos
computadores do laboratório o aluno costuma não ter escrita na pasta do
programa, e uma lista que some a cada aula não serve para nada.

### Demos, uma por placa

```
ESP32        — Semáforo          3 LEDs com resistores
Arduino Uno  — Botão e LED       INPUT_PULLUP
Arduino Mega — Barra de LEDs     10 saídas seguidas
Arduino Nano — Potenciômetro     analogRead + PWM
Nano RP2040  — Servo             Servo.h
Franzininho  — Pisca-pisca       ATtiny85 na mini de 170
```

Vão **embutidas no assembly**, como os SVGs: o executável é um arquivo só
e precisa continuar sendo. Abrir uma demo não conta como projeto salvo,
então salvar pede um lugar novo e o aluno nunca sobrescreve o exemplo.

### Os submenus voltaram, agora funcionando

Eles tinham saído do menu porque o WPF abria o popup com o fundo claro do
sistema. Agora o `MenuItem` tem template próprio **incluindo o Popup dos
filhos**, com fundo e borda explícitos — some a dependência do tema.

### O autoteste achou dois erros nas demos

Antes de qualquer um ver:

- **A mini de 170 tem as linhas em outro lugar.** Sem trilho em cima, a
  área central sobe: A na linha 1 em vez da 4. O gerador usou o layout da
  830 e o LED do Franzininho ficou fora do furo.
- **O RP2040 chama o pino de `3.3V`, não `3V3`.** Um fio da demo do servo
  apontava para pino inexistente.

O teste abre cada demo, confere que todo fio aponta para pino que existe,
que nenhuma peça ficou sem ligação e que os componentes alcançam algum
GPIO. E ele mesmo estava errado num ponto: exigia furo até das peças que
ficam fora de propósito, ligadas por fio. O critério virou **ligação**, não
furo.

### Fotografar um submenu

Popup é outra janela, e só é medido quando aparece de verdade na tela:
nem a captura da janela nem a do menu o alcançam. A captura remonta os
itens num menu próprio — o template é o mesmo, então o que se vê ali é o
que o submenu mostra.

## Marco 21: o bloco fácil

### O fio que sumia

O circuito padrão ligava o LED verde em **`D16`**, e a ESP32 DevKit V1
não tem esse pino — o GPIO 16 se chama `RX2` no header. `PosicaoPino`
devolvia null e o desenho pulava o fio, calado. O LED aparecia sem
ligação e nada explicava.

Corrigido o nome, e **a classe do bug foi fechada**: `FiosQuebrados()`
lista fio para peça ou pino inexistente, e a barra avisa em vez de deixar
o aluno procurando a ligação que "sumiu". O autoteste passa o circuito de
abertura e as seis demos por essa peneira.

### Recusar em vez de aceitar calado

`attachInterrupt` era aceito e não fazia nada: o sketch rodava, a
interrupção nunca disparava e o aluno procurava o erro no próprio código.
Agora recusa dizendo o que fazer:

```
interrupcao ainda nao e simulada. Troque por uma leitura dentro do
loop(): if (digitalRead(pino) == LOW) { ... }
```

O mesmo para `detachInterrupt`, `shiftOut` e `shiftIn`. As inofensivas
continuam passando: `noInterrupts`, `interrupts`, `yield` e
`digitalPinToInterrupt` não mudam nada no simulador, e o sketch roda
igual com ou sem elas.

### Aviso de placa divergente

A placa escolhida na Arduino IDE agora chega ao programa (`--placa`, vinda
do `simulador.peca` do `boards.txt`). Se ela não for a que está desenhada,
a barra diz — quem manda na simulação continua sendo o circuito.

### Os LEDs da ESP32

O SVG do Wokwi desenha os dois encapsulamentos mas não acende nada. As
posições vieram do próprio arquivo: `rect x=35 y=111` e `x=69,5 y=110`,
de 3,8×5,3, num viewBox de 107×201.

- **vermelho**: aceso enquanto a placa está energizada, sem GPIO
- **azul**: segue o GPIO 2, que é o `LED_BUILTIN`

`Maquina.Energizada` liga no `Reiniciar()` e desliga no `Desligar()`.

### Contorno nos fios

Cada fio ganhou um traço escuro por baixo, 2,4 px mais grosso. Onde dois
se cruzam, é ele que diz qual passa por cima.

Escolhi contorno em vez de sombra borrada de propósito: os fios são
refeitos a cada movimento do arraste, e um `DropShadowEffect` por fio
custaria caro nos PCs do laboratório. Medido depois da mudança: **0,01 ms
por quadro**, o mesmo de antes.

## Marco 22: moldar os fios

Do jeito do Wokwi: bolinha **cheia** em cada canto, bolinha **vazada** no
meio de cada trecho. Arrastar a vazada cria um canto; arrastar a cheia
move; clique duplo desfaz. `Alt` solta da grade.

`Fio.Dobras` guarda os cantos, e o traçado passa a ser a linha quebrada
que liga pino, cantos e pino — a partir do momento em que alguém move um
canto, quem manda na forma é ela, não o estilo. Sem cantos, o fio volta
ao formato do estilo.

Os cantos entram no `.sktch`, no `Curva`, no `NaCurva` (hit-test e laço)
e no botão **endireitar** da tab do fio.

### Canto que não dobra nada some sozinho

Arrastar uma alça até a linha dos vizinhos deixaria um ponto inútil no
meio de um trecho reto, gerando mais uma alça para atrapalhar.

A primeira versão media a **área do triângulo** formado pelos três
pontos. Errado: a área cresce com o comprimento do fio, então um limite
fixo reprovava fio curto e aprovava fio longo — o teste pegou. Agora é a
**distância do canto até a reta dos vizinhos**, comparada com meio passo
da grade.

### Duas placas no mesmo circuito

O simulador roda **um** programa numa `Maquina` só: a primeira placa
define o perfil e as outras apenas despejam os pinos no mesmo espaço de
GPIO. Com duas ESP32, o `D14` de qualquer uma aciona a mesma saída.

Não dava para descobrir isso olhando, então agora avisa. O conserto de
verdade — um sketch por placa, com o editor mostrando o código da que
está selecionada — está na lista.

## Marco 23: `String` e o diagrama do Wokwi de verdade

Dois itens do bloco médio, os dois de "atrapalha uma aula".

### Métodos de `String`

`String msg = "a"; msg += "b";` sempre funcionou, mas `msg.length()`
parava a execução com "não é suportado". O motivo era estrutural: o
motor guarda texto num `Valor`, e `Invocar` só sabia chamar método de
**objeto de biblioteca** (`lcd`, `servo`). Uma variável de texto não é
objeto, então caía direto no `throw`.

Agora, antes de recusar, `Invocar` pergunta se a variável tem texto e
manda para `ChamarTexto`: `length`, `charAt`, `substring`, `indexOf`,
`lastIndexOf`, `equals`, `equalsIgnoreCase`, `startsWith`, `endsWith`,
`toUpperCase`, `toLowerCase`, `trim`, `replace`, `concat`, `isEmpty`,
`toInt`, `toFloat`, `c_str`.

Duas escolhas seguem o Arduino em vez do C#:

- **Fora do intervalo não estoura.** `charAt(9)` num texto de 2 letras
  devolve vazio, `substring(9)` idem. No hardware real também não
  estoura, e travar o sketch por isso ensinaria a coisa errada.
- **`toInt()` para no primeiro caractere que não serve.** `"3.5cm"`
  vira 3,5 em `toFloat()`; `"abc"` vira 0. É o comportamento deles.

Método que não existe continua recusando com erro — passar 0 calado é
o que dá aula perdida procurando o bug.

### `board-esp32-devkit-c-v4`

O `diagram.json` que veio da turma trazia a ESP32 e 42 ligações, e na
tela apareciam LEDs soltos. Três problemas empilhados:

1. **A placa vinha como `board-esp32-devkit-c-v4`.** O importador só
   conhecia `wokwi-*`, então pulava a placa — e com ela **todos** os
   fios, porque cada um tem uma ponta nela. Agora qualquer `board-*` é
   traduzido, e as variantes de ESP32 caem na DevKit V1.
2. **Os pinos vinham por número** (`"4"`, `"16"`), e aqui o nome é o da
   serigrafia. `4` vira `D4`; `16` e `17` viram **RX2** e **TX2**, que
   é o que está impresso na placa para esses GPIOs.
3. **`$serialMonitor` não é peça**: é o monitor da tela deles. As duas
   ligações que apontam para ele são puladas sem alarde.

O 4º campo da ligação — o caminho do fio, em passos relativos como
`["v75.6", "h336", "v163.2"]` — antes era descartado. Agora vira
`Dobras`, então **o fio importado tem o mesmo desenho que tinha lá**.
Passo de tamanho zero (`["v0"]`, o mais comum) não gera canto nenhum.

Fio cuja ponta não existe aqui não é criado: some do desenho sem avisar
e deixa a pessoa procurando o que quebrou. Em vez disso ele é contado e
o aviso diz **quais** ficaram de fora.

### Conferindo sem abrir a janela

`--wokwi <arquivo>` importa e escreve o relatório: peças por tipo, cada
fio com suas dobras, tipos sem equivalente, fios perdidos e fios
quebrados. `--para <arquivo.sktch>` grava o resultado.

No arquivo da turma: **27 peças, 40 fios, 0 perdidos, 0 quebrados** —
os 42 menos as duas do monitor serial.

De quebra, a barra de status agora recontabiliza ao abrir um circuito
por caminho; antes continuava mostrando a contagem do circuito anterior.

## Marco 24: um sketch por placa, e componente que queima

### O código é da placa

Com duas placas no circuito, as duas dividiam o mesmo campo de texto:
não havia como ter um programa para cada, e nem como saber de quem era
o código que estava na tela.

Agora o sketch mora em `PecaNoCircuito.Codigo` e o campo mostra o da
placa **selecionada**, com o nome dela numa faixa em cima. Clicar na
outra placa troca o código; clicar num LED não mexe em nada — senão
editar o circuito viraria um pisca-pisca de código.

Três detalhes que o teste cobra:

- **Placa apagada não doa o código para a que sobrou.** Quando a dona do
  texto some, ele deixa de ser de alguém em vez de ser adotado: colar o
  código da placa apagada por cima do da outra seria pior que perdê-lo.
- **A assinatura de "não salvo" usa sempre a primeira placa**, não a que
  está na tela. Sem isso, clicar na outra placa já pedia para salvar.
- **Placa nova ganha um pisca com `LED_BUILTIN`**, não o modelo de
  abertura — ele acende os GPIO 13, 14 e 16, que existem na ESP32 e não
  num Arduino Uno.

O arquivo continua trazendo o campo `codigo` solto, com o da primeira
placa, para quem abrir o `.sktch` de fora. Quem roda continua sendo uma
placa por vez — a que está no campo — e o aviso agora diz qual.

### 💥 Queimar o que está ligado errado

O simulador era bom demais: LED direto no GPIO, sem resistor, acendia
bonito. Esse mesmo circuito apaga o LED de vez na bancada. Simulador que
só mostra o certo ensina o errado.

`Queima.Conferir` roda a cada quadro e pega três erros de aula:

1. **Curto de alimentação** — 3V3 (ou 5V, VIN, VCC) no mesmo nó do GND.
2. **Saída em curto com o terra** — pino em OUTPUT e HIGH ligado direto
   no GND, sem nada no meio.
3. **LED sem resistor** — um lado numa fonte, o outro no terra, e nenhum
   resistor em nenhum dos dois lados. Vale para o LED comum, para as
   três cores do RGB e para os dez da barra.

A regra do resistor sai de graça do netlist: o resistor une as duas
pernas, então "tem resistor em série" é o mesmo que "um dos nós do LED
tem perna de resistor" — sem varrer caminho nenhum.

Não é conta elétrica: não há corrente nem tensão aqui, só nós ligados
entre si. Mas dá o mesmo resultado para os erros que aparecem em aula, e
o que decide se algo queima é o momento em que o **código acende o
pino** — que é justamente a hora de mostrar.

A peça queimada ganha o 💥, para de funcionar (LED queimado não acende
mais) e a razão aparece na barra de status e no monitor serial. Rodar de
novo devolve tudo inteiro: ou o erro foi corrigido, ou queima outra vez
em segundos.

### O alarme falso é o risco de verdade

Um estouro em circuito certo custaria mais caro que o recurso inteiro,
então o teste liga **todas** as saídas em HIGH ao mesmo tempo e passa o
circuito de abertura e as seis demos: nenhuma queima. E os casos que
**não** podem queimar estão escritos um a um — resistor no ânodo,
resistor no catodo, pino em LOW, catodo no ar, resistor de pino para o
terra, circuito parado.

### Conferindo

`--rodar` liga a simulação antes da foto do `--captura`. Sem ele a foto
mostra o circuito parado, e o que precisa ser visto — LED aceso, peça
queimada — só existe rodando.

## Marco 25: o monitor serial da Arduino IDE

Quem manda o sketch pela IDE fica olhando para a IDE. Só que o monitor
serial dela abria **vazio**: a placa é fantasma, não há porta de verdade
do outro lado, e os `Serial.println` do exercício — que é como o aluno
depura — apareciam só dentro do CircuitSketch.

Agora a plataforma traz um **pluggable monitor**. Ele é um processo que
lê comandos em texto e responde JSON, igual à porta virtual do Marco 18;
no `OPEN` a IDE passa um endereço TCP, e alguém precisa abrir uma conexão
para lá — aquilo vira o cano de dados.

### Por que um arquivo no meio

O CircuitSketch escreve cada linha do monitor em
`%APPDATA%\CircuitSketch\serial.txt`, e o cano despeja esse arquivo no
socket. Arquivo, e não pipe nem socket direto, por dois motivos:

- **Dá para depurar com o Bloco de Notas.** Quando a integração não
  funcionar num computador do laboratório, abrir o arquivo já diz de que
  lado está o problema. A alternativa era adivinhar.
- **Nenhum dos dois precisa do outro vivo.** O simulador escreve mesmo
  sem a IDE aberta, e o monitor pode abrir no meio da execução — ele lê
  desde o começo do arquivo, então aparece tudo que já saiu, e não uma
  tela em branco.

Arquivo que **encolhe** é execução nova: o cano volta ao início e
escreve `--- placa reiniciada ---`, que é o que a IDE mostra quando a
placa reseta.

### Dois processos, não duas threads

A primeira versão bombeava numa thread dentro do mesmo processo e o
`CLOSE` derrubava o monitor inteiro, em silêncio: ler stdin bloqueia, e
o protocolo tem que continuar respondendo enquanto os dados correm.

Agora são dois: `monitor.ps1` fala o protocolo, `monitor-cano.ps1`
despeja o arquivo no socket. Fechar é matar. De quebra dá para rodar o
cano sozinho, para saber se o problema é do cano ou do protocolo.

### Só o sentido simulador → IDE

Mandar texto da IDE para o sketch pediria `Serial.read`, que o motor
ainda não tem. Um monitor que aceita texto e joga fora seria pior do que
um que não aceita.

### Como isso foi provado

Metade é código nosso e está no autoteste: o arquivo existe, tem o que o
sketch imprimiu, zera a cada execução e continua gravável com o cano
lendo junto (arquivo travado deixaria o monitor da IDE preto sem dizer
por quê).

A outra metade são dois processos de PowerShell e um socket, e isso
precisa ser provado de fora:

    python ferramentas/provar-monitor/provar-monitor.py

Ele faz o papel da IDE — HELLO, DESCRIBE, OPEN, CLOSE, QUIT — e confere
o que chega no socket.

E, por fim, com o **arduino-cli 1.5.1 de verdade**:

    arduino-cli monitor -p circuitsketch -l circuitsketch \
                        -b circuitsketch:esp32:simesp32 --timestamp

As linhas do simulador aparecem lá. O `-l circuitsketch` importa: sem
ele o CLI assume o protocolo `serial` e usa o monitor embutido, que
responde "Serial port not found". A IDE não tem esse problema — ela usa
o protocolo que a discovery anunciou.

O primeiro byte leva ~3 s (dois PowerShell frios até o cano subir). Nada
se perde nesse tempo, porque o cano lê desde o começo do arquivo.

### E dentro da Arduino IDE? Ainda não

Provado na IDE 2.3.6, não deduzido. O log do daemon mostra o
`HELLO` → `DESCRIBE` → `CONFIGURE baudrate 115200` →
`OPEN 127.0.0.1:51890 circuitsketch`, todos com `error=false`, seguidos
de `started monitor to circuitsketch using circuitsketch`. O socket fica
estabelecido (dá para ver no `Get-NetTCPConnection`) e o cano manda os
bytes — o mesmo cano, no mesmo instante, entrega tudo para um listener
de teste.

**E o painel fica vazio.** A IDE abre o monitor, conecta, recebe e não
desenha. A suspeita é que o widget "Serial Monitor" da IDE 2.x só
renderiza porta de protocolo `serial`.

Testei essa suspeita trocando o protocolo da porta virtual para
`serial`. Deu errado de um jeito pior: a IDE **parou de enxergar a
porta** (`calling HELLO: timeout`), e sem porta não há Upload. Trocar o
que funciona pelo que talvez funcione não vale, então voltou tudo ao
protocolo `circuitsketch`.

Enquanto isso, o caminho que funciona é o `arduino-cli`.

### O prazo do HELLO

Enquanto media isso apareceu outra coisa: às vezes a IDE não enxerga a
porta, e o log diz

    Waiting discovery process termination: exit status 128
    Discovery circuitsketch failed to run: calling HELLO: timeout

Sem porta não há Upload — nem monitor, porque o painel só conecta em
porta presente. O Marco 26 conta o que essa investigação achou (e o que
ela **não** achou).

## Marco 26: `enum`, e uma caçada que não terminou

### `enum` — o sketch da turma não rodava

Achado do jeito certo: mandando o sketch de verdade pela Arduino IDE e
vendo onde o CircuitSketch parava. Linha 23,
`enum Modo { ALTERNADO, WIGWAG, ... };`, com
`esperava ';' mas encontrei '{'`. O programa inteiro morria ali — e
enum é a primeira coisa que aparece quando o exercício tem mais de um
modo de funcionamento.

Aceitar a declaração não bastava: o **nome** do enum precisa virar um
tipo do sketch, senão `Modo m`, `(Modo)x` e o parâmetro
`nomeModo(Modo m)` continuam sem passar. Então a lista de tipos deixou
de ser estática e passou a ser **por `Parser`** — um `enum Modo` de um
sketch não pode virar tipo conhecido do sketch seguinte, e há um teste
só para isso.

Cobre `enum`, `enum class`, `enum Bits : uint8_t`, valores escritos
(`X = 5` e a contagem continuando em 6), enum dentro de função e
`enum Modo { ... } atual = C;`. Membro que vem depois de um valor que só
dá para saber rodando (`A = f(), B`) é **recusado com explicação**, em
vez de receber um número inventado.

O sketch da turma agora roda inteiro: 60 s virtuais, 4770 eventos de
pino, zero erros — e na tela, LEDs acesos e
`Luz de policia iniciada - modo ALTERNADO` no monitor.

### A porta que a IDE às vezes não vê

Aqui a investigação **não** terminou, e vale registrar o que foi medido
para ninguém repetir o caminho.

**Primeira hipótese: partida lenta do PowerShell.** Errada. Medido:
`cmd.exe /c porta.bat` responde o `HELLO` em **1,7 s** (pior caso 2,0 s),
e o prazo do arduino-cli é de 10 s. E o log mostra `exit status 128`
*antes* do timeout: o processo **sai sozinho**, e só então o `HELLO`
estoura por não ter mais ninguém do outro lado.

**Segunda tentativa: trocar o script por um executável nativo.**
Construído (`plataforma/fonte/porta`, NativeAOT, 933 KB), sobe em
**150 ms** contra 1700 ms, e fala o protocolo certo — o próprio daemon
registra `Received message type: add, port: circuitsketch`. E mesmo
assim `board list` devolve `"detected_ports": []`. Com o script de volta,
a mesma porta aparece. Não achei a causa; o executável ficou como fonte,
fora do caminho, e a descoberta continua sendo o script que funciona.

**O que se sabe hoje:** pelo `arduino-cli` a porta aparece; dentro da
IDE ela falha em algumas partidas e volta em outras (os uploads das
18h34 e 18h36 aconteceram depois de uma falha das 18h26). Quando a IDE
diz `[not connected]`, fechar e abrir costuma resolver.

### Uma correção sobre o Marco 25

O que está escrito acima corrige o que eu havia registrado antes: que a
causa era a partida do PowerShell e que bastava um executável. Medi as
duas coisas e nenhuma se sustentou.

## Marco 27: cinco da lista

### Fio que cobre outro sai de cima dele

Dois fios entre os mesmos dois pinos desenhavam **exatamente** o mesmo
traço: viravam um só na tela, e apagar "o de cima" apagava o que a
pessoa não estava vendo. O sombreado do Marco 21 ajudou a perceber, mas
eles continuavam empilhados.

As **pontas não se mexem** — elas estão nos pinos, e tirar o fio do pino
para separar seria trocar um problema por outro. O que se afasta é o
miolo: no *reto*, um ponto novo no meio; no *degrau*, os dois cantos.
Com três fios eles se espalham em torno do lugar original, sem que
nenhum "mude de lugar" mais que os outros.

*Solto* fica de fora de propósito: a curva dele já depende da distância
entre os pinos, então dois soltos já caem por caminhos diferentes. Fio
com dobra também: quem moldou o fio à mão decide por onde ele passa.

O teste que importa não é o desenho, é o **clique** — se os dois se
afastam na tela mas o clique acha sempre o mesmo, nada foi resolvido.

E o próprio teste pegou uma regressão de tabela: ao usar o traçado
desenhado para montar as alças, o ponto do meio de um fio afastado
virava "canto", e o fio reto passava a ter três alças em vez de uma.
Quantas alças existem sai das **dobras**, não do desenho.

### Interrupção de verdade

`attachInterrupt` era recusado com uma mensagem mandando ler o pino
dentro do `loop()`. Honesto — muito melhor que aceitar calado e nunca
disparar —, mas não ajudava quem **precisa** de interrupção, que é o
exercício de contar pulso de botão.

Agora funciona, com uma limitação dita na cara: a interrupção é atendida
**entre voltas do `loop()`** e depois de cada `delay()`, não no meio de
uma expressão. Imitar o corte de verdade pediria interromper a execução
em qualquer ponto; o efeito que o aluno vê é o mesmo — o sketch reage ao
botão sem tê-lo lido — e o estado nunca fica pela metade.

`RISING`, `FALLING` e `CHANGE` valem. `noInterrupts()` segura a entrega,
e o que passou enquanto estavam desligadas **não volta depois**, que é o
que o hardware faz. Ligar a vigia com o pino já mexido não dispara
sozinho. Apontar para uma função que não existe é recusado com o nome
dela.

`shiftOut` e `shiftIn` também saíram do "não suportado": ninguém simula
o 74HC595 aqui, mas os pinos mexem de verdade — oito pulsos de relógio,
visíveis na linha do tempo.

De brinde, o teste achou outra coisa: **`volatile` não era aceito**. E
todo exemplo de interrupção começa com `volatile int cliques = 0;`.

### `attachInterrupt(0, contar, FALLING)` não passa pelo caminho normal

O 2º argumento é o **nome de uma função**, não um valor. A avaliação dos
argumentos acontece antes do `switch`, então `contar` era avaliado como
variável e dava "variável 'contar' não foi declarada". Esta chamada é
desviada antes.

### Arrastar da paleta para a tela

A paleta virou uma janela que **fica aberta**, e não um diálogo modal —
por um motivo mecânico: diálogo modal desabilita a janela de trás, e
janela desabilitada não aceita arrastar-e-soltar. Só o id da peça viaja
no arrasto; o objeto é montado no editor, já no ponto onde foi solto e
passando pelo mesmo encaixe nos furos de sempre.

Enter e duplo clique continuam pondo no meio da tela, para quem prefere
o teclado.

### Exportar `diagram.json`

O caminho de volta, tradução inversa da importação do Marco 23: a placa
sai como `board-esp32-devkit-c-v4`, o pino volta a ser número (`D4` →
`4`, `RX2` → `16`) e as dobras viram o caminho relativo (`h192`,
`v-76.8`).

A prova não é conferir o texto campo a campo — daria para escrever um
arquivo bonito que o Wokwi não abre. A prova é a **ida e volta**:
exportar, importar de volta pelo nosso próprio importador (que foi
escrito contra um arquivo de verdade) e achar o mesmo circuito.

E a ida e volta achou um limite do formato deles: só há passos `h` e
`v`, **não existe trecho na diagonal**. Um canto que precisa dos dois
eixos sai como dois passos, e o fio chega lá em L, com um canto a mais.
Não dá para evitar sem inventar sintaxe que o Wokwi não lê. O teste
passou a cobrar o que de fato importa: o fio continua passando pelos
cantos que a pessoa colocou.

### Destaque de sintaxe

O editor de código virou um `AvalonEdit`, com número de linha de brinde
— o que ajuda quando o erro diz "linha 23". As palavras que ganham cor
saem da tabela do **interpretador**, não de um C++ genérico: pintar
`malloc` de azul prometeria uma coisa que o motor recusa.

Duas lições dessa parte:

- **`\b` dentro de uma raw string do C# não é `\b`.** Ali a barra é
  literal, então a regra de número chegava no motor de regex como
  barra-barra-b e nunca casava. Os números ficavam sem cor. A definição
  é montada com verbatim (`@"..."`), que é o que uma expressão regular
  quer.
- **Olhar a tela não serve para conferir cor.** Eu dei `pinMode` como
  "sem cor" olhando a captura, e estava errado: `#DCDCAA` é um creme
  pálido que passa por branco numa foto reduzida. Quem resolveu foi um
  teste que aplica o realce e **lê a cor de cada palavra**. Ele também
  cobra o que mais importa: comentário e texto entre aspas ganham a cor
  deles, e não a de código.

## Marco 28: três dos sete que só desenhavam

O inventário do autoteste dizia **7 aceitos sem efeito**. Agora diz 4.

### Relé KS2E

O mais pedido dos sete, porque é o exercício de "a placa liga uma carga
que ela não aguenta" — e sem simulação o aluno não via nada acontecer.

O netlist é estático (refeito quando o circuito muda, não a cada
quadro), então não dá para religar `P` em `NO` de verdade. O que dá, e é
o que o exercício mostra, é **levar o nível**: se o contato que fechou
tem terra de um lado e um GPIO do outro, o GPIO passa a ler aquele
nível. É o mesmo caminho do botão, que também não conduz no netlist.

Em repouso vale `NC`, com a bobina ligada vale `NO` — o que está
impresso na peça. As duas vias funcionam separadas, e uma luz no corpo
mostra se a bobina pegou, porque o efeito acontece do outro lado do
componente.

**O teste pegou um erro meu de raciocínio.** A primeira versão soltava o
nível do contato que abria — mas o GPIO costuma estar no **comum** (`P`),
não no contato, então o nível antigo ficava grudado e o relé nunca
"desligava". Soltar tudo também não serve: apagaria o nível que outra
peça esteja impondo no mesmo pino. Cada relé passou a lembrar quais
pinos **ele** está segurando.

### Controle remoto e receptor infravermelho

O receptor tinha meio caminho feito — `decode()` já olhava uma leitura.
O que não passava era o jeito como o sketch é escrito em aula:

```cpp
decode_results results;
if (irrecv.decode(&results)) { Serial.println(results.value); irrecv.resume(); }
```

Faltavam três coisas no motor, e **nenhuma é do infravermelho**:

1. o parser não aceitava `&` (o "endereço de");
2. o interpretador não lia campo com ponto (`results.value`) — dava
   "não pode ser lido assim";
3. não existia a classe `decode_results`.

As três foram resolvidas de forma geral. `&x` devolve o próprio `x` (não
há ponteiro aqui); quem precisa do objeto apontado — só o `decode()`,
por enquanto — olha o **argumento** em vez do valor, do mesmo jeito que
`attachInterrupt` olha o nome da função. Campo que não existe é recusado
dizendo quais existem.

`resume()` **consome** o código: sem isso um aperto virava dez, um por
volta do `loop()`.

E o par tem que estar completo: **sem receptor no circuito, o controle
não chega a lugar nenhum**. O código do controle é uma grandeza solta
(infravermelho não tem fio), e sem essa checagem o sketch recebia código
com o receptor fora da tela — justamente o erro de montagem que o aluno
precisa ver.

### O que ainda só desenha

`ili9341` (aceita os desenhos, não mostra nada), `microsd-card` (sem
sistema de arquivos), `rotary-dialer` (sem pulsos de discagem) e
`biaxial-stepper` (sem sequência própria). Estão no inventário do
autoteste com o motivo de cada um — dizer "52 de 52 funcionam" seria
mentira.

## Marco 29: os outros quatro, e a lista zera

O inventário do autoteste tinha uma lista chamada `aceitosSemEfeito`,
com sete nomes. **Hoje ela está vazia.**

### Motor de passo biaxial

A conta sai dos **pinos**, não da biblioteca `Stepper` — o exercício de
motor de passo costuma ser escrever a sequência à mão com
`digitalWrite`, e quem faz isso nunca cria um objeto `Stepper`. Lendo as
bobinas, os dois jeitos funcionam.

A fase é a primeira bobina ligada; o passo sai da **diferença** para a
fase anterior, e é daí que vem o sentido — o que importa é o motor andar
**para trás** quando a sequência está invertida, senão o aluno não tem
como descobrir que trocou dois fios. Salto de duas fases é ambíguo e não
conta: preferir não contar a contar errado, porque o ângulo é acumulado
e um passo inventado fica errado para sempre.

Um ponteiro por eixo mostra o giro. Sem ele o motor gira e ninguém vê.

### Disco de telefone

Não é um controle de posição como o potenciômetro: é um **evento com
duração**. Discar o 3 é mandar três pulsos, um depois do outro, e o
sketch conta as bordas — um deslizante não teria como representar isso.

Uma borda por quadro dá ~12 pulsos por segundo, perto dos 10 pps do
telefone de verdade e devagar o bastante para contar sem interrupção.
`DIAL` fica em LOW enquanto o disco volta; `PULSE` é quem pulsa. E o
**0 manda dez pulsos**, que é uma das coisas que o exercício existe para
mostrar.

### Cartão microSD

O exercício que pede cartão SD é sempre o mesmo — o **datalogger**: ler
um sensor, gravar uma linha e depois abrir o arquivo para conferir.

Os arquivos vivem em memória e somem ao reiniciar a placa, e isso é
**dito no monitor** quando o sketch chama `SD.begin()`. É a escolha
honesta: um arquivo de verdade no disco do laboratório traria pergunta
demais (onde fica? some quando? o aluno seguinte vê o meu?) para o que o
exercício precisa mostrar.

Escrever **acrescenta**, como no Arduino. Faltavam duas coisas no motor
para o sketch de sempre passar: `FILE_WRITE` não existia como constante,
e o parser não aceitava `File dados = SD.open(...)` — declaração de
objeto com valor inicial, que parava com "esperava ';' mas encontrei
'='". As duas foram resolvidas de forma geral.

### Display TFT ILI9341

Era o pior dos sete: as chamadas eram aceitas **em silêncio** e
devolviam `true`, então o sketch parecia funcionar e a peça continuava
um retângulo preto. Nada indicava que faltava alguma coisa.

Agora há um buffer de pixels de verdade — `fillScreen`, `drawPixel`,
`drawLine` (Bresenham), `drawRect`, `fillRect`, `drawCircle` e
`fillCircle` (ponto médio) —, e o editor mostra o buffer com
`WriteableBitmap` em vizinho-mais-próximo, para pixel continuar pixel.

**Pixels e texto ficam separados**, e não é preguiça: desenhar letra a
letra exigiria embutir uma fonte de bitmap, e numa tela de 240x320
mostrada em dois centímetros uma fonte 5x7 vira borrão. O texto guarda
posição, cor e tamanho, e o editor desenha com fonte de verdade por
cima.

Conferir **pixel** é o único jeito honesto de testar desenho: se
`drawLine` só incrementasse um contador, o teste passaria com a linha
saindo no lugar errado.

### O erro que a tela preta escondeu

Tudo passava no teste e a tela continuava preta na janela. A causa não
estava no desenho: as limpezas que eu tinha escrito para a tela do TFT,
a luz do relé e o ponteiro do motor foram parar dentro de
`ApagarQueimaduras()` em vez de `Redesenhar()` — e **`Rodar()` chama
`ApagarQueimaduras()`**. Ao rodar, a tela era removida do canvas e
ninguém a recriava, porque ela só nasce em `CriarVisual()`.

O relé e o motor sobreviveram ao mesmo erro porque se recriam sozinhos a
cada quadro. Foi essa diferença que escondeu o problema — e foi ela que
o denunciou: dois componentes com a mesma limpeza, um funcionando e o
outro não.

O teste que faltava agora existe: ele confere que a peça **ganha uma
tela no editor**, e não só que o buffer está certo.

## Marco 30: duas placas ao mesmo tempo

Era a obra grande da lista. Desde o começo o simulador tinha **uma**
máquina: com duas placas na tela, o `D14` de qualquer uma acionava a
mesma saída, o `Serial.println` das duas caía no mesmo monitor e só um
programa rodava. O Marco 24 deu um sketch a cada placa, mas quem
executava continuava sendo um só — e o aviso na barra dizia isso, o que
é honesto e não resolve.

Agora cada placa é uma `Maquina`, com o seu perfil, o seu monitor, o seu
relógio e a sua thread.

### Quem decide de qual placa é cada pino é o netlist

Foi a decisão que fez o resto caber. `Netlist.PinoDe` devolve **a placa
e o GPIO juntos**, em vez de só o número:

```csharp
var (placa, gpio) = rede.PinoDe(circuito, peca, pino);
```

Devolver os dois juntos não é capricho: perguntar só o número levava a
indexar a máquina errada, e esse erro **não aparece em circuito de uma
placa só** — que é o caso de quase todo teste.

### Uma peça, uma placa — resolvida num lugar só

O editor tinha 36 usos de `maq.` e 24 consultas de GPIO, todos no mesmo
formato. Trocar os 36 seria pedir para esquecer um.

Em vez disso, `MaquinaDaPeca` resolve **uma vez por peça**, no topo do
laço, e o corpo inteiro do desenho continua escrito como se houvesse uma
máquina só. É o único lugar do editor que sabe que existe mais de uma
placa.

Peça ligada em duas placas (um botão compartilhado) fica com a primeira.
É uma ambiguidade de verdade — ninguém sabe qual das duas deveria mandar
—, e escolher em silêncio é melhor que sortear a cada quadro. Peça solta
fica com uma máquina desligada, então apaga em vez de guardar o último
estado.

### A ponte que manteve 535 testes de pé

`Bancada.DeUmaSo(maq)` devolve uma bancada em que **toda** consulta cai
na mesma máquina — exatamente o comportamento anterior. Os testes de uma
placa e os modos de linha de comando continuam chamando
`AplicarEntradas(maq, rede)` sem saber que bancada existe.

Sem essa ponte, esta mudança teria mexido em dezenas de testes ao mesmo
tempo em que mexia no que eles testam — e aí eles não valeriam nada
como rede de proteção.

### O que roda, e o que a janela mostra

Todas as placas compilam **antes** de qualquer uma começar: metade
rodando e metade com erro de sintaxe seria pior de entender do que
nenhuma rodando. Erro de execução passa a dizer de qual placa veio —
"erro na linha 12" sem dizer onde manda procurar no sketch errado.

Os botões Rodar/Parar só voltam quando a **última** placa para.

O monitor serial, os avisos e a faixa do ADC nos rótulos são os da placa
**selecionada** — a mesma cujo código está no editor. As duas coisas
andam juntas de propósito: o cabeçalho do editor já diz qual é.

O buzzer toca o tom da placa em que **ele** está ligado, que pode não
ser a que está no editor. A queima também passou a olhar a máquina
certa: o "GPIO 14" de uma não diz nada sobre o da outra, e olhar a
errada acusaria curto que não existe.

### A prova

O teste que importa é o do meio: **o mesmo número de GPIO nas duas
placas, com valores diferentes**. É o caso que a máquina única errava, e
o que nenhum circuito de uma placa revela. Apagar uma não pode apagar o
LED da outra.

Na tela: uma ESP32 e um Uno, cada um com o seu sketch, cada LED aceso
pela sua placa.

## Marco 31: a caçada do Monitor Serial, e onde ela parou

Este marco não entrega um recurso. Entrega **o que foi medido**, para
ninguém (nem eu) repetir o caminho achando que é novidade.

### O sintoma

A Arduino IDE mostra `Not connected. Select a board and a port to
connect automatically.` no Monitor Serial, e o diálogo de portas diz
`NO PORTS DISCOVERED`.

### O diário

A descoberta e o cano do monitor passaram a escrever um diário em
`%TEMP%\circuitsketch-porta.log` e `%TEMP%\circuitsketch-cano.log`. Era
o que faltava: o log da IDE só dizia `exit status 128` e
`calling HELLO: timeout`, o que não explica nada e ainda aponta para o
lado errado.

Com o diário, o outro lado da conversa apareceu:

```
23:14:31.392 [12248] subiu | politica=Bypass | pasta=...
23:14:31.605 [12248] recebeu: HELLO 1 "arduino-cli/1.5.1 arduino-ide/2.3.10"
23:14:31.758 [12248] recebeu: START_SYNC
```

O processo sobe, recebe os dois comandos e responde com o `add`. Deste
lado está tudo certo.

### O que foi eliminado, com medida

- **Lentidão da partida.** Medido: o `HELLO` é respondido em **1,7 s**
  (pior caso 2,0 s) e o prazo do arduino-cli é de 10 s. E o log mostra
  `exit status 128` *antes* do timeout — o processo saía sozinho, não
  atrasava.
- **A pilha `cmd.exe` → PowerShell.** Chamar o script direto não mudou
  nada.
- **Script morrendo.** O diário prova que ele vive e responde.
- **Um executável nativo no lugar do script.** Construído (NativeAOT,
  933 KB, `HELLO` em 150 ms). O daemon registrava
  `Received message type: add, port: circuitsketch` e mesmo assim
  `board list` devolvia vazio. Fonte guardado em
  `plataforma/fonte/porta`, fora do caminho.
- **O nome do protocolo.** Testei `serial` no lugar de `circuitsketch`,
  desta vez com o diário para separar "descoberta falhou" de "a IDE
  ignorou". A descoberta estava sã e a IDE continuou dizendo
  `NO PORTS DISCOVERED`.

### O achado que fecha o diagnóstico

**O Upload funciona.** Com a porta invisível no diálogo e no monitor, o
botão de gravar compila, grava e abre o CircuitSketch com o sketch
rodando — pela mesma porta.

Ou seja: **o daemon tem a porta; a interface da IDE é que não a
mostra.** O diálogo de portas nem encontra a nossa placa na busca,
embora ela esteja selecionada na barra de cima.

Isso põe a causa dentro da Arduino IDE 2.3.10, no tratamento de
plataforma instalada à mão (pasta `hardware/`) que traz a própria
descoberta — fora do que dá para consertar do nosso lado.

### De quebra, um teste instável consertado

O arnês `ferramentas/provar-monitor` passou a falhar. Não era o produto:
o cano é um PowerShell novo e leva ~2 s para nascer e conectar, e o
teste já tinha trocado o conteúdo do arquivo quando ele chegou. Passava
sozinho e falhava no meio de uma compilação — o pior tipo de teste.
Agora ele **espera o cano dar sinal de vida** antes de mexer no arquivo.

E uma armadilha para quem for rodar o arnês: com o **CircuitSketch
aberto**, ele zera o `serial.txt` a cada execução e briga com o teste.
Feche o simulador antes.

### O que funciona hoje

- **O monitor do próprio CircuitSketch**, que está ao lado do circuito.
- **Pelo `arduino-cli`**, que é o mesmo binário que a IDE usa:

      arduino-cli monitor -p circuitsketch -l circuitsketch \
                          -b circuitsketch:esp32:simesp32 --timestamp

  O `-l circuitsketch` importa: sem ele o CLI assume protocolo `serial`
  e usa o monitor embutido, que responde "Serial port not found".

O espelho continua sendo escrito em `%APPDATA%\CircuitSketch\serial.txt`
o tempo todo, então no dia em que a IDE passar a mostrar a porta, o
monitor funciona sem mexer em nada.

## Marco 32: os quatro incômodos

Nenhum destes impedia uma aula. Todos apareciam na hora errada.

### A interrupção agora corta entre comandos

Era atendida só **entre voltas do `loop()`**. Um sketch preso num
`while` dentro do loop nunca atendia nada — e esperar num `while` é
exatamente o que se faz enquanto se espera um botão.

Agora ela é atendida a cada **comando**, que é onde o sketch pode ser
cortado sem deixar estado pela metade (uma expressão no meio da conta
não pode). Não é o corte em qualquer ponto do hardware de verdade, e
isso continua escrito aqui — mas o `while` esperando botão passou a
funcionar, que era o caso que doía.

Custa uma comparação por comando quando não há interrupção registrada,
que é o caso de quase todo sketch. Tem teste de tempo para isso: 20
voltas de 2000 passos em **74 ms**.

### O TFT deita

`setRotation()` era aceito, guardado e não virava nada. Agora:

- `width()` e `height()` **trocam de lugar** — sketch que centraliza com
  `width()/2` depende disso para não escrever fora da tela;
- o ponto que o sketch pede cai no lugar certo do painel;
- desenhar em `x = 300` passa a ser válido deitado, e continua sendo
  cortado em pé.

**O painel não gira.** Ele é sempre 240x320, e quem gira é a conta que
leva o `(x,y)` do sketch até o pixel. Isso mantém quem mostra a tela
simples — e o texto, que é desenhado por cima com fonte de verdade, é
virado pela mesma conta, guardando junto para que lado a tela estava.

O `CorEm` do teste desfaz a rotação, então dá para cobrar "desenhei em
(10,20), tem que estar em (10,20)" sem refazer à mão justamente a conta
que está sendo testada.

### O monitor mostra as duas placas

Mostrava só a selecionada, e a saída da outra ficava invisível até
alguém clicar nela. Num circuito com duas placas conversando, era metade
da conversa.

Agora o painel junta as duas, **com o nome de quem falou em cada linha**:

    [ESP32] [   24ms] ligando o vermelho
    [Uno]   [   24ms] ligando o azul
    [ESP32] [  440ms] esp viva
    [Uno]   [  930ms] uno vivo

Com **uma** placa nada muda: prefixo em todas as linhas de um circuito
comum seria ruído puro. E há teste para os dois casos.

### Peça entre duas placas agora é denunciada

Ela obedece à primeira, e isso é arbitrário — ninguém sabe qual das duas
deveria mandar. Escolher calado era pior: o LED respondia à placa errada
e nada na tela explicava. A janela passa a dizer **qual peça** e **quais
placas**.

**Terra compartilhado não conta.** Ligar o GND das duas placas é o certo
a fazer num circuito com duas, e acusar isso viraria um aviso que
ninguém lê. Só sinal conta — e tem teste para garantir que continua
assim.

## Marco 33: você escolhe qual placa manda

Uma peça ligada em duas placas obedecia à primeira que aparecesse. O
Marco 32 fez a janela **avisar** — mas avisar sem deixar resolver só
trocava o susto por uma mensagem.

Agora a aba da peça mostra, só quando ela alcança duas placas:

    manda (automático):  ● ESP32   Uno

Clicar na outra placa faz a peça obedecê-la, a bolinha muda de lugar e
**o aviso some**. O próprio aviso passou a dizer como resolver, em vez
de só o que está errado.

### Três decisões

- **A escolha mora na peça** (`PlacaQueManda`) e vai para o `.sktch`.
  Abrir o projeto amanhã não pede para escolher de novo.
- **Escolha velha é ignorada, não apagada.** Se o fio foi tirado ou a
  placa apagada, a peça não pode obedecer a uma placa com a qual não
  tem mais ligação — volta ao automático e o aviso reaparece. Mas o
  campo fica: religar o fio devolve a escolha sem refazer.
- **Copiar e colar leva a escolha para a placa colada.** Duplicar um
  circuito de duas placas faria a peça nova mandar na placa *original*;
  o colar traduz o id quando a placa foi copiada junto.

### O que o teste cobra

O **efeito**, e não o campo: com as duas máquinas em estados opostos no
mesmo GPIO, escolher a outra placa **apaga o LED** que estava aceso.
Mudar só a propriedade passaria num teste de campo com o LED errado na
tela.

### O erro que era escrito e apagado na linha seguinte

Achado pela **captura de tela**, e não por teste. Ao abrir um circuito
com um LED entre duas placas, a barra de status mostrava
`placa: ESP32 DevKit V1 · ADC 0–4095`. O erro era escrito — e uma linha
depois o aviso do perfil da placa escrevia por cima.

O de **fio em pino inexistente** sofria do mesmo mal havia muito tempo.
Nenhum teste via, porque todos conferiam que o erro era *detectado*, e
nenhum que ele era *mostrado*.

A regra da barra ficou escrita no código: informação primeiro, erro por
último, para o erro ser o que fica. E agora há teste lendo o texto da
barra ao abrir o arquivo.

## Marco 34: a interrupção corta no meio da expressão

O Marco 32 fez a interrupção ser atendida entre **comandos**. Faltava o
meio da **expressão**:

```cpp
int r = cont * 10 + digitalWrite(4, HIGH) + cont;
```

`cont` é lido duas vezes, com a borda de subida entre as leituras. No
hardware a rotina roda assim que a borda chega, e a segunda leitura já vê
o valor novo — é por isso que o Arduino pede `volatile` nessas
variáveis. Atendendo só entre comandos, as duas leituras davam 0 e `r`
saía 0. Agora sai **1**.

A checagem mora no começo de `Avaliar`, então vale para qualquer pedaço
de conta. Cortar ali é seguro: o estado da conta pela metade está na
pilha do C#, e a rotina roda num ambiente próprio.

### Dois preços de cortar mais fundo, pagos antes de virarem defeito

**O contador de "loop preso" era zerado pela rotina.** Quem chamava a
interrupção era a mesma função que chama `setup()` e `loop()`, e ela
zera o contador — certo para o `loop()`, errado aqui. Com a interrupção
cortando cada expressão, um `while (true)` que pulsa o pino vigiado
**nunca** chegaria ao limite. A rotina agora conta passos normalmente (um
laço preso *dentro* dela ainda estoura) e, ao terminar, o contador volta
a ser o do código interrompido. O teste roda esse sketch em outra tarefa
com prazo: se o defeito voltar, ele falha em vez de travar a suíte.

**Uma lista nova a cada subexpressão.** A consulta de interrupções
pendentes criava uma lista vazia a cada chamada, e passou a ser chamada
em cada `a + b`. Agora a lista só nasce quando alguma interrupção vence.
Há teste de tempo **com** interrupção registrada.

## Marco 35: nenhuma borda passa sem ser vista

A vigia de interrupção **comparava o nível** do pino entre um ponto e
outro do programa. Um `shiftOut` no pino vigiado dá oito pulsos e termina
no nível em que começou — comparando níveis, nenhum dos oito era visto.

Agora são dois caminhos, cada um para o que ele consegue ver:

- **O que a placa escreve** tem a borda anotada no próprio
  `DigitalWrite`, na hora, numa fila. Oito pulsos viram oito chamadas da
  rotina, dezesseis com `CHANGE`.
- **O que vem de fora** — botão, sensor — não passa por `DigitalWrite`
  e continua sendo visto comparando o nível.

A escrita atualiza o nível visto, então a comparação não conta a mesma
borda duas vezes. Com `noInterrupts()`, borda escrita não entra na fila:
a mesma regra do caminho de fora, de que o que passou com as
interrupções desligadas não volta.

A fila tem teto de mil e poucas entradas. Não é o que protege contra
rotina que se redispara sozinha — isso é o contador de passos —, só
impede a memória de crescer sem fim.

## Marco 36: depurador

Até aqui a única ferramenta para entender um sketch era o
`Serial.println` — e ele só mostra o que a pessoa já desconfiava. Agora
dá para parar o programa numa linha e olhar o que cada variável vale.

| Como | O que faz |
|---|---|
| clique à esquerda do número da linha, ou **F9** | marca / desmarca um ponto de parada |
| botão ❚❚ ou **F6** | pausa onde o sketch estiver; parado, o mesmo botão continua |
| botão ▶❚ ou **F10** | anda uma linha (entra nas funções) |
| botão ■ ou **F5** | para tudo, mesmo com o sketch parado |

Parado, a linha fica amarela e aparece o painel **PAUSADO** em cima do
monitor serial, com as variáveis que aquele ponto do programa enxerga: as
locais primeiro, as globais marcadas. Uma local com o mesmo nome de uma
global aparece uma vez só, com o valor de dentro — é o que o sketch lê
ali. O circuito continua na tela como estava: dá para comparar o valor
da variável com o LED.

Como funciona: o motor avisa o depurador antes de cada comando, e parar
é **bloquear a thread do sketch** ali. A janela fica livre. Três
detalhes que o primeiro rascunho não tinha e o teste cobra:

- **Pausar no meio de um `delay(5000)`** é atendido na hora. O `delay`
  dorme em fatias de 20 ms e olha se há pedido de pausa — e, de brinde,
  parar a simulação no meio de um delay longo também solta na hora.
- **O relógio congela na pausa.** Sem isso, `millis()` veria o tempo
  parado passar e um sketch que mede intervalo "atrasaria" tudo ao
  continuar. O delay interrompido cumpre só o que faltava.
- **`if (x) y = 1;` para uma vez só.** O `if` e o `y = 1` estão na mesma
  linha; continuar da pausa no `if` pararia de novo no `y = 1`. Dentro do
  comando em que parou, a mesma linha não para de novo.

Os pontos de parada são **de cada placa**: a linha 12 do sketch da Uno
não é a linha 12 do da ESP32. Com duas placas, a pausa mostra o código
de quem parou, e continuar solta as duas.

Sem ponto marcado, o depurador custa uma chamada por comando: a medição
fica no teste, junto com os outros.

**Limites:**

- Ponto de parada numa linha de `while` ou `for` para **na entrada** do
  laço, não a cada volta. Para parar a cada volta, marque uma linha de
  dentro.
- Os pontos ficam no **número** da linha: inserir linhas acima de um
  ponto marcado não o arrasta junto.
- Não há "passar por cima" de uma função: o passo entra nela.
- Não dá para mudar o valor de uma variável parado.

## Marco 37: versão 1.4, ícone e instalador

O programa ganhou número de versão (**1.4.0**, no `<Version>` de
`app/App.csproj`), ícone e um instalador de verdade. A pasta que se
entrega para a turma é gerada por um comando:

```
powershell -ExecutionPolicy Bypass -File ferramentas\instalador\gerar.ps1
```

e sai assim (padrão: Área de Trabalho\CircuitSketch):

| Arquivo | O que é |
|---|---|
| `Instalar CircuitSketch 1.4.exe` | instalador (~64 MB, com o programa dentro) |
| `LEIA-ME.txt` | instalação, plugin da IDE, atalhos, desinstalação, problemas comuns |
| `Plugin Arduino IDE\` | a pasta `hardware` pronta para copiar, e `Instalar plugin.bat` |

Para um update: trocar o `<Version>` e rodar o `gerar.ps1` de novo.

**O ícone** é desenhado em código (`ferramentas/icone/gerar-icone.ps1`):
um chip com um sinal digital verde sobre o azul do botão "+". Cada
tamanho é desenhado de novo, e em 16–24 px o chip perde pinos e o sinal
vira um degrau só — seis pinos em 16 px viram borrão.

**Por que um instalador próprio**, e não Inno Setup ou MSI:

- Instala **por usuário**, em `%LOCALAPPDATA%\Programs\CircuitSketch`,
  sem pedir administrador. No laboratório o aluno não tem essa senha.
- Compila com o `csc` que já vem no SDK do .NET, contra o .NET
  Framework 4.8 que já vem no Windows: nada para baixar para gerar, nada
  para instalar antes de rodar.

O instalador copia o programa, cria os atalhos (Menu Iniciar e, se
marcado, Área de Trabalho), registra em Configurações > Aplicativos e,
se marcado, instala o plugin na pasta de sketches da Arduino IDE — que
ele acha sozinho (`arduino-cli.yaml` da IDE 2, `preferences.txt` da
1.x, ou Documentos\Arduino). O `caminho.txt` do plugin é gravado em
UTF-8 com BOM: sem BOM, o PowerShell 5 lê como ANSI e um "Usuários" no
caminho vira lixo — o teste instala num caminho com acento para cobrar
isso.

**O desinstalador só apaga o que está anotado** em `desinstalar.txt`,
escrito na instalação. Um arquivo do usuário deixado na pasta fica, e a
pasta também. O plugin só é apagado se o `platform.txt` ainda for do
CircuitSketch.

O plugin também ficou mais tolerante: sem `caminho.txt` (copiado à mão),
o `ponte.ps1` procura o programa onde o instalador põe.

Duas armadilhas que o teste pegou:

- **As DLLs nativas do WPF.** O publish "single-file" deixa
  `wpfgfx_cor3.dll`, `PresentationNative_cor3.dll` e mais três do lado de
  fora do `.exe`. O primeiro pacote levava só o `.exe`, e o programa
  instalado morria ao abrir a janela (`DllNotFoundException`, visto no
  log de aplicativos do Windows). Ninguém tinha percebido porque o plugin
  sempre apontou para a pasta de publicação, onde elas estão. O
  `gerar.ps1` agora leva a pasta inteira (menos o `.pdb`) e confere as
  principais.
- **Caminho comprido.** Instalado numa pasta de 224 caracteres, o
  programa não abria e o desinstalador saía com -1 sem escrever nem uma
  linha de log: passando de 260, o .NET Framework não acha o próprio
  `.config`. A mesma instalação copiada para uma pasta de 198 caracteres
  funcionou inteira. O instalador recusa pasta com mais de 180 e sugere
  a padrão.

Testado em pastas de teste, sem tocar a instalação real: instalar,
abrir o programa instalado, reinstalar por cima (o `desinstalar.txt` não
duplica linhas), desinstalar com e sem arquivo do usuário na pasta, e a
instalação antiga "Simulador ESP32" sendo removida sem mexer em outra
plataforma da pasta `hardware`.

## Marco 38: o botão de RESET de todas as placas

Toda placa de desenvolvimento tem o botão soldado, e até a 1.4 só o da
ESP32 respondia: nas outras o desenho tinha o botão e clicar nele não
fazia nada. Agora as seis respondem — RESET no Uno, Mega, Nano, Nano
RP2040 e Franzininho; EN e BOOT na ESP32.

O centro de cada botão foi **medido no próprio SVG** e convertido pela
escala do desenho, como os da ESP32 já eram. Chutar essas medidas já me
custou três rodadas neste projeto; o teste confere que o clique acerta
cada um e que nenhum caiu fora da caixa da peça.

O comportamento é o da placa real, e não um "reiniciar" instantâneo:

- **segurar** o botão para a placa e apaga tudo (o anel fica escuro
  embaixo do dedo);
- **soltar** faz ela começar do `setup()`, com o monitor continuando de
  onde estava — como a IDE faz depois de um reset.

E é **por placa**: com duas placas na bancada, resetar uma não encosta na
outra. O RESET roda o programa que estava gravado, não o que está no
editor agora — trocar o código pede um ▶ novo, igual à placa de verdade.

## Marco 39: char de verdade, texto do C e um monitor que responde

Os exemplos de rede da aula não passavam nem do parser. Três buracos:

- **Não havia `char`.** `msg += (char)payload[i]` montava
  `"104101108108111"` no lugar de `"hello"`: o motor guardava o código do
  caractere e somava como número. Agora `char` é um tipo de verdade —
  guarda o código, conta como número e imprime a letra — e continua
  `char` na atribuição seguinte, que é o que faz
  `char c = client.read()` funcionar dentro do laço.
- **As conversões eram jogadas fora.** O parser reconhecia `(char)x` e
  ignorava. Agora `(char)`, `(int)`, `(byte)`, `(char*)` e companhia
  convertem de verdade.
- **Faltavam as funções de texto do C**: `snprintf`, `sprintf`,
  `strcpy`, `strcat`, `strlen`, `strcmp`, `dtostrf`, `itoa`. Todas
  escrevem num `char buffer[50]` — e para isso o vetor precisa chegar
  **sem ser avaliado**, senão viraria texto e a escrita se perdia.

Junto vieram: funções anônimas (`server.on("/", []() { ... })`), strings
cruas (`R"rawliteral(...)rawliteral"`, que é como os tutoriais guardam a
página HTML), `PROGMEM` e `IRAM_ATTR` ignorados, `Serial.printf`, e
`Serial.print(valor, HEX)`.

Duas correções que não estavam no plano e o teste cobrou:

- **`Serial.write(65)` escrevia "65"**, e não a letra "A". Agora
  `write()` manda byte, que é o que faz a ponte
  `Serial.write(SerialBT.read())` repetir o texto.
- **`comando.trim();` não fazia nada.** No Arduino esses métodos MUDAM a
  própria String (são `void` lá); aqui devolviam uma cópia que ninguém
  guardava, então o `"\r"` do fim ficava e nenhuma comparação batia.
  Vale para `trim`, `toUpperCase`, `toLowerCase`, `replace`, `remove`,
  `setCharAt` e `concat`.

E o **monitor serial agora manda texto para a placa**: o campo embaixo
dele alimenta `Serial.available()`, `read()`, `readString()`,
`readStringUntil()` e `parseInt()`, que até aqui respondiam "não chegou
nada" para sempre.

## Marco 40: rede — WiFi, HTTP, MQTT e Bluetooth

Era o que faltava para a aula de IoT, e estava recusado desde o começo
com "fora de escopo". O simulador ganhou uma rede própria, uma só para a
bancada inteira — rede é justamente o que liga uma placa à outra.

**O que o sketch escreve é o que ele escreveria na placa**: `WiFi.begin`,
`WiFi.status()`, `WebServer`, `WiFiServer` cru, `HTTPClient`,
`PubSubClient` e `BluetoothSerial`, com os mesmos números de retorno
(`WL_CONNECTED`, `-1` de conexão recusada, `state()` do MQTT).

Cada pedido tem três destinos possíveis, nesta ordem:

1. **Outra placa da bancada.** Ao conectar, cada uma ganha um IP
   (`10.0.0.2`, `.3`, ...). Uma ESP32 serve a página e a outra lê, sem
   sair do programa.
2. **O próprio computador.** Todo servidor aberto por uma placa ganha um
   endereço em `localhost` (8080, 8081...), e o painel REDE mostra o link:
   a página da placa abre no navegador de verdade. O broker MQTT interno
   também abre em `localhost:1883`, então um app de MQTT do computador
   conversa com as placas.
3. **A internet**, pela conexão do computador — ligável e desligável em
   ⋮ > "Rede: usar a internet do computador".

**O broker MQTT mora dentro do programa.** Se o broker público da aula
não responde (laboratório sem internet), a placa cai nele e as placas da
bancada continuam conversando entre si; o painel diz que foi isso que
aconteceu. O protocolo MQTT 3.1.1 é implementado de verdade — CONNECT,
SUBSCRIBE, PUBLISH, PINGREQ —, e o teste faz uma placa conectar por TCP
no broker interno para provar que o que sai do fio é MQTT mesmo.

**Bluetooth** é o clássico (`BluetoothSerial`), que é o da ESP32: o
painel BLUETOOTH faz o papel do celular — acha a placa, conecta e troca
mensagens. Duas placas também pareiam entre si. A Nano RP2040 Connect só
tem BLE, e recusa dizendo isso.

**Nada passa calado**, como sempre: uma placa sem WiFi recusa
`WiFi.begin()` dizendo quais placas têm; um `http.GET()` antes de
conectar devolve o mesmo `-1` da placa real, com o motivo escrito no
painel REDE.

## O que ainda falta

Medido com uma sonda que usa cada recurso, não de memória. As seções
antigas desta parte descreviam coisas resolvidas há muitos marcos — o
motor hoje roda `switch/case`, `do/while`, `String` com concatenação,
`analogWrite`, funções próprias, vetores, `LiquidCrystal` e `map` sem
reclamar.

### Incomoda, não impede

- **Bluetooth LE (BLE) não é simulado**, só o clássico
  (`BluetoothSerial`). É o único que a Nano RP2040 Connect tem, então
  nela o Bluetooth é recusado com essa explicação.
- **ArduinoJson não existe aqui**: `deserializeJson()` é recusado. Para
  ler uma resposta, `http.getString()` mais os métodos de `String`.
- **Sem relógio de rede**: `configTime()` e `getLocalTime()` ainda não
  existem.
- **`http.getStream()`** não existe; o corpo da resposta vem inteiro
  por `getString()`.

- **As rotinas de bordas escritas rodam logo depois da chamada, não
  entre um pulso e outro.** Os oito disparos de um `shiftOut` acontecem
  todos, mas depois dele — uma rotina que leia outro pino no meio da
  transmissão vê o estado final, não o de cada pulso.
- **A IDE não mostra a porta no diálogo nem no Monitor Serial**, embora
  o **Upload funcione por ela**. Cinco hipóteses foram medidas e
  descartadas (Marco 31); a causa está dentro da IDE. O monitor do
  CircuitSketch e o `arduino-cli` funcionam.
- **O depurador não muda valores nem passa por cima de funções**, e o
  ponto de parada não acompanha a linha quando se edita acima dele
  (Marco 36).

## Marco 41: MicroPython, e a lista inteira do professor

A Alpha 17 fecha a lista de doze itens que o professor mandou. O item
mais pesado era o terceiro — "tente usar C e Python na ESP" — e é dele
que sai a decisão de projeto da versão.

### Uma árvore, duas linguagens

O simulador passou a rodar **MicroPython**, e a escolha que sustenta
tudo é esta: o parser de Python produz **a mesma árvore** do parser de
C++.

```
LexerPython ─┐
             ├─→ Programa ─→ Interpretador ─→ Maquina ─→ desenho
Lexer     ───┘
```

Escrever um segundo interpretador teria jogado fora as pontes de
biblioteca que já existiam — rede, I²C, rádio, cartão SD — e criado
**duas verdades** sobre o que cada peça faz. Do jeito que ficou, um LED
aceso por `digitalWrite(2, HIGH)` e um aceso por `Pin(2, Pin.OUT).on()`
percorrem exatamente o mesmo caminho.

O que o Python tem e o C não tinha ganhou nós próprios (`CmdParaCada`,
`ListaLiteral`, `DicionarioLiteral`, `Fatia`, `ArgNomeado`) e três
arquivos de interpretador: a linguagem, os módulos e as peças.

**A linguagem sai do código, não de uma opção.** `Compilador.Detectar`
conta sinais — `#include` e `void setup()` de um lado, `import` e `def`
do outro, ponto-e-vírgula no fim da linha como desempate. O editor troca
de cores e a aba vira `.py` enquanto se digita.

Duas decisões que o resto herda:

- **`while True:` no fim do módulo vira o `loop()`**, e o que vem antes
  vira o `setup()`. Os dois rodam **nas globais**, e não num escopo
  filho: `prev_weather = ""` escrito antes do laço é a mesma variável
  que o laço lê depois. Num escopo filho ela morreria no fim do setup, e
  o sketch de MQTT republicaria a mesma medição para sempre.
- **O Python mostra `25.0` onde o Arduino mostra `25.00`.** Não é
  acabamento: é o que sai no `ujson.dumps`, e um payload de MQTT com o
  número errado é um payload errado.

### O que o professor viu, e o que era

| O que ele relatou | A causa |
|---|---|
| "só acha 2 redes, e por que ele acha wokwi?" | `"Wokwi-GUEST"` estava **fixo no código**, em `motor/Rede.cs`. O scan era inventado inteiro. |
| "a área de colisão não gira com a peça" | `CaixaDe` montava o retângulo de `X/Y/Largura/Altura` e **nunca olhava `peca.Rotacao`**. Os pinos giravam porque passam por `PosicaoLocal`; o corpo, não. |
| "não consigo colocar o fio na fileira que sobrou" | O desenho da ESP32 tem 107×204 px e cobre a protoboard de ponta a ponta. **142 dos 830 furos** ficavam sem alcance. |
| "por que o OLED tem 8 conexões?" | A peça catalogada era a versão **SPI**. A de I²C, que é a que a turma compra, tem quatro. |
| `Serial.print(")")` não compilava | `Checar` comparava o **texto** do token sem olhar o tipo: a string `")"` valia como fecha-parênteses. |

O caso do furo mereceu uma regra, e não um remendo: **furo livre ganha
do corpo de uma placa espetada naquela protoboard**. Só de placa — um
resistor deitado também cobre furos, mas o corpo dele é do tamanho do
próprio componente. E mover a placa continua funcionando porque ela
atravessa o **canal do meio**, que não tem furo nenhum — que é
exatamente por onde se pega um chip na bancada.

### O que não dá para prometer

- **A placa simulada não tem rádio.** O `scanNetworks()` mostra o que o
  computador enxerga, e a associação continua simulada. Sem rádio, o
  painel **avisa na interface** — não no monitor serial, que é do sketch.
- **O servidor da placa abre em `localhost`**, não em `192.168.4.1`. O
  painel mostra os dois endereços lado a lado, porque é isso que liga o
  IP que o aluno leu no monitor ao que o navegador dele alcança.
- **Instalar uma biblioteca faz o `#include` parar de dar erro**, e só.
  O simulador continua sem executar o que está dentro dela, e a janela
  diz isso com todas as letras.

### Placas novas

**Raspberry Pi Pico** e **STM32F103 "Blue Pill"**. A Pico foi barata —
o número do pino é o número do GP, sem tabela de tradução. A Blue Pill
não: lá o pino se chama `PA5`, `PC13`, e o core do Arduino numera as
portas em sequência (PA0..PA15 = 0..15, PB0 = 16, PC13 = 45). O
`LED_BUILTIN` dela é o 45, e acende com `LOW`.

### Marca d'água

1.038 → **1.180 testes**, sempre terminando em zero falhas. Entre eles,
os três sketches em MicroPython que o professor mandou como quebrados,
verbatim, e uma varredura de **308 combinações de peça e ângulo** para a
área de clique — que, com o bug de volta, falha em 85 dos 92 casos que
ela prova.

## Créditos

Arte dos componentes: [wokwi-elements](https://github.com/wokwi/wokwi-elements),
**MIT** © 2020 Uri Shaked. Texto da licença em
`app/assets/LICENSE-wokwi-elements.txt`.

Só a apresentação vem de lá — os elements não trazem lógica de
simulação, e essa é toda nossa. Nenhum código ou asset do simulador
comercial do Wokwi é usado.
