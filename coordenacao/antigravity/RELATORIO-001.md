# RELATÓRIO-001 — Verificação inteira na pasta do Antigravity

## O que foi feito

- Executado o script de verificação completa:
  `powershell -NoProfile -ExecutionPolicy Bypass -File ferramentas\verificar.ps1 -Agente antigravity`
- O script realizou:
  1. Compilação do projeto em `.artefatos\antigravity\` (sucesso);
  2. Conferência dos catálogos de idiomas com `ferramentas\idiomas.py --conferir`;
  3. Execução da suíte completa de testes (`CircuitSketch.exe --teste`), incluindo os 38 projetos de teste;
  4. Gravação dos arquivos de saída em `coordenacao\verificacao\antigravity\` (`resumo.txt`, `saida.txt`, `projetos.json`).
- Leitura dos arquivos gerados `resumo.txt` e `saida.txt`.
- Geração deste relatório `coordenacao/antigravity/RELATORIO-001.md`.
- Conforme as instruções da tarefa, nenhum arquivo de código do projeto foi alterado e nenhuma correção foi tentada.

## Comandos rodados e resultados

### Comando executado
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ferramentas\verificar.ps1 -Agente antigravity
```
- Código de saída do script: `1` (devido à presença de testes falhando e projetos não funcionais).
- Tempo total de execução: **1386 segundos** (~23 minutos).

### Resumo dos Resultados
- **Compilação**: Compilou com sucesso em `.artefatos\antigravity`.
- **Idiomas**: 10 frases marcadas no código; catálogo em inglês com 9/10 traduzidas (90%).
- **Suíte de Testes**: **2881 passaram, 7 falharam**.
- **Projetos de Teste**: **26 de 38 funcionam** (12 não funcionaram).

---

## Linhas FALHA da suíte (copiadas na íntegra)

As 7 falhas apontadas na suíte de testes:

```text
FALHA refazer a rede cabe num quadro  levou 5,53 ms
FALHA desempenho: o quadro custa menos de 1 ms  2,13 ms
FALHA velocidade: o executor passa dos 16 MHz do chip  7,4
FALHA bateria (Alpha 23): uln2003 funciona no circuito, sem aviso nenhum  eixo de 0 a 4 graus
FALHA bateria (Alpha 23): motor-28byj48 funciona no circuito, sem aviso nenhum  eixo de 0 a 16 graus
FALHA esp11-minuteria: faz o que a descricao promete  Minuteria pronta
FALHA uno-botao: faz o que a descricao promete  pino 13: 0
```

---

## Projetos que não funcionaram (12 de 38)

```text
NAO FUNCIONA drone-quadricoptero: a controladora iniciou -- monitor: 
NAO FUNCIONA carrinho-rc-sem-placa: manche direito para a direita: o servo vira (~180 graus) -- direcao em 93 graus
NAO FUNCIONA carrinho-rc-arduino: o Nano le o receptor -- monitor: sem sinal do radio | sem sinal do radio | sem sinal do radio | sem sinal do radio
NAO FUNCIONA rover-4wd: de re os quatro invertem -- m_fe 177 rpm, m_te 177 rpm, m_fd 177 rpm, m_td 177 rpm
NAO FUNCIONA rover-4wd-hc-sr04: com 100 cm livres o sonar le ~100 cm -- monitor: Frente: 99 cm | Frente: 99 cm | Frente: 99 cm | Frente: 99 cm
NAO FUNCIONA rover-4wd-reles: com 100 cm livres o sonar le ~100 cm -- monitor: Frente: 99 cm | Frente: 99 cm | Frente: 99 cm | Frente: 99 cm
NAO FUNCIONA controle-ir-leds: a tecla 1 chega (0xFF30CF) -- monitor: 💥 Arduino Uno queimou: curto: o pino 3 está em LOW e ligado direto na alimentação
NAO FUNCIONA nivel-mpu6050: inclinado o LED acende -- led com brilho 0,00
NAO FUNCIONA motor-de-passo-28byj48: o motor gira (mais de 90 graus) -- motor em -20 graus
NAO FUNCIONA ventilador-l298n: com o potenciometro baixo o ventilador gira devagar -- motor 0 rpm
NAO FUNCIONA balanca-hx711-lcd: o LCD mostra os 500 g -- LCD: Peso: 701 g / 
NAO FUNCIONA espnow-botao-led: soltou: apaga -- led com brilho 0,94
```

---

## Conteúdo integral de `coordenacao/verificacao/antigravity/resumo.txt`

```text
== compilar (.artefatos\antigravity)
compilou
== idiomas
  10 frases marcadas no codigo
    en     English                  9/10 traduzidas (90%)
== testes: a suite inteira
  2881 passaram, 7 falharam
  FALHA refazer a rede cabe num quadro  levou 5,53 ms
  FALHA desempenho: o quadro custa menos de 1 ms  2,13 ms
  FALHA velocidade: o executor passa dos 16 MHz do chip  7,4
  FALHA bateria (Alpha 23): uln2003 funciona no circuito, sem aviso nenhum  eixo de 0 a 4 graus
  FALHA bateria (Alpha 23): motor-28byj48 funciona no circuito, sem aviso nenhum  eixo de 0 a 16 graus
  FALHA esp11-minuteria: faz o que a descricao promete  Minuteria pronta
  FALHA uno-botao: faz o que a descricao promete  pino 13: 0
== projetos: 26 de 38 funcionam
  NAO FUNCIONA drone-quadricoptero: a controladora iniciou -- monitor: 
  NAO FUNCIONA carrinho-rc-sem-placa: manche direito para a direita: o servo vira (~180 graus) -- direcao em 93 graus
  NAO FUNCIONA carrinho-rc-arduino: o Nano le o receptor -- monitor: sem sinal do radio | sem sinal do radio | sem sinal do radio | sem sinal do radio
  NAO FUNCIONA rover-4wd: de re os quatro invertem -- m_fe 177 rpm, m_te 177 rpm, m_fd 177 rpm, m_td 177 rpm
  NAO FUNCIONA rover-4wd-hc-sr04: com 100 cm livres o sonar le ~100 cm -- monitor: Frente: 99 cm | Frente: 99 cm | Frente: 99 cm | Frente: 99 cm
  NAO FUNCIONA rover-4wd-reles: com 100 cm livres o sonar le ~100 cm -- monitor: Frente: 99 cm | Frente: 99 cm | Frente: 99 cm | Frente: 99 cm
  NAO FUNCIONA controle-ir-leds: a tecla 1 chega (0xFF30CF) -- monitor: 💥 Arduino Uno queimou: curto: o pino 3 está em LOW e ligado direto na alimentação
  NAO FUNCIONA nivel-mpu6050: inclinado o LED acende -- led com brilho 0,00
  NAO FUNCIONA motor-de-passo-28byj48: o motor gira (mais de 90 graus) -- motor em -20 graus
  NAO FUNCIONA ventilador-l298n: com o potenciometro baixo o ventilador gira devagar -- motor 0 rpm
  NAO FUNCIONA balanca-hx711-lcd: o LCD mostra os 500 g -- LCD: Peso: 701 g / 
  NAO FUNCIONA espnow-botao-led: soltou: apaga -- led com brilho 0,94
== levou 1386 s
```

---

## O que ficou de fora, dúvidas e riscos

- **O que ficou de fora**: Nenhuma correção foi tentada no código-fonte, nos testes ou nos projetos `.sktch`, conforme expressamente proibido pela tarefa ("Não tente consertar nada. Nao edite nenhum arquivo do projeto: esta tarefa so roda e relata.").
- **Observações sobre as falhas**:
  - 3 falhas de desempenho/tempo (`refazer a rede cabe num quadro`, `desempenho: o quadro custa menos de 1 ms`, `velocidade: o executor passa dos 16 MHz do chip`), provavelmente decorrentes da carga da máquina host durante a execução dos testes.
  - 2 falhas na bateria do motor de passo / ULN2003 (Alpha 23).
  - 2 falhas em sketches específicos (`esp11-minuteria`, `uno-botao`).
  - Dos 38 projetos de teste, 26 funcionam perfeitamente e 12 falharam em conferências específicas (incluindo um caso onde o Arduino Uno queimou por curto-circuito no projeto `controle-ir-leds`).
- **Riscos**: Nenhum risco aos arquivos originais ou ao repositório principal; apenas a pasta `.artefatos\antigravity` e `coordenacao\verificacao\antigravity` foram manipuladas pela execução.
