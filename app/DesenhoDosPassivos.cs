namespace Simulador.App;

// GERADO por ferramentas/extrair-svg/pecas-passivos.py -- nao edite a mao.
//
// Onde ficam as faixas do resistor (px da caixa): o editor pinta cada uma
// com a cor do valor, por cima do corpo desenhado no resistor.svg.
static class DesenhoDosPassivos
{
    /// <summary>O meio de cada faixa (x), a largura e a altura, com o centro em y.</summary>
    public static readonly (double X, double Largura, double Altura)[] FaixasDoResistor =
    [
        (21.09, 1.89, 8.84),
        (24.11, 1.89, 8.24),
        (27.13, 1.89, 7.80),
        (37.71, 1.89, 8.84),
    ];

    public const double CentroDasFaixas = 5.65;
}
