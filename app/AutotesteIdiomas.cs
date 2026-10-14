using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simulador.App;

/// <summary>
/// A base dos idiomas (Alpha 25): o catalogo troca a frase, a frase sem
/// traducao continua em portugues, o numero sai na cultura do idioma, o XAML
/// traduz o menu, e nenhum catalogo perde um {0} da frase original.
/// </summary>
public static partial class Autoteste
{
    static void Idiomas_Base()
    {
        saida.AppendLine("idiomas: a base da traducao (Alpha 25)");
        try
        {
            var todos = Idioma.Disponiveis();
            Conferir("idiomas: o original e o ingles estao na lista",
                todos.Any(i => i.Codigo == Idioma.Original) && todos.Any(i => i.Codigo == "en"),
                string.Join(", ", todos.Select(i => $"{i.Codigo} {i.Nome}")));

            bool usou = Idioma.Usar("en");
            Conferir("idiomas: em ingles a frase traduzida troca, e a que falta continua em portugues",
                usou && Idioma.Atual == "en" && Idioma.T("Salvar projeto") == "Save project"
                && Idioma.T("frase que ninguem traduziu") == "frase que ninguem traduziu",
                $"{Idioma.Atual}: '{Idioma.T("Salvar projeto")}'");
            Conferir("idiomas: o formato usa a traducao e a cultura do idioma (ponto decimal no ingles)",
                Idioma.T("{0} peças, {1} fios", 3, 2) == "3 parts, 2 wires" && Idioma.T("{0:0.0} V", 3.3) == "3.3 V",
                $"'{Idioma.T("{0} peças, {1} fios", 3, 2)}' / '{Idioma.T("{0:0.0} V", 3.3)}'");

            // O XAML: o menu da janela montada em ingles.
            var janela = new MainWindow();
            string menu;
            try
            {
                var aberto = janela.TesteAbrirMenuDeOpcoes();
                menu = string.Join(" | ", Itens(aberto.Items).Select(m => m.Header?.ToString()));
                aberto.IsOpen = false;
            }
            finally { try { janela.Close(); } catch (InvalidOperationException) { } }
            Conferir("idiomas: o menu montado em ingles sai traduzido ({local:T ...} no XAML)",
                menu.Contains("Save project as...") && menu.Contains("Recent projects"), menu.Length > 200 ? menu[..200] : menu);

            bool desconhecido = Idioma.Usar("xx");
            Conferir("idiomas: um idioma sem catalogo volta ao portugues",
                !desconhecido && Idioma.Atual == Idioma.Original && Idioma.T("Salvar projeto") == "Salvar projeto"
                && Idioma.T("{0:0.0} V", 3.3) == "3,3 V", Idioma.T("{0:0.0} V", 3.3));
        }
        finally
        {
            Idioma.Usar(Idioma.Original);
        }

        // Nenhum catalogo perde (ou ganha) uma marca {n} da frase original.
        var marca = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
        var assembly = typeof(Idioma).Assembly;
        var problemas = new List<string>();
        int catalogos = 0;
        foreach (string recurso in assembly.GetManifestResourceNames().Where(r => r.StartsWith("idiomas/", StringComparison.Ordinal)))
        {
            catalogos++;
            using var doc = JsonDocument.Parse(assembly.GetManifestResourceStream(recurso)!);
            foreach (var p in doc.RootElement.GetProperty("textos").EnumerateObject())
            {
                string traducao = p.Value.GetString();
                if (string.IsNullOrEmpty(traducao)) continue;
                var a = marca.Matches(p.Name).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                var b = marca.Matches(traducao).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                if (!a.SequenceEqual(b)) problemas.Add($"{recurso}: \"{p.Name}\" -> \"{traducao}\"");
            }
        }
        Conferir($"idiomas: nos {catalogos} catalogo(s), as traducoes guardam as marcas {{n}} da frase", problemas.Count == 0,
            string.Join(" | ", problemas.Take(5)));
    }

    /// <summary>Os itens de um menu, com os dos submenus.</summary>
    static IEnumerable<System.Windows.Controls.MenuItem> Itens(System.Windows.Controls.ItemCollection itens)
    {
        foreach (var item in itens.OfType<System.Windows.Controls.MenuItem>())
        {
            yield return item;
            foreach (var sub in Itens(item.Items)) yield return sub;
        }
    }
}
