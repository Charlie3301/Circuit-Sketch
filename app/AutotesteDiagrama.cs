namespace Simulador.App;

/// <summary>
/// O diagram.json no formato do Wokwi (Alpha 24): o arquivo escrito como o
/// editor deles escreve, com nome, autor e editor; as pecas de la e as daqui;
/// a ida e volta sem perder nada; e o caminho do fio com o "*".
/// </summary>
public static partial class Autoteste
{
    static void Diagrama_FormatoWokwi()
    {
        saida.AppendLine("diagram.json no formato do Wokwi");

        // ---- os ids das pecas novas, no jeito deles ----
        {
            var ed = new EditorCircuito();
            ed.Carregar(new Circuito());
            string[] ids = [ed.Adicionar("esp32-devkit-v1").Id, ed.Adicionar("led").Id, ed.Adicionar("led").Id,
                            ed.Adicionar("resistor").Id, ed.Adicionar("pushbutton").Id, ed.Adicionar("motor-dc").Id];
            saida.AppendLine("        ids das pecas novas: " + string.Join(", ", ids));
            Conferir("ids: as pecas novas nascem com id legivel (esp1, led1, led2, r1, btn1, motor1)",
                ids.SequenceEqual(["esp1", "led1", "led2", "r1", "btn1", "motor1"]), string.Join(", ", ids));
        }

        // ---- o arquivo: cabecalho, linhas e ligacoes como o editor deles ----
        var c = new Circuito { Nome = "Semaforo da turma", Autor = "Prof. Joao" };
        var placa = new PecaNoCircuito { Id = "esp1", Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var led = new PecaNoCircuito { Id = "led1", Tipo = "led", X = 300, Y = 40, Cor = "verde" };
        var r = new PecaNoCircuito { Id = "r1", Tipo = "resistor", X = 220, Y = 60, Ohms = 330, Rotacao = 90 };
        var motor = new PecaNoCircuito
        {
            Id = "motor1", Tipo = "motor-brushless", X = 500, Y = 100, Modelo = "personalizado",
            Ficha = new FichaPersonalizada
            {
                Volts = 14.8, RpmOuKv = 2300, Resistencia = 0.0746, CorrenteVazio = 1.04, CorrenteMaxima = 23,
                Inercia = 2.2e-6, MedidaA = 22, MedidaB = 5, ParesDePolos = 7,
            },
        };
        var esc = new PecaNoCircuito { Id = "esc1", Tipo = "esc", X = 400, Y = 300, Amperes = 40, Acabamento = "roxo" };
        var bat = new PecaNoCircuito { Id = "bat1", Tipo = "bateria", X = 600, Y = 300, Volts = 14.8, MiliAmperes = 60000, MiliAmperesHora = 1500, Nome = "LiPo 4S" };
        var pot = new PecaNoCircuito { Id = "pot1", Tipo = "potentiometer", X = 100, Y = 400, Giro = 0.75 };
        var nota = new PecaNoCircuito { Id = "txt1", Tipo = "texto", X = 50, Y = -80, Nome = "Liga o motor" };
        c.Pecas.AddRange([placa, led, r, motor, esc, bat, pot, nota]);
        c.Fios.Add(new Fio { DePeca = "esp1", DePino = "D2", ParaPeca = "r1", ParaPino = "1", Cor = "#D6202A" });
        c.Fios.Add(new Fio { DePeca = "r1", DePino = "2", ParaPeca = "led1", ParaPino = "A", Cor = "#123456" });
        var dobrado = new Fio { DePeca = "led1", DePino = "C", ParaPeca = "esp1", ParaPino = "GND.1", Cor = "#000000", Estilo = Fio.EstiloReto };
        dobrado.Dobras.Add(new PontoFio { X = 320, Y = 200 });
        c.Fios.Add(dobrado);

        string json = ExportarWokwi.Gerar(c);
        var linhas = json.Split('\n');
        saida.AppendLine("        comeco: " + string.Join(" | ", linhas.Take(6).Select(l => l.Trim())));
        Conferir("formato: version, name, author e editor no comeco, como o Wokwi grava",
            linhas[1] == "  \"version\": 1," && linhas[2] == "  \"name\": \"Semaforo da turma\","
            && linhas[3] == "  \"author\": \"Prof. Joao\"," && linhas[4] == "  \"editor\": \"circuitsketch\","
            && linhas[5] == "  \"parts\": [", string.Join(" | ", linhas.Take(6)));
        Conferir("formato: a peca curta fica numa linha so, com espaco dentro das chaves",
            linhas.Any(l => l == "    { \"type\": \"led\", \"id\": \"led1\", \"top\": 40, \"left\": 300, \"attrs\": { \"color\": \"green\" } },"),
            string.Join(" | ", linhas.Where(l => l.Contains("led1")).Take(2)));
        Conferir("formato: nenhuma linha de peca passa da largura do editor deles",
            linhas.All(l => l.Length <= FormatoWokwi.Largura + 1), linhas.Max(l => l.Length).ToString());
        Conferir("formato: a peca comprida abre um campo por linha",
            json.Contains("    {\n      \"type\": \"motor-brushless\",\n      \"id\": \"motor1\","), "motor");
        Conferir("formato: a ligacao e [de, para, cor, caminho], uma por linha, com a cor pelo nome",
            json.Contains("    [ \"esp1:D2\", \"r1:1\", \"red\", [] ]"), string.Join(" | ", linhas.Where(l => l.Contains("esp1:D2"))));
        Conferir("formato: cor fora da paleta deles vai em hexadecimal (e volta igual)",
            json.Contains("\"#123456\""), "cor");
        Conferir("formato: termina com as dependencias, como os projetos novos do Wokwi",
            json.TrimEnd().EndsWith("\"dependencies\": {}\n}".TrimEnd()), json[^40..]);

        // ---- a volta: tudo o que e nosso volta igual ----
        var volta = ImportarWokwi.Ler(json);
        var v = volta.Circuito;
        saida.AppendLine($"        volta: {v.Pecas.Count} pecas, {v.Fios.Count} fios, {volta.Ignorados.Count} fora, nome '{v.Nome}', autor '{v.Autor}'");
        Conferir("volta: nome e autor do arquivo vem junto", v.Nome == "Semaforo da turma" && v.Autor == "Prof. Joao", $"{v.Nome}/{v.Autor}");
        Conferir("volta: as pecas (as do Wokwi e as so daqui) voltam com os mesmos ids",
            v.Pecas.Select(p => p.Id).SequenceEqual(c.Pecas.Select(p => p.Id)) && volta.Ignorados.Count == 0,
            string.Join(", ", v.Pecas.Select(p => p.Id)));
        var m2 = v.Achar("motor1");
        Conferir("volta: a ficha personalizada do motor volta inteira (KV, resistencia, medidas, inercia)",
            m2?.Modelo == "personalizado" && m2.Ficha is { RpmOuKv: 2300, MedidaA: 22, MedidaB: 5, ParesDePolos: 7 }
            && Math.Abs(m2.Ficha.Resistencia - 0.0746) < 1e-9 && Math.Abs(m2.Ficha.Inercia - 2.2e-6) < 1e-15,
            m2?.Ficha is null ? "sem ficha" : $"{m2.Ficha.RpmOuKv} {m2.Ficha.Resistencia} {m2.Ficha.Inercia}");
        var e2 = v.Achar("esc1");
        var b2 = v.Achar("bat1");
        Conferir("volta: a ESC (40 A, roxa) e a bateria (14,8 V, 60 A, 1500 mAh) voltam iguais",
            e2?.Amperes == 40 && e2.Acabamento == "roxo" && b2?.Volts == 14.8 && b2.MiliAmperes == 60000
            && b2.MiliAmperesHora == 1500 && b2.Nome == "LiPo 4S",
            $"{e2?.Amperes} {e2?.Acabamento} {b2?.Volts} {b2?.MiliAmperes} {b2?.MiliAmperesHora} {b2?.Nome}");
        Conferir("volta: o LED verde, o resistor de 330 girado, o potenciometro em 3/4 e o texto da anotacao",
            v.Achar("led1")?.Cor == "verde" && v.Achar("r1") is { Ohms: 330, Rotacao: 90 }
            && v.Achar("pot1")?.Giro == 0.75 && v.Achar("txt1")?.Nome == "Liga o motor", "atributos");
        Conferir("volta: os fios voltam, com a cor e o canto",
            v.Fios.Count == 3 && v.Fios.Any(f => f.Cor == "#123456")
            && v.Fios.Any(f => f.Dobras.Any(d => Math.Abs(d.X - 320) < 0.5 && Math.Abs(d.Y - 200) < 0.5)),
            $"{v.Fios.Count} fios");

        // ---- o "*" do caminho: depois dele, os passos saem do destino ----
        {
            string comEstrela = """
            {
              "version": 1, "author": "x", "editor": "wokwi",
              "parts": [
                { "type": "wokwi-led", "id": "a", "top": 0, "left": 0, "attrs": {} },
                { "type": "wokwi-led", "id": "b", "top": 0, "left": 200, "attrs": {} }
              ],
              "connections": [ [ "a:A", "b:C", "green", [ "v10", "h5", "*", "v-15", "h10" ] ] ]
            }
            """;
            var ce = ImportarWokwi.Ler(comEstrela).Circuito;
            var fio = ce.Fios[0];
            var defLed = Catalogo.Obter("led");
            var pa = Geometria.PosicaoPino(ce.Achar("a"), defLed, defLed.Pinos.First(p => p.Nome == "A"));
            var pb = Geometria.PosicaoPino(ce.Achar("b"), defLed, defLed.Pinos.First(p => p.Nome == "C"));
            var esperado = new[] { (pa.X, pa.Y + 10), (pa.X + 5, pa.Y + 10), (pb.X + 10, pb.Y - 15), (pb.X + 10, pb.Y) };
            bool bate = fio.Dobras.Count == 4 && fio.Dobras.Zip(esperado).All(z => Math.Abs(z.First.X - z.Second.Item1) < 0.01
                                                                                && Math.Abs(z.First.Y - z.Second.Item2) < 0.01);
            saida.AppendLine("        caminho com *: " + string.Join(" ", fio.Dobras.Select(d => $"({d.X - pa.X:0};{d.Y - pa.Y:0})")));
            Conferir("caminho: o que vem depois do '*' sai do pino de destino, de tras para a frente (como a documentacao do Wokwi)",
                bate, string.Join(" ", fio.Dobras.Select(d => $"({d.X:0.0};{d.Y:0.0})")));
        }

        // ---- a aba recusa o que apagaria o circuito ----
        {
            var desconhecida = ImportarWokwi.Ler("""{ "version": 1, "parts": [ { "type": "wokwi-reator-nuclear", "id": "x", "top": 0, "left": 0, "attrs": {} } ], "connections": [] }""", c, estrito: true);
            var repetido = ImportarWokwi.Ler("""{ "version": 1, "parts": [ { "type": "wokwi-led", "id": "a", "top": 0, "left": 0, "attrs": {} }, { "type": "wokwi-led", "id": "a", "top": 9, "left": 9, "attrs": {} } ], "connections": [] }""", c, estrito: true);
            var solto = ImportarWokwi.Ler("""{ "version": 1, "parts": [ { "type": "wokwi-reator-nuclear", "id": "x", "top": 0, "left": 0, "attrs": {} } ], "connections": [] }""");
            Conferir("aba: peca que nao existe recusa o diagrama, dizendo qual",
                desconhecida.Erro?.Contains("wokwi-reator-nuclear") == true, desconhecida.Erro ?? "(aceitou)");
            Conferir("aba: id repetido recusa o diagrama", repetido.Erro?.Contains("mais de uma") == true, repetido.Erro ?? "(aceitou)");
            Conferir("importar (fora da aba): a peca desconhecida fica de fora e e listada",
                solto.Erro is null && solto.Ignorados.Contains("wokwi-reator-nuclear") && solto.Circuito.Pecas.Count == 0, string.Join(", ", solto.Ignorados));
        }

        // ---- a aba guarda o que o diagrama nao tem: o sketch, os arquivos do cartao ----
        {
            var comSketch = new Circuito();
            comSketch.Pecas.Add(new PecaNoCircuito { Id = "esp1", Tipo = "esp32-devkit-v1", Sketch = "void setup() {}\nvoid loop() {}" });
            comSketch.Pecas.Add(new PecaNoCircuito { Id = "sd1", Tipo = "microsd-card", X = 300, Arquivos = new() { ["dados.txt"] = "42" } });
            string texto = ExportarWokwi.Gerar(comSketch).Replace("\"left\": 300", "\"left\": 350");
            var aplicada = ImportarWokwi.Ler(texto, comSketch, estrito: true).Circuito;
            Conferir("aba: mover pelo texto nao perde o sketch da placa nem os arquivos do cartao",
                aplicada.Achar("sd1") is { X: 350 } sd && sd.Arquivos?["dados.txt"] == "42"
                && aplicada.Achar("esp1")?.Sketch?.Contains("loop") == true, "sketch/cartao");
        }

        // ---- os tipos: aqui sem prefixo; os de la (pelo plugin do Wokwi) e os da Alpha 24 abrem ----
        {
            string[] nossos = ["esp32-s3-devkit", "esp32-c3-devkit", "pi-pico-w", "stm32-bluepill", "and-gate", "ssd1306-i2c", "a4988", "motor-dc"];
            string[] deles = ["board-esp32-s3-devkitc-1", "board-esp32-c3-devkitm-1", "board-pi-pico-w", "board-stm32-bluepill",
                              "wokwi-gate-and-2", "board-ssd1306", "wokwi-a4988", "circuitsketch-motor-dc"];
            var circ = new Circuito();
            foreach (var (t, i) in nossos.Select((t, i) => (t, i))) circ.Pecas.Add(new PecaNoCircuito { Id = $"p{i}", Tipo = t, X = 300 * i });
            string saiu = ExportarWokwi.Gerar(circ);
            Conferir("tipos: o diagrama sai com os nomes daqui, sem 'wokwi-', 'board-' nem 'circuitsketch-'",
                nossos.All(t => saiu.Contains($"\"type\": \"{t}\"")) && !saiu.Contains("wokwi-") && !saiu.Contains("board-")
                && !saiu.Contains("circuitsketch-"), saiu.Length > 400 ? saiu[..400] : saiu);
            Conferir("tipos: os nomes do Wokwi (e o circuitsketch- da Alpha 24) abrem como os daqui (a S3 nao vira a DevKit V1)",
                nossos.Zip(deles).All(z => FormatoWokwi.TipoDoWokwi(z.Second) == z.First && FormatoWokwi.TipoDoWokwi(z.First) == z.First),
                string.Join(", ", deles.Select(d => FormatoWokwi.TipoDoWokwi(d))));
            Conferir("pinos: o A4988 deles chama EN de ENABLE, e abre no EN",
                FormatoWokwi.PinoDoWokwi("a4988", "ENABLE") == "EN" && FormatoWokwi.PinoDoWokwi("a4988", "EN") == "EN", "a4988");
        }
    }
}
