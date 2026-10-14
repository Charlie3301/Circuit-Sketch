# TAREFA-003 — Gerenciador de bibliotecas: a parte de dentro (sem tela)

Leia o `AGENTS.md` antes. A tela (a janela, os botoes) e do Claude: aqui e
so o codigo que busca, baixa e instala. Trabalhe na sua pasta
(`C:\Users\seraf\CircuitSketch-agentes\chatgpt`).

## O que ja existe

- `app/Acervo.cs`: as bibliotecas do simulador e as instaladas pela pessoa
  em `Acervo.Pasta` (`%APPDATA%\CircuitSketch\bibliotecas`, ou a pasta isolada
  dos testes, via `Armazem.Pasta`). Ja tem `Acervo.InstalarZip(arquivo)` (com a
  protecao contra caminho que sai da pasta), `Acervo.Instaladas()`,
  `Acervo.EhDoSimulador(cabecalho)` e `Acervo.OQueFalta(sketch)`.
- `app/JanelaBibliotecas.cs`: a janela (NAO mexa).

## O que fazer

Criar `app/BibliotecasOnline.cs` (namespace `Simulador.App`) com o gerenciador
do indice oficial da Arduino (o mesmo da Arduino IDE):

1. `const string UrlDoIndice = "https://downloads.arduino.cc/libraries/library_index.json.gz";`
2. `record BibliotecaOnline(...)`: nome, versao, autor, sentence, paragraph,
   website, category, architectures, `Cabecalhos` (o `providesIncludes`),
   url, archiveFileName, size, checksum ("SHA-256:..."), dependencias
   (nome + versao opcional) e a lista das outras versoes do mesmo nome.
3. `sealed class IndiceDeBibliotecas`:
   - `static IndiceDeBibliotecas Ler(Stream json)`: le o JSON (pode ter
     dezenas de MB: use `Utf8JsonReader`/`JsonDocument` sem montar objetos
     inuteis); fica com a versao mais nova de cada nome (comparacao de versao
     semantica: "1.10.0" > "1.9.2"; versao invalida fica por ultimo) e guarda
     as outras versoes.
   - `IReadOnlyList<BibliotecaOnline> Buscar(string texto, int maximo = 50)`:
     nome exato > nome comeca com > nome contem > cabecalho > sentence/paragraph
     contem. Sem diferenca de maiuscula e de acento.
   - `IReadOnlyList<BibliotecaOnline> QuemTem(string cabecalho)`: quem lista o
     cabecalho no `providesIncludes`; se ninguem lista, o nome igual ao
     cabecalho sem o `.h` (ex.: `Bounce2.h` -> `Bounce2`).
   - `IReadOnlyList<BibliotecaOnline> ParaInstalar(BibliotecaOnline alvo, ISet<string> jaInstaladas)`:
     a biblioteca e as dependencias (recursivo, sem repetir, sem ciclo
     infinito), na ordem de instalar (as dependencias antes), pulando as ja
     instaladas e as que o simulador ja tem por dentro (`Acervo.EhDoSimulador`
     pelo cabecalho principal).
4. `static class BibliotecasOnline`:
   - `PastaDoCache` = `Path.Combine(Armazem.Pasta, "indice-bibliotecas")`.
   - `Task<IndiceDeBibliotecas> CarregarAsync(HttpClient http, bool atualizar, CancellationToken ct)`:
     usa o .gz do cache se existir e `atualizar` for falso; senao baixa
     (gravando primeiro num .tmp e trocando no fim), descompacta com
     `GZipStream` e le. Sem internet e com cache: usa o cache. Sem os dois:
     erro com mensagem em portugues para a pessoa.
   - `Task<string> BaixarEInstalarAsync(HttpClient http, BibliotecaOnline lib, IProgress<double> progresso, CancellationToken ct)`:
     so aceita `https://downloads.arduino.cc/...` (recusa outro host com
     mensagem clara); baixa para um arquivo temporario, confere o tamanho e o
     SHA-256 do indice (diferente: apaga o temporario e da erro), instala com
     `Acervo.InstalarZip` e devolve o nome instalado. O temporario sai sempre
     (try/finally).
   - `Task<List<string>> InstalarComDependenciasAsync(...)`: usa `ParaInstalar`
     e instala uma por uma, devolvendo os nomes.
   - Nada disso pode travar a interface: tudo assincrono, com `CancellationToken`.
5. Criar `app/AutotesteBibliotecasOnline.cs`, bloco `Bibliotecas_Online`
   (mesmo estilo dos outros `Autoteste*.cs`: `Conferir("...", condicao, "visto")`),
   **sem internet nenhuma**: monte no proprio teste um indice JSON pequeno
   (3 ou 4 bibliotecas, duas versoes de uma, uma dependencia em cadeia, um
   ciclo de dependencia, um `providesIncludes`) e um .zip de biblioteca de
   mentira em memoria; sirva os dois por um `HttpMessageHandler` falso.
   Confira: versao mais nova, busca (ordem), `QuemTem`, ordem das
   dependencias, ciclo nao trava, SHA-256 errado recusa e nao instala, host
   fora do downloads.arduino.cc recusado, a instalacao cai em `Acervo.Pasta`
   (use a pasta isolada dos testes: confira como os outros testes isolam o
   `Armazem`) e o cache e usado sem internet.
6. Registrar o bloco: uma linha `Bloco("Bibliotecas_Online", Bibliotecas_Online);`
   em `app/Autoteste.cs`, junto dos outros blocos de bibliotecas (procure
   "Bibliotecas" ou "Acervo" ali).

## Arquivos que voce pode criar/alterar

- criar `app/BibliotecasOnline.cs`
- criar `app/AutotesteBibliotecasOnline.cs`
- `app/Autoteste.cs`: so a linha do `Bloco(...)`
- `coordenacao/chatgpt/RELATORIO-003.md` e saidas `coordenacao/chatgpt/saida-003*.txt`

## Proibido nesta tarefa

- Baixar qualquer coisa da internet (os testes sao offline; nao rode o
  download de verdade).
- Mexer na janela/tela ou em qualquer outro arquivo.

## Como conferir

```
dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/chatgpt
.artefatos\chatgpt\bin\App\debug\CircuitSketch.exe --teste --so Bibliotecas --saida coordenacao\chatgpt\saida-003.txt
```

O relatorio tem que trazer a ultima linha da saida ("N passaram, M falharam")
e as linhas FALHA, se houver. Seja enxuto: o seu limite de uso e curto.
