# ============================================================
#  Gera o instalador do CircuitSketch e a pasta de distribuicao
#
#    powershell -ExecutionPolicy Bypass -File ferramentas\instalador\gerar.ps1
#
#  Saida (padrao: Area de Trabalho\CircuitSketch):
#    Instalar CircuitSketch <versao>.exe
#    LEIA-ME.txt
#    Plugin Arduino IDE\
#
#  O numero sai do <Version> de app\App.csproj e o nome que a pessoa
#  ve sai do <InformationalVersion> ("Alpha 16"). Para um update:
#  trocar os dois la e rodar este script de novo.
#
#  Nada e baixado: o instalador compila com o csc do SDK do .NET contra
#  o .NET Framework 4.8 (ver o cabecalho de Instalador.cs).
# ============================================================
param(
    [string]$Destino = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'CircuitSketch'),

    # Usa o executavel ja publicado, sem publicar de novo.
    [switch]$SemPublicar
)

$ErrorActionPreference = 'Stop'
$aqui = $PSScriptRoot
$raiz = (Resolve-Path (Join-Path $aqui '..\..')).Path
$obj  = Join-Path $aqui 'obj'
$utf8ComBom = New-Object System.Text.UTF8Encoding $true
$utf8SemBom = New-Object System.Text.UTF8Encoding $false

# ---------------- versao ----------------
[xml]$projeto = Get-Content -LiteralPath (Join-Path $raiz 'app\App.csproj') -Raw
$versao = $projeto.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $versao -or $versao.Split('.').Count -ne 3) { throw 'app\App.csproj precisa de <Version>X.Y.Z</Version>' }

# O nome que aparece para a pessoa. O Windows exige numero em
# AssemblyVersion, mas "Alpha 16" nao cabe num campo numerico -- entao
# o numero fica no <Version> e o nome no <InformationalVersion>.
$nome = $projeto.Project.PropertyGroup | ForEach-Object { $_.InformationalVersion } | Where-Object { $_ } | Select-Object -First 1
$curta = if ($nome) { $nome } else {
    $partes = $versao.Split('.')
    if ($partes[2] -eq '0') { "$($partes[0]).$($partes[1])" } else { $versao }
}

Write-Host "CircuitSketch $curta ($versao)" -ForegroundColor Cyan

# ---------------- publicar ----------------
$exe = Join-Path $raiz 'app\bin\Release\net10.0-windows\win-x64\publish\CircuitSketch.exe'
if (-not $SemPublicar) {
    $aberto = Get-Process CircuitSketch -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
    if ($aberto) { throw 'Feche o CircuitSketch publicado antes: o publish precisa sobrescrever o executavel.' }

    # O PDB vai embutido (o erro relatado ainda diz a linha), e o PathMap
    # troca a pasta do projeto por um caminho neutro: sem ele, o executavel
    # levaria C:\Users\<quem gerou>\... nos caminhos de cada arquivo-fonte.
    Write-Host 'Publicando...'
    & dotnet publish (Join-Path $raiz 'app') -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
        -p:DebugType=embedded "-p:PathMap=$raiz=C:\CircuitSketch" -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }
}
if (-not (Test-Path -LiteralPath $exe)) { throw "Nao achei $exe" }

# ---------------- pacote ----------------
if (Test-Path -LiteralPath $obj) { Remove-Item -LiteralPath $obj -Recurse -Force }
$pacote = Join-Path $obj 'pacote'
$pacoteApp = Join-Path $pacote 'app'
$pacotePlugin = Join-Path $pacote 'plugin'
New-Item -ItemType Directory -Force -Path $pacoteApp, $pacotePlugin | Out-Null

# A pasta de publicacao inteira, e nao so o .exe: o single-file deixa as
# DLLs nativas do WPF (wpfgfx_cor3.dll e cia.) do lado de fora. Sem elas
# o programa instalado morre ao abrir a janela, com DllNotFoundException
# -- e ninguem via, porque o plugin apontava para a pasta de publicacao,
# onde elas estao. O .pdb fica de fora: so serve para depurar.
$publicacao = Split-Path $exe
Get-ChildItem -LiteralPath $publicacao -File |
    Where-Object { $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $pacoteApp

# E as pastas que ficam fora do single-file: o runtime do modo nativo
# (nativo\: o TinyCC e os cabecalhos do ESP-IDF), as bibliotecas que vem com
# o simulador (bibliotecas\: a ESP32-DOOM e a PlayerDAP) e as licencas. Sem
# elas o DOOM e o player nao compilam no programa instalado.
foreach ($pasta in Get-ChildItem -LiteralPath $publicacao -Directory) {
    Copy-Item -LiteralPath $pasta.FullName -Destination $pacoteApp -Recurse
}
foreach ($pasta in 'nativo', 'bibliotecas', 'licencas') {
    if (-not (Test-Path -LiteralPath (Join-Path $pacoteApp $pasta))) { throw "Falta a pasta $pasta na publicacao" }
}
foreach ($nativa in 'wpfgfx_cor3.dll', 'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $pacoteApp $nativa))) { throw "Falta $nativa na publicacao" }
}

# LEIA-ME com a versao e quebra de linha do Windows, para o Bloco de
# Notas de qualquer versao abrir certo.
$leiame = [IO.File]::ReadAllText((Join-Path $aqui 'LEIA-ME.txt'), $utf8SemBom)
$leiame = ($leiame.Replace('{VERSAO}', $curta) -replace "`r?`n", "`r`n")
[IO.File]::WriteAllText((Join-Path $pacoteApp 'LEIA-ME.txt'), $leiame, $utf8ComBom)

# Plugin: a plataforma inteira menos o experimento AOT (fonte\), que
# nao esta no caminho de carga, e menos o que e de uma maquina so.
foreach ($item in Get-ChildItem -LiteralPath (Join-Path $raiz 'plataforma')) {
    if ($item.Name -eq 'fonte') { continue }
    Copy-Item -LiteralPath $item.FullName -Destination $pacotePlugin -Recurse
}
Get-ChildItem -LiteralPath $pacotePlugin -Recurse -File |
    Where-Object { $_.Name -eq 'caminho.txt' -or $_.Extension -eq '.log' } |
    Remove-Item -Force

# A versao da plataforma acompanha a do programa. Sem BOM: a IDE le o
# platform.txt como texto puro.
$platformTxt = Join-Path $pacotePlugin 'platform.txt'
$texto = [IO.File]::ReadAllText($platformTxt, $utf8SemBom)
$texto = [regex]::Replace($texto, '(?m)^version=.*$', "version=$versao")
[IO.File]::WriteAllText($platformTxt, $texto, $utf8SemBom)

# ---------------- nada desta maquina no pacote ----------------
# O instalador vai para outras pessoas: nenhum arquivo dele pode levar o
# caminho da pasta de quem o gerou (C:\Users\<nome>\...) nem o da pasta do
# projeto. Procura em ASCII e em UTF-16 (as strings do .NET), em tudo.
$proibidos = @("\Users\$env:USERNAME\", "/Users/$env:USERNAME/", $raiz, $raiz.Replace('\', '/'))
$latin1 = [Text.Encoding]::GetEncoding(28591)
$achados = @()
foreach ($arquivo in Get-ChildItem -LiteralPath $pacote -Recurse -File) {
    $conteudo = $latin1.GetString([IO.File]::ReadAllBytes($arquivo.FullName))
    foreach ($p in $proibidos) {
        $largo = -join ($p.ToCharArray() | ForEach-Object { "$_`0" })
        if ($conteudo.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $conteudo.IndexOf($largo, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $achados += "$($arquivo.FullName.Substring($pacote.Length + 1)): $p"
        }
    }
}
if ($achados) {
    $achados | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw 'O pacote leva caminhos desta maquina (acima): nao distribuir assim.'
}
Write-Host 'Pacote conferido: nenhum caminho desta maquina.'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $obj 'pacote.zip'
Write-Host 'Compactando...'
[IO.Compression.ZipFile]::CreateFromDirectory($pacote, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

# ---------------- compilar ----------------
$dotnet = (Get-Command dotnet).Source
$csc = Get-ChildItem -Path (Join-Path (Split-Path $dotnet) 'sdk\*\Roslyn\bincore\csc.dll') |
       Sort-Object FullName | Select-Object -Last 1
if (-not $csc) { throw 'Nao achei o csc.dll do SDK do .NET' }

$referencias = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path -LiteralPath $referencias)) {
    # Sem o pacote de referencia, as bibliotecas do proprio Windows servem.
    $referencias = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
}

$versaoCs = Join-Path $obj 'Versao.cs'
[IO.File]::WriteAllText($versaoCs, @"
using System.Reflection;
[assembly: AssemblyTitle("CircuitSketch - instalador")]
[assembly: AssemblyDescription("Instalador do CircuitSketch, simulador offline de ESP32 e Arduino")]
[assembly: AssemblyProduct("CircuitSketch")]
[assembly: AssemblyCompany("CircuitSketch")]
[assembly: AssemblyVersion("$versao.0")]
[assembly: AssemblyFileVersion("$versao.0")]
[assembly: AssemblyInformationalVersion("$curta")]
namespace CircuitSketch.Instalacao
{
    static class Versao
    {
        public const string Curta = "$curta";
        public const string Completa = "$versao";
    }
}
"@, $utf8SemBom)

$icone = Join-Path $raiz 'app\assets\CircuitSketch.ico'
$logo = Join-Path $aqui 'logo.png'
foreach ($necessario in $icone, $logo) {
    if (-not (Test-Path -LiteralPath $necessario)) { throw "Falta $necessario (rode ferramentas\icone\gerar-icone.ps1)" }
}

function Compilar([string]$saida, [string[]]$recursos) {
    $argumentos = @(
        '-nologo', '-noconfig', '-nostdlib+', '-target:winexe', '-langversion:latest',
        '-optimize+', '-platform:anycpu', '-codepage:65001',
        "-win32icon:$icone", "-win32manifest:$(Join-Path $aqui 'instalador.manifest')",
        "-out:$saida"
    )
    foreach ($nome in 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms',
                      'System.IO.Compression', 'System.IO.Compression.FileSystem') {
        $argumentos += "-r:$(Join-Path $referencias "$nome.dll")"
    }
    foreach ($r in $recursos) { $argumentos += "-resource:$r" }
    $argumentos += (Join-Path $aqui 'Instalador.cs'), $versaoCs

    & $dotnet $csc.FullName @argumentos
    if ($LASTEXITCODE -ne 0) { throw "csc falhou em $saida" }
}

Write-Host 'Compilando o desinstalador e o instalador...'
$desinstalador = Join-Path $obj 'desinstalar.exe'
Compilar $desinstalador @("$logo,logo.png")

$instalador = Join-Path $obj 'instalar.exe'
Compilar $instalador @("$zip,pacote.zip", "$logo,logo.png", "$desinstalador,desinstalador.exe")

# ---------------- pasta de distribuicao ----------------
New-Item -ItemType Directory -Force -Path $Destino | Out-Null

# Tira so o que este script poe la. O resto da pasta nao e dele.
Get-ChildItem -LiteralPath $Destino -Filter 'Instalar CircuitSketch*.exe' | Remove-Item -Force
$pastaPlugin = Join-Path $Destino 'Plugin Arduino IDE'
if (Test-Path -LiteralPath $pastaPlugin) { Remove-Item -LiteralPath $pastaPlugin -Recurse -Force }

$nomeInstalador = "Instalar CircuitSketch $curta.exe"
Copy-Item -LiteralPath $instalador -Destination (Join-Path $Destino $nomeInstalador)
[IO.File]::WriteAllText((Join-Path $Destino 'LEIA-ME.txt'), $leiame, $utf8ComBom)

$hardware = Join-Path $pastaPlugin 'hardware\circuitsketch\esp32'
New-Item -ItemType Directory -Force -Path $hardware | Out-Null
Copy-Item -Path (Join-Path $pacotePlugin '*') -Destination $hardware -Recurse
Copy-Item -Path (Join-Path $aqui 'plugin\*') -Destination $pastaPlugin

Write-Host ''
Write-Host 'Pronto:' -ForegroundColor Green
Get-ChildItem -LiteralPath $Destino | ForEach-Object {
    $tamanho = if ($_.PSIsContainer) { '' } else { '{0,8:N1} MB' -f ($_.Length / 1MB) }
    Write-Host ("  {0,-38} {1}" -f $_.Name, $tamanho)
}
