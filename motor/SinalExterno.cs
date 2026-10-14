namespace Simulador;

/// <summary>
/// Um sinal que vem de FORA da placa e se repete sozinho, com o tempo de
/// verdade: o pulso de cada canal de um receptor de radio (1 a 2 ms a cada
/// 20 ms), o trem do PPM (oito canais num fio so).
///
/// O nivel sai do relogio do chip, e nao do quadro da tela: o digitalRead
/// le o pino no instante em que o sketch pergunta, o pulseIn espera a borda
/// e mede a largura, e a interrupcao dispara em cada borda -- inclusive no
/// meio de um delay, que e onde o sketch de receptor passa quase o tempo
/// todo. Na tela (25 quadros por segundo) um pulso de 1,5 ms nem existiria.
/// </summary>
public sealed class SinalExterno
{
    /// <summary>A volta inteira, em us (20.000 no PWM do receptor, 22.500 no PPM).</summary>
    public double PeriodoUs { get; init; } = 20000;

    /// <summary>Onde comeca a volta 0, no relogio do chip (us).</summary>
    public double FaseUs { get; init; }

    /// <summary>Os pulsos em HIGH dentro da volta: inicio e largura (us), em ordem.</summary>
    public (double Inicio, double Largura)[] Pulsos { get; init; } = [];

    double NaVolta(double us) => ((us - FaseUs) % PeriodoUs + PeriodoUs) % PeriodoUs;

    public int Nivel(double us)
    {
        double t = NaVolta(us);
        foreach (var (inicio, largura) in Pulsos)
            if (t >= inicio && t < inicio + largura) return 1;
        return 0;
    }

    /// <summary>O proximo instante, depois de <paramref name="us"/>, em que o nivel muda.</summary>
    public double ProximaBorda(double us)
    {
        if (Pulsos.Length == 0 || PeriodoUs <= 0) return double.PositiveInfinity;
        double volta = us - NaVolta(us);
        for (int k = 0; k < 3; k++, volta += PeriodoUs)
            foreach (var (inicio, largura) in Pulsos)
                foreach (double borda in new[] { volta + inicio, volta + inicio + largura })
                    if (borda > us + 1e-6) return borda;
        return double.PositiveInfinity;
    }

    /// <summary>
    /// O proximo pulso no nivel pedido que COMECA depois de <paramref name="us"/>:
    /// e o que o pulseIn mede (se o pino ja esta no meio de um, ele espera o
    /// proximo, como o do Arduino).
    /// </summary>
    public (double Inicio, double Fim)? ProximoPulso(double us, int nivel)
    {
        if (Pulsos.Length == 0 || PeriodoUs <= 0) return null;
        double volta = us - NaVolta(us);
        for (int k = 0; k < 3; k++, volta += PeriodoUs)
        {
            for (int i = 0; i < Pulsos.Length; i++)
            {
                var (inicio, largura) = Pulsos[i];
                double ini, fim;
                if (nivel == 1) { ini = volta + inicio; fim = ini + largura; }
                else
                {
                    // O LOW vai do fim deste pulso ao comeco do proximo.
                    ini = volta + inicio + largura;
                    fim = i + 1 < Pulsos.Length ? volta + Pulsos[i + 1].Inicio : volta + PeriodoUs + Pulsos[0].Inicio;
                }
                if (ini > us + 1e-6 && fim > ini) return (ini, fim);
            }
        }
        return null;
    }
}
