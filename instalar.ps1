# Instala a plataforma do CircuitSketch no sketchbook da Arduino IDE.
# Instalacao MANUAL: sem internet, sem Boards Manager, sem URL extra.
# Feche a Arduino IDE antes de rodar.

$origem  = Join-Path $PSScriptRoot 'plataforma'
$destino = Join-Path $env:USERPROFILE 'OneDrive\Documents\Arduino\hardware\circuitsketch\esp32'

# Instalacao antiga, de quando o programa se chamava Simulador ESP32.
# Sem apagar, a IDE listaria as duas placas.
$antiga = Join-Path $env:USERPROFILE 'OneDrive\Documents\Arduino\hardware\simulador'
if (Test-Path $antiga) {
    Write-Host "Removendo a instalacao antiga (Simulador ESP32)..." -ForegroundColor Yellow
    Remove-Item $antiga -Recurse -Force
}

# Onde o executavel do simulador foi publicado.
$exe = Join-Path $PSScriptRoot 'app\bin\Release\net10.0-windows\win-x64\publish\CircuitSketch.exe'

if (Test-Path $destino) {
    Write-Host "Removendo instalacao anterior..." -ForegroundColor Yellow
    Remove-Item $destino -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destino | Out-Null
Copy-Item "$origem\*" $destino -Recurse -Force

# Grava o caminho do executavel para o enviar.bat encontrar.
$arquivoCaminho = Join-Path $destino 'tools\caminho.txt'
if (Test-Path $exe) {
    Set-Content -Path $arquivoCaminho -Value $exe -Encoding ascii -NoNewline
    Write-Host "Simulador encontrado:" -ForegroundColor Green
    Write-Host "  $exe"
} else {
    Write-Host "AVISO: CircuitSketch ainda nao publicado." -ForegroundColor Yellow
    Write-Host "  Rode:  dotnet publish app -c Release -r win-x64 --self-contained -p:PublishSingleFile=true"
    Write-Host "  Ate la o Verificar so registra em circuitsketch-ponte.log."
}

Write-Host ""
Write-Host "Plataforma instalada em:" -ForegroundColor Green
Write-Host "  $destino"
Write-Host ""
Write-Host "Na Arduino IDE: Ferramentas > Placa > ESP32 (CircuitSketch)"
Write-Host "Os dois botoes funcionam:"
Write-Host "  Verificar  - compila e abre o CircuitSketch"
Write-Host "  Upload     - mostra o progresso da gravacao e abre o CircuitSketch"
