# TAREFA-001 (Antigravity): a verificacao inteira, na sua pasta

Leia `AGENTS.md` antes. Sua pasta de trabalho e esta
(`C:\Users\seraf\CircuitSketch-agentes\antigravity`). Nao edite nenhum
arquivo do projeto: esta tarefa so roda e relata.

## O que fazer

1. Rode, nesta pasta (leva uns 8 minutos; nao mexa no mouse e no teclado
   enquanto as janelas da CircuitSketch abrem e fecham):
   ```
   powershell -NoProfile -ExecutionPolicy Bypass -File ferramentas\verificar.ps1 -Agente antigravity
   ```
   Espere terminar sozinho. Ele compila, confere os idiomas, roda a suite
   inteira (que inclui os 38 projetos de teste) e grava o resumo em
   `coordenacao\verificacao\antigravity\resumo.txt`.
2. Leia o `resumo.txt` e o `saida.txt` da mesma pasta.

## Arquivos que voce pode criar

- o que o `verificar.ps1` gravar (`.artefatos\antigravity\`,
  `coordenacao\verificacao\antigravity\`)
- `coordenacao/antigravity/RELATORIO-001.md`

Mais nada.

## Entrega

`coordenacao/antigravity/RELATORIO-001.md` com: o resumo inteiro (copie o
`resumo.txt`), cada linha FALHA da suite copiada inteira, quantos projetos
funcionam, e quanto tempo levou. Nao tente consertar nada.
