# ============================================================
#  A cara da gravacao, uma por familia de placa.
#
#  Cada toolchain tem a sua saida, e e essa que o aluno reconhece:
#  as AVR mostram avrdude, com a barra de "#"; as de 32 bits mostram
#  esptool, com as linhas de "Writing at 0x... (n %)". Mostrar a
#  errada seria tao estranho quanto nao mostrar nada.
#
#  O que NENHUMA das duas faz e fingir sondagem de hardware: nao ha
#  chip na porta, entao o cabecalho diz que o alvo e simulado em vez
#  de inventar revisao de silicio e MAC.
# ============================================================

function Show-GravacaoEsptool {
    param([int]$Programa, [string]$Sketch, [string]$Placa)

    $comprimido = [int]($Programa * 0.56)
    $inicio = 0x10000

    Write-Host "CircuitSketch - alvo simulado (nenhuma placa e usada)"
    Write-Host ("Placa: {0}" -f $Placa)
    Write-Host ("Sketch: {0}" -f $Sketch)
    Write-Host ""
    Write-Host "Configuring flash size..."
    Write-Host ("Flash will be erased from 0x{0:x8} to 0x{1:x8}..." -f $inicio, ($inicio + $Programa))
    Write-Host ("Compressed {0} bytes to {1}..." -f $Programa, $comprimido)

    # Doze passos: o bastante para ver a barra andar sem fazer o aluno
    # esperar. O sono e curto de proposito -- os PCs do laboratorio sao
    # lentos e ninguem quer atraso artificial num ciclo de teste.
    $relogio = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 1; $i -le 12; $i++) {
        Write-Host ("Writing at 0x{0:x8}... ({1} %)" -f ($inicio + [int]($Programa * $i / 12)),
                                                        [int](100 * $i / 12))
        Start-Sleep -Milliseconds 55
    }
    $relogio.Stop()

    $segundos = [Math]::Max($relogio.Elapsed.TotalSeconds, 0.1)
    # Ponto, nao virgula: numa maquina pt-BR o "{0:N1}" sairia "0,8" e a
    # linha deixaria de ser igual a de um upload de verdade.
    $tempo = $segundos.ToString("0.0", [System.Globalization.CultureInfo]::InvariantCulture)
    Write-Host ("Wrote {0} bytes ({1} compressed) at 0x{2:x8} in {3} seconds (effective {4} kbit/s)..." `
                -f $Programa, $comprimido, $inicio, $tempo,
                   [int](($comprimido * 8) / $segundos / 1000))
    Write-Host "Hash of data verified."
    Write-Host ""
    Write-Host "Leaving..."
}

function Show-BarraAvrdude {
    param([string]$Rotulo, [double]$Segundos)

    # A barra do avrdude e uma linha so, atualizada com retorno de
    # carro. Reproduzir isso mantem a saida igual a de uma placa real.
    $largura = 50
    $tempo = $Segundos.ToString("0.00", [System.Globalization.CultureInfo]::InvariantCulture)
    for ($i = 0; $i -le $largura; $i += 5) {
        $cheio = '#' * $i
        $vazio = ' ' * ($largura - $i)
        [Console]::Out.Write(("`r{0} | {1}{2} | {3}% {4}s" -f
            $Rotulo, $cheio, $vazio, [int](100 * $i / $largura), $tempo))
        Start-Sleep -Milliseconds 25
    }
    [Console]::Out.WriteLine()
}

function Show-GravacaoAvrdude {
    param([int]$Programa, [string]$Sketch, [string]$Placa, [string]$Assinatura)

    Write-Host "CircuitSketch - alvo simulado (nenhuma placa e usada)"
    Write-Host ("Placa: {0}" -f $Placa)
    Write-Host ("Sketch: {0}" -f $Sketch)
    Write-Host ""
    Write-Host "avrdude: AVR device initialized and ready to accept instructions"
    Show-BarraAvrdude -Rotulo 'Reading' -Segundos 0.00
    if ($Assinatura) { Write-Host ("avrdude: Device signature = {0}" -f $Assinatura) }
    Write-Host "avrdude: erasing chip"
    Write-Host ("avrdude: writing flash ({0} bytes):" -f $Programa)
    Show-BarraAvrdude -Rotulo 'Writing' -Segundos 0.16
    Write-Host ("avrdude: {0} bytes of flash written" -f $Programa)
    Write-Host "avrdude: verifying flash memory against the file"
    Show-BarraAvrdude -Rotulo 'Reading' -Segundos 0.13
    Write-Host ("avrdude: {0} bytes of flash verified" -f $Programa)
    Write-Host ""
    Write-Host "avrdude done.  Thank you."
}
