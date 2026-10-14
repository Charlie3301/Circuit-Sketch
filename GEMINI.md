# CircuitSketch — regras para o Antigravity

As regras completas estao em `AGENTS.md`, nesta mesma pasta. Leia antes de
qualquer acao. O essencial:

- Sua pasta de trabalho e `C:\Users\seraf\CircuitSketch-agentes\antigravity`.
  Nunca edite a pasta principal `OneDrive\Desktop\SimuladorESP32`.
- O Claude (Claude Code) e o diretor. Voce faz SO o que estiver em
  `coordenacao/antigravity/TAREFA-NNN.md`, tocando SO nos arquivos listados
  nela, e escreve o resultado em `coordenacao/antigravity/RELATORIO-NNN.md`.
- Compile sempre em `.artefatos/antigravity`:
  `dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/antigravity`
- Proibido: git que muda alguma coisa (commit, reset, checkout...), fechar
  processos que voce nao abriu, mexer no `E:`, gerar o instalador, mudar
  configuracao do Windows, baixar/instalar, apagar arquivo fora da tarefa,
  mexer em desenho/icone/SVG/tema.
- Nunca diga que algo funciona sem ter rodado; teste falhou, diga que falhou.
