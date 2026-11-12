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

    /// <summary>
    /// A tela em ingles, varrida inteira: a janela em cada modo, a barra de
    /// cada peca do catalogo, o menu, cada pagina das configuracoes e as
    /// janelas de apoio. O que ainda tiver cara de portugues (acento ou
    /// palavra que so o portugues usa) sai na lista -- e a frase que ficou
    /// sem marcar no codigo, ou sem traduzir no catalogo.
    /// </summary>
    static void Idiomas_SobraDePortugues()
    {
        // As tabelas fixas (cores, atalhos, temas...) guardam a lingua de
        // quando nasceram, e depois dos outros blocos ja nasceram em
        // portugues. Entao a varredura roda num processo so dela para cada
        // idioma completo, que ja comeca nele (--idioma en, --idioma es...),
        // e aqui so se traz o resultado.
        if (Idioma.Atual == Idioma.Original)
        {
            foreach (var (outro, _) in Idioma.Disponiveis(soCompletos: true).Where(d => d.Codigo != Idioma.Original))
            {
                saida.AppendLine($"idiomas: o que sobra em portugues com a tela em {outro}");
                string relatorio = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"circuitsketch-idiomas-{outro}-{Environment.ProcessId}.txt");
                bool terminou = false;
                int codigo = -1;
                try
                {
                    System.IO.File.Delete(relatorio);
                    using var filho = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,
                        ["--teste", "--idioma", outro, "--so", "Idiomas_SobraDePortugues", "--saida", relatorio]) { UseShellExecute = false });
                    terminou = filho!.WaitForExit(600_000);
                    if (!terminou) filho.Kill(true);
                    else codigo = filho.ExitCode;
                    string[] linhas = System.IO.File.Exists(relatorio) ? System.IO.File.ReadAllLines(relatorio) : [];
                    foreach (string linha in linhas.Where(l => l.Contains("sobra: ", StringComparison.Ordinal) || l.Contains("FALHA", StringComparison.Ordinal)))
                        saida.AppendLine(linha);
                }
                finally { try { System.IO.File.Delete(relatorio); } catch (System.IO.IOException) { } }
                Conferir($"idiomas: a varredura em {outro} (num processo so dela) nao achou portugues sobrando",
                    terminou && codigo == 0, terminou ? $"saiu com {codigo}" : "nao terminou em 10 minutos");
            }
            return;
        }

        string lingua = Idioma.Atual;
        saida.AppendLine($"idiomas: o que sobra em portugues com a tela em {lingua}");
        var achados = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var janelas = new List<System.Windows.Window>();
        Idioma.Usar(lingua);
        try
        {
            var janela = new MainWindow
            {
                ShowActivated = false, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000
            };
            janela.Show();
            janelas.Add(janela);
            void Olhar(string onde, object raiz) => VarrerPortugues(raiz, onde, achados);

            foreach (string modo in new[] { "simulacao", "circuito", "componentes", "descricao", "simulacao" })
            {
                janela.MostrarModo(modo);
                Olhar("janela, modo " + modo, janela);
            }

            janela.TesteAbrirDemo("esp32-semaforo");
            Olhar("demo", janela);
            var menu = janela.TesteAbrirMenuDeOpcoes();
            Olhar("menu", menu);
            menu.IsOpen = false;
            var daLigacao = janela.TesteMenuDaLigacao(0);
            if (daLigacao is not null) { Olhar("menu da ligacao", daLigacao); daLigacao.IsOpen = false; }

            // Uma peca de cada tipo, cada uma selecionada: a barra de cima e a lista de componentes.
            var circuito = new Circuito();
            int i = 0;
            foreach (var def in Catalogo.Todas.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                circuito.Pecas.Add(new PecaNoCircuito { Id = "p" + i, Tipo = def.Id, X = i % 16 * 400, Y = i / 16 * 400 });
                i++;
            }
            janela.TesteAbrir(circuito, "void setup() {}\nvoid loop() {}\n");
            janela.MostrarModo("componentes");
            Olhar("lista de componentes", janela.listaDeComponentes);
            janela.MostrarModo("simulacao");
            foreach (var peca in circuito.Pecas)
            {
                janela.editor.SelecionarPorId(peca.Id);
                Olhar("barra: " + peca.Tipo, janela.barraObjeto);
            }
            janela.editor.LimparSelecao();
            var doCircuito = janela.TesteMenuDoCircuito();
            if (doCircuito is not null) { Olhar("menu do circuito", doCircuito); doCircuito.IsOpen = false; }

            var config = janela.TesteCriarConfiguracoes();
            janelas.Add(config);
            foreach (string assunto in config.AssuntosParaTeste)
            {
                config.AbrirParaTeste(assunto);
                Olhar("configuracoes: " + assunto, config);
            }

            void Janela(string onde, Func<System.Windows.Window> criar)
            {
                try
                {
                    var j = criar();
                    if (j is null) return;
                    janelas.Add(j);
                    j.Measure(new System.Windows.Size(1200, 800));
                    Olhar(onde, j);
                }
                catch (Exception e) { achados.TryAdd("(a janela nao abriu: " + e.Message + ")", onde); }
            }
            Janela("paleta", () => new JanelaPecas(janela) { ShowActivated = false });
            Janela("abertura", () => new JanelaAbertura(janela));
            Janela("atalhos", () => new JanelaAtalhos(janela));
            Janela("bibliotecas", () => new JanelaBibliotecas(null, "#include <Inexistente.h>\nvoid setup() {}\nvoid loop() {}\n"));
            Janela("faltando", () => new JanelaFaltando(janela, [new Faltando("Inexistente.h", null, 0, 0)], Linguagem.Arduino));
            Janela("cartao", () => new JanelaCartao(janela, new Dictionary<string, string> { ["leia.txt"] = "x" }, false));
            Janela("pendrive", () => new JanelaCartao(janela, new Dictionary<string, string> { ["leia.txt"] = "x" }, true, titulo: Idioma.N("Pendrive USB")));
            Janela("celular", () => janela.TesteAbrirCelular());
            string analisador = circuito.Pecas.FirstOrDefault(p => p.Tipo == "logic-analyzer")?.Id;
            if (analisador is not null) Janela("analisador", () => janela.TesteAbrirAnalisador(analisador));

            MotorTraduzido(lingua);
        }
        finally
        {
            Idioma.Usar(Idioma.Original);
            foreach (var j in Enumerable.Reverse(janelas))
                try { j.Close(); } catch (InvalidOperationException) { }
        }

        // O nome de cada idioma vai na lingua dele: "Português (Brasil)" na lista e certo.
        foreach (var (_, nome) in Idioma.Disponiveis()) achados.Remove(nome);

        foreach (var (texto, onde) in achados)
            saida.AppendLine($"        sobra: \"{(texto.Length > 140 ? texto[..140] + "..." : texto)}\"  ({onde})");
        Conferir($"idiomas: com a tela em {lingua}, nenhum texto com cara de portugues", achados.Count == 0,
            $"{achados.Count} texto(s), na lista acima");
    }

    /// <summary>
    /// O motor no idioma da varredura (roda no processo dela, que ja nasceu
    /// nele): os erros do interpretador saem traduzidos, o tipo da excecao do
    /// Python -- que sai das palavras do erro -- continua o mesmo, e o caminho
    /// de volta (Idioma.Desfazer) acha o portugues de cada frase do catalogo.
    /// </summary>
    static void MotorTraduzido(string lingua)
    {
        // Cada except so pega se o tipo bater; o que nao bate cai no "outro".
        string python = """
            for n in range(5):
                try:
                    if n == 0:
                        x = 1 / 0
                    elif n == 1:
                        v = [1, 2, 3]
                        print(v[7])
                    elif n == 2:
                        d = {"a": 1}
                        print(d["b"])
                    elif n == 3:
                        int("abc")
                    else:
                        print(nao_existe)
                except ZeroDivisionError as e:
                    print("ZeroDivisionError:", e)
                except IndexError as e:
                    print("IndexError:", e)
                except KeyError as e:
                    print("KeyError:", e)
                except ValueError as e:
                    print("ValueError:", e)
                except NameError as e:
                    print("NameError:", e)
                except Exception as e:
                    print("outro:", e)
            """;
        string serial;
        try { serial = RodarPython(python).Serial; }
        catch (Exception e) { serial = "(nao rodou: " + e.Message + ")"; }
        Conferir($"idiomas: em {lingua} o except do Python pega cada erro do simulador pelo tipo (divisao, indice, chave, valor, nome)",
            serial.Contains("ZeroDivisionError: " + Idioma.T("divisao por zero")) && serial.Contains("IndexError: ") && serial.Contains("KeyError: ")
            && serial.Contains("ValueError: ") && serial.Contains("NameError: ") && !serial.Contains("outro:"), serial);
        Conferir($"idiomas: em {lingua} a mensagem das excecoes do Python sai traduzida",
            serial.Split('|').All(l => !PareceComPortugues(l)), serial);

        // Os erros do C++ do interpretador (de sintaxe e de execucao), com o "line N:" da frente.
        static string Erro(string corpo)
        {
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                var interp = new Interpretador(new Parser(Lexer.Tokenizar(
                    "int v[3];\nvoid setup() {\n" + corpo + "\n}\nvoid loop() {}\n")).AnalisarPrograma(), maq);
                interp.Preparar();
                interp.Setup();
                return string.Join(" ", maq.Avisos);
            }
            catch (Exception e) { return e.Message; }
        }
        var erros = new[]
        {
            Erro("float f = 1.5 % 2.0;"), Erro("v[7] = 1;"), Erro("x = 3;"), Erro("int a = 1 @ 2;"),
            Erro("ArduinoOTA.begin();"), Erro("Serial.voar();"), Erro("int m[2][2] = {{1, 2, 3}};"),
        };
        var emPortugues = erros.Where(PareceComPortugues).ToList();
        string linha = Idioma.T("linha {0}", "");
        Conferir($"idiomas: em {lingua} os erros do C++ do interpretador saem traduzidos",
            emPortugues.Count == 0 && erros.All(e => !string.IsNullOrEmpty(e)) && erros.Any(e => e.StartsWith(linha, StringComparison.Ordinal)),
            string.Join(" | ", emPortugues.Count > 0 ? emPortugues : erros));

        // O caminho de volta, frase por frase: a traducao montada com valores
        // volta para o portugues montado com os mesmos valores. Frases que
        // duas chaves traduzem igual nao tem volta unica e ficam de fora.
        var assembly = typeof(Idioma).Assembly;
        using var doc = JsonDocument.Parse(assembly.GetManifestResourceStream($"idiomas/{lingua}.json")!);
        var pares = doc.RootElement.GetProperty("textos").EnumerateObject()
            .Select(p => (Pt: p.Name, En: p.Value.GetString())).Where(p => !string.IsNullOrEmpty(p.En)).ToList();
        var repetidas = pares.GroupBy(p => p.En).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var marca = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
        var semVolta = new List<string>();
        int conferidas = 0;
        foreach (var (pt, en) in pares)
        {
            // Duas marcas coladas ("{2}{2}") nao tem fronteira: o Desfazer nao tenta.
            if (repetidas.Contains(en) || !marca.IsMatch(pt) || Regex.IsMatch(en, @"\}\{\d")) continue;
            int quantos = marca.Matches(pt).Max(m => int.Parse(m.Groups[1].Value)) + 1;
            var valores = Enumerable.Range(0, quantos).Select(i => (object)$"V{i}x").ToArray();
            string montada, esperada;
            try { montada = string.Format(en, valores); esperada = string.Format(pt, valores); }
            catch (FormatException) { semVolta.Add("(formato) " + pt); continue; }
            // A que e quase so marca ("{0}: {1}") casaria com qualquer coisa: o Desfazer nem tenta.
            if (marca.Replace(en, "").Replace("{{", "{").Replace("}}", "}").Trim().Length < 4) continue;
            conferidas++;
            if (Idioma.Desfazer(montada) != esperada) semVolta.Add($"{montada} -> {Idioma.Desfazer(montada)}");
        }
        Conferir($"idiomas: Idioma.Desfazer devolve o portugues das {conferidas} frases com valores do catalogo {lingua}",
            conferidas > 500 && semVolta.Count == 0, string.Join(" | ", semVolta.Take(6)));
    }

    static readonly HashSet<string> SoDoPortugues = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "da", "das", "dos", "e", "o", "os", "um", "uma", "uns", "umas", "em", "na", "nas", "nos",
        "para", "pra", "com", "sem", "que", "não", "nao", "pelo", "pela", "ao", "aos", "à", "mais", "ou", "seu", "sua",
        "esta", "este", "isso", "isto", "aqui", "quando", "como", "porque", "também", "já", "só", "ainda", "cada",
        "peça", "peças", "fio", "fios", "placa", "pino", "pinos", "ligado", "ligada", "desligado", "abrir", "fechar",
    };

    /// <summary>
    /// Palavras curtas que o portugues divide com o espanhol ("de", "que",
    /// "para", "placa"...): numa tela em espanhol elas sao do espanhol.
    /// </summary>
    static readonly HashSet<string> TambemDoEspanhol = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "da", "das", "dos", "e", "o", "nos", "para", "que", "esta", "este", "como", "porque", "cada", "placa", "abrir", "pelo",
    };

    static readonly Regex AcentoDoPortugues = new("[ãõáàâéêíóôúüçÃÕÁÀÂÉÊÍÓÔÚÇ]");
    // O espanhol tambem tem á, é, í, ó, ú e ü: so o til do a e do o, o circunflexo, a crase e o ç sao do portugues.
    static readonly Regex AcentoSoDoPortugues = new("[ãõàâêôçÃÕÀÂÊÔÇ]");
    static readonly Regex Palavra = new(@"\p{L}{3,}");

    /// <summary>
    /// As palavras do portugues da tela: as que aparecem nas frases originais
    /// do catalogo do idioma atual e em nenhuma traducao dele. E o que pega
    /// "Desfazer" ou "Solto", sem acento e sem palavra de ligacao.
    /// </summary>
    static readonly Dictionary<string, HashSet<string>> VocabularioDoPortugues = [];

    static HashSet<string> Vocabulario(string lingua)
    {
        lock (VocabularioDoPortugues)
        {
            if (VocabularioDoPortugues.TryGetValue(lingua, out var pronto)) return pronto;
            var assembly = typeof(Idioma).Assembly;
            using var doc = JsonDocument.Parse(assembly.GetManifestResourceStream($"idiomas/{lingua}.json")!);
            var originais = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var traduzidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.GetProperty("textos").EnumerateObject())
            {
                foreach (Match m in Palavra.Matches(p.Name)) originais.Add(m.Value);
                foreach (Match m in Palavra.Matches(p.Value.GetString() ?? "")) traduzidas.Add(m.Value);
            }
            originais.ExceptWith(traduzidas);
            return VocabularioDoPortugues[lingua] = originais;
        }
    }

    /// <summary>Um texto que parece portugues: tem acento, ou uma palavra que so o portugues usa.</summary>
    static bool PareceComPortugues(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        string lingua = Idioma.Atual == Idioma.Original ? "en" : Idioma.Atual;
        bool espanhol = lingua.StartsWith("es", StringComparison.Ordinal);
        if ((espanhol ? AcentoSoDoPortugues : AcentoDoPortugues).IsMatch(texto)) return true;
        var vocabulario = Vocabulario(lingua);
        foreach (string pedaco in texto.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Em maiusculas e sigla (COM, OS), nao palavra. Com numero, ponto ou
            // sublinhado no meio e modelo ou nome de arquivo (Nucleo-C031C6,
            // Adafruit_BMP280.h): fica como e em qualquer lingua.
            string palavra = pedaco.Trim('.', ',', ':', ';', '(', ')', '"', '\'', '!', '?', '…', '·', '—', '-');
            if (palavra == palavra.ToUpperInvariant() || palavra.Any(c => char.IsDigit(c) || c is '.' or '_')) continue;
            if (SoDoPortugues.Contains(palavra) && !(espanhol && TambemDoEspanhol.Contains(palavra))) return true;
            foreach (Match m in Palavra.Matches(palavra))
                if (vocabulario.Contains(m.Value)) return true;
        }
        return false;
    }

    /// <summary>
    /// Os textos que a pessoa le: TextBlock, o Content e o Header de texto,
    /// as dicas, os menus de contexto e o titulo da janela. O que a pessoa (ou
    /// o sketch) escreveu -- campos, editor de codigo, saidas dos paineis --
    /// fica de fora.
    /// </summary>
    static void VarrerPortugues(object o, string onde, IDictionary<string, string> achados)
    {
        void Anotar(string texto)
        {
            if (texto is null) return;
            texto = texto.Trim();
            if (PareceComPortugues(texto)) achados.TryAdd(texto, onde);
        }

        switch (o)
        {
            case null: return;
            case string s: Anotar(s); return;
            case System.Windows.Controls.Primitives.TextBoxBase:
            case System.Windows.Controls.PasswordBox:
            case ICSharpCode.AvalonEdit.TextEditor:
                return;
            case System.Windows.FrameworkElement { Name: "txtSerial" or "txtConsole" or "txtRede" or "txtBluetooth" or "txtPlaca"
                or "txtVariaveis" or "txtNomeDoCircuito" or "txtAutorDoCircuito" or "txtPlacaDoCodigo" }:
                return;
        }

        if (o is System.Windows.Window w) Anotar(w.Title);
        if (o is System.Windows.Controls.TextBlock bloco)
        {
            if (bloco.Inlines.Count > 0)
            {
                var texto = new System.Text.StringBuilder();
                void Juntar(System.Windows.Documents.Inline inline)
                {
                    switch (inline)
                    {
                        case System.Windows.Documents.Run run: texto.Append(run.Text); break;
                        case System.Windows.Documents.LineBreak: texto.Append(' '); break;
                        case System.Windows.Documents.Span span: foreach (var filho in span.Inlines) Juntar(filho); break;
                        case System.Windows.Documents.InlineUIContainer ui: VarrerPortugues(ui.Child, onde, achados); break;
                    }
                }
                foreach (var inline in bloco.Inlines) Juntar(inline);
                Anotar(texto.ToString());
            }
            else Anotar(bloco.Text);
        }
        if (o is System.Windows.Controls.ContentControl { Content: string conteudo }) Anotar(conteudo);
        if (o is System.Windows.Controls.HeaderedContentControl { Header: string cabecalho }) Anotar(cabecalho);
        if (o is System.Windows.Controls.HeaderedItemsControl { Header: string titulo }) Anotar(titulo);
        if (o is System.Windows.FrameworkElement fe)
        {
            if (fe.ToolTip is string dica) Anotar(dica);
            else if (fe.ToolTip is System.Windows.DependencyObject dicaMontada) VarrerPortugues(dicaMontada, onde, achados);
            if (fe.ContextMenu is not null) VarrerPortugues(fe.ContextMenu, onde, achados);
        }
        if (o is System.Windows.Controls.ItemsControl lista)
            foreach (object item in lista.Items)
                if (item is string itemDeTexto) Anotar(itemDeTexto);
        if (o is System.Windows.DependencyObject d)
            foreach (object filho in System.Windows.LogicalTreeHelper.GetChildren(d))
                if (filho is not string) VarrerPortugues(filho, onde, achados);
    }

    static IEnumerable<System.Windows.Controls.MenuItem> Itens(System.Windows.Controls.ItemCollection itens)
    {
        foreach (var item in itens.OfType<System.Windows.Controls.MenuItem>())
        {
            yield return item;
            foreach (var sub in Itens(item.Items)) yield return sub;
        }
    }
}
