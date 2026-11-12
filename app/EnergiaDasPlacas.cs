namespace Simulador.App;

/// <summary>
/// A placa sem o cabo USB (Alpha 24, "bateria funcional"): com uma bateria
/// no circuito, a placa so liga se a bateria -- direto, ou por um regulador
/// (o 5 V do L298N, o BEC do ESC) -- chegar num pino de alimentacao dela,
/// dentro da faixa daquele pino. Ligada assim, ela puxa a corrente dela da
/// bateria, e o regulador dela alimenta o 3V3 (e o 5 V do Uno) para os
/// sensores. Abaixo da faixa, nao liga; acima, queima.
///
/// As faixas sao as das fichas: o VIN do Uno pede 7 a 12 V (o regulador
/// precisa de folga sobre os 5 V; 20 V e o maximo absoluto), o VIN da ESP32
/// DevKit alimenta o AMS1117 (4,3 V para dar 3,3 V; 15 V e o maximo dele), o
/// 3V3 aceita 3,0 a 3,6 V (a partir de ~4 V a ESP32 queima), o 5V do Uno e o
/// trilho direto (4,5 a 5,5 V), o VSYS da Pico aceita 1,8 a 5,5 V.
/// </summary>
public sealed partial class Energia
{
    /// <summary>Como uma placa sem o cabo USB esta sendo alimentada.</summary>
    /// <param name="Pino">O pino por onde a energia chega (nulo: nenhum).</param>
    /// <param name="Motivo">Sem energia ou queimada: o porque, em uma frase.</param>
    /// <param name="Direto">O pino vai direto no chip (o 3V3, o 5V do Uno), sem regulador no caminho.</param>
    public sealed record AlimentacaoDaPlaca(bool Ligada, string Pino, double Volts, string Motivo, bool Queimou = false, bool Direto = false);

    readonly Dictionary<string, AlimentacaoDaPlaca> alimentacao = [];

    /// <summary>As placas sem o cabo USB nesta conta (as outras ficam com os trilhos ideais, como sempre).</summary>
    IReadOnlySet<string> placasSemCabo;

    /// <summary>A alimentacao de uma placa sem cabo (nulo: ela esta na USB, ou nao e placa).</summary>
    public AlimentacaoDaPlaca AlimentacaoDe(string idDaPlaca) => alimentacao.GetValueOrDefault(idDaPlaca);

    bool SemCabo(string idDaPeca) => placasSemCabo is not null && placasSemCabo.Contains(idDaPeca);

    /// <summary>
    /// Uma entrada de alimentacao: a faixa que liga, o que queima, os trilhos
    /// que o regulador dela poe de pe, e se ela vai direto no chip.
    /// </summary>
    sealed record Entrada(string Pino, double Minimo, double Maximo, double Queima, string[] Regulados, bool Direto = false);

    /// <summary>As entradas de alimentacao desta placa, da mais alta (que passa por regulador) para a mais baixa.</summary>
    static List<Entrada> EntradasDe(PerfilPlaca perfil, PecaDef def, string tipo)
    {
        var entradas = new List<Entrada>();
        bool Tem(string n) => def.Pinos.Any(p => p.Nome == n);
        // (o VBAT da ESP32-H2 DevKit e o mesmo trilho de 3,3 V, para uma bateria)
        string[] TresVolts() => [.. def.Pinos.Select(p => p.Nome).Where(n => n is "3V3" or "3.3V" or "VBAT" || n.StartsWith("3V3.", StringComparison.Ordinal))];
        bool cincoVolts = perfil.TensaoDeOperacao >= 4.5;
        string proc = perfil.Processador ?? "";
        bool avr = proc.StartsWith("ATmega", StringComparison.Ordinal);

        if (Tem("VIN"))
        {
            if (avr || tipo == "franzininho")
                entradas.Add(new("VIN", 6.5, 12, 20, [.. TresVolts(), .. new[] { "5V", "VCC.1", "VCC.2" }.Where(Tem)]));
            else if (tipo.StartsWith("st-nucleo", StringComparison.Ordinal))
                entradas.Add(new("VIN", 7.0, 12, 15, [.. TresVolts(), .. new[] { "5V" }.Where(Tem)]));
            else
                entradas.Add(new("VIN", 4.3, 12, 15, TresVolts()));
        }
        // O 5V de uma placa de 5 V e o trilho do chip; o de uma de 3,3 V entra no regulador.
        foreach (string cinco in new[] { "5V", "VBUS", "VUSB" }.Where(Tem))
            entradas.Add(cincoVolts
                ? new(cinco, 4.5, 5.5, 6.0, TresVolts(), Direto: true)
                : new(cinco, 4.3, 5.5, 6.5, TresVolts()));
        if (Tem("VSYS")) entradas.Add(new("VSYS", 1.8, 5.5, 6.0, TresVolts()));
        if (!cincoVolts)
            foreach (string tres in TresVolts())
                entradas.Add(new(tres, 3.0, 3.6, 4.0, [], Direto: true));
        foreach (string vcc in def.Pinos.Select(p => p.Nome).Where(n => n == "VCC" || n.StartsWith("VCC.", StringComparison.Ordinal)))
            entradas.Add(cincoVolts ? new(vcc, 2.7, 5.5, 6.0, [], Direto: true) : new(vcc, 3.0, 3.6, 4.0, [], Direto: true));
        return entradas;
    }

    /// <summary>O que a placa ligada puxa da alimentacao, em amperes (o chip, os reguladores, o LED de energia).</summary>
    static double ConsumoDe(PerfilPlaca perfil) => (perfil.Processador ?? "") switch
    {
        "Xtensa LX6" or "Xtensa LX7" or "RISC-V" or "ESP8266" => 0.08,
        "RP2040" => 0.03,
        var p when p.StartsWith("ATmega", StringComparison.Ordinal) => 0.05,
        var p when p.StartsWith("ATtiny", StringComparison.Ordinal) => 0.006,
        _ => 0.03,
    };

    string TerraDe(PecaDef def) => def.Pinos.FirstOrDefault(p => Netlist.EhPinoDeTerra(p.Nome))?.Nome;

    /// <summary>
    /// A placa na USB com uma tensao de fora (a bateria no 3V3, ou no 5V do
    /// Uno) acima do que o pino de alimentacao aguenta: o porque (nulo se nao).
    /// O trilho da USB so fornece; quem empurra o pino para cima e a bateria.
    /// </summary>
    public AlimentacaoDaPlaca SobretensaoNaUsb(PecaNoCircuito placa)
    {
        if (SemCabo(placa.Id) || PerfilPlaca.De(placa.Tipo) is not { } perfil || Catalogo.Obter(placa.Tipo) is not { } def) return null;
        if (bancada?.De(placa.Id) is not { Energizada: true }) return null;
        string terra = TerraDe(def);
        if (terra is null) return null;
        foreach (var entrada in EntradasDe(perfil, def, placa.Tipo))
        {
            double volts = TensaoEntre(placa.Id, entrada.Pino, terra);
            if (volts > entrada.Queima)
                return new(false, entrada.Pino, volts,
                    Idioma.T("recebeu {0:0.#} V no pino {1}, que aguenta até {2:0.#} V", volts, entrada.Pino, entrada.Maximo),
                    Queimou: true, Direto: entrada.Direto);
        }
        return null;
    }

    /// <summary>
    /// A primeira passada: uma carga leve (10 kohm) em cada entrada das placas
    /// sem cabo. Sem ela, uma bateria presa so no VIN (sem o GND) ficaria
    /// solta no meio da conta e a placa "veria" metade da tensao dela.
    /// </summary>
    void CargasDeMedida()
    {
        foreach (var placa in circuitoDaConta.Pecas)
        {
            if (!SemCabo(placa.Id) || PerfilPlaca.De(placa.Tipo) is not { } perfil || Catalogo.Obter(placa.Tipo) is not { } def) continue;
            string terra = TerraDe(def);
            if (terra is null) continue;
            int gnd = No(rede, placa.Id, terra);
            foreach (var entrada in EntradasDe(perfil, def, placa.Tipo))
            {
                int no = No(rede, placa.Id, entrada.Pino);
                if (no >= 0 && gnd >= 0 && no != gnd)
                    ramos.Add(new Ramo(placa.Id, no, gnd, 10_000, 0, 0, 0.01, Rotulo: "medida"));
            }
        }
    }

    /// <summary>
    /// Depois dos reguladores dos drivers: cada placa sem cabo olha as
    /// entradas dela. A que estiver na faixa liga a placa -- que entao puxa a
    /// corrente dela por ali, e poe de pe os trilhos do regulador.
    ///
    /// Em passadas: a placa ligada pode alimentar outra (o 3V3 de uma no 3V3
    /// da outra), e essa so ve a tensao depois da conta refeita com o
    /// regulador da primeira -- qualquer que seja a ordem delas no circuito.
    /// </summary>
    void LigarPlacas()
    {
        for (int passada = 0; passada < 4; passada++)
        {
            int ligadas = alimentacao.Count(a => a.Value.Ligada);
            LigarAsApagadas();
            bool ligouAlguma = alimentacao.Count(a => a.Value.Ligada) > ligadas;
            bool sobrouApagada = alimentacao.Any(a => a.Value is { Ligada: false, Queimou: false });
            if (!ligouAlguma || !sobrouApagada) return;
            Resolver();
        }
    }

    void LigarAsApagadas()
    {
        foreach (var placa in circuitoDaConta.Pecas)
        {
            if (!SemCabo(placa.Id) || PerfilPlaca.De(placa.Tipo) is not { } perfil || Catalogo.Obter(placa.Tipo) is not { } def) continue;
            // Ja decidida numa passada anterior: ligada (os ramos dela ja estao na conta) ou queimada.
            if (alimentacao.TryGetValue(placa.Id, out var antes) && (antes.Ligada || antes.Queimou)) continue;
            string terra = TerraDe(def);
            var entradas = EntradasDe(perfil, def, placa.Tipo);
            if (terra is null || entradas.Count == 0)
            {
                alimentacao[placa.Id] = new(true, null, perfil.TensaoDeOperacao, null);
                continue;
            }

            var medidas = entradas.Select(e => (Entrada: e, Volts: TensaoEntre(placa.Id, e.Pino, terra))).ToList();

            // Acima do que aguenta: queimou (o regulador, ou o chip direto).
            var demais = medidas.FirstOrDefault(m => m.Volts > m.Entrada.Queima);
            if (demais.Entrada is not null)
            {
                alimentacao[placa.Id] = new(false, demais.Entrada.Pino, demais.Volts,
                    Idioma.T("recebeu {0:0.#} V no pino {1}, que aguenta até {2:0.#} V", demais.Volts, demais.Entrada.Pino, demais.Entrada.Maximo),
                    Queimou: true, Direto: demais.Entrada.Direto);
                continue;
            }

            var boa = medidas.FirstOrDefault(m => m.Volts >= m.Entrada.Minimo);
            if (boa.Entrada is null)
            {
                var fraca = medidas.Where(m => m.Volts > 0.5).OrderByDescending(m => m.Volts).FirstOrDefault();
                var pinos = entradas.Select(e => e.Pino).Distinct().Take(3).ToList();
                alimentacao[placa.Id] = new(false, fraca.Entrada?.Pino, fraca.Volts, fraca.Entrada is not null
                    ? Idioma.T("{0:0.##} V no pino {1} não bastam: ela precisa de {2:0.#} V ali para ligar", fraca.Volts, fraca.Entrada.Pino, fraca.Entrada.Minimo)
                    : Idioma.T("sem o cabo USB (há bateria no circuito), e nenhuma bateria chega no {0} dela (com o − no GND)", (pinos.Count > 1 ? string.Join(", ", pinos.SkipLast(1)) + " " + Idioma.T("ou") + " " + pinos[^1] : pinos[0])));
                continue;
            }

            alimentacao[placa.Id] = new(true, boa.Entrada.Pino, boa.Volts,
                boa.Volts > boa.Entrada.Maximo ? Idioma.T("acima dos {0:0.#} V do pino, o regulador esquenta", boa.Entrada.Maximo) : null);

            // Ligada so com a simulacao rodando (como a da USB): parada, ou
            // antes do Rodar a ligar, ela nao puxa nada e o regulador fica apagado.
            if (bancada?.De(placa.Id) is not { Energizada: true }) continue;

            // A placa puxa a corrente dela pela entrada que a liga...
            int gnd = No(rede, placa.Id, terra);
            int entradaNo = No(rede, placa.Id, boa.Entrada.Pino);
            double consumo = ConsumoDe(perfil);
            if (entradaNo >= 0 && gnd >= 0)
                ramos.Add(new Ramo(placa.Id, entradaNo, gnd, Math.Max(boa.Volts, 1) / consumo, 0, consumo, 1.0, Rotulo: "placa"));

            // ...e o regulador dela poe os trilhos de pe (o 3V3 da ESP32 no VIN, o 5V e o 3.3V do Uno).
            foreach (string trilho in boa.Entrada.Regulados.Distinct())
            {
                int no = No(rede, placa.Id, trilho);
                if (no < 0 || gnd < 0 || no == entradaNo) continue;
                double volts = trilho.StartsWith("5V", StringComparison.Ordinal) || trilho.StartsWith("VCC", StringComparison.Ordinal) ? 5.0 : 3.3;
                if (volts >= boa.Volts) continue;   // um regulador nao sobe a tensao
                ramos.Add(new Ramo(placa.Id, no, gnd, 0.2, 0, 0, 0.8, Fem: volts, Rotulo: "regulador"));
            }
        }
    }
}
