<#
  ferramentas\verificar.ps1 [-Agente <nome>] [-SoProjetos] [-Bloco <Nome>]

  A verificacao inteira num comando so (base da Alpha 26):
    1. compila em .artefatos\<agente> (cada agente na sua pasta: duas
       compilacoes no mesmo app\obj se atropelam);
    2. confere os catalogos de idiomas (ferramentas\idiomas.py --conferir);
    3. roda a suite (que inclui os 38 projetos de teste) -- ou so os projetos
       (-SoProjetos), ou so um bloco (-Bloco);
    4. resume: a linha final da suite, cada FALHA, e os projetos que nao
       funcionam.

  Tudo vai para coordenacao\verificacao\<agente>\: saida.txt (a suite),
  projetos.json (o resultado de cada projeto) e resumo.txt.
  Sai com 1 se a compilacao falhar, um catalogo estiver com defeito ou algum
  teste falhar.
#>
param(
    [string]$Agente = 'verificar',
    [switch]$SoProjetos,
    [string]$Bloco = ''
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
Set-Location $raiz
$pasta = Join-Path $raiz "coordenacao\verificacao\$Agente"
New-Item -ItemType Directory -Force $pasta | Out-Null
$resumo = New-Object System.Collections.Generic.List[string]
$relogio = [Diagnostics.Stopwatch]::StartNew()
$problema = $false

function Anotar([string]$linha) { $resumo.Add($linha); Write-Host $linha }

# 1. compilar
Anotar "== compilar (.artefatos\$Agente)"
$compilacao = & dotnet build app/App.csproj -nologo -v q --artifacts-path ".artefatos/$Agente" 2>&1
if ($LASTEXITCODE -ne 0) {
    Anotar 'COMPILACAO FALHOU:'
    $compilacao | Select-String ' error ' | Select-Object -First 15 | ForEach-Object { Anotar "  $($_.Line.Trim())" }
    $resumo | Set-Content (Join-Path $pasta 'resumo.txt') -Encoding UTF8
    exit 1
}
Anotar 'compilou'
$exe = Join-Path $raiz ".artefatos\$Agente\bin\App\debug\CircuitSketch.exe"

# 2. idiomas
Anotar '== idiomas'
$idiomas = & python ferramentas/idiomas.py --conferir 2>&1
$idiomas | ForEach-Object { Anotar "  $_" }
if ($LASTEXITCODE -ne 0) { $problema = $true }

# 3. testes
$saida = Join-Path $pasta 'saida.txt'
$json = Join-Path $pasta 'projetos.json'
Remove-Item $saida, $json -ErrorAction SilentlyContinue
$argumentos = @('--teste', '--saida', "`"$saida`"")
if ($SoProjetos) { $argumentos = @('--teste', '--so', 'Projetos_Testados', '--saida', "`"$saida`"") }
elseif ($Bloco) { $argumentos = @('--teste', '--so', $Bloco, '--saida', "`"$saida`"") }
Anotar ("== testes: " + ($(if ($SoProjetos) { 'so os projetos' } elseif ($Bloco) { "o bloco $Bloco" } else { 'a suite inteira' })))
$env:CS_RESULTADOS = $json
try { $p = Start-Process -FilePath $exe -ArgumentList $argumentos -Wait -PassThru }
finally { Remove-Item Env:CS_RESULTADOS -ErrorAction SilentlyContinue }

if (-not (Test-Path $saida)) {
    Anotar "A SUITE NAO GRAVOU A SAIDA (codigo $($p.ExitCode))"
    $problema = $true
} else {
    $linhas = Get-Content $saida -Encoding UTF8
    Anotar ('  ' + ($linhas | Where-Object { $_ -match 'passaram, \d+ falharam' } | Select-Object -Last 1))
    $falhas = @($linhas | Where-Object { $_ -match '^\s*FALHA' })
    foreach ($f in $falhas) { Anotar "  $($f.Trim())" }
    if ($falhas.Count -gt 0) { $problema = $true }
}

# 4. os projetos
if (Test-Path $json) {
    $res = Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json
    $nomes = @($res.projetos.PSObject.Properties)
    $bons = @($nomes | Where-Object { $_.Value.funcionou })
    Anotar "== projetos: $($bons.Count) de $($nomes.Count) funcionam"
    foreach ($n in $nomes | Where-Object { -not $_.Value.funcionou }) {
        $primeira = if ($n.Value.erro) { "nao rodou: $($n.Value.erro)" }
                    else { $c = @($n.Value.conferencias | Where-Object { -not $_.ok })[0]; "$($c.nome) -- $($c.visto)" }
        Anotar "  NAO FUNCIONA $($n.Name): $primeira"
    }
}

Anotar ("== levou {0:0} s" -f $relogio.Elapsed.TotalSeconds)
$resumo | Set-Content (Join-Path $pasta 'resumo.txt') -Encoding UTF8
if ($problema) { exit 1 } else { exit 0 }
