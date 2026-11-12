namespace Simulador;

/// <summary>
/// O Matter da placa. O protocolo (PASE, CASE, o IPv6 das controladoras) nao roda no
/// simulador: quem faz o papel do aplicativo (o Google Home, o Apple Home) e a bancada.
/// <see cref="Comissionar"/> e "ler o codigo de pareamento"; <see cref="Escrever"/> e o
/// aplicativo mudando um atributo (chega no attributeChangeCB do endpoint); o que a
/// placa reporta fica aqui e no registro da placa (o painel Rede, "matter").
/// </summary>
public sealed class MatterDaPlaca(Maquina dona)
{
    readonly object trava = new();
    readonly Dictionary<string, string> relatados = [];
    readonly Queue<string> escritas = new();
    readonly List<string> eventos = [];

    /// <summary>Algum aplicativo comissionou a placa (entrou no fabric).</summary>
    public bool Comissionada { get; private set; }

    public void Comissionar()
    {
        Comissionada = true;
        dona.Rede?.Anotar(dona, "matter", Idioma.T("comissionada (a bancada faz o papel do aplicativo)"));
    }

    public void Descomissionar()
    {
        Comissionada = false;
        lock (trava) escritas.Clear();
        dona.Rede?.Anotar(dona, "matter", Idioma.T("saiu do fabric"));
    }

    public void Relatar(int endpoint, long cluster, long atributo, string valor)
    {
        lock (trava) relatados[$"{endpoint}/{cluster}/{atributo}"] = valor;
        dona.Rede?.Anotar(dona, "matter", Idioma.T("endpoint {0} cluster 0x{1:X4} atributo 0x{2:X4} = {3}", endpoint, cluster, atributo, valor));
    }

    /// <summary>O ultimo valor que a placa reportou desse atributo, ou null.</summary>
    public string Valor(int endpoint, long cluster, long atributo)
    {
        lock (trava) return relatados.TryGetValue($"{endpoint}/{cluster}/{atributo}", out var v) ? v : null;
    }

    /// <summary>O aplicativo escreve num atributo; so vale com a placa comissionada.</summary>
    public void Escrever(int endpoint, long cluster, long atributo, double valor)
    {
        if (!Comissionada) return;
        lock (trava) escritas.Enqueue(FormattableString.Invariant($"{endpoint} {cluster} {atributo} {valor}"));
    }

    /// <summary>A proxima escrita do aplicativo ("endpoint cluster atributo valor"), ou vazio.</summary>
    public string ProximaEscrita()
    {
        lock (trava) return escritas.Count > 0 ? escritas.Dequeue() : "";
    }

    public void Evento(int endpoint, string nome, int posicao, int quantas)
    {
        string texto = $"{endpoint} {nome} {posicao} {quantas}";
        lock (trava) eventos.Add(texto);
        dona.Rede?.Anotar(dona, "matter", "evento " + texto);
    }

    /// <summary>Os eventos do interruptor ("endpoint nome posicao quantas"), na ordem.</summary>
    public List<string> Eventos { get { lock (trava) return [.. eventos]; } }
}
