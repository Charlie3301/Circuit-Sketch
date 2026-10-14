namespace Simulador;

public sealed class Radio
{
    public Maquina Dona { get; init; }
    public bool Ligado { get; set; }
    public bool Ouvindo { get; set; }
    public int Canal { get; set; } = 76;
    public string Escrita { get; set; } = "";

    public readonly HashSet<string> Leitura = [];
    public readonly Queue<string> Caixa = new();
    public string UltimoRemetente { get; set; } = "";
}

public sealed class CargaEspNow
{
    public string Origem { get; init; } = "";
    public string Texto { get; init; } = "";
    public Dictionary<string, Valor> Campos { get; init; }
    public int Tamanho { get; init; }
}

public sealed class PlacaEspNow
{
    public Maquina Dona { get; init; }
    public bool Ligado { get; set; }
    public string AoReceber { get; set; }
    public string AoEnviar { get; set; }
    public readonly HashSet<string> Pares = [];
    public readonly Queue<CargaEspNow> Caixa = new();
}
