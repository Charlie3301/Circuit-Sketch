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
            wokwi.Categoria == "Importação e exportação" && wokwi.Autor.Length > 0 && wokwi.Arquivos.SequenceEqual([System.IO.Path.Combine("wokwi", "wokwi.cs")])
            && wokwi.Instalado is { } inst && inst.Programa == Plugins.VersaoDoPrograma && inst.Versao == wokwi.Versao
            && Plugins.Categorias.Contains("Importação e exportação"),
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
            Conferir("sem o plugin, os pinos do Wokwi tambem nao viram os daqui (o ENABLE do A4988, o 4 da DevKitC)",
                FormatoWokwi.PinoDoWokwi("a4988", "ENABLE") is null && FormatoWokwi.PinoDoWokwi("esp32-devkit-v1", "4") is null
                && FormatoWokwi.PinoDoWokwi("a4988", "EN") == "EN",
                $"{FormatoWokwi.PinoDoWokwi("a4988", "ENABLE")} {FormatoWokwi.PinoDoWokwi("esp32-devkit-v1", "4")}");
            Conferir("sem o plugin, o Exportar nao oferece o formato do Wokwi", Plugins.Exportadores.Count == 0,
                string.Join(",", Plugins.Exportadores.Select(p => p.Formato)));

            // ---- a exportacao para o Wokwi, e a volta ----
            Plugins.Ligar("wokwi", true);
            Conferir("com o plugin, os pinos do Wokwi viram os daqui (ENABLE -> EN, 4 -> D4, GND.1 da Pico -> GND)",
                FormatoWokwi.PinoDoWokwi("a4988", "ENABLE") == "EN" && FormatoWokwi.PinoDoWokwi("esp32-devkit-v1", "4") == "D4"
                && FormatoWokwi.PinoDoWokwi("pi-pico", "GND.1") == "GND",
                $"{FormatoWokwi.PinoDoWokwi("a4988", "ENABLE")} {FormatoWokwi.PinoDoWokwi("esp32-devkit-v1", "4")} {FormatoWokwi.PinoDoWokwi("pi-pico", "GND.1")}");

            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "esp", Tipo = "esp32-s3-devkit", X = 0, Y = 0 },
                new PecaNoCircuito { Id = "drv", Tipo = "a4988", X = 300, Y = 0 },
                new PecaNoCircuito { Id = "lcd", Tipo = "lcd1602-i2c", X = 500, Y = 0 },
                new PecaNoCircuito { Id = "led1", Tipo = "led", X = 300, Y = 200 },
                new PecaNoCircuito { Id = "m1", Tipo = "motor-dc", X = 500, Y = 300 },
            ]);
            c.Fios.Add(new Fio { DePeca = "drv", DePino = "EN", ParaPeca = "esp", ParaPino = "EN" });
            c.Fios.Add(new Fio { DePeca = "led1", DePino = "A", ParaPeca = "esp", ParaPino = "4" });
            var formato = Plugins.Exportadores.FirstOrDefault(p => p.Id == "wokwi");
            string deles = ExportarWokwi.Gerar(c, "teste", "eu", formato);
            string daqui = ExportarWokwi.Gerar(c, "teste", "eu");
            Conferir("exportar para o Wokwi: as pecas com os nomes de la (board-, wokwi-, o LCD com pins=i2c, circuitsketch- no que eles nao tem) e os pinos de la",
                formato?.Formato == "Wokwi" && deles.Contains("\"board-esp32-s3-devkitc-1\"") && deles.Contains("\"wokwi-a4988\"")
                && deles.Contains("\"wokwi-lcd1602\"") && deles.Contains("\"pins\": \"i2c\"") && deles.Contains("\"wokwi-led\"")
                && deles.Contains("\"circuitsketch-motor-dc\"") && deles.Contains("\"drv:ENABLE\"") && deles.Contains("\"esp:RST\""),
                deles.Length > 700 ? deles[..700] : deles);
            Conferir("o Exportar normal continua com os nomes daqui",
                !daqui.Contains("wokwi-") && !daqui.Contains("board-") && daqui.Contains("\"lcd1602-i2c\"") && daqui.Contains("\"drv:EN\""),
                daqui.Length > 400 ? daqui[..400] : daqui);
            var volta = ImportarWokwi.Ler(deles);
            Conferir("o que foi para o Wokwi volta igual: as pecas, o LCD com modulo e os fios nos mesmos pinos",
                volta.Ignorados.Count == 0 && volta.FiosPerdidos.Count == 0
                && c.Pecas.All(p => volta.Circuito.Achar(p.Id)?.Tipo == p.Tipo)
                && volta.Circuito.Fios.Any(f => f.DePeca == "drv" && f.DePino == "EN" && f.ParaPino == "EN"),
                Tipos(volta) + " | perdidos: " + string.Join(";", volta.FiosPerdidos));
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
            && bom.TipoImportado("xyz-led", null, t => t == "led") == "led",
            $"{semNome.Erro} | {bom.Erro} {bom.Nome} {bom.Versao}");
    }
}
