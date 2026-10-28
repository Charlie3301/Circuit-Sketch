# Baixa o compilador de C/C++ do modo nativo (o Zig, que traz o clang e a
# biblioteca padrao do C++) para %LOCALAPPDATA%\CircuitSketch\zig.
#
# Fica fora da pasta do projeto de proposito: sao ~400 MB e milhares de
# arquivos, que nao cabem no git nem devem ir para a sincronizacao do
# OneDrive. O CompiladorNativo procura primeiro em nativo\zig (o pacote do
# instalador) e depois aqui.
#
# A versao e a impressao digital sao as publicadas em ziglang.org.

$ErrorActionPreference = 'Stop'
$versao = '0.16.0'
$sha256 = '68659eb5f1e4eb1437a722f1dd889c5a322c9954607f5edcf337bc3684a75a7e'
$url = "https://ziglang.org/download/$versao/zig-x86_64-windows-$versao.zip"

$destino = Join-Path $env:LOCALAPPDATA 'CircuitSketch\zig'
if (Test-Path -LiteralPath (Join-Path $destino 'zig.exe')) {
    $tem = & (Join-Path $destino 'zig.exe') version
    if ($tem -eq $versao) { Write-Host "Zig $versao ja esta em $destino"; return }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "zig-$versao-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Force $temp | Out-Null
try {
    $zip = Join-Path $temp 'zig.zip'
    Write-Host "Baixando $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
    if ($hash -ne $sha256) { throw "Impressao digital errada: $hash (esperada $sha256)" }
    Expand-Archive -LiteralPath $zip -DestinationPath $temp
    if (Test-Path -LiteralPath $destino) { Remove-Item -Recurse -Force -LiteralPath $destino }
    New-Item -ItemType Directory -Force (Split-Path $destino) | Out-Null
    Move-Item -LiteralPath (Join-Path $temp "zig-x86_64-windows-$versao") -Destination $destino
    Write-Host "Zig $versao em $destino"
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $temp -ErrorAction SilentlyContinue
}
