using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simulador.App;

/// <summary>
/// Os projetos de teste da Alpha 24 (projetos/, de ferramentas/projetos.py):
/// cada um aberto na janela de verdade e conduzido pelo roteiro dele em
/// projetos/roteiros.json -- o manche, a distancia do sonar, o botao, a
/// senha -- conferindo o que a pessoa veria (o motor girando, o servo, o LED,
/// o LCD, o monitor).
///
/// O bloco so relata: um projeto que nao funciona e o que a Alpha 24 tem que
/// corrigir, nao uma regressao. Quando o roteiro diz "esperado": "funciona",
/// ele passa a contar como teste. Com CS_RESULTADOS apontando um arquivo, o
/// resultado sai em JSON (o ferramentas/testados.py separa os projetos em
/// TESTED\WORKING e TESTED\NOT WORKING com ele).
/// </summary>
public static partial class Autoteste
{
    static string AcharOsProjetos()
    {
        for (var pasta = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            string candidata = Path.Combine(pasta.FullName, "projetos");
            if (File.Exists(Path.Combine(candidata, "roteiros.json"))) return candidata;
        }
        return null;
    }

    static void Projetos_Testados()
    {
        saida.AppendLine("projetos de teste (Alpha 24): cada um rodando o seu roteiro");
        string pasta = AcharOsProjetos();
        if (pasta is null)
        {
            Conferir("projetos: a pasta projetos/ com o roteiros.json existe", false);
            return;
        }

        var roteiros = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, "roteiros.json")))!.AsObject();
        string so = Environment.GetEnvironmentVariable("CS_PROJETO");
        var resultados = new JsonObject();
        int funcionaram = 0, total = 0;

        foreach (var (nome, roteiro) in roteiros)
        {
            if (!string.IsNullOrEmpty(so) && !nome.Contains(so, StringComparison.OrdinalIgnoreCase)) continue;
            total++;
            var conferencias = new JsonArray();
            string erro = null;
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RodarRoteiro(Path.Combine(pasta, nome + ".sktch"), roteiro!["passos"]!.AsArray(), conferencias);
            }
            catch (Exception ex)
            {
                erro = $"{ex.GetType().Name}: {ex.Message}";
            }

            bool funcionou = erro is null && conferencias.Count > 0 && conferencias.All(c => c!["ok"]!.GetValue<bool>());
            if (funcionou) funcionaram++;
            resultados[nome] = new JsonObject
            {
                ["titulo"] = roteiro!["titulo"]?.GetValue<string>(),
                ["funcionou"] = funcionou,
                ["erro"] = erro,
                ["segundos"] = Math.Round(relogio.Elapsed.TotalSeconds, 1),
                ["conferencias"] = conferencias,
            };

            saida.AppendLine($"        {(funcionou ? "FUNCIONA    " : "NAO FUNCIONA")} {nome} ({relogio.Elapsed.TotalSeconds:0} s)");
            if (erro is not null) saida.AppendLine($"            nao rodou: {erro}");
            foreach (var c in conferencias.Where(c => !c!["ok"]!.GetValue<bool>()))
                saida.AppendLine($"            falhou: {c!["nome"]} -- {c["visto"]}");

            if (roteiro["esperado"]?.GetValue<string>() == "funciona")
                Conferir($"projeto {nome} continua funcionando", funcionou,
                    erro ?? string.Join(" | ", conferencias.Where(c => !c!["ok"]!.GetValue<bool>()).Select(c => $"{c!["nome"]}: {c["visto"]}")));
        }

        saida.AppendLine($"        {funcionaram} de {total} projetos funcionam");
        if (Environment.GetEnvironmentVariable("CS_RESULTADOS") is { Length: > 0 } destino)
        {
            var tudo = new JsonObject { ["data"] = DateTime.Now.ToString("s"), ["projetos"] = resultados };
            File.WriteAllText(destino, tudo.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }), new System.Text.UTF8Encoding(false));
        }
    }

    /// <summary>Abre o projeto, roda e faz cada passo do roteiro, anotando cada conferencia.</summary>
    static void RodarRoteiro(string arquivo, JsonArray passos, JsonArray conferencias)
    {
        var (circuito, codigo) = Sktch.Abrir(arquivo);
        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(circuito, codigo);
            janela.TesteRodar();
            var ed = janela.TesteEditorCircuito;

            foreach (var passo in passos.Select(p => p!.AsObject()))
            {
                if (passo["esperar"] is { } s) EsperarPor(() => false, (int)(s.GetValue<double>() * 1000));
                else if (passo["confere"] is { } c)
                {
                    double ate = passo["ate"]?.GetValue<double>() ?? 3;
                    string visto = "";
                    bool ok = EsperarPor(() => Conferencia(janela, c.AsObject(), out visto), (int)(ate * 1000));
                    if (ok) Conferencia(janela, c.AsObject(), out visto);
                    conferencias.Add(new JsonObject { ["nome"] = c["nome"]!.GetValue<string>(), ["ok"] = ok, ["visto"] = visto });
                }
                else if (passo["peca"] is { } idPeca)
                {
                    var peca = ed.Atual.Achar(idPeca.GetValue<string>())
                               ?? throw new InvalidOperationException($"o roteiro mexe na peca '{idPeca}', que nao existe");
                    if (passo["giro"] is { } g1) peca.Giro = g1.GetValue<double>();
                    if (passo["giro2"] is { } g2) peca.Giro2 = g2.GetValue<double>();
                    if (passo["giro3"] is { } g3) peca.Giro3 = g3.GetValue<double>();
                    if (passo["giro4"] is { } g4) peca.Giro4 = g4.GetValue<double>();
                    if (passo["bits"] is { } b) peca.Bits = b.GetValue<int>();
                    if (passo["volts"] is { } v) peca.Volts = v.GetValue<double>();
                    ed.AvisarQueMudou();
                }
                else if (passo["apertar"] is { } a) ed.BotoesPressionados.Add(a.GetValue<string>());
                else if (passo["soltar"] is { } so) ed.BotoesPressionados.Remove(so.GetValue<string>());
                else if (passo["tecla"] is { } t) ed.TeclaMatricialParaTeste(t.GetValue<string>(), passo["valor"]?.GetValue<string>());
                else if (passo["controle_ir"] is { } ir)
                {
                    ed.ApertarTeclaDoControle(ir.GetValue<string>(), passo["botao"]!.GetValue<string>());
                    EsperarPor(() => false, 400);
                    ed.Soltar();
                }
                else if (passo["celular_conectar"] is { } nomeBt) janela.TesteConectarCelular(nomeBt.GetValue<string>());
                else if (passo["celular_enviar"] is { } texto) janela.TesteEnviarPeloCelular(texto.GetValue<string>());
                else if (passo["serial"] is { } entrada)
                    janela.TesteMaquinaDa(passo["placa"]?.GetValue<string>() ?? "placa").EntradaSerial.Adicionar(entrada.GetValue<string>() + "\n");
                else throw new InvalidOperationException("passo desconhecido no roteiro: " + passo.ToJsonString());
            }
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Uma conferencia do roteiro: se ela vale agora, e o que se viu (para o relatorio).</summary>
    static bool Conferencia(MainWindow janela, JsonObject c, out string visto)
    {
        var ed = janela.TesteEditorCircuito;
        var maquina = janela.TesteMaquinaDa(c["placa"]?.GetValue<string>() ?? "placa");
        static string[] Lista(JsonNode n) => n is JsonArray a ? [.. a.Select(x => x!.GetValue<string>())] : [n!.GetValue<string>()];
        double Rpm(string id) => ed.RpmDoMotor(id);

        if (c["serial"] is { } serial)
        {
            var linhas = LinhasDoSerial(maquina);
            visto = "monitor: " + string.Join(" | ", linhas.TakeLast(4));
            return linhas.Any(l => l.Contains(serial.GetValue<string>()));
        }
        // A linha inteira (o "re" sozinho, e nao o "re" de dentro do "frente").
        if (c["linha"] is { } linha)
        {
            var linhas = LinhasDoSerial(maquina);
            visto = "monitor: " + string.Join(" | ", linhas.TakeLast(4));
            return linhas.Any(l => l.Trim() == linha.GetValue<string>());
        }
        if (c["energizada"] is { } energizada)
        {
            visto = $"energizada={maquina.Energizada} ({ed.SituacaoDaEnergia(c["placa"]?.GetValue<string>() ?? "placa")})";
            return maquina.Energizada == energizada.GetValue<bool>();
        }
        if (c["rpm_min"] is { } rpmMin)
        {
            var ids = Lista(rpmMin);
            double valor = c["valor"]!.GetValue<double>();
            visto = string.Join(", ", ids.Select(id => $"{id} {Rpm(id):0} rpm"));
            return ids.All(id => Math.Abs(Rpm(id)) >= valor);
        }
        if (c["rpm_max"] is { } rpmMax)
        {
            var ids = Lista(rpmMax);
            double valor = c["valor"]!.GetValue<double>();
            visto = string.Join(", ", ids.Select(id => $"{id} {Rpm(id):0} rpm"));
            return ids.All(id => Math.Abs(Rpm(id)) <= valor);
        }
        if (c["rpm_sinal"] is JsonArray sinais)
        {
            var itens = sinais.Select(x => (Id: x!["peca"]!.GetValue<string>(), Sinal: x["sinal"]!.GetValue<int>(), Min: x["min"]!.GetValue<double>())).ToList();
            visto = string.Join(", ", itens.Select(i => $"{i.Id} {Rpm(i.Id):0} rpm"));
            return itens.All(i => Rpm(i.Id) * i.Sinal >= i.Min);
        }
        if (c["rpm_maior"] is JsonArray pares)
        {
            var itens = pares.Select(p => (A: p![0]!.GetValue<string>(), B: p[1]!.GetValue<string>())).ToList();
            visto = string.Join(", ", itens.Select(i => $"{i.A} {Math.Abs(Rpm(i.A)):0} rpm vs {i.B} {Math.Abs(Rpm(i.B)):0} rpm"));
            return itens.All(i => Math.Abs(Rpm(i.A)) > Math.Abs(Rpm(i.B)) * 1.05);
        }
        if (c["servo"] is { } servo)
        {
            string id = servo["peca"]!.GetValue<string>();
            double angulo = ed.AnguloDoServoParaTeste(id);
            visto = $"{id} em {angulo:0} graus";
            return angulo >= servo["min"]!.GetValue<double>() && angulo <= servo["max"]!.GetValue<double>();
        }
        if (c["passo_min"] is { } passo)
        {
            string id = passo["peca"]!.GetValue<string>();
            double angulo = ed.AnguloDoPassoParaTeste(id);
            visto = $"{id} em {angulo:0} graus";
            return Math.Abs(angulo) >= passo["valor"]!.GetValue<double>();
        }
        if (c["led"] is { } led)
        {
            string id = led["peca"]!.GetValue<string>();
            double brilho = ed.BrilhoParaTeste(id);
            visto = $"{id} com brilho {brilho:0.00}";
            return (brilho > 0.05) == led["aceso"]!.GetValue<bool>();
        }
        if (c["brilho"] is { } b)
        {
            string id = b["peca"]!.GetValue<string>();
            double brilho = ed.BrilhoParaTeste(id);
            visto = $"{id} com brilho {brilho:0.00}";
            return (b["min"] is null || brilho >= b["min"]!.GetValue<double>()) && (b["max"] is null || brilho <= b["max"]!.GetValue<double>());
        }
        if (c["lcd"] is { } lcd)
        {
            var tela = maquina.Lcd;
            var linhas = tela is null ? [] : Enumerable.Range(0, tela.Linhas).Select(l => LinhaDoLcd(tela, l)).ToList();
            visto = tela is null ? "sem LCD na placa" : "LCD: " + string.Join(" / ", linhas);
            return linhas.Any(l => l.Contains(lcd.GetValue<string>()));
        }
        if (c["tela_min"] is { } telaMin)
        {
            string id = telaMin["peca"]!.GetValue<string>();
            int acesos = ed.AcesosNaTelaParaTeste(id);
            visto = $"{acesos} pontos acesos em {id}";
            return acesos >= telaMin["valor"]!.GetValue<int>();
        }
        if (c["empuxo_min"] is { } empuxo)
        {
            var ids = Lista(empuxo);
            visto = string.Join(", ", ids.Select(id => $"{id} {ed.EmpuxoParaTeste(id):0} gf"));
            return ids.All(id => ed.EmpuxoParaTeste(id) >= c["valor"]!.GetValue<double>());
        }
        if (c["sem_avisos"] is not null)
        {
            var avisos = ed.EstourosPelaEnergia().Select(e => $"{e.Peca}: {e.Motivo}").Concat(ed.Queimadas.Select(q => $"{q} queimou")).ToList();
            visto = avisos.Count == 0 ? "nenhum aviso" : string.Join(" / ", avisos);
            return avisos.Count == 0;
        }
        if (c["aviso"] is { } aviso)
        {
            var avisos = ed.EstourosPelaEnergia().Select(e => $"{e.Peca}: {e.Motivo}").ToList();
            visto = avisos.Count == 0 ? "nenhum aviso" : string.Join(" / ", avisos);
            return avisos.Any(a => a.Contains(aviso.GetValue<string>()));
        }
        if (c["tom"] is { } tom)
        {
            int[] tocando = [.. Enumerable.Range(0, Maquina.NumPinos).Where(p => maquina.Frequencia[p] > 0)];
            visto = tocando.Length == 0 ? "nenhum pino tocando" : string.Join(", ", tocando.Select(p => $"pino {p}: {maquina.Frequencia[p]} Hz"));
            return (tocando.Length > 0) == tom.GetValue<bool>();
        }
        if (c["pinos"] is JsonObject pinos)
        {
            var itens = pinos.Select(p => (Gpio: int.Parse(p.Key), Valor: p.Value!.GetValue<int>())).ToList();
            visto = string.Join(", ", itens.Select(i => $"{i.Gpio}={maquina.Digital[i.Gpio]}"));
            return itens.All(i => maquina.Digital[i.Gpio] == i.Valor);
        }
        if (c["fita_acesa"] is { } fita)
        {
            int gpio = fita["gpio"]!.GetValue<int>();
            int acesos = maquina.Fitas.TryGetValue(gpio, out var f) ? f.Cores.Count(cor => (cor & 0xFFFFFF) != 0) : 0;
            visto = $"{acesos} LEDs acesos na fita do pino {gpio}";
            return acesos >= fita["min"]!.GetValue<int>();
        }
        throw new InvalidOperationException("conferencia desconhecida no roteiro: " + c.ToJsonString());
    }
}
