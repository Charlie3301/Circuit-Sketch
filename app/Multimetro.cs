using System.Globalization;
using System.Windows;

namespace Simulador.App;

/// <summary>As posicoes da chave do multimetro.</summary>
public enum FuncaoDoMultimetro { TensaoAlternada, TensaoContinua, Resistencia, Continuidade, Corrente }

/// <summary>
/// Onde uma ponta de prova encostou: o pino de uma peca (ou o furo de uma base), ou
/// um fio (Fio >= 0: a ponta vale o no do fio, que e o de uma das pontas dele).
/// </summary>
public sealed record PontaDoMultimetro(string Peca, string Pino, int Fio, Point Onde, string Rotulo);

/// <summary>
/// O que o visor mostra: os digitos ("5.000", "OL", "----"), a unidade ("V", "mA",
/// "kΩ"), e o que mais o multimetro diz (o bipe da continuidade, uma nota).
/// </summary>
public sealed record LeituraDoMultimetro(string Visor, string Unidade, double? Valor, bool Fora, bool Bipe, string Nota)
{
    /// <summary>Para a etiqueta ao lado da ponta: "5.000 V", "OL".</summary>
    public string Curta => Valor is null && !Fora ? "" : Fora ? "OL" : $"{Visor} {Unidade}";
}

/// <summary>
/// O multimetro: le a conta de energia com as duas pontas, como um de verdade.
///
/// - V~ e V⎓: a tensao entre a ponta vermelha e a preta (sem a preta, o terra do
///   circuito). A alternada e a do PWM, em valor eficaz.
/// - A: a corrente que passa pelo que a ponta vermelha toca (a peca, ou o no).
/// - Ω: a resistencia entre as pontas, com as fontes de fora; sem caminho, "OL".
/// - ·))): a mesma conta, e bipa abaixo de 50 Ω.
///
/// A faixa e automatica (o "AUTO" do visor), com quatro digitos.
/// </summary>
public static class Multimetro
{
    /// <summary>O limite da continuidade: abaixo disto o multimetro bipa.</summary>
    public const double LimiteDaContinuidade = 50;

    /// <summary>Acima disto o ohmimetro mostra "OL" (a faixa mais alta, 40 MΩ).</summary>
    public const double ForaDaFaixaDoOhmimetro = 40e6;

    public static LeituraDoMultimetro Medir(FuncaoDoMultimetro funcao, Energia conta, PontaDoMultimetro vermelha,
                                            PontaDoMultimetro preta, bool rodando, Circuito circuito)
    {
        string unidadeBase = funcao switch
        {
            FuncaoDoMultimetro.Corrente => "mA",
            FuncaoDoMultimetro.Resistencia or FuncaoDoMultimetro.Continuidade => "Ω",
            _ => "V",
        };
        LeituraDoMultimetro Vazio(string nota) => new("----", unidadeBase, null, false, false, nota);

        if (vermelha is null)
            return Vazio(Idioma.T("Encoste a ponta vermelha: botão esquerdo num pino, furo ou fio."));

        bool ohmimetro = funcao is FuncaoDoMultimetro.Resistencia or FuncaoDoMultimetro.Continuidade;
        if (!ohmimetro && !rodando)
            return new LeituraDoMultimetro(funcao == FuncaoDoMultimetro.Corrente ? "0.000" : "0.000", unidadeBase, 0, false, false,
                Idioma.T("Circuito parado: rode a simulação (F5) para ter tensão e corrente."));
        if (conta is null)
            return Vazio(rodando ? "Medindo…" : Idioma.T("Sem a conta do circuito."));

        string raizVermelha = RaizDa(conta, vermelha, circuito);
        string raizPreta = preta is null ? null : RaizDa(conta, preta, circuito);

        switch (funcao)
        {
            case FuncaoDoMultimetro.TensaoContinua:
            case FuncaoDoMultimetro.TensaoAlternada:
            {
                double v = funcao == FuncaoDoMultimetro.TensaoContinua
                    ? conta.ContinuaEntre(raizVermelha, raizPreta)
                    : conta.AlternadaEntre(raizVermelha, raizPreta);
                var (visor, unidade) = Formatar(v, "V");
                string nota = preta is null ? Idioma.T("Sem a ponta preta: medindo contra o terra (GND) do circuito.") : null;
                if (funcao == FuncaoDoMultimetro.TensaoAlternada && Math.Abs(v) < 1e-4)
                    nota ??= Idioma.T("Sem parte alternada aqui: é tensão contínua (o V~ mede o PWM e o tone).");
                return new LeituraDoMultimetro(visor, unidade, v, false, false, nota);
            }

            case FuncaoDoMultimetro.Corrente:
            {
                var peca = vermelha.Fio < 0 ? circuito?.Achar(vermelha.Peca) : null;
                bool daPeca = peca is not null && !Catalogo.EhProtoboard(peca.Tipo) && !PerfilPlaca.EhPlaca(peca.Tipo)
                              && conta.CorrentesDePeca.ContainsKey(peca.Id);
                double i = daPeca ? conta.CorrenteDaPeca(peca.Id) : conta.CorrenteDoNo(raizVermelha);
                var (visor, unidade) = Formatar(i, "A");
                string alvo = daPeca ? Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Id : Idioma.T("o fio / o nó");
                return new LeituraDoMultimetro(visor, unidade, i, false, false, Idioma.T("A corrente que passa por {0}.", alvo));
            }

            default:
            {
                if (preta is null)
                    return Vazio(Idioma.T("Encoste a ponta preta: botão direito (ou Shift+clique) no outro lado."));
                double? r = conta.ResistenciaEntre(raizVermelha, raizPreta);
                bool fora = r is null || r > ForaDaFaixaDoOhmimetro;
                bool bipe = funcao == FuncaoDoMultimetro.Continuidade && !fora && r < LimiteDaContinuidade;
                string nota = rodando && Math.Abs(conta.ContinuaEntre(raizVermelha, raizPreta)) > 0.05
                    ? Idioma.T("Circuito ligado: no de verdade, meça a resistência com ele desligado.")
                    : fora ? Idioma.T("Sem caminho entre as pontas (circuito aberto).") : null;
                if (fora) return new LeituraDoMultimetro("OL", r is null ? "MΩ" : "MΩ", null, true, false, nota);
                var (visor, unidade) = Formatar(r.Value, "Ω");
                return new LeituraDoMultimetro(visor, unidade, r, false, bipe, nota);
            }
        }
    }

    /// <summary>O no da ponta na conta: o do pino, ou o de uma das pontas do fio.</summary>
    static string RaizDa(Energia conta, PontaDoMultimetro ponta, Circuito circuito)
    {
        if (ponta.Fio >= 0 && circuito is not null && ponta.Fio < circuito.Fios.Count)
        {
            var fio = circuito.Fios[ponta.Fio];
            return conta.RaizDoPino(fio.DePeca, fio.DePino) ?? conta.RaizDoPino(fio.ParaPeca, fio.ParaPino);
        }
        return conta.RaizDoPino(ponta.Peca, ponta.Pino);
    }

    /// <summary>
    /// A faixa automatica: o valor em quatro digitos com o prefixo certo
    /// ("0.512" V vira "512.0" mV; 4700 Ω vira "4.700" kΩ).
    /// </summary>
    public static (string Visor, string Unidade) Formatar(double valor, string unidade)
    {
        (double Fator, string Prefixo)[] faixas = unidade == "Ω"
            ? [(1e6, "M"), (1e3, "k"), (1, "")]
            : unidade == "A"
                ? [(1, ""), (1e-3, "m"), (1e-6, "µ")]
                : [(1, ""), (1e-3, "m")];

        double a = Math.Abs(valor);
        if (a < 1e-12)
            return ("0.000", unidade == "A" ? "mA" : unidade);

        int escolhida = faixas.Length - 1;
        for (int k = 0; k < faixas.Length; k++)
            if (a >= faixas[k].Fator) { escolhida = k; break; }

        for (int tentativa = 0; tentativa < 2; tentativa++)
        {
            var (fator, prefixo) = faixas[escolhida];
            double x = valor / fator, ax = Math.Abs(x);
            int casas = ax >= 1000 ? 0 : ax >= 100 ? 1 : ax >= 10 ? 2 : 3;
            string texto = x.ToString("F" + casas, CultureInfo.InvariantCulture);
            // O arredondamento passou de 9999: sobe uma faixa.
            if (Math.Abs(double.Parse(texto, CultureInfo.InvariantCulture)) >= 9999.5 && escolhida > 0) { escolhida--; continue; }
            return (texto, prefixo + unidade);
        }
        return ("OL", unidade);
    }

    /// <summary>O nome da funcao, como no painel e na barra.</summary>
    public static string Nome(FuncaoDoMultimetro funcao) => funcao switch
    {
        FuncaoDoMultimetro.TensaoAlternada => Idioma.T("tensão alternada (V~)"),
        FuncaoDoMultimetro.TensaoContinua => Idioma.T("tensão contínua (V⎓)"),
        FuncaoDoMultimetro.Resistencia => Idioma.T("resistência (Ω)"),
        FuncaoDoMultimetro.Continuidade => Idioma.T("continuidade (·)))"),
        _ => Idioma.T("corrente (A)"),
    };
}
