namespace Simulador.App;

/// <summary>
/// Os pinos da placa que nao sao GPIO nem alimentacao (Alpha 25):
///
///  - o RESET da barra (e o do ICSP): ligado no GND -- por um fio, ou por um
///    botao apertado entre ele e o GND --, segura a placa parada, como o
///    botao de RESET; soltou, ela volta do setup();
///  - o AREF: com analogReference(EXTERNAL), o conversor usa a tensao que
///    chega nele como fundo de escala (medida a cada quadro na conta de
///    energia); o ADC_VREF da Pico faz o mesmo sem precisar pedir.
/// </summary>
public partial class EditorCircuito
{
    /// <summary>Placas com o RESET ligado no GND agora.</summary>
    readonly HashSet<string> resetPorFio = [];

    /// <summary>O pino que segura cada placa (RESET, EN, RUN, 3V3_EN), para a mensagem.</summary>
    readonly Dictionary<string, string> pinoDoResetPorFio = [];

    /// <summary>O pino que esta segurando a placa no GND agora (nulo: nenhum).</summary>
    public string PinoQueSegura(string idDaPlaca) => pinoDoResetPorFio.GetValueOrDefault(idDaPlaca);

    /// <summary>O RESET de uma placa foi ligado no GND (true) ou soltou (false).</summary>
    public event Action<string, bool> ResetPorFioMudou;

    static bool EhPinoDeReset(string nome)
    {
        int ponto = nome.LastIndexOf('.');
        string base_ = ponto > 0 && nome[(ponto + 1)..].All(char.IsAsciiDigit) ? nome[..ponto] : nome;
        // EN: o CHIP_PU das ESP32 e o CH_PD das ESP8266 (o RST da serigrafia
        // nas DevKits novas); RUN: o da Pico. Todos seguram o chip parado no GND.
        // O 3V3_EN da Pico desliga o regulador: a placa apaga enquanto ele estiver no GND.
        return base_ is "RESET" or "RST" or "EN" or "RUN" or "3V3_EN";
    }

    /// <summary>A janela nao pode segurar agora (parada): esquece, para tentar de novo no proximo quadro.</summary>
    public void EsquecerResetPorFio(string idDaPlaca) => resetPorFio.Remove(idDaPlaca);

    void AplicarPinosDaPlaca(Bancada bancada, Netlist rede)
    {
        foreach (var placa in Atual.Pecas)
        {
            if (!PerfilPlaca.EhPlaca(placa.Tipo) || Catalogo.Obter(placa.Tipo) is not { } def) continue;

            // ---- o RESET no GND ----
            string segura = EmSimulacao ? def.Pinos.FirstOrDefault(p => EhPinoDeReset(p.Nome) && ResetNoTerra(rede, placa.Id, p.Nome))?.Nome : null;
            bool preso = segura is not null;
            if (preso) pinoDoResetPorFio[placa.Id] = segura; else pinoDoResetPorFio.Remove(placa.Id);
            if (preso ? resetPorFio.Add(placa.Id) : resetPorFio.Remove(placa.Id))
                ResetPorFioMudou?.Invoke(placa.Id, preso);

            var maq = bancada.De(placa.Id);
            if (maq is null) continue;

            // ---- o ADC_VREF da Pico: a referencia do conversor, sempre ----
            // Solto, fica a de fabrica (os 3,3 V pelo filtro da placa); ligado
            // numa tensao, ela manda; no GND, o conversor fica sem referencia.
            if (maq.Perfil.AdcVrefNoPino)
            {
                const string vref = "ADC_VREF";
                double volts = EnergiaDoQuadro?.TensaoDoPino(placa.Id, vref) ?? 0;
                maq.TensaoNoAref = !PinoLigado(rede, placa.Id, vref) ? null
                    : ChegaEm(rede, placa.Id, vref, terra: true) ? 0
                    : volts > 0.05 ? volts : null;
                continue;
            }

            // ---- a tensao no AREF, para o analogReference(EXTERNAL) ----
            if (maq.ReferenciaAnalogica != Maquina.ReferenciaExterna) continue;
            var aref = def.Pinos.FirstOrDefault(p => p.Nome == "AREF");
            maq.TensaoNoAref = aref is not null && PinoLigado(rede, placa.Id, aref.Nome)
                ? EnergiaDoQuadro?.TensaoDoPino(placa.Id, aref.Nome) : null;
        }
    }

    /// <summary>O pino chega no GND, direto ou por um botao apertado (um lado nele, o outro no GND).</summary>
    bool ResetNoTerra(Netlist rede, string placa, string pino)
    {
        string raiz = rede.RaizDe(Netlist.No(placa, pino));
        if (raiz is null) return false;
        if (ChegaEm(rede, placa, pino, terra: true)) return true;
        foreach (string id in BotoesPressionados)
        {
            if (Atual.Achar(id) is not { } botao || Catalogo.Obter(botao.Tipo) is not { } defBotao) continue;
            bool noReset = defBotao.Pinos.Any(q => rede.RaizDe(Netlist.No(id, q.Nome)) == raiz);
            bool noTerra = defBotao.Pinos.Any(q => rede.RaizDe(Netlist.No(id, q.Nome)) != raiz && ChegaEm(rede, id, q.Nome, terra: true));
            if (noReset && noTerra) return true;
        }
        return false;
    }

    /// <summary>Alguma outra peca (ou outro pino) esta no mesmo no deste pino?</summary>
    bool PinoLigado(Netlist rede, string peca, string pino)
    {
        string raiz = rede.RaizDe(Netlist.No(peca, pino));
        if (raiz is null) return false;
        foreach (var outra in Atual.Pecas)
        {
            if (Catalogo.Obter(outra.Tipo) is not { } d) continue;
            foreach (var p in d.Pinos)
                if ((outra.Id != peca || p.Nome != pino) && rede.RaizDe(Netlist.No(outra.Id, p.Nome)) == raiz)
                    return true;
        }
        return false;
    }

    /// <summary>Para o teste: a placa esta com o RESET preso no GND?</summary>
    internal bool ResetPorFioParaTeste(string idDaPlaca) => resetPorFio.Contains(idDaPlaca);
}
