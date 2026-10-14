# ============================================================
#  Tamanho do sketch, no formato que a Arduino IDE sabe ler.
#
#  A IDE nao inventa as linhas "Sketch uses ... bytes": ela roda o
#  recipe.size.pattern e passa a saida pelas recipe.size.regex do
#  platform.txt. Sem isso o build terminava sem informacao nenhuma
#  de memoria, diferente de qualquer placa real.
#
#  Os parametros vem do boards.txt, entao cada placa tem o seu piso
#  e o seu fator. A conta mora em medida.ps1, compartilhada com a
#  gravacao, para os dois passos nunca mostrarem tamanhos diferentes.
# ============================================================
param(
    [string]$Sketch,
    [double]$PisoFlash = 197000,
    [double]$PisoRam = 13400,
    [double]$FatorFlash = 6.4,
    [double]$FatorRam = 0.42,
    [int]$FlashMaxima = 1310720,
    [int]$RamMaxima = 327680
)

. (Join-Path $PSScriptRoot 'medida.ps1')

$m = Get-MedidaDoSketch -Caminho $Sketch -PisoFlash $PisoFlash -PisoRam $PisoRam `
        -FatorFlash $FatorFlash -FatorRam $FatorRam `
        -FlashMaxima $FlashMaxima -RamMaxima $RamMaxima

# Formato do "size -A", que e o que a receita espera.
"section              size"
".flash.text        {0}" -f $m.Programa
".dram0.data        {0}" -f $m.Dados
