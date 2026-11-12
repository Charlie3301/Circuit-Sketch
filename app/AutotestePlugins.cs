namespace Simulador.App;

/// <summary>
/// Os plugins: a pasta "plugins" compilada pelo programa, o liga/desliga, um plugin
/// quebrado que nao atrapalha, e o primeiro deles -- o do Wokwi, que le os nomes
/// das pecas dos diagram.json deles (sem ele, so os nomes daqui abrem).
/// </summary>
public static partial class Autoteste
{
    static void Plugins_Wokwi()
    {
        saida.AppendLine("plugins: o do Wokwi");

        var wokwi = Plugins.Todos.FirstOrDefault(p => p.Id == "wokwi");
        Conferir("plugins: o do Wokwi vem na pasta plugins, compila e vem ligado",
            wokwi is { Erro: null, Nome: "Wokwi", Ligado: true } && wokwi.Versao.Length > 0 && wokwi.Descricao.Length > 0,
            wokwi is null ? $"(sem ele em {Plugins.Pasta})" : wokwi.Erro ?? $"ligado={wokwi.Ligado}");
        if (wokwi is null) return;
        Plugins.RegistrarInstalados();
        Conferir("plugins: o bloco do Wokwi tem categoria, autor, o arquivo e a versao do programa em que foi instalado",
            wokwi.Categoria == "Importação" && wokwi.Autor.Length > 0 && wokwi.Arquivos.SequenceEqual([System.IO.Path.Combine("wokwi", "wokwi.cs")])
            && wokwi.Instalado is { } inst && inst.Programa == Plugins.VersaoDoPrograma && inst.Versao == wokwi.Versao
            && Plugins.Categorias.Contains("Importação"),
            $"{wokwi.Categoria} | {wokwi.Autor} | {string.Join(",", wokwi.Arquivos)} | {wokwi.Instalado}");

        const string diagrama = """
            {
              "version": 1,
              "author": "teste",
              "editor": "wokwi",
              "parts": [
                { "type": "board-esp32-devkit-c-v4", "id": "esp", "top": 0, "left": 0, "attrs": {} },
                { "type": "wokwi-led", "id": "led1", "top": 0, "left": 200, "attrs": { "color": "red" } },
                { "type": "wokwi-breadboard-half", "id": "bb1", "top": 200, "left": 0, "attrs": {} },
                { "type": "board-esp32-s3-devkitc-1", "id": "s3", "top": 0, "left": 400, "attrs": {} },
                { "type": "wokwi-gate-and-2", "id": "e1", "top": 0, "left": 600, "attrs": {} },
                { "type": "led", "id": "led2", "top": 100, "left": 200, "attrs": {} },
                { "type": "circuitsketch-ds18b20", "id": "ds1", "top": 100, "left": 300, "attrs": {} }
              ],
              "connections": [ [ "led1:A", "esp:D4", "green", [] ], [ "led1:C", "esp:GND.1", "black", [] ] ]
            }
            """;
        string Tipos(ImportarWokwi.Resultado r) => string.Join(",", r.Circuito.Pecas.OrderBy(p => p.Id).Select(p => $"{p.Id}={p.Tipo}"));

        bool era = wokwi.Ligado;
        try
        {
            Plugins.Ligar("wokwi", true);
            var r = ImportarWokwi.Ler(diagrama);
            var porId = r.Circuito.Pecas.ToDictionary(p => p.Id, p => p.Tipo);
            Conferir("plugin do Wokwi: as pecas deles abrem com os nomes daqui (wokwi-led e led, board-esp32-s3-devkitc-1 e esp32-s3-devkit...), sem o prefixo",
                r.Ignorados.Count == 0 && porId.GetValueOrDefault("esp") == "esp32-devkit-v1" && porId.GetValueOrDefault("led1") == "led"
                && porId.GetValueOrDefault("bb1") == Catalogo.IdProtoboard && porId.GetValueOrDefault("s3") == "esp32-s3-devkit"
                && porId.GetValueOrDefault("e1") == "and-gate" && porId.GetValueOrDefault("led2") == "led"
                && porId.GetValueOrDefault("ds1") == "ds18b20" && r.Circuito.Pecas.All(p => !p.Tipo.StartsWith("wokwi-") && !p.Tipo.StartsWith("board-")),
                Tipos(r) + " | ignorados: " + string.Join(",", r.Ignorados));
            Conferir("plugin do Wokwi: os fios do diagrama ligam nas pecas traduzidas",
                r.Circuito.Fios.Count == 2 && r.FiosPerdidos.Count == 0, $"{r.Circuito.Fios.Count} fios, perdidos: {string.Join(";", r.FiosPerdidos)}");

            Plugins.Ligar("wokwi", false);
            var sem = ImportarWokwi.Ler(diagrama);
            Conferir("sem o plugin do Wokwi, as pecas dele nao abrem; os nomes daqui e o circuitsketch- continuam abrindo",
                !Plugins.Todos.First(p => p.Id == "wokwi").Ligado
                && sem.Ignorados.Count == 5 && sem.Ignorados.All(t => t.StartsWith("wokwi-") || t.StartsWith("board-"))
                && sem.Circuito.Pecas.Select(p => p.Id).Order().SequenceEqual(["ds1", "led2"]),
                Tipos(sem) + " | ignorados: " + string.Join(",", sem.Ignorados));
        }
        finally { Plugins.Ligar("wokwi", era); }

        // Um plugin que nao compila aparece com o erro, e um que lanca excecao nao derruba a importacao.
        var quebrado = Plugins.Compilar("quebrado", [("quebrado.cs", "public static class P { public const string Nome = \"Q\"; int x = ; }")]);
        Conferir("plugins: um que nao compila fica na lista com o erro (arquivo e linha), sem derrubar o resto",
            quebrado.Erro is { } erro && erro.Contains("quebrado.cs(1)"), quebrado.Erro ?? "(compilou)");
        var semNome = Plugins.Compilar("sem-nome", [("a.cs", "public static class P { }")]);
        var bom = Plugins.Compilar("bom", [("b.cs", """
            using System;
            public static class P
            {
                public const string Nome = "Bom", Descricao = "d", Versao = "2.1";
                public static string TipoImportado(string tipo, Func<string, bool> existe)
                    => tipo == "xyz-led" && existe("led") ? "led" : throw new InvalidOperationException();
            }
            """)]);
        Conferir("plugins: a classe e achada pelo Nome, o gancho pela assinatura, e sem o Nome o erro diz o que falta",
            semNome.Erro is not null && bom is { Erro: null, Nome: "Bom", Versao: "2.1" } && bom.TipoImportado is not null
            && bom.TipoImportado("xyz-led", t => t == "led") == "led",
            $"{semNome.Erro} | {bom.Erro} {bom.Nome} {bom.Versao}");
    }
}
