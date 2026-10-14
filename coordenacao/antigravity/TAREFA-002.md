# TAREFA-002 — Projetos de teste dos componentes novos

Leia o `GEMINI.md` (e o `AGENTS.md`) antes. Trabalhe so na sua pasta
(`C:\Users\seraf\CircuitSketch-agentes\antigravity`).

## Contexto

`ferramentas/projetos.py` gera os projetos de teste em `projetos/` (um
`.sktch` por projeto) e os roteiros em `projetos/roteiros.json`. O bloco de
testes `Projetos_Testados` abre cada projeto, roda e confere o roteiro. Todo
projeto que funciona vira teste de regressao (`AINDA_NAO_FUNCIONAM` no fim do
arquivo lista os que ainda nao).

Leia 3 projetos prontos para pegar o jeito (`semaforo`, `dimmer`,
`ventilador` e as funcoes `peca`, `fio`, `led`, `botao`, `bateria`,
`confere`, `mexer`, `esperar`, `projeto` no comeco do arquivo).

Os componentes novos (Alpha 24), com os pinos e os modelos nestes arquivos:
- `app/Diodos.cs`: `diodo` (1N4007, 1N4148), `diodo-zener` (varios volts),
  `diodo-schottky` (1N5819...), pinos `A` e `C` (C = catodo, o lado da faixa);
- `app/Passivos.cs`: `capacitor`, `capacitor-eletrolitico` (pinos `+` e `-`),
  `indutor`, `cristal` (o valor vai no campo `Modelo`, ex. `"100u-25v"`);
- `app/Semicondutores.cs`: `transistor-npn`, `transistor-pnp` (`E`, `B`, `C`),
  `mosfet-n` (`G`, `D`, `S`; `irlz44n`, `irf520`), `optoacoplador`
  (`A`, `K`, `C`, `E`), `fusivel` (`1`, `2`; `Modelo` = corrente, ex. `"1a"`),
  `varistor` (`1`, `2`);
- `app/Catalogo.cs`: `pcb-<colunas>x<linhas>` (a PCB perfurada; os furos sao
  `x<coluna>y<linha>` e NAO se ligam entre si: so fio liga).

Os testes de unidade destas pecas (para ver os numeros que o simulador da):
`app/AutotesteDiodos*.cs`, `app/AutotestePassivos*.cs`,
`app/AutotesteSemicondutores.cs`, `app/AutotestePcb.cs`.

## O que fazer

Escrever **8 projetos novos** em `ferramentas/projetos.py` (funcoes novas,
chamadas na lista `for gerar in [...]` do fim), cada um com descricao em
portugues, sketch de verdade e roteiro com conferencias:

1. `npn-motor-esp32`: ESP32 liga um motor DC por um 2N2222A (resistor de
   1 kohm na base) e um diodo 1N4007 de roda livre no motor; bateria 6 V.
   Confere: com o pino em HIGH o motor gira; LOW, para; sem avisos.
2. `mosfet-fita-uno`: Uno + IRLZ44N chaveando uma carga de 12 V (um motor ou
   uma lampada/LED com resistor) com PWM; confere que o PWM muda a rotacao
   (ou o brilho).
3. `zener-referencia`: bateria 9 V, resistor, zener de 5,1 V; o A0 de uma
   placa le a tensao do zener (por divisor se precisar). Confere o valor no
   monitor serial.
4. `capacitor-rc-led`: o capacitor carregando por um resistor; o LED apaga
   devagar quando o pino vai a LOW (ou o A0 mostra a curva subindo). Confere
   o tempo aproximado (RC).
5. `opto-isolado`: duas placas (ou placa + bateria) separadas pelo PC817: o
   botao de um lado acende o LED do outro.
6. `fusivel-curto`: bateria + fusivel de 1 A + carga; um botao poe a carga
   em curto: o fusivel queima (aviso) e corta.
7. `schottky-protecao`: o diodo Schottky contra a bateria invertida: com a
   bateria ao contrario, nada liga e nada queima.
8. `pcb-semaforo`: o semaforo montado numa `pcb-18x24`: LEDs e resistores
   espetados nos furos, e os fios ligando furo a furo (como solda por
   baixo). Confere que os tres LEDs acendem na sequencia.

Depois:

```
python ferramentas/projetos.py
dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/antigravity
$env:CS_PROJETO = "npn-motor-esp32"   # um por vez, para ir mais rapido
.artefatos\antigravity\bin\App\debug\CircuitSketch.exe --teste --so Projetos_Testados --saida coordenacao\antigravity\saida-002.txt
```

Rode cada projeto novo (troque o `CS_PROJETO`), e no fim todos juntos (sem
`CS_PROJETO`). Projeto que nao funciona: veja se o erro e do SEU projeto
(fio errado, conta errada) e conserte; se parecer erro do SIMULADOR, nao
mexa no simulador: ponha o nome em `AINDA_NAO_FUNCIONAM` e explique no
relatorio o que viu (o que esperava, o que veio).

## Arquivos que voce pode alterar

- `ferramentas/projetos.py`
- os `projetos/*.sktch` e `projetos/roteiros.json` que o script gera
- `coordenacao/antigravity/RELATORIO-002.md` e `coordenacao/antigravity/saida-002*.txt`

Nada de `app/`, `motor/` ou desenho. Nao apague projeto nenhum.

## Relatorio

`coordenacao/antigravity/RELATORIO-002.md`: os 8 projetos, o resultado de
cada um (FUNCIONA / NAO FUNCIONA com a linha que falhou) e a ultima linha
da saida.
