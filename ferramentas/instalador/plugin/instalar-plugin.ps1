# ============================================================
#  Instala o plugin do CircuitSketch na Arduino IDE
#
#  O instalador do CircuitSketch ja faz isto sozinho, com a opcao
#  "Instalar o plugin da Arduino IDE". Este script e para quando isso
#  nao serviu: a Arduino IDE foi instalada depois do CircuitSketch, a
#  pasta de sketches mudou, ou o plugin foi apagado.
#
#  Feche a Arduino IDE antes.
#
#  Uso: dois cliques em "Instalar plugin.bat"
#   ou: powershell -ExecutionPolicy Bypass -File instalar-plugin.ps1 -Sketchbook "D:\Sketches"
# ============================================================
param([string]$Sketchbook)

$ErrorActionPreference = 'Stop'
$origem = Join-Path $PSScriptRoot 'hardware\circuitsketch\esp32'

function Find-Sketchbook {
    # Arduino IDE 2: directories.user em ~\.arduinoIDE\arduino-cli.yaml
    # (so existe se a pasta foi trocada nas preferencias).
    $yaml = Join-Path $env:USERPROFILE '.arduinoIDE\arduino-cli.yaml'
    if (Test-Path -LiteralPath $yaml) {
        $dentro = $false
        foreach ($linha in Get-Content -LiteralPath $yaml) {
            if ($linha.Trim() -eq '' -or $linha.TrimStart().StartsWith('#')) { continue }
            if ($linha -notmatch '^\s') { $dentro = $linha.Trim().StartsWith('directories:'); continue }
            if ($dentro -and $linha.Trim() -match '^user:\s*(.+)$') {
                $valor = $Matches[1].Trim()
                if ($valor.StartsWith('"')) { $valor = $valor.Trim('"').Replace('\\', '\') } else { $valor = $valor.Trim("'") }
                if ($valor) { return $valor }
            }
        }
    }

    # Arduino IDE 1.x
    $pref = Join-Path $env:LOCALAPPDATA 'Arduino15\preferences.txt'
    if (Test-Path -LiteralPath $pref) {
        foreach ($linha in Get-Content -LiteralPath $pref) {
            if ($linha.StartsWith('sketchbook.path=')) {
                $valor = $linha.Substring(16).Trim()
                if ($valor) { return $valor }
            }
        }
    }

    # Padrao das duas. GetFolderPath resolve os Documentos levados pelo OneDrive.
    return Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Arduino'
}

function Find-CircuitSketch {
    $pastas = @()
    $chave = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CircuitSketch' -ErrorAction SilentlyContinue
    if ($chave -and $chave.InstallLocation) { $pastas += $chave.InstallLocation }
    $pastas += (Join-Path $env:LOCALAPPDATA 'Programs\CircuitSketch')

    foreach ($pasta in $pastas) {
        $exe = Join-Path $pasta 'CircuitSketch.exe'
        if (Test-Path -LiteralPath $exe) { return $exe }
    }
    return $null
}

function Test-NossoPlugin([string]$pasta) {
    $platform = Join-Path $pasta 'platform.txt'
    return (Test-Path -LiteralPath $platform) -and ((Get-Content -LiteralPath $platform -Raw) -match 'CircuitSketch|Simulador')
}

Write-Host ''
Write-Host 'CircuitSketch - plugin da Arduino IDE' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path -LiteralPath (Join-Path $origem 'boards.txt'))) {
    Write-Host 'Nao achei os arquivos do plugin em:' -ForegroundColor Red
    Write-Host "  $origem"
    Write-Host 'Rode este script de dentro da pasta "Plugin Arduino IDE".'
    exit 1
}

if (Get-Process -Name 'Arduino IDE', 'arduino' -ErrorAction SilentlyContinue) {
    Write-Host 'AVISO: a Arduino IDE esta aberta. As placas so aparecem depois de fechar e abrir a IDE.' -ForegroundColor Yellow
    Write-Host ''
}

if (-not $Sketchbook) { $Sketchbook = Find-Sketchbook }
$hardware = Join-Path $Sketchbook 'hardware'
$destino = Join-Path $hardware 'circuitsketch\esp32'
Write-Host 'Pasta de sketches da Arduino IDE:'
Write-Host "  $Sketchbook"
Write-Host ''

# Instalacao de quando o programa se chamava Simulador ESP32: sem tirar,
# a IDE listaria as placas duas vezes.
$antiga = Join-Path $hardware 'simulador'
if ((Test-Path -LiteralPath $antiga) -and
    (Get-ChildItem -LiteralPath $antiga -Recurse -Filter platform.txt | Where-Object { Test-NossoPlugin $_.DirectoryName })) {
    Write-Host 'Removendo a instalacao antiga (Simulador ESP32)...' -ForegroundColor Yellow
    Remove-Item -LiteralPath $antiga -Recurse -Force
}

if (Test-Path -LiteralPath $destino) {
    if (-not (Test-NossoPlugin $destino)) {
        Write-Host "A pasta $destino ja existe, mas nao e do CircuitSketch. Nada foi mexido." -ForegroundColor Red
        exit 1
    }
    Remove-Item -LiteralPath $destino -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destino | Out-Null
Copy-Item -Path (Join-Path $origem '*') -Destination $destino -Recurse -Force

$exe = Find-CircuitSketch
if ($exe) {
    # UTF-8 com BOM: sem BOM, o PowerShell do plugin le como ANSI, e um
    # acento no caminho ("Usuarios") viraria lixo.
    [IO.File]::WriteAllText((Join-Path $destino 'tools\caminho.txt'), $exe, (New-Object System.Text.UTF8Encoding $true))
    Write-Host 'CircuitSketch encontrado:' -ForegroundColor Green
    Write-Host "  $exe"
}
else {
    Write-Host 'AVISO: nao achei o CircuitSketch instalado.' -ForegroundColor Yellow
    Write-Host '  Instale o CircuitSketch e rode este script de novo, ou crie o arquivo'
    Write-Host "  $(Join-Path $destino 'tools\caminho.txt')"
    Write-Host '  com o caminho completo do CircuitSketch.exe.'
}

Write-Host ''
Write-Host 'Plugin instalado em:' -ForegroundColor Green
Write-Host "  $destino"
Write-Host ''
Write-Host 'Abra a Arduino IDE e escolha: Ferramentas > Placa > CircuitSketch'
exit 0
