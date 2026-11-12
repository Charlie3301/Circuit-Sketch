namespace Simulador.App;

/// <summary>
/// O PWM na conta de energia (Alpha 24).
///
/// Antes o pino em PWM entrava como uma tensao media: analogWrite(128) na
/// ESP32 era uma fonte de 1,65 V. Para um resistor da no mesmo, mas nada que
/// tem limiar funciona assim: o LED (que precisa de ~2 V) ficava apagado, o
/// gate do MOSFET de nivel logico nao passava do limiar e o motor parava. Na
/// placa o pino fica nos 3,3 V cheios metade do tempo e em 0 V na outra
/// metade, e a carga ve a MEDIA DO QUE ACONTECE em cada metade.
///
/// Entao a conta resolve o circuito em cada trecho do periodo -- os pinos com
/// ciclo maior que o comeco do trecho em HIGH, os outros em LOW (os
/// temporizadores comecam o periodo juntos) -- e faz a media pesada pela
/// largura do trecho. Com k ciclos diferentes sao k + 1 contas, e so quando
/// ha PWM ligado em alguma coisa: o circuito sem PWM continua numa conta so.
/// </summary>
public sealed partial class Energia
{
    /// <summary>Num trecho do periodo: o PWM de cada (placa, GPIO) esta em HIGH (true) ou em LOW.</summary>
    IReadOnlyDictionary<(string Placa, int Gpio), bool> pwmNoTrecho;

    /// <summary>A tensao do pino em PWM neste trecho (nula: o pino nao esta em PWM, ou nao ha trechos).</summary>
    double? TensaoDoPwm(string placa, int gpio, Maquina maq, double tensaoDaPlaca)
    {
        if (pwmNoTrecho is null || maq.Pwm[gpio] is <= 0 or >= 255) return null;
        return pwmNoTrecho.TryGetValue((placa, gpio), out bool alto) ? (alto ? tensaoDaPlaca : 0) : null;
    }

    /// <summary>Os pinos em PWM (0 &lt; ciclo &lt; 1) ligados em alguma outra peca, com o ciclo de cada um.</summary>
    static List<((string Placa, int Gpio) Pino, double Ciclo)> PinosEmPwm(Circuito circuito, Bancada bancada, Netlist rede)
    {
        var lista = new List<((string, int), double)>();
        if (bancada is null) return lista;
        foreach (var placa in circuito.Pecas)
        {
            if (PerfilPlaca.De(placa.Tipo) is not { } perfil || Catalogo.Obter(placa.Tipo) is not { } def) continue;
            var maq = bancada.De(placa.Id);
            if (maq is null || !maq.Energizada) continue;
            var vistos = new HashSet<int>();
            foreach (var pino in def.Pinos)
            {
                int gpio = perfil.Numero(pino.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos || maq.Modo[gpio] != 1 || maq.Pwm[gpio] is <= 0 or >= 255) continue;
                if (!rede.EstaLigado(placa.Id, pino.Nome) || !vistos.Add(gpio)) continue;   // o mesmo GPIO pode ter dois nomes ("16" e "TX")
                lista.Add(((placa.Id, gpio), maq.Pwm[gpio] / 255.0));
            }
        }
        return lista;
    }

    /// <summary>
    /// Os trechos do periodo: de um ciclo ao proximo, com os pinos que ainda
    /// estao em HIGH nele e a largura (o peso na media).
    /// </summary>
    static List<(Dictionary<(string, int), bool> Altos, double Peso)> TrechosDoPeriodo(
        List<((string Placa, int Gpio) Pino, double Ciclo)> pinos)
    {
        var cortes = pinos.Select(p => p.Ciclo).Append(0).Append(1).Distinct().OrderBy(c => c).ToList();
        var trechos = new List<(Dictionary<(string, int), bool>, double)>();
        for (int i = 0; i + 1 < cortes.Count; i++)
        {
            double inicio = cortes[i], peso = cortes[i + 1] - inicio;
            if (peso <= 1e-9) continue;
            trechos.Add((pinos.ToDictionary(p => p.Pino, p => p.Ciclo > inicio + 1e-12), peso));
        }
        return trechos;
    }

    /// <summary>
    /// A conta pela media dos trechos: a de maior peso fica com a estrutura
    /// (ramos, avisos, drivers ligados), e as tensoes e as correntes viram a
    /// media pesada. O nivel de cada peca e o pior que ela teve num trecho.
    /// </summary>
    static Energia Media(List<(Energia Conta, double Peso)> trechos)
    {
        var base_ = trechos.MaxBy(t => t.Peso).Conta;
        // As tensoes de cada trecho, antes da media: a parte alternada do multimetro (EnergiaMedida.cs).
        var tensoesDosTrechos = trechos.Select(t => (new Dictionary<string, double>(t.Conta.tensaoPorNo), t.Peso)).ToList();
        void MediaDe<T>(Func<Energia, Dictionary<T, double>> tabela)
        {
            var soma = new Dictionary<T, double>();
            foreach (var (conta, peso) in trechos)
                foreach (var (chave, valor) in tabela(conta))
                    soma[chave] = soma.GetValueOrDefault(chave) + valor * peso;
            var destino = tabela(base_);
            destino.Clear();
            foreach (var (chave, valor) in soma) destino[chave] = valor;
        }
        MediaDe(c => c.tensaoPorNo);
        base_.tensoesDosTrechos = tensoesDosTrechos;
        MediaDe(c => c.correntePorPeca);
        MediaDe(c => c.correntePorNo);
        MediaDe(c => c.correnteDoRamo);
        foreach (var (conta, _) in trechos)
            foreach (var (peca, nivel) in conta.nivelPorPeca)
                if (!base_.nivelPorPeca.TryGetValue(peca, out var antes) || nivel > antes) base_.nivelPorPeca[peca] = nivel;
        return base_;
    }
}
