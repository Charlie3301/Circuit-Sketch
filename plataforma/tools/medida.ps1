# ============================================================
#  De onde saem os numeros de memoria.
#
#  Fica num arquivo so porque DOIS passos os usam: o recipe.size,
#  que imprime "Sketch uses ... bytes", e a gravacao, que imprime
#  quanto foi escrito. Se cada um tivesse a sua conta, um dia iam
#  divergir e o console mostraria dois tamanhos para o mesmo sketch.
#
#  Aqui nao ha compilacao de verdade, entao o tamanho sai do FONTE:
#  um piso (o que um sketch vazio ja ocupa naquela placa) mais um
#  fator por byte de codigo. Os dois vem do boards.txt, porque
#  variam muito: um sketch vazio ocupa ~197 KB numa ESP32 e ~444
#  bytes num Uno. Sao estimativas, e por isso o cabecalho da
#  gravacao diz que o alvo e simulado.
# ============================================================

function Get-BytesDoFonte {
    param([string]$Caminho)

    if (-not $Caminho) { return 0 }

    if (Test-Path -LiteralPath $Caminho -PathType Container) {
        $itens = @(Get-ChildItem -LiteralPath $Caminho -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.Extension -in '.ino', '.cpp', '.c', '.h' })
        if ($itens.Count -eq 0) { return 0 }
        return [int]($itens | Measure-Object -Property Length -Sum).Sum
    }

    if (Test-Path -LiteralPath $Caminho -PathType Leaf) {
        return [int](Get-Item -LiteralPath $Caminho).Length
    }
    return 0
}

function Get-MedidaDoSketch {
    param(
        [string]$Caminho,
        [double]$PisoFlash = 197000,
        [double]$PisoRam = 13400,
        [double]$FatorFlash = 6.4,
        [double]$FatorRam = 0.42,
        [int]$FlashMaxima = 1310720,
        [int]$RamMaxima = 327680
    )

    $fonte = Get-BytesDoFonte -Caminho $Caminho

    $programa = [int]($PisoFlash + $fonte * $FatorFlash)
    $dados = [int]($PisoRam + $fonte * $FatorRam)

    # Passar do limite nao teria sentido: a IDE mostraria mais de 100%.
    # Deixa um fio de folga para o numero nao encostar no teto exato.
    $tetoFlash = [int]($FlashMaxima * 0.98)
    $tetoRam = [int]($RamMaxima * 0.98)
    if ($programa -gt $tetoFlash) { $programa = $tetoFlash }
    if ($dados -gt $tetoRam) { $dados = $tetoRam }

    return [pscustomobject]@{
        Fonte    = $fonte
        Programa = $programa
        Dados    = $dados
    }
}
