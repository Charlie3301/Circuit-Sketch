namespace Simulador;

/// <summary>
/// O sketch compilado (CompiladorNativo) dentro do interpretador da placa.
///
/// O interpretador continua sendo quem a janela, o depurador e os testes
/// conhecem; quando o programa veio compilado, o setup() e o loop() sao os da
/// DLL, e o interpretador vira o "core" dela: cada pinMode, delay ou
/// Serial.print que o C++ chama cai aqui (ChamarDoNativo), na mesma
/// implementacao das funcoes do caminho interpretado.
/// </summary>
public partial class Interpretador
{
    SketchNativo sketchNativo;

    /// <summary>O programa veio compilado de verdade (e roda nativo).</summary>
    public bool RodaNativo => prog.Compilado is not null;

    /// <summary>Carrega a DLL desta placa (uma vez por rodada), na thread da placa.</summary>
    void AbrirSketchNativo()
    {
        if (sketchNativo is not null) return;
        maq.Sketch?.Encerrar();
        sketchNativo = new SketchNativo(maq, ChamarDoNativo, AtenderInterrupcoes, () => maq.Interrupcoes.Count > 0);
        maq.Sketch = sketchNativo;
        sketchNativo.Carregar(prog.Compilado, Origem);
    }

    /// <summary>
    /// Uma chamada do C++ ao core: vira a mesma chamada que o sketch
    /// interpretado faria ("delay(500)", "Serial.print(...)"), com os valores
    /// prontos no lugar das expressoes.
    /// </summary>
    Valor ChamarDoNativo(string alvo, string nome, List<Valor> args, List<bool> nomes)
    {
        var exprs = new List<Expr>(args.Count);
        for (int i = 0; i < args.Count; i++)
            exprs.Add(nomes[i] ? new Variavel(args[i].Texto, 0) : new Pronto(args[i], 0));
        Expr funcao = alvo is null ? new Variavel(nome, 0) : new Membro(new Variavel(alvo, 0), nome, 0);
        return Avaliar(new Chamada(funcao, exprs, 0), global);
    }

    /// <summary>"__nativa_3": a rotina de interrupcao 3 da DLL.</summary>
    static bool RotinaNativa(string nome, out int indice)
    {
        indice = -1;
        return nome is not null && nome.StartsWith("__nativa_", StringComparison.Ordinal)
               && int.TryParse(nome.AsSpan(9), out indice);
    }

    /// <summary>
    /// A constante do core com o numero que o simulador usa ("RISING" e 1):
    /// o cabecalho da placa do C++ compilado define as mesmas.
    /// </summary>
    public static string ConstanteDoCore(string nome)
        => Constantes.TryGetValue(nome, out var v) && !v.EhTexto ? ((long)v.Num).ToString() : null;
}
