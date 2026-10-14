# CircuitSketch — regras para os agentes

Este arquivo vale para TODO agente que trabalhar nesta pasta (ChatGPT/Codex,
Antigravity ou outro). Leia inteiro antes de qualquer acao.

## Quem e quem

- **Claude (Claude Code) e o diretor.** Planeja, divide o trabalho, faz as
  partes pesadas, revisa tudo o que entra e decide. Toda tarefa sai dele.
- **ChatGPT/Codex** fica com as tarefas intermediarias.
- **Antigravity** fica com as tarefas mais leves (testes, conferencias,
  relatorios, ajustes pequenos).
- **Parte grafica e so do Claude:** icones, desenhos de pecas (SVG), botoes,
  telas, cores, temas. Nenhum outro agente mexe em `app/assets/*.svg`,
  `ferramentas/extrair-svg/pecas-*.py`, `app/assets/*.png`, XAML de aparencia
  ou nos temas.

## Onde cada um trabalha

Cada agente tem a SUA copia do projeto (um worktree do git), fora do
OneDrive. A pasta principal (`OneDrive\Desktop\SimuladorESP32`) e do Claude:
nenhum outro agente edita nada nela.

| Agente | Pasta de trabalho |
|---|---|
| ChatGPT/Codex | `C:\Users\seraf\CircuitSketch-agentes\chatgpt` |
| Antigravity | `C:\Users\seraf\CircuitSketch-agentes\antigravity` |

O Claude le o que voce mudou (`git diff`) e traz para a pasta principal.

## Como funciona uma tarefa

1. As tarefas ficam em `coordenacao/<agente>/TAREFA-NNN.md` (`chatgpt` ou
   `antigravity`), dentro da sua pasta de trabalho. Faca SO o que a tarefa pede.
2. A tarefa lista os arquivos que voce pode criar ou alterar. **Nao toque em
   nenhum outro.** Precisa de mais um? Pare e escreva isso no relatorio.
3. Ao terminar, escreva `coordenacao/<agente>/RELATORIO-NNN.md`:
   - o que foi feito, arquivo por arquivo;
   - os comandos que voce rodou e o resultado de verdade (numeros de testes
     passando/falhando, com a linha do erro quando falhar);
   - o que ficou de fora, duvidas e riscos.
   Nunca diga que algo funciona sem ter rodado. Teste falhou? Diga que falhou.
4. Escreva em portugues.

## Proibido (sem excecao, mesmo que pareca ajudar)

- **Git que muda alguma coisa:** commit, reset, checkout, restore, clean,
  stash, rebase, merge, branch. O git e do Claude. Pode usar `git status`,
  `git diff` e `git log` para ler.
- **Fechar ou matar processos que voce nao abriu.** Principalmente a
  CircuitSketch aberta pelo usuario (`app/bin/Teste`, `app/bin/Claude`).
- **Mexer no pendrive `E:`.** Os originais do professor ficam em
  `E:\Sktch[\NOT WORKING` e nunca sao alterados.
- **Gerar o instalador** (`ferramentas/instalador/gerar.ps1`): so na Alpha 26.
- **Rodar `ferramentas/bateria.py` sem argumento** (o padrao grava no `E:`).
  Use sempre `python ferramentas/bateria.py bateria`.
- **Mudar configuracao do Windows**, de seguranca, de rede, ou ligar o
  Bluetooth do computador.
- **Baixar ou instalar qualquer coisa** sem a tarefa autorizar.
- **Mandar dados do usuario** (e-mail, arquivos, musicas) para qualquer lugar.
- **Apagar arquivo** que a tarefa nao mandou apagar.

## Compilar e testar

Cada agente compila na SUA pasta (duas compilacoes na mesma pasta quebram):

```
dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/<agente>
```

O executavel fica em `.artefatos/<agente>/bin/App/debug/CircuitSketch.exe`.

- Um bloco de testes: `CircuitSketch.exe --teste --so <Bloco> --saida coordenacao/<agente>/saida-NNN.txt`
- A suite inteira (~5 min): `CircuitSketch.exe --teste --saida coordenacao/<agente>/saida-NNN.txt`
- O passeio pela interface: `CircuitSketch.exe --passeio <pasta>`
- A ultima linha da saida diz "N passaram, M falharam".

Os testes abrem janelas da CircuitSketch e as fecham sozinhos. Nao use o
teclado e o mouse no app enquanto eles rodam.

## O projeto

- C#/WPF (.NET), simulador offline de ESP32/Arduino para distribuicao
  publica. Codigo em `app/` (interface e editor) e `motor/` (interpretador,
  placas, perifericos). Testes em `app/Autoteste*.cs`.
- Nomes em portugues. Siga o estilo do arquivo que voce estiver editando
  (comentarios, nomes, densidade). Texto que o usuario le leva acento.
- Geradores em `ferramentas/` (Python): `bateria.py` (bateria de testes em
  `bateria/`), `projetos.py` (projetos de teste em `projetos/`).
- Fidelidade ao hardware real e prioridade: na duvida, o datasheet manda.
