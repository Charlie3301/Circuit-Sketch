namespace Simulador.App;

/// <summary>
/// Os passivos redesenhados (Alpha 25, pecas-passivos.py): cada modelo tem o
/// desenho dele (o eletrolitico de 2200 uF e maior que o de 1 uF, o fio do
/// indutor de 10 mH e mais fino), o editor usa o do modelo escolhido, a arte
/// grande passa da caixa pela sobra sem tirar os pinos do lugar, e as faixas
/// do resistor caem no corpo dele.
/// </summary>
public static partial class Autoteste
{
    static void Passivos_Desenho()
    {
        saida.AppendLine("passivos: um desenho por modelo");

        var modelos = new List<(string Tipo, string Modelo)>();
        foreach (string tipo in new[] { "capacitor", "capacitor-eletrolitico", "indutor", "cristal" })
            modelos.AddRange(Passivos.ModelosDe(tipo).Select(m => (tipo, m.Modelo)));
        foreach (string tipo in new[] { "fusivel", "varistor" })
            modelos.AddRange(Semicondutores.Opcoes(tipo).Select(o => (tipo, o.Modelo)));

        var faltam = new List<string>();
        var ed = new EditorCircuito();
        foreach (var (tipo, modelo) in modelos)
        {
            var def = Catalogo.Obter(tipo);
            var peca = new PecaNoCircuito { Id = "p", Tipo = tipo, Modelo = modelo };
            string nome = $"{tipo}-{modelo}";
            if (!Pecas.Existe(nome) || ed.DesenhoDa(peca, def) is not System.Windows.Controls.Image img
                || !ReferenceEquals(img.Source, Pecas.Obter(nome)))
                faltam.Add(nome);
        }
        Conferir($"os {modelos.Count} modelos (ceramico, eletrolitico, indutor, cristal, fusivel, varistor) tem o desenho deles, e e o usado",
            faltam.Count == 0, "faltam: " + string.Join(", ", faltam));

        var pequeno = Pecas.SobraDe("capacitor-eletrolitico-1u-50v");
        var grande = Pecas.SobraDe("capacitor-eletrolitico-2200u-16v");
        Conferir("o eletrolitico de 1 uF (5 x 11 mm) cabe na caixa; o de 2200 uF (10 x 20 mm) passa dela pela sobra",
            pequeno.X == 0 && pequeno.Y == 0 && grande.X > 5 && grande.Y > 25, $"{pequeno} / {grande}");

        // trocar o modelo refaz o tamanho da imagem (a sobra do desenho novo)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "cap", Tipo = "capacitor-eletrolitico", Modelo = "10u-25v", X = 100, Y = 100 });
            var editor = new EditorCircuito();
            editor.Carregar(c);
            editor.SelecionarParaTeste("cap");
            var antes = editor.DesenhoDaPecaParaTeste("cap");
            double alturaAntes = antes?.Height ?? 0;
            editor.DefinirModeloSimples("2200u-16v");
            var depois = editor.DesenhoDaPecaParaTeste("cap");
            Conferir("trocar 10 uF por 2200 uF: a imagem cresce para a lata de 20 mm (a margem negativa leva a sobra para fora)",
                depois is not null && depois.Height > alturaAntes + 50 && depois.Margin.Top < -25,
                $"{alturaAntes:0} -> {depois?.Height:0}, margem {depois?.Margin.Top:0}");
        }

        // as faixas do resistor: quatro, dentro do corpo, a de tolerancia afastada das outras
        var faixas = DesenhoDosPassivos.FaixasDoResistor;
        var defR = Catalogo.Obter("resistor");
        bool dentro = faixas.All(f => f.X > defR.Largura * 0.25 && f.X < defR.Largura * 0.75 && f.Altura < defR.Altura);
        bool afastada = faixas.Length == 4 && faixas[3].X - faixas[2].X > 2 * (faixas[2].X - faixas[1].X);
        Conferir("resistor: as quatro faixas no corpo, a de tolerancia afastada (e por ela que se sabe o lado de ler)",
            dentro && afastada, string.Join(" ", faixas.Select(f => f.X.ToString("0.0"))));
    }
}
