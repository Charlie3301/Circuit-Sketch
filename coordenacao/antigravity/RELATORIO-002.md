# RELATÓRIO-002 — Projetos de teste dos componentes novos (Alpha 24)

## O que foi feito

- `ferramentas/projetos.py`:
  - Atualizada a variável `BASE` para resolver dinamicamente o caminho da raiz do repositório local (`os.path.normpath(os.path.join(...))`), evitando apontamentos absolutos ao OneDrive e garantindo que os arquivos gerados fiquem estritamente na pasta de trabalho do agente (`projetos/`).
  - Implementadas as 8 novas funções geradoras de projetos para os componentes da Alpha 24:
    1. `npn_motor_esp32()`: ESP32 com motor DC acionado por transistor NPN 2N2222A (resistor de 1 kohm na base), diodo de roda livre 1N4007 em antiparalelo com o motor e alimentação de 6 V (4 pilhas AA).
    2. `mosfet_fita_uno()`: Arduino Uno chaveando carga de 12 V (LED com resistor limitador de 470 ohms) por MOSFET de potência canal N de nível lógico (IRLZ44N) com modulação PWM controlada por potenciômetro no A0.
    3. `zener_referencia()`: Diodo Zener de 5,1 V polarizado por resistor de 470 ohms em bateria de 9 V, com divisor resistivo de precisão 10k/10k conectado ao pino A0 do Uno para leitura via monitor serial.
    4. `capacitor_rc_led()`: Circuito de carga e descarga RC onde o pino 13 do Uno carrega um capacitor eletrolítico de 1000 µF através de resistor e diodo 1N4148; ao ir para LOW, a carga acumulada descarrega lentamente pelo resistor de 330 ohms mantendo o LED aceso temporariamente antes de apagar.
    5. `opto_isolado()`: Isolamento galvânico completo utilizando optoacoplador PC817 entre o circuito de controle (Arduino Uno com botão) e o circuito de potência/carga (bateria de 9 V com LED verde), sem terra comum entre os lados.
    6. `fusivel_curto()`: Circuito de proteção com fusível rápido de 1 A alimentando carga normal de 12 V (~20 mA); ao acionar o botão de teste, um curto-circuito é imposto à linha, a corrente excede a capacidade nominal e o fusível queima/abre, protegendo a fonte e cortando a alimentação da carga.
    7. `schottky_protecao()`: Proteção de polaridade reversa com diodo Schottky 1N5819 em série com bateria de 9 V; conduz com queda reduzida (~0,3 V) em regime direto e bloqueia completamente a corrente ao inverter a tensão da bateria, sem danificar componentes nem emitir avisos.
    8. `pcb_semaforo()`: Semáforo completo montado numa placa de circuito impresso perfurada de 18x24 furos (`pcb-18x24`, 5x7 cm); os componentes são espetados nos furos isolados e conectados furo-a-furo por baixo por fios/solda, reproduzindo a montagem real de prototipagem em ilhas.
  - Registradas as 8 funções na lista `for gerar in [...]` e gerados os arquivos correspondentes.

- Arquivos gerados em `projetos/`:
  - `projetos/npn-motor-esp32.sktch`
  - `projetos/mosfet-fita-uno.sktch`
  - `projetos/zener-referencia.sktch`
  - `projetos/capacitor-rc-led.sktch`
  - `projetos/opto-isolado.sktch`
  - `projetos/fusivel-curto.sktch`
  - `projetos/schottky-protecao.sktch`
  - `projetos/pcb-semaforo.sktch`
  - `projetos/roteiros.json` (atualizado com os 46 roteiros de teste).

- `coordenacao/antigravity/saida-002.txt`:
  - Saída completa da execução do bloco `Projetos_Testados`.

- `coordenacao/antigravity/RELATORIO-002.md`:
  - Este relatório.

Nenhum arquivo em `app/`, `motor/` ou desenho foi alterado. Nenhum projeto preexistente foi apagado.

---

## Comandos rodados e resultados

### 1. Compilação do aplicativo
```powershell
dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/antigravity
```
- Resultado: Compilação com êxito (0 Erros, 0 Avisos).

### 2. Geração dos projetos
```powershell
python ferramentas/projetos.py
```
- Resultado: `46 projetos em C:/Users/seraf/CircuitSketch-agentes/antigravity/projetos`

### 3. Testes individuais de cada projeto novo (com `CS_PROJETO`)
Todos os 8 projetos foram testados individualmente com o executável compilado:
- `npn-motor-esp32`: **FUNCIONA** (5 s)
- `mosfet-fita-uno`: **FUNCIONA** (2 s)
- `zener-referencia`: **FUNCIONA** (2 s)
- `capacitor-rc-led`: **FUNCIONA** (8 s)
- `opto-isolado`: **FUNCIONA** (2 s)
- `fusivel-curto`: **FUNCIONA** (3 s)
- `schottky-protecao`: **FUNCIONA** (2 s)
- `pcb-semaforo`: **FUNCIONA** (7 s)

### 4. Execução de todos os projetos juntos (sem `CS_PROJETO`)
```powershell
cmd /c "set CS_PROJETO=&& .artefatos\antigravity\bin\App\debug\CircuitSketch.exe --teste --so Projetos_Testados --saida coordenacao\antigravity\saida-002.txt"
```
- Resultado geral: **46 de 46 projetos funcionam** (38 originais + 8 novos).
- Código de saída: `0`.

---

## Resultado dos 8 projetos novos

| Projeto | Resultado | Detalhe |
|---|---|---|
| `npn-motor-esp32` | **FUNCIONA** (4 s) | Motor gira em HIGH (>500 rpm), para em LOW (≤50 rpm), sem avisos. |
| `mosfet-fita-uno` | **FUNCIONA** (0 s) | PWM baixo deixa brilho fraco (≤0.35); PWM alto deixa forte (≥0.70). |
| `zener-referencia` | **FUNCIONA** (0 s) | Leitura serial do Zener estável em ~5,2 V via divisor 10k/10k. |
| `capacitor-rc-led` | **FUNCIONA** (6 s) | Capacitor 1000 µF carrega em HIGH; mantém LED aceso após ir a LOW e apaga após descarga RC. |
| `opto-isolado` | **FUNCIONA** (1 s) | Circuito isolado por PC817; botão no Uno acende e apaga LED na bateria sem terra comum. |
| `fusivel-curto` | **FUNCIONA** (1 s) | Operação normal com LED aceso; botão de curto queima o fusível de 1 A e corta a alimentação. |
| `schottky-protecao` | **FUNCIONA** (0 s) | Polaridade direta conduz com baixa queda; polaridade invertida bloqueia sem queimar nada. |
| `pcb-semaforo` | **FUNCIONA** (5 s) | 3 LEDs e 3 resistores em furos isolados de PCB 18x24 com soldas/fios inferiores; ciclo completo funciona. |

---

## Última linha da saída (`coordenacao/antigravity/saida-002.txt`)

```text
46 passaram, 0 falharam
```

---

## O que ficou de fora, dúvidas e riscos

- **Ajustes de timing realizados durante o desenvolvimento:**
  - Inicialmente, em `mosfet-fita-uno`, com resistor de 1000 ohms na linha de 12 V a corrente de 10 mA atingia brilho máximo de 0,60; com resistor de 470 ohms (~21 mA), o brilho atingiu o nível esperado (≥0,70) em PWM alto.
  - Em `zener-referencia`, a tensão medida e impressa no serial foi de 5,2 V (devido à resistência interna zener de 7 ohms sob ~8 mA), conferida com precisão de 0,1 V.
  - Em `capacitor-rc-led`, o tempo do ciclo do sketch foi ajustado para manter LOW por tempo suficiente para que a conferência de descarga avaliasse o LED apagado antes do próximo ciclo de subida.
  - Em `fusivel-curto`, foi incluído o Uno para capturar o aviso persistente emitido pelo simulador no monitor serial e na barra de eventos, registrando o corte de circuito de forma robusta.
- **Riscos e observações:** Nenhum risco. Nenhum componente do simulador (`app/`, `motor/`) precisou de correção para suportar os projetos; todos os componentes funcionam fielmente às especificações do modelo físico do circuito.
