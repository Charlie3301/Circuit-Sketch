namespace Simulador.App;

/// <summary>
/// Os reguladores de tensao no editor (Alpha 24): o calor, a protecao
/// termica, os avisos e o rotulo em cima da peca. A eletrica (a saida, a
/// queda, o limite de corrente, o que entra) e da Energia.
///
/// O calor e o que ensina: o 7805 de 12 V para 5 V com meio ampere esquenta
/// 3,5 W, e sem dissipador o TO-220 (~50 graus por watt) passa dos 150 graus
/// em menos de um minuto -- a protecao corta a saida, ele esfria e volta,
/// e a placa reinicia sem parar. E o problema classico de quem alimenta
/// servo e motor pelo 7805.
/// </summary>
public partial class EditorCircuito
{
    /// <summary>A temperatura do chip de cada regulador, em graus (25: a do ambiente).</summary>
    readonly Dictionary<string, double> temperaturaDoRegulador = [];

    /// <summary>Os reguladores com a protecao termica cortando a saida (ate esfriar 20 graus).</summary>
    readonly HashSet<string> emProtecaoTermica = [];

    /// <summary>O que cada um esquentava quando a protecao cortou (para o aviso dizer por que).</summary>
    readonly Dictionary<string, Energia.EstadoDoRegulador> antesDaProtecao = [];

    const double Ambiente = 25, Desliga = 150, Religa = 130;

    /// <summary>Quanto tempo o chip leva para chegar perto do calor de regime (a constante termica).</summary>
    const double ConstanteTermica = 40;

    /// <summary>Para o teste e para a barra: a temperatura de agora.</summary>
    public double TemperaturaDoRegulador(string id) => temperaturaDoRegulador.GetValueOrDefault(id, Ambiente);

    /// <summary>Para o teste: a protecao termica esta cortando a saida?</summary>
    internal bool EmProtecaoTermicaParaTeste(string id) => emProtecaoTermica.Contains(id);

    /// <summary>Os reguladores sem saida agora: a protecao termica, ou queimados.</summary>
    HashSet<string> ReguladoresDesligados()
    {
        var fora = new HashSet<string>(emProtecaoTermica);
        foreach (var p in Atual.Pecas)
            if (Reguladores.EhRegulador(p.Tipo) && EstaQueimada(p.Id)) fora.Add(p.Id);
        return fora;
    }

    /// <summary>O calor do quadro: cada regulador anda para a temperatura de regime da potencia de agora.</summary>
    void AquecerReguladores(Energia conta, double segundos)
    {
        if (conta is null) return;
        foreach (var peca in Atual.Pecas)
        {
            if (!Reguladores.EhRegulador(peca.Tipo)) continue;
            var estado = conta.RegulagemDe(peca.Id);
            double potencia = estado?.Potencia ?? 0;
            double regime = Ambiente + potencia * Reguladores.Da(peca).ResistenciaTermica;
            double t = TemperaturaDoRegulador(peca.Id);
            t += (regime - t) * (1 - Math.Exp(-Math.Max(0, segundos) / ConstanteTermica));
            temperaturaDoRegulador[peca.Id] = t;

            if (t >= Desliga && emProtecaoTermica.Add(peca.Id) && estado is not null) antesDaProtecao[peca.Id] = estado;
            else if (t <= Religa) emProtecaoTermica.Remove(peca.Id);
        }
    }

    /// <summary>Esquece o calor (Rodar de novo: as pecas voltam frias, como as queimadas voltam inteiras).</summary>
    void EsfriarReguladores()
    {
        temperaturaDoRegulador.Clear();
        emProtecaoTermica.Clear();
        antesDaProtecao.Clear();
    }

    /// <summary>Em cima de cada regulador ligado: o que sai, a corrente e, quente, a temperatura.</summary>
    void MostrarRotulosDosReguladores()
    {
        foreach (var peca in Atual.Pecas)
        {
            if (!Reguladores.EhRegulador(peca.Tipo)) continue;
            MostrarRotuloAcima(peca, RotuloDoRegulador(peca));
        }
    }

    string RotuloDoRegulador(PecaNoCircuito peca)
    {
        if (EnergiaDoQuadro?.RegulagemDe(peca.Id) is not { } e || e.Entrada < 0.5) return null;
        double t = TemperaturaDoRegulador(peca.Id);
        if (emProtecaoTermica.Contains(peca.Id)) return $"🔥 {t:0} °C · proteção térmica";
        if (e.SobreTensao) return $"⚠ {e.Entrada:0.#} V na entrada";
        string texto = $"{e.Saida:0.0} V · {e.CorrenteDeSaida:0.00} A";
        if (t >= 60) texto += $" · {(t >= 100 ? "🔥 " : "")}{t:0} °C";
        return texto;
    }

    /// <summary>Para a barra: curta ("12,0 → 5,0 V · 0,42 A · 96 °C") ou a frase inteira (a dica).</summary>
    public string SituacaoDoRegulador(string id, bool curta)
    {
        var peca = Atual.Achar(id);
        if (peca is null || !Reguladores.EhRegulador(peca.Tipo)) return "";
        var e = EnergiaDoQuadro?.RegulagemDe(id);
        double t = TemperaturaDoRegulador(id);
        if (e is null || e.Entrada < 0.5)
            return curta ? "sem entrada" : $"Sem tensão na entrada: ligue o + da bateria no {Reguladores.PinosDe(peca.Tipo).Entrada} e o - no GND.";
        if (EstaQueimada(id)) return curta ? "queimou" : "Queimou: a entrada passou do máximo da ficha.";
        if (curta)
            return $"{e.Entrada:0.0} → {e.Saida:0.0} V · {e.CorrenteDeSaida:0.00} A" + (t >= 40 ? $" · {t:0} °C" : "")
                   + (emProtecaoTermica.Contains(id) ? " · desligou" : "");
        double eficiencia = e.Entrada * e.CorrenteDeEntrada > 1e-6 ? e.Saida * e.CorrenteDeSaida / (e.Entrada * e.CorrenteDeEntrada) : 0;
        return $"Entrada {e.Entrada:0.00} V ({e.CorrenteDeEntrada:0.000} A), saída {e.Saida:0.00} V ({e.CorrenteDeSaida:0.000} A). "
               + $"Esquenta {e.Potencia:0.00} W ({eficiencia:0%} de eficiência), o chip a {t:0} °C."
               + (emProtecaoTermica.Contains(id) ? " A proteção térmica desligou a saída até ele esfriar." : "")
               + (e.EmQueda ? $" A entrada não dá a folga: a saída ficou abaixo dos {e.Nominal:0.#} V." : "")
               + (e.Limitando ? " A carga pede mais que o limite: ele segura a corrente." : "");
    }

    /// <summary>O modelo (78xx, AMS1117) ou a saida do trimpot (LM2596, MT3608) do regulador selecionado.</summary>
    public void DefinirRegulador(string modelo, double? volts)
    {
        var peca = Selecionada;
        if (peca is null || !Reguladores.EhRegulador(peca.Tipo)) return;
        if (modelo is not null) peca.Modelo = modelo;
        if (volts is { } v) peca.Volts = v;
        if (visuais.TryGetValue(peca.Id, out var visual) && visual is System.Windows.Controls.Image img)
            img.Source = Pecas.Obter(NomeSvg(peca, false));
        Mudou?.Invoke();
    }

    /// <summary>O desenho do modelo: o 7812 escrito no TO-220, o AMS1117 de 5 V.</summary>
    static string NomeSvgDoRegulador(PecaNoCircuito peca) => peca.Tipo switch
    {
        "regulador-7805" when peca.Modelo is { } m && m != "7805" && Reguladores.Linha78.Any(f => f.Modelo == m) => "regulador-7805-" + m,
        "ams1117" when peca.Modelo == "5.0" => "ams1117-5v0",
        _ => peca.Tipo,
    };

    /// <summary>O que os reguladores avisam (ou queimam) neste quadro.</summary>
    List<Estouro> EstourosDosReguladores(Energia conta)
    {
        var achados = new List<Estouro>();
        foreach (var peca in Atual.Pecas)
        {
            if (!Reguladores.EhRegulador(peca.Tipo) || conta.RegulagemDe(peca.Id) is not { } e) continue;
            var ficha = Reguladores.Da(peca);
            string nome = "o " + Reguladores.NomeCurto(peca);

            if (e.SobreTensao)
            {
                achados.Add(new Estouro(peca.Id,
                    $"{nome} recebeu {e.Entrada:0.#} V na entrada, e o máximo dele é {ficha.EntradaMaxima:0} V: queimou."));
                continue;
            }

            if (emProtecaoTermica.Contains(peca.Id))
            {
                var antes = antesDaProtecao.GetValueOrDefault(peca.Id) ?? e;
                achados.Add(new Estouro(peca.Id,
                    $"{nome} chegou a {Desliga:0} °C e a proteção térmica desligou a saída: ele estava esquentando "
                    + $"{antes.Potencia:0.#} W ({antes.Entrada:0.#} V na entrada, {antes.Saida:0.#} V e {antes.CorrenteDeSaida:0.##} A na saída). "
                    + (ficha.Tipo == TipoDeRegulador.Linear
                        ? "Sem dissipador ele aguenta ~1,5 W. Use um dissipador, uma entrada mais baixa, ou um regulador chaveado (LM2596)."
                        : "Diminua a carga, ou use um modulo maior."), Queimou: false));
                continue;
            }

            if (e.Limitando)
                achados.Add(new Estouro(peca.Id,
                    $"a saída do {Reguladores.NomeCurto(peca)} está pedindo mais que o limite dele (~{ficha.LimiteDeCorrente:0.#} A): "
                    + $"ele segura a corrente e a tensão cai para {e.Saida:0.#} V. Confira se a saída não está em curto.", Queimou: false));
            else if (e.SoPassando && e.Entrada >= ficha.EntradaMinima && e.CorrenteDeSaida > 0.001)
                achados.Add(new Estouro(peca.Id,
                    $"{nome} só sobe a tensão: com {e.Entrada:0.#} V na entrada ele não chega aos {e.Nominal:0.#} V ajustados -- "
                    + $"sai a entrada menos o diodo ({e.Saida:0.#} V). Para baixar a tensão, use um step-down (LM2596).", Queimou: false));
            else if (e.EmQueda && e.CorrenteDeSaida > 0.001)
                achados.Add(new Estouro(peca.Id,
                    $"{nome} precisa de ~{ficha.Folga:0.#} V a mais na entrada: com {e.Entrada:0.#} V ele só dá {e.Saida:0.#} V "
                    + $"(e não {e.Nominal:0.#} V).", Queimou: false));
            else if (!e.Ligado && ficha.Tipo == TipoDeRegulador.Abaixador && e.Entrada > 0.5)
                achados.Add(new Estouro(peca.Id,
                    $"{nome} precisa de pelo menos {ficha.EntradaMinima:0.#} V na entrada para funcionar (tem {e.Entrada:0.#} V).", Queimou: false));
        }
        return achados;
    }
}
