namespace Simulador;

/// <summary>
/// O ESP RainMaker da placa: a nuvem da Espressif e o aplicativo do celular
/// sao o computador da bancada. O que o sketch relata (updateAndReport) fica
/// aqui e no registro da placa (o painel Rede, "rainmaker"); o que o
/// "aplicativo" escreve num parametro (<see cref="Escrever"/>) chega no
/// callback do sketch, como uma escrita vinda da nuvem.
/// </summary>
public sealed class RainMakerDaPlaca(Maquina dona)
{
    readonly object trava = new();
    readonly Dictionary<string, string> relatados = [];
    readonly Queue<string> escritas = new();
    readonly List<string> avisos = [];

    /// <summary>A placa ligou o RainMaker e esta na rede (o MQTT conectou).</summary>
    public bool Conectada { get; private set; }

    public void Conectou()
    {
        Conectada = true;
        dona.Rede?.Anotar(dona, "rainmaker", "conectou na nuvem do RainMaker (a bancada faz o papel dela)");
    }

    public void Relatar(string dispositivo, string parametro, string valor)
    {
        lock (trava) relatados[$"{dispositivo}/{parametro}"] = valor;
        dona.Rede?.Anotar(dona, "rainmaker", $"{dispositivo}.{parametro} = {valor}");
    }

    /// <summary>O ultimo valor que a placa relatou desse parametro, ou null.</summary>
    public string Valor(string dispositivo, string parametro)
    {
        lock (trava) return relatados.TryGetValue($"{dispositivo}/{parametro}", out var v) ? v : null;
    }

    /// <summary>O "aplicativo" muda um parametro: o sketch recebe no callback do dispositivo.</summary>
    public void Escrever(string dispositivo, string parametro, string valor)
    {
        lock (trava) escritas.Enqueue($"{dispositivo}|{parametro}|{valor}");
    }

    /// <summary>A proxima escrita do aplicativo ("dispositivo|parametro|valor"), ou vazio.</summary>
    public string ProximaEscrita()
    {
        lock (trava) return escritas.Count > 0 ? escritas.Dequeue() : "";
    }

    public void Avisar(string texto)
    {
        lock (trava) avisos.Add(texto);
        dona.Rede?.Anotar(dona, "rainmaker", texto);
    }

    /// <summary>Os alertas e as notificacoes que a placa mandou ao aplicativo.</summary>
    public List<string> Avisos { get { lock (trava) return [.. avisos]; } }
}
