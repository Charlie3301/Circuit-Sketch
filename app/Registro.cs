namespace Simulador.App;

public enum NivelDoConsole { Info, Aviso, Erro }

/// <summary>Um recado do simulador: a hora, o peso e o texto.</summary>
public readonly record struct LinhaDoConsole(DateTime Quando, NivelDoConsole Nivel, string Texto)
{
    /// <summary>A marca da coluna do meio. Largura fixa para as linhas alinharem.</summary>
    public string Marca => Nivel switch
    {
        NivelDoConsole.Erro => "ERRO ",
        NivelDoConsole.Aviso => "AVISO",
        _ => "     ",
    };

    public override string ToString() => $"{Quando:HH:mm:ss}  {Marca}  {Texto}";
}

/// <summary>
/// O console: o que o SIMULADOR tem a dizer.
///
/// Alpha 18: "adicionar CONSOLE do lado esquerdo do monitor serial. O
/// aplicativo ainda vai abrir com o monitor serial como principal, mas
/// vai ter o console também."
///
/// São dois assuntos que estavam no mesmo lugar e não são a mesma coisa.
/// O monitor serial é do SKETCH -- cada linha ali saiu de um
/// Serial.println que o aluno escreveu. O console é do PROGRAMA: o que
/// compilou, o que queimou, o que parou e por quê. Até aqui isso morava
/// na linha de status, que guarda uma mensagem só: a anterior sumia sem
/// deixar rastro, e com ela o motivo de o sketch ter parado.
/// </summary>
public sealed class Registro
{
    /// <summary>
    /// Quantas linhas ficam guardadas.
    ///
    /// Um sketch que erra dentro do loop escreve depressa; sem teto, uma
    /// aula inteira de mensagens fica na memória para ninguém ler.
    /// </summary>
    const int Maximo = 2000;

    readonly List<LinhaDoConsole> linhas = [];
    readonly Lock trava = new();

    /// <summary>Quantas linhas JÁ PASSARAM por aqui, contando as que caíram pela borda.</summary>
    public long Total { get; private set; }

    public event Action Mudou;

    public IReadOnlyList<LinhaDoConsole> Linhas
    {
        get { lock (trava) return [.. linhas]; }
    }

    public void Escrever(NivelDoConsole nivel, string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;

        lock (trava)
        {
            // Mensagem igual à anterior não vira linha nova. A linha de
            // status é reescrita muitas vezes com o mesmo texto -- sem
            // isto, "rodando..." sairia uma vez por quadro e empurraria
            // para fora tudo o que interessa.
            if (linhas.Count > 0 && linhas[^1].Nivel == nivel && linhas[^1].Texto == texto)
                return;

            linhas.Add(new LinhaDoConsole(DateTime.Now, nivel, texto));
            Total++;

            if (linhas.Count > Maximo) linhas.RemoveRange(0, linhas.Count - Maximo);
        }

        Mudou?.Invoke();
    }

    public void Info(string texto) => Escrever(NivelDoConsole.Info, texto);
    public void Aviso(string texto) => Escrever(NivelDoConsole.Aviso, texto);
    public void Erro(string texto) => Escrever(NivelDoConsole.Erro, texto);

    public void Limpar()
    {
        lock (trava)
        {
            linhas.Clear();
            Total = 0;
        }

        Mudou?.Invoke();
    }

    /// <summary>O console inteiro em texto, para copiar ou para o teste.</summary>
    public string EmTexto() => string.Join(Environment.NewLine, Linhas);
}
