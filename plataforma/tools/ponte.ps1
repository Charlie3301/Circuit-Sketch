# ============================================================
#  Ponte Arduino IDE  ->  CircuitSketch
#
#  Chamada de dois lugares do platform.txt:
#
#    -Modo compilar : hook de postbuild, no botao Verificar
#    -Modo gravar   : ferramenta de upload, no botao Upload
#
#  Substituiu o antigo enviar.bat. O .bat tinha dois problemas que
#  esta versao nao tem: os argumentos eram posicionais (foi assim que
#  a palavra UPLOAD acabou sendo lida como a pasta do sketch, e o
#  Upload terminava em erro), e passar dez valores de placa por
#  argumento posicional em lote seria pedir para errar aspas.
#
#  Por que a gravacao nao recebe o caminho do sketch: {build.source.path}
#  so e resolvida na compilacao. No upload ela chegava literal, entre
#  chaves. Entao o passo de compilar anota o caminho na pasta de build
#  e o de gravar le de la.
#
#  O caminho do executavel vem de caminho.txt, escrito pelo
#  instalar.ps1 -- assim o script nao precisa adivinhar onde o
#  projeto foi parar.
# ============================================================
param(
    [ValidateSet('compilar', 'gravar')]
    [string]$Modo = 'compilar',

    [string]$Sketch,
    [string]$Build,

    [string]$Placa = 'CircuitSketch',

    # Id da peca no catalogo do simulador (esp32-devkit-v1, arduino-uno...).
    # Vai junto para o programa poder avisar quando a placa escolhida na
    # IDE nao e a que esta desenhada no circuito.
    [string]$Peca = '',
    [string]$Gravador = 'esptool',
    [string]$Assinatura = '',

    [double]$PisoFlash = 197000,
    [double]$PisoRam = 13400,
    [double]$FatorFlash = 6.4,
    [double]$FatorRam = 0.42,
    [int]$FlashMaxima = 1310720,
    [int]$RamMaxima = 327680
)

. (Join-Path $PSScriptRoot 'medida.ps1')
. (Join-Path $PSScriptRoot 'gravar.ps1')

$anotacao = if ($Build) { Join-Path $Build 'circuitsketch-sketch.txt' } else { $null }

if ($Modo -eq 'compilar') {
    if (-not (Test-Path -LiteralPath $Sketch)) {
        [Console]::Error.WriteLine("ERROR: sketch folder not found: $Sketch")
        exit 1
    }
    # Deixa o rastro para o passo de gravar, que nao recebe o caminho.
    if ($anotacao) { Set-Content -LiteralPath $anotacao -Value $Sketch -Encoding UTF8 }
}
else {
    if ($anotacao -and (Test-Path -LiteralPath $anotacao)) {
        $Sketch = (Get-Content -LiteralPath $anotacao -Raw).Trim()
    }
    if (-not $Sketch -or -not (Test-Path -LiteralPath $Sketch)) {
        [Console]::Error.WriteLine("ERROR: the sketch of this build was not found.")
        [Console]::Error.WriteLine("Verify (compile) once before uploading.")
        exit 1
    }

    $m = Get-MedidaDoSketch -Caminho $Sketch -PisoFlash $PisoFlash -PisoRam $PisoRam `
            -FatorFlash $FatorFlash -FatorRam $FatorRam `
            -FlashMaxima $FlashMaxima -RamMaxima $RamMaxima

    if ($Gravador -eq 'avrdude') {
        Show-GravacaoAvrdude -Programa $m.Programa -Sketch $Sketch `
                             -Placa $Placa -Assinatura $Assinatura
    }
    else {
        Show-GravacaoEsptool -Programa $m.Programa -Sketch $Sketch -Placa $Placa
    }
}

# ---- abre o simulador ----
$caminho = Join-Path $PSScriptRoot 'caminho.txt'
$exe = if (Test-Path -LiteralPath $caminho) { (Get-Content -LiteralPath $caminho -Raw).Trim() } else { '' }

# Sem caminho.txt (plugin copiado a mao) ou apontando para um lugar que
# nao existe mais: procura onde o instalador poe o programa.
if (-not $exe -or -not (Test-Path -LiteralPath $exe)) {
    $pastas = @()
    $chave = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CircuitSketch' -ErrorAction SilentlyContinue
    if ($chave -and $chave.InstallLocation) { $pastas += $chave.InstallLocation }
    $pastas += (Join-Path $env:LOCALAPPDATA 'Programs\CircuitSketch')
    foreach ($pasta in $pastas) {
        $candidato = Join-Path $pasta 'CircuitSketch.exe'
        if (Test-Path -LiteralPath $candidato) { $exe = $candidato; break }
    }
}

if ($exe -and (Test-Path -LiteralPath $exe)) {
    if ($Modo -eq 'gravar') { Write-Host "Opening CircuitSketch..." }
    $argumentos = @($Sketch)
    if ($Peca) { $argumentos += @('--placa', $Peca) }
    Start-Process -FilePath $exe -ArgumentList $argumentos -WindowStyle Normal
    exit 0
}

[Console]::Error.WriteLine("WARNING: CircuitSketch was not found.")
[Console]::Error.WriteLine("Run 'Instalar plugin.bat' (in the CircuitSketch folder) again to register where CircuitSketch.exe is.")
exit 0
