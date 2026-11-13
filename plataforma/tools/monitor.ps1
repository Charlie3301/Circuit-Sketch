# ============================================================
#  Monitor serial do CircuitSketch (pluggable monitor).
#
#  Sem isto o monitor serial da Arduino IDE abria vazio: a placa e
#  fantasma, entao nao ha porta de verdade do outro lado. O aluno
#  mandava o sketch pela IDE e os Serial.println apareciam so dentro
#  do CircuitSketch -- longe de onde ele estava olhando.
#
#  Um "monitor" e um processo que le comandos em texto na entrada
#  padrao e responde JSON, uma mensagem por linha, na saida. No OPEN
#  a IDE passa um endereco TCP: alguem tem que abrir uma conexao para
#  la, e aquilo vira o cano de dados. Documentacao do protocolo:
#  https://arduino.github.io/arduino-cli/latest/pluggable-monitor-specification/
#
#  Quem faz o cano e o monitor-cano.ps1, num processo a parte. Este
#  aqui so fala o protocolo: ler stdin bloqueia, e com o bombeamento
#  no mesmo processo o CLOSE derrubava o monitor inteiro em silencio.
#
#  A fonte dos dados e um arquivo que o CircuitSketch vai escrevendo
#  (%APPDATA%\CircuitSketch\serial.txt). Arquivo, e nao pipe, porque
#  assim nenhum dos dois precisa do outro vivo: o monitor pode abrir
#  no meio da execucao, e da para conferir o que esta acontecendo
#  abrindo o arquivo no Bloco de Notas.
#
#  Os dois sentidos: o que a pessoa digita no monitor da IDE vai para
#  %APPDATA%\CircuitSketch\entrada.txt, e o CircuitSketch entrega ao
#  Serial.read do sketch que esta rodando.
# ============================================================

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$arquivo = Join-Path $env:APPDATA 'CircuitSketch\serial.txt'
$entrada = Join-Path $env:APPDATA 'CircuitSketch\entrada.txt'
$cano    = Join-Path $PSScriptRoot 'monitor-cano.ps1'
$filho   = $null

function Responder($objeto) {
    $json = $objeto | ConvertTo-Json -Compress -Depth 8
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function Fechar {
    if ($null -eq $script:filho) { return }
    try { if (-not $script:filho.HasExited) { $script:filho.Kill() } } catch { }
    $script:filho = $null
}

while ($true) {
    $linha = [Console]::In.ReadLine()
    if ($null -eq $linha) { break }

    $partes  = $linha.Trim() -split '\s+'
    $comando = $partes[0].ToUpperInvariant()

    switch ($comando) {
        'HELLO' {
            Responder @{ eventType = 'hello'; protocolVersion = 1; message = 'OK' }
        }

        'DESCRIBE' {
            # A velocidade e so enfeite: nao ha fio nenhum do outro
            # lado. Vai na lista porque a IDE mostra o seletor, e uma
            # lista vazia parece defeito.
            Responder @{
                eventType = 'describe'
                message   = 'OK'
                port_description = @{
                    protocol = 'circuitsketch'
                    configuration_parameters = @{
                        baudrate = @{
                            label    = 'Baudrate'
                            type     = 'enum'
                            values   = @('9600', '19200', '38400', '57600', '115200')
                            selected = '115200'
                        }
                    }
                }
            }
        }

        'CONFIGURE' {
            Responder @{
                eventType   = 'configure'
                message     = 'OK'
                param_name  = $(if ($partes.Count -gt 1) { $partes[1] } else { '' })
                param_value = $(if ($partes.Count -gt 2) { $partes[2] } else { '' })
            }
        }

        'OPEN' {
            try {
                Fechar   # abrir duas vezes seguidas nao deixa cano orfao

                # "OPEN <endereco:porta> <porta_da_placa>"
                #
                # -NoNewWindow para nao piscar console na cara do aluno.
                # Em troca, o filho herda ESTA saida padrao: se algum dia
                # alguem puser um Write-Host no monitor-cano.ps1, aquilo
                # cai no meio do JSON do protocolo e a IDE nao entende
                # mais nada. O cano so escreve no socket, e e por isso.
                $filho = Start-Process -PassThru -NoNewWindow -FilePath 'powershell' `
                    -ArgumentList @(
                        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-NonInteractive',
                        '-File', $cano, '-Endereco', $partes[1], '-Arquivo', $arquivo,
                        '-Entrada', $entrada)

                Responder @{ eventType = 'open'; message = 'OK' }
            }
            catch {
                Fechar
                Responder @{
                    eventType = 'open'; error = $true
                    message   = "nao consegui abrir o monitor: $($_.Exception.Message)"
                }
            }
        }

        'CLOSE' {
            Fechar
            Responder @{ eventType = 'close'; message = 'OK' }
        }

        'QUIT' {
            Fechar
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

Fechar
