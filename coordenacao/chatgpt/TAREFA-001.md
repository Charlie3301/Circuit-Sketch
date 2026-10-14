# TAREFA-001 (ChatGPT/Codex): o separador dos projetos testados

Leia `AGENTS.md` antes. Sua pasta de trabalho e esta
(`C:\Users\seraf\CircuitSketch-agentes\chatgpt`).

## Contexto

`projetos/` tem 38 projetos de teste (`*.sktch`, gerados por
`ferramentas/projetos.py`). Um bloco do autoteste (que o Claude esta
escrevendo) roda cada projeto e grava o resultado num JSON. Falta o script
que pega esse resultado e separa os projetos em duas pastas para o usuario:
`TESTED\WORKING` e `TESTED\NOT WORKING`.

## O que fazer

Crie `ferramentas/testados.py` (Python 3, so biblioteca padrao).

Uso:

```
python ferramentas/testados.py <resultados.json> [<destino>]
```

- `<destino>` padrao: `C:/Users/seraf/OneDrive/Desktop/TESTED`.
- Os `.sktch` de origem estao na pasta `projetos/` ao lado do
  `resultados.json` (use a pasta do JSON, nao um caminho fixo).

Formato do `resultados.json` (este e o contrato; siga exatamente):

```json
{
  "data": "2026-10-13T22:40:00",
  "projetos": {
    "rover-4wd": {
      "titulo": "Rover 4WD (L298N, 4 motores amarelos)",
      "funcionou": false,
      "erro": null,
      "conferencias": [
        {"nome": "frente", "ok": true, "visto": "serial: frente"},
        {"nome": "os quatro motores giram", "ok": false, "visto": "m_fe 0 rpm, m_te 0 rpm"}
      ]
    }
  }
}
```

`erro` e um texto quando o projeto nem chegou a rodar (excecao, nao compilou);
nesse caso `conferencias` pode vir vazia.

Para cada projeto do JSON:

1. Escolha a pasta: `funcionou: true` -> `<destino>/WORKING`; senao
   `<destino>/NOT WORKING` (crie as duas se faltarem).
2. Se uma copia antiga desse mesmo projeto estiver na OUTRA pasta, apague so
   ela (o arquivo `<nome>.sktch` daquele projeto). Nao apague mais nada em
   `<destino>`.
3. Leia `projetos/<nome>.sktch` (JSON, UTF-8). O campo `descricao` e uma
   lista de linhas. Ponha NO COMECO dela um cabecalho:
   - funcionou:
     `TESTADO NO SIMULADOR em DD/MM/AAAA: FUNCIONA.`, linha vazia,
     `O que foi conferido:`, uma linha `  - <nome da conferencia>` para cada
     uma, linha vazia.
   - nao funcionou:
     `TESTADO NO SIMULADOR em DD/MM/AAAA: NAO FUNCIONA.` (com acento: "NÃO"),
     linha vazia, `O que falhou:`, uma linha `  - <nome>: <visto>` para cada
     conferencia com `ok: false` (ou `  - nao rodou: <erro>` se houver erro),
     linha vazia, `O que funcionou:` com as que passaram (se houver), linha
     vazia.
   A data vem do campo `data` do JSON.
4. Grave a copia em `<pasta escolhida>/<nome>.sktch`, com
   `json.dumps(..., ensure_ascii=False, indent=1)` e UTF-8 sem BOM (o mesmo
   formato do original). Nunca altere o arquivo de `projetos/`.

No fim, imprima um resumo: quantos em WORKING, quantos em NOT WORKING, e a
lista dos que nao funcionaram com a primeira falha de cada.

## Teste antes de entregar

- Crie `coordenacao/chatgpt/exemplo-resultados.json` com 3 projetos
  verdadeiros de `projetos/` (um que funcionou, um com conferencia falhando,
  um com `erro`), no formato acima.
- Para o teste, o script precisa achar os `.sktch`: aceite um terceiro
  argumento opcional `--origem <pasta>` (padrao: a pasta `projetos/` ao lado
  do JSON). No teste use `--origem projetos`.
- Rode com destino `coordenacao/chatgpt/TESTED-exemplo` e confira as tres
  copias (o cabecalho certo, o resto do arquivo igual ao original).
- Rode de novo trocando um projeto de funcionou para nao funcionou e confira
  que ele saiu de WORKING e foi para NOT WORKING.

## Arquivos que voce pode criar ou alterar

- `ferramentas/testados.py`
- `coordenacao/chatgpt/exemplo-resultados.json`
- `coordenacao/chatgpt/TESTED-exemplo/` (o que o teste gerar)
- `coordenacao/chatgpt/RELATORIO-001.md`

Mais nada. Nao rode o script com o destino padrao (o Desktop do usuario):
quem faz isso e o Claude.

## Entrega

`coordenacao/chatgpt/RELATORIO-001.md` como diz o `AGENTS.md`, com a saida
dos dois testes.
