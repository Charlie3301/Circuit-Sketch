# TAREFA-002 (ChatGPT/Codex): por que 11 projetos de teste nao funcionam

Leia `AGENTS.md` antes. Sua pasta: `C:\Users\seraf\CircuitSketch-agentes\chatgpt`.

## Contexto

`projetos/` tem 38 projetos (gerados por `ferramentas/projetos.py`, com o
roteiro de teste de cada um em `projetos/roteiros.json`). O bloco
`Projetos_Testados` (`app/AutotesteProjetos.cs`) roda cada roteiro na janela
de verdade. A primeira rodada: 27 funcionam, 11 nao. O resultado completo
esta em `coordenacao/resultados-projetos.json`.

Os 11: drone-quadricoptero, carrinho-rc-sem-placa, carrinho-rc-arduino,
rover-4wd, rover-4wd-hc-sr04, rover-4wd-reles, controle-ir-leds,
nivel-mpu6050, ventilador-l298n, balanca-hx711-lcd, espnow-botao-led.

Cada falha e de um de dois tipos:

- **A) o projeto ou o roteiro esta errado** (o circuito, o sketch ou a
  conferencia nao batem com o que aconteceria na placa de verdade). Exemplo
  ja conhecido: o rover-4wd procura "re" no monitor, e "frente" tambem
  contem "re" -- a conferencia passa na hora errada.
- **B) o simulador faz diferente da placa de verdade.** Esse e o objetivo dos
  projetos: achar isso.

## O que fazer

Para cada um dos 11:

1. Descubra a causa. Rode so ele:
   ```
   dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/chatgpt
   $env:CS_PROJETO = "<nome>"; $env:CS_RESULTADOS = "coordenacao/chatgpt/res-<nome>.json"
   .artefatos\chatgpt\bin\App\debug\CircuitSketch.exe --teste --so Projetos_Testados --saida coordenacao/chatgpt/saida-<nome>.txt
   ```
   Leia o codigo do simulador (`app/`, `motor/`) para achar o porque. Pistas
   (nao conclusoes): o drone e o nivel-mpu6050 nao imprimem nada (os dois
   usam Adafruit_MPU6050; o console da janela mostra erro de compilacao?); no
   carrinho-rc-sem-placa o ESC acelera mas o servo nao segue o receptor; no
   carrinho-rc-arduino o receptor esta no 5V.2 do Nano; no controle-ir-leds o
   Uno "queima" por um curto no pino 3; no rover-4wd-reles a frente sai ao
   contrario; no rover-4wd-hc-sr04 o lado com PWM 200 gira igual ao lado no
   maximo.
2. Se for tipo A: corrija em `ferramentas/projetos.py` (o circuito, o sketch
   ou o roteiro), regenere com `python ferramentas/projetos.py projetos`
   (SEMPRE com o argumento `projetos`; sem ele o script grava na pasta do
   Claude) e confirme rodando o projeto de novo.
   **Nunca afrouxe uma conferencia para esconder um comportamento errado do
   simulador.** Na duvida entre A e B, e B.
3. Se for tipo B: NAO conserte o simulador. Escreva o diagnostico: o que a
   placa de verdade faria (com a fonte: datasheet, codigo da biblioteca real,
   documentacao do core), o que o simulador faz, o arquivo e a linha onde
   esta a diferenca, e a correcao que voce proporia.

## Arquivos que voce pode criar ou alterar

- `ferramentas/projetos.py`
- `projetos/*.sktch` e `projetos/roteiros.json` (so regenerando pelo script)
- `coordenacao/chatgpt/` (saidas, resultados, relatorio)

`app/` e `motor/` sao SO para ler nesta tarefa.

## Entrega

`coordenacao/chatgpt/RELATORIO-002.md`: uma secao por projeto -- tipo (A ou
B), causa, evidencia (saida do teste, trecho de codigo com arquivo:linha) e o
que foi feito (A) ou a correcao proposta (B). No fim, a rodada dos 38 com
`CS_PROJETO` vazio: quantos funcionam agora.
