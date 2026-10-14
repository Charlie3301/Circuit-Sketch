# ============================================================
#  Porta virtual do CircuitSketch (pluggable discovery).
#
#  Sem isto o botao Upload nunca funciona: a Arduino IDE exige uma
#  porta, e com a placa fantasma nao ha nenhuma. O erro que aparecia
#  era "port not found", e o unico caminho que sobrava era o botao
#  Verificar.
#
#  Uma "discovery" e um processo que fala um protocolo simples: le
#  comandos em texto na entrada padrao e responde JSON, uma mensagem
#  por linha, na saida. A IDE mantem o processo vivo enquanto estiver
#  aberta. Documentacao do protocolo:
#  https://arduino.github.io/arduino-cli/latest/pluggable-discovery-specification/
#
#  Aqui ele anuncia UMA porta, que nao existe em lugar nenhum -- e
#  exatamente o ponto: da a IDE algo para selecionar, e o upload
#  passa a chegar no simulador.
#
#  ---- o diario ----
#
#  Em algumas partidas a IDE simplesmente nao enxerga a porta. Pelo
#  arduino-cli ela aparece sempre; na IDE, as vezes sim, as vezes nao,
#  com o MESMO codigo dos dois lados. O diario aqui embaixo mostra o
#  que acontece deste lado: o processo sobe, recebe HELLO, recebe
#  START_SYNC e responde com o "add" -- tudo certo. Do outro lado,
#  nada. A causa ainda nao foi encontrada.
#
#  JA TENTEI trocar isto por um executavel nativo (fonte em
#  plataforma/fonte/porta), que sobe em 150 ms contra 1700 ms daqui e
#  responde o protocolo certo. Nao adiantou: o daemon registrava
#  "Received message type: add" e a porta nao aparecia em "board
#  list". A causa nao foi encontrada, entao vale o que funciona.
# ============================================================

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# ---- diario ----
#
# O log da IDE so diz "exit status 128" e "calling HELLO: timeout", o
# que nao explica nada. Aqui fica o outro lado da conversa: se o
# processo subiu, o que ele recebeu e como ele morreu. Serve para o
# laboratorio, quando a porta nao aparecer no computador de alguem.
$diario = Join-Path $env:TEMP 'circuitsketch-porta.log'

function Anotar($texto) {
    try {
        $carimbo = (Get-Date).ToString('HH:mm:ss.fff')
        Add-Content -Path $diario -Value "$carimbo [$PID] $texto" -Encoding utf8
    } catch { }
}

Anotar "subiu | politica=$(Get-ExecutionPolicy) | pasta=$PWD"

$porta = @{
    address       = 'circuitsketch'
    label         = 'CircuitSketch (simulador)'
    protocol      = 'circuitsketch'
    protocolLabel = 'Simulador'
    hardwareId    = ''
    properties    = @{ vid = '0x0000'; pid = '0x0000' }
}

function Responder($objeto) {
    $json = $objeto | ConvertTo-Json -Compress -Depth 8
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function Anunciar {
    Responder @{ eventType = 'add'; port = $porta }
}

# TENTEI reanunciar a porta de tempos em tempos, com [Console]::In.ReadLineAsync() e
# Wait(500) para nao bloquear. Nao funciona: em PowerShell o
# Console.In e um leitor sincronizado, e a chamada "async" bloqueia
# do mesmo jeito -- medido, a resposta ao START_SYNC so saiu quando o
# comando SEGUINTE chegou. Leitura bloqueante de volta.

while ($true) {
    $linha = [Console]::In.ReadLine()
    if ($null -eq $linha) { Anotar "entrada fechou (EOF)"; break }

    $comando = ($linha.Trim() -split '\s+')[0].ToUpperInvariant()
    Anotar "recebeu: $linha"

    switch ($comando) {
        'HELLO' {
            Responder @{ eventType = 'hello'; protocolVersion = 1; message = 'OK' }
        }
        'START' {
            Responder @{ eventType = 'start'; message = 'OK' }
        }
        'LIST' {
            Responder @{ eventType = 'list'; ports = @($porta) }
        }
        'START_SYNC' {
            Responder @{ eventType = 'start_sync'; message = 'OK' }
            Anunciar
        }
        'STOP' {
            Responder @{ eventType = 'stop'; message = 'OK' }
        }
        'QUIT' {
            Responder @{ eventType = 'quit'; message = 'OK' }
            break
        }
        default {
            Responder @{
                eventType = 'command_error'
                error     = $true
                message   = "comando desconhecido: $comando"
            }
        }
    }
}

Anotar "saiu do laco"
