# RELATÓRIO-001 — separador dos projetos testados

## O que foi feito

- `ferramentas/testados.py`: script Python 3, somente biblioteca padrão, com argumentos `resultados`, destino opcional e `--origem`. Usa por padrão `projetos/` ao lado do JSON e o destino solicitado na tarefa. Separa as cópias, acrescenta os cabeçalhos, remove somente a cópia do mesmo projeto na outra categoria e imprime contagens e primeiras falhas. Grava JSON com `ensure_ascii=False`, `indent=1`, UTF-8 sem BOM e quebras LF. Lê os projetos antes de gravar, rejeita nomes com separadores de caminho e evita destino coincidente com o original.
- `coordenacao/chatgpt/exemplo-resultados.json`: três resultados fictícios para projetos reais: `semaforo` aprovado, `rover-4wd` com conferência falhando e `servo-potenciometro` com erro de compilação simulado. Não representam medições do simulador.
- `coordenacao/chatgpt/TESTED-exemplo/NOT WORKING/semaforo.sktch`: cópia final após a mudança de classificação no segundo teste.
- `coordenacao/chatgpt/TESTED-exemplo/NOT WORKING/rover-4wd.sktch`: cópia com falha e conferência aprovada descritas no cabeçalho.
- `coordenacao/chatgpt/TESTED-exemplo/NOT WORKING/servo-potenciometro.sktch`: cópia com o erro simulado no cabeçalho.
- `coordenacao/chatgpt/TESTED-exemplo/WORKING/preservar.txt`: arquivo de controle criado pelo teste e preservado durante a mudança de classificação.
- `coordenacao/chatgpt/RELATORIO-001.md`: este relatório.

## Comandos e resultados

Leituras: `Get-Content -Raw AGENTS.md`; `Get-Content -Raw -Encoding UTF8 coordenacao/chatgpt/TAREFA-001.md`; `Get-Content -Encoding UTF8 ferramentas/projetos.py -TotalCount 65`. Todos encerraram com código 0. A primeira leitura exibiu acentos incorretamente no terminal; as regras também estavam integralmente fornecidas na conversa.

Inspeção: `git status --short` mostrou inicialmente apenas `?? coordenacao/`. `rg --files -g AGENTS.md -g '*.sktch' -g 'testados.py' -g 'RELATORIO-001.md' -g 'exemplo-resultados.json' ferramentas projetos coordenacao` listou os 38 projetos. Uma chamada `python -c` carregou e imprimiu as descrições dos três projetos escolhidos. Todos encerraram com código 0.

Os testes foram conduzidos por código Python enviado com here-string do PowerShell (`@' ... '@ | python -`), usando `subprocess.run` para executar o comando abaixo, com destino explicitamente local:

```text
python ferramentas/testados.py coordenacao/chatgpt/exemplo-resultados.json coordenacao/chatgpt/TESTED-exemplo --origem projetos
```

O verificador comparou os cabeçalhos com listas literais esperadas, todo o conteúdo restante com o JSON original, os bytes com `json.dumps(..., ensure_ascii=False, indent=1).encode('utf-8')`, a ausência de cópia na categoria oposta e a igualdade byte a byte dos originais antes e depois. Também verificou código de saída, contagens e existência das duas pastas.

### Tentativas iniciais que falharam

1. O script encerrou com código 0 e resumo 1 WORKING / 2 NOT WORKING, mas o verificador encerrou com código 1 após 3 verificações aprovadas e 1 falha:

   ```text
   AssertionError: Conte?do incorreto: semaforo
   ```

   O pipe do PowerShell substituiu os acentos nos literais do verificador. Foi definida `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)` somente na sessão do comando de teste, sem alteração persistente de configuração.

2. Com o pipe corrigido, o script novamente encerrou com código 0, mas o verificador encerrou com código 1 após 4 verificações aprovadas e 1 falha:

   ```text
   AssertionError: Formatação/BOM: semaforo
   ```

   A comparação semântica passou; a comparação de bytes detectou CRLF na cópia. O script foi corrigido para gravar com `newline='\n'`.

### Execução final — teste 1

```text
WORKING: 1
NOT WORKING: 2
Projetos que não funcionaram:
  - rover-4wd: os quatro motores giram: m_fe 0 rpm, m_te 0 rpm
  - servo-potenciometro: não rodou: Erro de compilação simulado para testar o separador
Verificações: 15 passaram, 0 falharam.
```

### Execução final — teste 2

O verificador mudou temporariamente `semaforo.funcionou` para `false` e sua conferência para `ok: false`, com `visto: "LED amarelo não acendeu (falha simulada)"`, e repetiu o mesmo comando.

```text
WORKING: 0
NOT WORKING: 3
Projetos que não funcionaram:
  - semaforo: sequência dos três LEDs: LED amarelo não acendeu (falha simulada)
  - rover-4wd: os quatro motores giram: m_fe 0 rpm, m_te 0 rpm
  - servo-potenciometro: não rodou: Erro de compilação simulado para testar o separador
Verificações: 15 passaram, 0 falharam.

Preservação adicional: 3 passaram, 0 falharam.
TOTAL: 33 passaram, 0 falharam.
```

As três verificações adicionais confirmaram a preservação do arquivo de controle e a igualdade byte a byte das cópias dos outros dois projetos entre as execuções. O processo de verificação final e ambas as chamadas do script encerraram com código 0.

## Estado final, limites e riscos

O JSON de exemplo foi restaurado aos bytes iniciais, mantendo os três cenários pedidos para reutilização. As cópias ficaram no estado do segundo teste: três projetos em NOT WORKING e nenhum `.sktch` em WORKING. Rodar novamente o comando com o exemplo entregue retorna o semáforo a WORKING.

Nenhum original foi modificado. O destino padrão no Desktop não foi utilizado. Não houve compilação nem execução de testes do aplicativo, pois a tarefa trata somente do separador Python. O caminho padrão de origem não foi exercitado em execução: os dois testes usaram `--origem projetos`, conforme solicitado. Não houve instalação, acesso ao pendrive nem comando Git de alteração.

Não há pendência de escopo ou necessidade de alterar outros arquivos. A gravação das cópias não é transacional: uma falha de disco pode deixar uma execução parcial; o script informa o erro com código de saída 1.

Conferência final: `git status --short --untracked-files=all` encerrou com código 0 e listou os arquivos entregues, além de `TAREFA-001.md`, `execucao-001.log` e `execucao-001.log.err`, que não foram editados por mim. `git diff --check` encerrou com código 0, sem saída; esse comando não avalia o conteúdo dos arquivos novos ainda não rastreados.
