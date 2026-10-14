# ============================================================
#  O cano de dados do monitor serial.
#
#  Processo separado de proposito. O monitor.ps1 precisa continuar
#  respondendo CLOSE e QUIT enquanto os dados correm, e ler stdin
#  bloqueia -- a primeira versao fazia o bombeamento numa thread
#  dentro do mesmo processo e o CLOSE derrubava tudo em silencio.
#
#  Com dois processos, cada um faz uma coisa so: o pai fala o
#  protocolo, este aqui despeja o arquivo no socket, e fechar e
#  matar. De quebra da para rodar este sozinho para ver se o
#  problema esta no cano ou no protocolo.
# ============================================================

param(
    [Parameter(Mandatory = $true)][string]$Endereco,   # 127.0.0.1:1234
    [Parameter(Mandatory = $true)][string]$Arquivo
)

$ErrorActionPreference = 'Stop'

# ---- diario ----
# A IDE conecta o monitor e mostra a janela vazia. O log dela nao diz
# nada depois do OPEN, entao o que falta saber e deste lado: quantos
# bytes sairam daqui.
$diario = Join-Path $env:TEMP 'circuitsketch-cano.log'
function Anotar($t) {
    try {
        Add-Content -Path $diario -Encoding utf8 `
            -Value "$((Get-Date).ToString('HH:mm:ss.fff')) [$PID] $t"
    } catch { }
}

Anotar "subiu | endereco=$Endereco | arquivo=$Arquivo"

$partes  = $Endereco -split ':'
$cliente = New-Object System.Net.Sockets.TcpClient
$cliente.Connect($partes[0], [int]$partes[1])
Anotar "conectou"

$escritor = New-Object System.IO.StreamWriter($cliente.GetStream())
$escritor.AutoFlush = $true

# Comeca do inicio de proposito: quem abre o monitor no meio da
# execucao quer ver o que ja saiu, e nao uma tela em branco.
$posicao = 0

try {
    while ($cliente.Connected) {
        Start-Sleep -Milliseconds 120

        if (-not (Test-Path -LiteralPath $Arquivo)) { continue }
        $tamanho = (Get-Item -LiteralPath $Arquivo).Length

        # Arquivo encolheu: o simulador reiniciou a execucao. Volta ao
        # comeco em vez de ficar esperando passar do fim.
        if ($tamanho -lt $posicao) {
            $posicao = 0
            $escritor.Write("`r`n--- placa reiniciada ---`r`n")
        }
        if ($tamanho -eq $posicao) { continue }

        # ReadWrite no compartilhamento: o CircuitSketch esta escrevendo
        # neste arquivo agora, e abrir exclusivo daria erro a cada volta.
        $leitura = [System.IO.File]::Open($Arquivo, 'Open', 'Read', 'ReadWrite')
        try {
            $leitura.Seek($posicao, 'Begin') | Out-Null
            $buffer = New-Object byte[] ($tamanho - $posicao)
            $lidos  = $leitura.Read($buffer, 0, $buffer.Length)
        }
        finally { $leitura.Dispose() }

        if ($lidos -le 0) { continue }

        $posicao += $lidos
        $texto = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $lidos)

        # Terminal quer CRLF; o arquivo pode vir com qualquer um dos dois.
        $saida = $texto.Replace("`r`n", "`n").Replace("`n", "`r`n")
        $escritor.Write($saida)
        Anotar "mandou $($saida.Length) caracteres"
    }
}
catch { Anotar "ESTOUROU: $_" }
finally {
    Anotar "saindo"
    try { $escritor.Dispose() } catch { }
    try { $cliente.Close() }   catch { }
}
