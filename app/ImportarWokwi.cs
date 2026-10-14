using System.Globalization;
using System.Text.Json;

namespace Simulador.App;

/// <summary>
/// Le o diagram.json do Wokwi e monta um circuito nosso -- o de um projeto
/// do Wokwi, e o da aba "diagram.json" da IDE, que e o mesmo formato.
///
/// O formato deles:
///   parts:       [{ type: "wokwi-led", id: "led1", top, left, rotate, attrs:{...} }]
///   connections: [[ "led1:A", "esp:D2", "green", ["v0"] ]]
///
/// O que nao e obvio pelo exemplo:
///
///   1. As placas novas vem como "board-esp32-s3-devkitc-1", sem o prefixo
///      "wokwi-"; a DevKitC traz o pino so como numero ("4", "16"), e aqui
///      os pinos tem o nome da serigrafia ("D4"; o GPIO 16 e o RX2).
///   2. "$serialMonitor" nao e peca: e o monitor serial da tela deles.
///   3. O 4o campo da ligacao e o caminho do fio: "h192" anda 192 para a
///      direita a partir do pino de ORIGEM, ate o "*"; o que vem depois do
///      "*" sai do pino de DESTINO, de tras para a frente.
/// </summary>
public static class ImportarWokwi
{
    /// <param name="Ignorados">Tipos de peca sem equivalente aqui.</param>
    public record Resultado(Circuito Circuito, List<string> Ignorados)
    {
        /// <summary>Ligacoes que nao deu para reproduzir.</summary>
        public List<string> FiosPerdidos { get; init; } = [];

        /// <summary>No modo estrito (a aba da IDE): por que o diagrama foi recusado.</summary>
        public string Erro { get; init; }
    }

    /// <summary>
    /// Este texto e um diagram.json do Wokwi?
    ///
    /// Pergunta que aparece onde nao se espera: varios sketches que o
    /// professor mandou tem o diagrama INTEIRO colado no campo de descricao.
    /// </summary>
    public static bool PareceDiagrama(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;

        string limpo = texto.TrimStart();
        if (!limpo.StartsWith('{')) return false;
        if (!limpo.Contains("\"parts\"", StringComparison.Ordinal)) return false;

        try
        {
            using var doc = JsonDocument.Parse(limpo, Opcoes);
            return doc.RootElement.TryGetProperty("parts", out var partes)
                   && partes.ValueKind == JsonValueKind.Array
                   && partes.GetArrayLength() > 0;
        }
        catch (JsonException) { return false; }
    }

    static readonly JsonDocumentOptions Opcoes = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static Resultado Ler(string json) => Ler(json, null, estrito: false);

    /// <param name="atual">
    /// O circuito de agora, na aba da IDE: a peca com o mesmo id e tipo guarda
    /// o que o diagrama nao carrega (o sketch, os arquivos do cartao), e o fio
    /// que nao mudou guarda o jeito de desenhar.
    /// </param>
    /// <param name="estrito">
    /// Peca desconhecida ou id repetido recusam o diagrama inteiro (a aba nao
    /// pode apagar o circuito por causa de uma linha), em vez de ficar de fora.
    /// </param>
    public static Resultado Ler(string json, Circuito atual, bool estrito)
    {
        var circuito = new Circuito();
        var ignorados = new List<string>();
        var perdidos = new List<string>();
        var porId = new Dictionary<string, PecaNoCircuito>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json, Opcoes);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
            return estrito ? Recusa("o diagrama tem que ser um objeto { ... }") : new Resultado(circuito, []);

        circuito.Nome = Texto(raiz, "name");
        circuito.Autor = Texto(raiz, "author");

        // O resto do topo ("serialMonitor", "dependencies"...) vai junto como veio.
        foreach (var campo in raiz.EnumerateObject())
            if (campo.Name is not ("version" or "name" or "author" or "editor" or "parts" or "connections"))
                (circuito.ExtrasDoDiagrama ??= [])[campo.Name] = campo.Value.GetRawText();

        if (raiz.TryGetProperty("parts", out var partes) && partes.ValueKind == JsonValueKind.Array)
        {
            foreach (var parte in partes.EnumerateArray())
            {
                string tipoWokwi = Texto(parte, "type");
                string id = Texto(parte, "id");
                if (string.IsNullOrEmpty(tipoWokwi) || string.IsNullOrEmpty(id))
                {
                    if (estrito) return Recusa("toda peca precisa de \"type\" e \"id\"");
                    continue;
                }
                if (id.Contains(':'))
                {
                    if (estrito) return Recusa($"o id '{id}' tem ':', que separa a peca do pino nas ligacoes");
                    id = id.Replace(':', '_');
                }

                string tipo = FormatoWokwi.TipoDoWokwi(tipoWokwi);
                parte.TryGetProperty("attrs", out var attrs);

                // O LCD deles troca de conector por atributo: pins="i2c" sao
                // quatro vias (GND, VCC, SDA, SCL) em vez das dezesseis.
                if (tipo is "lcd1602" or "lcd2004" && Atributo(attrs, "pins") == "i2c") tipo += "-i2c";

                if (tipo is null || Catalogo.Obter(tipo) is null)
                {
                    if (estrito) return Recusa($"a peca '{tipoWokwi}' nao existe no catalogo");
                    ignorados.Add(tipoWokwi);
                    continue;
                }

                if (porId.ContainsKey(id))
                {
                    if (estrito) return Recusa($"o id '{id}' aparece em mais de uma peca");
                    int n = 2;
                    while (porId.ContainsKey($"{id}_{n}")) n++;
                    id = $"{id}_{n}";
                }

                var antiga = atual?.Achar(id);
                var peca = new PecaNoCircuito { Id = id, Tipo = tipo };
                if (antiga is not null && antiga.Tipo == tipo)
                {
                    // O que nao mora no diagrama (nem no deles): fica como estava.
                    peca.Codigo = antiga.Codigo;
                    peca.Arquivos = antiga.Arquivos;
                    peca.ArquivosDoComputador = antiga.ArquivosDoComputador;
                    peca.ArquivosGravados = antiga.ArquivosGravados;
                    peca.PastaDoComputador = antiga.PastaDoComputador;
                }
                peca.X = Numero(parte, "left");
                peca.Y = Numero(parte, "top");
                peca.Rotacao = Numero(parte, "rotate");
                AplicarAtributos(peca, attrs);

                circuito.Pecas.Add(peca);
                porId[id] = peca;
            }
        }

        if (raiz.TryGetProperty("connections", out var ligacoes) && ligacoes.ValueKind == JsonValueKind.Array)
        {
            foreach (var lig in ligacoes.EnumerateArray())
            {
                if (lig.ValueKind != JsonValueKind.Array || lig.GetArrayLength() < 2) continue;

                var itens = lig.EnumerateArray().ToArray();
                string refA = itens[0].ValueKind == JsonValueKind.String ? itens[0].GetString() : null;
                string refB = itens[1].ValueKind == JsonValueKind.String ? itens[1].GetString() : null;

                // O monitor serial da tela deles nao e peca nossa.
                if (EhMonitorSerial(refA) || EhMonitorSerial(refB)) continue;

                var de = Separar(refA, porId);
                var para = Separar(refB, porId);
                if (de is null || para is null)
                {
                    perdidos.Add($"{refA} -> {refB}");
                    continue;
                }

                string cor = itens.Length > 2 && itens[2].ValueKind == JsonValueKind.String ? itens[2].GetString() : "green";
                var fio = new Fio
                {
                    DePeca = de.Value.Peca.Id,
                    DePino = de.Value.Pino,
                    ParaPeca = para.Value.Peca.Id,
                    ParaPino = para.Value.Pino,
                    Cor = FormatoWokwi.CorDoFioDoWokwi(cor),
                };

                var caminho = itens.Length > 3 && itens[3].ValueKind == JsonValueKind.Array
                    ? itens[3].EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList()
                    : [];

                // O fio que nao mudou na aba continua o mesmo (com o jeito de
                // desenhar dele): o caminho escrito e o que ele mesmo daria.
                var igual = atual?.Fios.FirstOrDefault(f =>
                    f.DePeca == fio.DePeca && f.DePino == fio.DePino && f.ParaPeca == fio.ParaPeca && f.ParaPino == fio.ParaPino
                    && string.Equals(f.Cor, fio.Cor, StringComparison.OrdinalIgnoreCase)
                    && ExportarWokwi.Rota(atual, f) == Rota(caminho));
                if (igual is not null)
                {
                    var copia = new Fio
                    {
                        DePeca = igual.DePeca, DePino = igual.DePino, ParaPeca = igual.ParaPeca, ParaPino = igual.ParaPino,
                        Cor = igual.Cor, Estilo = igual.Estilo, CaminhoDoWokwi = igual.CaminhoDoWokwi,
                    };
                    copia.Dobras.AddRange(igual.Dobras.Select(d => new PontoFio { X = d.X, Y = d.Y }));
                    circuito.Fios.Add(copia);
                    continue;
                }

                AplicarCaminho(fio, caminho, de.Value.Peca, para.Value.Peca);
                circuito.Fios.Add(fio);
            }
        }

        return new Resultado(circuito, [.. ignorados.Distinct()]) { FiosPerdidos = perdidos };
    }

    static Resultado Recusa(string motivo) => new(null, []) { Erro = motivo };

    static string Rota(List<string> passos)
        => passos.Count == 0 ? "[]" : "[ " + string.Join(", ", passos.Select(FormatoWokwi.Aspas)) + " ]";

    // =====================================================================
    //  os atributos
    // =====================================================================

    /// <summary>
    /// O que o Wokwi conhece ("color", "value", "text") e o que so existe aqui
    /// (a ficha do motor, a bateria, a posicao do potenciometro...), com os
    /// nomes que o <see cref="ExportarWokwi"/> escreve. O que nao vier fica no
    /// padrao -- apagar a linha na aba desfaz a mudanca.
    /// </summary>
    static void AplicarAtributos(PecaNoCircuito p, JsonElement attrs)
    {
        string A(string chave) => Atributo(attrs, chave);
        double N(string chave, double padrao)
            => double.TryParse(A(chave), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : padrao;

        if (p.Tipo == "led" && A("color") is { } corDoLed) p.Cor = FormatoWokwi.CorDoLedDoWokwi(corDoLed);
        else if (A("cor") is { } cor) p.Cor = cor;
        if (p.Tipo == "resistor") p.Ohms = N("value", N("ohms", 220));
        else p.Ohms = N("ohms", 220);
        p.Nome = p.Tipo == "texto" ? A("text") : A("nome");

        p.Giro = N("giro", p.Tipo == "analog-joystick" ? 0.5 : 0);
        p.Giro2 = N("giro2", 0.5);
        p.Giro3 = N("giro3", 0.5);
        p.Giro4 = N("giro4", 0.5);
        p.Bits = (int)N("bits", 0);
        p.Volts = N("volts", Reguladores.VoltsDeFabrica(p.Tipo) ?? 9);
        p.MiliAmperes = N("corrente", 500);
        p.MiliAmperesHora = N("capacidade", 500);
        p.Modelo = A("modelo");
        p.Ficha = p.Modelo == "personalizado"
            ? new FichaPersonalizada
            {
                Volts = N("ficha-volts", 0), RpmOuKv = N("ficha-rpm-kv", 0), Resistencia = N("ficha-resistencia", 1),
                CorrenteVazio = N("ficha-corrente-vazio", 0), CorrenteMaxima = N("ficha-corrente-maxima", 0),
                Inercia = N("ficha-inercia", 1e-6), MedidaA = N("ficha-medida-a", 0), MedidaB = N("ficha-medida-b", 0),
                ParesDePolos = (int)N("ficha-polos", 7),
            }
            : null;
        p.Amperes = N("amperes", 0);
        p.Acabamento = A("acabamento");
        p.Ssid = A("ssid");
        p.Senha = A("senha");
        p.PlacaQueManda = A("placa-que-manda");
        p.JanelaUs = N("janela-us", 0);
        p.Espelhada = A("espelhada") is "true" or "1";
        p.Usb = A("usb") switch { "true" or "1" => true, "false" or "0" => false, _ => null };
        p.Teclas = A("teclas") is { Length: > 0 } teclas
            ? teclas.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(par => par.Split('=', 2))
                    .Where(par => par.Length == 2)
                    .GroupBy(par => par[0]).ToDictionary(g => g.Key, g => g.Last()[1])
            : null;

        // O que o simulador nao usa (o "volume" do buzzer, a "key" do botao)
        // fica guardado, e o arquivo sai de volta com ele.
        Dictionary<string, string> guardados = null;
        if (attrs.ValueKind == JsonValueKind.Object)
            foreach (var campo in attrs.EnumerateObject())
                if (!FormatoWokwi.AtributoConhecido(p.Tipo, campo.Name))
                    (guardados ??= [])[campo.Name] = Atributo(attrs, campo.Name) ?? campo.Value.GetRawText();
        p.AtributosDoWokwi = guardados;
    }

    /// <summary>Um atributo como texto (o Wokwi grava texto; a mao, as vezes vem numero).</summary>
    static string Atributo(JsonElement attrs, string chave)
    {
        if (attrs.ValueKind != JsonValueKind.Object || !attrs.TryGetProperty(chave, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    // =====================================================================
    //  os fios
    // =====================================================================

    /// <summary>
    /// O caminho do fio, do jeito deles: passos relativos. Os de antes do "*"
    /// saem do pino de origem; os de depois saem do pino de destino, lidos de
    /// tras para a frente ("v10","h5","*","v-15","h10" = 10 para baixo e 5
    /// para a direita da origem; 10 para a direita e 15 para cima do destino).
    /// Cada parada vira uma dobra nossa, com a posicao absoluta; passo de
    /// tamanho zero ("v0", o mais comum) nao vira dobra nenhuma.
    /// </summary>
    static void AplicarCaminho(Fio fio, List<string> caminho, PecaNoCircuito origem, PecaNoCircuito destino)
    {
        if (caminho.Count == 0) return;
        foreach (var p in Pontos(fio, caminho, origem, destino))
            fio.Dobras.Add(new PontoFio { X = p.X, Y = p.Y });

        // O caminho escrito vai junto: sem mexer no fio, ele sai de volta igual.
        fio.CaminhoDoWokwi = caminho;

        // Com dobra o tracado e de canto em canto; "solto" faria a curva
        // passar longe delas.
        if (fio.Dobras.Count > 0) fio.Estilo = Fio.EstiloReto;
    }

    /// <summary>Os cantos que um caminho do Wokwi da para este fio, com as pecas onde estao agora.</summary>
    internal static List<System.Windows.Point> Pontos(Fio fio, List<string> caminho, PecaNoCircuito origem, PecaNoCircuito destino)
    {
        var defOrigem = Catalogo.Obter(origem.Tipo);
        var defDestino = Catalogo.Obter(destino.Tipo);
        var pinoOrigem = defOrigem?.Pinos.FirstOrDefault(x => x.Nome == fio.DePino);
        var pinoDestino = defDestino?.Pinos.FirstOrDefault(x => x.Nome == fio.ParaPino);
        if (pinoOrigem is null) return [];

        int estrela = caminho.IndexOf("*");
        var antes = estrela < 0 ? caminho : caminho.Take(estrela).ToList();
        var depois = estrela < 0 ? [] : caminho.Skip(estrela + 1).Reverse().ToList();

        var daOrigem = Andar(Geometria.PosicaoPino(origem, defOrigem, pinoOrigem), antes);
        var doDestino = pinoDestino is null ? []
            : Andar(Geometria.PosicaoPino(destino, defDestino, pinoDestino), depois);
        doDestino.Reverse();
        return [.. daOrigem, .. doDestino];
    }

    static List<System.Windows.Point> Andar(System.Windows.Point de, List<string> passos)
    {
        var pontos = new List<System.Windows.Point>();
        var ponto = de;
        foreach (string passo in passos)
        {
            if (string.IsNullOrEmpty(passo)) continue;
            char eixo = passo[0];
            if (eixo != 'h' && eixo != 'v') continue;
            if (!double.TryParse(passo[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) continue;
            if (Math.Abs(d) < 0.01) continue;
            ponto = eixo == 'h' ? new System.Windows.Point(ponto.X + d, ponto.Y) : new System.Windows.Point(ponto.X, ponto.Y + d);
            pontos.Add(ponto);
        }
        return pontos;
    }

    /// <summary>"$serialMonitor" e afins: peca de mentira do editor deles.</summary>
    static bool EhMonitorSerial(string referencia) => referencia is not null && referencia.StartsWith('$');

    static (PecaNoCircuito Peca, string Pino)? Separar(string referencia, Dictionary<string, PecaNoCircuito> porId)
    {
        if (string.IsNullOrWhiteSpace(referencia)) return null;
        int corte = referencia.IndexOf(':');
        if (corte <= 0) return null;
        if (!porId.TryGetValue(referencia[..corte], out var peca)) return null;
        string pino = FormatoWokwi.PinoDoWokwi(peca.Tipo, referencia[(corte + 1)..]);
        return pino is null ? null : (peca, pino);
    }

    static string Texto(JsonElement e, string campo)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    static double Numero(JsonElement e, string campo)
        => e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
