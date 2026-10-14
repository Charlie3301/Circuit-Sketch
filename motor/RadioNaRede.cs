namespace Simulador;

public sealed partial class Rede
{
    public const string MacDeBroadcast = "FF:FF:FF:FF:FF:FF";

    readonly Dictionary<Maquina, Radio> radios = [];
    readonly Dictionary<Maquina, PlacaEspNow> espnow = [];

    public Radio RadioDe(Maquina placa)
    {
        lock (trava)
        {
            if (!radios.TryGetValue(placa, out var r))
                radios[placa] = r = new Radio { Dona = placa };
            return r;
        }
    }

    public int OuvintesDoRadio(string endereco, int canal, Radio menos)
    {
        lock (trava)
            return radios.Values.Count(
                r => r != menos && r.Ligado && r.Ouvindo && r.Canal == canal
                     && r.Leitura.Contains(endereco));
    }

    public bool EnviarNoRadio(Radio quem, string carga)
    {
        if (quem is null || !quem.Ligado || string.IsNullOrEmpty(quem.Escrita)) return false;

        var alvos = new List<Radio>();
        lock (trava)
            alvos.AddRange(radios.Values.Where(
                r => r != quem && r.Ligado && r.Ouvindo && r.Canal == quem.Canal
                     && r.Leitura.Contains(quem.Escrita)));

        foreach (var alvo in alvos)
            lock (trava)
            {
                alvo.Caixa.Enqueue(carga);
                alvo.UltimoRemetente = quem.Dona?.Nome ?? "placa";
                while (alvo.Caixa.Count > 32) alvo.Caixa.Dequeue();
            }

        Anotar(quem.Dona, "radio",
            alvos.Count > 0
                ? $"nRF24 enviou \"{Curto(carga, 40)}\" em {quem.Escrita} · {alvos.Count} ouvindo"
                : $"nRF24 enviou \"{Curto(carga, 40)}\" em {quem.Escrita} · ninguem ouvindo");

        return alvos.Count > 0;
    }

    public string ReceberDoRadio(Radio quem)
    {
        lock (trava) return quem.Caixa.Count > 0 ? quem.Caixa.Dequeue() : null;
    }

    public bool TemNoRadio(Radio quem)
    {
        lock (trava) return quem.Caixa.Count > 0;
    }

    public PlacaEspNow EspNowDe(Maquina placa)
    {
        lock (trava)
        {
            if (!espnow.TryGetValue(placa, out var p))
                espnow[placa] = p = new PlacaEspNow { Dona = placa };
            return p;
        }
    }

    public string MacDe(Maquina placa) => Wifi(placa).Mac;

    public int EnviarEspNow(PlacaEspNow quem, string destino, CargaEspNow carga)
    {
        if (quem is null || !quem.Ligado) return 0;

        bool todos = string.Equals(destino, MacDeBroadcast, StringComparison.OrdinalIgnoreCase);
        var alvos = new List<PlacaEspNow>();

        lock (trava)
            foreach (var (placa, outra) in espnow)
            {
                if (outra == quem || !outra.Ligado) continue;
                if (todos || string.Equals(Wifi(placa).Mac, destino,
                                           StringComparison.OrdinalIgnoreCase))
                    alvos.Add(outra);
            }

        foreach (var alvo in alvos)
            lock (trava)
            {
                alvo.Caixa.Enqueue(carga);
                while (alvo.Caixa.Count > 32) alvo.Caixa.Dequeue();
            }

        Anotar(quem.Dona, "esp-now",
            alvos.Count > 0
                ? $"enviou para {destino} · {alvos.Count} placa(s)"
                : $"enviou para {destino} · nenhuma placa recebeu");

        return alvos.Count;
    }

    public CargaEspNow ReceberEspNow(PlacaEspNow quem)
    {
        lock (trava) return quem.Caixa.Count > 0 ? quem.Caixa.Dequeue() : null;
    }

    public List<Maquina> PlacasComEspNow()
    {
        lock (trava) return [.. espnow.Where(x => x.Value.Ligado).Select(x => x.Key)];
    }

    void EsquecerRadios(Maquina placa)
    {
        lock (trava)
        {
            radios.Remove(placa);
            espnow.Remove(placa);
        }
    }
}
